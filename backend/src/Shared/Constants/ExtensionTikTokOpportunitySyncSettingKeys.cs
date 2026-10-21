namespace Shared.Constants;

/// <summary>
/// Setting keys for tiktok-shop-extension Product opportunities (trending keywords) auto-sync
/// (managed via admin Settings).
/// </summary>
public static class ExtensionTikTokOpportunitySyncSettingKeys
{
    /// <summary>When true, the extension auto-scrapes seller-us lead/list on the configured interval.</summary>
    public const string Enabled = "extension.tiktok_opportunity_sync.enabled";

    /// <summary>Interval magnitude (paired with <see cref="IntervalUnit"/>). Minimum 1.</summary>
    public const string IntervalValue = "extension.tiktok_opportunity_sync.interval_value";

    /// <summary>Interval unit: <c>seconds</c> or <c>minutes</c>.</summary>
    public const string IntervalUnit = "extension.tiktok_opportunity_sync.interval_unit";

    /// <summary>Interval unit value: seconds.</summary>
    public const string UnitSeconds = "seconds";

    /// <summary>Interval unit value: minutes.</summary>
    public const string UnitMinutes = "minutes";

    /// <summary>Default interval magnitude when setting missing/invalid.</summary>
    public const int DefaultIntervalValue = 30;
}
