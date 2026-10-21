using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// TikTok Compass product metrics: extension ingest (one row per product per Vietnam day)
/// and the paged read model shared by admin / seller portals.
/// </summary>
public interface IListingAnalyticsService
{
    /// <summary>
    /// Overwrites today's rows for the seller's shop with the scraped Compass numbers.
    /// Idempotent: re-running on the same day updates the same rows and bumps <c>SyncedAt</c>.
    /// </summary>
    Task<SyncListingAnalyticsResultDto> SyncAsync(
        Guid ownerId,
        SyncListingAnalyticsDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Paged rows for one snapshot day (latest day when the query omits it).</summary>
    Task<ListingAnalyticsPageDto> ListAsync(
        ListingAnalyticsListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Snapshot days that have data in the given scope (newest first).</summary>
    Task<ListingAnalyticsDatesDto> GetSnapshotDatesAsync(
        Guid? ownerUserId,
        Guid? shopId,
        CancellationToken cancellationToken = default);
}
