namespace Domain.Orders;

/// <summary>
/// Computes seller net payout from TikTok / 4Seller <c>orderAmountVo</c> fields.
/// </summary>
public static class TikTokSellerNetCalculator
{
    /// <summary>Seller-funded shipping (seller pays / collects shipping).</summary>
    public const string ShipTypeSeller = "seller";

    /// <summary>TikTok / platform shipping (seller pays platform shipping net).</summary>
    public const string ShipTypeTikTok = "tiktok";

    /// <summary>
    /// Detects shipping type from 4Seller <c>shippingMethod</c> label.
    /// Defaults to seller shipping when unknown.
    /// </summary>
    public static string ResolveShipType(string? shippingMethod)
    {
        if (string.IsNullOrWhiteSpace(shippingMethod))
        {
            return ShipTypeSeller;
        }

        var lower = shippingMethod.Trim().ToLowerInvariant();
        if (lower.Contains("tiktok shipping", StringComparison.Ordinal) ||
            lower.Contains("platform shipping", StringComparison.Ordinal))
        {
            return ShipTypeTikTok;
        }

        return ShipTypeSeller;
    }

    /// <summary>
    /// True when the order uses TikTok / platform shipping — seller must not Mark Ship
    /// (<c>to-ship-mark-ship</c>); TikTok publishes tracking.
    /// </summary>
    public static bool IsTikTokPlatformShipping(string? shippingMethod) =>
        ResolveShipType(shippingMethod) == ShipTypeTikTok;

    /// <summary>
    /// Seller ship:
    /// productBefore − productSellerDisc + (shipBefore − shipSellerDisc) − tax − other.
    /// TikTok / platform ship (fee ship không vào thực nhận):
    /// productBefore − productSellerDisc − tax − other.
    /// <paramref name="productPlatformDiscount"/> is display-only (platform-funded; not in seller net).
    /// </summary>
    public static decimal Compute(
        decimal productAmountBeforeDiscount,
        decimal productSellerDiscount,
        decimal productPlatformDiscount,
        decimal shippingFeeBeforeDiscount,
        decimal shippingFeeSellerDiscount,
        decimal totalTax,
        decimal otherFee,
        string? shippingMethod)
    {
        _ = productPlatformDiscount;
        var netShipping = shippingFeeBeforeDiscount - shippingFeeSellerDiscount;
        var productNet = productAmountBeforeDiscount - productSellerDiscount;
        var shipType = ResolveShipType(shippingMethod);

        return shipType == ShipTypeTikTok
            ? productNet - totalTax - otherFee
            : productNet + netShipping - totalTax - otherFee;
    }

    /// <summary>
    /// True when 4Seller detail actually has TikTok money — all-zero
    /// <c>costVo</c>/<c>orderAmountVo</c> (seen on some refunds) is not a successful enrich.
    /// </summary>
    public static bool HasMeaningfulBreakdown(
        decimal productAmountBeforeDiscount,
        decimal? totalPaidAmount,
        decimal shippingFeeBeforeDiscount,
        decimal productSellerDiscount)
    {
        return productAmountBeforeDiscount != 0
            || (totalPaidAmount ?? 0) != 0
            || shippingFeeBeforeDiscount != 0
            || productSellerDiscount != 0;
    }

    /// <summary>
    /// Keep the last non-zero list amount when 4Seller later sends 0 (refund payloads
    /// sometimes wipe <c>amount</c>/<c>itemPrice</c> while the paid order still has history).
    /// </summary>
    public static decimal KeepLastNonZero(decimal current, decimal incoming) =>
        incoming != 0 || current == 0 ? incoming : current;
}
