using Application.DTOs.Dashboard;

namespace Application.Interfaces;

/// <summary>
/// Admin dashboard aggregate queries (counts / sums only).
/// </summary>
public interface IDashboardService
{
    /// <summary>Ops snapshot + seller ranking for the selected period (VN). Optional include sections.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(
        DashboardQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Orders / revenue / cost / profit / listed series.
    /// Supports period|days lookback, or custom fromDate/toDate + grain (day|week|month|year).
    /// Pass <c>includeListed=false</c> to skip listing scans.
    /// Pass <c>metrics=orders</c> to count orders only (skip Items/cost joins).
    /// </summary>
    Task<DashboardTrendsDto> GetTrendsAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Order-count heatmap by day × seller and/or day × shop.
    /// Pass <c>dimension=seller|shop</c> to compute only the active UI toggle.
    /// </summary>
    Task<DashboardOrderHeatmapDto> GetOrderHeatmapAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Order-count heatmap by hour-of-day (0–23), aggregated across the selected VN date range.
    /// Query <c>timezone=us|vn</c> selects America/Los_Angeles (default) or Asia/Ho_Chi_Minh.
    /// Pass <c>dimension=total|seller</c> to compute only that matrix.
    /// </summary>
    Task<DashboardOrderHourlyHeatmapDto> GetOrderHourlyHeatmapAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All active Seller-role users with orders/revenue/profit and per-order averages
    /// for the selected VN period (or custom fromDate–toDate).
    /// </summary>
    Task<SellerPerformanceDto> GetSellerPerformanceAsync(
        SellerPerformanceQuery query,
        CancellationToken cancellationToken = default);
}
