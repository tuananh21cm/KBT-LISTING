using System.Globalization;

namespace Shared.Helpers;

/// <summary>
/// Local-clock schedule for Telegram daily / seller summaries (Asia/Ho_Chi_Minh wall clock).
/// </summary>
public static class TelegramSummarySchedule
{
    /// <summary>
    /// Parses <c>HH:mm</c> / <c>H:mm</c>, or legacy bare hour <c>0–23</c>. Defaults to 08:00 when invalid.
    /// </summary>
    public static void TryParseLocalClock(string? raw, out int hour, out int minute)
    {
        hour = 8;
        minute = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var s = raw.Trim();
        var colon = s.IndexOf(':');
        if (colon > 0)
        {
            var hourPart = s[..colon];
            var minutePart = s[(colon + 1)..];
            if (int.TryParse(hourPart, NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                && int.TryParse(minutePart, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                && h is >= 0 and <= 23
                && m is >= 0 and <= 59)
            {
                hour = h;
                minute = m;
            }

            return;
        }

        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var legacyHour)
            && legacyHour is >= 0 and <= 23)
        {
            hour = legacyHour;
        }
    }

    /// <summary>
    /// True when local clock has reached today's scheduled hour:minute.
    /// </summary>
    public static bool IsLocalClockDue(DateTime localNow, int hour, int minute) =>
        localNow.TimeOfDay >= new TimeSpan(hour, minute, 0);

    /// <summary>
    /// Picks which local calendar day to summarize.
    /// Prefers today once the scheduled clock is reached; otherwise catch-up yesterday
    /// if that slot was missed (critical for late schedules like 23:59 where the same-day
    /// window is only ~1 minute before midnight).
    /// </summary>
    /// <returns>
    /// <c>true</c> when a summary should be sent for <paramref name="summaryLocalDate"/>
    /// (date at 00:00 local, <see cref="DateTimeKind.Unspecified"/>).
    /// </returns>
    public static bool TryResolveSummaryLocalDate(
        DateTime localNow,
        int hour,
        int minute,
        string? lastSentDateYyyyMmDd,
        out DateTime summaryLocalDate)
    {
        summaryLocalDate = default;
        var today = localNow.Date;
        var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var scheduledToday = today.AddHours(hour).AddMinutes(minute);

        if (localNow >= scheduledToday && !IsSameDate(lastSentDateYyyyMmDd, todayKey))
        {
            summaryLocalDate = today;
            return true;
        }

        var yesterday = today.AddDays(-1);
        var yesterdayKey = yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var scheduledYesterday = yesterday.AddHours(hour).AddMinutes(minute);

        // Catch-up only until today's slot opens (so we never double-fire for two days at once).
        if (localNow >= scheduledYesterday
            && localNow < scheduledToday
            && !IsSameDate(lastSentDateYyyyMmDd, yesterdayKey))
        {
            summaryLocalDate = yesterday;
            return true;
        }

        return false;
    }

    private static bool IsSameDate(string? lastSent, string expected) =>
        string.Equals(lastSent?.Trim(), expected, StringComparison.Ordinal);
}
