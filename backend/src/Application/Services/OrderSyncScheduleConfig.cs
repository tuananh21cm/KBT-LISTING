using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Pure parse / due-check helpers for the two order-sync loops. Kept free of DI so unit tests
/// can cover clamp and clock rules without spinning up the orchestrators.
/// </summary>
public static class OrderSyncScheduleConfig
{
    /// <summary>Parsed settings for the light new-orders loop.</summary>
    public readonly record struct NewOrdersConfig(
        bool Enabled,
        int IntervalMinutes,
        int LookbackDays,
        int BatchSize);

    /// <summary>Parsed settings for the heavier status loop.</summary>
    public readonly record struct StatusConfig(
        bool Enabled,
        int IntervalHours,
        int LookbackDays,
        int BatchSize)
    {
        /// <summary>Interval expressed in minutes for shared nextDue / overdue math.</summary>
        public int IntervalMinutes => IntervalHours * 60;
    }

    /// <summary>Parses the four <c>fourseller.order_sync.new.*</c> settings.</summary>
    public static NewOrdersConfig ParseNewOrders(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue(OrderSyncSettingKeys.NewEnabled, out var enabledRaw);
        values.TryGetValue(OrderSyncSettingKeys.NewIntervalMinutes, out var intervalRaw);
        values.TryGetValue(OrderSyncSettingKeys.NewLookbackDays, out var lookbackRaw);
        values.TryGetValue(OrderSyncSettingKeys.NewBatchSize, out var batchRaw);

        return new NewOrdersConfig(
            // Default on (same as the old single-loop seed) so a fresh deploy keeps catching orders.
            IsEnabled(enabledRaw, defaultEnabled: true),
            ClampInt(
                intervalRaw,
                OrderSyncSettingKeys.DefaultNewIntervalMinutes,
                OrderSyncSettingKeys.MinIntervalMinutes,
                OrderSyncSettingKeys.MaxIntervalMinutes),
            ClampInt(
                lookbackRaw,
                OrderSyncSettingKeys.DefaultNewLookbackDays,
                OrderSyncSettingKeys.MinNewLookbackDays,
                OrderSyncSettingKeys.MaxNewLookbackDays),
            ClampInt(
                batchRaw,
                OrderSyncSettingKeys.DefaultNewBatchSize,
                OrderSyncSettingKeys.MinBatchSize,
                OrderSyncSettingKeys.MaxBatchSize));
    }

    /// <summary>Parses the four <c>fourseller.order_sync.status.*</c> settings.</summary>
    public static StatusConfig ParseStatus(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue(OrderSyncSettingKeys.StatusEnabled, out var enabledRaw);
        values.TryGetValue(OrderSyncSettingKeys.StatusIntervalHours, out var intervalRaw);
        values.TryGetValue(OrderSyncSettingKeys.StatusLookbackDays, out var lookbackRaw);
        values.TryGetValue(OrderSyncSettingKeys.StatusBatchSize, out var batchRaw);

        return new StatusConfig(
            IsEnabled(enabledRaw, defaultEnabled: true),
            ClampInt(
                intervalRaw,
                OrderSyncSettingKeys.DefaultStatusIntervalHours,
                OrderSyncSettingKeys.MinIntervalHours,
                OrderSyncSettingKeys.MaxIntervalHours),
            ClampInt(
                lookbackRaw,
                OrderSyncSettingKeys.DefaultStatusLookbackDays,
                OrderSyncSettingKeys.MinStatusLookbackDays,
                OrderSyncSettingKeys.MaxStatusLookbackDays),
            ClampInt(
                batchRaw,
                OrderSyncSettingKeys.DefaultStatusBatchSize,
                OrderSyncSettingKeys.MinBatchSize,
                OrderSyncSettingKeys.MaxBatchSize));
    }

    /// <summary>
    /// True when no prior finish stamp exists, or enough time has elapsed since the last finish.
    /// Interval is measured from <paramref name="lastFinishedUtc"/> (end of run), not start.
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastFinishedUtc, int intervalMinutes, DateTimeOffset now) =>
        lastFinishedUtc is not { } last
        || now - last >= TimeSpan.FromMinutes(intervalMinutes);

    /// <summary>
    /// Splits <paramref name="owners"/> into successive batches of at most <paramref name="batchSize"/>.
    /// A non-positive batch size means "all at once" (manual sync keeps the old fan-out).
    /// </summary>
    public static IEnumerable<IReadOnlyList<T>> ChunkOwners<T>(IReadOnlyList<T> owners, int batchSize)
    {
        if (owners.Count == 0)
        {
            yield break;
        }

        var size = batchSize > 0 ? batchSize : owners.Count;
        for (var i = 0; i < owners.Count; i += size)
        {
            var take = Math.Min(size, owners.Count - i);
            var batch = new T[take];
            for (var j = 0; j < take; j++)
            {
                batch[j] = owners[i + j];
            }

            yield return batch;
        }
    }

    private static bool IsEnabled(string? raw, bool defaultEnabled)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultEnabled;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            return false;
        }

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
            || defaultEnabled;
    }

    private static int ClampInt(string? raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}
