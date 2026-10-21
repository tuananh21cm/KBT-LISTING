namespace Application.DTOs.FourSeller;

/// <summary>
/// Recommended TikTok category from 4Seller AI.
/// </summary>
public class FourSellerAiCategoryResultDto
{
    /// <summary>TikTok category external ID (leaf).</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Category display name.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Breadcrumb path labels, e.g. Womenswear/.../Adaptive Panties.</summary>
    public string? NodePath { get; set; }

    /// <summary>Breadcrumb path of external IDs, slash-separated.</summary>
    public string? NodePathId { get; set; }

    /// <summary>Path of external IDs root→leaf for Cascader.</summary>
    public List<string> PathIds { get; set; } = [];

    /// <summary>Whether the node is a leaf.</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Marketplace site.</summary>
    public string Site { get; set; } = "US";
}
