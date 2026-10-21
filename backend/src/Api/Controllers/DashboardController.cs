using Application.DTOs.Dashboard;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller dashboard aggregates — always scoped to the authenticated user's OwnerId.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    /// <summary>
    /// Initializes the seller dashboard controller.
    /// </summary>
    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Ops snapshot for the current seller (Asia/Ho_Chi_Minh period windows).
    /// Client <c>ownerUserId</c> is ignored; JWT user id is forced.
    /// </summary>
    [Authorize(Policy = "Permission:seller.dashboard.view")]
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> Summary(
        [FromQuery] DashboardQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _dashboardService.GetSummaryAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(result));
    }

    /// <summary>
    /// Trend series for the current seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.dashboard.view")]
    [HttpGet("trends")]
    [ProducesResponseType(typeof(ApiResponse<DashboardTrendsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardTrendsDto>>> Trends(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _dashboardService.GetTrendsAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardTrendsDto>.Ok(result));
    }

    /// <summary>
    /// Order-count heatmap (day × shop / seller matrices) for the current seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.dashboard.view")]
    [HttpGet("order-heatmap")]
    [ProducesResponseType(typeof(ApiResponse<DashboardOrderHeatmapDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardOrderHeatmapDto>>> OrderHeatmap(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _dashboardService.GetOrderHeatmapAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardOrderHeatmapDto>.Ok(result));
    }

    /// <summary>
    /// Hour-of-day heatmap for the current seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.dashboard.view")]
    [HttpGet("order-hourly-heatmap")]
    [ProducesResponseType(typeof(ApiResponse<DashboardOrderHourlyHeatmapDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardOrderHourlyHeatmapDto>>> OrderHourlyHeatmap(
        [FromQuery] DashboardTrendsQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _dashboardService.GetOrderHourlyHeatmapAsync(query, cancellationToken);
        return Ok(ApiResponse<DashboardOrderHourlyHeatmapDto>.Ok(result));
    }

    /// <summary>
    /// Operational performance for the current seller (KPI + growth days).
    /// Client <c>ownerUserId</c> is ignored; JWT user id is forced.
    /// </summary>
    [Authorize(Policy = "Permission:seller.dashboard.view")]
    [HttpGet("seller-performance")]
    [ProducesResponseType(typeof(ApiResponse<SellerPerformanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SellerPerformanceDto>>> SellerPerformance(
        [FromQuery] SellerPerformanceQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _dashboardService.GetSellerPerformanceAsync(query, cancellationToken);
        return Ok(ApiResponse<SellerPerformanceDto>.Ok(result));
    }

    private void ScopeToCurrentUser(DashboardQuery query)
    {
        query.OwnerUserId = User.GetUserId();
    }
}
