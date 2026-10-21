using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Precomputed «đề xuất hủy list» counters for one shop on one Vietnam snapshot day.
/// Written after Compass analytics sync (and refreshed when the delist panel scores), so the
/// shop-task board can show <c>delistCount/scanned</c> without re-scoring every listing.
/// </summary>
public class ListingDelistDaySummary : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shop the counters belong to.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Vietnam calendar day matching <see cref="ListingAnalyticsSnapshot.SnapshotDate"/>.</summary>
    public DateOnly SnapshotDate { get; set; }

    /// <summary>Products scored on that snapshot day (capped at the scorer scan limit).</summary>
    public int Scanned { get; set; }

    /// <summary>Rows with verdict <c>delist</c> (score ≥ 70).</summary>
    public int DelistCount { get; set; }

    /// <summary>Rows with verdict <c>review</c> (score 50–69).</summary>
    public int ReviewCount { get; set; }

    /// <summary>Skipped because they still sell.</summary>
    public int SkippedWinners { get; set; }

    /// <summary>Skipped because too new or missing listing age.</summary>
    public int SkippedTooNew { get; set; }

    /// <summary>Scored but below the review threshold.</summary>
    public int SkippedHealthy { get; set; }

    /// <summary>
    /// How many delist candidates to actually cancel to fit keywords waiting
    /// (<c>min(delistCount, max(0, listNowKeywords − remainingSlots))</c>).
    /// </summary>
    public int RecommendedDelistCount { get; set; }

    /// <summary>TikTok publish limit total at compute time (audit).</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>TikTok published count at compute time (audit).</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>UTC time the counters were last written.</summary>
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Shop navigation.</summary>
    public Shop Shop { get; set; } = null!;
}
