using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// TikTok category catalog node synced from 4Seller meta API.
/// </summary>
public class TikTokCategory : BaseEntity
{
    /// <summary>
    /// Internal identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// External category ID from TikTok / 4Seller.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent external ID; null for root nodes.
    /// </summary>
    public string? ParentExternalId { get; set; }

    /// <summary>
    /// Marketplace site (US, UK, …).
    /// </summary>
    public string Site { get; set; } = "US";

    /// <summary>
    /// Whether this node can be selected for a listing.
    /// </summary>
    public bool IsLeaf { get; set; }

    /// <summary>
    /// Depth in the tree (0 = root).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Slash-separated path of external IDs from root to this node.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Whether the node was present in the latest sync.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// UTC timestamp of the last successful sync for this node.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Raw JSON snapshot of the 4Seller category node.
    /// </summary>
    public string? RawPayload { get; set; }
}
