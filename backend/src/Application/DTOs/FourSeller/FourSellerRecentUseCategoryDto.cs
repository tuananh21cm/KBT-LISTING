namespace Application.DTOs.FourSeller;

/// <summary>
/// One row from 4Seller <c>GET /api/ai/recent-use-category</c>.
/// <see cref="Id"/> is what <c>batch-binging-category</c> expects as <c>categoryId</c>
/// (not TikTok <see cref="CategoryId"/>).
/// </summary>
public class FourSellerRecentUseCategoryDto
{
    /// <summary>4Seller recent-use row id — send this as bind <c>categoryId</c>.</summary>
    public long Id { get; set; }

    /// <summary>Marketplace site (US, UK, …).</summary>
    public string Site { get; set; } = "US";

    /// <summary>TikTok leaf category external id.</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Parent TikTok category id.</summary>
    public string? CategoryParentId { get; set; }

    /// <summary>Category display name.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Breadcrumb path labels.</summary>
    public string? NodePath { get; set; }

    /// <summary>Breadcrumb path of external ids.</summary>
    public string? NodePathId { get; set; }

    /// <summary>1 = leaf category.</summary>
    public int LeafCategory { get; set; }

    /// <summary>Category version string from 4Seller (e.g. v2).</summary>
    public string? CategoryVersion { get; set; }
}
