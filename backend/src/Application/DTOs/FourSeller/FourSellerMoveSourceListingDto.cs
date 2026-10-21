namespace Application.DTOs.FourSeller;

/// <summary>
/// One row from 4Seller <c>POST /api/listing/tiktok/batch-get-move-source-listing</c>.
/// Used when a draft listing still needs a TikTok category before variants/prices appear.
/// </summary>
public class FourSellerMoveSourceListingDto
{
    /// <summary>Whether source category already matched a target category.</summary>
    public bool Match { get; set; }

    /// <summary>4Seller match / mapping id when present.</summary>
    public long? MatchId { get; set; }

    /// <summary>Source marketplace category id (e.g. SHEIN).</summary>
    public string? SourceCategoryId { get; set; }

    /// <summary>Source marketplace category name.</summary>
    public string? SourceCategoryName { get; set; }

    /// <summary>Source marketplace category path.</summary>
    public string? SourceCategoryPath { get; set; }

    /// <summary>Source platform code (e.g. SHEIN).</summary>
    public string? SourcePlatform { get; set; }

    /// <summary>Target 4Seller shop id.</summary>
    public long TargetShopId { get; set; }

    /// <summary>Target shop display name.</summary>
    public string? TargetShopName { get; set; }

    /// <summary>Target 4Seller listing id.</summary>
    public long TargetListingId { get; set; }

    /// <summary>Listing title.</summary>
    public string? ListingName { get; set; }

    /// <summary>Listing image URL.</summary>
    public string? ListingUrl { get; set; }

    /// <summary>Already-bound TikTok category id (null when user must pick).</summary>
    public string? TargetCategoryId { get; set; }

    /// <summary>Already-bound TikTok category name.</summary>
    public string? TargetCategoryName { get; set; }

    /// <summary>Already-bound TikTok category path.</summary>
    public string? TargetCategoryPath { get; set; }

    /// <summary>Already-bound TikTok category node path ids.</summary>
    public string? TargetCategoryNodePathId { get; set; }
}
