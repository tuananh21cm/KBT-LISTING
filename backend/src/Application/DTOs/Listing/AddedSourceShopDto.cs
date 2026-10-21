namespace Application.DTOs.Listing;

/// <summary>
/// Minimal row for extension "already added" sync: source URL + 4Seller shop id only.
/// </summary>
public class AddedSourceShopDto
{
    /// <summary>Source product URL on the listing mirror.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>4Seller shop id (same as extension multi-select / post-html shopIdList).</summary>
    public long FourSellerShopId { get; set; }
}
