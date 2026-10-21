namespace Application.DTOs.Order;

/// <summary>
/// Updates supplier (NCC) cost breakdown + fulfil payment link.
/// Local Cost/ShippingFee/Tax/Ads are deprecated and ignored.
/// </summary>
public class UpdateOrderCostRequest
{
    /// <summary>Supplier platform order number.</summary>
    public string? SupplierOrderNo { get; set; }

    /// <summary>Supplier retail (list) price.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Supplier subtotal (giá nhập NCC).</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Supplier shipping fee.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>Optional on-time / delivery-guarantee fee.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon/promo discount (positive = amount reduced).</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership/club savings (positive = amount saved).</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed (positive = amount reduced).</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used (payment, not coupon).</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Supplier sales tax.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Supplier grand total charged (ledger Amount).</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>Purchase / fulfil account used for this order.</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Payment card used for this order.</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Order handler (người đi đơn) for sales attribution; null uses the current admin user.</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>
    /// When the purchase was made (UTC). Null keeps existing or defaults to UtcNow on first fulfil save.
    /// </summary>
    public DateTime? FulfilPurchasedAt { get; set; }
}
