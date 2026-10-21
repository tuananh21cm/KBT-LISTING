namespace Application.Interfaces;

/// <summary>Resolves auto publish queue status for draft listing list rows.</summary>
public interface IListingAutoPublishQueueStatusResolver
{
    /// <summary>
    /// Returns queue status and latest run status per listing id.
    /// Keys omitted when no queue badge should show.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ListingAutoPublishQueueStatusResult>> ResolveAsync(
        IReadOnlyList<Guid> listingIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolved queue badge for one listing.</summary>
public sealed record ListingAutoPublishQueueStatusResult(
    string QueueStatus,
    string? LastRunStatus,
    DateTime? NextPickAt = null);
