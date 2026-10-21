namespace Application.Interfaces;

/// <summary>Cancels in-flight auto publish when system or shop setting is turned off.</summary>
public interface IListingAutoPublishCancellationCoordinator
{
    /// <summary>Links global + shop tokens for a new run.</summary>
  CancellationToken LinkRun(Guid runId, Guid shopId, CancellationToken outer);

    /// <summary>Cancel all auto publish runs system-wide.</summary>
    void CancelAll(string reason);

    /// <summary>Cancel auto publish runs for one shop.</summary>
    void CancelShop(Guid shopId, string reason);

    /// <summary>Clear shop cancellation state after re-enabling auto publish.</summary>
    void ResetShop(Guid shopId);

    /// <summary>Unregister run when finished.</summary>
    void UnregisterRun(Guid runId, Guid shopId);
}
