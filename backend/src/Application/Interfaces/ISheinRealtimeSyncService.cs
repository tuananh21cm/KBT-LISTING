namespace Application.Interfaces;

/// <summary>
/// Server-side SHEIN order sync: calls SHEIN with sessions stored on fulfil accounts (cookie +
/// proxy). Active for background ticks while <c>header_source = local</c>; also used by the
/// Sync Order button to sync one account immediately after session push.
/// </summary>
public interface ISheinRealtimeSyncService
{
    /// <summary>
    /// Runs one pass if settings enable local (server) sync and the interval has elapsed.
    /// Returns a human-readable summary, or null when the pass was skipped.
    /// </summary>
    Task<string?> RunIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs one fulfil account immediately (manual Sync Order in local mode).
    /// Does not require auto-sync <c>enabled</c>.
    /// </summary>
    Task<string> RunAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
}
