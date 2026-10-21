using Domain.Entities;

namespace Domain.Orders;

/// <summary>
/// System-wide order revenue = TikTok seller net payout (after platform/seller fees),
/// with fallback to customer paid while pricing detail is not yet enriched.
/// </summary>
public static class OrderRevenue
{
    /// <summary>
    /// Resolves revenue for stats, settlement, Telegram, and UI Rev.
    /// </summary>
    public static decimal Resolve(decimal? sellerNetAmount, decimal totalPaidAmount, decimal amount) =>
        sellerNetAmount ?? (totalPaidAmount != 0 ? totalPaidAmount : amount);

    /// <summary>
    /// Resolves revenue from an <see cref="Order"/> entity.
    /// </summary>
    public static decimal Resolve(Order order) =>
        Resolve(order.SellerNetAmount, order.TotalPaidAmount, order.Amount);
}
