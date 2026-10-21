namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to fetch TikTok categories from 4Seller for a given parent.
/// </summary>
public class FourSellerCategoryListRequest
{
    /// <summary>Marketplace site (US, UK, …).</summary>
    public string Site { get; set; } = "US";

    /// <summary>Parent category ID; 0 for roots.</summary>
    public string ParentId { get; set; } = "0";

    /// <summary>4Seller numeric shop ID.</summary>
    public long ShopId { get; set; }
}
