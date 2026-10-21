namespace Shared.Constants;

/// <summary>
/// Setting keys for the tiktok-shop-extension auto-sync + Telegram profile-change alerts
/// (managed via admin Settings).
/// </summary>
public static class ExtensionTikTokShopSyncSettingKeys
{
    /// <summary>When true, the extension auto-scrapes seller-us on the configured interval (chrome.alarms).</summary>
    public const string Enabled = "extension.tiktok_shop_sync.enabled";

    /// <summary>Interval magnitude (paired with <see cref="IntervalUnit"/>). Minimum 1.</summary>
    public const string IntervalValue = "extension.tiktok_shop_sync.interval_value";

    /// <summary>Interval unit: <c>seconds</c> or <c>minutes</c>.</summary>
    public const string IntervalUnit = "extension.tiktok_shop_sync.interval_unit";

    /// <summary>When true, Telegram when synced email differs from previously stored email.</summary>
    public const string NotifyEmailChange = "extension.tiktok_shop_sync.notify_email_change";

    /// <summary>When true, Telegram when synced mobile differs from previously stored mobile.</summary>
    public const string NotifyMobileChange = "extension.tiktok_shop_sync.notify_mobile_change";

    /// <summary>When true, Telegram when synced settlement bank differs from previously stored bank.</summary>
    public const string NotifyBankChange = "extension.tiktok_shop_sync.notify_bank_change";

    /// <summary>
    /// When true, Telegram when settlement bank last-4 digits do not match any
    /// <c>ReceivingBankAccounts.AccountNumber</c> last-4.
    /// </summary>
    public const string NotifyBankLast4Mismatch = "extension.tiktok_shop_sync.notify_bank_last4_mismatch";

    /// <summary>
    /// When true, Telegram when the ops <c>Shop.Status</c> changes (Active / Deactivated / Suspended),
    /// whether from a TTS sync or a manual admin/seller edit.
    /// </summary>
    public const string NotifyStatusChange = "extension.tiktok_shop_sync.notify_status_change";

    /// <summary>Telegram chat / group id for TikTok shop profile alerts.</summary>
    public const string TelegramChatId = "extension.tiktok_shop_sync.telegram_chat_id";

    /// <summary>Interval unit value: seconds.</summary>
    public const string UnitSeconds = "seconds";

    /// <summary>Interval unit value: minutes.</summary>
    public const string UnitMinutes = "minutes";

    /// <summary>Default interval magnitude when setting missing/invalid.</summary>
    public const int DefaultIntervalValue = 60;

    /// <summary>Default Telegram chat id for profile alerts.</summary>
    public const string DefaultTelegramChatId = "-1004331688585";
}
