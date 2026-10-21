namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to delete 4Seller crawl move-drafts.
/// </summary>
public class FourSellerDeleteDraftsRequest
{
    /// <summary>Move-draft ids to delete.</summary>
    public List<long> MoveDraftIds { get; set; } = [];
}
