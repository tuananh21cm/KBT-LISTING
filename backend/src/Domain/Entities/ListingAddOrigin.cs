using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Staging origin for a post-html add, keyed by owner + source URL + 4Seller shop.
/// Copied onto <see cref="Listing.AddedFrom"/> after draft sync when <see cref="Listing.SourceUrl"/> is present.
/// </summary>
public class ListingAddOrigin : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Seller who posted HTML (JWT owner).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Stable source key (Shein <c>p:{goodsId}</c>).</summary>
    public string SourceUrlKey { get; set; } = string.Empty;

    /// <summary>4Seller shop id from post-html <c>shopIdList</c>.</summary>
    public long FourSellerShopId { get; set; }

    /// <summary><c>recommended</c> or <c>manual</c>.</summary>
    public string AddedFrom { get; set; } = string.Empty;

    /// <summary>Owner navigation.</summary>
    public ApplicationUser Owner { get; set; } = null!;
}
