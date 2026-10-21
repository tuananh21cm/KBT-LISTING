namespace Application.DTOs.FourSeller;

/// <summary>
/// Input for matching a local shop against 4Seller shop list.
/// </summary>
public class FourSellerShopCheckRequest
{
    /// <summary>Local shop display name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Local platform code (e.g. TikTok).</summary>
    public string PlatformCode { get; set; } = string.Empty;

    /// <summary>External shop ID on marketplace.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Stored 4Seller shop / partner ID.</summary>
    public string? FourSellerShopId { get; set; }

    /// <summary>Shop region (e.g. US).</summary>
    public string Region { get; set; } = "US";
}
