namespace Shared.Constants;

/// <summary>
/// Setting keys for automatic USPS track-scan push (PrintPackShip).
/// </summary>
public static class UspsScanAutoSettingKeys
{
    /// <summary>Master switch. Default false.</summary>
    public const string AutoEnabled = "order.usps_scan.auto_enabled";

    /// <summary>
    /// Minimum age after <c>TrackingAddedAt</c> before an eligible USPS order may be auto-pushed.
    /// Guards against pushing a track added seconds ago; the US business-hours window decides *when*.
    /// Default 2; clamp 1–168.
    /// </summary>
    public const string DelayHours = "order.usps_scan.delay_hours";

    /// <summary>
    /// Tracks per <c>PushAsync</c> call. Default 10.
    /// <c>0</c> = send all eligible in one call (still split by PrintPackShip max if needed).
    /// Clamp 0–100.
    /// </summary>
    public const string ChunkSize = "order.usps_scan.chunk_size";

    /// <summary>
    /// Minutes to wait after a full scan cycle finishes before the next.
    /// Default 10; clamp 1–1440.
    /// </summary>
    public const string IntervalMinutes = "order.usps_scan.interval_minutes";

    /// <summary>
    /// Gate pushes on US post-office business hours. Default true.
    /// When false only <see cref="DelayHours"/> applies (legacy round-the-clock behaviour).
    /// </summary>
    public const string WindowEnabled = "order.usps_scan.window_enabled";

    /// <summary>Mon–Fri open clock, <c>HH:mm</c> US Eastern. Default <c>08:00</c>.</summary>
    public const string WeekdayStart = "order.usps_scan.weekday_start";

    /// <summary>Mon–Fri close clock, <c>HH:mm</c> US Eastern. Default <c>17:00</c>.</summary>
    public const string WeekdayEnd = "order.usps_scan.weekday_end";

    /// <summary>Whether Saturday morning counts as a push window. Default true.</summary>
    public const string SaturdayEnabled = "order.usps_scan.saturday_enabled";

    /// <summary>Saturday open clock, <c>HH:mm</c> US Eastern. Default <c>08:00</c>.</summary>
    public const string SaturdayStart = "order.usps_scan.saturday_start";

    /// <summary>Saturday close clock, <c>HH:mm</c> US Eastern. Default <c>12:00</c>.</summary>
    public const string SaturdayEnd = "order.usps_scan.saturday_end";

    /// <summary>
    /// Stop pushing this many minutes before the window closes so the last batch still gets scanned.
    /// Default 30; clamp 0–240.
    /// </summary>
    public const string CutoffMarginMinutes = "order.usps_scan.cutoff_margin_minutes";

    /// <summary>Default minimum track age in hours.</summary>
    public const int DefaultDelayHours = 2;

    /// <summary>Default chunk size (10 tracks per PushAsync).</summary>
    public const int DefaultChunkSize = 10;

    /// <summary>Default interval minutes after each cycle.</summary>
    public const int DefaultIntervalMinutes = 10;

    /// <summary>Default cutoff margin before the window closes.</summary>
    public const int DefaultCutoffMarginMinutes = 30;

    /// <summary>Hard ceiling on eligible ids queried per cycle (not a UI setting).</summary>
    public const int HardCapPerCycle = 500;

    /// <summary>All keys read by the auto-scan loop (single settings round-trip).</summary>
    public static readonly string[] All =
    [
        AutoEnabled,
        DelayHours,
        ChunkSize,
        IntervalMinutes,
        WindowEnabled,
        WeekdayStart,
        WeekdayEnd,
        SaturdayEnabled,
        SaturdayStart,
        SaturdayEnd,
        CutoffMarginMinutes,
    ];
}
