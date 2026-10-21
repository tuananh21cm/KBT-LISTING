namespace Application.DTOs.Order;

/// <summary>
/// Runtime status of automatic order sync (background + health keep-alive).
/// </summary>
public sealed class OrderSyncRuntimeStatusDto
{
    /// <summary>Whether auto sync is enabled in Settings (last read).</summary>
    public bool? Enabled { get; init; }

    /// <summary>Configured interval in minutes (last read).</summary>
    public int? IntervalMinutes { get; init; }

    /// <summary>UTC when the last sync attempt started.</summary>
    public DateTimeOffset? LastStartedUtc { get; init; }

    /// <summary>UTC when the last sync attempt finished successfully (not skipped).</summary>
    public DateTimeOffset? LastFinishedUtc { get; init; }

    /// <summary>Short summary of the last finished sync.</summary>
    public string? LastSummary { get; init; }

    /// <summary>True while SyncAsync (4Seller pull) is in progress — not during interval checks.</summary>
    public bool IsRunning { get; init; }

    /// <summary>UTC when the next auto sync becomes due (lastFinished + interval), if known.</summary>
    public DateTimeOffset? NextDueUtc { get; init; }

    /// <summary>
    /// Seconds past <see cref="NextDueUtc"/> while idle (not running). Null when on-time or syncing.
    /// Prolonged overdue usually means keep-alive/BackgroundService stopped or gate/sync failures.
    /// </summary>
    public int? SecondsOverdue { get; init; }
}
