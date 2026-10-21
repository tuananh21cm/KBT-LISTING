using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Listing;

/// <summary>
/// Request AI rewrite of a product description from current form values.
/// </summary>
public class RewriteListingDescriptionRequest
{
    /// <summary>Product title (Product name).</summary>
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Current description (HTML or plain text from the editor).</summary>
    [Required]
    public string Description { get; set; } = string.Empty;

    /// <summary>Category leaf name or full path — grounds keywords in the right niche.</summary>
    [MaxLength(500)]
    public string? CategoryName { get; set; }

    /// <summary>Brand name; ignored when it is the "No Brand" placeholder.</summary>
    [MaxLength(255)]
    public string? BrandName { get; set; }

    /// <summary>Variant colors available for this listing.</summary>
    public List<string>? Colors { get; set; }

    /// <summary>Variant sizes available for this listing.</summary>
    public List<string>? Sizes { get; set; }

    /// <summary>Mapped category Specifics (material, fit, season…) the copy may cite as facts.</summary>
    public List<RewriteListingAttributeDto>? Attributes { get; set; }

    /// <summary>
    /// Optional OpenAI <c>seed</c> for Dev SEO harness only. Production callers leave null —
    /// the API then omits seed so listings stay diverse at rewrite temperature.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// A single product attribute passed to the rewrite prompt as a verified fact.
/// </summary>
public class RewriteListingAttributeDto
{
    /// <summary>Attribute display name (e.g. Material).</summary>
    [MaxLength(255)]
    public string? Name { get; set; }

    /// <summary>Attribute value (e.g. 95% Polyester).</summary>
    [MaxLength(1000)]
    public string? Value { get; set; }
}
