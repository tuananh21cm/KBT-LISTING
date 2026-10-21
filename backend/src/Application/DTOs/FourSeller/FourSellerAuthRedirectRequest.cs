namespace Application.DTOs.FourSeller;

/// <summary>
/// Request payload for 4Seller auth redirect URL API.
/// </summary>
public class FourSellerAuthRedirectRequest
{
    /// <summary>Platform code (lowercase, e.g. tiktok).</summary>
    public string PlatformCode { get; set; } = string.Empty;

    /// <summary>Shop display name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Target site/region codes.</summary>
    public IReadOnlyList<string> SiteList { get; set; } = [];

    /// <summary>Shopify shop URL placeholder or actual URL.</summary>
    public string ShopifyShop { get; set; } = string.Empty;
}
