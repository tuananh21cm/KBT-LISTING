namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to delete 4Seller listings (draft/online/offline).
/// </summary>
public class FourSellerDeleteListingsRequest
{
    /// <summary>Listing ids to delete on 4Seller.</summary>
    public List<long> ListingIds { get; set; } = [];
}
