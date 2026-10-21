namespace Shared.Constants;

/// <summary>
/// Setting keys for the backend-driven SHEIN order-detail sweep (managed via admin Settings).
///
/// Unlike <see cref="ExtensionSheinSyncSettingKeys"/> this loop never paginates
/// <c>order/list</c>: it only replays <c>get_order_info</c> for billnos already stored in the
/// ledger that still lack real carrier tracking or a published Mark Ship, using the cookie +
/// headers the extension pushed onto the fulfil account. That keeps the pass cheap and lets
/// tracking reach the platform even when nobody has the browser open.
/// </summary>
public static class SheinDetailSyncSettingKeys
{
    /// <summary>When true, the API server runs full detail-sync cycles on the configured rest interval.</summary>
    public const string Enabled = "shein.order_detail_sync.enabled";

    /// <summary>
    /// Minutes to rest after a full cycle finishes before starting the next snapshot.
    /// Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.
    /// </summary>
    public const string IntervalMinutes = "shein.order_detail_sync.interval_minutes";

    /// <summary>Billnos processed per batch inside one cycle (cycle still covers the full snapshot).</summary>
    public const string MaxBillnos = "shein.order_detail_sync.max_billnos";

    /// <summary>
    /// Ceiling on remote 4Seller Mark Ship calls per batch. The extension path is capped at 2 by
    /// <c>OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest</c> because Cloudflare cuts the HTTP
    /// request at ~100s; a background batch has no such deadline.
    /// </summary>
    public const string MaxMarkShipPerPass = "shein.order_detail_sync.max_mark_ship_per_pass";

    /// <summary>
    /// When true, Telegram gets one message per order the moment this sweep turns a warehouse-only
    /// (or missing) code into real carrier tracking — reporting the Mark Ship outcome, not just the
    /// tracking. Sent after Mark Ship ran, so a message never claims the platform has a code it does not.
    /// </summary>
    public const string NotifyCarrierTracking = "shein.order_detail_sync.notify_carrier_tracking";

    /// <summary>Telegram chat that receives the Add track messages.</summary>
    public const string TelegramChatId = "shein.order_detail_sync.telegram_chat_id";

    /// <summary>Default chat for Add track messages (same ops group as cancel/refund).</summary>
    public const string DefaultTelegramChatId = "-1004462111161";

    /// <summary>Default rest between full cycles (minutes).</summary>
    public const int DefaultIntervalMinutes = 10;

    /// <summary>Minimum rest — below this a slow cycle can feel continuous.</summary>
    public const int MinIntervalMinutes = 1;

    /// <summary>Maximum rest (3 hours).</summary>
    public const int MaxIntervalMinutes = 180;

    /// <summary>Default billno batch size inside a cycle.</summary>
    public const int DefaultMaxBillnos = 40;

    /// <summary>Minimum billno ceiling.</summary>
    public const int MinMaxBillnos = 5;

    /// <summary>Maximum billno ceiling.</summary>
    public const int MaxMaxBillnos = 200;

    /// <summary>Default Mark Ship ceiling per pass.</summary>
    public const int DefaultMaxMarkShipPerPass = 10;

    /// <summary>Minimum Mark Ship ceiling.</summary>
    public const int MinMaxMarkShipPerPass = 1;

    /// <summary>Maximum Mark Ship ceiling.</summary>
    public const int MaxMaxMarkShipPerPass = 50;

    /// <summary>
    /// UI warning threshold only — the sweep still probes SHEIN with the stored cookie.
    /// A captured session older than this is shown as "Cũ" on the fulfil-accounts page;
    /// whether it still works is decided by a live <c>get_order_info</c> (or Test session).
    /// </summary>
    public static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(12);
}
