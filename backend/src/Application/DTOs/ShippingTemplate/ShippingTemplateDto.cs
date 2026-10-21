namespace Application.DTOs.ShippingTemplate;

/// <summary>
/// Shipping template response DTO.
/// </summary>
public class ShippingTemplateDto
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
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

    /// <summary>Whether this is the user's default shipping template.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Create shipping template request.
/// </summary>
public class CreateShippingTemplateDto
{
    /// <summary>Display name.</summary>
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

    /// <summary>Mark as the user's default shipping template.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Update shipping template request.
/// </summary>
public class UpdateShippingTemplateDto
{
    /// <summary>Display name.</summary>
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

    /// <summary>Mark as the user's default shipping template.</summary>
    public bool IsDefault { get; set; }
}
