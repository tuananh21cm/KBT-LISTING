using Application.Services;
using Xunit;

namespace Application.Tests;

public class UsRetailCalendarTests
{
    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    public void UpcomingEvents_ComputesEasterSunday(int year, int month, int day)
    {
        // 40 days before Easter still sits inside the 21-56 day sourcing window.
        var expected = new DateOnly(year, month, day);
        var events = UsRetailCalendar.GetUpcomingEvents(
            expected.ToDateTime(TimeOnly.MinValue).AddDays(-40));

        var easter = Assert.Single(events, e => e.Name == "Easter");
        Assert.Equal(expected, easter.Date);
    }

    [Theory]
    [InlineData(2026, 11, 26)]
    [InlineData(2027, 11, 25)]
    public void UpcomingEvents_ComputesThanksgivingAndBlackFriday(int year, int month, int day)
    {
        var thanksgiving = new DateOnly(year, month, day);
        var events = UsRetailCalendar.GetUpcomingEvents(
            thanksgiving.ToDateTime(TimeOnly.MinValue).AddDays(-30));

        Assert.Equal(thanksgiving, Assert.Single(events, e => e.Name == "Thanksgiving").Date);
        Assert.Equal(
            thanksgiving.AddDays(1),
            Assert.Single(events, e => e.Name == "Black Friday").Date);
    }

    [Fact]
    public void UpcomingEvents_StaysInsideSourcingWindowAndCrossesYearEnd()
    {
        var events = UsRetailCalendar.GetUpcomingEvents(new DateTime(2026, 12, 10));

        Assert.All(events, e => Assert.InRange(
            e.DaysAway,
            UsRetailCalendar.MinDaysAhead,
            UsRetailCalendar.MaxDaysAhead));
        Assert.Contains(events, e => e.Name == "New Year" && e.Date.Year == 2027);
        Assert.DoesNotContain(events, e => e.Name == "Christmas");
    }

    [Theory]
    [InlineData(1, "winter")]
    [InlineData(3, "early-spring")]
    [InlineData(7, "summer")]
    [InlineData(8, "back-to-school")]
    [InlineData(10, "fall")]
    [InlineData(12, "holiday")]
    public void GetSeason_MapsMonthToShoppingSeason(int month, string expected)
        => Assert.Equal(expected, UsRetailCalendar.GetSeason(new DateTime(2026, month, 15)));
}
