using Application.DTOs.Order;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Helpers;

namespace Api.Controllers;

/// <summary>
/// Lightweight liveness endpoint for IIS preload / keep-alive.
/// Background jobs (order sync, listing sync, Telegram) run in-process — the app pool must stay warm.
/// Each successful hit also pulses the new-orders loop when due, and the status loop only
/// when new-orders does not need priority (not running / not overdue / has finished at least once).
/// </summary>
[ApiController]
[Route("api/v1/health")]
[Route("api/v1/healthy")]
[Route("health")]
[Route("healthy")]
public class HealthController : ControllerBase
{
    private readonly IOrderSyncOrchestrator _orderSyncOrchestrator;
    private readonly IOrderStatusSyncOrchestrator _orderStatusSyncOrchestrator;

    /// <summary>
    /// Initializes the health controller.
    /// </summary>
    public HealthController(
        IOrderSyncOrchestrator orderSyncOrchestrator,
        IOrderStatusSyncOrchestrator orderStatusSyncOrchestrator)
    {
        _orderSyncOrchestrator = orderSyncOrchestrator;
        _orderStatusSyncOrchestrator = orderStatusSyncOrchestrator;
    }

    /// <summary>
    /// Returns OK when the API process is running. Anonymous so IIS Application Initialization and
    /// external pings can wake/keep the worker without auth. Pulses new-orders when due; pulses
    /// status only when new-orders does not need priority. Does not await the full 4Seller sync.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // Pulse sync without blocking liveness: BackgroundService + ScheduleIfDue drive the work.
        // Awaiting RunIfDueAsync here previously held /health for 60–150s during 4Seller sync.
        // Prefer new-orders: status holds FourSellerSyncGate for the whole heavy pass.
        _orderSyncOrchestrator.ScheduleIfDue();
        var newOrders = await _orderSyncOrchestrator.GetStatusAsync(cancellationToken);
        var newNeedsPriority =
            newOrders.IsRunning
            || newOrders.SecondsOverdue is > 0
            || (newOrders.Enabled == true && newOrders.LastFinishedUtc is null);
        if (!newNeedsPriority)
        {
            _orderStatusSyncOrchestrator.ScheduleIfDue();
        }

        var status = await _orderStatusSyncOrchestrator.GetStatusAsync(cancellationToken);
        var vietnamNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, AppTimeZones.Vietnam);

        return Ok(new
        {
            status = "ok",
            vietnamTime = vietnamNow,
            timezone = AppTimeZones.VietnamId,
            orderSync = new
            {
                newOrders = ToHealthBranch(newOrders),
                status = ToHealthBranch(status)
            }
        });
    }

    private static object ToHealthBranch(OrderSyncRuntimeStatusDto sync) => new
    {
        sync.Enabled,
        sync.IntervalMinutes,
        lastStartedVietnam = ToVietnamTime(sync.LastStartedUtc),
        lastFinishedVietnam = ToVietnamTime(sync.LastFinishedUtc),
        sync.LastSummary,
        sync.IsRunning,
        nextDueVietnam = ToVietnamTime(sync.NextDueUtc),
        sync.SecondsOverdue
    };

    private static DateTimeOffset? ToVietnamTime(DateTimeOffset? value) =>
        value is null
            ? null
            : TimeZoneInfo.ConvertTime(value.Value, AppTimeZones.Vietnam);
}
