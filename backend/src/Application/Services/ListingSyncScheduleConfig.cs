using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Pure parse helpers for the two listing-sync loops. Kept free of DI so unit tests
/// can cover clamp and enabled rules without spinning up the background services.
/// </summary>
public static class ListingSyncScheduleConfig
{
    /// <summary>Parsed settings for the light draft loop.</summary>
    public readonly record struct DraftConfig(bool Enabled, int IntervalMinutes);

    /// <summary>Parsed settings for the heavier published status loop.</summary>
    public readonly record struct PublishedConfig(bool Enabled, int IntervalMinutes, int LookbackDays);

    /// <summary>Parses the two <c>fourseller.listing_sync.draft.*</c> settings.</summary>
    public static DraftConfig ParseDraft(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue(ListingSyncSettingKeys.DraftEnabled, out var enabledRaw);
        values.TryGetValue(ListingSyncSettingKeys.DraftIntervalMinutes, out var intervalRaw);

        return new DraftConfig(
            IsEnabled(enabledRaw, defaultEnabled: true),
            ClampInt(
                intervalRaw,
                ListingSyncSettingKeys.DefaultDraftIntervalMinutes,
                ListingSyncSettingKeys.MinIntervalMinutes,
                ListingSyncSettingKeys.MaxIntervalMinutes));
    }

    /// <summary>Parses the three <c>fourseller.listing_sync.published.*</c> settings.</summary>
    public static PublishedConfig ParsePublished(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue(ListingSyncSettingKeys.PublishedEnabled, out var enabledRaw);
        values.TryGetValue(ListingSyncSettingKeys.PublishedIntervalMinutes, out var intervalRaw);
        values.TryGetValue(ListingSyncSettingKeys.PublishedLookbackDays, out var lookbackRaw);

        return new PublishedConfig(
            IsEnabled(enabledRaw, defaultEnabled: true),
            ClampInt(
                intervalRaw,
                ListingSyncSettingKeys.DefaultPublishedIntervalMinutes,
                ListingSyncSettingKeys.MinIntervalMinutes,
                ListingSyncSettingKeys.MaxIntervalMinutes),
            ClampInt(
                lookbackRaw,
                ListingSyncSettingKeys.DefaultPublishedLookbackDays,
                ListingSyncSettingKeys.MinPublishedLookbackDays,
                ListingSyncSettingKeys.MaxPublishedLookbackDays));
    }

    private static bool IsEnabled(string? raw, bool defaultEnabled)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultEnabled;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            return false;
        }

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
            || defaultEnabled;
    }

    private static int ClampInt(string? raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}
