using Domain.Entities;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Quy tắc duy nhất quyết định lần mua NCC nào được tính vào giá mua của đơn.
///
/// Kết quả nằm ở <see cref="SupplierPurchase.IsCharged"/> và các cột <c>Orders.Charged*</c>
/// (materialize lúc <see cref="ApplyAggregates(Order, IEnumerable{SupplierPurchase})"/>, chốt lại ở
/// <c>ApplicationDbContext.SaveChangesAsync</c>). List/dashboard đọc cột; SUM live chỉ là lưới an
/// toàn khi snapshot trống, không phải đường đọc thường ngày.
///
/// <b>Cost / Profit (đã chốt):</b> Chi thật NCC (TrueSpend) =
/// <c>SupplierTotal + SupplierWalletPrice + SupplierMembershipDiscount</c>
/// (= <see cref="Order.ChargedTrueSpend"/>). Club cộng vào vì giảm membership chỉ có nhờ đã mua SHEIN Club.
/// <c>Orders.totalCost</c> / Profit = gross NCC (subtotal+ship+tax+…) + BuyTrack + ThankCard — không dùng TrueSpend.
/// <c>SupplierTotal</c> riêng là phần thẻ. Billno <c>refunded</c>/<c>canceled</c> không tính — tiền đã hoàn.
/// </summary>
public static class OrderSupplierCost
{
    /// <summary>Billno đã kết thúc dòng tiền: huỷ hoặc hoàn (tiền đã về tài khoản).</summary>
    public static bool IsTerminalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        var g = status.Trim().ToLowerInvariant();
        return g is SupplierStatusGroups.Canceled
            or "cancelled"
            or SupplierStatusGroups.Refunded;
    }

    /// <summary>
    /// Những lần mua thực sự được tính tiền. Chỉ billno còn hiệu lực (chưa huỷ/hoàn, đã thanh toán).
    /// </summary>
    public static List<T> SelectCharged<T>(
        IEnumerable<T> purchases,
        Func<T, string?> statusOf,
        Func<T, bool> unpaidOf,
        Func<T, string> groupOf)
    {
        _ = groupOf;
        return purchases
            .Where(p => !unpaidOf(p) && !IsTerminalStatus(statusOf(p)))
            .ToList();
    }

    /// <summary>
    /// Tập được tính tiền của một đơn. Dòng nhập tay có tiền thắng; <c>ignored</c> bị loại.
    /// </summary>
    public static List<SupplierPurchase> SelectCharged(IEnumerable<SupplierPurchase> purchases)
    {
        var ordered = purchases
            .Where(p => p.MatchState != SupplierPurchaseMatchStates.Ignored)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToList();

        var manual = ordered
            .Where(p => p.Source == SupplierPurchaseSources.Manual && HasMoney(p))
            .ToList();
        if (manual.Count > 0) return manual;

        return SelectCharged(
            ordered.Where(p => p.Source != SupplierPurchaseSources.Manual),
            p => p.SupplierStatus,
            p => p.SupplierUnpaid,
            CheckoutGroupKey);
    }

    /// <summary>Dòng có ít nhất một con số tiền — phân biệt "chưa lấy được" với "bằng 0".</summary>
    public static bool HasMoney(SupplierPurchase purchase) =>
        purchase.SupplierRetailPrice.HasValue
        || purchase.SupplierSubtotal.HasValue
        || purchase.SupplierShippingFee.HasValue
        || purchase.SupplierRetailDeliveryFee.HasValue
        || purchase.SupplierOnTimeGuarantee.HasValue
        || purchase.SupplierCouponPrice.HasValue
        || purchase.SupplierMembershipDiscount.HasValue
        || purchase.SupplierPointsDiscount.HasValue
        || purchase.SupplierWalletPrice.HasValue
        || purchase.SupplierTax.HasValue
        || purchase.SupplierTotal.HasValue;

    /// <summary>Khoá nhận diện một lần checkout (relation / confluence / billno).</summary>
    public static string CheckoutGroupKey(SupplierPurchase purchase)
    {
        if (!string.IsNullOrWhiteSpace(purchase.RelationBillno)) return purchase.RelationBillno.Trim();
        if (!string.IsNullOrWhiteSpace(purchase.ConfluenceBatchNo)) return purchase.ConfluenceBatchNo.Trim();
        return purchase.SupplierOrderNo ?? purchase.Id.ToString();
    }

    /// <summary>
    /// Tính lại <see cref="SupplierPurchase.IsCharged"/> và materialize <c>Orders.Charged*</c>
    /// từ navigation đang nạp.
    ///
    /// <b>Không phải</b> chốt cuối: <c>ApplicationDbContext.SaveChangesAsync</c> chạy lại phép này
    /// trên tập link đã nạp kèm <c>Include</c> trước khi phát SQL. Gọi ở đây vẫn cần vì cùng
    /// phương thức thường đọc <c>IsCharged</c> ngay sau đó (reconcile sổ cái), nhưng một lần gọi
    /// với navigation chưa nạp không còn ghi hỏng snapshot được nữa.
    /// </summary>
    public static void ApplyAggregates(Order order) =>
        ApplyAggregates(order, order.SupplierPurchases ?? []);

    /// <summary>
    /// Như <see cref="ApplyAggregates(Order)"/> nhưng nhận tập link tường minh — dùng ở
    /// <c>SaveChangesAsync</c>, nơi tập link sau khi commit khác navigation đang nạp (dòng vừa
    /// được gắn sang đơn khác, hoặc graph mới chưa fix-up khoá).
    /// </summary>
    public static void ApplyAggregates(Order order, IEnumerable<SupplierPurchase> links)
    {
        var purchases = links as IList<SupplierPurchase> ?? links.ToList();
        foreach (var purchase in purchases) purchase.IsCharged = false;

        if (purchases.Count == 0)
        {
            ClearChargedAggregates(order);
            return;
        }

        foreach (var purchase in SelectCharged(purchases)) purchase.IsCharged = true;
        WriteChargedAggregates(order, purchases);
    }

    /// <summary>
    /// Các cột snapshot mà <see cref="ApplyAggregates(Order, IEnumerable{SupplierPurchase})"/> ghi.
    ///
    /// Dùng ở <c>SaveChangesAsync</c> để nhận ra một đơn vừa bị ghi snapshot rồi tính lại từ tập
    /// link nạp đủ: <c>Order.SupplierPurchases</c> khởi tạo sẵn list rỗng nên không chỗ nào phân
    /// biệt được "đơn không có lần mua" với "navigation chưa nạp", và lần ghi từ navigation rỗng
    /// oan đó xoá sạch giá mua của đơn.
    /// </summary>
    public static readonly string[] SnapshotProperties =
    [
        nameof(Order.ChargedSupplierRetailPrice),
        nameof(Order.ChargedSupplierSubtotal),
        nameof(Order.ChargedSupplierShippingFee),
        nameof(Order.ChargedSupplierRetailDeliveryFee),
        nameof(Order.ChargedSupplierOnTimeGuarantee),
        nameof(Order.ChargedSupplierCouponPrice),
        nameof(Order.ChargedSupplierMembershipDiscount),
        nameof(Order.ChargedSupplierPointsDiscount),
        nameof(Order.ChargedSupplierWalletPrice),
        nameof(Order.ChargedSupplierTax),
        nameof(Order.ChargedSupplierTotal),
        nameof(Order.ChargedTrueSpend)
    ];

    /// <summary>Xoá snapshot Charged* khi đơn không còn lần mua.</summary>
    public static void ClearChargedAggregates(Order order)
    {
        order.ChargedSupplierRetailPrice = null;
        order.ChargedSupplierSubtotal = null;
        order.ChargedSupplierShippingFee = null;
        order.ChargedSupplierRetailDeliveryFee = null;
        order.ChargedSupplierOnTimeGuarantee = null;
        order.ChargedSupplierCouponPrice = null;
        order.ChargedSupplierMembershipDiscount = null;
        order.ChargedSupplierPointsDiscount = null;
        order.ChargedSupplierWalletPrice = null;
        order.ChargedSupplierTax = null;
        order.ChargedSupplierTotal = null;
        order.ChargedTrueSpend = null;
    }

    /// <summary>Ghi Charged* từ các dòng IsCharged (sau khi cờ đã set).</summary>
    public static void WriteChargedAggregates(Order order, IEnumerable<SupplierPurchase> purchases)
    {
        var list = purchases as IList<SupplierPurchase> ?? purchases.ToList();
        order.ChargedSupplierRetailPrice = SumCharged(list, p => p.SupplierRetailPrice);
        order.ChargedSupplierSubtotal = SumCharged(list, p => p.SupplierSubtotal);
        order.ChargedSupplierShippingFee = SumCharged(list, p => p.SupplierShippingFee);
        order.ChargedSupplierRetailDeliveryFee = SumCharged(list, p => p.SupplierRetailDeliveryFee);
        order.ChargedSupplierOnTimeGuarantee = SumCharged(list, p => p.SupplierOnTimeGuarantee);
        order.ChargedSupplierCouponPrice = SumCharged(list, p => p.SupplierCouponPrice);
        order.ChargedSupplierMembershipDiscount = SumCharged(list, p => p.SupplierMembershipDiscount);
        order.ChargedSupplierPointsDiscount = SumCharged(list, p => p.SupplierPointsDiscount);
        order.ChargedSupplierWalletPrice = SumCharged(list, p => p.SupplierWalletPrice);
        order.ChargedSupplierTax = SumCharged(list, p => p.SupplierTax);
        order.ChargedSupplierTotal = SumCharged(list, p => p.SupplierTotal);
        order.ChargedTrueSpend = TrueSpend(
            order.ChargedSupplierTotal,
            order.ChargedSupplierWalletPrice,
            order.ChargedSupplierMembershipDiscount);
    }

    /// <summary>
    /// Giá mua gộp NCC (trước giảm coupon/points) = subtotal + ship + tax + RDF + on-time.
    /// Null khi chưa có subtotal (breakdown chưa đủ).
    /// </summary>
    public static decimal? GrossPurchaseCostFromBreakdown(SupplierPurchase purchase)
    {
        if (!purchase.SupplierSubtotal.HasValue) return null;
        return (purchase.SupplierSubtotal ?? 0m)
            + (purchase.SupplierShippingFee ?? 0m)
            + (purchase.SupplierTax ?? 0m)
            + (purchase.SupplierRetailDeliveryFee ?? 0m)
            + (purchase.SupplierOnTimeGuarantee ?? 0m);
    }

    /// <summary>
    /// Fallback gross từ snapshot sổ cái: thẻ + ví + coupon + club + points (không trừ giảm giá).
    /// Null khi cả năm thành phần đều thiếu.
    /// </summary>
    public static decimal? GrossPurchaseCostFromLedger(
        decimal amount,
        decimal? wallet,
        decimal? coupon,
        decimal? membership,
        decimal? points)
    {
        if (amount == 0m && wallet is null && coupon is null && membership is null && points is null)
        {
            return null;
        }

        return amount + (wallet ?? 0m) + (coupon ?? 0m) + (membership ?? 0m) + (points ?? 0m);
    }

    /// <summary>Ưu tiên breakdown purchase; fallback ledger snapshots.</summary>
    public static decimal? ResolveGrossPurchaseCost(
        SupplierPurchase? purchase,
        decimal ledgerAmount,
        decimal? ledgerWallet,
        decimal? ledgerCoupon,
        decimal? ledgerMembership,
        decimal? ledgerPoints)
    {
        if (purchase is not null)
        {
            var fromBreakdown = GrossPurchaseCostFromBreakdown(purchase);
            if (fromBreakdown.HasValue) return fromBreakdown;
        }

        return GrossPurchaseCostFromLedger(
            ledgerAmount, ledgerWallet, ledgerCoupon, ledgerMembership, ledgerPoints);
    }

    /// <summary>
    /// Gross NCC cấp đơn từ cột <c>Orders.Charged*</c> — breakdown trước, fallback ledger, rồi
    /// SUM trực tiếp trên <c>order.SupplierPurchases</c> khi snapshot trống.
    ///
    /// Lớp cuối là điều kiện để Cost/Profit không bao giờ lệch khỏi breakdown hiển thị ngay cạnh
    /// nó: mười một field breakdown của DTO đã lùi về SUM live từ trước, nên một snapshot trống
    /// từng làm card đơn hiện <c>—</c> trong khi drawer hiện đủ subtotal/ship/tax của cùng đơn.
    /// Đơn nào chưa nạp navigation thì lớp này im lặng trả <c>null</c> như cũ.
    /// </summary>
    public static decimal? ResolveGrossPurchaseCost(Order order)
    {
        if (order.ChargedSupplierSubtotal.HasValue)
        {
            return (order.ChargedSupplierSubtotal ?? 0m)
                + (order.ChargedSupplierShippingFee ?? 0m)
                + (order.ChargedSupplierTax ?? 0m)
                + (order.ChargedSupplierRetailDeliveryFee ?? 0m)
                + (order.ChargedSupplierOnTimeGuarantee ?? 0m);
        }

        return GrossPurchaseCostFromLedger(
                   order.ChargedSupplierTotal ?? 0m,
                   order.ChargedSupplierWalletPrice,
                   order.ChargedSupplierCouponPrice,
                   order.ChargedSupplierMembershipDiscount,
                   order.ChargedSupplierPointsDiscount)
            ?? ResolveGrossPurchaseCost(order.SupplierPurchases ?? []);
    }

    /// <summary>
    /// Gross NCC cộng trực tiếp trên các dòng được tính tiền — cùng thứ tự ưu tiên với bản đọc cột
    /// (breakdown trước, fallback ledger). Null khi không dòng nào có số.
    /// </summary>
    public static decimal? ResolveGrossPurchaseCost(IEnumerable<SupplierPurchase> purchases)
    {
        var list = purchases as IList<SupplierPurchase> ?? purchases.ToList();
        var subtotal = SumCharged(list, p => p.SupplierSubtotal);
        if (subtotal.HasValue)
        {
            return subtotal.Value
                + (SumCharged(list, p => p.SupplierShippingFee) ?? 0m)
                + (SumCharged(list, p => p.SupplierTax) ?? 0m)
                + (SumCharged(list, p => p.SupplierRetailDeliveryFee) ?? 0m)
                + (SumCharged(list, p => p.SupplierOnTimeGuarantee) ?? 0m);
        }

        return GrossPurchaseCostFromLedger(
            SumCharged(list, p => p.SupplierTotal) ?? 0m,
            SumCharged(list, p => p.SupplierWalletPrice),
            SumCharged(list, p => p.SupplierCouponPrice),
            SumCharged(list, p => p.SupplierMembershipDiscount),
            SumCharged(list, p => p.SupplierPointsDiscount));
    }

    /// <summary>
    /// Chi thật = thẻ + ví + club (null nếu cả ba null).
    /// Club cộng vào vì giảm membership chỉ có nhờ đã mua SHEIN Club.
    /// </summary>
    public static decimal? TrueSpend(decimal? cardTotal, decimal? wallet, decimal? membership = null)
    {
        if (!cardTotal.HasValue && !wallet.HasValue && !membership.HasValue) return null;
        return (cardTotal ?? 0m) + (wallet ?? 0m) + (membership ?? 0m);
    }

    /// <summary>TrueSpend từ cột Orders (ưu tiên) hoặc SUM purchases.</summary>
    public static decimal? ResolveTrueSpend(Order order)
    {
        if (order.ChargedTrueSpend.HasValue) return order.ChargedTrueSpend;
        return TrueSpend(
                   order.ChargedSupplierTotal,
                   order.ChargedSupplierWalletPrice,
                   order.ChargedSupplierMembershipDiscount)
            ?? TrueSpend(
                SumCharged(order.SupplierPurchases ?? [], p => p.SupplierTotal),
                SumCharged(order.SupplierPurchases ?? [], p => p.SupplierWalletPrice),
                SumCharged(order.SupplierPurchases ?? [], p => p.SupplierMembershipDiscount));
    }

    /// <summary>
    /// API <c>totalCost</c> / Profit = gross NCC + BuyTrack + ThankCard.
    /// Null khi chưa có gross và cả hai phí xử lý = 0.
    /// </summary>
    public static decimal? ResolveOrderTotalCost(
        decimal? grossPurchaseCost,
        decimal buyTrackAmount,
        decimal thankCardAmount)
    {
        if (grossPurchaseCost is null && buyTrackAmount == 0m && thankCardAmount == 0m)
        {
            return null;
        }

        return (grossPurchaseCost ?? 0m) + buyTrackAmount + thankCardAmount;
    }

    /// <summary>API <c>totalCost</c> từ entity Order.</summary>
    public static decimal? ResolveOrderTotalCost(Order order) =>
        ResolveOrderTotalCost(ResolveGrossPurchaseCost(order), order.BuyTrackAmount, order.ThankCardAmount);

    /// <summary>Cùng gate Profit/totalCost: có gross breakdown/total hoặc phí xử lý.</summary>
    public static bool OrderHasPurchaseCost(decimal? grossPurchaseCost, decimal buyTrack, decimal thankCard) =>
        grossPurchaseCost.HasValue || buyTrack != 0m || thankCard != 0m;

    /// <summary>
    /// Lần mua đại diện để hiển thị/tìm kiếm: ưu tiên dòng được tính tiền có mã carrier.
    /// </summary>
    public static SupplierPurchase? PrimaryPurchase(IEnumerable<SupplierPurchase> purchases)
    {
        var all = purchases.Where(p => p.SupplierOrderNo != null).ToList();
        if (all.Count == 0) return null;

        var charged = all.Where(p => p.IsCharged).ToList();
        return (charged.Count > 0 ? charged : all)
            .OrderByDescending(p => p.TrackingKind == SupplierTrackingKinds.Carrier)
            .ThenByDescending(p => p.TrackingKind == SupplierTrackingKinds.Warehouse)
            .ThenByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .First();
    }

    /// <summary>
    /// Cộng dồn một cột tiền trên các dòng được tính tiền; không dòng nào có số thì trả
    /// <c>null</c> để phân biệt "chưa lấy được" với "bằng 0".
    /// </summary>
    public static decimal? SumCharged(
        IEnumerable<SupplierPurchase> purchases,
        Func<SupplierPurchase, decimal?> selector)
    {
        decimal? total = null;
        foreach (var purchase in purchases)
        {
            if (!purchase.IsCharged) continue;
            var value = selector(purchase);
            if (!value.HasValue) continue;
            total = (total ?? 0m) + value.Value;
        }
        return total;
    }
}
