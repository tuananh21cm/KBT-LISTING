namespace Application.DTOs.Category;

/// <summary>
/// Flat TikTok category row for admin list / search.
/// </summary>
public class CategoryDto
{
    /// <summary>Internal ID.</summary>
    public Guid Id { get; set; }

    /// <summary>External TikTok / 4Seller category ID.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent external ID; null for roots.</summary>
    public string? ParentExternalId { get; set; }

    /// <summary>Marketplace site.</summary>
    public string Site { get; set; } = string.Empty;

    /// <summary>Whether selectable for listings.</summary>
    public bool IsLeaf { get; set; }

    /// <summary>Tree depth (0 = root).</summary>
    public int Level { get; set; }

    /// <summary>Slash-separated external ID path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Slash-separated display names along <see cref="Path"/> (filled on search).
    /// </summary>
    public string? PathLabel { get; set; }

    /// <summary>Present in latest sync.</summary>
    public bool IsActive { get; set; }

    /// <summary>Last sync UTC.</summary>
    public DateTime? LastSyncedAt { get; set; }
}
