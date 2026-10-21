using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.DTOs.Order;

namespace Application.Services.Shein;

/// <summary>
/// Money side of the SHEIN payload reader. Ported from the extension's <c>sheinOrders.ts</c>;
/// the field precedence encodes which SHEIN keys were found trustworthy on real orders, so it
/// must stay in step with the TypeScript or the two paths will disagree on cost.
/// </summary>
public static partial class SheinOrderNormalizer
{
    /// <summary>Retail / struck-through unit price keys on a goods row.</summary>
    private static readonly string[] GoodsRetailKeys =
    [
        "retail_price_vo", "retailPriceVo", "retailPrice", "retail_price", "retailPriceWithSymbol",
        "marketPrice", "market_price", "originPrice", "origin_price", "originalPrice", "original_price"
    ];

    /// <summary>Sale unit price keys — the UI subtotal basis, before club/coupon.</summary>
    private static readonly string[] GoodsSaleKeys =
    [
        "special_price_vo", "specialPriceVo", "special_price", "specialPrice", "unitPrice",
        "unit_price", "unitDiscountPrice", "unit_discount_price", "salePrice", "sale_price",
        "goodsPrice", "goods_price"
    ];

    /// <summary>Keys that may carry the human label of a price-summary row.</summary>
    private static readonly string[] PriceLabelKeys =
    [
        "name", "title", "label", "lable", "text", "desc", "key", "price_type_name", "priceTypeName",
        "show_name", "showName", "local_name", "localName"
    ];

    /// <summary>Amount keys on a price-summary row.</summary>
    private static readonly string[] PriceAmountKeys =
        ["amount", "price", "value", "priceWithSymbol", "amountWithSymbol", "usdAmount"];

    /// <summary>Arrays that may hold the price summary rows shown in the SHEIN UI.</summary>
    private static readonly string[] PriceListKeys =
    [
        "priceDetailList", "price_detail_list", "orderPriceList", "order_price_list",
        "priceInfoList", "price_info_list", "paymentPriceList", "payment_price_list",
        "priceList", "price_list", "showPriceDetailList", "show_price_detail_list"
    ];

    [GeneratedRegex(@"^(items?\s+|merchandise\s+|product\s+|order\s+)?sub\s*-?\s*total\s*:?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalLabelRegex();

    [GeneratedRegex(@"\b(retail(\s*price)?|original(\s*price)?|list(\s*price)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RetailLabelRegex();

    [GeneratedRegex(@"total\s*savings|shipping|free\s*ship|\bclub\b|membership|\bprime\b|\bpoints?\b|\bwallet\b|cashback|retail\s*delivery|delivery\s*fee", RegexOptions.IgnoreCase)]
    private static partial Regex CouponExclusionRegex();

    [GeneratedRegex(@"total\s*savings|shipping|free\s*ship|coupon|\bpoints?\b|\bwallet\b|cashback", RegexOptions.IgnoreCase)]
    private static partial Regex ClubExclusionRegex();

    [GeneratedRegex(@"total\s*savings|shipping|free\s*ship|coupon|\bclub\b|\bwallet\b", RegexOptions.IgnoreCase)]
    private static partial Regex PointsExclusionRegex();

    [GeneratedRegex(@"shein\s*club|club\s*exclusive|\bmembership\b|\bprime\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClubLabelRegex();

    [GeneratedRegex(@"\bpoints?\b", RegexOptions.IgnoreCase)]
    private static partial Regex PointsLabelRegex();

    [GeneratedRegex(@"\bwallet\b|cashback|shein\s*wallet|store\s*credit", RegexOptions.IgnoreCase)]
    private static partial Regex WalletLabelRegex();

    [GeneratedRegex(@"coupon|\bclub\b|\bpoints?\b|shipping|retail\s*delivery", RegexOptions.IgnoreCase)]
    private static partial Regex WalletExclusionRegex();

    [GeneratedRegex(@"retail\s*delivery|delivery\s*fee", RegexOptions.IgnoreCase)]
    private static partial Regex RetailDeliveryLabelRegex();

    /// <summary>
    /// Nhãn chứa chữ "total" nhưng <b>không</b> phải tổng thanh toán. Ba regex loại trừ ở trên đều
    /// đã phải chặn <c>total savings</c> nên nhãn đó chắc chắn có trong payload thật; nhánh Total là
    /// nhánh cuối của chuỗi else-if nên thiếu bộ lọc này thì số tiền tiết kiệm được ghi thành tổng
    /// tiền của đơn.
    /// </summary>
    [GeneratedRegex(@"saving|saved|discount|refund|quantity|\bqty\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalExclusionRegex();

    /// <summary>Nhãn "total" nào thực sự là tổng thanh toán.</summary>
    internal static bool IsPaymentTotalLabel(string label)
    {
        var l = label.Trim();
        return !TotalExclusionRegex().IsMatch(l) && TotalLabelRegex().IsMatch(l);
    }

    [GeneratedRegex(@"(^|\b)total(\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalLabelRegex();

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Quantity on one goods row; anything unusable counts as one unit.</summary>
    private static decimal GoodsQty(JsonObject goods)
    {
        var q = SheinJson.ToNumber(SheinJson.Pick(goods, "quantity", "goods_number", "goodsNumber", "qty"));
        return q is > 0 && double.IsFinite(q.Value) ? (decimal)q.Value : 1m;
    }

    /// <summary>Unit price from the goods row or its nested <c>product</c>.</summary>
    private static decimal? ReadGoodsUnit(JsonObject goods, string[] keys)
    {
        foreach (var key in keys)
        {
            if (SheinJson.Has(goods, key))
            {
                var value = SheinJson.ReadMoney(SheinJson.Get(goods, key));
                if (value.HasValue)
                {
                    return value;
                }
            }
        }

        var product = SheinJson.Obj(SheinJson.Get(goods, "product"));
        if (product is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (SheinJson.Has(product, key))
            {
                var value = SheinJson.ReadMoney(SheinJson.Get(product, key));
                if (value.HasValue)
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>Literal sum over the goods rows (unit × qty); no discounts applied.</summary>
    private static decimal? SumGoodsField(JsonObject root, string[] keys)
    {
        var sum = 0m;
        var any = false;
        foreach (var goods in SheinJson.ObjectItems(root, "orderGoodsList"))
        {
            var unit = ReadGoodsUnit(goods, keys);
            if (unit is null)
            {
                continue;
            }

            sum += unit.Value * GoodsQty(goods);
            any = true;
        }

        return any ? Round2(sum) : null;
    }

    private static decimal? ReadOrderRetail(JsonObject root) =>
        SheinJson.Money(root, "retailTotallPrice")
        ?? SheinJson.Money(root, "retailTotalPrice")
        ?? SheinJson.Money(root, "retail_totall_price")
        ?? SheinJson.Money(root, "retail_total_price")
        ?? SheinJson.Money(root, "retailPrice", "retail_price")
        ?? SumGoodsField(root, GoodsRetailKeys);

    /// <summary>
    /// Subtotal before discounts. Raw <c>subTotalPrice</c> is deliberately not preferred — on
    /// club orders SHEIN already subtracts the club discount from it.
    /// </summary>
    private static decimal? ReadOrderSubtotal(JsonObject root) =>
        SheinJson.Money(root, "newSubTotalPrice")
        ?? SheinJson.Money(root, "new_sub_total_price")
        ?? SheinJson.Money(root, "normal_goods_total_price")
        ?? SheinJson.Money(root, "normalGoodsTotalPrice")
        ?? SumGoodsField(root, GoodsSaleKeys);

    /// <summary>A literal zero here is a SHEIN placeholder, so it counts as "unknown".</summary>
    private static decimal? ReadOrderCoupon(JsonObject root)
    {
        var v = SheinJson.Money(root, "couponPrice", "coupon_price");
        return v is null or 0 ? null : Math.Abs(v.Value);
    }

    private static decimal? ReadOrderClub(JsonObject root)
    {
        var v = SheinJson.Money(root, "prime_discount_amount_vo_total", "primeDiscountAmountVoTotal", "clubDiscount");
        return v is null or 0 ? null : Math.Abs(v.Value);
    }

    private static decimal? ReadOrderPoints(JsonObject root)
    {
        var v = SheinJson.Money(
            root,
            "pointPrice", "point_price", "pointsPrice", "points_price",
            "usePointPrice", "use_point_price", "integralPrice", "integral_price");
        return v is null or 0 ? null : Math.Abs(v.Value);
    }

    private static decimal? ReadOrderWallet(JsonObject root)
    {
        var v = SheinJson.Money(root, "usedWalletPrice", "used_wallet_price", "walletPrice", "wallet_price");
        return v is null or 0 ? null : Math.Abs(v.Value);
    }

    private static decimal? ReadOrderRetailDeliveryFee(JsonObject root)
    {
        var v = SheinJson.Money(root, "retail_delivery_fee", "retailDeliveryFee", "retail_delivery_fee_amount");
        return v is null or 0 ? null : Math.Abs(v.Value);
    }

    /// <summary>
    /// Real tax. <c>taxPrice</c> is usually a zero placeholder even when tax was charged; the
    /// charged amount lives in <c>total_tax</c> / <c>person_all_tax</c> / the <c>tax</c> array.
    /// </summary>
    private static decimal? ReadOrderTax(JsonObject root)
    {
        var fromTotal = SheinJson.Money(root, "total_tax")
                        ?? SheinJson.Money(root, "totalTax")
                        ?? SheinJson.Money(root, "person_all_tax")
                        ?? SheinJson.Money(root, "personAllTax")
                        ?? SheinJson.Money(root, "hybrid_gst_tax_price")
                        ?? SheinJson.Money(root, "hybridGstTaxPrice")
                        ?? SheinJson.Money(root, "icms_total_tax_price")
                        ?? SheinJson.Money(root, "icmsTotalTaxPrice");
        if (fromTotal is not null and not 0)
        {
            return fromTotal;
        }

        var sum = 0m;
        var any = false;
        foreach (var row in SheinJson.ObjectItems(root, "tax"))
        {
            var v = SheinJson.Money(row, "taxPrice", "tax_price", "amount", "usdAmount");
            if (v.HasValue)
            {
                sum += v.Value;
                any = true;
            }
        }

        if (any)
        {
            return Round2(sum);
        }

        var flat = SheinJson.Money(root, "taxPrice", "tax_price");
        return flat is not null and not 0 ? flat : fromTotal;
    }

    /// <summary>On-time delivery guarantee / insurance, summed across the insurance rows.</summary>
    private static decimal? SumInsurance(JsonObject root)
    {
        var sum = 0m;
        var any = false;
        foreach (var ins in SheinJson.ObjectItems(root, "order_insurance_res_list"))
        {
            var fee = SheinJson.Money(ins, "insurance_fee_usd", "insurance_fee");
            if (fee.HasValue)
            {
                sum += fee.Value;
                any = true;
            }
        }

        return any ? sum : null;
    }

    /// <summary>
    /// Fallback subtotal: total + wallet − ship − retailDelivery − onTime − tax + club + coupon + points.
    /// </summary>
    private static decimal? ComputeSubtotalFromTotal(
        decimal? total,
        decimal? shipping,
        decimal? onTime,
        decimal? tax,
        decimal? club,
        decimal? coupon,
        decimal? points,
        decimal? wallet = null,
        decimal? retailDelivery = null)
    {
        if (total is null)
        {
            return null;
        }

        return Round2(
            total.Value
            + (wallet ?? 0)
            - (shipping ?? 0) - (retailDelivery ?? 0) - (onTime ?? 0) - (tax ?? 0)
            + (club ?? 0) + (coupon ?? 0) + (points ?? 0));
    }

    /// <summary>True when enough discount components are known to invert the subtotal formula.</summary>
    private static bool CanInvertSubtotalFromTotal(
        decimal? tax,
        decimal? club,
        decimal? coupon,
        decimal? onTime,
        decimal? points,
        decimal? wallet = null,
        decimal? retailDelivery = null) =>
        tax is not null || coupon is not null || club is not null || points is not null
        || wallet is not null || retailDelivery is not null || (onTime ?? 0) > 0;

    /// <summary>Discounts after reconciliation.</summary>
    private readonly record struct ReconciledDiscounts(decimal? Coupon, decimal? Club, decimal? Points);

    /// <summary>
    /// Forces the discount rows to satisfy the identity
    /// <c>coupon + club + points ≈ subtotal + ship + retailDelivery + onTime + tax − total − wallet</c>.
    ///
    /// Wallet is payment from cashback balance (not a discount). Points are their own UI row and
    /// are never inflated by free shipping, so they are taken out of the budget first. SHEIN's
    /// FREE-shipping promo adds the same struck-through fee (~$3.99) to <em>both</em>
    /// <c>couponPrice</c> and <c>prime_discount</c>, which is why an excess that appears in both
    /// is split in half rather than assigned to either.
    /// </summary>
    private static ReconciledDiscounts ReconcileDiscounts(
        decimal? subtotal,
        decimal? shipping,
        decimal? onTime,
        decimal? tax,
        decimal? total,
        decimal? coupon,
        decimal? club,
        decimal? points,
        decimal? wallet = null,
        decimal? retailDelivery = null)
    {
        if (subtotal is null || total is null)
        {
            return new ReconciledDiscounts(coupon, club, points);
        }

        var expected = Round2(
            subtotal.Value + (shipping ?? 0) + (retailDelivery ?? 0) + (onTime ?? 0) + (tax ?? 0)
            - total.Value - (wallet ?? 0));
        if (expected < -0.02m)
        {
            return new ReconciledDiscounts(coupon, club, points);
        }

        var expectedClamped = Math.Max(0m, expected);
        decimal? p = points is null ? null : Math.Min(Math.Abs(points.Value), expectedClamped);
        var budget = Round2(expectedClamped - (p ?? 0));

        decimal? c = coupon is null ? null : Math.Abs(coupon.Value);
        decimal? m = club is null ? null : Math.Abs(club.Value);

        if (c is null && m is null)
        {
            return new ReconciledDiscounts(budget, 0, p);
        }

        if (c is null)
        {
            return new ReconciledDiscounts(Round2(Math.Max(0, budget - (m ?? 0))), m, p);
        }

        if (m is null)
        {
            var derived = Round2(budget - c.Value);
            if (derived >= -0.02m)
            {
                return new ReconciledDiscounts(c, Math.Max(0, derived), p);
            }
        }

        var sum = Round2(c.Value + (m ?? 0));
        if (Math.Abs(sum - budget) <= 0.02m)
        {
            return new ReconciledDiscounts(c, m ?? 0, p);
        }

        // One of the two already fits inside the discount budget — trust that label and derive
        // the other, instead of half-splitting a correct coupon just because club is inflated.
        if (c <= budget + 0.02m)
        {
            return new ReconciledDiscounts(c, Round2(Math.Max(0, budget - c.Value)), p);
        }

        if (m is not null && m <= budget + 0.02m)
        {
            return new ReconciledDiscounts(Round2(Math.Max(0, budget - m.Value)), m, p);
        }

        var excess = Round2(sum - budget);
        if (excess > 0.02m && (shipping ?? 0) == 0 && m is not null)
        {
            var half = Round2(excess / 2);
            var c2 = Round2(c.Value - half);
            var m2 = Round2(m.Value - half);
            if (c2 >= -0.02m && m2 >= -0.02m &&
                Math.Abs(Round2(Math.Max(0, c2) + Math.Max(0, m2)) - budget) <= 0.02m)
            {
                return new ReconciledDiscounts(Math.Max(0, c2), Math.Max(0, m2), p);
            }
        }

        return new ReconciledDiscounts(budget, 0, p);
    }

    private static bool IsCouponDiscountLabel(string label)
    {
        var l = label.Trim();
        if (CouponExclusionRegex().IsMatch(l))
        {
            return false;
        }

        return l.Contains("coupon", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(l, @"^discount\b", RegexOptions.IgnoreCase);
    }

    private static bool IsClubDiscountLabel(string label) =>
        !ClubExclusionRegex().IsMatch(label.Trim()) && ClubLabelRegex().IsMatch(label.Trim());

    private static bool IsPointsDiscountLabel(string label) =>
        !PointsExclusionRegex().IsMatch(label.Trim()) && PointsLabelRegex().IsMatch(label.Trim());

    private static bool IsWalletLabel(string label) =>
        !WalletExclusionRegex().IsMatch(label.Trim()) && WalletLabelRegex().IsMatch(label.Trim());

    private static bool IsRetailDeliveryLabel(string label) =>
        RetailDeliveryLabelRegex().IsMatch(label.Trim());

    /// <summary>Amount of the first price-summary row whose label matches.</summary>
    private static decimal? ReadPriceRowByLabel(JsonObject root, Regex labelRegex)
    {
        foreach (var listKey in PriceListKeys)
        {
            foreach (var row in SheinJson.ObjectItems(root, listKey))
            {
                var label = RowLabel(row);
                if (label is null || !labelRegex.IsMatch(label))
                {
                    continue;
                }

                var amount = SheinJson.Money(row, SheinJson.MoneyKeys);
                if (amount.HasValue)
                {
                    return amount;
                }
            }
        }

        return null;
    }

    /// <summary>First non-empty label-ish string on a node.</summary>
    private static string? RowLabel(JsonObject row)
    {
        foreach (var key in PriceLabelKeys)
        {
            if (SheinJson.Get(row, key) is JsonValue value &&
                value.TryGetValue<string>(out var s) &&
                !string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// True when the breakdown carries enough signal to replace the stored one wholesale, rather
    /// than a total-only payload that would blank out retail/coupon/tax already on the link.
    /// </summary>
    public static bool IsRichBreakdown(SheinOrderDto target)
    {
        if ((target.RetailPrice ?? 0) > 0) return true;
        if ((target.Tax ?? 0) > 0) return true;
        if ((target.CouponPrice ?? 0) > 0) return true;
        if ((target.ClubDiscount ?? 0) > 0) return true;
        if ((target.PointsDiscount ?? 0) > 0) return true;
        if ((target.WalletPrice ?? 0) > 0) return true;
        if ((target.RetailDeliveryFee ?? 0) > 0) return true;

        var sub = target.Subtotal ?? 0;
        var total = target.Total ?? 0;
        return sub > 0 && total > 0 && Math.Abs(sub - total) > 0.02m;
    }

    /// <summary>
    /// Zero-fills missing money fields, but only once the breakdown is rich enough.
    ///
    /// <see cref="SheinOrderDto.Total"/> cố tình <b>không</b> được zero-fill. Một thành phần vắng
    /// trong breakdown rich đúng là bằng 0 (không có coupon thì giảm coupon là 0), nhưng tổng thanh
    /// toán vắng nghĩa là payload không nói — thường vì <c>get_order_info</c> trả bản rút gọn. Điền
    /// 0 vào đó làm vô hiệu guard "không ghi Total=0 từ null" ở <c>ApplySheinCostsToLink</c>: nó chỉ
    /// còn thấy một free-order tường minh và ghi <c>Total = 0</c> xuống DB, chi phí đơn biến mất và
    /// lãi bị thổi phồng.
    /// </summary>
    private static void DefaultMissingBreakdownToZero(SheinOrderDto target)
    {
        if (!IsRichBreakdown(target))
        {
            return;
        }

        target.RetailPrice ??= 0;
        target.Subtotal ??= 0;
        target.ShippingFee ??= 0;
        target.RetailDeliveryFee ??= 0;
        target.OnTimeGuarantee ??= 0;
        target.CouponPrice ??= 0;
        target.ClubDiscount ??= 0;
        target.PointsDiscount ??= 0;
        target.WalletPrice ??= 0;
        target.Tax ??= 0;
    }
}
