using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Coordinates the heavier platform-status order sync for BackgroundService and health keep-alive.
/// Shares <c>FourSellerSyncGate</c> with the light new-orders loop — while a status pass holds
/// the gate the new-orders loop pauses and retries on the next tick.
/// </summary>
public interface IOrderStatusSyncOrchestrator
{
    /// <summary>Cached snapshot (may lack Settings until first tick).</summary>
    OrderSyncRuntimeStatusDto GetStatus();

    /// <summary>
    /// Snapshot with <c>enabled</c> / <c>intervalMinutes</c> refreshed from Settings DB.
    /// </summary>
    Task<OrderSyncRuntimeStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fire-and-forget pulse (starts <see cref="RunIfDueAsync"/> without <c>Task.Run</c>).
    /// Used by <c>/health</c> so liveness stays fast.
    /// </summary>
    void ScheduleIfDue();

    /// <summary>
    /// Awaits a status sync when due. No-ops when disabled, still within interval, or another
    /// run is in progress. Pass <see cref="CancellationToken.None"/> when the sync must finish.
    /// </summary>
    Task RunIfDueAsync(CancellationToken cancellationToken = default);
}
