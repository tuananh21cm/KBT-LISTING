using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>
/// Header KPI rules for Lịch sử mua hàng. <see cref="FulfilService"/> must stay in sync.
/// Profit is unique per order — joint shipment / multi-Shein rows repeat <c>profitAmount</c> on every
/// ledger line in the table, but summary counts each <see cref="Order"/> once.
/// </summary>
public static class FulfilTransactionListSummaryApply
{
    /// <summary>Lightweight order snapshot for unique-order Profit / handling-fee KPIs.</summary>
    public readonly record struct OrderProfitRow(
        Guid Id,
        decimal? SellerNetAmount,
        decimal TotalPaidAmount,
        decimal Amount,
        decimal? ChargedSupplierSubtotal,
        decimal? ChargedSupplierShippingFee,
        decimal? ChargedSupplierTax,
        decimal? ChargedSupplierRetailDeliveryFee,
        decimal? ChargedSupplierOnTimeGuarantee,
        decimal? ChargedSupplierTotal,
        decimal? ChargedSupplierWalletPrice,
        decimal? ChargedSupplierCouponPrice,
        decimal? ChargedSupplierMembershipDiscount,
        decimal? ChargedSupplierPointsDiscount,
        decimal BuyTrackAmount,
        decimal ThankCardAmount);
    /// <summary>Cột Thẻ: Debit cộng, Credit trừ — net tiền trừ trên thẻ.</summary>
    public static decimal CardOfRow(string direction, decimal amount)
    {
        var abs = Math.Abs(amount);
        return string.Equals(direction, FulfilTransaction.DirectionCredit, StringComparison.OrdinalIgnoreCase)
            ? -abs
            : abs;
    }

    /// <summary>TrueSpend per CostUpdate row = thẻ + ví + club (preview Mua hàng).</summary>
    public static decimal TrueSpendOfRow(string source, decimal amount, decimal? wallet, decimal? membership)
    {
        if (!string.Equals(source, FulfilTransaction.SourceCostUpdate, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        return amount + (wallet ?? 0m) + (membership ?? 0m);
    }

    /// <summary>
    /// Gross NCC per CostUpdate row (ledger fallback) — chưa gồm BuyTrack/ThankCard.
    /// </summary>
    public static decimal GrossNccOfRow(
        string source,
        decimal amount,
        decimal? wallet,
        decimal? coupon,
        decimal? membership,
        decimal? points)
    {
        if (!string.Equals(source, FulfilTransaction.SourceCostUpdate, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        return OrderSupplierCost.GrossPurchaseCostFromLedger(
            amount, wallet, coupon, membership, points) ?? 0m;
    }

    /// <summary>Cost column = gross NCC + BuyTrack + ThankCard; Manual contributes 0.</summary>
    public static decimal CostOfRow(
        string source,
        decimal amount,
        decimal? wallet,
        decimal? coupon,
        decimal? membership,
        decimal? points,
        decimal buyTrack,
        decimal thankCard)
    {
        if (!string.Equals(source, FulfilTransaction.SourceCostUpdate, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        return GrossNccOfRow(source, amount, wallet, coupon, membership, points) + buyTrack + thankCard;
    }

    /// <summary>Same gate as per-row Profit: no totalCost → profitAmount is null, skip.</summary>
    public static bool OrderHasCost(decimal? grossPurchaseCost, decimal buyTrack, decimal thankCard) =>
        OrderSupplierCost.OrderHasPurchaseCost(grossPurchaseCost, buyTrack, thankCard);

    /// <summary>Revenue − (gross NCC + BuyTrack + ThankCard) = Orders.totalCost.</summary>
    public static decimal OrderProfit(
        decimal revenue,
        decimal? grossPurchaseCost,
        decimal buyTrack,
        decimal thankCard) =>
        revenue - ((grossPurchaseCost ?? 0m) + buyTrack + thankCard);

    /// <summary>
    /// Sum profit once per distinct order id present on the ledger filter.
    /// </summary>
    public static decimal SumUniqueOrderProfit(
        IEnumerable<Guid> ledgerOrderIds,
        IReadOnlyDictionary<Guid, (decimal Revenue, decimal? GrossPurchaseCost, decimal BuyTrack, decimal ThankCard)> orders)
    {
        decimal sum = 0m;
        foreach (var id in ledgerOrderIds.Distinct())
        {
            if (!orders.TryGetValue(id, out var order))
            {
                continue;
            }

            if (!OrderHasCost(order.GrossPurchaseCost, order.BuyTrack, order.ThankCard))
            {
                continue;
            }

            sum += OrderProfit(order.Revenue, order.GrossPurchaseCost, order.BuyTrack, order.ThankCard);
        }

        return sum;
    }

    /// <summary>
    /// Gross NCC from <c>Orders.Charged*</c> — same rules as <see cref="OrderSupplierCost.ResolveGrossPurchaseCost(Order)"/>.
    /// </summary>
    public static decimal? GrossOfOrderProfitRow(OrderProfitRow o)
    {
        if (o.ChargedSupplierSubtotal.HasValue)
        {
            return (o.ChargedSupplierSubtotal ?? 0m)
                + (o.ChargedSupplierShippingFee ?? 0m)
                + (o.ChargedSupplierTax ?? 0m)
                + (o.ChargedSupplierRetailDeliveryFee ?? 0m)
                + (o.ChargedSupplierOnTimeGuarantee ?? 0m);
        }

        return OrderSupplierCost.GrossPurchaseCostFromLedger(
            o.ChargedSupplierTotal ?? 0m,
            o.ChargedSupplierWalletPrice,
            o.ChargedSupplierCouponPrice,
            o.ChargedSupplierMembershipDiscount,
            o.ChargedSupplierPointsDiscount);
    }

    /// <summary>
    /// Σ Profit over already-distinct order rows (1× / đơn). Matches per-row <c>profitAmount</c>
    /// for multi-Shein / joint shipment without adding the same order twice.
    /// </summary>
    public static decimal SumDistinctOrderProfits(IEnumerable<OrderProfitRow> orders)
    {
        decimal sum = 0m;
        foreach (var o in orders)
        {
            var gross = GrossOfOrderProfitRow(o);
            if (!OrderHasCost(gross, o.BuyTrackAmount, o.ThankCardAmount))
            {
                continue;
            }

            var revenue = OrderRevenue.Resolve(o.SellerNetAmount, o.TotalPaidAmount, o.Amount);
            sum += OrderProfit(revenue, gross, o.BuyTrackAmount, o.ThankCardAmount);
        }

        return sum;
    }
}
