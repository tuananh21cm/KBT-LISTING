namespace Application.DTOs.Category;

/// <summary>
/// AI-recommended TikTok category for Cascader prefill.
/// </summary>
public class RecommendCategoryResultDto
{
    /// <summary>TikTok category external ID (leaf).</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Category display name.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Breadcrumb path labels.</summary>
    public string? NodePath { get; set; }

    /// <summary>Path of external IDs root→leaf for Cascader.</summary>
    public List<string> PathIds { get; set; } = [];

    /// <summary>Whether the node is a leaf.</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Marketplace site.</summary>
    public string Site { get; set; } = "US";

    /// <summary>
    /// True when the AI category exists in the synced <c>TikTokCategories</c> catalog,
    /// is active and is a leaf — i.e. safe to publish with.
    /// </summary>
    public bool IsValidated { get; set; }

    /// <summary>Why validation failed (null when <see cref="IsValidated"/> is true).</summary>
    public string? ValidationWarning { get; set; }

    /// <summary>Slash-separated path of external IDs from the catalog (root to leaf).</summary>
    public string? CategoryPath { get; set; }

    /// <summary>
    /// When set, 4Seller AI returned this leaf but Settings <c>category_blocked_remap</c> replaced it
    /// with <see cref="CategoryId"/> because the original leaf cannot be published into.
    /// </summary>
    public string? ReplacedFromCategoryId { get; set; }

    /// <summary>Display name of <see cref="ReplacedFromCategoryId"/> when known.</summary>
    public string? ReplacedFromCategoryName { get; set; }

    /// <summary>Human-readable reason the category was remapped (for UI toast / run log).</summary>
    public string? ReplacementReason { get; set; }
}
