namespace Application.Interfaces;

/// <summary>Runs the full auto publish pipeline for one listing.</summary>
public interface IListingAutoPublishOrchestrator
{
    /// <summary>Execute prep + publish; returns run id.</summary>
    Task<Guid> RunAsync(Guid listingId, CancellationToken cancellationToken = default);
}
