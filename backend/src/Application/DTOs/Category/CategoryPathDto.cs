namespace Application.DTOs.Category;

/// <summary>
/// Lazy-cascader seed: the option tree pre-expanded from root down to a target
/// node, plus the ordered external-ID path so the UI can set its selected value.
/// </summary>
public class CategoryPathDto
{
    /// <summary>Ordered external IDs from root to target (inclusive).</summary>
    public List<string> PathIds { get; set; } = [];

    /// <summary>
    /// Root options with children pre-loaded along the path to target;
    /// unrelated branches stay collapsed for on-demand loading.
    /// </summary>
    public List<CategoryTreeNodeDto> Options { get; set; } = [];
}
