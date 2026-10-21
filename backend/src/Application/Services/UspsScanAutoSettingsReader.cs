using Application.Interfaces;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Clamped snapshot of the <c>order.usps_scan.*</c> settings for one auto-scan tick.
/// </summary>
/// <param name="AutoEnabled">Master switch.</param>
/// <param name="DelayHours">Minimum track age in hours since <c>TrackingAddedAt</c>.</param>
/// <param name="ChunkSize">Tracks per push call; <c>0</c> = send all eligible in one call.</param>
/// <param name="IntervalMinutes">Wait after a cycle completes before the next one.</param>
/// <param name="Window">US business-hours gate.</param>
public sealed record UspsScanAutoSettings(
    bool AutoEnabled,
    int DelayHours,
    int ChunkSize,
    int IntervalMinutes,
    UspsScanWindowConfig Window);

/// <summary>
/// Reads every auto-scan setting in a single <see cref="ISettingProvider.GetValuesAsync"/> round-trip.
/// The provider is not cached, so the loop and the orchestrator share one snapshot per tick.
/// </summary>
public static class UspsScanAutoSettingsReader
{
    /// <summary>Loads and clamps the auto-scan settings.</summary>
    public static async Task<UspsScanAutoSettings> ReadAsync(
        ISettingProvider settings,
        CancellationToken cancellationToken = default)
    {
        var values = await settings.GetValuesAsync(UspsScanAutoSettingKeys.All, cancellationToken);
        return From(values);
    }

    /// <summary>Clamps a raw key/value map into a settings snapshot.</summary>
    public static UspsScanAutoSettings From(IReadOnlyDictionary<string, string> values)
    {
        var chunkSize = ClampInt(
            Get(values, UspsScanAutoSettingKeys.ChunkSize),
            UspsScanAutoSettingKeys.DefaultChunkSize,
            min: 0,
            max: 100);

        return new UspsScanAutoSettings(
            AutoEnabled: ParseBool(Get(values, UspsScanAutoSettingKeys.AutoEnabled), fallback: false),
            DelayHours: ClampInt(
                Get(values, UspsScanAutoSettingKeys.DelayHours),
                UspsScanAutoSettingKeys.DefaultDelayHours,
                min: 1,
                max: 168),
            ChunkSize: chunkSize,
            IntervalMinutes: ClampInt(
                Get(values, UspsScanAutoSettingKeys.IntervalMinutes),
                UspsScanAutoSettingKeys.DefaultIntervalMinutes,
                min: 1,
                max: 1440),
            Window: new UspsScanWindowConfig(
                Enabled: ParseBool(Get(values, UspsScanAutoSettingKeys.WindowEnabled), fallback: true),
                WeekdayStart: UspsScanWindow.ParseClock(
                    Get(values, UspsScanAutoSettingKeys.WeekdayStart),
                    UspsScanWindow.DefaultWeekdayStart),
                WeekdayEnd: UspsScanWindow.ParseClock(
                    Get(values, UspsScanAutoSettingKeys.WeekdayEnd),
                    UspsScanWindow.DefaultWeekdayEnd),
                SaturdayEnabled: ParseBool(Get(values, UspsScanAutoSettingKeys.SaturdayEnabled), fallback: true),
                SaturdayStart: UspsScanWindow.ParseClock(
                    Get(values, UspsScanAutoSettingKeys.SaturdayStart),
                    UspsScanWindow.DefaultSaturdayStart),
                SaturdayEnd: UspsScanWindow.ParseClock(
                    Get(values, UspsScanAutoSettingKeys.SaturdayEnd),
                    UspsScanWindow.DefaultSaturdayEnd),
                CutoffMarginMinutes: ClampInt(
                    Get(values, UspsScanAutoSettingKeys.CutoffMarginMinutes),
                    UspsScanAutoSettingKeys.DefaultCutoffMarginMinutes,
                    min: 0,
                    max: 240)));
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static bool ParseBool(string? raw, bool fallback)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return fallback;
        }

        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }

    private static int ClampInt(string? raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }
}
