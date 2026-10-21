using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// «Đề xuất hủy list»: ranks the weakest products of one shop from its Compass snapshots so the
/// seller can free TikTok publish slots for the keywords «Shop này bán gì» is waiting to list.
/// </summary>
public interface IListingDelistSuggestionService
{
    /// <summary>
    /// Scores every product of one snapshot day against the shop medians and returns the weakest
    /// ones together with the slot budget and the keywords to list instead. Also upserts
    /// <c>ListingDelistDaySummary</c> so the shop-task board can show delist/scanned without
    /// re-scoring.
    /// </summary>
    Task<ListingDelistSuggestionDto> SuggestAsync(
        ListingDelistSuggestionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scores the shop's snapshot day and upserts <c>ListingDelistDaySummary</c> counters.
    /// Called after Compass analytics sync so the optimize board chip has numbers ready.
    /// No-ops when the day has no snapshot rows.
    /// </summary>
    Task ComputeAndPersistAsync(
        Guid shopId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default);
}
