namespace Application.DTOs.Shop;

/// <summary>
/// Admin diagnostic: orders whose <c>ShopId</c> is null, grouped by 4Seller shop id so ops can
/// see which reconnect / transfer left units unmapped.
/// </summary>
public class ShopMappingHealthDto
{
    /// <summary>Total orders with <c>ShopId == null</c>.</summary>
    public int UnlinkedOrderCount { get; set; }

    /// <summary>Groups of unlinked orders by <c>FourSellerShopId</c>.</summary>
    public List<ShopMappingHealthGroupDto> Groups { get; set; } = [];
}

/// <summary>One 4Seller shop id with orphaned local orders.</summary>
public class ShopMappingHealthGroupDto
{
    /// <summary>4Seller internal shop id on the order (null when order also lacks it).</summary>
    public long? FourSellerShopId { get; set; }

    /// <summary>Number of unlinked orders carrying this id.</summary>
    public int OrderCount { get; set; }

    /// <summary>Cookie / order owner currently stamped on those rows.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Display name of <see cref="OwnerId"/>.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Earliest order create time in the group.</summary>
    public DateTime? FirstOrderAt { get; set; }

    /// <summary>Latest order create time in the group.</summary>
    public DateTime? LastOrderAt { get; set; }

    /// <summary>
    /// True when a local <c>Shops</c> row already has this id as <c>FourSellerShopId</c>
    /// or <c>PreviousFourSellerShopId</c>.
    /// </summary>
    public bool HasLocalShop { get; set; }
}
