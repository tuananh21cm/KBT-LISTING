namespace Application.Interfaces;

/// <summary>
/// Schedules best-effort listing sync after browser-extension <c>post-html</c>
/// or after Save Draft / Publish / Update / Deactivate / Duplicate.
/// Debounces per seller so rapid calls collapse into one 4Seller pull.
/// </summary>
public interface IPostHtmlDraftSyncScheduler
{
    /// <summary>
    /// Queues a non-blocking sync for <paramref name="ownerId"/> (never awaits the pull).
    /// Repeated calls within the debounce window reset the timer; one <c>SyncAsync</c> runs
    /// using the statuses from the <b>last</b> schedule call.
    /// </summary>
    /// <param name="ownerId">Listing owner (seller) whose cookie is used for the pull.</param>
    /// <param name="statuses">
    /// 4Seller page statuses to pull. Null/empty defaults to <c>draft</c> only
    /// (post-html behaviour).
    /// </param>
    /// <param name="debounce">
    /// Quiet window before pull. Null = default 12s (post-html coalesce).
    /// Zero = start immediately (admin push actions — response already returned).
    /// </param>
    /// <param name="syncScope">
    /// <c>full</c> (draft content) or <c>status</c> (published status-only). Null = infer from
    /// <paramref name="statuses"/> (draft-only → full, otherwise status).
    /// </param>
    /// <param name="listingIds">
    /// Optional 4Seller listing ids to re-check (bypass delta watermark). Merged across debounced
    /// schedules for the same seller.
    /// </param>
    void Schedule(
        Guid ownerId,
        IReadOnlyList<string>? statuses = null,
        TimeSpan? debounce = null,
        string? syncScope = null,
        IReadOnlyList<string>? listingIds = null);
}
