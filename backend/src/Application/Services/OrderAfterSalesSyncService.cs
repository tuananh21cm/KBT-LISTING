using System.Globalization;
using System.Text.Json;
using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Pulls TikTok return / refund cases from 4Seller and reconciles them onto local orders.
/// </summary>
public class OrderAfterSalesSyncService : IOrderAfterSalesSyncService
{
    private static readonly JsonSerializerOptions WatermarkJson = new();

    /// <summary>Marker value stored once a one-shot maintenance pass has swept every seller.</summary>
    private const string MaintenanceDone = "done";

    private readonly IApplicationDbContext _db;
    private readonly IFourSellerApiService _fourSeller;
    private readonly ITelegramNotifier _telegram;
    private readonly ISettingProvider _settings;
    private readonly ILogger<OrderAfterSalesSyncService> _logger;

    /// <summary>Initializes the after-sales sync service.</summary>
    public OrderAfterSalesSyncService(
        IApplicationDbContext db,
        IFourSellerApiService fourSeller,
        ITelegramNotifier telegram,
        ISettingProvider settings,
        ILogger<OrderAfterSalesSyncService> logger)
    {
        _db = db;
        _fourSeller = fourSeller;
        _telegram = telegram;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrderAfterSalesSyncResultDto> TriggerPlatformPullAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OrderAfterSalesSyncResultDto();
        var owners = await LoadOwnersAsync(ownerUserId, result, cancellationToken);

        foreach (var owner in owners)
        {
            try
            {
                await _fourSeller.TriggerTikTokReturnSyncAsync(owner.Id, cancellationToken);
                result.SyncedSellers++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "After-sales trigger failed for seller {OwnerId}", owner.Id);
                result.FailedSellers.Add(new OrderSyncSellerFailureDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = owner.Name,
                    Message = ex.Message
                });
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OrderAfterSalesSyncResultDto> PullAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        // Order sync writes the same rows from another DbContext; overlapping the two lets the
        // second SaveChanges push back the values it read before the first one changed them.
        if (!await FourSellerSyncGate.TryEnterAsync(cancellationToken))
        {
            _logger.LogInformation("After-sales pull skipped — another 4Seller sync is running");
            return new OrderAfterSalesSyncResultDto { Skipped = true };
        }

        try
        {
            return await PullCoreAsync(ownerUserId, cancellationToken);
        }
        finally
        {
            FourSellerSyncGate.Exit();
        }
    }

    /// <inheritdoc />
    public async Task<OrderAfterSalesSyncResultDto> ReconcileAfterSalesFullScanAsync(
        CancellationToken cancellationToken = default)
    {
        var marker = await _settings.GetValueAsync(
            AfterSalesSyncSettingKeys.ReturnStatusBackfill, cancellationToken);
        if (string.Equals(marker?.Trim(), MaintenanceDone, StringComparison.OrdinalIgnoreCase))
        {
            return new OrderAfterSalesSyncResultDto { Skipped = true };
        }

        if (!await FourSellerSyncGate.TryEnterAsync(cancellationToken))
        {
            _logger.LogInformation("After-sales reconcile deferred — another 4Seller sync is running");
            return new OrderAfterSalesSyncResultDto { Skipped = true };
        }

        try
        {
            var result = await RepairCoreAsync(cancellationToken);

            // Mark done only after a clean sweep. A seller that failed still has orders that do not
            // match its returns list, and re-reading a seller already reconciled writes nothing
            // (every case verdict is idempotent), so retrying the whole pass costs only the reads.
            if (result.SyncedSellers > 0 && result.FailedSellers.Count == 0)
            {
                await _settings.SetValueAsync(
                    AfterSalesSyncSettingKeys.ReturnStatusBackfill,
                    MaintenanceDone,
                    cancellationToken);
            }

            return result;
        }
        finally
        {
            FourSellerSyncGate.Exit();
        }
    }

    private async Task<OrderAfterSalesSyncResultDto> RepairCoreAsync(CancellationToken cancellationToken)
    {
        var result = new OrderAfterSalesSyncResultDto();
        var owners = await LoadOwnersAsync(null, result, cancellationToken);
        if (owners.Count == 0)
        {
            // Nothing to read from, and nothing reconciled — stay unmarked so a seller who logs in
            // later still gets the pass.
            _logger.LogWarning("After-sales reconcile found no seller with a valid 4Seller cookie");
            return result;
        }

        foreach (var owner in owners)
        {
            try
            {
                var owned = await PullOwnerAsync(
                    owner,
                    new OwnerPassOptions(
                        // No watermark: the wrong clocks reach as far back as refunds exist, and the
                        // pull cursor sits at today so an incremental read would never see them.
                        Watermark: null,
                        SinglePage: false,
                        MaxPages: AfterSalesSyncSettingKeys.RepairMaxPagesPerOwner,
                        // Silent: these orders were already announced when they were first refunded.
                        Notify: false,
                        ForceRefundClock: true,
                        // Chỉ lượt này được dọn: nó đọc hết danh sách nên "không có case" mới là kết
                        // luận đọc được, khác hẳn pull incremental dừng ở watermark.
                        ReleaseRefundsWithoutCase: true),
                    cancellationToken);

                result.SyncedSellers++;
                result.ReturnsRead += owned.ReturnsRead;
                result.OrdersUpdated += owned.ChangedOrderIds.Count;
                result.Refunded += owned.Refunded;
                result.RefundReverted += owned.RefundReverted;
                result.RefundWithoutCaseReleased += owned.RefundWithoutCaseReleased;
                result.RefundWithoutCaseStuck += owned.RefundWithoutCaseStuck;
                result.CasesWithoutClock += owned.CasesWithoutClock;
                result.UnmatchedReturns += owned.UnmatchedReturns;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "After-sales reconcile failed for seller {OwnerId}", owner.Id);
                result.FailedSellers.Add(new OrderSyncSellerFailureDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = owner.Name,
                    Message = ex.Message
                });
            }
        }

        _logger.LogInformation(
            "After-sales reconcile: sellers={Sellers}, returns={Returns}, orders={Orders}, refunded={Refunded}, "
                + "reverted={Reverted}, withoutCaseReleased={Released}, withoutCaseStuck={Stuck}, "
                + "casesWithoutClock={NoClock}, unmatched={Unmatched}, failed={Failed}",
            result.SyncedSellers,
            result.ReturnsRead,
            result.OrdersUpdated,
            result.Refunded,
            result.RefundReverted,
            result.RefundWithoutCaseReleased,
            result.RefundWithoutCaseStuck,
            result.CasesWithoutClock,
            result.UnmatchedReturns,
            result.FailedSellers.Count);

        return result;
    }

    private async Task<OrderAfterSalesSyncResultDto> PullCoreAsync(
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var result = new OrderAfterSalesSyncResultDto();
        var owners = await LoadOwnersAsync(ownerUserId, result, cancellationToken);
        if (owners.Count == 0)
        {
            return result;
        }

        var watermarks = ReadWatermarks(
            await _settings.GetValueAsync(AfterSalesSyncSettingKeys.Watermark, cancellationToken));
        var watermarksChanged = false;
        var notices = new List<OrderAfterSalesNotice>();

        foreach (var owner in owners)
        {
            DateTime? watermark = watermarks.TryGetValue(owner.Id, out var stored) ? stored : null;
            // No cursor yet = every open case in the seller's history looks "new". Sync the state
            // but stay silent, otherwise enabling the feature dumps dozens of messages on ops.
            var isFirstRun = watermark is null;

            try
            {
                var owned = await PullOwnerAsync(
                    owner,
                    new OwnerPassOptions(
                        watermark,
                        SinglePage: isFirstRun,
                        MaxPages: AfterSalesSyncSettingKeys.MaxPagesPerOwner,
                        Notify: !isFirstRun,
                        ForceRefundClock: false,
                        // Pull dừng ở watermark nên nó chỉ thấy case vừa đổi — "đơn không có case"
                        // ở đây là kết luận sai, và dọn theo nó sẽ xoá sạch tab hoàn.
                        ReleaseRefundsWithoutCase: false),
                    cancellationToken);

                result.SyncedSellers++;
                result.ReturnsRead += owned.ReturnsRead;
                result.OrdersUpdated += owned.ChangedOrderIds.Count;
                result.Refunded += owned.Refunded;
                result.RefundReverted += owned.RefundReverted;
                result.CasesWithoutClock += owned.CasesWithoutClock;
                result.UnmatchedReturns += owned.UnmatchedReturns;
                notices.AddRange(owned.Notices);

                if (owned.MaxUpdateTime is { } maxUpdate
                    && (watermark is null || maxUpdate > watermark.Value))
                {
                    watermarks[owner.Id] = maxUpdate;
                    watermarksChanged = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Watermark stays put for this owner so the next run re-reads the same window.
                _logger.LogWarning(ex, "After-sales pull failed for seller {OwnerId}", owner.Id);
                result.FailedSellers.Add(new OrderSyncSellerFailureDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = owner.Name,
                    Message = ex.Message
                });
            }
        }

        if (watermarksChanged)
        {
            try
            {
                await _settings.SetValueAsync(
                    AfterSalesSyncSettingKeys.Watermark,
                    JsonSerializer.Serialize(
                        watermarks.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToString("O")),
                        WatermarkJson),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Losing the cursor only costs a repeated read next run; the orders are already
                // saved, and re-applying the same verdict produces no change and no notification.
                _logger.LogWarning(ex, "Failed to persist after-sales watermark");
            }
        }

        if (notices.Count > 0)
        {
            result.Notified = await _telegram.NotifyAfterSalesOrdersAsync(notices, cancellationToken);
        }

        return result;
    }

    private async Task<OwnerPullResult> PullOwnerAsync(
        OwnerRef owner,
        OwnerPassOptions options,
        CancellationToken cancellationToken)
    {
        var pass = await ReadReturnsAsync(owner.Id, options, cancellationToken);
        var records = pass.Records;
        var outcome = new OwnerPullResult { ReturnsRead = records.Count };
        if (records.Count == 0)
        {
            // Đọc về rỗng thì không kết luận được gì — kể cả bước dọn ở cuối, vì "không khớp case
            // nào" lúc này có thể chỉ là 4Seller trả thiếu.
            return outcome;
        }

        outcome.MaxUpdateTime = records.Select(r => r.PlatformUpdateTime).Max();

        // A buyer can open a case, cancel it, then open another one on the same order. Only the
        // newest case describes where the order stands now; older rows would fight over the status.
        var latestByOrder = records
            .GroupBy(r => r.ErpOrderId > 0 ? r.ErpOrderId.ToString() : NormalizeKey(r.PlatformOrderId))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => r.PlatformCreateTime ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .First());

        var erpIds = latestByOrder.Values
            .Where(r => r.ErpOrderId > 0)
            .Select(r => r.ErpOrderId)
            .Distinct()
            .ToList();
        var platformIds = latestByOrder.Values
            .Select(r => NormalizeKey(r.PlatformOrderId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        IQueryable<Order> orderQuery = _db.Orders
            .Where(o => o.OwnerId == owner.Id
                && (erpIds.Contains(o.FourSellerOrderId)
                    || (o.PlatformOrderId != null && platformIds.Contains(o.PlatformOrderId))));
        if (options.Notify)
        {
            // Items only feed the Telegram card (product title, photo). A silent pass would pull
            // item rows for every matched order and read none of them; the workflow-signal refresh
            // reloads what it needs on its own.
            orderQuery = orderQuery.Include(o => o.Items);
        }

        var orders = await orderQuery.ToListAsync(cancellationToken);

        var byErpId = orders
            .GroupBy(o => o.FourSellerOrderId)
            .ToDictionary(g => g.Key, g => g.First());
        var byPlatformId = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.PlatformOrderId))
            .GroupBy(o => o.PlatformOrderId!.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        // Only the Telegram notices need shop names, so a silent pass skips the extra query.
        var shopNames = options.Notify
            ? await LoadShopNamesAsync(latestByOrder.Values, cancellationToken)
            : [];
        var now = DateTime.UtcNow;
        var unmatched = new List<string>();
        // Đơn nào có case — kể cả case không làm đổi gì. Bước dọn ở cuối cần "có case hay không",
        // không phải "case có ghi gì không".
        var casedOrderIds = new HashSet<Guid>();

        foreach (var record in latestByOrder.Values)
        {
            var order = ResolveOrder(record, byErpId, byPlatformId);
            if (order is null)
            {
                outcome.UnmatchedReturns++;
                // The cursor still moves past this case, so a case that closes while its order is
                // missing locally (never synced, or deleted) is never revisited. Name the orders:
                // silently dropping a refund is exactly what this flow exists to prevent.
                if (unmatched.Count < 20)
                {
                    unmatched.Add(NormalizeKey(record.PlatformOrderId));
                }

                continue;
            }

            casedOrderIds.Add(order.Id);

            var before = Snapshot(order);
            var applied = Apply(order, record);
            var classified = TikTokReturnStatuses.Classify(record.PlatformReturnStatus);
            if (classified != AfterSalesOutcome.Closed && RefundClockOf(record) is null)
            {
                outcome.CasesWithoutClock++;
            }

            if (options.ForceRefundClock && classified != AfterSalesOutcome.Closed)
            {
                applied |= ForceRefundedAt(order, RefundClockOf(record));
            }

            if (!applied)
            {
                continue;
            }

            var after = Snapshot(order);
            if (after == before)
            {
                continue;
            }

            order.UpdatedAt = now;
            outcome.ChangedOrderIds.Add(order.Id);
            if (classified == AfterSalesOutcome.Closed)
            {
                if (PlatformOrderStatuses.IsRefund(before.Status)
                    && !PlatformOrderStatuses.IsRefund(after.Status))
                {
                    outcome.RefundReverted++;
                }
            }
            else if (PlatformOrderStatuses.IsRefund(after.Status))
            {
                outcome.Refunded++;
            }

            // Chỉ Telegram khi đổi status / mốc / lý do — đổi mỗi mình returnStatus (case tiến
            // thêm một bước) không được spam lại tin "đơn cần xử lý".
            if (!options.Notify || !IsNotifyWorthy(before, after))
            {
                continue;
            }

            shopNames.TryGetValue(
                (record.ShopId ?? 0).ToString(CultureInfo.InvariantCulture), out var shopName);
            outcome.Notices.Add(new OrderAfterSalesNotice
            {
                Order = order,
                ShopNameFallback = shopName,
                Outcome = classified,
                ReturnStatusText = record.ReturnStatus,
                ReturnReason = record.ReturnReason,
                ReturnType = record.ReturnType,
                RefundTotal = record.RefundTotal,
                Currency = record.Currency,
                ReturnTrackingNumber = record.ReturnTrackingNumber
            });
        }

        if (unmatched.Count > 0)
        {
            _logger.LogWarning(
                "After-sales pull for seller {OwnerId}: {Count} case(s) have no local order — {OrderIds}",
                owner.Id,
                outcome.UnmatchedReturns,
                string.Join(", ", unmatched));
        }

        if (options.ReleaseRefundsWithoutCase && pass.ReachedEndOfList)
        {
            await ReleaseRefundsWithoutCaseAsync(owner, casedOrderIds, outcome, now, cancellationToken);
        }
        else if (options.ReleaseRefundsWithoutCase)
        {
            // Trần trang chặn giữa danh sách: còn case chưa đọc tới, nên "không có case" chưa chắc.
            _logger.LogWarning(
                "Skipping refund-without-case sweep for seller {OwnerId}: the returns walk never reached the end of the list",
                owner.Id);
        }

        if (outcome.ChangedOrderIds.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return outcome;
    }

    /// <summary>
    /// Gỡ <c>REFUND</c> khỏi những đơn **không có case nào** trên <c>returns/tiktok/page</c>.
    ///
    /// Sự có mặt của case là nguồn sự thật cho <c>REFUND</c>: 4Seller liệt kê đơn ở danh sách hoàn
    /// từ lúc buyer mở yêu cầu, nên đơn không nằm trong danh sách đó thì không được ở tab hoàn. Số
    /// đơn kiểu này là di sản của bản đầu suy <c>REFUND</c> từ <c>cancelReason</c> của
    /// <c>/api/v2/order/page</c> — lý do huỷ có mặt trong lúc case còn mở rồi thôi gửi, nên trạng
    /// thái đặt theo nó không bao giờ gỡ được.
    ///
    /// Chỉ gọi từ lượt full scan (không watermark, đã đi hết danh sách). Trong pull incremental thì
    /// "không khớp case" chỉ nghĩa là case không đổi từ watermark tới nay — dọn ở đó sẽ xoá sạch
    /// tab hoàn ngay lượt đầu.
    ///
    /// Trạng thái thương mại dựng lại từ <c>RawPayload</c> — đúng giá trị sync đơn sẽ ghi nếu không
    /// bị latch (<see cref="PlatformOrderStatuses.ResolveCommerceStatus"/>). Không đọc ra thì log và
    /// để yên: đoán trạng thái của một đơn còn tệ hơn để nó lệch.
    /// </summary>
    private async Task ReleaseRefundsWithoutCaseAsync(
        OwnerRef owner,
        HashSet<Guid> casedOrderIds,
        OwnerPullResult outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var latched = await _db.Orders
            .Where(o => o.OwnerId == owner.Id
                && o.PlatformOrderStatus == PlatformOrderStatuses.Refund
                && !casedOrderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        foreach (var order in latched)
        {
            var restored = ReadRawCommerceStatus(order);
            if (restored is null)
            {
                outcome.RefundWithoutCaseStuck++;
                _logger.LogWarning(
                    "Order {OrderId} sits in REFUND with no return case on 4Seller, but its raw payload carries no commerce status to restore",
                    order.Id);
                continue;
            }

            order.PlatformOrderStatus = restored;
            if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.Refunded)
                is OrderFulfilTimeline.Source.Platform or OrderFulfilTimeline.Source.Legacy)
            {
                // Chỉ gỡ mốc luồng này (hoặc backfill cũ) đã stamp; mốc tay / SHEIN rank cao hơn.
                OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Refunded);
            }

            if (!PlatformOrderStatuses.IsCanceled(restored))
            {
                // Đơn huỷ thật thì `CancelReason` là lý do huỷ, giữ lại; còn lại là lý do hoàn đi
                // kèm cái latch vừa gỡ nên xoá cho sạch card.
                order.CancelReason = null;
            }

            // Case không còn trên danh sách hoàn → không còn dòng trạng thái return trên card.
            order.ReturnStatus = null;

            order.UpdatedAt = now;
            outcome.ChangedOrderIds.Add(order.Id);
            outcome.RefundWithoutCaseReleased++;
        }
    }

    /// <summary>
    /// Đọc <c>platformOrderStatus</c> từ ảnh JSON 4Seller lưu trên đơn. Trả null khi không có payload,
    /// payload không parse được, hoặc giá trị lại là <c>REFUND</c> (không dựng lại được gì từ nó).
    /// </summary>
    private string? ReadRawCommerceStatus(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.RawPayload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawPayload);
            if (!document.RootElement.TryGetProperty("platformOrderStatus", out var status)
                || status.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var normalized = PlatformOrderStatuses.Normalize(status.GetString());
            return PlatformOrderStatuses.IsRefund(normalized) ? null : normalized;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Order {OrderId} carries an unparseable 4Seller payload", order.Id);
            return null;
        }
    }

    /// <summary>
    /// Reads return pages newest-change-first and stops at the watermark.
    /// </summary>
    private async Task<ReturnsPass> ReadReturnsAsync(
        Guid ownerId,
        OwnerPassOptions options,
        CancellationToken cancellationToken)
    {
        var watermark = options.Watermark;
        var collected = new List<FourSellerReturnRecordDto>();
        var seenIds = new HashSet<long>();

        for (var page = 1; page <= options.MaxPages; page++)
        {
            var response = await _fourSeller.GetTikTokReturnPageAsync(
                ownerId, page, AfterSalesSyncSettingKeys.PageSize, cancellationToken);
            if (response.Records.Count == 0)
            {
                return new ReturnsPass(collected, ReachedEndOfList: true);
            }

            var reachedWatermark = false;
            foreach (var record in response.Records)
            {
                if (watermark is { } mark
                    && record.PlatformUpdateTime is { } updated
                    && updated <= mark)
                {
                    reachedWatermark = true;
                    break;
                }

                // 4Seller pages a live, re-sorted table: a case updated between two page requests
                // shifts rows and can hand back one we already have.
                if (seenIds.Add(record.Id))
                {
                    collected.Add(record);
                }
            }

            if (reachedWatermark || options.SinglePage)
            {
                // Stopped on purpose partway down the list — the cases below were never looked at.
                return new ReturnsPass(collected, ReachedEndOfList: false);
            }

            if (response.Records.Count < AfterSalesSyncSettingKeys.PageSize)
            {
                return new ReturnsPass(collected, ReachedEndOfList: true);
            }

            if (page == options.MaxPages)
            {
                // The cursor still jumps to the newest change read, so cases older than this block
                // are skipped for good. Reaching this means the loop has been down long enough to
                // fall behind by 1000 changed cases — worth a human looking at it.
                _logger.LogWarning(
                    "After-sales read for seller {OwnerId} hit the {MaxPages}-page cap; older cases were skipped",
                    ownerId,
                    options.MaxPages);
            }
        }

        return new ReturnsPass(collected, ReachedEndOfList: false);
    }

    /// <summary>
    /// Writes the after-sales verdict onto the order. Returns false when the case says nothing
    /// actionable about it.
    /// </summary>
    private bool Apply(Order order, FourSellerReturnRecordDto record)
    {
        var reason = PlatformOrderStatuses.NormalizeCancelReason(record.ReturnReason);
        var returnStatus = NormalizeReturnStatus(record.ReturnStatus);

        switch (TikTokReturnStatuses.Classify(record.PlatformReturnStatus))
        {
            case AfterSalesOutcome.Closed:
                if (!PlatformOrderStatuses.IsRefund(order.PlatformOrderStatus))
                {
                    // Case đã đóng mà local không còn REFUND — chỉ cần gỡ dòng returnStatus còn sót.
                    if (order.ReturnStatus is null)
                    {
                        return false;
                    }

                    order.ReturnStatus = null;
                    return true;
                }

                var restored = PlatformOrderStatuses.Normalize(record.PlatformOrderStatus);
                if (restored is null || PlatformOrderStatuses.IsRefund(restored))
                {
                    // Without a commerce status to fall back to, clearing REFUND would leave the
                    // order in a state no flow can describe. Order sync latches REFUND, so it
                    // cannot repair this either — surface it instead of guessing.
                    _logger.LogWarning(
                        "After-sales case {ReturnId} closed for order {OrderId} but carries no commerce status to restore",
                        record.ReturnId,
                        order.Id);
                    return false;
                }

                order.PlatformOrderStatus = restored;
                order.CancelReason = null;
                order.ReturnStatus = null;
                if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.Refunded)
                    is OrderFulfilTimeline.Source.Platform)
                {
                    // Only release the clock this flow stamped; a manual refund note stays.
                    OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Refunded);
                }

                return true;

            default:
                // Case còn mở (`AWAITING_BUYER_SHIP` / `BUYER_SHIPPED_ITEM`) và case đã hoàn xong
                // ghi giống nhau: **có mặt** trong danh sách returns nghĩa là đơn đang nằm trong
                // luồng hoàn của sàn, và 4Seller liệt kê nó ở đó ngay từ lúc buyer mở yêu cầu — tab
                // hoàn của ta phải khớp danh sách đó, không chờ tiền về buyer. `RefundedAt` vì thế
                // là **mốc mở case** (`platformCreateTime`), đúng thứ tự TikTok Shop / 4Seller sort.
                // `platformUpdateTime` là lần xử lý case gần nhất (dịch mỗi lượt sàn cập nhật) nên
                // chỉ làm con trỏ pull incremental, và chỉ đỡ khi sàn không trả mốc mở.
                //
                // Chỉ nhãn Telegram khác nhau (`AfterSalesOutcome` đi nguyên vào notice), nên case
                // mở báo "đơn cần xử lý hoàn/trả" chứ không báo "vừa bị refund".
                var refundedAt = RefundClockOf(record);
                if (refundedAt is null)
                {
                    // REFUND không mốc là trạng thái không ai đọc được: card hiện "Refund —" và tab
                    // hoàn (sort theo `RefundedAt`) đẩy đơn xuống đuôi null. Thà giữ status thương
                    // mại còn đọc được, chỉ ghi lý do + trạng thái case để ops vẫn thấy đơn cần xử lý.
                    _logger.LogWarning(
                        "After-sales case {ReturnId} for order {OrderId} carries no platform clock; leaving the commerce status alone",
                        record.ReturnId,
                        order.Id);
                    return WriteReasonAndReturnStatus(order, reason, returnStatus);
                }

                order.PlatformOrderStatus = PlatformOrderStatuses.Refund;
                order.CancelReason = reason ?? order.CancelReason;
                order.ReturnStatus = returnStatus ?? order.ReturnStatus;
                // Cùng rank chỉ nhận clock sớm hơn, nên case mới mở trên đơn đã hoàn trước đó không
                // đè mất mốc hoàn thật.
                OrderFulfilTimeline.Stamp(
                    order,
                    OrderFulfilTimeline.Milestone.Refunded,
                    refundedAt,
                    OrderFulfilTimeline.Source.Platform);
                return true;
        }
    }

    /// <summary>
    /// Ghi lý do + trạng thái case khi không có quyền đổi commerce status / mốc hoàn.
    /// </summary>
    private static bool WriteReasonAndReturnStatus(Order order, string? reason, string? returnStatus)
    {
        var changed = false;
        if (reason is not null && !string.Equals(order.CancelReason, reason, StringComparison.Ordinal))
        {
            order.CancelReason = reason;
            changed = true;
        }

        if (returnStatus is not null
            && !string.Equals(order.ReturnStatus, returnStatus, StringComparison.Ordinal))
        {
            order.ReturnStatus = returnStatus;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Human-readable <c>returnStatus</c> from 4Seller — trim + cap so the OrderCard line stays short.
    /// </summary>
    private static string? NormalizeReturnStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    /// <summary>
    /// Clock hoàn của một case: lúc mở yêu cầu, lùi về lần cập nhật gần nhất khi sàn không trả mốc
    /// mở. Cả hai trống thì case không nói được đơn hoàn lúc nào.
    /// </summary>
    private static DateTime? RefundClockOf(FourSellerReturnRecordDto record)
        => record.PlatformCreateTime ?? record.PlatformUpdateTime;

    /// <summary>
    /// Pins the refund clock to <paramref name="refundClock"/>, overwriting the value this flow
    /// stamped before. Returns false when nothing needed changing.
    ///
    /// <see cref="OrderFulfilTimeline.Stamp"/> keeps the earlier clock at equal rank, which is what
    /// normally lets a create time replace a stored update time. It cannot help the other way round
    /// (a case whose create time is later than whatever was stamped), so the stamp is dropped first
    /// and the create time written outright. A Manual or SHEIN clock outranks this flow: a human
    /// refund note or a SHEIN supplier refund is not ours to correct.
    /// </summary>
    private static bool ForceRefundedAt(Order order, DateTime? refundClock)
    {
        if (refundClock is null || order.RefundedAt == refundClock)
        {
            return false;
        }

        if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.Refunded)
            is OrderFulfilTimeline.Source.Manual or OrderFulfilTimeline.Source.Shein)
        {
            return false;
        }

        OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Refunded);
        OrderFulfilTimeline.Stamp(
            order,
            OrderFulfilTimeline.Milestone.Refunded,
            refundClock,
            OrderFulfilTimeline.Source.Platform);
        return true;
    }

    private static Order? ResolveOrder(
        FourSellerReturnRecordDto record,
        IReadOnlyDictionary<long, Order> byErpId,
        IReadOnlyDictionary<string, Order> byPlatformId)
    {
        if (record.ErpOrderId > 0 && byErpId.TryGetValue(record.ErpOrderId, out var byErp))
        {
            return byErp;
        }

        var platformId = NormalizeKey(record.PlatformOrderId);
        return !string.IsNullOrEmpty(platformId) && byPlatformId.TryGetValue(platformId, out var byPlatform)
            ? byPlatform
            : null;
    }

    /// <summary>
    /// Shop display names keyed by 4Seller shop id — only used as a Telegram fallback when the
    /// order itself has no matched local shop.
    /// </summary>
    private async Task<Dictionary<string, string?>> LoadShopNamesAsync(
        IEnumerable<FourSellerReturnRecordDto> records,
        CancellationToken cancellationToken)
    {
        var shopIds = records
            .Where(r => r.ShopId is > 0)
            .Select(r => r.ShopId!.Value.ToString(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        if (shopIds.Count == 0)
        {
            return [];
        }

        var shops = await _db.Shops
            .AsNoTracking()
            .Where(s => s.FourSellerShopId != null && shopIds.Contains(s.FourSellerShopId))
            .Select(s => new { s.FourSellerShopId, s.Name })
            .ToListAsync(cancellationToken);

        return shops
            .GroupBy(s => s.FourSellerShopId!)
            .ToDictionary(g => g.Key, g => (string?)g.First().Name);
    }

    private async Task<List<OwnerRef>> LoadOwnersAsync(
        Guid? ownerUserId,
        OrderAfterSalesSyncResultDto result,
        CancellationToken cancellationToken)
    {
        var query = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn);

        if (ownerUserId.HasValue)
        {
            query = query.Where(u => u.Id == ownerUserId.Value);
        }

        var owners = await query
            .Select(u => new OwnerRef(u.Id, u.FullName ?? u.UserName))
            .ToListAsync(cancellationToken);

        if (ownerUserId.HasValue && owners.Count == 0)
        {
            result.FailedSellers.Add(new OrderSyncSellerFailureDto
            {
                OwnerUserId = ownerUserId.Value,
                Message = "Seller chưa có cookie 4Seller hợp lệ. Chạy Check login trước khi đồng bộ."
            });
        }

        return owners;
    }

    private Dictionary<Guid, DateTime> ReadWatermarks(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw, WatermarkJson);
            if (parsed is null)
            {
                return [];
            }

            var map = new Dictionary<Guid, DateTime>();
            foreach (var (key, value) in parsed)
            {
                if (Guid.TryParse(key, out var ownerId)
                    && DateTime.TryParse(
                        value,
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var at))
                {
                    map[ownerId] = DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc);
                }
            }

            return map;
        }
        catch (JsonException ex)
        {
            // A corrupt cursor must not stall the sync; losing it only costs one silent catch-up run.
            _logger.LogWarning(ex, "After-sales watermark is not valid JSON, restarting from scratch");
            return [];
        }
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static OrderAfterSalesState Snapshot(Order order) =>
        new(order.PlatformOrderStatus, order.RefundedAt, order.CancelReason, order.ReturnStatus);

    /// <summary>
    /// Telegram only when the ops-facing outcome moved — not when the human-readable return status
    /// alone advanced (buyer shipped the item, seller issued the refund, …).
    /// </summary>
    private static bool IsNotifyWorthy(OrderAfterSalesState before, OrderAfterSalesState after) =>
        !string.Equals(before.Status, after.Status, StringComparison.Ordinal)
        || before.RefundedAt != after.RefundedAt
        || !string.Equals(before.CancelReason, after.CancelReason, StringComparison.Ordinal);

    /// <summary>The columns this flow owns — the change test for persistence and notifications.</summary>
    private readonly record struct OrderAfterSalesState(
        string? Status,
        DateTime? RefundedAt,
        string? CancelReason,
        string? ReturnStatus);

    private readonly record struct OwnerRef(Guid Id, string? Name);

    /// <summary>
    /// What one owner pass should do. The incremental pull and the one-shot
    /// <see cref="ReconcileAfterSalesFullScanAsync"/> read the same pages and reconcile the same
    /// columns; only the reading window, the notifications and the refund-clock overwrite differ,
    /// so they share one code path rather than drifting apart as two copies.
    /// </summary>
    /// <param name="Watermark">Stop once a case has not changed since this clock; null = read everything.</param>
    /// <param name="SinglePage">Stop after page one (a first pull only needs current state).</param>
    /// <param name="MaxPages">Runaway guard on the page walk.</param>
    /// <param name="Notify">Queue Telegram notices for the orders that changed.</param>
    /// <param name="ForceRefundClock">Pin <c>RefundedAt</c> to the case's create time, overwriting a stored clock.</param>
    /// <param name="ReleaseRefundsWithoutCase">
    /// Strip <c>REFUND</c> off orders no case mentions. Only safe once the walk has seen the whole
    /// list — see <see cref="ReleaseRefundsWithoutCaseAsync"/>.
    /// </param>
    private readonly record struct OwnerPassOptions(
        DateTime? Watermark,
        bool SinglePage,
        int MaxPages,
        bool Notify,
        bool ForceRefundClock,
        bool ReleaseRefundsWithoutCase);

    /// <summary>
    /// One owner's return pages plus whether the walk ran out of rows on its own.
    /// <paramref name="ReachedEndOfList"/> false means it stopped early (watermark, single page, or
    /// the page cap), so "no case mentions this order" cannot be concluded from it.
    /// </summary>
    private readonly record struct ReturnsPass(
        List<FourSellerReturnRecordDto> Records,
        bool ReachedEndOfList);

    private sealed class OwnerPullResult
    {
        public int ReturnsRead { get; init; }

        public DateTime? MaxUpdateTime { get; set; }

        public int Refunded { get; set; }

        public int RefundReverted { get; set; }

        public int RefundWithoutCaseReleased { get; set; }

        public int RefundWithoutCaseStuck { get; set; }

        public int CasesWithoutClock { get; set; }

        public int UnmatchedReturns { get; set; }

        /// <summary>Orders touched this run, deduped: one order may carry more than one case key.</summary>
        public HashSet<Guid> ChangedOrderIds { get; } = [];

        public List<OrderAfterSalesNotice> Notices { get; } = [];
    }
}
