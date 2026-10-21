using Application.DTOs.Shein;

namespace Application.Interfaces;

/// <summary>
/// Backend-driven SHEIN detail sweep: replays <c>get_order_info</c> for billnos already in the
/// ledger that still lack real carrier tracking or a published Mark Ship, using the cookie +
/// headers the extension pushed onto the fulfil account (through that account's proxy).
///
/// It never paginates <c>order/list</c> — discovering new SHEIN orders stays with the extension.
/// Each auto cycle snapshots <b>all</b> eligible billnos, processes them in <c>max_billnos</c>
/// batches, then rests for <c>interval_minutes</c> — no overlapping cycles (single-flight).
/// </summary>
public interface ISheinOrderDetailSweepService
{
    /// <summary>
    /// Runs one full cycle if enabled, idle, and the rest interval after the previous cycle
    /// elapsed. Returns null when skipped (disabled, resting, or another cycle in flight).
    /// </summary>
    Task<SheinDetailSweepResult?> RunIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one full cycle now (ignores enabled + interval). Skips with a note if a cycle is
    /// already running (single-flight).
    /// </summary>
    Task<SheinDetailSweepResult> RunNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync track for one platform order: <c>get_order_info</c> for every SHEIN billno on the
    /// order, full-replace cost breakdown (phase detail / updateMode all), then Auto Mark Ship.
    /// </summary>
    Task<SheinOrderSyncTrackResult> RunForOrderAsync(
        Guid orderId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
