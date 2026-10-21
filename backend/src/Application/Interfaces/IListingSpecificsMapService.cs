using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// Maps free-form Shein/Temu description specifics onto the TikTok/4Seller category schema.
/// Suggestion only — does not persist.
/// </summary>
public interface IListingSpecificsMapService
{
    /// <summary>
    /// Parses description pairs, loads category attribute schema via the listing owner's
    /// 4Seller cookie, then hybrid-matches (deterministic → AI) with confidence thresholds.
    /// Zero Key:Value pairs returns an empty result (no 4Seller/OpenAI call) instead of throwing.
    /// </summary>
    Task<MapListingSpecificsResultDto> MapAsync(
        Guid listingId,
        MapListingSpecificsRequest request,
        CancellationToken cancellationToken = default);
}
