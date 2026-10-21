namespace Application.Interfaces;

/// <summary>Persists which auto publish pipeline steps already succeeded for a listing.</summary>
public interface IListingAutoPublishStepCheckpointService
{
    /// <summary>Load persisted checkpoints; hydrate from last failed run when empty.</summary>
    Task<HashSet<string>> EnsureLoadedAsync(Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>Mark a step completed and persist immediately (survives run crash).</summary>
    Task MarkCompletedAsync(Guid listingId, string stepCode, CancellationToken cancellationToken = default);

    /// <summary>Clear all checkpoints after full publish success or when listing leaves draft.</summary>
    Task ClearAsync(Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>Drop checkpoints from <paramref name="stepCode"/> onward (inclusive).</summary>
    Task InvalidateFromAsync(Guid listingId, string stepCode, CancellationToken cancellationToken = default);
}
