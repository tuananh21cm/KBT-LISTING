namespace Application.Interfaces;

/// <summary>Global cap on concurrent auto publish pipelines.</summary>
public interface IListingAutoPublishConcurrencyGate
{
    /// <summary>Resize capacity from settings (safe between runs).</summary>
    void Configure(int maxConcurrentPublishes);

    /// <summary>Wait for a publish slot.</summary>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>Reserve a publish slot without blocking. Returns false when at capacity.</summary>
    bool TryAcquire(CancellationToken cancellationToken = default);

    /// <summary>Release a publish slot.</summary>
    void Release();

    /// <summary>Configured max concurrent pipelines.</summary>
    int Capacity { get; }

    /// <summary>Pipeline runs currently holding a slot.</summary>
    int InFlight { get; }

    /// <summary>Slots still available for new pipelines.</summary>
    int AvailableSlots { get; }
}
