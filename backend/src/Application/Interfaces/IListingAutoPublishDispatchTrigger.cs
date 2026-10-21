namespace Application.Interfaces;

/// <summary>Wakes the auto publish scheduler when a pipeline slot frees.</summary>
public interface IListingAutoPublishDispatchTrigger
{
    /// <summary>Notify the scheduler that a publish slot may be available.</summary>
    void Signal();

    /// <summary>Wait until the next dispatch round should run.</summary>
    Task WaitAsync(CancellationToken cancellationToken = default);
}
