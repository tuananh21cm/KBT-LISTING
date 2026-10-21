using System.Text.Json.Nodes;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services.Shein;

/// <summary>
/// Drives SHEIN order sync from the server using sessions the extension pushed onto fulfil
/// accounts (<c>header_source = local</c>). Mirrors extension <c>SHEIN_SYNC</c>: list →
/// <see cref="IOrderSyncService.SyncFromSheinAsync"/> → detail <c>get_order_info</c>.
/// </summary>
public class SheinRealtimeSyncService : ISheinRealtimeSyncService
{
    /// <summary>Orders requested per list page; matches what the storefront itself asks for.</summary>
    private const int ListPageSize = 50;

    /// <summary>Safety ceiling for list pages (~2000 orders); lookback cuts earlier.</summary>
    private const int MaxPagesNew = 40;

    /// <summary>Pause between list pages. Zero — gọi trang kế ngay, không chờ.</summary>
    private static readonly TimeSpan PageDelay = TimeSpan.Zero;

    /// <summary>Ceiling on detail fetches per account per pass, so one pass always terminates.</summary>
    private const int MaxDetailPerAccount = 200;

    /// <summary>Orders per <c>SyncFromSheinAsync</c> call, matching the extension's batching.</summary>
    private const int ListBatchSize = 40;
    private const int DetailBatchSize = 25;

    private readonly IApplicationDbContext _db;
    private readonly IOrderSyncService _orderSync;
    private readonly ISheinOrderClient _client;
    private readonly ILogger<SheinRealtimeSyncService> _logger;

    /// <summary>When the next pass becomes due (UTC).</summary>
    private static DateTime _nextDueUtc = DateTime.MinValue;

    /// <summary>Initializes the realtime sync service.</summary>
    public SheinRealtimeSyncService(
        IApplicationDbContext db,
        IOrderSyncService orderSync,
        ISheinOrderClient client,
        ILogger<SheinRealtimeSyncService> logger)
    {
        _db = db;
        _orderSync = orderSync;
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var config = await _orderSync.GetSheinSyncConfigAsync(cancellationToken);
        // Local = backend gọi SHEIN bằng session đã lưu (cookie + proxy).
        if (!config.Enabled ||
            !string.Equals(config.HeaderSource, ExtensionSheinSyncSettingKeys.HeaderSourceLocal, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTime.UtcNow < _nextDueUtc)
        {
            return null;
        }

        // Fetch/list ngoài lock — mỗi lô SyncFromSheinAsync tự khóa theo billno.
        _nextDueUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, config.IntervalSeconds));
        return await RunAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> RunAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var config = await _orderSync.GetSheinSyncConfigAsync(cancellationToken);
        if (!string.Equals(config.HeaderSource, ExtensionSheinSyncSettingKeys.HeaderSourceLocal, StringComparison.OrdinalIgnoreCase))
        {
            return "Chế độ hiện tại không phải Local — backend không gọi SHEIN.";
        }

        var account = await _db.FulfilAccounts
            .Include(a => a.Assignees)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return "Không tìm thấy tài khoản fulfil.";
        }

        if (!account.IsActive)
        {
            return $"Tài khoản {account.Name} đang tắt.";
        }

        var session = SheinSessionContext.TryCreate(
            account.Id,
            account.Name,
            account.Email,
            account.SheinHeadersJson,
            account.SheinCookie,
            account.SheinOrigin,
            account.SheinProxy);

        if (session is null)
        {
            return $"Tài khoản {account.Name} chưa có session SHEIN đủ (header + cookie).";
        }

        var fallbackUserId = await ResolveFallbackUserIdAsync(cancellationToken);
        var actingUserId = account.Assignees?.FirstOrDefault()?.UserId ?? fallbackUserId;
        if (actingUserId is null)
        {
            return $"Tài khoản {account.Name}: chưa có user để ghi sổ cái.";
        }

        // SyncFromSheinAsync khóa theo billno — không giữ cổng toàn process quanh fetch.
        try
        {
            var summary = await SyncAccountAsync(account, session, config, actingUserId.Value, cancellationToken);
            var text = $"{account.Name}: {summary}";
            _logger.LogInformation("SHEIN local sync (manual): {Summary}", text);
            return text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SHEIN local sync failed for account {Account}", account.Name);
            await MarkSessionAsync(account, ex.Message, cancellationToken);
            return $"{account.Name}: lỗi — {ex.Message}";
        }
    }

    private async Task<string> RunAsync(SheinSyncConfigDto config, CancellationToken cancellationToken)
    {
        var accounts = await _db.FulfilAccounts
            .Where(a => a.IsActive && a.SheinHeadersJson != null && a.SheinCookie != null)
            .Include(a => a.Assignees)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return "Chưa có tài khoản fulfil nào lưu session SHEIN.";
        }

        var fallbackUserId = await ResolveFallbackUserIdAsync(cancellationToken);
        var summaries = new List<string>();

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = SheinSessionContext.TryCreate(
                account.Id,
                account.Name,
                account.Email,
                account.SheinHeadersJson,
                account.SheinCookie,
                account.SheinOrigin,
                account.SheinProxy);

            if (session is null)
            {
                continue;
            }

            var actingUserId = account.Assignees?.FirstOrDefault()?.UserId ?? fallbackUserId;
            if (actingUserId is null)
            {
                _logger.LogWarning(
                    "SHEIN realtime sync skipped {Account}: no user to attribute ledger rows to",
                    account.Name);
                continue;
            }

            try
            {
                var summary = await SyncAccountAsync(account, session, config, actingUserId.Value, cancellationToken);
                summaries.Add($"{account.Name}: {summary}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SHEIN realtime sync failed for account {Account}", account.Name);
                await MarkSessionAsync(account, ex.Message, cancellationToken);
                summaries.Add($"{account.Name}: lỗi — {ex.Message}");
            }
        }

        var text = summaries.Count > 0 ? string.Join(" · ", summaries) : "Không có tài khoản nào chạy được.";
        _logger.LogInformation("SHEIN realtime sync: {Summary}", text);
        return text;
    }

    private async Task<string> SyncAccountAsync(
        FulfilAccount account,
        SheinSessionContext session,
        SheinSyncConfigDto config,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        var maxPages = MaxPagesNew;
        // Always cut list by orderedAt lookback (former mode "new").
        var lookbackDays = Math.Clamp(
            config.RefreshLookbackDays <= 0
                ? ExtensionSheinSyncSettingKeys.DefaultRefreshLookbackDays
                : config.RefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MinRefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MaxRefreshLookbackDays);
        var lookbackCutoffUtc = DateTime.UtcNow.AddDays(-lookbackDays);

        var listOrders = new List<SheinOrderDto>();
        for (var page = 1; page <= maxPages; page++)
        {
            var result = await _client.FetchOrderListPageAsync(session, page, ListPageSize, cancellationToken);
            if (!result.Ok)
            {
                await MarkSessionAsync(account, result.Error, cancellationToken, result.SessionInvalid);
                return listOrders.Count == 0
                    ? result.Error ?? "không lấy được đơn."
                    : $"dừng ở trang {page} ({result.Error}).";
            }

            var pageOrders = ExtractListOrders(result.Payload, session.Origin, config.CaptureRawPayload);

            // Unknown orderedAt kept (same as extension) — sparse list must not drop matches.
            var within = pageOrders
                .Where(o => o.OrderedAt is null || o.OrderedAt.Value >= lookbackCutoffUtc)
                .ToList();
            var allOutside = pageOrders.Count > 0 && within.Count == 0;
            listOrders.AddRange(within);
            if (allOutside || within.Count < pageOrders.Count)
            {
                break;
            }

            // A short page means there is nothing behind it; stop rather than pay for empty calls.
            if (pageOrders.Count < ListPageSize)
            {
                break;
            }

            if (page < maxPages && PageDelay > TimeSpan.Zero)
            {
                await Task.Delay(PageDelay, cancellationToken);
            }
        }

        await MarkSessionAsync(account, null, cancellationToken);

        var needsDetail = new List<string>();
        var matched = 0;
        var updatedTracking = 0;
        var updatedCost = 0;

        // Lượt do backend chạy cũng cần một mã tra cứu như lượt từ extension, nếu không nhật ký
        // sẽ đầy những dòng legacy không gộp được.
        var runId = $"backend-{Guid.NewGuid():N}"[..32];
        var listBatchIndex = 0;

        foreach (var batch in Chunk(listOrders, ListBatchSize))
        {
            foreach (var order in batch)
            {
                if (string.IsNullOrWhiteSpace(order.AccountEmail)
                    && !string.IsNullOrWhiteSpace(session.AccountEmail))
                {
                    order.AccountEmail = session.AccountEmail.Trim().ToLowerInvariant();
                }
            }

            // Không set MaxAutoMarkShipAttempts / AutoMarkShipPollInteractive — chỉ ingest;
            // Mark Ship do shein.order_detail_sync (giống HTTP extension).
            var result = await _orderSync.SyncFromSheinAsync(
                new SheinOrderSyncRequest
                {
                    Phase = "list",
                    Orders = batch,
                    RunId = runId,
                    BatchIndex = ++listBatchIndex,
                    SyncFulfilAccountId = account.Id
                },
                actingUserId,
                cancellationToken);
            matched += result.Matched;
            updatedTracking += result.UpdatedTracking;
            updatedCost += result.UpdatedCost;
            needsDetail.AddRange(result.NeedsDetailBillnos);
        }

        // Chỉ còn force_billnos (chẩn đoán). Cập nhật tracking/mốc dựa trên đơn có trong list SHEIN.
        needsDetail.AddRange(config.RefreshBillnos);

        var detailBillnos = needsDetail
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDetailPerAccount)
            .ToList();

        var detailOrders = await FetchDetailsAsync(
            session, detailBillnos, listOrders, config, cancellationToken);

        var detailBatchIndex = 0;
        foreach (var batch in Chunk(detailOrders, DetailBatchSize))
        {
            foreach (var order in batch)
            {
                if (string.IsNullOrWhiteSpace(order.AccountEmail)
                    && !string.IsNullOrWhiteSpace(session.AccountEmail))
                {
                    order.AccountEmail = session.AccountEmail.Trim().ToLowerInvariant();
                }
            }

            var result = await _orderSync.SyncFromSheinAsync(
                new SheinOrderSyncRequest
                {
                    Phase = "detail",
                    Orders = batch,
                    RunId = runId,
                    BatchIndex = ++detailBatchIndex,
                    SyncFulfilAccountId = account.Id
                },
                actingUserId,
                cancellationToken);
            updatedTracking += result.UpdatedTracking;
            updatedCost += result.UpdatedCost;
        }

        return $"{listOrders.Count} đơn · khớp {matched} · tracking +{updatedTracking} · phí +{updatedCost}";
    }

    /// <summary>
    /// Fetches <c>get_order_info</c> for each billno and folds it into the list payload (or a stub
    /// when the billno was outside the fetched pages).
    /// </summary>
    private async Task<List<SheinOrderDto>> FetchDetailsAsync(
        SheinSessionContext session,
        List<string> billnos,
        List<SheinOrderDto> listOrders,
        SheinSyncConfigDto config,
        CancellationToken cancellationToken)
    {
        if (billnos.Count == 0)
        {
            return [];
        }

        var byBillno = listOrders
            .Where(o => !string.IsNullOrWhiteSpace(o.Billno))
            .GroupBy(o => o.Billno, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var results = new List<SheinOrderDto>(billnos.Count);
        var gate = new SemaphoreSlim(Math.Max(1, config.ConcurrentThreads));
        var sync = new object();

        var tasks = billnos.Select(async billno =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var detail = await _client.FetchOrderDetailAsync(session, billno, cancellationToken);
                if (!detail.Ok)
                {
                    _logger.LogDebug(
                        "SHEIN detail {Billno} failed for {Account}: {Error}",
                        billno, session.AccountName, detail.Error);
                    return;
                }

                var target = byBillno.TryGetValue(billno, out var listOrder)
                    ? listOrder
                    : SheinOrderNormalizer.StubOrder(billno);

                SheinOrderNormalizer.ApplyDetail(detail.Payload, target, config.CaptureRawPayload);

                lock (sync)
                {
                    results.Add(target);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>Pulls the order array out of the list response, whichever envelope SHEIN used.</summary>
    private static List<SheinOrderDto> ExtractListOrders(JsonNode? payload, string origin, bool captureRaw)
    {
        var root = SheinJson.Obj(payload);
        var info = SheinJson.Obj(SheinJson.Pick(root, "info", "data", "result")) ?? root;
        var array = SheinJson.Arr(SheinJson.Pick(info, "orders", "orderList", "order_list", "list"));

        if (array is null)
        {
            return [];
        }

        return array
            .OfType<JsonObject>()
            .Select(o => SheinOrderNormalizer.NormalizeListOrder(o, origin, captureRaw))
            .Where(o => !string.IsNullOrWhiteSpace(o.Billno))
            .ToList();
    }

    /// <summary>
    /// Records the outcome of using a session. A transport hiccup is not written as a session
    /// error — only SHEIN rejecting the session should push an operator to re-capture.
    /// </summary>
    private async Task MarkSessionAsync(
        FulfilAccount account,
        string? error,
        CancellationToken cancellationToken,
        bool sessionInvalid = true)
    {
        var checkedAt = DateTime.UtcNow;
        var storedError = error is null
            ? null
            : sessionInvalid
                ? error.Length > 500 ? error[..500] : error
                : account.SheinSessionLastError;

        // Ghi thẳng hai cột bằng ExecuteUpdate thay vì mutate entity rồi SaveChanges. Hàm này được
        // gọi cả trong nhánh catch của một lượt sync vừa sửa dở hàng loạt Order/OrderSupplierLink
        // trên **cùng** một DbContext scoped, nên một SaveChanges ở đây sẽ commit luôn trạng thái dở
        // dang đó: link đã Remove mà chưa gắn lại, tổng tiền tính được một nửa, dòng sổ cái xoá mà
        // chưa tạo dòng mới. ExecuteUpdate không đọc ChangeTracker nên trạng thái phiên vẫn được ghi
        // lại mà không mang theo bất cứ thay đổi nào khác.
        await _db.FulfilAccounts
            .Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(a => a.SheinSessionLastCheckedAt, checkedAt)
                    .SetProperty(a => a.SheinSessionLastError, storedError),
                cancellationToken);

        account.SheinSessionLastCheckedAt = checkedAt;
        account.SheinSessionLastError = storedError;
    }

    /// <summary>
    /// User the ledger rows are attributed to when an account has no assignee. Falls back to the
    /// oldest active user, which in this deployment is the admin created by the seeder.
    /// </summary>
    private async Task<Guid?> ResolveFallbackUserIdAsync(CancellationToken cancellationToken)
    {
        return await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
