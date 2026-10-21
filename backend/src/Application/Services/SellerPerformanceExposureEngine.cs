namespace Application.Services;

/// <summary>
/// Inclusive date interval on the Vietnam business calendar.
/// </summary>
public readonly record struct DateInterval(DateOnly Start, DateOnly End)
{
    /// <summary>Inclusive day count; 0 when inverted.</summary>
    public int DayCount => End < Start ? 0 : End.DayNumber - Start.DayNumber + 1;
}

/// <summary>
/// Pure exposure helpers for seller-performance rates:
/// calendar days, operational seller-days (union), and shop-days (sum).
/// </summary>
public static class SellerPerformanceExposureEngine
{
    /// <summary>
    /// Clamps an operational period into the reporting window.
    /// Open periods use <paramref name="reportTo"/> as the inclusive end.
    /// </summary>
    public static DateInterval? ClampToRange(
        DateOnly reportFrom,
        DateOnly reportTo,
        DateOnly startedOn,
        DateOnly? endedOn)
    {
        if (reportTo < reportFrom)
        {
            return null;
        }

        var end = endedOn ?? reportTo;
        var start = startedOn > reportFrom ? startedOn : reportFrom;
        var clampedEnd = end < reportTo ? end : reportTo;
        if (start > clampedEnd)
        {
            return null;
        }

        return new DateInterval(start, clampedEnd);
    }

    /// <summary>
    /// Merges overlapping/adjacent inclusive intervals so days are not double-counted.
    /// </summary>
    public static List<DateInterval> Merge(IEnumerable<DateInterval> intervals)
    {
        var ordered = intervals
            .Where(i => i.DayCount > 0)
            .OrderBy(i => i.Start)
            .ThenBy(i => i.End)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var merged = new List<DateInterval>(ordered.Count);
        var current = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            // Adjacent days merge too (end+1 == next.start).
            if (next.Start.DayNumber <= current.End.DayNumber + 1)
            {
                var end = next.End > current.End ? next.End : current.End;
                current = new DateInterval(current.Start, end);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>Σ inclusive days after merge.</summary>
    public static int CountDays(IEnumerable<DateInterval> intervals) =>
        Merge(intervals).Sum(i => i.DayCount);

    /// <summary>
    /// Shop-days = sum of per-shop merged days (shops do not union with each other).
    /// </summary>
    public static int CountShopDays(
        DateOnly reportFrom,
        DateOnly reportTo,
        IEnumerable<(DateOnly StartedOn, DateOnly? EndedOn)> shopPeriods)
    {
        var clamped = shopPeriods
            .Select(p => ClampToRange(reportFrom, reportTo, p.StartedOn, p.EndedOn))
            .Where(i => i.HasValue)
            .Select(i => i!.Value);
        return CountDays(clamped);
    }

    /// <summary>
    /// Seller operational days = unique VN days with ≥1 operational shop (union across shops).
    /// </summary>
    public static int CountSellerOperationalDays(
        DateOnly reportFrom,
        DateOnly reportTo,
        IEnumerable<(DateOnly StartedOn, DateOnly? EndedOn)> sellerShopPeriods)
    {
        var clamped = sellerShopPeriods
            .Select(p => ClampToRange(reportFrom, reportTo, p.StartedOn, p.EndedOn))
            .Where(i => i.HasValue)
            .Select(i => i!.Value);
        return CountDays(clamped);
    }

    /// <summary>
    /// Number of distinct shops that contribute ≥1 operational day in the range.
    /// </summary>
    public static int CountOperationalShops(
        DateOnly reportFrom,
        DateOnly reportTo,
        IEnumerable<(Guid ShopId, DateOnly StartedOn, DateOnly? EndedOn)> periods)
    {
        return periods
            .GroupBy(p => p.ShopId)
            .Count(g => CountShopDays(
                reportFrom,
                reportTo,
                g.Select(x => (x.StartedOn, x.EndedOn))) > 0);
    }

    /// <summary><c>orders / days</c>, 2 dp. Null when days ≤ 0.</summary>
    public static decimal? AveragePerDay(int orders, int days)
    {
        if (days <= 0)
        {
            return null;
        }

        return Math.Round((decimal)orders / days, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary><c>eligible / shops</c>, 2 dp. Null when shops ≤ 0.</summary>
    public static decimal? AveragePerShop(int eligibleShopOrders, int operationalShopCount)
    {
        if (operationalShopCount <= 0)
        {
            return null;
        }

        return Math.Round(
            (decimal)eligibleShopOrders / operationalShopCount,
            2,
            MidpointRounding.AwayFromZero);
    }
}
