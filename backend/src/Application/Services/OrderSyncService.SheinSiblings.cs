using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Services.Shein;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using static Application.Services.SheinMatchKeys;

namespace Application.Services;

/// <summary>
/// Phân phối billno SHEIN giữa nhiều đơn sàn của <b>cùng một khách</b> cho
/// <see cref="OrderSyncService"/>.
///
/// Một khách đặt hai đơn riêng về cùng địa chỉ là chuyện thường ngày, và khi hai đơn đó còn trùng
/// cả SKU thì mọi khoá khớp (phone, zip, tên, sku) đều bằng nhau — phần chấm điểm không có cách nào
/// tách chúng ra. Hệ quả: mọi billno của khách đổ vào đúng một đơn, đơn còn lại vĩnh viễn không có
/// giá vốn (ca đơn 577577266831396921 ôm hai billno trong khi 577577053923479609 trống).
///
/// Khoá phân giải ưu tiên: <b>dấu «Đã mua»</b> (OrderFulfilAttempt / FulfilPurchasedAt Manual) khớp
/// mốc trả tiền SHEIN — bằng chứng trực tiếp billno thuộc đơn nào. Chỉ khi nhóm khách không ai bấm
/// đơn nào mới rơi về suy đoán tuần tự theo <b>số chỗ còn lại</b> (một đơn sàn n dòng hàng chỉ cần
/// tối đa n lần mua SHEIN còn hiệu lực; đơn đã đủ thì billno tiếp theo thuộc về đơn anh em còn trống).
/// </summary>
public partial class OrderSyncService
{
    /// <summary>
    /// Lần mua còn chiếm chỗ trên đơn: dòng SHEIN đã trả tiền và chưa huỷ/hoàn.
    ///
    /// Dòng nhập tay không tính — nó là con số người đi đơn gõ vào để chốt giá vốn, không phải một
    /// lần mua ở NCC, và chặn billno thật vì đơn "đã có manual" sẽ làm luồng sync mất dấu lần mua.
    /// Billno huỷ/hoàn cũng không tính: mua lại sau khi huỷ chính là billno mới phủ chỗ cũ.
    /// </summary>
    internal static bool IsLiveSupplierPurchase(SupplierPurchase purchase) =>
        !string.Equals(purchase.Source, SupplierPurchaseSources.Manual, StringComparison.OrdinalIgnoreCase)
        && !purchase.SupplierUnpaid
        && !IsTerminalSupplierStatus(purchase.SupplierStatus);

    /// <summary>Số lần mua NCC còn hiệu lực đang gắn trên đơn.</summary>
    internal static int LivePurchaseCount(Order order)
    {
        var count = 0;
        foreach (var purchase in order.SupplierPurchases ?? [])
        {
            if (IsLiveSupplierPurchase(purchase)) count++;
        }
        return count;
    }

    /// <summary>
    /// Trần số lần mua SHEIN hợp lý của một đơn sàn: mỗi dòng hàng cần nhiều nhất một lần mua.
    ///
    /// Đây là <b>trần trên</b>, không phải yêu cầu — một billno gộp cả hai dòng hàng vẫn hợp lệ.
    /// Đơn chưa đồng bộ được dòng hàng nào vẫn được một chỗ để không khoá nhầm luồng khớp.
    /// </summary>
    internal static int PurchaseCapacity(Order order) => Math.Max(1, order.Items?.Count ?? 0);

    /// <summary>Đơn còn nhận thêm được một lần mua SHEIN nữa hay không.</summary>
    internal static bool HasRoomForPurchase(Order order) =>
        LivePurchaseCount(order) < PurchaseCapacity(order);

    /// <summary>
    /// Sắp billno theo mốc mua tăng dần rồi theo mã — cặp "đơn đặt sớm ← billno mua sớm" tất định.
    /// </summary>
    internal static void SortSheinOrdersForMatch(List<SheinOrderDto> orders)
    {
        orders.Sort(static (a, b) =>
        {
            var ta = a.SheinPaidAt ?? a.OrderedAt;
            var tb = b.SheinPaidAt ?? b.OrderedAt;
            if (ta is null && tb is null)
            {
                return string.Compare(a.Billno, b.Billno, StringComparison.OrdinalIgnoreCase);
            }

            if (ta is null) return 1;
            if (tb is null) return -1;

            var byTime = NormalizeUtc(ta.Value).CompareTo(NormalizeUtc(tb.Value));
            return byTime != 0
                ? byTime
                : string.Compare(a.Billno, b.Billno, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Nhãn đơn dùng trong Notes / SkipOutcomes.</summary>
    internal static string OrderLabel(Order order) =>
        order.PlatformOrderId ?? order.FourSellerOrderId.ToString();

    /// <summary>Ghi skip <c>sibling_linked</c> khi đơn đã đủ hàng và không còn anh em trống.</summary>
    private static void MarkSiblingLinkedSkip(
        SheinOrderSyncResultDto result,
        SupplierPurchase purchase,
        string billno,
        Order occupied,
        string attachReason,
        DateTime now)
    {
        var existing = string.Join(
            ", ",
            (occupied.SupplierPurchases ?? [])
                .Select(p => p.SupplierOrderNo ?? "(nhập tay)")
                .Take(3));
        SupplierPurchaseIngest.MarkUnmatched(
            purchase, SheinSkipReasons.SiblingLinked, null, now);
        AddSheinSkip(
            result,
            billno,
            SheinSkipReasons.SiblingLinked,
            $"đơn {OrderLabel(occupied)} đã gắn {existing} ({attachReason})");
        result.Notes.Add(
            $"SHEIN {billno}: cùng khách đã gắn {existing}"
            + $" (đơn {OrderLabel(occupied)}) — bỏ qua ({attachReason}).");
    }

    /// <summary>
    /// Lý do <c>CanAttachAdditionalPurchase</c> từ chối vì đơn đã đủ lần mua cho số dòng hàng của
    /// nó. Tách khỏi <c>sku-already-covered</c> vì hai ca cần hai cách xử lý khác nhau: ca này có
    /// đích rõ ràng (đơn anh em còn trống), ca kia thì không.
    /// </summary>
    internal const string OrderCapacityFullReason = "order-capacity-full";

    /// <summary>
    /// Bằng chứng SHEIN xác nhận hai billno thuộc cùng một lần giao — được phép vượt trần
    /// <see cref="PurchaseCapacity"/> vì chúng là một lần mua bị tách gói, không phải hai lần mua.
    /// </summary>
    private static bool SharesSheinShipmentGroup(Order order, SupplierPurchase purchase)
    {
        var relation = NormalizeGroupKey(purchase.RelationBillno);
        var confluence = NormalizeGroupKey(purchase.ConfluenceBatchNo);
        if (relation is null && confluence is null) return false;

        foreach (var other in order.SupplierPurchases ?? [])
        {
            if (other.Id == purchase.Id) continue;
            if (!IsLiveSupplierPurchase(other)) continue;

            if (relation is not null
                && string.Equals(NormalizeGroupKey(other.RelationBillno), relation, StringComparison.Ordinal))
            {
                return true;
            }

            if (confluence is not null
                && string.Equals(NormalizeGroupKey(other.ConfluenceBatchNo), confluence, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Đơn anh em cùng khách còn chỗ cho billno này — đích chuyển hướng khi đơn được chấm điểm cao
    /// nhất đã đủ hàng.
    ///
    /// Ưu tiên dấu «Đã mua» khớp mốc trả tiền (E1/E2); không có thì xếp theo
    /// <see cref="ComparePurchaseDemand"/> (ít lần mua → đặt sớm → Id). Mỗi đơn chỉ nhận một billno
    /// mới trong một lượt (<paramref name="claimedThisPass"/>), kể cả khi còn dư chỗ theo số dòng hàng.
    /// </summary>
    /// <returns>Đơn đích + <c>byPurchaseMark</c> = true khi chọn theo dấu Đã mua (để Notes phân biệt).</returns>
    internal static (Order? Order, bool ByPurchaseMark) FindSiblingWithRoom(
        IReadOnlyList<Order> pool,
        SheinOrderDto shein,
        string sheinPhone,
        string sheinZip,
        string billno,
        Order? exclude,
        IReadOnlyCollection<Guid> claimedThisPass,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        if (sheinPhone.Length == 0 || sheinZip.Length == 0) return (null, false);

        var eligible = new List<Order>();
        foreach (var order in pool)
        {
            if (exclude is not null && order.Id == exclude.Id) continue;
            if (claimedThisPass.Contains(order.Id)) continue;
            if (IsPlatformTerminalOrder(order)) continue;
            if (sheinPhone != PhoneKey(order.BuyerPhone)) continue;
            if (sheinZip != ZipKey(order.PostalCode)) continue;
            if (OrderHasSupplierPurchase(order, billno)) continue;
            if (IsPurchasedBeforePlatformOrder(shein, order)) continue;
            if (!HasRoomForPurchase(order)) continue;
            eligible.Add(order);
        }

        if (eligible.Count == 0) return (null, false);

        if (purchaseMarks is not null && !purchaseMarks.IsEmpty)
        {
            var (marked, attempt) = FindOrderByPurchaseMark(
                eligible, shein, purchaseMarks, claimedThisPass);
            if (marked is not null)
            {
                if (attempt is not null
                    && !SharesSheinShipmentGroupPending(marked, shein))
                {
                    purchaseMarks.Consume(attempt.Id);
                }

                return (marked, true);
            }
        }

        Order? best = null;
        foreach (var order in eligible)
        {
            if (best is null || ComparePurchaseDemand(order, best) < 0) best = order;
        }

        return (best, false);
    }

    /// <summary>
    /// Billno sắp gắn có cùng relation/confluence với lần mua đã live trên đơn — joint shipment,
    /// không tiêu thụ attempt (một lần mua tách gói).
    /// </summary>
    private static bool SharesSheinShipmentGroupPending(Order order, SheinOrderDto shein)
    {
        var relation = NormalizeGroupKey(shein.RelationBillno);
        var confluence = NormalizeGroupKey(shein.ConfluenceBatchNo);
        if (relation is null && confluence is null) return false;

        foreach (var other in order.SupplierPurchases ?? [])
        {
            if (!IsLiveSupplierPurchase(other)) continue;
            if (relation is not null
                && string.Equals(NormalizeGroupKey(other.RelationBillno), relation, StringComparison.Ordinal))
            {
                return true;
            }

            if (confluence is not null
                && string.Equals(NormalizeGroupKey(other.ConfluenceBatchNo), confluence, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Thứ tự "đơn nào cần billno hơn": đã bấm Đã mua mà chưa có lần mua → ít lần mua hơn → đặt sớm
    /// hơn → <c>Id</c> để kết quả tất định qua các lượt chạy.
    /// </summary>
    private static int ComparePurchaseDemand(Order left, Order right)
    {
        var byMarked = WaitingMarkedPurchaseRank(left).CompareTo(WaitingMarkedPurchaseRank(right));
        if (byMarked != 0) return byMarked;

        var byLive = LivePurchaseCount(left).CompareTo(LivePurchaseCount(right));
        if (byLive != 0) return byLive;

        var byPlaced = ComparePlacedAt(left, right);
        if (byPlaced != 0) return byPlaced;

        return left.Id.CompareTo(right.Id);
    }

    /// <summary>0 = đã bấm Đã mua (Manual) mà chưa có lần mua — xếp trước; 1 = còn lại.</summary>
    private static int WaitingMarkedPurchaseRank(Order order)
    {
        if (LivePurchaseCount(order) > 0) return 1;
        if (order.FulfilPurchasedAt is null) return 1;
        if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.FulfilPurchased)
            != OrderFulfilTimeline.Source.Manual)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>Đơn có <c>OrderCreateTime</c> cũ hơn đứng trước; đơn thiếu mốc xếp cuối.</summary>
    private static int ComparePlacedAt(Order left, Order right)
    {
        if (left.OrderCreateTime is null && right.OrderCreateTime is null) return 0;
        if (left.OrderCreateTime is null) return 1;
        if (right.OrderCreateTime is null) return -1;
        return NormalizeUtc(left.OrderCreateTime.Value)
            .CompareTo(NormalizeUtc(right.OrderCreateTime.Value));
    }

    /// <summary>
    /// Phân giải thế hoà bằng dấu «Đã mua» rồi "đơn nào còn trống" — dùng được ở <b>mọi</b> chế độ
    /// cập nhật.
    ///
    /// Bỏ qua khi hoà là lựa chọn đúng cho những tiêu chí có thể gắn nhầm tiền sang đơn của lần mua
    /// khác. Tiêu chí này thì không: gắn vào một đơn chưa có lần mua nào không tước billno của ai,
    /// và không gắn mới là thứ khoá vĩnh viễn cả hai đơn của một khách đặt hai lần.
    ///
    /// Trả <c>null</c> khi không đơn nào còn chỗ, hoặc khi hai đơn cần nhất có cùng mốc đặt hàng và
    /// không có dấu bấm — lúc đó thật sự không có căn cứ nào để chọn.
    /// </summary>
    internal static Order? ResolveTiedByPurchaseDemand(
        List<Order> tied,
        SheinOrderDto? shein = null,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        var withRoom = tied.Where(HasRoomForPurchase).ToList();
        if (withRoom.Count == 0) return null;
        if (withRoom.Count == 1) return withRoom[0];

        if (shein is not null && purchaseMarks is not null && !purchaseMarks.IsEmpty)
        {
            var (marked, attempt) = FindOrderByPurchaseMark(withRoom, shein, purchaseMarks);
            if (marked is not null)
            {
                if (attempt is not null && !SharesSheinShipmentGroupPending(marked, shein))
                {
                    purchaseMarks.Consume(attempt.Id);
                }

                return marked;
            }
        }

        var ranked = withRoom.OrderBy(o => o, Comparer<Order>.Create(ComparePurchaseDemand)).ToList();
        var first = ranked[0];
        var second = ranked[1];

        if (WaitingMarkedPurchaseRank(first) != WaitingMarkedPurchaseRank(second)) return first;
        if (LivePurchaseCount(first) != LivePurchaseCount(second)) return first;
        if (ComparePlacedAt(first, second) != 0) return first;

        // Cùng số lần mua và cùng mốc đặt hàng — không có gì để chọn, trả về hàng đợi.
        return null;
    }

    /// <summary>
    /// Xác nhận lại trên DB rằng đơn vẫn còn chỗ trước khi gắn một billno mới.
    ///
    /// Pool của một lượt sync được nạp một lần rồi dùng suốt lượt, nhưng lượt sync nền và lượt đẩy
    /// từ extension chạy chồng nhau: hai lượt cùng nạp pool trước khi lượt kia <c>SaveChanges</c> thì
    /// cả hai đều thấy đơn còn trống và cùng gắn vào đó. Đúng cơ chế đã tạo ra hai billno trên đơn
    /// 577577266831396921 (hai mốc <c>MatchedAt</c> cách nhau 2,5 giây).
    ///
    /// Với mọi dòng mua mà lượt này đang theo dõi, bộ nhớ là nguồn đúng (nó biết cả attach lẫn detach
    /// chưa lưu); chỉ những dòng DB mà lượt này chưa từng chạm mới là chữ ký của lượt sync khác.
    /// </summary>
    private async Task<bool> HasRoomForPurchaseInDbAsync(
        Order order,
        SupplierPurchase incoming,
        CancellationToken cancellationToken)
    {
        var committed = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId == order.Id && p.Id != incoming.Id)
            .Select(p => new { p.Id, p.Source, p.SupplierUnpaid, p.SupplierStatus })
            .ToListAsync(cancellationToken);

        var trackedIds = _db.SupplierPurchases.Local.Select(p => p.Id).ToHashSet();

        var live = 0;
        foreach (var purchase in order.SupplierPurchases ?? [])
        {
            if (purchase.Id == incoming.Id) continue;
            if (IsLiveSupplierPurchase(purchase)) live++;
        }

        foreach (var row in committed)
        {
            if (trackedIds.Contains(row.Id)) continue;
            if (string.Equals(row.Source, SupplierPurchaseSources.Manual, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.SupplierUnpaid) continue;
            if (IsTerminalSupplierStatus(row.SupplierStatus)) continue;
            live++;
        }

        return live < PurchaseCapacity(order);
    }

    /// <summary>
    /// Dọn hậu quả của những lượt sync trước: đơn đang ôm nhiều lần mua hơn số dòng hàng của nó thì
    /// trả bớt billno cho đơn anh em cùng khách còn trống.
    ///
    /// Chỉ di chuyển khi có đích rõ ràng. Không có đơn anh em nào thì <b>giữ nguyên</b> và chỉ ghi
    /// cảnh báo: gỡ về hàng đợi sẽ xoá giá vốn của một đơn đang chạy để đổi lấy một dòng trong hàng
    /// đợi, tức là làm hỏng thêm chứ không sửa được gì.
    ///
    /// Idempotent: sau một lần chuyển, đơn nguồn về đúng trần nên lượt sau không còn điều kiện kích
    /// hoạt, và đơn đích đã đủ chỗ nên không nhận tiếp.
    /// </summary>
    private async Task<int> RebalanceOverfilledOrdersAsync(
        IReadOnlyList<Order> pool,
        SheinOrderSyncResultDto result,
        HashSet<Guid> ordersNeedingCostRefresh,
        HashSet<Guid> claimedThisPass,
        Guid actingUserId,
        DateTime now,
        CancellationToken cancellationToken,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        var moved = 0;

        foreach (var order in pool)
        {
            if (LivePurchaseCount(order) <= PurchaseCapacity(order)) continue;

            var phoneKey = PhoneKey(order.BuyerPhone);
            var zipKey = ZipKey(order.PostalCode);
            if (phoneKey.Length == 0 || zipKey.Length == 0) continue;

            while (LivePurchaseCount(order) > PurchaseCapacity(order))
            {
                var surplus = SelectSurplusPurchase(order, purchaseMarks);
                if (surplus is null) break;

                var (target, byMark) = FindRebalanceTarget(
                    pool, order, surplus, phoneKey, zipKey, claimedThisPass, purchaseMarks);
                if (target is null)
                {
                    result.Notes.Add(
                        $"Đơn {OrderLabel(order)}: đang giữ {LivePurchaseCount(order)} lần mua NCC cho"
                        + $" {PurchaseCapacity(order)} dòng hàng — chưa tìm được đơn cùng khách còn trống để"
                        + " chuyển bớt, giữ nguyên.");
                    break;
                }

                var sourceLabel = OrderLabel(order);
                await DetachPurchaseFromOrderAsync(
                    pool, surplus, SheinSkipReasons.RebalancedToSibling, now, cancellationToken);

                MovePurchaseToOrder(target, surplus, now, actingUserId);

                ordersNeedingCostRefresh.Add(order.Id);
                ordersNeedingCostRefresh.Add(target.Id);
                claimedThisPass.Add(target.Id);
                moved++;
                result.Rebalanced++;

                var reason = byMark
                    ? "khớp dấu Đã mua"
                    : "đơn cùng khách còn trống";
                result.Notes.Add(
                    $"SHEIN {surplus.SupplierOrderNo}: đơn {sourceLabel} đã đủ lần mua cho số dòng hàng"
                    + $" — chuyển sang đơn {OrderLabel(target)} ({reason}).");
            }
        }

        return moved;
    }

    /// <summary>
    /// Billno phải rời đơn: lần mua mới nhất chưa có bằng chứng SHEIN gộp gói với các dòng còn lại,
    /// và chưa khớp E1 với chính đơn đang giữ (dấu bấm = sự thật của người).
    /// Chọn dòng mới nhất vì dòng cũ nhất mới là lần mua khớp với đơn này từ đầu.
    /// </summary>
    internal static SupplierPurchase? SelectSurplusPurchase(
        Order order,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        return (order.SupplierPurchases ?? [])
            .Where(IsLiveSupplierPurchase)
            .Where(p => !string.IsNullOrWhiteSpace(p.SupplierOrderNo))
            // Lần mua do người xử lý tự gắn là quyết định đã có người tra; cân bằng theo số dòng
            // hàng là suy đoán của máy, nên nó nhường.
            .Where(p => p.ManualLinkedAt is null)
            .Where(p => !SharesSheinShipmentGroup(order, p))
            .Where(p => !MatchesPurchaseMark(order, p, purchaseMarks))
            .OrderByDescending(p => p.SupplierPaidAt ?? p.OrderedAt ?? p.MatchedAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Đơn nhận billno thừa: cùng khách, chưa có lần mua nào, và được đặt trước khi billno được mua.
    /// Ưu tiên dấu «Đã mua» khớp mốc của billno thừa. Ràng buộc cuối chặn ca khách mua lặp lại —
    /// hàng mua trước khi đơn tồn tại không thuộc đơn đó.
    /// </summary>
    internal static (Order? Order, bool ByPurchaseMark) FindRebalanceTarget(
        IReadOnlyList<Order> pool,
        Order source,
        SupplierPurchase purchase,
        string phoneKey,
        string zipKey,
        IReadOnlyCollection<Guid> claimedThisPass,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        var purchasedAt = purchase.SupplierPaidAt ?? purchase.OrderedAt;

        var eligible = new List<Order>();
        foreach (var order in pool)
        {
            if (order.Id == source.Id) continue;
            if (claimedThisPass.Contains(order.Id)) continue;
            if (IsPlatformTerminalOrder(order)) continue;
            if (LivePurchaseCount(order) > 0) continue;
            if (phoneKey != PhoneKey(order.BuyerPhone)) continue;
            if (zipKey != ZipKey(order.PostalCode)) continue;
            if (IsPlacedAfterPurchase(order, purchasedAt)) continue;
            eligible.Add(order);
        }

        if (eligible.Count == 0) return (null, false);

        if (purchaseMarks is not null && !purchaseMarks.IsEmpty && purchasedAt.HasValue)
        {
            // Dùng SheinOrderDto tối thiểu để tái dùng FindOrderByPurchaseMark.
            var stub = new SheinOrderDto
            {
                SheinPaidAt = purchasedAt,
                OrderedAt = purchase.OrderedAt,
            };
            var (marked, attempt) = FindOrderByPurchaseMark(eligible, stub, purchaseMarks, claimedThisPass);
            if (marked is not null)
            {
                if (attempt is not null) purchaseMarks.Consume(attempt.Id);
                return (marked, true);
            }
        }

        Order? best = null;
        foreach (var order in eligible)
        {
            if (best is null || ComparePurchaseDemand(order, best) < 0) best = order;
        }

        return (best, false);
    }

    /// <summary>
    /// Đơn sàn được đặt muộn hơn mốc mua quá <see cref="PlatformOrderCausalitySlack"/> — hàng đã mua
    /// trước khi đơn tồn tại thì không phải hàng của đơn đó.
    /// </summary>
    private static bool IsPlacedAfterPurchase(Order order, DateTime? purchasedAt)
    {
        if (purchasedAt is null || order.OrderCreateTime is null) return false;
        return NormalizeUtc(order.OrderCreateTime.Value) - NormalizeUtc(purchasedAt.Value)
            > PlatformOrderCausalitySlack;
    }

    /// <summary>
    /// Gắn lại một lần mua đã có sẵn dữ liệu NCC sang đơn khác.
    ///
    /// Không dùng <c>AttachSupplierPurchase</c> vì ở đây không có payload SHEIN để dựng lại mốc
    /// trạng thái; mốc "Mua" suy từ chính dòng mua, còn mốc vận chuyển do lượt sync kế tiếp của
    /// billno đó stamp lại.
    /// </summary>
    private static void MovePurchaseToOrder(
        Order target,
        SupplierPurchase purchase,
        DateTime now,
        Guid actingUserId)
    {
        SupplierPurchaseIngest.Attach(purchase, target, now, actingUserId);

        var purchasedAt = purchase.SupplierPaidAt ?? purchase.OrderedAt;
        if (purchasedAt.HasValue)
        {
            OrderFulfilTimeline.Stamp(
                target,
                OrderFulfilTimeline.Milestone.FulfilPurchased,
                NormalizeUtc(purchasedAt.Value),
                OrderFulfilTimeline.Source.Shein);
            OrderFulfilTimeline.Normalize(target);
        }

        RefreshOrderSupplierAggregates(target);
        // Lần mua mang theo tài khoản đã trả tiền cho nó; đơn đích phải nhận cùng quy kết, bằng
        // không đơn vừa nhận billno hiện đúng giá mua mà trống cột «TK mua» trên danh sách.
        SyncFulfilAttributionWithPurchases(target);
        target.UpdatedAt = now;
    }

    /// <summary>
    /// True khi mọi ứng viên phone+zip đều là đơn đã hủy/hoàn và mốc mua xảy ra sau mốc hủy —
    /// dùng để phân biệt hàng đợi <c>platform_canceled</c> với <c>no_local_match</c>.
    /// </summary>
    internal static bool IsOnlyPlatformCanceledAfterPurchaseCandidate(
        IReadOnlyList<Order> pool,
        SheinOrderDto shein,
        string sheinPhone,
        string sheinZip)
    {
        if (sheinPhone.Length == 0 || sheinZip.Length == 0) return false;

        var sawCanceledAfter = false;
        foreach (var order in pool)
        {
            if (sheinPhone != PhoneKey(order.BuyerPhone)) continue;
            if (sheinZip != ZipKey(order.PostalCode)) continue;
            if (IsPurchasedBeforePlatformOrder(shein, order)) continue;

            if (!IsPlatformTerminalOrder(order)) return false;
            if (!IsPurchasedAfterPlatformCancel(shein, order)) return false;
            sawCanceledAfter = true;
        }

        return sawCanceledAfter;
    }

    /// <summary>
    /// Billno đang gắn trên đơn sàn đã hủy/hoàn trong khi còn đơn anh em còn hiệu lực — chuyển
    /// sang đơn sống. Quyết định gắn tay (<c>ManualLinkedAt</c>) không bị máy đụng tới.
    ///
    /// Không có đích thì <b>giữ nguyên</b> (ca đã chi tiền cho đúng đơn rồi khách mới hủy — sự thật
    /// kế toán). Idempotent: sau chuyển đơn terminal hết lần mua; đơn đích đã có lần mua nên không
    /// nhận tiếp.
    /// </summary>
    private async Task<int> RelocatePurchasesOffCanceledOrdersAsync(
        IReadOnlyList<Order> pool,
        SheinOrderSyncResultDto result,
        HashSet<Guid> ordersNeedingCostRefresh,
        HashSet<Guid> claimedThisPass,
        Guid actingUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moved = 0;

        foreach (var order in pool)
        {
            if (!IsPlatformTerminalOrder(order)) continue;
            if (LivePurchaseCount(order) == 0) continue;

            var phoneKey = PhoneKey(order.BuyerPhone);
            var zipKey = ZipKey(order.PostalCode);
            if (phoneKey.Length == 0 || zipKey.Length == 0) continue;

            // Snapshot: collection thay đổi khi detach.
            var candidates = (order.SupplierPurchases ?? [])
                .Where(IsLiveSupplierPurchase)
                .Where(p => p.ManualLinkedAt is null)
                .Where(p => !string.IsNullOrWhiteSpace(p.SupplierOrderNo))
                .OrderBy(p => p.SupplierPaidAt ?? p.OrderedAt ?? p.MatchedAt ?? p.CreatedAt)
                .ThenBy(p => p.Id)
                .ToList();

            foreach (var surplus in candidates)
            {
                if (!IsLiveSupplierPurchase(surplus)) continue;
                if (surplus.OrderId != order.Id) continue;

                var target = FindCanceledRelocateTarget(
                    pool, order, surplus, phoneKey, zipKey, claimedThisPass);
                if (target is null)
                {
                    result.Notes.Add(
                        $"Đơn {OrderLabel(order)} (đã hủy/hoàn): giữ {surplus.SupplierOrderNo}"
                        + " — không có đơn cùng khách còn hiệu lực để chuyển.");
                    continue;
                }

                var sourceLabel = OrderLabel(order);
                await DetachPurchaseFromOrderAsync(
                    pool, surplus, SheinSkipReasons.MovedOffCanceledOrder, now, cancellationToken);

                // Detach đã dọn mốc SHEIN; gỡ luôn quy kết tài khoản/thẻ kẻo đơn hủy vẫn hiện
                // "do TK X mua" dù không còn billno nào.
                SyncFulfilAttributionWithPurchases(order);

                MovePurchaseToOrder(target, surplus, now, actingUserId);

                ordersNeedingCostRefresh.Add(order.Id);
                ordersNeedingCostRefresh.Add(target.Id);
                // Chỉ đánh dấu đã nhận khi đơn đích hết chỗ — bằng không một đơn sống nhiều dòng
                // hàng chỉ nhận được một billno trong khi đơn hủy còn ôm billno thứ hai.
                if (!HasRoomForPurchase(target))
                {
                    claimedThisPass.Add(target.Id);
                }

                moved++;
                result.Rebalanced++;
                result.Notes.Add(
                    $"SHEIN {surplus.SupplierOrderNo}: đơn {sourceLabel} đã hủy/hoàn trên sàn"
                    + $" — chuyển sang đơn {OrderLabel(target)} còn hiệu lực.");
            }
        }

        return moved;
    }

    /// <summary>
    /// Đích nhận billno từ đơn đã hủy: không terminal, cùng khách, còn chỗ, SKU trùng (khi billno
    /// có product id), và không vi phạm nhân quả đặt/mua. Không đòi <c>LivePurchaseCount == 0</c>:
    /// một đơn sống nhiều dòng hàng có thể nhận nhiều billno đang kẹt trên đơn hủy trong cùng lượt.
    /// </summary>
    internal static Order? FindCanceledRelocateTarget(
        IReadOnlyList<Order> pool,
        Order source,
        SupplierPurchase purchase,
        string phoneKey,
        string zipKey,
        IReadOnlyCollection<Guid> claimedThisPass)
    {
        var purchasedAt = purchase.SupplierPaidAt ?? purchase.OrderedAt;
        Order? best = null;

        foreach (var order in pool)
        {
            if (order.Id == source.Id) continue;
            if (claimedThisPass.Contains(order.Id)) continue;
            if (IsPlatformTerminalOrder(order)) continue;
            if (!HasRoomForPurchase(order)) continue;
            if (phoneKey != PhoneKey(order.BuyerPhone)) continue;
            if (zipKey != ZipKey(order.PostalCode)) continue;
            if (IsPlacedAfterPurchase(order, purchasedAt)) continue;
            if (!PurchaseOverlapsOrderSku(purchase, order)) continue;

            if (best is null || ComparePurchaseDemand(order, best) < 0) best = order;
        }

        return best;
    }

    /// <summary>
    /// SKU của lần mua trùng ít nhất một dòng hàng của đơn. Billno không có product id → cho phép
    /// (không chặn chuyển vì thiếu dữ liệu).
    /// </summary>
    private static bool PurchaseOverlapsOrderSku(SupplierPurchase purchase, Order order)
    {
        var keys = ParseLinkProductIds(purchase.ProductIdsJson);
        if (keys.Count == 0) return true;

        foreach (var item in order.Items ?? [])
        {
            if (OrderItemProductIdKeys(item).Any(keys.Contains)) return true;
        }

        return false;
    }
}
