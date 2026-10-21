namespace Application.DTOs.Listing;

/// <summary>
/// Lightweight tab badges for the browser extension (no listing rows).
/// </summary>
public class ListingTabCountsDto
{
    /// <summary>Local draft count for the selected 4Seller shops.</summary>
    public int DraftTotal { get; set; }

    /// <summary>
    /// TikTok published listing count (<c>publish_limit.published_num</c>), same as shop view Listing.
    /// </summary>
    public int PublishLimitPublished { get; set; }

    /// <summary>
    /// TikTok listing cap (<c>publish_limit.total_limit</c>), same as shop view Listing.
    /// </summary>
    public int PublishLimitTotal { get; set; }
}
