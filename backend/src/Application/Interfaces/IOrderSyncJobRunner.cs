using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Runs manual order syncs in the background so the HTTP request returns immediately and the UI
/// polls for progress instead of holding a connection open past Cloudflare's ~100s limit (524).
/// </summary>
public interface IOrderSyncJobRunner
{
    /// <summary>
    /// Queues a sync and returns its initial snapshot. The run is detached from the caller's
    /// request, so a browser / proxy timeout cannot cancel it mid-upsert.
    /// </summary>
    OrderSyncJobStatusDto Start(OrderSyncRequest request, Guid? startedByUserId);

    /// <summary>Current snapshot, or null when the id is unknown or has aged out.</summary>
    OrderSyncJobStatusDto? GetStatus(Guid jobId);

    /// <summary>
    /// Who started the job, used to stop one seller from polling another seller's run.
    /// Null when the job is unknown or was started without a user.
    /// </summary>
    Guid? GetStartedByUserId(Guid jobId);
}
