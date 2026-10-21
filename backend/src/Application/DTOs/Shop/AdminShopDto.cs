namespace Application.DTOs.Shop;

/// <summary>
/// Shop response DTO for admin views.
/// </summary>
public class AdminShopDto : ShopDto
{
    /// <summary>Owner user identifier.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner display name.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Owner username.</summary>
    public string OwnerUserName { get; set; } = string.Empty;

    /// <summary>Unique shop purchase cost on the company ledger. Null when the shop has no row.</summary>
    public decimal? AcquisitionCost { get; set; }
}
