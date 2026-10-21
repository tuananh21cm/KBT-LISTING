using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Shop membership for a canonical TikTok trending keyword.
/// Unique per shop + keyword so two shops/sellers share one <see cref="ProductOpportunity"/>.
/// </summary>
public class ProductOpportunityShopLink : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shop that synced this keyword.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Canonical keyword row.</summary>
    public Guid ProductOpportunityId { get; set; }

    /// <summary>Shop navigation.</summary>
    public Shop Shop { get; set; } = null!;

    /// <summary>Keyword navigation.</summary>
    public ProductOpportunity ProductOpportunity { get; set; } = null!;
}
