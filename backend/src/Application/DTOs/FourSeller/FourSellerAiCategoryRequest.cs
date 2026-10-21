namespace Application.DTOs.FourSeller;

/// <summary>
/// Request body for 4Seller AI TikTok category recommendation.
/// </summary>
public class FourSellerAiCategoryRequest
{
    /// <summary>4Seller numeric shop id (from shop list record.Id).</summary>
    public long ShopId { get; set; }

    /// <summary>Marketplace site (US, UK, …).</summary>
    public string Site { get; set; } = "US";

    /// <summary>Product title used for AI recommendation.</summary>
    public string Title { get; set; } = string.Empty;
}
