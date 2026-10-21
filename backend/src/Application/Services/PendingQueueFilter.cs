using System.Linq.Expressions;
using Domain.Entities;
using Domain.Orders;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Sub-filters for sider <c>/orders/pending</c> (always on top of <see cref="OrderWorkflowTabs.PendingProcess"/>).
/// </summary>
public static class PendingQueueFilter
{
    /// <summary>Đơn tạo trong ngày VN.</summary>
    public const string New = "new";

    /// <summary>Có <c>IssueStatus=Processing</c> (Hết hàng / Out Stock).</summary>
    public const string OutStock = "out_stock";

    /// <summary>Tuổi 0–&lt;24 giờ.</summary>
    public const string Hours24 = "24h";

    /// <summary>Tuổi 24–&lt;48 giờ.</summary>
    public const string Hours48 = "48h";

    /// <summary>Tuổi ≥48 giờ (quá 48h).</summary>
    public const string Over48h = "over_48h";

    /// <summary>
    /// Builds the EF predicate for a pending-queue sub-filter, or <c>null</c> when blank / unknown.
    /// Age buckets use <c>OrderPaidTime ?? OrderCreateTime ?? CreatedAt</c> relative to now.
    /// Shared by the list filter and the badge counts so the two cannot drift apart.
    /// </summary>
    public static Expression<Func<Order, bool>>? PredicateFor(string? pendingFilter)
    {
        if (string.IsNullOrWhiteSpace(pendingFilter))
        {
            return null;
        }

        var key = pendingFilter.Trim().ToLowerInvariant();
        var nowUtc = DateTime.UtcNow;

        return key switch
        {
            New => CreatedTodayVn(nowUtc),
            OutStock => o => o.IssueStatus == OrderIssueStatuses.Processing,
            Hours24 => AgeHours(nowUtc, minHoursInclusive: 0, maxHoursExclusive: 24),
            Hours48 => AgeHours(nowUtc, minHoursInclusive: 24, maxHoursExclusive: 48),
            Over48h => AgeHours(nowUtc, minHoursInclusive: 48, maxHoursExclusive: null),
            _ => null
        };
    }

    /// <summary>
    /// Applies a pending-queue sub-filter (no-op when blank / unknown).
    /// </summary>
    public static IQueryable<Order> WherePendingFilter(IQueryable<Order> q, string? pendingFilter)
    {
        var predicate = PredicateFor(pendingFilter);
        return predicate is null ? q : q.Where(predicate);
    }

    private static Expression<Func<Order, bool>> CreatedTodayVn(DateTime nowUtc)
    {
        var vnNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, AppTimeZones.Vietnam);
        var (dayFrom, dayTo) = AppTimeZones.GetVietnamDayUtcRange(DateOnly.FromDateTime(vnNow));
        return o =>
            (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) >= dayFrom
            && (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) <= dayTo;
    }

    /// <summary>
    /// Orders whose age is ≥ <paramref name="minHoursInclusive"/> and
    /// (optionally) &lt; <paramref name="maxHoursExclusive"/>.
    /// </summary>
    private static Expression<Func<Order, bool>> AgeHours(
        DateTime nowUtc,
        int minHoursInclusive,
        int? maxHoursExclusive)
    {
        var olderThanMin = nowUtc.AddHours(-minHoursInclusive);
        if (maxHoursExclusive is int maxHours)
        {
            var newerThanMax = nowUtc.AddHours(-maxHours);
            return o =>
                (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) <= olderThanMin
                && (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) > newerThanMax;
        }

        return o => (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) <= olderThanMin;
    }
}
