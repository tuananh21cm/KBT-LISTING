namespace Application.DTOs.Listing;

/// <summary>
/// Request to delete multiple local listing mirrors (and on 4Seller when linked).
/// </summary>
public class BulkDeleteListingsRequest
{
    /// <summary>Local listing ids to delete.</summary>
    public List<Guid> Ids { get; set; } = [];
}
