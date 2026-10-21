namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to move scraped drafts onto 4Seller platform shops.
/// </summary>
public class FourSellerSaveToPlatformRequest
{
    /// <summary>Move-draft ids to push.</summary>
    public List<long> MoveDraftId { get; set; } = [];

    /// <summary>4Seller internal shop ids (record.Id).</summary>
    public List<long> ShopIdList { get; set; } = [];
}
