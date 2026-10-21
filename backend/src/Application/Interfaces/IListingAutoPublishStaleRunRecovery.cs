namespace Application.Interfaces;

/// <summary>Marks orphan auto publish runs stuck in Running after crashes.</summary>
public interface IListingAutoPublishStaleRunRecovery
{
    /// <summary>Mark stale Running runs as failed.</summary>
    Task<int> RecoverStaleRunsAsync(CancellationToken cancellationToken = default);
}
