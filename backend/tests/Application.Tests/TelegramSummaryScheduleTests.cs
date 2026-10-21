using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class TelegramSummaryScheduleTests
{
    [Theory]
    [InlineData("23:59", 23, 59)]
    [InlineData("08:00", 8, 0)]
    [InlineData("8:05", 8, 5)]
    [InlineData("22", 22, 0)]
    [InlineData("", 8, 0)]
    [InlineData("99:99", 8, 0)]
    public void TryParseLocalClock_ParsesOrDefaults(string raw, int hour, int minute)
    {
        TelegramSummarySchedule.TryParseLocalClock(raw, out var h, out var m);
        Assert.Equal(hour, h);
        Assert.Equal(minute, m);
    }

    [Fact]
    public void Resolve_23_59_SameMinute_SendsToday()
    {
        var now = new DateTime(2026, 8, 4, 23, 59, 10);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(now, 23, 59, lastSentDateYyyyMmDd: null, out var date);
        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 8, 4), date);
    }

    [Fact]
    public void Resolve_23_59_MissedPastMidnight_CatchUpYesterday()
    {
        // Job woke at 00:14 after missing the ~1-minute same-day window.
        var now = new DateTime(2026, 8, 5, 0, 14, 0);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(now, 23, 59, lastSentDateYyyyMmDd: null, out var date);
        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 8, 4), date);
    }

    [Fact]
    public void Resolve_23_59_AlreadySentYesterday_WaitsForTodaySlot()
    {
        var now = new DateTime(2026, 8, 5, 0, 14, 0);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(
            now, 23, 59, lastSentDateYyyyMmDd: "2026-08-04", out _);
        Assert.False(ok);
    }

    [Fact]
    public void Resolve_08_00_BeforeSlot_CatchUpYesterdayIfMissed()
    {
        var now = new DateTime(2026, 8, 5, 7, 30, 0);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(now, 8, 0, lastSentDateYyyyMmDd: null, out var date);
        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 8, 4), date);
    }

    [Fact]
    public void Resolve_08_00_AfterSlot_PrefersTodayOverYesterdayCatchUp()
    {
        var now = new DateTime(2026, 8, 5, 8, 1, 0);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(
            now, 8, 0, lastSentDateYyyyMmDd: null, out var date);
        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 8, 5), date);
    }

    [Fact]
    public void Resolve_DoesNotResendSameDay()
    {
        var now = new DateTime(2026, 8, 5, 10, 0, 0);
        var ok = TelegramSummarySchedule.TryResolveSummaryLocalDate(
            now, 8, 0, lastSentDateYyyyMmDd: "2026-08-05", out _);
        Assert.False(ok);
    }
}
