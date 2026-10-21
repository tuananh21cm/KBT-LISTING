using Shared.Helpers;
using Xunit;

namespace Application.Tests;

/// <summary>
/// US business-hours window for USPS auto scan: weekday / Saturday windows, Sunday off,
/// cutoff margin, and DST correctness across the March / November transitions.
/// </summary>
public class UspsScanWindowTests
{
    // 2026-01-14 is a Wednesday in EST (UTC-5): 08:00 ET = 13:00 UTC, 17:00 ET = 22:00 UTC.
    // 2026-07-15 is a Wednesday in EDT (UTC-4): 08:00 ET = 12:00 UTC, 17:00 ET = 21:00 UTC.
    private static readonly UspsScanWindowConfig Default = new(
        Enabled: true,
        WeekdayStart: new TimeOnly(8, 0),
        WeekdayEnd: new TimeOnly(17, 0),
        SaturdayEnabled: true,
        SaturdayStart: new TimeOnly(8, 0),
        SaturdayEnd: new TimeOnly(12, 0),
        CutoffMarginMinutes: 30);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void IsOpen_Disabled_AlwaysTrue_EvenOnSunday()
    {
        var config = Default with { Enabled = false };

        Assert.True(UspsScanWindow.IsOpen(Utc(2026, 1, 18, 3), config));
    }

    [Theory]
    // Winter (EST, UTC-5): window 13:00–22:00 UTC, effective close 21:30 UTC.
    [InlineData(12, 59, false)]
    [InlineData(13, 0, true)]
    [InlineData(18, 0, true)]
    [InlineData(21, 29, true)]
    [InlineData(21, 30, false)]
    [InlineData(22, 0, false)]
    public void IsOpen_Weekday_Est_RespectsWindowAndCutoffMargin(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, UspsScanWindow.IsOpen(Utc(2026, 1, 14, hour, minute), Default));
    }

    [Theory]
    // Summer (EDT, UTC-4) shifts the same ET window one hour earlier in UTC.
    [InlineData(11, 59, false)]
    [InlineData(12, 0, true)]
    [InlineData(20, 29, true)]
    [InlineData(20, 30, false)]
    public void IsOpen_Weekday_Edt_ShiftsWithDaylightSaving(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, UspsScanWindow.IsOpen(Utc(2026, 7, 15, hour, minute), Default));
    }

    [Theory]
    // Saturday 2026-01-17 (EST): 08:00–12:00 ET = 13:00–17:00 UTC, effective close 16:30 UTC.
    [InlineData(12, 59, false)]
    [InlineData(13, 0, true)]
    [InlineData(16, 29, true)]
    [InlineData(16, 30, false)]
    [InlineData(21, 0, false)]
    public void IsOpen_Saturday_UsesItsOwnHalfDayWindow(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, UspsScanWindow.IsOpen(Utc(2026, 1, 17, hour, minute), Default));
    }

    [Fact]
    public void IsOpen_Saturday_Disabled_IsClosed()
    {
        var config = Default with { SaturdayEnabled = false };

        Assert.True(UspsScanWindow.IsOpen(Utc(2026, 1, 17, 14), Default));
        Assert.False(UspsScanWindow.IsOpen(Utc(2026, 1, 17, 14), config));
    }

    [Fact]
    public void IsOpen_Sunday_AlwaysClosed()
    {
        // 2026-01-18 15:00 UTC = Sunday 10:00 ET, mid-window on any other day.
        Assert.False(UspsScanWindow.IsOpen(Utc(2026, 1, 18, 15), Default));
    }

    [Fact]
    public void IsOpen_CutoffMarginWiderThanWindow_IsClosed()
    {
        var config = Default with
        {
            WeekdayStart = new TimeOnly(8, 0),
            WeekdayEnd = new TimeOnly(9, 0),
            SaturdayStart = new TimeOnly(8, 0),
            SaturdayEnd = new TimeOnly(9, 0),
            CutoffMarginMinutes = 60,
        };

        Assert.False(UspsScanWindow.IsOpen(Utc(2026, 1, 14, 13), config));
        // No day can ever open, so the hosted loop must fall back to its poll cap.
        Assert.Null(UspsScanWindow.NextOpenUtc(Utc(2026, 1, 14, 13), config));
    }

    [Fact]
    public void IsOpen_EndBeforeStart_IsClosed()
    {
        var config = Default with
        {
            WeekdayStart = new TimeOnly(17, 0),
            WeekdayEnd = new TimeOnly(8, 0),
            CutoffMarginMinutes = 0,
        };

        Assert.False(UspsScanWindow.IsOpen(Utc(2026, 1, 14, 13), config));
    }

    [Fact]
    public void NextOpenUtc_WhenOpen_ReturnsNow()
    {
        var now = Utc(2026, 1, 14, 15);

        Assert.Equal(now, UspsScanWindow.NextOpenUtc(now, Default));
    }

    [Fact]
    public void NextOpenUtc_AfterWeekdayClose_ReturnsNextMorning()
    {
        // Wednesday 22:30 UTC = 17:30 ET, past close → Thursday 08:00 ET = 13:00 UTC.
        Assert.Equal(
            Utc(2026, 1, 15, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 1, 14, 22, 30), Default));
    }

    [Fact]
    public void NextOpenUtc_WithinCutoffMargin_ReturnsNextMorning()
    {
        // Wednesday 21:45 UTC = 16:45 ET, inside the 30-minute cutoff margin.
        Assert.Equal(
            Utc(2026, 1, 15, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 1, 14, 21, 45), Default));
    }

    [Fact]
    public void NextOpenUtc_FridayEvening_ReturnsSaturdayMorning()
    {
        Assert.Equal(
            Utc(2026, 1, 17, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 1, 16, 23), Default));
    }

    [Fact]
    public void NextOpenUtc_FridayEvening_SaturdayOff_SkipsToMonday()
    {
        var config = Default with { SaturdayEnabled = false };

        Assert.Equal(
            Utc(2026, 1, 19, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 1, 16, 23), config));
    }

    [Fact]
    public void NextOpenUtc_Sunday_SkipsToMondayMorning()
    {
        Assert.Equal(
            Utc(2026, 1, 19, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 1, 18, 10), Default));
    }

    [Fact]
    public void NextOpenUtc_AcrossSpringForward_UsesDaylightOffset()
    {
        // Saturday 2026-03-07 20:00 UTC = 15:00 EST (past Saturday close). DST starts 2026-03-08,
        // so Monday 08:00 ET is EDT = 12:00 UTC, not 13:00 UTC.
        Assert.Equal(
            Utc(2026, 3, 9, 12),
            UspsScanWindow.NextOpenUtc(Utc(2026, 3, 7, 20), Default));
    }

    [Fact]
    public void NextOpenUtc_AcrossFallBack_UsesStandardOffset()
    {
        // Saturday 2026-10-31 19:00 UTC = 15:00 EDT (past close). DST ends 2026-11-01,
        // so Monday 08:00 ET is EST = 13:00 UTC.
        Assert.Equal(
            Utc(2026, 11, 2, 13),
            UspsScanWindow.NextOpenUtc(Utc(2026, 10, 31, 19), Default));
    }

    [Fact]
    public void NextOpenUtc_Disabled_ReturnsNow()
    {
        var config = Default with { Enabled = false };
        var now = Utc(2026, 1, 18, 3);

        Assert.Equal(now, UspsScanWindow.NextOpenUtc(now, config));
    }

    [Theory]
    [InlineData("08:00", 8, 0)]
    [InlineData("8:30", 8, 30)]
    [InlineData(" 17:45 ", 17, 45)]
    [InlineData("00:00", 0, 0)]
    [InlineData("9", 9, 0)]
    public void ParseClock_AcceptsClockAndBareHour(string raw, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), UspsScanWindow.ParseClock(raw, new TimeOnly(1, 2)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:00")]
    [InlineData("08:60")]
    [InlineData("-1:00")]
    [InlineData("abc")]
    [InlineData("8h30")]
    public void ParseClock_FallsBackOnGarbage(string? raw)
    {
        var fallback = new TimeOnly(8, 0);

        Assert.Equal(fallback, UspsScanWindow.ParseClock(raw, fallback));
    }

    [Fact]
    public void FormatClock_PadsToHhMm()
    {
        Assert.Equal("08:00", UspsScanWindow.FormatClock(new TimeOnly(8, 0)));
        Assert.Equal("17:05", UspsScanWindow.FormatClock(new TimeOnly(17, 5)));
    }
}
