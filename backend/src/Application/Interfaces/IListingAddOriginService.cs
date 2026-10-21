using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Stages listing add origin at post-html and copies it onto listings after draft sync.
/// </summary>
public interface IListingAddOriginService
{
    /// <summary>
    /// Upsert origin for each shop in <paramref name="shopIds"/>. No-op when origin/URL is missing.
    /// </summary>
    Task UpsertFromPostHtmlAsync(
        Guid ownerId,
        string? sourceUrl,
        IReadOnlyList<long> shopIds,
        string? addedFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Write-once copy of staging origin onto listings that have SourceUrl + FourSellerShopId.
    /// </summary>
    Task ApplyToListingsAsync(
        IReadOnlyCollection<Listing> listings,
        CancellationToken cancellationToken = default);
}
