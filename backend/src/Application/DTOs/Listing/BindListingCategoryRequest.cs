namespace Application.DTOs.Listing;

/// <summary>
/// Binds a TikTok category to a draft listing via 4Seller <c>batch-binging-category</c>.
/// </summary>
public class BindListingCategoryRequest
{
    /// <summary>
    /// Recent-use category row <c>id</c> from <c>/api/ai/recent-use-category</c>
    /// (sent upstream as bind <c>categoryId</c>).
    /// </summary>
    public long RecentCategoryId { get; set; }

    /// <summary>TikTok leaf category external id — used to update local mirror after bind.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Category display name — used to update local mirror after bind.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Optional breadcrumb path of external ids.</summary>
    public string? NodePathId { get; set; }
}
