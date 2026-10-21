using Application.DTOs.Dashboard;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin dashboard aggregates (counts / sums only — no full entity lists).
/// </summary>
[ApiController]
[Route("api/v1/admin/dashboard")]
public class AdminDashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    /// <summary>
    /// Initializes the admin dashboard controller.
    /// </summary>
    public AdminDashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Ops snapshot + seller ranking for period day|week|month (Asia/Ho_Chi_Minh).
    /// Optional <c>include</c> limits which sections are computed (faster for stats pages).
    /// Pass <c>metrics=orders</c> for order-count-only seller/shop rankings (skip finance joins).
    /// </summary>
    [Authorize(Policy = "Permission:admin.dashboard.view")]
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> Summary(
        [FromQuery] DashboardQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSummaryAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(result));
    }

    /// <summary>
    /// Trend series: period/days or custom fromDate–toDate with grain day|week|month|year.
    /// Includes cost/profit per bucket. Pass <c>includeListed=false</c> to skip listing scans.
    /// Pass <c>metrics=orders</c> to count orders only (skip Items/cost joins).
    /// </summary>
    [Authorize(Policy = "Permission:admin.dashboard.view")]
    [HttpGet("trends")]
    [ProducesResponseType(typeof(ApiResponse<DashboardTrendsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardTrendsDto>>> Trends(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetTrendsAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardTrendsDto>.Ok(result));
    }

    /// <summary>
    /// Order-count heatmap: day × seller and/or day × shop (same period window as trends).
    /// Optional <c>dimension=seller|shop</c> skips the unused matrix.
    /// </summary>
    [Authorize(Policy = "Permission:admin.dashboard.view")]
    [HttpGet("order-heatmap")]
    [ProducesResponseType(typeof(ApiResponse<DashboardOrderHeatmapDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardOrderHeatmapDto>>> OrderHeatmap(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetOrderHeatmapAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardOrderHeatmapDto>.Ok(result));
    }

    /// <summary>
    /// Order-count heatmap: hour-of-day 0–23 aggregated across the selected VN range.
    /// Query <c>timezone=us|vn</c> (default us = America/Los_Angeles). Optional <c>dimension=total|seller</c>.
    /// </summary>
    [Authorize(Policy = "Permission:admin.dashboard.view")]
    [HttpGet("order-hourly-heatmap")]
    [ProducesResponseType(typeof(ApiResponse<DashboardOrderHourlyHeatmapDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardOrderHourlyHeatmapDto>>> OrderHourlyHeatmap(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetOrderHourlyHeatmapAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardOrderHourlyHeatmapDto>.Ok(result));
    }

    /// <summary>
    /// Seller performance: all active sellers with orders/revenue/profit and per-order averages
    /// for period day|week|month or custom fromDate–toDate (Asia/Ho_Chi_Minh).
    /// </summary>
    [Authorize(Policy = "Permission:admin.dashboard.view")]
    [HttpGet("seller-performance")]
    [ProducesResponseType(typeof(ApiResponse<SellerPerformanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SellerPerformanceDto>>> SellerPerformance(
        [FromQuery] SellerPerformanceQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSellerPerformanceAsync(query, cancellationToken);
        return Ok(ApiResponse<SellerPerformanceDto>.Ok(result));
    }
}
