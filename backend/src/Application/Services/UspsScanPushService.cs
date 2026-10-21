using Application.DTOs.Order;
using Application.Interfaces;
using Application.Options;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Orchestrates PrintPackShip USPS track-scan: filter → create → persist Draft → send → stamp.
/// </summary>
public sealed class UspsScanPushService : IUspsScanPushService
{
    private const decimal DefaultBuyTrackFee = 0.5m;
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _db;
    private readonly IPrintPackShipScanClient _client;
    private readonly IUspsScanPushGate _gate;
    private readonly ISettingProvider _settings;
    private readonly PrintPackShipOptions _options;
    private readonly ILogger<UspsScanPushService> _logger;

    /// <summary>Creates the service.</summary>
    public UspsScanPushService(
        IApplicationDbContext db,
        IPrintPackShipScanClient client,
        IUspsScanPushGate gate,
        ISettingProvider settings,
        IOptions<PrintPackShipOptions> options,
        ILogger<UspsScanPushService> logger)
    {
        _db = db;
        _client = client;
        _gate = gate;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UspsScanPushResultDto> PushAsync(
        IReadOnlyList<Guid> orderIds,
        string? notes,
        Guid actingUserId,
        Guid? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một đơn để đẩy track scan.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await PushCoreAsync(orderIds, notes, actingUserId, ownerUserId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UspsScanPushResultDto> PushCoreAsync(
        IReadOnlyList<Guid> orderIds,
        string? notes,
        Guid actingUserId,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var distinctIds = orderIds.Distinct().ToList();
        var max = _options.MaxTrackingPerBatch > 0 ? _options.MaxTrackingPerBatch : 100;

        var orders = await _db.Orders
            .Include(o => o.SupplierPurchases)
            .Where(o => distinctIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var byId = orders.ToDictionary(o => o.Id);
        var skipped = new List<UspsScanSkippedOrderDto>();
        var eligible = new List<(Order Order, string TrackingNo)>();
        var seenTracking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in distinctIds)
        {
            if (!byId.TryGetValue(id, out var order))
            {
                skipped.Add(new UspsScanSkippedOrderDto
                {
                    OrderId = id,
                    Reason = ownerUserId.HasValue
                        ? UspsScanSkipReasons.NotOwned
                        : UspsScanSkipReasons.NotFound,
                });
                continue;
            }

            if (ownerUserId.HasValue && order.OwnerId != ownerUserId.Value)
            {
                skipped.Add(new UspsScanSkippedOrderDto
                {
                    OrderId = id,
                    PlatformOrderId = order.PlatformOrderId,
                    Reason = UspsScanSkipReasons.NotOwned,
                });
                continue;
            }

            if (order.UspsScanBatchId is not null)
            {
                skipped.Add(Skip(order, null, UspsScanSkipReasons.AlreadyPushed));
                continue;
            }

            if (!OrderFulfilHandlingSignals.HasUspsSignal(order))
            {
                skipped.Add(Skip(order, null, UspsScanSkipReasons.NotUsps));
                continue;
            }

            var tracking = ResolveCarrierTracking(order);
            if (string.IsNullOrWhiteSpace(tracking))
            {
                skipped.Add(Skip(order, null, UspsScanSkipReasons.NoTracking));
                continue;
            }

            if (OrderFulfilHandlingSignals.IsTerminalCommerce(order))
            {
                skipped.Add(Skip(order, tracking, UspsScanSkipReasons.TerminalCommerce));
                continue;
            }

            if (OrderFulfilHandlingSignals.IsAlreadyInTransitOrDelivered(order))
            {
                skipped.Add(Skip(order, tracking, UspsScanSkipReasons.AlreadyInTransit));
                continue;
            }

            if (!seenTracking.Add(tracking))
            {
                skipped.Add(Skip(order, tracking, UspsScanSkipReasons.DuplicateTracking));
                continue;
            }

            eligible.Add((order, tracking));
        }

        if (eligible.Count > 0)
        {
            var trackingList = eligible.Select(e => e.TrackingNo).ToList();
            var pendingTracks = await (
                    from i in _db.UspsScanBatchItems.AsNoTracking()
                    join b in _db.UspsScanBatches.AsNoTracking() on i.BatchId equals b.Id
                    where trackingList.Contains(i.TrackingNo)
                        && b.Status == UspsScanBatch.StatusSendUnknown
                    select i.TrackingNo)
                .ToListAsync(cancellationToken);

            if (pendingTracks.Count > 0)
            {
                var pendingSet = new HashSet<string>(pendingTracks, StringComparer.OrdinalIgnoreCase);
                var kept = new List<(Order Order, string TrackingNo)>();
                foreach (var row in eligible)
                {
                    if (pendingSet.Contains(row.TrackingNo))
                    {
                        skipped.Add(Skip(row.Order, row.TrackingNo, UspsScanSkipReasons.PendingBatch));
                    }
                    else
                    {
                        kept.Add(row);
                    }
                }

                eligible = kept;
            }
        }

        if (eligible.Count == 0)
        {
            return new UspsScanPushResultDto
            {
                Status = string.Empty,
                SentCount = 0,
                Skipped = skipped,
            };
        }

        if (eligible.Count > max)
        {
            throw new BusinessException(
                $"Mỗi lần đẩy tối đa {max} mã vận chuyển (đang chọn {eligible.Count}). Giảm số đơn rồi thử lại.");
        }

        var trackingNumbers = eligible.Select(e => e.TrackingNo).ToList();
        var create = await _client.CreateTrackingBatchAsync(
            trackingNumbers,
            notes,
            cancellationToken);

        if (create.Kind != PrintPackShipCallKind.Ok
            || !create.Success
            || create.BatchId is null)
        {
            throw new BusinessException(
                create.Error
                ?? create.Message
                ?? "Không tạo được batch PrintPackShip.");
        }

        var externalBatchId = create.BatchId.Value;
        var batch = new UspsScanBatch
        {
            ExternalBatchId = externalBatchId,
            Status = UspsScanBatch.StatusDraft,
            TrackingCount = trackingNumbers.Count,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = actingUserId,
            RawCreateResponse = Truncate(create.RawJson, 32_000),
            Items = eligible.Select(e => new UspsScanBatchItem
            {
                OrderId = e.Order.Id,
                TrackingNo = e.TrackingNo,
            }).ToList(),
        };

        _db.UspsScanBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        var send = await _client.SendBatchAsync(externalBatchId, cancellationToken);

        if (send.Kind == PrintPackShipCallKind.Unknown)
        {
            // Ambiguous charge — block re-push via SendUnknown.
            batch.Status = UspsScanBatch.StatusSendUnknown;
            batch.ErrorMessage = Truncate(send.Error ?? send.Message ?? "Kết quả gửi batch không rõ.", 2000);
            batch.RawSendResponse = Truncate(send.RawJson, 32_000);
            batch.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "PrintPackShip send batch {BatchId} outcome unknown: {Error}",
                externalBatchId,
                batch.ErrorMessage);

            return new UspsScanPushResultDto
            {
                BatchId = externalBatchId,
                Status = UspsScanBatch.StatusSendUnknown,
                SentCount = 0,
                Skipped = skipped,
                Error = batch.ErrorMessage
                    + " Đơn chưa được đánh dấu — không đẩy lại các mã này cho đến khi admin kiểm tra batch trên PrintPackShip.",
            };
        }

        if (send.Kind == PrintPackShipCallKind.Failed || !send.Success)
        {
            batch.Status = UspsScanBatch.StatusSendFailed;
            batch.ErrorMessage = Truncate(send.Error ?? send.Message ?? "Gửi batch thất bại.", 2000);
            batch.RawSendResponse = Truncate(send.RawJson, 32_000);
            batch.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return new UspsScanPushResultDto
            {
                BatchId = externalBatchId,
                Status = UspsScanBatch.StatusSendFailed,
                SentCount = 0,
                Skipped = skipped,
                Error = batch.ErrorMessage,
            };
        }

        // Success path — stamp orders + batch in one transaction when supported.
        var pricePerLabel = send.PricePerLabel
            ?? await ReadBuyTrackFallbackAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var pagesFailed = send.PagesFailed ?? 0;

        await using var tx = await BeginTransactionAsync(cancellationToken);

        batch.Status = UspsScanBatch.StatusSent;
        batch.SentAt = now;
        batch.PricePerLabel = pricePerLabel;
        batch.ScanFee = send.ScanFee;
        batch.PaidPages = send.PaidPages;
        batch.PagesTotal = send.PagesTotal;
        batch.PagesSuccess = send.PagesSuccess;
        batch.PagesFailed = send.PagesFailed;
        batch.BalancePrevious = send.BalancePrevious;
        batch.BalanceCurrent = send.BalanceCurrent;
        batch.RawSendResponse = Truncate(send.RawJson, 32_000);
        batch.UpdatedAt = now;
        if (pagesFailed > 0)
        {
            batch.ErrorMessage = Truncate(
                $"Gửi thành công nhưng {pagesFailed}/{send.PagesTotal ?? pagesFailed} trang lỗi.",
                2000);
        }

        foreach (var (order, _) in eligible)
        {
            order.UspsProcessedAt ??= now;
            order.UspsScanSentAt = now;
            order.UspsScanBatchId = externalBatchId;
            order.BuyTrackAmount = pricePerLabel;
            order.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        decimal? totalCharged = null;
        if (send.ScanFee is not null)
        {
            totalCharged = send.ScanFee;
        }
        else if (send.PaidPages is int paid && paid > 0)
        {
            totalCharged = pricePerLabel * paid;
        }

        return new UspsScanPushResultDto
        {
            BatchId = externalBatchId,
            Status = UspsScanBatch.StatusSent,
            SentCount = eligible.Count,
            PricePerLabel = pricePerLabel,
            ScanFee = send.ScanFee,
            TotalCharged = totalCharged,
            BalanceCurrent = send.BalanceCurrent,
            PagesFailed = pagesFailed,
            Skipped = skipped,
            Error = batch.ErrorMessage,
        };
    }

    /// <inheritdoc />
    public async Task<UspsScanBatchListPageDto> ListBatchesAsync(
        UspsScanBatchListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var size = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, MaxPageSize);

        var q = _db.UspsScanBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .Include(b => b.Items)
            .ThenInclude(i => i.Order)
            .OrderByDescending(b => b.CreatedAt);

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new UspsScanBatchListPageDto
        {
            Total = total,
            PageCurrent = page,
            PageSize = size,
            Items = rows.Select(b => new UspsScanBatchDto
            {
                Id = b.Id,
                ExternalBatchId = b.ExternalBatchId,
                Status = b.Status,
                TrackingCount = b.TrackingCount,
                PricePerLabel = b.PricePerLabel,
                ScanFee = b.ScanFee,
                PaidPages = b.PaidPages,
                PagesFailed = b.PagesFailed,
                BalanceCurrent = b.BalanceCurrent,
                CreatedByUserId = b.CreatedByUserId,
                CreatedByName = string.IsNullOrWhiteSpace(b.CreatedByUser?.FullName)
                    ? b.CreatedByUser?.UserName
                    : b.CreatedByUser?.FullName,
                CreatedAt = b.CreatedAt,
                SentAt = b.SentAt,
                ErrorMessage = b.ErrorMessage,
                Items = b.Items.Select(i => new UspsScanBatchItemDto
                {
                    OrderId = i.OrderId,
                    PlatformOrderId = i.Order?.PlatformOrderId,
                    TrackingNo = i.TrackingNo,
                }).ToList(),
            }).ToList(),
        };
    }

    private async Task<decimal> ReadBuyTrackFallbackAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(OrderHandlingSettingKeys.BuyTrack, cancellationToken);
        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= 0m)
        {
            return value;
        }

        return DefaultBuyTrackFee;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (_db is not DbContext ef)
        {
            return null;
        }

        var provider = ef.Database.ProviderName ?? "";
        if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ef.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Prefer published Order.TrackingNo; else first carrier SupplierPurchase tracking.
    /// Mirrors frontend resolveOrderTracking priority for USPS list.
    /// </summary>
    internal static string? ResolveCarrierTracking(Order order)
    {
        var published = order.TrackingNo?.Trim();
        if (!string.IsNullOrEmpty(published))
        {
            return published;
        }

        var carrier = order.SupplierPurchases?
            .Where(p => p.TrackingKind == SupplierTrackingKinds.Carrier
                && !string.IsNullOrWhiteSpace(p.TrackingNo))
            .Select(p => p.TrackingNo!.Trim())
            .FirstOrDefault();

        return string.IsNullOrEmpty(carrier) ? null : carrier;
    }

    private static UspsScanSkippedOrderDto Skip(Order order, string? tracking, string reason) =>
        new()
        {
            OrderId = order.Id,
            PlatformOrderId = order.PlatformOrderId,
            TrackingNo = tracking,
            Reason = reason,
        };

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max];
    }
}
