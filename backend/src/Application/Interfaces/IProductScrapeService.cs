using Application.DTOs.FourSeller;

namespace Application.Interfaces;

/// <summary>
/// Product scrape with local ownership over the acting user's 4Seller crawl pool.
/// </summary>
public interface IProductScrapeService
{
    /// <summary>
    /// Creates a scrape batch for the user and submits URLs to 4Seller.
    /// </summary>
    Task<FourSellerCrawlUrlsResponse> SubmitUrlsAsync(
        Guid userId,
        FourSellerCrawlUrlsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists owned drafts (optionally claim a batch against 4Seller first).
    /// </summary>
    Task<FourSellerMoveDraftPageDto> GetDraftsAsync(
        Guid userId,
        bool isAdmin,
        FourSellerMoveDraftPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves owned drafts to platform shops.
    /// </summary>
    Task<FourSellerSaveToPlatformResponse> SaveToPlatformAsync(
        Guid userId,
        bool isAdmin,
        FourSellerSaveToPlatformRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes owned drafts on 4Seller and locally.
    /// </summary>
    Task DeleteDraftsAsync(
        Guid userId,
        bool isAdmin,
        FourSellerDeleteDraftsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proxies schedule progress from 4Seller.
    /// </summary>
    Task<FourSellerScheduleProgressDto> GetScheduleAsync(
        Guid userId,
        string scheduleCode,
        CancellationToken cancellationToken = default);
}
