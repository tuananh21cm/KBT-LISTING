namespace Application.DTOs.FourSeller;

/// <summary>
/// Carrier from 4Seller <c>list-platform-shipping-service</c>.
/// </summary>
public class FourSellerShippingServiceDto
{
    /// <summary>4Seller carrier id.</summary>
    public long Id { get; set; }

    /// <summary>Carrier display name (used as shippingService in mark-ship).</summary>
    public string Carrier { get; set; } = string.Empty;

    /// <summary>Platform code (e.g. TIKTOK, AMAZON).</summary>
    public string? Platform { get; set; }

    /// <summary>Region (e.g. US).</summary>
    public string? Region { get; set; }
}
