using Application.DTOs.Supplier;
using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Upserts supplier PDP snapshots (seller + product) and applies them onto matching listings.
/// </summary>
public interface ISupplierProductInfoService
{
    /// <summary>
    /// Upsert seller + product by source URL key and patch existing listings that share the key.
    /// No-op when there is nothing to persist.
    /// </summary>
    Task UpsertFromUrlAsync(
        SupplierProductInfoUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Apply snapshot onto a listing when present.</summary>
    Task ApplyToListingAsync(Listing listing, CancellationToken cancellationToken = default);

    /// <summary>Batch-apply snapshots onto listings (one DB round-trip for lookups).</summary>
    Task ApplyToListingsAsync(
        IReadOnlyCollection<Listing> listings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist Bunny CDN size chart URL on supplier product snapshot by source URL key.
    /// </summary>
    Task SetSizeChartImageUrlAsync(
        SupplierSizeChartImageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Read stored size chart capture URL for a source product URL.</summary>
    Task<SupplierSizeChartImageStatusDto> GetSizeChartImageStatusAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default);
}
