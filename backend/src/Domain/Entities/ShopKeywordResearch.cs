using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Persisted «Shop này bán gì» result for one shop. One row per shop; overwritten on re-analyze.
/// Survives keyword snapshot replace so sellers can reopen the drawer without calling OpenAI.
/// Individual <c>listNow</c>/<c>upcoming</c> items are pruned when the seller deletes that
/// keyword or unsaves an audience-suggest keyword that has no snapshot.
/// </summary>
public class ShopKeywordResearch : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning shop (unique).</summary>
    public Guid ShopId { get; set; }

    /// <summary>Serialized <c>ShopKeywordResearchDto</c> (jsonb, camelCase).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// <c>Shops.ProductOpportunitiesSyncedAt</c> at analyze time — used to flag a newer snapshot.
    /// </summary>
    public DateTime? SnapshotSyncedAt { get; set; }

    /// <summary>When this analysis was last written (UTC).</summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>Shop navigation.</summary>
    public Shop Shop { get; set; } = null!;
}
