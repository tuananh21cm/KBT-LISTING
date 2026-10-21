namespace Application.Interfaces;

/// <summary>
/// Drives the after-sales (return / refund) sync loops on their configured intervals.
/// </summary>
public interface IAfterSalesSyncOrchestrator
{
    /// <summary>
    /// Runs whichever of the two loops (trigger / pull) is due. Never throws.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RunIfDueAsync(CancellationToken cancellationToken = default);
}
