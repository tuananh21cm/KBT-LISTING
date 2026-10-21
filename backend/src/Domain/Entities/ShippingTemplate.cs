using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Per-user reusable shipping preset (weight + package dimensions) for product forms.
/// </summary>
public class ShippingTemplate : BaseEntity
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner user (templates are private per account).</summary>
    public Guid CreatedById { get; set; }

    /// <summary>Display name (unique per user).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Package weight.</summary>
    public decimal PackageWeight { get; set; }

    /// <summary>Package length.</summary>
    public decimal PackageLength { get; set; }

    /// <summary>Package width.</summary>
    public decimal PackageWidth { get; set; }

    /// <summary>Package height.</summary>
    public decimal PackageHeight { get; set; }

    /// <summary>Weight unit (lb / kg).</summary>
    public string WeightUnit { get; set; } = "lb";

    /// <summary>Dimension unit (inch / cm).</summary>
    public string DimensionUnit { get; set; } = "inch";

    /// <summary>Whether this is the user's default shipping template (at most one per user).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Owner navigation.</summary>
    public ApplicationUser CreatedBy { get; set; } = null!;
}
