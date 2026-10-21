namespace Application.DTOs.FourSeller;

/// <summary>
/// One order payload for 4Seller <c>to-ship-mark-ship</c>.
/// </summary>
public class FourSellerMarkShipItemDto
{
    /// <summary>4Seller order id.</summary>
    public long Id { get; set; }

    /// <summary>Platform lower-case (e.g. tiktok).</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>4Seller shop id.</summary>
    public long ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Platform order id.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Platform order number (same as id when folded).</summary>
    public string? PlatformOrderNo { get; set; }

    /// <summary>Carrier name.</summary>
    public string ShippingService { get; set; } = string.Empty;

    /// <summary>Tracking number.</summary>
    public string TrackingNo { get; set; } = string.Empty;

    /// <summary>Optional carrier tracking URL (4Seller accepts empty string).</summary>
    public string? TrackingUrl { get; set; }
}
