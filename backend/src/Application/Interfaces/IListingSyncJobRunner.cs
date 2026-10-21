using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// Runs manual listing syncs in the background so the HTTP request returns immediately and the UI
/// polls for progress instead of holding a connection open for minutes.
/// </summary>
public interface IListingSyncJobRunner
{
    /// <summary>
    /// Queues a sync and returns its initial snapshot. The run is detached from the caller's
    /// request, so a browser timeout or navigation cannot cancel it mid-upsert.
    /// </summary>
    ListingSyncJobStatusDto Start(ListingSyncRequest request, Guid? startedByUserId);

    /// <summary>Current snapshot, or null when the id is unknown or has aged out.</summary>
    ListingSyncJobStatusDto? GetStatus(Guid jobId);

    /// <summary>
    /// Who started the job, used to stop one seller from polling another seller's run.
    /// Null when the job is unknown or was started without a user.
    /// </summary>
    Guid? GetStartedByUserId(Guid jobId);
}
