namespace Shared.Constants;

/// <summary>Close reasons for <c>ShopOnlineSessions</c>.</summary>
public static class ShopOnlineCloseReasons
{
    /// <summary>Last seller-us tab closed while Chrome still running.</summary>
    public const string TabClosed = "TabClosed";

    /// <summary>Extension found a pending local session after Chrome restart / crash.</summary>
    public const string BrowserRestartReconcile = "BrowserRestartReconcile";

    /// <summary>No heartbeat within the grace window — closed at LastSeenAt.</summary>
    public const string HeartbeatTimeout = "HeartbeatTimeout";

    /// <summary>Session crossed Vietnam midnight — closed at end of previous VN day.</summary>
    public const string DayBoundary = "DayBoundary";

    /// <summary>Replaced by a newer client session or explicit close without a specific reason.</summary>
    public const string Explicit = "Explicit";

    /// <summary>All accepted close-reason strings.</summary>
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        TabClosed,
        BrowserRestartReconcile,
        HeartbeatTimeout,
        DayBoundary,
        Explicit,
    };
}
