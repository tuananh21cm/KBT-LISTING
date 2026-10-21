namespace Application.DTOs.FourSeller;

/// <summary>
/// Pricing fields from 4Seller <c>GET /api/v2/order/detail</c>
/// (<c>orderAmountVo</c>, or fallback <c>costDetailVo</c>/<c>costVo</c> when amount Vo is null + shipping method).
/// </summary>
public class FourSellerOrderAmountDetailDto
{
    /// <summary>4Seller internal order id.</summary>
    public long OrderId { get; set; }

    /// <summary>Shipping method label from <c>orderVo.shippingMethod</c>.</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>Customer paid after vouchers.</summary>
    public decimal? TotalPaidAmount { get; set; }

    /// <summary>Product amount before discounts.</summary>
    public decimal ProductAmountBeforeDiscount { get; set; }

    /// <summary>Platform-funded product discount.</summary>
    public decimal ProductPlatformDiscount { get; set; }

    /// <summary>Seller-funded product discount.</summary>
    public decimal ProductSellerDiscount { get; set; }

    /// <summary>Shipping fee before discounts.</summary>
    public decimal ShippingFeeBeforeDiscount { get; set; }

    /// <summary>Seller-funded shipping discount.</summary>
    public decimal ShippingFeeSellerDiscount { get; set; }

    /// <summary>Platform tax.</summary>
    public decimal TotalTax { get; set; }

    /// <summary>Other fees.</summary>
    public decimal OtherFee { get; set; }

    /// <summary>Currency code when present.</summary>
    public string? Currency { get; set; }
}
