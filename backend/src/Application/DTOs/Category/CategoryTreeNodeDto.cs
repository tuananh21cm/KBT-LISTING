namespace Application.DTOs.Category;

/// <summary>
/// Tree node for Cascader / Tree UI.
/// </summary>
public class CategoryTreeNodeDto
{
    /// <summary>External category ID (Cascader value).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Whether this is a selectable leaf.</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Child nodes.</summary>
    public List<CategoryTreeNodeDto> Children { get; set; } = [];
}
