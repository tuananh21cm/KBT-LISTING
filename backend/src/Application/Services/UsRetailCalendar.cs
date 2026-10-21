using System.Globalization;

namespace Application.Services;

/// <summary>
/// One US retail / fashion demand milestone with its distance from today.
/// </summary>
/// <param name="Name">Event label (e.g. Halloween).</param>
/// <param name="Date">Event date.</param>
/// <param name="DaysAway">Whole days from the reference date.</param>
public sealed record UsRetailEvent(string Name, DateOnly Date, int DaysAway)
{
    /// <summary>Context line: <c>Halloween (in 61 days, 2026-10-31)</c>.</summary>
    public string ToContextLine()
        => $"{Name} (in {DaysAway} days, {Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})";
}

/// <summary>
/// Deterministic US seasonality + retail events so the model never has to guess the calendar.
/// </summary>
public static class UsRetailCalendar
{
    /// <summary>Earliest lead time worth sourcing for: shipping 6-8 days + listing indexing.</summary>
    public const int MinDaysAhead = 21;

    /// <summary>Furthest horizon kept in the prompt.</summary>
    public const int MaxDaysAhead = 56;

    /// <summary>
    /// Shopping season label for the given date.
    /// </summary>
    public static string GetSeason(DateTime dateUtc) => dateUtc.Month switch
    {
        1 or 2 => "winter",
        3 => "early-spring",
        4 or 5 => "spring",
        6 or 7 => "summer",
        8 => "back-to-school",
        9 or 10 => "fall",
        _ => "holiday"
    };

    /// <summary>
    /// Events falling inside the sourcing window (<see cref="MinDaysAhead"/> to
    /// <see cref="MaxDaysAhead"/> days out), ordered by date.
    /// </summary>
    public static List<UsRetailEvent> GetUpcomingEvents(DateTime dateUtc)
    {
        var today = DateOnly.FromDateTime(dateUtc.Date);
        var events = new List<UsRetailEvent>();

        foreach (var year in new[] { today.Year, today.Year + 1 })
        {
            foreach (var (name, date) in BuildYearEvents(year))
            {
                var daysAway = date.DayNumber - today.DayNumber;
                if (daysAway >= MinDaysAhead && daysAway <= MaxDaysAhead)
                {
                    events.Add(new UsRetailEvent(name, date, daysAway));
                }
            }
        }

        return events.OrderBy(e => e.DaysAway).ToList();
    }

    private static IEnumerable<(string Name, DateOnly Date)> BuildYearEvents(int year)
    {
        var thanksgiving = NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4);

        yield return ("New Year", new DateOnly(year, 1, 1));
        yield return ("Valentine's Day", new DateOnly(year, 2, 14));
        yield return ("St. Patrick's Day", new DateOnly(year, 3, 17));
        yield return ("Easter", EasterSunday(year));
        yield return ("Mother's Day", NthWeekdayOfMonth(year, 5, DayOfWeek.Sunday, 2));
        yield return ("Memorial Day", LastWeekdayOfMonth(year, 5, DayOfWeek.Monday));
        yield return ("Father's Day", NthWeekdayOfMonth(year, 6, DayOfWeek.Sunday, 3));
        yield return ("Independence Day", new DateOnly(year, 7, 4));
        yield return ("Back to School peak", new DateOnly(year, 8, 15));
        yield return ("Labor Day", NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1));
        yield return ("Halloween", new DateOnly(year, 10, 31));
        yield return ("Thanksgiving", thanksgiving);
        yield return ("Black Friday", thanksgiving.AddDays(1));
        yield return ("Cyber Monday", thanksgiving.AddDays(4));
        yield return ("Christmas", new DateOnly(year, 12, 25));
    }

    /// <summary>Anonymous Gregorian computus.</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek weekday, int nth)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (7 * (nth - 1)));
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }
}
