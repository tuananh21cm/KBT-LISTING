namespace Application.DTOs.FourSeller;

/// <summary>
/// A single TikTok category node from 4Seller get-category-list.
/// </summary>
public class FourSellerCategoryNodeDto
{
    /// <summary>TikTok category ID (`categoryId`).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name (`categoryName`).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent TikTok category ID (`categoryParentId`).</summary>
    public string? ParentCategoryId { get; set; }

    /// <summary>Whether the node is a selectable leaf (`leafCategory == 1`).</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Slash-separated category ID path (`nodePathId`).</summary>
    public string? NodePathId { get; set; }

    /// <summary>Human-readable path (`nodePath`).</summary>
    public string? NodePath { get; set; }

    /// <summary>Raw JSON of the original node for debugging / re-sync.</summary>
    public string? RawPayload { get; set; }
}
