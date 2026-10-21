using Shared.Constants;

namespace Application.DTOs.Listing;

/// <summary>Request body for listing sync.</summary>
public class ListingSyncRequest
{
    /// <summary>Optional: sync only this seller. Null = all logged-in cookies.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Platform (default tiktok).</summary>
    public string? Platform { get; set; }

    /// <summary>
    /// 4Seller page statuses to pull (draft, active, inactive, …).
    /// Empty defaults to all <see cref="Shared.Constants.ListingSiteStatuses.DefaultSyncStatuses"/>.
    /// </summary>
    public List<string>? Statuses { get; set; }

    /// <summary>Optional 4Seller shop id filter (empty = all).</summary>
    public string? ShopId { get; set; }

    /// <summary>
    /// Optional inclusive lower bound (UTC, typically a Vietnam business-day start).
    /// When set with <see cref="DateTo"/>, forwarded to 4Seller <c>createTime*</c> only
    /// as America/Los_Angeles wall-clock (same absolute window; not platformCreateTime*).
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>
    /// Optional inclusive upper bound (UTC).
    /// </summary>
    public DateTime? DateTo { get; set; }

    /// <summary>
    /// Cap on listings pulled <b>per status bucket</b> per seller.
    /// Null = paginate until all pages. Background / list Sync send an explicit cap.
    /// </summary>
    public int? MaxRecordsPerSeller { get; set; }

    /// <summary>
    /// Legacy: <c>light</c>/<c>status</c> → status-only; <c>full</c> → draft content pull.
    /// Prefer <see cref="SyncScope"/>.
    /// </summary>
    public string? SyncMode { get; set; }

    /// <summary>
    /// <c>full</c> = draft content (page + batch-detail); <c>status</c> = published status-only.
    /// When set, overrides <see cref="SyncMode"/>.
    /// </summary>
    public string? SyncScope { get; set; }

    /// <summary>
    /// Server-set: <c>manual</c> | <c>background</c> | <c>extension</c>.
    /// Controllers overwrite client values to <c>manual</c>.
    /// </summary>
    public string? SyncSource { get; set; }

    /// <summary>
    /// Optional explicit 4Seller listing ids. Asks each status bucket about just these rows, which is
    /// the only affordable way to re-check listings buried deep in a bucket sorted by update time.
    /// Set = the caller picked the rows, so the delta watermark is neither read nor written (pages
    /// filtered by id say nothing about how far a bucket has been scanned).
    /// </summary>
    public List<string>? ListingIds { get; set; }

    /// <summary>
    /// Fills <see cref="SyncScope"/> when the client omitted it: draft-only → <c>full</c>;
    /// otherwise <c>status</c> (legacy <c>light</c> default).
    /// </summary>
    public void ApplyDefaults()
    {
        if (!string.IsNullOrWhiteSpace(SyncScope))
        {
            SyncScope = ListingSyncScopes.Resolve(SyncScope, SyncMode);
            return;
        }

        if (string.Equals(SyncMode?.Trim(), ListingSyncModes.Full, StringComparison.OrdinalIgnoreCase))
        {
            SyncScope = ListingSyncScopes.Full;
            return;
        }

        var statuses = Statuses?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (statuses is { Count: 1 }
            && string.Equals(statuses[0], ListingSiteStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            SyncScope = ListingSyncScopes.Full;
            return;
        }

        if (string.IsNullOrWhiteSpace(SyncMode))
        {
            SyncMode = ListingSyncModes.Light;
        }

        SyncScope = ListingSyncScopes.Resolve(null, SyncMode);
    }
}
