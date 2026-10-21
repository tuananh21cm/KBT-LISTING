using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Coordinates automatic 4Seller order sync for BackgroundService and health keep-alive.
/// IIS may recycle the worker; keep-alive hits must be able to drive sync when due.
/// </summary>
public interface IOrderSyncOrchestrator
{
    /// <summary>Cached snapshot (may lack Settings until first tick).</summary>
    OrderSyncRuntimeStatusDto GetStatus();

    /// <summary>
    /// Snapshot with <c>enabled</c> / <c>intervalMinutes</c> refreshed from Settings DB.
    /// Used by <c>/health</c> so the first keep-alive response is not all-null.
    /// </summary>
    Task<OrderSyncRuntimeStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fire-and-forget pulse (starts <see cref="RunIfDueAsync"/> without <c>Task.Run</c>).
    /// Used by <c>/health</c> so liveness stays fast while BackgroundService / pulse still drive sync.
    /// </summary>
    void ScheduleIfDue();

    /// <summary>
    /// Awaits a sync when due (BackgroundService). Health keep-alive uses <see cref="ScheduleIfDue"/> instead.
    /// No-ops when disabled, still within interval, or another run is in progress.
    /// Pass <see cref="CancellationToken.None"/> when the sync must finish for Telegram.
    /// </summary>
    Task RunIfDueAsync(CancellationToken cancellationToken = default);
}
