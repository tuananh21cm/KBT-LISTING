using Application.Interfaces;

namespace Application.Services;

/// <summary>In-process signal to backfill auto publish when a pipeline completes.</summary>
public sealed class ListingAutoPublishDispatchTrigger : IListingAutoPublishDispatchTrigger
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // coalesced wake
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _signal.WaitAsync(cancellationToken);
}
