using System.Text.Json;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using static Application.Services.SheinMatchKeys;

namespace Application.Services;

/// <summary>SHEIN multi-purchase / Joint Shipment helpers for <see cref="OrderSyncService"/>.</summary>
public partial class OrderSyncService
{
    private static readonly JsonSerializerOptions LinkJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string ClassifySheinTracking(string? trackingNo, string? carrier) =>
        OrderFourSellerStatusApply.ClassifyTracking(trackingNo, carrier);

    /// <summary>
    /// Lượt quét nền (<c>RunId</c> <c>detail-…</c>) vừa biến mã kho / mã rỗng thành mã carrier thật
    /// — xếp đơn vào watchlist Telegram "Add track" lần đầu.
    ///
    /// Đường extension (<c>ext-…</c>) im lặng: đã có UI theo dõi. Lượt sau snapshot đã carrier nên
    /// không vào lại qua gate này; nếu Mark Ship còn pending / notify chưa sẵn sàng thì
    /// <see cref="Order.TelegramAddTrackNotifyPendingAt"/> giữ chỗ để
    /// <see cref="EnqueuePendingDetailAddTrackRetries"/> gửi ở lượt <c>detail-</c> sau.
    /// </summary>
    internal static bool ShouldNotifyDetailAddTrack(
        string? runId,
        SheinPurchaseSnapshot before,
        SupplierPurchase link) =>
        runId?.StartsWith("detail-", StringComparison.Ordinal) == true
        && link.TrackingKind == SupplierTrackingKinds.Carrier
        && ClassifySheinTracking(before.TrackingNo, null) != SupplierTrackingKinds.Carrier;

    /// <summary>
    /// Đơn đã stamp pending (Mark Ship hoãn / thiếu BotToken·Chat·toggle) và chưa gửi thành công
    /// — xếp lại vào watchlist trên lượt <c>detail-</c> khi đơn còn trong pool.
    /// </summary>
    internal static void EnqueuePendingDetailAddTrackRetries(
        IReadOnlyList<Order> pool,
        IDictionary<Guid, SheinDetailAddTrackNotifyItem> watchlist,
        DateTime nowUtc)
    {
        foreach (var order in pool)
        {
            if (order.TelegramAddTrackNotifiedAt is not null) continue;
            if (order.TelegramAddTrackNotifyPendingAt is null) continue;
            if (watchlist.ContainsKey(order.Id)) continue;

            var link = order.SupplierPurchases?
                .Where(p =>
                    string.Equals(
                        p.TrackingKind,
                        SupplierTrackingKinds.Carrier,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.LastSeenAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefault();
            if (link is null) continue;

            var billno = link.SupplierOrderNo?.Trim();
            if (string.IsNullOrWhiteSpace(billno)) continue;

            var detectedAt = order.TelegramAddTrackNotifyPendingAt ?? nowUtc;
            watchlist.TryAdd(
                order.Id,
                new SheinDetailAddTrackNotifyItem(order, link, billno, detectedAt));
        }
    }

    /// <summary>
    /// Ghi nhận một billno bị bỏ qua: tăng <c>Unmatched</c> và lưu mã lý do ổn định kèm chi tiết
    /// đủ để lần lại.
    ///
    /// Mọi nhánh <c>continue</c> của <see cref="SyncFromSheinAsync"/> phải đi qua đây. Extension
    /// từng đếm lý do bằng regex trên <c>Notes</c> nên chỉ hai nhánh có mẫu chuỗi mới hiện lên,
    /// còn lại rơi im lặng và tổng "khớp + bỏ qua" không bằng số đơn đã lấy.
    /// </summary>
    private static void AddSheinSkip(
        SheinOrderSyncResultDto result,
        string billno,
        string reason,
        string? detail = null)
    {
        result.Unmatched++;
        result.SkipOutcomes.Add(new SheinSkipOutcomeDto
        {
            Billno = billno.Trim(),
            Reason = reason,
            Detail = detail
        });
        result.SkipCounts[reason] =
            result.SkipCounts.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    /// <summary>Số ngày giữ nhật ký lượt sync — đủ để soi một sự cố, đủ nhỏ để bảng không phình.</summary>
    private const int SheinSyncRunLogRetentionDays = 7;

    /// <summary>
    /// Ghi một dòng nhật ký cho lô sync vừa xử lý và dọn dòng quá hạn.
    ///
    /// Nhật ký là mục đích duy nhất của bảng này nên không được phép làm hỏng lượt sync: mọi lỗi
    /// ghi log đều nuốt lại thành warning, phần đồng bộ đã <c>SaveChanges</c> xong từ trước.
    /// </summary>
    private async Task WriteSheinSyncRunLogAsync(
        SheinOrderSyncRequest request,
        SheinOrderSyncResultDto result,
        string phase,
        string updateMode,
        int lookbackDays,
        int poolSize,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var runId = (request.RunId ?? string.Empty).Trim();
            if (runId.Length > 64) runId = runId[..64];
            if (runId.Length == 0)
            {
                // Client cũ không gửi runId: vẫn ghi để không mất lô, gộp theo ngày-giờ khi đọc.
                runId = $"legacy-{startedAt:yyyyMMddHHmm}";
            }

            _db.SheinSyncRunLogs.Add(new SheinSyncRunLog
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Phase = phase,
                BatchIndex = request.BatchIndex,
                UpdateMode = updateMode,
                LookbackDays = lookbackDays,
                PoolSize = poolSize,
                Fetched = result.Fetched,
                Matched = result.Matched,
                Unmatched = result.Unmatched,
                Unchanged = result.Unchanged,
                UpdatedTracking = result.UpdatedTracking,
                UpdatedCost = result.UpdatedCost,
                SkipCountsJson = result.SkipCounts.Count > 0
                    ? JsonSerializer.Serialize(result.SkipCounts, LinkJsonOptions)
                    : null,
                SkipOutcomesJson = result.SkipOutcomes.Count > 0
                    ? JsonSerializer.Serialize(result.SkipOutcomes, LinkJsonOptions)
                    : null,
                NeedsDetailReasonsJson = result.NeedsDetailReasons.Count > 0
                    ? JsonSerializer.Serialize(result.NeedsDetailReasons, LinkJsonOptions)
                    : null,
                FulfilAccountName = result.ResolvedFulfilAccountName,
                SaveFailed = result.SaveFailed,
                CreatedAt = startedAt,
                DurationMs = (int)Math.Clamp(
                    (DateTime.UtcNow - startedAt).TotalMilliseconds, 0, int.MaxValue)
            });
            await _db.SaveChangesAsync(cancellationToken);

            // Dọn rác đúng một lần cho mỗi lượt: lô đầu của phase list. Không chốt cả phase thì mỗi
            // chunk detail (client cũ luôn gửi batchIndex = 1) lại chạy một ExecuteDelete trên toàn
            // bảng.
            if (phase == "list" && request.BatchIndex is null or <= 1)
            {
                var cutoff = DateTime.UtcNow.AddDays(-SheinSyncRunLogRetentionDays);
                await _db.SheinSyncRunLogs
                    .Where(x => x.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không ghi được nhật ký lượt sync SHEIN (runId={RunId}).", request.RunId);
        }
    }

    /// <summary>SHEIN purchase terminal for cost/timeline: refunded or canceled.</summary>
    private static bool IsTerminalSupplierStatus(string? status) =>
        OrderSupplierCost.IsTerminalStatus(NormalizeSheinStatusGroup(status));

    /// <summary>
    /// Billno ghost: SHEIN nói rõ chưa thanh toán **và** hệ thống chưa từng ghi nhận mốc trừ tiền
    /// nào cho nó.
    ///
    /// Điều kiện thứ hai là chốt an toàn một chiều: mốc <c>SupplierPaidAt</c> đã lưu là bằng chứng
    /// tiền đã ra, nên một payload sau đó báo <c>pay_time = 0</c> (rút gọn, cache cũ, đọc lỗi) không
    /// được phép xoá một lần mua thật. Từ khi mọi billno có một dòng <c>SupplierPurchases</c> riêng,
    /// bằng chứng đó nằm sẵn trên dòng đó — không còn phải quét cả pool để tìm.
    /// </summary>
    private static bool IsUnpaidGhostBillno(SupplierPurchase purchase, SheinOrderDto shein)
    {
        if (shein.SheinUnpaid != true) return false;
        if (shein.SheinPaidAt.HasValue) return false;
        return purchase.SupplierPaidAt is null;
    }

    /// <summary>
    /// Đồng bộ quy kết tiền (tài khoản mua hàng + thẻ) cấp đơn với tập lần mua hiện tại: điền từ lần
    /// mua khi đơn chưa có, bỏ khi tài khoản đang lưu không còn lần mua nào mang nó, gỡ hẳn khi đơn
    /// không còn billno nào.
    ///
    /// Hai field đó được sync tự điền từ email billing của billno, nên để lại sau khi gỡ thì đơn
    /// chưa từng mua vẫn hiện như đã có người trả tiền; ngược lại, không điền khi gắn thì đơn hiện
    /// đúng tiền trong sổ cái nhưng trống tên tài khoản.
    ///
    /// Giữ <c>PaidByUserId</c>: đó là người phụ trách đi đơn, không phải khoản tiền.
    /// </summary>
    private static void SyncFulfilAttributionWithPurchases(Order order)
    {
        EnsureSupplierPurchases(order);
        OrderPurchaseEvidence.SyncAttributionWithPurchases(order);
    }

    /// <summary>
    /// Gỡ một lần mua khỏi đơn sàn đang giữ nó và trả về đơn đó (hoặc <c>null</c> khi chưa gắn đơn
    /// nào).
    ///
    /// Dòng mua <b>không</b> bị xoá: nó quay về hàng đợi chưa gắn kèm <c>SkipReason</c>. Bản cũ
    /// <c>DELETE</c> hẳn bản ghi, nên một billno bị heuristic nhận sai là hàng ảo sẽ mất sạch giá
    /// mua, mốc thời gian và payload gốc — không còn gì để đối chiếu khi phát hiện nhận sai.
    ///
    /// Thứ tự bên trong là bắt buộc: <see cref="OrderPurchaseEvidence"/> đối chiếu timestamp của mốc
    /// với clock trên dòng mua, nên đánh giá sau khi gỡ thì không còn gì để đối chiếu và mốc sai sẽ
    /// ở lại vĩnh viễn.
    /// </summary>
    private async Task<Order?> DetachPurchaseFromOrderAsync(
        IReadOnlyList<Order> pool,
        SupplierPurchase purchase,
        string reason,
        DateTime now,
        CancellationToken cancellationToken,
        bool markGhost = false)
    {
        if (purchase.OrderId is null) return null;

        var order = pool.FirstOrDefault(o => o.Id == purchase.OrderId.Value);
        if (order is null)
        {
            // Đơn giữ billno này không nằm trong pool lần chạy hiện tại: vẫn phải gỡ, nhưng không
            // dựng lại được aggregate/milestone của nó ở đây. Xóa dòng chi theo purchase — nếu để
            // lại, gắn tay sau sẽ INSERT trùng IX_FulfilTransactions_PurchaseId_CostUpdate.
            SupplierPurchaseIngest.Detach(purchase, null, reason, now);
            await RemoveCostUpdatesForPurchaseAsync(purchase.Id, cancellationToken);
            return null;
        }

        EnsureSupplierPurchases(order);
        if (markGhost) purchase.SupplierUnpaid = true;
        OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order);

        SupplierPurchaseIngest.Detach(purchase, order, reason, now);

        RefreshOrderSupplierAggregates(order);
        OrderSheinMilestoneEvidence.ClearMilestonesWithoutLinkEvidence(order);
        order.UpdatedAt = now;
        return order;
    }

    /// <summary>Xóa mọi dòng CostUpdate còn treo theo lần mua (orphan sau detach ngoài pool).</summary>
    private async Task RemoveCostUpdatesForPurchaseAsync(
        Guid supplierPurchaseId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.FulfilTransactions
            .Where(t =>
                t.SupplierPurchaseId == supplierPurchaseId
                && t.Source == FulfilTransaction.SourceCostUpdate)
            .ToListAsync(cancellationToken);

        foreach (var local in _db.FulfilTransactions.Local
            .Where(t =>
                t.SupplierPurchaseId == supplierPurchaseId
                && t.Source == FulfilTransaction.SourceCostUpdate))
        {
            if (_db.Entry(local).State == EntityState.Deleted) continue;
            if (!rows.Contains(local)) rows.Add(local);
        }

        if (rows.Count == 0) return;
        _db.FulfilTransactions.RemoveRange(rows);
    }

    /// <summary>
    /// Record which purchase account and card paid for THIS billno on its own row.
    /// The SHEIN billing email is per-checkout, so keeping it only on the order would let a
    /// sibling billno of the same Joint Shipment overwrite it.
    /// </summary>
    private async Task ApplyPurchasePaymentAccountAsync(
        Order order,
        SupplierPurchase purchase,
        Guid? billnoAccountId,
        CancellationToken cancellationToken)
    {
        // The billing email of THIS billno is authoritative. A value inherited from the parent
        // order (including the one-time migration backfill) must be correctable, otherwise a
        // Joint Shipment bought from two SHEIN accounts stays attributed to a single one.
        if (billnoAccountId.HasValue && purchase.FulfilAccountId != billnoAccountId)
        {
            purchase.FulfilAccountId = billnoAccountId;
            purchase.FulfilCardId = null;
        }
        else
        {
            purchase.FulfilAccountId ??= order.FulfilAccountId;
        }

        if (purchase.FulfilCardId is null)
        {
            // Reuse the card explicitly chosen on the order only when it is the same account.
            purchase.FulfilCardId = purchase.FulfilAccountId == order.FulfilAccountId
                ? order.FulfilCardId ?? await ResolveActiveCardIdAsync(purchase.FulfilAccountId, cancellationToken)
                : await ResolveActiveCardIdAsync(purchase.FulfilAccountId, cancellationToken);
        }

        if (purchase.FulfilAccountId.HasValue || purchase.FulfilCardId.HasValue)
        {
            // Không lấy actingUserId — người mua = dấu Đã mua / order.PaidByUserId.
            purchase.PaidByUserId ??= order.PaidByUserId;
        }
    }

    /// <summary>
    /// Drop every CostUpdate debit of an order — an order may hold one row per billno, so
    /// removing just the first would leave the rest of the money on the card ledger.
    /// </summary>
    private async Task RemoveCostUpdateTransactionAsync(Order order, CancellationToken cancellationToken)
    {
        foreach (var row in await LoadCostUpdateTransactionsAsync(order.Id, cancellationToken))
        {
            _db.FulfilTransactions.Remove(row);
        }
    }

    /// <summary>
    /// Near-identical SHEIN billnos (Joint Shipment siblings), e.g. N2M1/N2M2 — same length, Hamming ≤ 2.
    /// </summary>
    private static bool AreNearIdenticalBillnos(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        a = a.Trim();
        b = b.Trim();
        if (a.Length != b.Length || a.Length < 8) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[i]))
            {
                diff++;
                if (diff > 2) return false;
            }
        }
        return diff >= 1;
    }

    private static HashSet<string> ParseLinkProductIds(string? json)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return keys;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json, LinkJsonOptions);
            if (list == null) return keys;
            foreach (var raw in list)
            {
                var v = raw?.Trim();
                if (!string.IsNullOrEmpty(v)) keys.Add(v);
            }
        }
        catch
        {
            // ignore bad snapshot
        }
        return keys;
    }

    /// <summary>
    /// SKU đã có billno <b>còn hiệu lực</b> mua hộ. Billno huỷ/hoàn hoặc chưa thanh toán không
    /// chiếm chỗ: mua lại sau khi huỷ chính là billno mới phủ đúng SKU cũ, nếu vẫn tính chúng vào
    /// tập covered thì <see cref="CanAttachAdditionalPurchase"/> trả <c>sku-already-covered</c> và
    /// lần mua lại bị khoá vĩnh viễn khỏi đơn (ca GSU18F24F0025M2 trên đơn 577499611162513640).
    /// </summary>
    private static HashSet<string> CoveredProductKeys(Order order)
    {
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var purchase in order.SupplierPurchases ?? [])
        {
            if (purchase.SupplierUnpaid || IsTerminalSupplierStatus(purchase.SupplierStatus)) continue;
            foreach (var k in ParseLinkProductIds(purchase.ProductIdsJson))
                covered.Add(k);
        }
        return covered;
    }

    /// <summary>
    /// Biên độ lệch đồng hồ cho phép giữa mốc đặt đơn sàn và mốc mua trên SHEIN. Hai mốc đến từ
    /// hai hệ thống khác nhau (4Seller parse wall-clock, SHEIN trả epoch) nên chênh vài giờ là
    /// bình thường; chỉ khoảng lệch lớn hơn ngưỡng này mới là dấu hiệu billno thuộc đơn khác.
    /// </summary>
    private static readonly TimeSpan PlatformOrderCausalitySlack = TimeSpan.FromHours(12);

    /// <summary>
    /// Billno được mua <b>trước</b> khi khách đặt đơn sàn thì không phải hàng của đơn đó, dù trùng
    /// phone+zip — trùng chỉ vì cùng một khách mua lặp lại về cùng địa chỉ. Không có ràng buộc này
    /// thì đơn <c>577499611162513640</c> (đặt 29/07 16:04) nhận hai billno mua 21/07 và giao xong
    /// 29/07 13:04, tức hàng về tay khách trước khi đơn tồn tại: chi phí, mốc Mua và mốc Giao đều
    /// sai. Trước đây guard <c>sku-already-covered</c> tình cờ che ca này, nên khi bỏ billno huỷ ra
    /// khỏi tập SKU đã cover thì phải nêu ràng buộc ra tường minh.
    /// </summary>
    private static bool IsPurchasedBeforePlatformOrder(SheinOrderDto shein, Order order)
    {
        if (order.OrderCreateTime is null) return false;

        // Lấy mốc mua muộn nhất biết được để không chặn oan khi một trong hai trường vắng.
        DateTime? purchasedAt = null;
        foreach (var candidate in new[] { shein.OrderedAt, shein.SheinPaidAt })
        {
            if (candidate is null) continue;
            var utc = NormalizeUtc(candidate.Value);
            if (purchasedAt is null || utc > purchasedAt) purchasedAt = utc;
        }
        if (purchasedAt is null) return false;

        var placedAt = NormalizeUtc(order.OrderCreateTime.Value);
        return placedAt - purchasedAt.Value > PlatformOrderCausalitySlack;
    }

    /// <summary>
    /// Đơn sàn đã hủy hoặc hoàn (TikTok/4Seller) — không còn là đích fulfil mặc định khi còn đơn
    /// anh em còn hiệu lực. Dùng <see cref="PlatformOrderStatuses.IsNonRevenueCommerce"/> để phủ cả
    /// <c>CANCELLED</c>/<c>REFUND</c> và bucket legacy <c>canceled</c>.
    /// </summary>
    internal static bool IsPlatformTerminalOrder(Order order) =>
        PlatformOrderStatuses.IsNonRevenueCommerce(order.PlatformOrderStatus, order.OrderStatus);

    /// <summary>
    /// Billno được mua <b>sau</b> khi đơn sàn đã hủy (mốc <see cref="Order.CanceledAt"/> từ
    /// 4Seller <c>CancelTime</c>). Tiền đó không thuộc đơn đã đóng — không gắn mới.
    /// Thiếu <c>CanceledAt</c> → <c>false</c> (không suy diễn).
    /// </summary>
    internal static bool IsPurchasedAfterPlatformCancel(SheinOrderDto shein, Order order)
    {
        if (order.CanceledAt is null) return false;
        if (!IsPlatformTerminalOrder(order)) return false;

        DateTime? purchasedAt = null;
        foreach (var candidate in new[] { shein.OrderedAt, shein.SheinPaidAt })
        {
            if (candidate is null) continue;
            var utc = NormalizeUtc(candidate.Value);
            if (purchasedAt is null || utc > purchasedAt) purchasedAt = utc;
        }
        if (purchasedAt is null) return false;

        var canceledAt = NormalizeUtc(order.CanceledAt.Value);
        return purchasedAt.Value - canceledAt > PlatformOrderCausalitySlack;
    }

    /// <summary>
    /// Whether a second SHEIN billno may attach to a local order that already has other purchases.
    /// Prefer SHEIN <c>relation_billno</c> / <c>confluence_batch_no</c> over near-identical billno Hamming.
    /// </summary>
    internal static bool CanAttachAdditionalPurchase(Order order, SheinOrderDto shein, out string reason)
    {
        if (IsPurchasedBeforePlatformOrder(shein, order))
        {
            reason = "ordered-before-platform-order";
            return false;
        }

        var sheinKeys = BuildSheinProductIdKeys(shein);
        var covered = CoveredProductKeys(order);
        var itemCount = order.Items?.Count ?? 0;
        var purchases = (order.SupplierPurchases ?? [])
            .Where(p => p.Source != SupplierPurchaseSources.Manual)
            .ToList();

        var relation = NormalizeGroupKey(shein.RelationBillno);
        if (relation is not null &&
            purchases.Any(p => string.Equals(NormalizeGroupKey(p.RelationBillno), relation, StringComparison.Ordinal)))
        {
            reason = "shared-relation-billno";
            return true;
        }

        var confluence = NormalizeGroupKey(shein.ConfluenceBatchNo);
        if (confluence is not null &&
            purchases.Any(p => string.Equals(NormalizeGroupKey(p.ConfluenceBatchNo), confluence, StringComparison.Ordinal)))
        {
            reason = "shared-confluence-batch";
            return true;
        }

        if (!HasRoomForPurchase(order))
        {
            reason = OrderCapacityFullReason;
            return false;
        }

        var uncoveredOverlap = 0;
        foreach (var item in order.Items ?? [])
        {
            var itemKeys = OrderItemProductIdKeys(item).ToList();
            if (itemKeys.Count == 0) continue;
            var itemCovered = itemKeys.Any(covered.Contains);
            var overlapsShein = itemKeys.Any(sheinKeys.Contains);
            if (overlapsShein && !itemCovered) uncoveredOverlap++;
        }

        if (uncoveredOverlap > 0)
        {
            reason = "sku-uncovered";
            return true;
        }

        // Hamming ≤ 2 chỉ được dùng khi SHEIN đã xác nhận đây là Joint Shipment. Thân billno gần như
        // tuần tự nên hai đơn **không liên quan** của cùng tài khoản trong cùng ngày rất dễ lệch ≤ 2
        // ký tự, và gắn thêm dựa trên đó là cộng chi phí của một đơn SHEIN khác vào đơn sàn này.
        // "Nhiều món" không phải bằng chứng — relation_billno và confluence_batch_no ở trên mới là.
        if (shein.IsJointShipment == true
            && purchases.Any(p => AreNearIdenticalBillnos(p.SupplierOrderNo, shein.Billno)))
        {
            reason = "joint-near-billno";
            return true;
        }

        if (shein.IsJointShipment == true && itemCount >= 2 && sheinKeys.Count > 0)
        {
            // Joint flag + multi-item but SKU already covered — still allow if any shein key
            // is not in covered (partial key mismatch).
            if (sheinKeys.Any(k => !covered.Contains(k)))
            {
                reason = "joint-new-sku-keys";
                return true;
            }
        }

        reason = covered.Count > 0 ? "sku-already-covered" : "no-sku-overlap";
        return false;
    }

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;

    /// <summary>
    /// Bằng chứng near-miss cho một billno không khớp được đơn nào.
    ///
    /// Không có nó thì người xử lý chỉ thấy "không khớp đơn nào" và không biết phải sửa gì: pool có
    /// đơn trùng phone không, trùng zip không, trùng cả hai mà vẫn bị loại vì lý do gì. Ba con số
    /// này trả lời đúng ba câu hỏi đầu tiên phải hỏi.
    /// </summary>
    private static string BuildSheinSkipEvidence(
        SheinOrderDto shein,
        IReadOnlyList<Order> pool,
        IReadOnlyList<Order> matchPool,
        string sheinPhone,
        string sheinZip,
        bool isUpdateAll,
        Guid? accountId)
    {
        var phoneMatches = 0;
        var zipMatches = 0;
        var bothMatches = 0;
        var rejectedByCausality = 0;
        var bothSamples = new List<string>();

        foreach (var order in pool)
        {
            var phoneHit = sheinPhone.Length > 0 && PhoneKey(order.BuyerPhone) == sheinPhone;
            var zipHit = sheinZip.Length > 0 && ZipKey(order.PostalCode) == sheinZip;
            if (phoneHit) phoneMatches++;
            if (zipHit) zipMatches++;
            if (!phoneHit || !zipHit) continue;

            bothMatches++;
            if (IsPurchasedBeforePlatformOrder(shein, order)) rejectedByCausality++;
            if (bothSamples.Count < 3)
            {
                bothSamples.Add(order.PlatformOrderId ?? order.FourSellerOrderId.ToString());
            }
        }

        return JsonSerializer.Serialize(
            new
            {
                poolSize = pool.Count,
                matchPoolSize = matchPool.Count,
                accountFilterApplied = !isUpdateAll && accountId.HasValue,
                sheinPhoneKey = sheinPhone.Length == 0 ? null : sheinPhone,
                sheinZipKey = sheinZip.Length == 0 ? null : sheinZip,
                phoneMatches,
                zipMatches,
                bothMatches,
                rejectedByCausality,
                bothSamples
            },
            LinkJsonOptions);
    }

    private static string? NormalizeGroupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant();
    }

    private static bool OrderHasSupplierPurchase(Order order, string billno) =>
        (order.SupplierPurchases ?? []).Any(p =>
            string.Equals(p.SupplierOrderNo, billno, StringComparison.OrdinalIgnoreCase));

    private static SupplierPurchase? FindSupplierPurchase(Order order, string billno) =>
        (order.SupplierPurchases ?? []).FirstOrDefault(p =>
            string.Equals(p.SupplierOrderNo, billno, StringComparison.OrdinalIgnoreCase));

    private static void EnsureSupplierPurchases(Order order)
    {
        order.SupplierPurchases ??= new List<SupplierPurchase>();
    }

    private static void RefreshOrderSupplierAggregates(Order order)
    {
        EnsureSupplierPurchases(order);
        OrderSupplierCost.ApplyAggregates(order);
    }

    /// <summary>
    /// Gắn một lần mua đã ghi nhận vào đơn sàn vừa khớp.
    ///
    /// Mọi trường phía NCC do <see cref="SupplierPurchaseIngest"/> ghi trước đó, nên hàm này chỉ còn
    /// việc của phía đơn: đổi chủ, tính lại cờ <c>IsCharged</c> và stamp mốc fulfil. Đổi chủ là một
    /// phép gán <c>OrderId</c> — bản cũ phải DELETE rồi INSERT lại vì bảng con buộc mỗi dòng thuộc
    /// một đơn, và chính vòng xoá-thêm đó làm mất lịch sử mỗi lượt sync.
    /// </summary>
    private SupplierPurchase AttachSupplierPurchase(
        Order order,
        SupplierPurchase purchase,
        SheinOrderDto shein,
        DateTime now,
        Guid actingUserId)
    {
        EnsureSupplierPurchases(order);
        SupplierPurchaseIngest.Attach(purchase, order, now, actingUserId);
        RefreshOrderSupplierAggregates(order);
        ApplySheinStatusMilestones(order, purchase, shein);
        return purchase;
    }

    /// <summary>
    /// Stamps the fulfil timeline from the SHEIN lifecycle using per-milestone real clocks
    /// (Shein rank). No real clock for a stage ⇒ that milestone stays empty (the UI shows the
    /// stage reached-but-undated instead of an inaccurate gap). Ordering is repaired by
    /// <see cref="OrderFulfilTimeline.Normalize"/>. Does not change platform <c>PlatformOrderStatus</c>.
    ///
    /// Commerce terminal (Hủy/Refund trên timeline) chỉ thuộc Sync 4Seller —
    /// không stamp <c>RefundedAt</c>/<c>CanceledAt</c> từ SHEIN hoàn NCC (tránh timeline "Refund"
    /// trong khi TikTok vẫn Đang giao/Đã giao). Trạng thái terminal vẫn ghi trên
    /// <c>SupplierPurchases.SupplierStatus</c>.
    /// </summary>
    private static void ApplySheinStatusMilestones(
        Order order,
        SupplierPurchase purchase,
        SheinOrderDto shein)
    {
        var group = NormalizeSheinStatusGroup(shein.SheinStatusGroup)
            ?? NormalizeSheinStatusGroup(purchase.SupplierStatus);
        if (group is null) return;

        // Chỉ dùng clock per-chặng từ extension — không fallback SheinStatusAt
        // (extractSheinStatus có thể gắn update_time ≈ giờ sync, đúng thứ ta đang loại bỏ).
        DateTime? Utc(DateTime? v) => v.HasValue ? NormalizeUtc(v.Value) : null;

        var shippedAt = Utc(shein.SheinShippedAt);
        var deliveredAt = Utc(shein.SheinDeliveredAt);
        // Không fallback sang shippedAt: SHEIN không có clock gán mã vận đơn, lấy mốc rời kho thay
        // thế sẽ khiến Track và Transit luôn bằng nhau (đúng triệu chứng hai cột trùng nhau trên
        // UI). Thiếu clock thì để trống, 4Seller sở hữu mốc này qua OrderShippedTime.
        var trackingAt = Utc(shein.SheinTrackingAt);

        const OrderFulfilTimeline.Source src = OrderFulfilTimeline.Source.Shein;

        switch (group)
        {
            case SupplierStatusGroups.Shipped:
            case SupplierStatusGroups.Delivered:
                if (purchase.TrackingKind == SupplierTrackingKinds.Carrier
                    && !string.IsNullOrWhiteSpace(purchase.TrackingNo))
                {
                    OrderFulfilTimeline.Stamp(order, OrderFulfilTimeline.Milestone.TrackingAdded, trackingAt, src);
                }

                // SHEIN "shipped" ≈ in transit. Với group=delivered vẫn stamp Transit nếu có
                // sheinShippedAt riêng (extract một lượt) — thiếu thì để trống, không bịa.
                OrderFulfilTimeline.Stamp(order, OrderFulfilTimeline.Milestone.InTransit, shippedAt, src);

                if (group == SupplierStatusGroups.Delivered)
                {
                    OrderFulfilTimeline.Stamp(order, OrderFulfilTimeline.Milestone.Delivered, deliveredAt, src);
                }

                break;
            case SupplierStatusGroups.Canceled:
            case SupplierStatusGroups.Refunded:
                // Không stamp order-level Hủy/Refund — commerce terminal chỉ từ 4Seller.
                break;
        }

        OrderFulfilTimeline.Normalize(order);
    }

    internal static string? NormalizeSheinStatusGroup(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var g = raw.Trim().ToLowerInvariant();
        return g switch
        {
            SupplierStatusGroups.Processing => SupplierStatusGroups.Processing,
            SupplierStatusGroups.Shipped => SupplierStatusGroups.Shipped,
            SupplierStatusGroups.Delivered => SupplierStatusGroups.Delivered,
            SupplierStatusGroups.Canceled => SupplierStatusGroups.Canceled,
            "cancelled" => SupplierStatusGroups.Canceled,
            SupplierStatusGroups.Refunded => SupplierStatusGroups.Refunded,
            _ => null
        };
    }

    private sealed record AutoMarkShipCandidate(
        Order Order,
        SupplierPurchase Link,
        string Billno,
        DateTime? TrackingAt);

    /// <summary>
    /// Ảnh chụp một dòng mua <b>trước</b> bước ghi nhận.
    ///
    /// Bước ghi nhận chạy trước mọi guard nên nó đã áp payload lên dòng mua; so dòng mua với payload
    /// sau đó luôn ra "không đổi", và chế độ Chỉ đơn mới sẽ bỏ qua đúng những billno vừa có thay đổi
    /// thật.
    /// </summary>
    internal sealed record SheinPurchaseSnapshot(
        bool Existed,
        string? TrackingNo,
        string? SupplierStatus,
        string? SupplierStatusRaw,
        decimal? SupplierSubtotal,
        decimal? SupplierTotal,
        decimal? SupplierCouponPrice,
        decimal? SupplierWalletPrice,
        decimal? SupplierMembershipDiscount,
        decimal? SupplierPointsDiscount)
    {
        public static SheinPurchaseSnapshot Of(SupplierPurchase? purchase) =>
            purchase is null
                ? new SheinPurchaseSnapshot(false, null, null, null, null, null, null, null, null, null)
                : new SheinPurchaseSnapshot(
                    true,
                    purchase.TrackingNo,
                    purchase.SupplierStatus,
                    purchase.SupplierStatusRaw,
                    purchase.SupplierSubtotal,
                    purchase.SupplierTotal,
                    purchase.SupplierCouponPrice,
                    purchase.SupplierWalletPrice,
                    purchase.SupplierMembershipDiscount,
                    purchase.SupplierPointsDiscount);
    }

    /// <summary>
    /// Total = 0 + coupon &gt; 0 + wallet = 0 — pattern của đơn trả bằng ví mà sync cũ đổ số ví vào coupon.
    /// </summary>
    internal static bool LooksLikeWalletMisattributed(
        decimal? total,
        decimal? coupon,
        decimal? wallet) =>
        total is 0m
        && (coupon ?? 0m) > 0m
        && (wallet ?? 0m) == 0m;

    /// <summary>Whether mode <c>new</c> should update an already-linked billno (tracking/status/total/milestone/wallet delta).</summary>
    internal static bool HasSheinIncrementalDelta(
        SupplierPurchase purchase,
        SheinPurchaseSnapshot before,
        SheinOrderDto shein,
        Order order)
    {
        if (!before.Existed) return true;

        // Stored terminal đã được ApplyStatus phục hồi → phải đi tiếp Attach/Reconcile
        // để bật IsCharged và dựng dòng CostUpdate. So incoming group thì cả hai đều
        // "refunded" nên nhánh status bên dưới không thấy delta.
        if (OrderSupplierCost.IsTerminalStatus(before.SupplierStatus)
            && !OrderSupplierCost.IsTerminalStatus(purchase.SupplierStatus))
        {
            return true;
        }

        if (OrderAutoMarkShipPolicy.ShouldAttempt(order, purchase, DateTime.UtcNow)) return true;

        // Link còn requestId treo / đang `unconfirmed` chặn mọi lần publish sau, nên `ShouldAttempt`
        // ở trên luôn false. Không cho nó đi tiếp thì vòng khớp `continue` trước cả bước xếp hàng
        // reconcile — mà mã vận đơn lúc này đã đứng yên nên không còn delta nào khác đánh thức nó.
        if (OrderAutoMarkShipPolicy.NeedsReconcile(purchase)
            || string.Equals(
                purchase.AutoMarkShipStatus,
                AutoMarkShipStatuses.Unconfirmed,
                StringComparison.Ordinal))
        {
            return true;
        }

        // Linked SHEIN billno proves purchase — force update only when we have a real place-time
        // to stamp (otherwise leave empty; don't churn forever).
        if (order.FulfilPurchasedAt is null && shein.OrderedAt.HasValue) return true;

        // Prefer SHEIN place-time over earlier sync-time / legacy stamp.
        if (shein.OrderedAt.HasValue
            && order.FulfilPurchasedAt > NormalizeUtc(shein.OrderedAt.Value))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shein.TrackingNo)
            && !string.Equals(before.TrackingNo?.Trim(), shein.TrackingNo.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var incomingGroup = NormalizeSheinStatusGroup(shein.SheinStatusGroup);
        if (incomingGroup is not null
            && !string.Equals(before.SupplierStatus, incomingGroup, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shein.SheinStatus)
            && !string.Equals(before.SupplierStatusRaw, shein.SheinStatus.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Tiền thẻ: kể cả null → có số (detail lần đầu). Trước đây đòi cả hai HasValue
        // nên Upsert đã ghi tiền nhưng skip Attach/Reconcile → Charged* + ledger stale.
        if (shein.Total.HasValue
            && (!before.SupplierTotal.HasValue
                || shein.Total.Value != before.SupplierTotal.Value))
        {
            return true;
        }

        // Ví / coupon / club / points: chỉ khi payload mang số (tránh list sparse null→0 churn).
        if (shein.WalletPrice.HasValue
            && Math.Abs(shein.WalletPrice.Value - (before.SupplierWalletPrice ?? 0m)) >= 0.005m)
        {
            return true;
        }

        if (shein.CouponPrice.HasValue
            && Math.Abs(shein.CouponPrice.Value - (before.SupplierCouponPrice ?? 0m)) >= 0.005m)
        {
            return true;
        }

        if (shein.ClubDiscount.HasValue
            && Math.Abs(shein.ClubDiscount.Value - (before.SupplierMembershipDiscount ?? 0m)) >= 0.005m)
        {
            return true;
        }

        if (shein.PointsDiscount.HasValue
            && Math.Abs(shein.PointsDiscount.Value - (before.SupplierPointsDiscount ?? 0m)) >= 0.005m)
        {
            return true;
        }

        // Breakdown vừa đủ sau detail — ép Reconcile dù total/coupon không đổi (sổ cái snapshot).
        if (purchase.SupplierSubtotal.HasValue && !before.SupplierSubtotal.HasValue)
        {
            return true;
        }

        // Real per-milestone clocks that this payload can contribute (gate the "missing milestone"
        // checks on actually having a clock, so orders that legitimately lack one don't churn every sync).
        DateTime? Utc(DateTime? v) => v.HasValue ? NormalizeUtc(v.Value) : null;
        var shippedClock = Utc(shein.SheinShippedAt);
        var deliveredClock = Utc(shein.SheinDeliveredAt);
        var trackingClock = Utc(shein.SheinTrackingAt) ?? shippedClock;

        // Terminal SHEIN status but local milestone still missing / a later (wrong) clock → force update.
        if (incomingGroup == SupplierStatusGroups.Delivered)
        {
            if (order.DeliveredAt is null && deliveredClock.HasValue) return true;
            if (deliveredClock.HasValue && order.DeliveredAt > deliveredClock.Value) return true;
            if (order.InTransitAt is null && shippedClock.HasValue) return true;
        }

        if (incomingGroup == SupplierStatusGroups.Shipped
            && order.InTransitAt is null && shippedClock.HasValue)
        {
            return true;
        }

        // Carrier last-mile present, a real clock available, but Track milestone still missing.
        if (order.TrackingAddedAt is null
            && trackingClock.HasValue
            && !string.IsNullOrWhiteSpace(shein.TrackingNo)
            && ClassifySheinTracking(shein.TrackingNo, shein.Carrier) == SupplierTrackingKinds.Carrier)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Billno lần đầu vào bảng mua: xếp hàng <c>get_order_info</c> và hoãn khớp trên phase list.
    /// List sparse thiếu breakdown/<c>order_type</c>; khớp sớm dễ gắn Club rồi gỡ hoặc sổ cái sparse.
    ///
    /// Bỏ qua khi list đã chứng minh unpaid ghost hoặc terminal (refunded/canceled) — không tốn
    /// request SHEIN. Membership/virtual đã biết từ payload cũng bỏ (không cần detail để loại).
    /// Membership chưa chắc (thiếu order_type) → xếp hàng; phase detail gắn cờ rồi mới quyết khớp.
    /// </summary>
    internal static bool ShouldQueueFirstSeenDetail(
        bool purchaseExistedBeforeUpsert,
        SupplierPurchase purchase,
        SheinOrderDto shein)
    {
        if (purchaseExistedBeforeUpsert) return false;
        if (purchase.MatchState == SupplierPurchaseMatchStates.Ignored) return false;
        // Đã biết Club/Saver từ payload — không tốn detail. List thường thiếu order_type nên cờ
        // còn false → vẫn xếp hàng; phase detail gắn cờ rồi bỏ khớp.
        if (purchase.IsMembershipOrVirtual) return false;
        if (IsUnpaidGhostBillno(purchase, shein)) return false;
        if (IsTerminalSupplierStatus(NormalizeSheinStatusGroup(shein.SheinStatusGroup))) return false;
        return true;
    }

    /// <summary>
    /// Mã lý do phase <c>list</c> phải gọi <c>get_order_info</c> cho billno này, hoặc <c>null</c>
    /// khi list đã đủ. List đã cập nhật trạng thái/tracking; detail chỉ dành cho lần mua mới, giá
    /// thiếu, total lệch, thiếu mã vận đơn chặng cuối sau khi rời kho, hoặc thiếu clock mốc mà
    /// payload list không mang theo.
    ///
    /// Trả về mã thay vì <c>bool</c> vì detail là phần đắt nhất của một lượt sync (một request
    /// SHEIN mỗi billno): không biết điều kiện nào bật thì không đánh giá được gate có siết đúng
    /// hay đang kéo lại toàn bộ đơn mỗi lượt.
    ///
    /// <paramref name="forceDetail"/> dành cho chế độ "Cập nhật tất cả": nó tồn tại để dựng lại
    /// breakdown giá từ dữ liệu SHEIN mới, mà chỉ payload detail mới cho phép ghi đè toàn bộ
    /// (<c>SupplierPurchaseIngest</c> chỉ full-replace ở phase detail), nên mọi billno đã khớp đều
    /// phải qua <c>get_order_info</c>.
    ///
    /// Billno lần đầu (chưa từng có dòng mua) được xếp hàng riêng qua
    /// <see cref="ShouldQueueFirstSeenDetail"/> trước vòng khớp — không đi qua hàm này.
    /// </summary>
    internal static string? EvaluateSheinListNeedsDetail(
        Order order,
        SupplierPurchase purchase,
        SheinOrderDto shein,
        bool billnoLinked,
        decimal? prevLinkTotal,
        bool forceDetail = false)
    {
        // 1) New match / new joint purchase — need detail for cost breakdown.
        if (billnoLinked) return SheinNeedsDetailReasons.NewLink;

        if (forceDetail) return SheinNeedsDetailReasons.ModeAll;

        // 2) Cost incomplete after list merge.
        if (purchase.SupplierSubtotal is null || purchase.SupplierTotal is null)
        {
            return SheinNeedsDetailReasons.CostIncomplete;
        }

        // 2b) Ví bị đổ vào coupon (totalPrice=0 + coupon>0 + wallet=0) — cần detail đọc usedWalletPrice.
        if (LooksLikeWalletMisattributed(
                purchase.SupplierTotal,
                purchase.SupplierCouponPrice,
                purchase.SupplierWalletPrice))
        {
            return SheinNeedsDetailReasons.WalletCostStale;
        }

        // 3) Total changed vs previously stored total — reconcile breakdown via detail.
        if (shein.Total.HasValue
            && prevLinkTotal.HasValue
            && shein.Total.Value != prevLinkTotal.Value)
        {
            return SheinNeedsDetailReasons.TotalDelta;
        }

        var group = NormalizeSheinStatusGroup(shein.SheinStatusGroup);

        // 4) Shipped/delivered but still missing last-mile carrier tracking.
        if ((group == SupplierStatusGroups.Shipped || group == SupplierStatusGroups.Delivered)
            && NeedsCarrierTrackingFromDetail(purchase, shein))
        {
            return SheinNeedsDetailReasons.NoCarrierTracking;
        }

        // 5) Terminal/shipped status missing a milestone clock that list did not supply.
        if (NeedsMilestoneClockFromDetail(order, shein, group))
        {
            return SheinNeedsDetailReasons.MissingMilestoneClock;
        }

        return null;
    }

    /// <summary>
    /// True when status implies a parcel left the warehouse but we only have empty/warehouse
    /// tracking — <c>get_order_info</c> is authoritative for last-mile carrier numbers.
    /// </summary>
    internal static bool NeedsCarrierTrackingFromDetail(SupplierPurchase purchase, SheinOrderDto shein)
    {
        // Prefer the post-upsert classification; fall back to payload when the row has none yet.
        var kind = purchase.TrackingKind;
        if (string.IsNullOrWhiteSpace(kind) || kind == SupplierTrackingKinds.None)
        {
            kind = ClassifySheinTracking(shein.TrackingNo, shein.Carrier);
        }

        return kind != SupplierTrackingKinds.Carrier;
    }

    /// <summary>
    /// True when local timeline still lacks a required clock and the list payload also lacks it
    /// (detail may still have it). If list already carried the clock, list-phase upsert stamped it.
    /// </summary>
    internal static bool NeedsMilestoneClockFromDetail(
        Order order,
        SheinOrderDto shein,
        string? group)
    {
        if (group is null) return false;

        DateTime? Utc(DateTime? v) => v.HasValue ? NormalizeUtc(v.Value) : null;
        var shippedClock = Utc(shein.SheinShippedAt);
        var deliveredClock = Utc(shein.SheinDeliveredAt);

        if (group == SupplierStatusGroups.Delivered)
        {
            if (order.DeliveredAt is null && deliveredClock is null) return true;
            if (order.InTransitAt is null && shippedClock is null) return true;
        }

        if (group == SupplierStatusGroups.Shipped
            && order.InTransitAt is null
            && shippedClock is null)
        {
            return true;
        }

        // Canceled/Refunded: không còn stamp order-level terminal từ SHEIN → không xếp detail vì thiếu mốc đó.
        return false;
    }
}
