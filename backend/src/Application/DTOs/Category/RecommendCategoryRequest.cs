using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Category;

/// <summary>
/// Request AI category recommendation for a product title.
/// </summary>
public class RecommendCategoryRequest
{
    /// <summary>Marketplace site (US, UK, …).</summary>
    [Required]
    public string Site { get; set; } = "US";

    /// <summary>Product title.</summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Admin: seller whose 4Seller cookie/proxy to use (listing owner). Ignored on seller portal.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Optional 4Seller shop id — skips auto-pick of first Connected TikTok shop.
    /// </summary>
    public long? FourSellerShopId { get; set; }
}
