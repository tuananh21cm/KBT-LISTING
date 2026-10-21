namespace Shared.Constants;

/// <summary>
/// Setting keys for the two background 4Seller order sync loops (managed via admin Settings).
///
/// <c>new</c> — light pass that catches fresh / open / recently canceled orders on a short
/// create-date lookback (minutes). <c>status</c> — heavier pass that refreshes platform
/// commerce status across all six buckets on a long lookback (hours). Each loop has its own
/// on/off, clock and account batch size so one can run without waiting for the other.
/// </summary>
public static class OrderSyncSettingKeys
{
    // ─── Legacy keys (migrated away by DataSeeder; kept as constants for the migrate pass) ───

    /// <summary>Legacy single-loop enabled flag. Removed after migrate.</summary>
    public const string LegacyEnabled = "fourseller.order_sync.enabled";

    /// <summary>Legacy single-loop interval. Removed after migrate.</summary>
    public const string LegacyIntervalMinutes = "fourseller.order_sync.interval_minutes";

    /// <summary>Legacy single-loop lookback. Removed after migrate.</summary>
    public const string LegacyLookbackDays = "fourseller.order_sync.lookback_days";

    // ─── New-orders loop ─────────────────────────────────────────────────────────────────────

    /// <summary>When true, the API server periodically syncs new / open / recent canceled orders.</summary>
    public const string NewEnabled = "fourseller.order_sync.new.enabled";

    /// <summary>Minutes between new-order sync runs. Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.</summary>
    public const string NewIntervalMinutes = "fourseller.order_sync.new.interval_minutes";

    /// <summary>
    /// VN calendar days (including today) for the new-orders create-date window.
    /// Clamped to <see cref="MinNewLookbackDays"/>–<see cref="MaxNewLookbackDays"/>.
    /// </summary>
    public const string NewLookbackDays = "fourseller.order_sync.new.lookback_days";

    /// <summary>
    /// How many 4Seller accounts run in parallel inside one new-orders pass.
    /// Clamped to <see cref="MinBatchSize"/>–<see cref="MaxBatchSize"/>.
    /// </summary>
    public const string NewBatchSize = "fourseller.order_sync.new.batch_size";

    // ─── Status loop ─────────────────────────────────────────────────────────────────────────

    /// <summary>When true, the API server periodically refreshes platform status across all six buckets.</summary>
    public const string StatusEnabled = "fourseller.order_sync.status.enabled";

    /// <summary>Hours between status sync runs. Clamped to <see cref="MinIntervalHours"/>–<see cref="MaxIntervalHours"/>.</summary>
    public const string StatusIntervalHours = "fourseller.order_sync.status.interval_hours";

    /// <summary>
    /// VN calendar days (including today) for the status create-date window.
    /// Clamped to <see cref="MinStatusLookbackDays"/>–<see cref="MaxStatusLookbackDays"/>.
    /// </summary>
    public const string StatusLookbackDays = "fourseller.order_sync.status.lookback_days";

    /// <summary>
    /// How many 4Seller accounts run in parallel inside one status pass.
    /// Clamped to <see cref="MinBatchSize"/>–<see cref="MaxBatchSize"/>.
    /// </summary>
    public const string StatusBatchSize = "fourseller.order_sync.status.batch_size";

    // ─── Watchdog ────────────────────────────────────────────────────────────────────────────

    /// <summary>When true, a stalled sync loop or a crashed background loop raises a Telegram alert.</summary>
    public const string WatchdogEnabled = "fourseller.order_sync.watchdog.enabled";

    /// <summary>
    /// Minutes past the next due time before the watchdog alerts.
    /// Clamped to <see cref="MinWatchdogOverdueMinutes"/>–<see cref="MaxWatchdogOverdueMinutes"/>.
    /// </summary>
    public const string WatchdogOverdueMinutes = "fourseller.order_sync.watchdog.overdue_minutes";

    // ─── Defaults / bounds ───────────────────────────────────────────────────────────────────

    /// <summary>Default minutes between new-order sync runs.</summary>
    public const int DefaultNewIntervalMinutes = 5;

    /// <summary>Default VN lookback days for the new-orders loop.</summary>
    public const int DefaultNewLookbackDays = 2;

    /// <summary>Default account batch size for the new-orders loop.</summary>
    public const int DefaultNewBatchSize = 15;

    /// <summary>Default hours between status sync runs.</summary>
    public const int DefaultStatusIntervalHours = 12;

    /// <summary>Default VN lookback days for the status loop.</summary>
    public const int DefaultStatusLookbackDays = 30;

    /// <summary>Default account batch size for the status loop.</summary>
    public const int DefaultStatusBatchSize = 10;

    /// <summary>Minimum minutes for the new-orders interval.</summary>
    public const int MinIntervalMinutes = 1;

    /// <summary>Maximum minutes for the new-orders interval (24 hours).</summary>
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Minimum hours for the status interval.</summary>
    public const int MinIntervalHours = 1;

    /// <summary>Maximum hours for the status interval (7 days).</summary>
    public const int MaxIntervalHours = 168;

    /// <summary>Minimum lookback for the new-orders loop.</summary>
    public const int MinNewLookbackDays = 1;

    /// <summary>Maximum lookback for the new-orders loop.</summary>
    public const int MaxNewLookbackDays = 7;

    /// <summary>Minimum lookback for the status loop.</summary>
    public const int MinStatusLookbackDays = 1;

    /// <summary>Maximum lookback for the status loop.</summary>
    public const int MaxStatusLookbackDays = 60;

    /// <summary>Minimum account batch size.</summary>
    public const int MinBatchSize = 1;

    /// <summary>Maximum account batch size.</summary>
    public const int MaxBatchSize = 100;

    /// <summary>Default minutes overdue before the watchdog alerts.</summary>
    public const int DefaultWatchdogOverdueMinutes = 15;

    /// <summary>Minimum watchdog threshold — below this a slow 4Seller pass would alert on itself.</summary>
    public const int MinWatchdogOverdueMinutes = 5;

    /// <summary>Maximum watchdog threshold (4 hours).</summary>
    public const int MaxWatchdogOverdueMinutes = 240;
}
