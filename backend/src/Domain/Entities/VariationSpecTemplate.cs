using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Reusable variation spec preset (Color / Size / Number of Items) with display order.
/// </summary>
public class VariationSpecTemplate : BaseEntity
{
    /// <summary>Template identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Spec type: Color, Size, or Number of Items.
    /// </summary>
    public string SpecType { get; set; } = string.Empty;

    /// <summary>Preset value (e.g. Pink, XL, 2).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Display order within SpecType (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether the preset is available for selection.</summary>
    public bool IsActive { get; set; } = true;
}
