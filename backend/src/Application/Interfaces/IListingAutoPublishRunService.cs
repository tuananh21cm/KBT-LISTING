namespace Application.Interfaces;

using Application.DTOs.Listing;

/// <summary>Read/write auto publish run logs.</summary>
public interface IListingAutoPublishRunService
{
    Task<List<ListingAutoPublishRunDto>> ListRunsAsync(
        Guid listingId,
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<ListingAutoPublishRunDto?> GetRunAsync(
        Guid listingId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task RetryAutoPublishAsync(Guid listingId, CancellationToken cancellationToken = default);
}
