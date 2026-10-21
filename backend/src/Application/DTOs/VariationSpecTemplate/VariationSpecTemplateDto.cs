namespace Application.DTOs.VariationSpecTemplate;

/// <summary>
/// Variation spec template response DTO.
/// </summary>
public class VariationSpecTemplateDto
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Spec type: Color, Size, or Number of Items.</summary>
    public string SpecType { get; set; } = string.Empty;

    /// <summary>Preset value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Display order.</summary>
    public int SortOrder { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Create variation spec template request.
/// </summary>
public class CreateVariationSpecTemplateDto
{
    /// <summary>Spec type.</summary>
    public string SpecType { get; set; } = string.Empty;

    /// <summary>Preset value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional sort order; if null, appends to end.</summary>
    public int? SortOrder { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Update variation spec template request.
/// </summary>
public class UpdateVariationSpecTemplateDto
{
    /// <summary>Preset value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Display order.</summary>
    public int SortOrder { get; set; }

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Bulk reorder request for one spec type.
/// </summary>
public class ReorderVariationSpecTemplatesDto
{
    /// <summary>Spec type being reordered.</summary>
    public string SpecType { get; set; } = string.Empty;

    /// <summary>Ordered template IDs (first = SortOrder 0).</summary>
    public List<Guid> OrderedIds { get; set; } = [];
}
