namespace Application.DTOs.Shop;

/// <summary>
/// Bulk reorder request for all shops (first id = SortOrder 0).
/// </summary>
public class ReorderShopsDto
{
    /// <summary>Ordered shop IDs covering every shop currently in the system.</summary>
    public List<Guid> OrderedIds { get; set; } = [];
}
