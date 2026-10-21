using Application.DTOs.Shop;
using Shared.Constants;

namespace Application.Services;

/// <summary>Builds auto publish system config for shop UI.</summary>
public static class AutoPublishSystemConfigBuilder
{
    public static AutoPublishSystemConfigDto FromSettings(IReadOnlyDictionary<string, string?> values)
    {
        var enabledRaw = values.GetValueOrDefault(ListingAutoPublishSettingKeys.Enabled);
        return new AutoPublishSystemConfigDto
        {
            SystemAutoPublishEnabled = string.Equals(
                enabledRaw?.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase),
            DefaultIntervalSeconds = ParseInt(
                values.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultIntervalSeconds),
                int.Parse(ListingAutoPublishSettingKeys.DefaultIntervalSecondsValue),
                1),
            DefaultMinDraftAgeMinutes = ParseInt(
                values.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes),
                int.Parse(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutesValue),
                ListingAutoPublishSettingKeys.MinDraftAgeMinutes)
        };
    }

    private static int ParseInt(string? raw, int defaultValue, int min)
    {
        if (!int.TryParse(raw?.Trim(), out var n))
        {
            n = defaultValue;
        }

        return Math.Max(min, n);
    }
}
