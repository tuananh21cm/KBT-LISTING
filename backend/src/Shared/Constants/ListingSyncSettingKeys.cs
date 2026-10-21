namespace Shared.Constants;

/// <summary>
/// Setting keys for the two background 4Seller listing sync loops (managed via admin Settings).
///
/// <c>draft</c> — light pass that pulls newest drafts (page + batch-detail) on a minutes clock.
/// <c>published</c> — status-only pass that refreshes site status across published buckets inside a
/// create-date lookback window. Each loop has its own on/off and interval so one can run without
/// waiting for the other; a process-wide gate serializes the actual pull so seller leases do not collide.
/// </summary>
public static class ListingSyncSettingKeys
{
    // ─── Legacy keys (migrated away by DataSeeder; kept as constants for the migrate pass) ───

    /// <summary>Legacy single-loop enabled flag. Removed after migrate.</summary>
    public const string LegacyEnabled = "fourseller.listing_sync.enabled";

    /// <summary>Legacy single-loop interval. Removed after migrate.</summary>
    public const string LegacyIntervalMinutes = "fourseller.listing_sync.interval_minutes";

    /// <summary>Legacy draft include flag. Removed after migrate.</summary>
    public const string LegacySyncDraft = "fourseller.listing_sync.sync_draft";

    /// <summary>Legacy published include flag. Removed after migrate.</summary>
    public const string LegacySyncPublished = "fourseller.listing_sync.sync_published";

    // ─── Draft loop ──────────────────────────────────────────────────────────────────────────

    /// <summary>When true, the API server periodically syncs Draft listings (full scope).</summary>
    public const string DraftEnabled = "fourseller.listing_sync.draft.enabled";

    /// <summary>
    /// Minutes between draft sync runs when enabled.
    /// Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.
    /// Measured from the end of the previous run.
    /// </summary>
    public const string DraftIntervalMinutes = "fourseller.listing_sync.draft.interval_minutes";

    // ─── Published loop ──────────────────────────────────────────────────────────────────────

    /// <summary>When true, the API server periodically refreshes Published-tab listing statuses.</summary>
    public const string PublishedEnabled = "fourseller.listing_sync.published.enabled";

    /// <summary>
    /// Minutes between published status sync runs when enabled.
    /// Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.
    /// Measured from the end of the previous run.
    /// </summary>
    public const string PublishedIntervalMinutes = "fourseller.listing_sync.published.interval_minutes";

    /// <summary>
    /// VN calendar days (including today) for the published create-date window.
    /// Clamped to <see cref="MinPublishedLookbackDays"/>–<see cref="MaxPublishedLookbackDays"/>.
    /// </summary>
    public const string PublishedLookbackDays = "fourseller.listing_sync.published.lookback_days";

    // ─── Defaults / bounds ───────────────────────────────────────────────────────────────────

    /// <summary>Default minutes between draft sync runs.</summary>
    public const int DefaultDraftIntervalMinutes = 15;

    /// <summary>Default minutes between published status sync runs.</summary>
    public const int DefaultPublishedIntervalMinutes = 60;

    /// <summary>Default VN lookback days for the published loop.</summary>
    public const int DefaultPublishedLookbackDays = 10;

    /// <summary>Minimum minutes for either loop interval.</summary>
    public const int MinIntervalMinutes = 1;

    /// <summary>Maximum minutes for either loop interval (24 hours).</summary>
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Minimum lookback for the published loop.</summary>
    public const int MinPublishedLookbackDays = 1;

    /// <summary>
    /// Maximum lookback for the published loop.
    /// Must stay ≤ 31 because <c>AppTimeZones.GetVietnamLookbackUtcRange</c> clamps at 31.
    /// </summary>
    public const int MaxPublishedLookbackDays = 30;
}
