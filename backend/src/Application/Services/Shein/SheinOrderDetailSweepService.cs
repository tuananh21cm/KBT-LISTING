using System.Linq.Expressions;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services.Shein;

/// <inheritdoc cref="ISheinOrderDetailSweepService" />
public class SheinOrderDetailSweepService : ISheinOrderDetailSweepService
{
    /// <summary>Orders per <c>SyncFromSheinAsync</c> call, matching the extension's detail batching.</summary>
    private const int DetailBatchSize = 25;

    /// <summary>Parallel <c>get_order_info</c> calls per account when settings say nothing useful.</summary>
    private const int DefaultConcurrency = 4;

    private readonly IApplicationDbContext _db;
    private readonly IOrderSyncService _orderSync;
    private readonly ISheinOrderClient _client;
    private readonly ISettingProvider _settings;
    private readonly ILogger<SheinOrderDetailSweepService> _logger;

    /// <summary>0 = idle, 1 = a full cycle is in flight (single-flight across scoped instances).</summary>
    private static int _cycleRunning;

    /// <summary>UTC when the last full cycle finished (success or empty). MinValue → first cycle is due.</summary>
    private static DateTime _cycleCompletedAtUtc = DateTime.MinValue;

    /// <summary>Initializes the detail sweep service.</summary>
    public SheinOrderDetailSweepService(
        IApplicationDbContext db,
        IOrderSyncService orderSync,
        ISheinOrderClient client,
        ISettingProvider settings,
        ILogger<SheinOrderDetailSweepService> logger)
    {
        _db = db;
        _orderSync = orderSync;
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Links whose platform order is still waiting to ship and whose SHEIN tracking is not yet a
    /// real last-mile carrier code. These are the ones <c>get_order_info</c> can actually improve:
    /// the warehouse <c>C…</c>/<c>N…</c> code only becomes a carrier number inside the detail
    /// payload's <c>goods_pkg_rel_list[].shipping_no</c>.
    /// </summary>
    public static Expression<Func<SupplierPurchase, bool>> NeedsCarrierTrackingPredicate()
    {
        return l => l.SupplierOrderNo != null
            && l.Order != null
            && (l.Order.TrackingNo == null || l.Order.TrackingNo == "")
            && l.Order.PlatformOrderStatus == PlatformOrderStatuses.AwaitingShipment
            && (l.TrackingKind != SupplierTrackingKinds.Carrier
                || l.TrackingNo == null
                || l.TrackingNo == "");
    }

    /// <summary>
    /// Merges pending-Mark-Ship then needs-tracking into one snapshot: pending first, dedupe by
    /// billno, no ceiling. Callers chunk by <c>max_billnos</c> when processing.
    /// </summary>
    public static List<CandidateRow> BuildCandidateSnapshot(
        IEnumerable<CandidateRow> pendingMarkShip,
        IEnumerable<CandidateRow> needsTracking)
    {
        return pendingMarkShip
            .Concat(needsTracking)
            .GroupBy(r => r.SupplierOrderNo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SheinDetailSweepResult?> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var config = await ReadConfigAsync(cancellationToken);
        if (!config.Enabled)
        {
            return null;
        }

        if (_cycleCompletedAtUtc != DateTime.MinValue
            && DateTime.UtcNow - _cycleCompletedAtUtc < TimeSpan.FromMinutes(config.IntervalMinutes))
        {
            return null;
        }

        if (!TryBeginCycle())
        {
            return null;
        }

        try
        {
            return await RunFullCycleAsync(config, cancellationToken);
        }
        finally
        {
            EndCycle();
        }
    }

    /// <inheritdoc />
    public async Task<SheinDetailSweepResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        var config = await ReadConfigAsync(cancellationToken);

        if (!TryBeginCycle())
        {
            return new SheinDetailSweepResult
            {
                Summary = "Đang chạy chu kỳ khác — bỏ qua (single-flight).",
                Notes = { "SHEIN detail sweep đang chạy; thử lại sau khi chu kỳ hiện tại xong." }
            };
        }

        try
        {
            return await RunFullCycleAsync(config, cancellationToken);
        }
        finally
        {
            EndCycle();
        }
    }

    private static bool TryBeginCycle() =>
        Interlocked.CompareExchange(ref _cycleRunning, 1, 0) == 0;

    private static void EndCycle()
    {
        _cycleCompletedAtUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _cycleRunning, 0);
    }

    /// <summary>Test seam: reset in-process cycle gates between unit tests.</summary>
    internal static void ResetCycleStateForTests()
    {
        Interlocked.Exchange(ref _cycleRunning, 0);
        _cycleCompletedAtUtc = DateTime.MinValue;
    }

    /// <summary>Test seam: mark a cycle as completed at <paramref name="completedAtUtc"/>.</summary>
    internal static void SetCycleCompletedAtForTests(DateTime completedAtUtc)
    {
        _cycleCompletedAtUtc = completedAtUtc;
        Interlocked.Exchange(ref _cycleRunning, 0);
    }

    /// <inheritdoc />
    public async Task<SheinOrderSyncTrackResult> RunForOrderAsync(
        Guid orderId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var result = new SheinOrderSyncTrackResult { OrderId = orderId };

        await ClearSoftStaleSessionErrorsAsync(cancellationToken);

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.SupplierPurchases)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new Domain.Exceptions.NotFoundException("Không tìm thấy đơn hàng.");

        var links = (order.SupplierPurchases ?? [])
            .Where(p => p.Source == SupplierPurchaseSources.Shein
                && !string.IsNullOrWhiteSpace(p.SupplierOrderNo))
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToList();

        if (links.Count == 0)
        {
            throw new Domain.Exceptions.BusinessException(
                "Đơn chưa gắn billno SHEIN — không thể Sync track.");
        }

        var chargedManual = OrderSupplierCost.SelectCharged(order.SupplierPurchases ?? [])
            .Any(p => p.Source == SupplierPurchaseSources.Manual);
        if (chargedManual)
        {
            result.Notes.Add(
                "Đơn đang tính Cost theo dòng nhập tay (manual) — đã làm mới breakdown SHEIN nhưng Cost/Profit vẫn theo dòng manual.");
        }

        var beforeByBillno = links.ToDictionary(
            p => p.SupplierOrderNo!,
            p => new BillnoSnapshot(
                p.TrackingNo,
                p.TrackingKind,
                p.Carrier,
                p.SupplierTotal,
                p.SupplierSubtotal,
                p.AutoMarkShipStatus,
                p.AutoMarkShipError,
                p.FulfilAccountId),
            StringComparer.OrdinalIgnoreCase);

        var accounts = (await LoadAccountsAsync(cancellationToken)).ToDictionary(a => a.Account.Id);
        var usable = accounts.Values.Where(IsUsable).ToList();
        var soleAccountId = usable.Count == 1 ? usable[0].Account.Id : (Guid?)null;

        var byAccount = new Dictionary<Guid, List<string>>();
        var billnoResults = new Dictionary<string, SheinOrderSyncTrackBillnoResult>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            var billno = link.SupplierOrderNo!;
            var row = new SheinOrderSyncTrackBillnoResult { Billno = billno };
            billnoResults[billno] = row;

            var accountId = link.FulfilAccountId ?? soleAccountId;
            if (accountId is null || !accounts.TryGetValue(accountId.Value, out var owner))
            {
                row.SkipReason = SheinDetailSweepSkipReasons.AccountUnknown;
                row.Error =
                    "Không xác định được tài khoản mua hàng cho billno này (thiếu FulfilAccountId và hệ thống có nhiều session).";
                continue;
            }

            row.FulfilAccountName = owner.Account.Name;

            if (!owner.Account.IsActive)
            {
                row.SkipReason = SheinDetailSweepSkipReasons.AccountInactive;
                row.Error = $"Tài khoản mua hàng \"{owner.Account.Name}\" đang vô hiệu.";
                continue;
            }

            if (owner.Session is null)
            {
                row.SkipReason = SheinDetailSweepSkipReasons.SessionMissing;
                row.Error =
                    $"Tài khoản \"{owner.Account.Name}\" chưa có session SHEIN — mở extension bắt lại cookie.";
                continue;
            }

            if (!byAccount.TryGetValue(accountId.Value, out var list))
            {
                list = [];
                byAccount[accountId.Value] = list;
            }

            list.Add(billno);
        }

        var config = await ReadConfigAsync(cancellationToken);
        var runId = $"detail-{Guid.NewGuid():N}"[..32];
        var markShipBudget = Math.Min(links.Count, 5);

        foreach (var (accountId, billnos) in byAccount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = accounts[accountId];
            // Sync track dùng user đang bấm (admin/seller), không fallback assignee — ghi đúng người thao tác.
            var actingOwner = owner with { ActingUserId = actingUserId };

            try
            {
                var fetch = await FetchDetailsAsync(
                    owner.Session!, billnos, config.Concurrency, cancellationToken);

                foreach (var fail in fetch.Failures)
                {
                    if (!billnoResults.TryGetValue(fail.Billno, out var row)) continue;
                    row.FetchOk = false;
                    row.SkipReason = SheinDetailSweepSkipReasons.FetchFailed;
                    row.Error = fail.Error
                        ?? $"Không lấy được chi tiết SHEIN cho {fail.Billno}.";
                }

                if (fetch.Orders.Count > 0)
                {
                    await MarkSessionAsync(accountId, null, cancellationToken);
                }
                else if (fetch.Failures.Count > 0)
                {
                    await MarkSessionAsync(
                        accountId,
                        fetch.Failures[0].Error,
                        cancellationToken,
                        fetch.SessionInvalid);
                }

                if (fetch.Orders.Count == 0) continue;

                result.Fetched += fetch.Orders.Count;
                foreach (var orderDto in fetch.Orders)
                {
                    if (billnoResults.TryGetValue(orderDto.Billno ?? "", out var okRow))
                    {
                        okRow.FetchOk = true;
                    }
                }

                var sync = await PushDetailsCoreAsync(
                    fetch.Orders, actingOwner, runId, markShipBudget, cancellationToken);
                if (sync.SaveFailed)
                {
                    result.Notes.Add(
                        $"{owner.Account.Name}: lô {fetch.Orders.Count} billno không ghi được vào DB.");
                    foreach (var orderDto in fetch.Orders)
                    {
                        if (!billnoResults.TryGetValue(orderDto.Billno ?? "", out var row)) continue;
                        row.Error ??= "Không ghi được vào DB.";
                    }
                }
                else
                {
                    result.AutoShipped += sync.AutoShipped;
                    result.AutoShipFailed += sync.AutoShipFailed;
                    result.AutoShipBlocked += sync.AutoShipBlocked;
                }

                markShipBudget = Math.Max(
                    0,
                    markShipBudget - (sync.AutoShipped + sync.AutoShipFailed + sync.AutoShipBlocked));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Sync track failed for order {OrderId} account {Account}",
                    orderId, owner.Account.Name);
                await MarkSessionAsync(accountId, ex.Message, cancellationToken, sessionInvalid: false);
                result.Notes.Add($"{owner.Account.Name}: lỗi — {ex.Message}");
                foreach (var billno in billnos)
                {
                    if (!billnoResults.TryGetValue(billno, out var row)) continue;
                    row.SkipReason ??= SheinDetailSweepSkipReasons.FetchFailed;
                    row.Error = ex.Message;
                }
            }
        }

        // Đọc lại link sau sync để điền tracking/cost/Mark Ship per billno.
        var afterLinks = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId == orderId
                && p.Source == SupplierPurchaseSources.Shein
                && p.SupplierOrderNo != null)
            .ToListAsync(cancellationToken);

        foreach (var link in afterLinks)
        {
            var billno = link.SupplierOrderNo!;
            if (!billnoResults.TryGetValue(billno, out var row)) continue;
            if (!row.FetchOk) continue;

            beforeByBillno.TryGetValue(billno, out var before);
            row.TrackingNo = link.TrackingNo;
            row.TrackingKind = link.TrackingKind;
            row.Carrier = link.Carrier;
            row.SupplierTotal = link.SupplierTotal;
            row.SupplierSubtotal = link.SupplierSubtotal;
            row.CostIncomplete = link.SupplierSubtotal is null || link.SupplierTotal is null;
            row.AutoMarkShipStatus = link.AutoMarkShipStatus;
            row.AutoMarkShipError = link.AutoMarkShipError;

            var trackingChanged = !string.Equals(
                before?.TrackingNo, link.TrackingNo, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    before?.TrackingKind, link.TrackingKind, StringComparison.OrdinalIgnoreCase);
            var costChanged = before?.SupplierTotal != link.SupplierTotal
                || before?.SupplierSubtotal != link.SupplierSubtotal;

            row.UpdatedTracking = trackingChanged;
            row.UpdatedCost = costChanged;
            if (trackingChanged) result.UpdatedTracking++;
            if (costChanged) result.UpdatedCost++;
        }

        result.Billnos = links
            .Select(l => billnoResults[l.SupplierOrderNo!])
            .ToList();

        result.Summary =
            $"Sync track · fetched {result.Fetched}/{links.Count} · tracking +{result.UpdatedTracking} · " +
            $"cost +{result.UpdatedCost} · mark ship {result.AutoShipped} " +
            $"(lỗi {result.AutoShipFailed}, chặn {result.AutoShipBlocked})";

        // Soft per-billno errors stay on billnos[] for the FE toast — do not echo into Notes
        // (that duplicated the same line twice). When Mark Ship did not run this pass but a
        // prior failed/blocked/unconfirmed remains, explain so counters 0/0/0 are not confusing.
        var markShipRanThisPass =
            result.AutoShipped + result.AutoShipFailed + result.AutoShipBlocked > 0;
        if (!markShipRanThisPass
            && result.Billnos.Any(b =>
                string.Equals(b.AutoMarkShipStatus, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.AutoMarkShipStatus, "blocked", StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.AutoMarkShipStatus, "unconfirmed", StringComparison.OrdinalIgnoreCase)))
        {
            result.Notes.Add(
                "Mark Ship chưa chạy lại trong lượt này (còn lỗi/chờ từ lần trước — đang cooldown hoặc bấm Đẩy track).");
        }

        _logger.LogInformation(
            "Sync track order {OrderId}: {Summary}", orderId, result.Summary);
        return result;
    }

    private sealed record BillnoSnapshot(
        string? TrackingNo,
        string? TrackingKind,
        string? Carrier,
        decimal? SupplierTotal,
        decimal? SupplierSubtotal,
        string? AutoMarkShipStatus,
        string? AutoMarkShipError,
        Guid? FulfilAccountId);

    private async Task<SheinDetailSweepResult> RunFullCycleAsync(
        SweepConfig config,
        CancellationToken cancellationToken)
    {
        var result = new SheinDetailSweepResult();
        // Badge từng bị nhuộm đỏ bởi soft-stale ("Session bắt lúc … đã quá 12 giờ") dù cookie
        // còn sống. Xoá trước khi chọn ứng viên để cột Session khớp ngay chu kỳ này.
        await ClearSoftStaleSessionErrorsAsync(cancellationToken);
        var candidates = await SelectAllCandidatesAsync(cancellationToken);
        result.Candidates = candidates.Count;

        if (candidates.Count == 0)
        {
            result.Summary = "Không có đơn nào cần lấy chi tiết.";
            return result;
        }

        var accounts = (await LoadAccountsAsync(cancellationToken)).ToDictionary(a => a.Account.Id);
        var usable = accounts.Values.Where(IsUsable).ToList();

        if (usable.Count == 0)
        {
            result.Summary =
                "Chưa có tài khoản mua hàng nào lưu session SHEIN dùng được — bật Auto Sync trên extension để đẩy cookie lên.";
            return result;
        }

        // Link không gắn tài khoản chỉ chạy được khi hệ thống có đúng một session: gọi
        // get_order_info bằng cookie của tài khoản khác thì SHEIN trả 401/đơn không tồn tại,
        // và đó là lỗi im lặng vì payload vẫn là JSON hợp lệ.
        var soleAccountId = usable.Count == 1 ? usable[0].Account.Id : (Guid?)null;
        var runId = $"detail-{Guid.NewGuid():N}"[..32];
        var batches = 0;

        foreach (var chunk in Chunk(candidates, config.MaxBillnos))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batches++;

            var byAccount = new Dictionary<Guid, List<string>>();
            foreach (var candidate in chunk)
            {
                var accountId = candidate.FulfilAccountId ?? soleAccountId;
                if (accountId is null || !accounts.TryGetValue(accountId.Value, out var owner))
                {
                    Bump(result, SheinDetailSweepSkipReasons.AccountUnknown);
                    continue;
                }

                if (!owner.Account.IsActive)
                {
                    Bump(result, SheinDetailSweepSkipReasons.AccountInactive);
                    continue;
                }

                if (owner.Session is null)
                {
                    Bump(result, SheinDetailSweepSkipReasons.SessionMissing);
                    continue;
                }

                if (!byAccount.TryGetValue(accountId.Value, out var list))
                {
                    list = [];
                    byAccount[accountId.Value] = list;
                }

                list.Add(candidate.SupplierOrderNo);
            }

            // Ngân sách Mark Ship reset mỗi lô (max_billnos), không cộng dồn cả chu kỳ.
            var markShipBudget = config.MaxMarkShipPerPass;

            foreach (var (accountId, billnos) in byAccount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var owner = accounts[accountId];
                try
                {
                    var fetch = await FetchDetailsAsync(
                        owner.Session!, billnos, config.Concurrency, cancellationToken);

                    if (fetch.Failures.Count > 0)
                    {
                        Bump(result, SheinDetailSweepSkipReasons.FetchFailed, fetch.Failures.Count);
                    }

                    // Chỉ xoá lỗi khi thực sự gọi được ít nhất một đơn. Xoá vô điều kiện sau một lượt
                    // trượt sạch sẽ khiến cột Session hiện "Sẵn sàng" cho một session đã chết.
                    if (fetch.Orders.Count > 0)
                    {
                        await MarkSessionAsync(accountId, null, cancellationToken);
                    }
                    else
                    {
                        var firstError = fetch.Failures.Count > 0 ? fetch.Failures[0].Error : null;
                        await MarkSessionAsync(
                            accountId, firstError, cancellationToken, fetch.SessionInvalid);
                        result.Notes.Add(
                            $"{owner.Account.Name}: {billnos.Count} billno đều không lấy được — {firstError ?? "không rõ lý do"}.");
                        continue;
                    }

                    result.Fetched += fetch.Orders.Count;
                    markShipBudget = await PushDetailsAsync(
                        fetch.Orders, owner, runId, markShipBudget, result, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "SHEIN detail sweep failed for account {Account}", owner.Account.Name);
                    await MarkSessionAsync(accountId, ex.Message, cancellationToken, sessionInvalid: false);
                    result.Notes.Add($"{owner.Account.Name}: lỗi — {ex.Message}");
                }
            }
        }

        result.Batches = batches;
        var skipped = result.Skipped.Count == 0
            ? string.Empty
            : " · bỏ qua " + string.Join(", ", result.Skipped.Select(kv => $"{kv.Key}={kv.Value}"));
        result.Summary =
            $"Chu kỳ {result.Fetched}/{result.Candidates} đơn · {batches} lô · tracking +{result.UpdatedTracking} · " +
            $"cost +{result.UpdatedCost} · mark ship {result.AutoShipped} " +
            $"(lỗi {result.AutoShipFailed}, chặn {result.AutoShipBlocked}){skipped}";
        _logger.LogInformation("SHEIN detail sweep: {Summary}", result.Summary);
        return result;
    }

    /// <summary>
    /// Full eligible snapshot at cycle start — no <c>max_billnos</c> ceiling, no <c>LastSeenAt</c>
    /// freshness ranking. Pending Mark Ship first (stable by <c>FirstSeenAt</c>), then needs-tracking.
    /// New matches that appear mid-cycle wait for the next snapshot.
    /// </summary>
    private async Task<List<CandidateRow>> SelectAllCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var needsTracking = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(NeedsCarrierTrackingPredicate())
            .OrderBy(l => l.FirstSeenAt)
            .ThenBy(l => l.SupplierOrderNo)
            .Select(l => new CandidateRow(l.SupplierOrderNo!, l.FulfilAccountId, l.FirstSeenAt))
            .ToListAsync(cancellationToken);

        var pendingMarkShip = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(OrderAutoMarkShipPolicy.PendingQueuePredicate(DateTime.UtcNow))
            .OrderBy(l => l.FirstSeenAt)
            .ThenBy(l => l.SupplierOrderNo)
            .Select(l => new CandidateRow(l.SupplierOrderNo!, l.FulfilAccountId, l.FirstSeenAt))
            .ToListAsync(cancellationToken);

        return BuildCandidateSnapshot(pendingMarkShip, needsTracking);
    }

    /// <summary>
    /// Every fulfil account plus its (possibly unusable) session, so the pass can name the exact
    /// reason a billno was skipped instead of lumping "no account" and "no cookie" together.
    /// Accounts are a handful of rows; loading them all beats one query per reason.
    /// </summary>
    private async Task<List<SessionAccount>> LoadAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await _db.FulfilAccounts
            .AsNoTracking()
            .Include(a => a.Assignees)
            .ToListAsync(cancellationToken);

        var fallbackUserId = await ResolveFallbackUserIdAsync(cancellationToken);

        return accounts
            .Select(a => new SessionAccount(
                a,
                SheinSessionContext.TryCreate(
                    a.Id, a.Name, a.Email, a.SheinHeadersJson, a.SheinCookie, a.SheinOrigin, a.SheinProxy),
                a.Assignees?.FirstOrDefault()?.UserId ?? fallbackUserId))
            .ToList();
    }

    /// <summary>Account that can actually be swept right now: active and session built.</summary>
    private static bool IsUsable(SessionAccount account) =>
        account.Account.IsActive && account.Session is not null;

    /// <summary>
    /// Fetches <c>get_order_info</c> per billno in parallel and folds each onto a stub order.
    /// Shared by bulk auto sweep and Sync track.
    /// </summary>
    private async Task<FetchOutcome> FetchDetailsAsync(
        SheinSessionContext session,
        List<string> billnos,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var orders = new List<SheinOrderDto>(billnos.Count);
        var failures = new List<FetchFailure>(billnos.Count);
        var sessionInvalid = false;
        var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var sync = new object();

        var tasks = billnos.Select(async billno =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var detail = await _client.FetchOrderDetailAsync(session, billno, cancellationToken);
                if (!detail.Ok)
                {
                    // Không log cookie/header ở bất kỳ nhánh nào — chỉ billno và lý do.
                    _logger.LogDebug(
                        "SHEIN detail {Billno} failed for {Account}: {Error}",
                        billno, session.AccountName, detail.Error);
                    lock (sync)
                    {
                        failures.Add(new FetchFailure(billno, detail.Error));
                        // Một billno chết vì `OrderNotExist` không có nghĩa cookie chết; chỉ tín
                        // hiệu auth/risk mới được phép bắt người vận hành bắt lại session.
                        sessionInvalid |= detail.SessionInvalid;
                    }
                    return;
                }

                var order = SheinOrderNormalizer.StubOrder(billno);
                // Always capture raw for sweep/Sync Track: RawPayload.detail was previously always
                // null because captureRaw defaulted false — blocks "Thiếu order detail" diagnosis.
                SheinOrderNormalizer.ApplyDetail(detail.Payload, order, captureRaw: true);

                lock (sync)
                {
                    orders.Add(order);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new FetchOutcome(orders, failures, sessionInvalid);
    }

    /// <summary>
    /// Feeds fetched details through phase <c>detail</c> + <c>updateMode=all</c> (same path for
    /// auto sweep and Sync track) so cost reconcile / Mark Ship are not skipped after Upsert.
    /// Returns Mark Ship budget left for the rest of the pass.
    /// </summary>
    private async Task<int> PushDetailsAsync(
        List<SheinOrderDto> orders,
        SessionAccount owner,
        string runId,
        int markShipBudget,
        SheinDetailSweepResult result,
        CancellationToken cancellationToken)
    {
        if (owner.ActingUserId is null)
        {
            result.Notes.Add(
                $"{owner.Account.Name}: chưa có người phụ trách và không tìm được user mặc định — bỏ qua.");
            return markShipBudget;
        }

        var batchIndex = 0;
        foreach (var batch in Chunk(orders, DetailBatchSize))
        {
            var sync = await PushDetailsCoreAsync(
                batch, owner, runId, markShipBudget, cancellationToken, ++batchIndex);

            if (sync.SaveFailed)
            {
                // Lô vỡ ở SaveChanges thì mọi counter của nó về 0 — không nói ra thì lượt này trông
                // như một lượt yên ả không có việc gì làm.
                result.Notes.Add(
                    $"{owner.Account.Name}: lô {batchIndex} ({batch.Count} đơn) không ghi được vào DB.");
                continue;
            }

            result.UpdatedTracking += sync.UpdatedTracking;
            result.UpdatedCost += sync.UpdatedCost;
            result.AutoShipped += sync.AutoShipped;
            result.AutoShipFailed += sync.AutoShipFailed;
            result.AutoShipBlocked += sync.AutoShipBlocked;

            markShipBudget = Math.Max(
                0, markShipBudget - (sync.AutoShipped + sync.AutoShipFailed + sync.AutoShipBlocked));
        }

        return markShipBudget;
    }

    /// <summary>
    /// Shared <c>SyncFromSheinAsync(phase=detail, updateMode=all)</c> call used by both flows.
    /// </summary>
    private async Task<SheinOrderSyncResultDto> PushDetailsCoreAsync(
        List<SheinOrderDto> orders,
        SessionAccount owner,
        string runId,
        int markShipBudget,
        CancellationToken cancellationToken,
        int batchIndex = 1)
    {
        if (owner.ActingUserId is null)
        {
            return new SheinOrderSyncResultDto();
        }

        return await _orderSync.SyncFromSheinAsync(
            new SheinOrderSyncRequest
            {
                Phase = "detail",
                // Đã tốn get_order_info — ép full path (cost reconcile + Mark Ship), không để
                // mode new bỏ qua khi HasSheinIncrementalDelta = false.
                UpdateMode = "all",
                Orders = orders,
                RunId = runId,
                BatchIndex = batchIndex,
                MaxAutoMarkShipAttempts = markShipBudget,
                // Sync Track + background detail sweep are not Cloudflare-capped; use long poll.
                AutoMarkShipPollInteractive = true
            },
            owner.ActingUserId.Value,
            cancellationToken);
    }

    /// <summary>
    /// Prefix of the old soft-stale hint that used to be written into
    /// <see cref="FulfilAccount.SheinSessionLastError"/> when capture age exceeded
    /// <see cref="SheinDetailSyncSettingKeys.MaxSessionAge"/>. Age is a UI warning now;
    /// leftover rows would keep the Session badge red until an operator re-tested.
    /// </summary>
    internal const string SoftStaleSessionErrorPrefix = "Session bắt lúc";

    /// <summary>
    /// Clears leftover soft-stale hints so the Session column stops saying "Lỗi" for
    /// cookies that were only aged, not rejected by SHEIN.
    /// </summary>
    private Task ClearSoftStaleSessionErrorsAsync(CancellationToken cancellationToken)
    {
        return _db.FulfilAccounts
            .Where(a => a.SheinSessionLastError != null
                && a.SheinSessionLastError.StartsWith(SoftStaleSessionErrorPrefix))
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.SheinSessionLastError, (string?)null),
                cancellationToken);
    }

    /// <summary>
    /// Records session health with <c>ExecuteUpdate</c>, never <c>SaveChanges</c>: this runs on the
    /// same scoped DbContext as a detail batch that may be half-applied, and a SaveChanges here
    /// would commit that partial state (links removed but not re-attached, totals half-computed).
    /// </summary>
    private async Task MarkSessionAsync(
        Guid accountId,
        string? error,
        CancellationToken cancellationToken,
        bool sessionInvalid = true)
    {
        var checkedAt = DateTime.UtcNow;
        var storedError = error is null || !sessionInvalid
            ? null
            : error.Length > 500 ? error[..500] : error;

        if (error is not null && !sessionInvalid)
        {
            // Sự cố mạng/proxy không phải lý do bắt người vận hành bắt lại cookie — chỉ ghi mốc.
            await _db.FulfilAccounts
                .Where(a => a.Id == accountId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(a => a.SheinSessionLastCheckedAt, checkedAt),
                    cancellationToken);
            return;
        }

        await _db.FulfilAccounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(a => a.SheinSessionLastCheckedAt, checkedAt)
                    .SetProperty(a => a.SheinSessionLastError, storedError),
                cancellationToken);
    }

    private async Task<Guid?> ResolveFallbackUserIdAsync(CancellationToken cancellationToken)
    {
        return await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SweepConfig> ReadConfigAsync(CancellationToken cancellationToken)
    {
        var values = await _settings.GetValuesAsync(
            [
                SheinDetailSyncSettingKeys.Enabled,
                SheinDetailSyncSettingKeys.IntervalMinutes,
                SheinDetailSyncSettingKeys.MaxBillnos,
                SheinDetailSyncSettingKeys.MaxMarkShipPerPass,
                ExtensionSheinSyncSettingKeys.ConcurrentThreads
            ],
            cancellationToken);

        return new SweepConfig
        {
            Enabled = string.Equals(
                values.GetValueOrDefault(SheinDetailSyncSettingKeys.Enabled),
                "true",
                StringComparison.OrdinalIgnoreCase),
            IntervalMinutes = ClampSetting(
                values, SheinDetailSyncSettingKeys.IntervalMinutes,
                SheinDetailSyncSettingKeys.DefaultIntervalMinutes,
                SheinDetailSyncSettingKeys.MinIntervalMinutes,
                SheinDetailSyncSettingKeys.MaxIntervalMinutes),
            MaxBillnos = ClampSetting(
                values, SheinDetailSyncSettingKeys.MaxBillnos,
                SheinDetailSyncSettingKeys.DefaultMaxBillnos,
                SheinDetailSyncSettingKeys.MinMaxBillnos,
                SheinDetailSyncSettingKeys.MaxMaxBillnos),
            MaxMarkShipPerPass = ClampSetting(
                values, SheinDetailSyncSettingKeys.MaxMarkShipPerPass,
                SheinDetailSyncSettingKeys.DefaultMaxMarkShipPerPass,
                SheinDetailSyncSettingKeys.MinMaxMarkShipPerPass,
                SheinDetailSyncSettingKeys.MaxMaxMarkShipPerPass),
            // Số luồng dùng chung với extension: cùng một tài khoản SHEIN, cùng ngưỡng rate limit.
            Concurrency = ClampSetting(
                values, ExtensionSheinSyncSettingKeys.ConcurrentThreads,
                DefaultConcurrency,
                ExtensionSheinSyncSettingKeys.MinConcurrentThreads,
                ExtensionSheinSyncSettingKeys.MaxConcurrentThreads)
        };
    }

    private static int ClampSetting(
        Dictionary<string, string> values, string key, int fallback, int min, int max)
    {
        return int.TryParse(values.GetValueOrDefault(key), out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static void Bump(SheinDetailSweepResult result, string reason, int count = 1)
    {
        result.Skipped[reason] = result.Skipped.GetValueOrDefault(reason) + count;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }

    /// <summary>
    /// One billno in a detail-sweep snapshot. Public so tests can assert merge/order without DI.
    /// </summary>
    public sealed record CandidateRow(string SupplierOrderNo, Guid? FulfilAccountId, DateTime FirstSeenAt);

    /// <summary>
    /// What one account's fetch round produced. <paramref name="SessionInvalid"/> is the OR of the
    /// auth/risk signals only — a billno that no longer exists on SHEIN must not get the operator
    /// sent back to re-capture a perfectly good cookie.
    /// </summary>
    private sealed record FetchOutcome(
        List<SheinOrderDto> Orders,
        List<FetchFailure> Failures,
        bool SessionInvalid);

    private sealed record FetchFailure(string Billno, string? Error);

    private sealed record SessionAccount(
        FulfilAccount Account,
        SheinSessionContext? Session,
        Guid? ActingUserId);

    private sealed class SweepConfig
    {
        public bool Enabled { get; init; }
        public int IntervalMinutes { get; init; }
        public int MaxBillnos { get; init; }
        public int MaxMarkShipPerPass { get; init; }
        public int Concurrency { get; init; }
    }
}
