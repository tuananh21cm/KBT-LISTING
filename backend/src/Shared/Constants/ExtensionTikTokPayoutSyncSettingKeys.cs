namespace Shared.Constants;

/// <summary>
/// Setting keys for the tiktok-shop-extension payout (paid) auto-sync + Telegram alerts
/// (managed via admin Settings).
/// </summary>
public static class ExtensionTikTokPayoutSyncSettingKeys
{
    /// <summary>When true, the extension auto-scrapes seller-us payouts on the configured interval.</summary>
    public const string Enabled = "extension.tiktok_payout_sync.enabled";

    /// <summary>Interval magnitude (paired with <see cref="IntervalUnit"/>). Minimum 1.</summary>
    public const string IntervalValue = "extension.tiktok_payout_sync.interval_value";

    /// <summary>Interval unit: <c>seconds</c> or <c>minutes</c>.</summary>
    public const string IntervalUnit = "extension.tiktok_payout_sync.interval_unit";

    /// <summary>
    /// ISO date (yyyy-MM-dd) — do not fetch/sync payouts with success time older than this date (UTC midnight).
    /// Empty = no floor other than DB cursor.
    /// </summary>
    public const string MaxDate = "extension.tiktok_payout_sync.max_date";

    /// <summary>When true, Telegram when a new TikTok payout is inserted.</summary>
    public const string NotifyNewPayment = "extension.tiktok_payout_sync.notify_new_payment";

    /// <summary>Telegram chat / group id for new payout alerts.</summary>
    public const string TelegramChatId = "extension.tiktok_payout_sync.telegram_chat_id";

    /// <summary>Interval unit value: seconds.</summary>
    public const string UnitSeconds = "seconds";

    /// <summary>Interval unit value: minutes.</summary>
    public const string UnitMinutes = "minutes";

    /// <summary>Default interval magnitude when setting missing/invalid.</summary>
    public const int DefaultIntervalValue = 120;

    /// <summary>Default Telegram chat id for payout alerts.</summary>
    public const string DefaultTelegramChatId = "-1004331688585";
}
