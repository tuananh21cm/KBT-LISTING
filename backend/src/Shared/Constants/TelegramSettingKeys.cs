namespace Shared.Constants;

/// <summary>
/// Setting keys for Telegram notification toggles/schedules (managed via admin Settings).
/// BotToken lives in appsettings <c>Telegram</c> section; chat ids can be overridden in Settings.
/// </summary>
public static class TelegramSettingKeys
{
    /// <summary>When true, send a message for each newly inserted order.</summary>
    public const string NotifyNewOrder = "telegram.notify_new_order";

    /// <summary>When true, send a message for newly canceled / refunded orders.</summary>
    public const string NotifyCancelRefund = "telegram.notify_cancel_refund";

    /// <summary>
    /// Chat / group id for new orders + daily order summary + seller summary.
    /// Empty → fallback <c>Telegram:ChatId</c> in appsettings.
    /// </summary>
    public const string OrdersChatId = "telegram.orders_chat_id";

    /// <summary>
    /// Chat / group id for ops: cancel/refund, USPS, warehouse track, fulfil summary,
    /// order message, stale no-tracking.
    /// </summary>
    public const string CancelRefundChatId = "telegram.cancel_refund_chat_id";

    /// <summary>When true, send a daily order status summary.</summary>
    public const string DailySummaryEnabled = "telegram.daily_summary.enabled";

    /// <summary>Local clock <c>HH:mm</c> (Asia/Ho_Chi_Minh) to push the daily summary. Legacy bare hour 0–23 still accepted.</summary>
    public const string DailySummaryHour = "telegram.daily_summary.hour";

    /// <summary>Last sent local date (yyyy-MM-dd) to avoid duplicate daily messages.</summary>
    public const string DailySummaryLastSentDate = "telegram.daily_summary.last_sent_date";

    /// <summary>When true, send a daily per-seller Listed/Order/Revenue summary.</summary>
    public const string SellerSummaryEnabled = "telegram.seller_summary.enabled";

    /// <summary>Local clock <c>HH:mm</c> (Asia/Ho_Chi_Minh) to push the seller summary. Legacy bare hour 0–23 still accepted.</summary>
    public const string SellerSummaryHour = "telegram.seller_summary.hour";

    /// <summary>Last sent local date (yyyy-MM-dd) to avoid duplicate seller summary messages.</summary>
    public const string SellerSummaryLastSentDate = "telegram.seller_summary.last_sent_date";

    /// <summary>When true, send a daily fulfil / purchase processing summary.</summary>
    public const string FulfilSummaryEnabled = "telegram.fulfil_summary.enabled";

    /// <summary>Local clock <c>HH:mm</c> (Asia/Ho_Chi_Minh) to push the fulfil summary.</summary>
    public const string FulfilSummaryHour = "telegram.fulfil_summary.hour";

    /// <summary>Last sent local date (yyyy-MM-dd) to avoid duplicate fulfil summary messages.</summary>
    public const string FulfilSummaryLastSentDate = "telegram.fulfil_summary.last_sent_date";

    /// <summary>When true, notify fulfil when an order first needs USPS BuyTrack handling.</summary>
    public const string NotifyUsps = "telegram.notify_usps";

    /// <summary>When true, notify fulfil when Shein warehouse / local tracking first appears.</summary>
    public const string NotifyWarehouseTrack = "telegram.notify_warehouse_track";

    /// <summary>When true, notify ops chat when an order local message (Thông báo) is saved with content.</summary>
    public const string NotifyOrderMessage = "telegram.notify_order_message";

    /// <summary>
    /// When true, send a daily ops digest of purchased orders still without last-mile tracking
    /// after <see cref="NotifyStaleNoTrackingHours"/> hours. Uses the same local clock as
    /// <see cref="FulfilSummaryHour"/>.
    /// </summary>
    public const string NotifyStaleNoTracking = "telegram.notify_stale_no_tracking";

    /// <summary>Hours since purchase before the stale-no-tracking digest. Default 72.</summary>
    public const string NotifyStaleNoTrackingHours = "telegram.notify_stale_no_tracking.hours";

    /// <summary>Last sent local date (yyyy-MM-dd) to avoid duplicate stale-no-tracking digests.</summary>
    public const string NotifyStaleNoTrackingLastSentDate = "telegram.notify_stale_no_tracking.last_sent_date";

    /// <summary>Default hours for <see cref="NotifyStaleNoTrackingHours"/>.</summary>
    public const int DefaultStaleNoTrackingHours = 72;

    /// <summary>Minimum allowed hours for the stale-no-tracking threshold.</summary>
    public const int MinStaleNoTrackingHours = 1;

    /// <summary>Maximum allowed hours for the stale-no-tracking threshold (30 days).</summary>
    public const int MaxStaleNoTrackingHours = 720;
}
