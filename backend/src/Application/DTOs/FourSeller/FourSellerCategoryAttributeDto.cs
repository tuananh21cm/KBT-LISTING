namespace Application.DTOs.FourSeller;

/// <summary>
/// Normalized TikTok category attribute from 4Seller
/// <c>GET /api/meta/tiktok/get-category-attribute-list-by-id</c>.
/// </summary>
public class FourSellerCategoryAttributeDto
{
    /// <summary>TikTok attribute id.</summary>
    public string AttributeId { get; set; } = string.Empty;

    /// <summary>Display / schema attribute name.</summary>
    public string AttributeName { get; set; } = string.Empty;

    /// <summary>Whether the attribute is required for the category.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Whether multiple values may be selected.</summary>
    public bool IsMultiple { get; set; }

    /// <summary>Whether custom (free-text) values are allowed.</summary>
    public bool IsCustomizable { get; set; }

    /// <summary>Attribute type / input hint when present (e.g. <c>2</c> = sale prop Color/Size).</summary>
    public string? AttributeType { get; set; }

    /// <summary>
    /// Raw <c>requirementConditions</c> JSON from 4Seller (string or array). Empty when unconditional.
    /// Example: <c>[{"condition_type":"VALUE_ID_MATCH","attribute_id":"101400","attribute_value_id":"1000058"}]</c>.
    /// </summary>
    public string? RequirementConditionsJson { get; set; }

    /// <summary>Allowed enum values (may be empty for free-text attrs).</summary>
    public List<FourSellerCategoryAttributeValueDto> Values { get; set; } = [];
}

/// <summary>Allowed value under a category attribute.</summary>
public class FourSellerCategoryAttributeValueDto
{
    /// <summary>TikTok value id (may be empty for customizable free-text).</summary>
    public string ValueId { get; set; } = string.Empty;

    /// <summary>Display value name.</summary>
    public string ValueName { get; set; } = string.Empty;
}
