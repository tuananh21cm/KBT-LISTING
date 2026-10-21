namespace Application.DTOs.FourSeller;

/// <summary>
/// One item for 4Seller <c>POST /api/listing/tiktok/batch-binging-category</c>.
/// <see cref="CategoryId"/> is the recent-use row <c>id</c>, not the TikTok category id.
/// </summary>
public class FourSellerBindCategoryItemDto
{
    /// <summary>4Seller listing id.</summary>
    public long ListingId { get; set; }

    /// <summary>Recent-use category row id (from <c>recent-use-category</c>).</summary>
    public long CategoryId { get; set; }
}
