using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Pure helpers of the Compass snapshot flow: which Vietnam day a sync lands on, which Compass
/// <c>last7days</c> window it covers, and how the "% vs previous period" fallback is computed.
/// Kept separate from <see cref="ListingAnalyticsService"/> so the rounding/clamping rules are
/// unit-testable.
///
/// <para>Each sync stores one <b>rolling 7-day aggregate</b> (Pacific), not a single calendar day.
/// <see cref="SnapshotDateFor"/> is only the dedupe key (VN day of sync). Metrics and
/// <see cref="RangeStart"/>/<see cref="RangeEnd"/> describe the Compass window.</para>
/// </summary>
public static class ListingAnalyticsMetrics
{
    /// <summary>Compass <c>last7days</c> span — each snapshot row covers this many Pacific days.</summary>
    public const int DefaultRangeDays = 7;

    /// <summary>Alias for documentation / delist scoring — same as <see cref="DefaultRangeDays"/>.</summary>
    public const int WindowDays = DefaultRangeDays;

    /// <summary>Largest value <c>numeric(12,6)</c> can hold.</summary>
    public const decimal MaxRatio = 999_999.999999m;

    /// <summary>Largest value <c>numeric(18,2)</c> can hold.</summary>
    public const decimal MaxMoney = 9_999_999_999_999_999.99m;

    /// <summary>Vietnam calendar day used as the dedupe key (server decides, never the client).</summary>
    public static DateOnly SnapshotDateFor(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            AppTimeZones.Vietnam));

    /// <summary>Today in the Compass shop timezone (Pacific) — the default range end.</summary>
    public static DateOnly PacificToday(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            AppTimeZones.Pacific));

    /// <summary>
    /// Range the numbers cover. Missing ends default to the Pacific <c>last7days</c> window ending
    /// today; a reversed range collapses to a single day so the row never claims a window it does not
    /// have.
    /// </summary>
    public static (DateOnly Start, DateOnly End) ResolveRange(
        DateOnly? start,
        DateOnly? end,
        DateOnly pacificToday)
    {
        var rangeEnd = end ?? pacificToday;
        var rangeStart = start ?? rangeEnd.AddDays(-DefaultRangeDays);
        return rangeStart > rangeEnd ? (rangeEnd, rangeEnd) : (rangeStart, rangeEnd);
    }

    /// <summary>
    /// Snapshot date used as the baseline when Compass did not send <c>total_prev</c> ratios.
    /// Daily sync shifts the 7-day window by one day, so the comparable prior window is roughly
    /// <see cref="WindowDays"/> VN snapshot days earlier — not the previous calendar sync day.
    /// </summary>
    public static DateOnly ComparisonSnapshotDate(DateOnly snapshotDate) =>
        snapshotDate.AddDays(-WindowDays);

    /// <summary>
    /// Growth as a fraction (0.12 = +12%). Null when either side is missing or when the baseline is
    /// zero and the current value is not — "grew from nothing" has no meaningful percentage.
    /// </summary>
    public static decimal? Delta(decimal? current, decimal? previous)
    {
        if (current is null || previous is null)
        {
            return null;
        }

        if (previous.Value == 0m)
        {
            return current.Value == 0m ? 0m : null;
        }

        return Clamp(Round((current.Value - previous.Value) / previous.Value));
    }

    /// <summary>CTR derived from traffic when Compass did not send it.</summary>
    public static decimal? CtrFrom(long clicks, long impressions) =>
        impressions > 0 ? Round((decimal)clicks / impressions) : null;

    /// <summary>GMV clamped to the column range; negatives (refund noise) collapse to zero.</summary>
    public static decimal NormalizeMoney(decimal? value) =>
        value is null || value.Value <= 0m
            ? 0m
            : Math.Round(Math.Min(value.Value, MaxMoney), 2, MidpointRounding.AwayFromZero);

    /// <summary>Counts are never negative.</summary>
    public static int NormalizeCount(int? value) => value is null or < 0 ? 0 : value.Value;

    /// <summary>Counts are never negative.</summary>
    public static long NormalizeLong(long? value) => value is null or < 0 ? 0 : value.Value;

    /// <summary>Ratios keep their sign but stay inside <c>numeric(12,6)</c>.</summary>
    public static decimal? NormalizeRatio(decimal? value) =>
        value is null ? null : Clamp(Round(value.Value));

    /// <summary>Rounds to the 6 decimals the ratio/CTR columns store.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    /// <summary>Clamps to the ratio column range.</summary>
    public static decimal Clamp(decimal value) => Math.Clamp(value, -MaxRatio, MaxRatio);
}
