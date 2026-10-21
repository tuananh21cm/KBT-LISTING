namespace Application.Services;

/// <summary>Computes adaptive poll delay for auto publish background scheduler.</summary>
public static class ListingAutoPublishScheduleCalculator
{
    public const int MinPollSeconds = 5;
    public const int IdlePollSeconds = 30;

    /// <summary>Per-shop inputs for next-delay calculation.</summary>
    public sealed record ShopScheduleRow(
        Guid Id,
        int? IntervalSeconds,
        DateTime? LastAutoPublishAt,
        bool HasEligibleDraft);

    /// <summary>
    /// Returns how long to sleep before the next scheduler round.
    /// Shop publish spacing is driven by per-shop interval seconds, not system scheduler minutes.
    /// </summary>
    public static TimeSpan ComputeNextDelay(
        DateTime utcNow,
        IReadOnlyList<ShopScheduleRow> shops,
        int defaultIntervalSeconds)
    {
        if (shops.Count == 0)
        {
            return TimeSpan.FromSeconds(IdlePollSeconds);
        }

        var eligibleShops = shops.Where(s => s.HasEligibleDraft).ToList();
        if (eligibleShops.Count == 0)
        {
            return TimeSpan.FromSeconds(IdlePollSeconds);
        }

        foreach (var shop in eligibleShops)
        {
            var interval = shop.IntervalSeconds ?? defaultIntervalSeconds;
            var nextReadyAt = shop.LastAutoPublishAt.HasValue
                ? shop.LastAutoPublishAt.Value.AddSeconds(interval)
                : utcNow;

            if (nextReadyAt <= utcNow)
            {
                return TimeSpan.FromSeconds(MinPollSeconds);
            }
        }

        var earliestReady = eligibleShops
            .Select(shop =>
            {
                var interval = shop.IntervalSeconds ?? defaultIntervalSeconds;
                return shop.LastAutoPublishAt.HasValue
                    ? shop.LastAutoPublishAt.Value.AddSeconds(interval)
                    : utcNow;
            })
            .Min();

        return ClampDelay(earliestReady - utcNow);
    }

    internal static TimeSpan ClampDelay(TimeSpan delay)
    {
        var min = TimeSpan.FromSeconds(MinPollSeconds);
        var max = TimeSpan.FromSeconds(IdlePollSeconds);

        if (delay < min)
        {
            return min;
        }

        if (delay > max)
        {
            return max;
        }

        return delay;
    }
}
