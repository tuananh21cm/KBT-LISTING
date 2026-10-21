using System.Globalization;

namespace Shared.Helpers;

/// <summary>
/// US post-office business-hours window for USPS track-scan pushes.
/// Clock values are US Eastern wall time (<see cref="AppTimeZones.Eastern"/>) so the window keeps
/// the same business meaning across DST; VN display times are derived, never stored.
/// </summary>
public sealed record UspsScanWindowConfig(
    bool Enabled,
    TimeOnly WeekdayStart,
    TimeOnly WeekdayEnd,
    bool SaturdayEnabled,
    TimeOnly SaturdayStart,
    TimeOnly SaturdayEnd,
    int CutoffMarginMinutes);

/// <summary>
/// Pure window math: is the US post office open right now, and when does it open next.
/// </summary>
public static class UspsScanWindow
{
    /// <summary>Days scanned ahead by <see cref="NextOpenUtc"/> (covers a full closed week).</summary>
    private const int LookaheadDays = 8;

    /// <summary>Default weekday window 08:00–17:00 ET.</summary>
    public static readonly TimeOnly DefaultWeekdayStart = new(8, 0);

    /// <summary>Default weekday close 17:00 ET.</summary>
    public static readonly TimeOnly DefaultWeekdayEnd = new(17, 0);

    /// <summary>Default Saturday window 08:00–12:00 ET.</summary>
    public static readonly TimeOnly DefaultSaturdayStart = new(8, 0);

    /// <summary>Default Saturday close 12:00 ET.</summary>
    public static readonly TimeOnly DefaultSaturdayEnd = new(12, 0);

    /// <summary>
    /// True when <paramref name="utcNow"/> falls inside the configured Eastern window,
    /// after subtracting the cutoff margin from the close time.
    /// Always true when the window gate is disabled.
    /// </summary>
    public static bool IsOpen(DateTime utcNow, UspsScanWindowConfig config)
    {
        if (!config.Enabled)
        {
            return true;
        }

        var easternNow = TimeZoneInfo.ConvertTimeFromUtc(AppTimeZones.EnsureUtc(utcNow), AppTimeZones.Eastern);
        if (!TryResolveEffectiveWindow(easternNow.DayOfWeek, config, out var start, out var effectiveEnd))
        {
            return false;
        }

        var nowTime = easternNow.TimeOfDay;
        return nowTime >= start.ToTimeSpan() && nowTime < effectiveEnd;
    }

    /// <summary>
    /// Next UTC instant the window is open. Returns <paramref name="utcNow"/> when already open,
    /// and <c>null</c> when every day is closed (all days off, or cutoff margin swallows the window).
    /// </summary>
    public static DateTime? NextOpenUtc(DateTime utcNow, UspsScanWindowConfig config)
    {
        var instant = AppTimeZones.EnsureUtc(utcNow);
        if (!config.Enabled)
        {
            return instant;
        }

        var easternNow = TimeZoneInfo.ConvertTimeFromUtc(instant, AppTimeZones.Eastern);

        for (var offset = 0; offset < LookaheadDays; offset++)
        {
            var day = easternNow.Date.AddDays(offset);
            if (!TryResolveEffectiveWindow(day.DayOfWeek, config, out var start, out var effectiveEnd))
            {
                continue;
            }

            if (offset == 0)
            {
                var nowTime = easternNow.TimeOfDay;
                if (nowTime >= effectiveEnd)
                {
                    continue;
                }

                if (nowTime >= start.ToTimeSpan())
                {
                    return instant;
                }
            }

            return ToUtc(day.Add(start.ToTimeSpan()));
        }

        return null;
    }

    /// <summary>
    /// Parses <c>HH:mm</c> / <c>H:mm</c>, or a bare hour <c>0–23</c>. Falls back to
    /// <paramref name="fallback"/> when the value is missing or malformed.
    /// </summary>
    public static TimeOnly ParseClock(string? raw, TimeOnly fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var value = raw.Trim();
        var colon = value.IndexOf(':');
        if (colon > 0)
        {
            if (int.TryParse(value[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
                && int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
                && hour is >= 0 and <= 23
                && minute is >= 0 and <= 59)
            {
                return new TimeOnly(hour, minute);
            }

            return fallback;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bareHour)
            && bareHour is >= 0 and <= 23)
        {
            return new TimeOnly(bareHour, 0);
        }

        return fallback;
    }

    /// <summary>Formats a clock for setting storage / logs (<c>HH:mm</c>).</summary>
    public static string FormatClock(TimeOnly value) =>
        value.ToString("HH\\:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Window for a given Eastern weekday, with the cutoff margin already applied to the close time.
    /// Sunday is always closed; Saturday follows its own toggle and clocks.
    /// </summary>
    private static bool TryResolveEffectiveWindow(
        DayOfWeek dayOfWeek,
        UspsScanWindowConfig config,
        out TimeOnly start,
        out TimeSpan effectiveEnd)
    {
        start = default;
        effectiveEnd = default;

        TimeOnly end;
        switch (dayOfWeek)
        {
            case DayOfWeek.Sunday:
                return false;
            case DayOfWeek.Saturday:
                if (!config.SaturdayEnabled)
                {
                    return false;
                }

                start = config.SaturdayStart;
                end = config.SaturdayEnd;
                break;
            default:
                start = config.WeekdayStart;
                end = config.WeekdayEnd;
                break;
        }

        var margin = TimeSpan.FromMinutes(Math.Max(0, config.CutoffMarginMinutes));
        effectiveEnd = end.ToTimeSpan() - margin;

        // Margin wider than the window (or end <= start) leaves nothing to push into.
        return effectiveEnd > start.ToTimeSpan();
    }

    /// <summary>
    /// Eastern wall clock to UTC. DST gaps shift forward past the missing hour; ambiguous
    /// (repeated) local times take the first, daylight-offset occurrence.
    /// </summary>
    private static DateTime ToUtc(DateTime easternLocal)
    {
        var local = DateTime.SpecifyKind(easternLocal, DateTimeKind.Unspecified);

        if (AppTimeZones.Eastern.IsInvalidTime(local))
        {
            var rule = AppTimeZones.Eastern.GetAdjustmentRules()
                .FirstOrDefault(r => local.Date >= r.DateStart.Date && local.Date <= r.DateEnd.Date);
            var gap = rule?.DaylightDelta ?? TimeSpan.FromHours(1);
            local = local.Add(gap);
        }

        if (AppTimeZones.Eastern.IsAmbiguousTime(local))
        {
            var offsets = AppTimeZones.Eastern.GetAmbiguousTimeOffsets(local);
            var daylightOffset = offsets.Max();
            return DateTime.SpecifyKind(local - daylightOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, AppTimeZones.Eastern);
    }
}
