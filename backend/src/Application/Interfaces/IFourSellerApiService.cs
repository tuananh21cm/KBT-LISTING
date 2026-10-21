using Application.DTOs.FourSeller;

namespace Application.Interfaces;

/// <summary>
/// HTTP client for 4Seller external APIs (uses the acting user's session cookie).
/// </summary>
public interface IFourSellerApiService
{
    /// <summary>
    /// Checks 4Seller login status via <c>GET /api/user/is-login</c>.
    /// Uses <paramref name="fourSellerCookie"/> when provided; otherwise the user's stored cookie.
    /// Does not throw for an invalid/expired session — returns <c>IsLoggedIn=false</c>.
    /// </summary>
    Task<FourSellerLoginCheckResponse> CheckLoginAsync(
        Guid userId,
        string? fourSellerCookie = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an OAuth authorize redirect URL via 4Seller.
    /// </summary>
    Task<FourSellerAuthRedirectResponse> GetAuthRedirectUrlAsync(
        Guid userId,
        FourSellerAuthRedirectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a shop is connected on 4Seller by matching shop list records.
    /// </summary>
    Task<FourSellerShopCheckResponse> CheckShopConnectionAsync(
        Guid userId,
        FourSellerShopCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all shops from 4Seller (<c>/api/shop/get-list</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerShopRecordDto>> GetShopListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the tidy shop list from 4Seller (<c>/api/shop/get-tidy-list</c>) for sync/upsert.
    /// </summary>
    Task<IReadOnlyList<FourSellerShopRecordDto>> GetTidyShopListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Matches a local shop against a preloaded 4Seller shop list.
    /// </summary>
    FourSellerShopCheckResponse MatchShopAgainstList(
        FourSellerShopCheckRequest request,
        IReadOnlyList<FourSellerShopRecordDto> fourSellerShops);

    /// <summary>
    /// Fetches TikTok category children for a parent from 4Seller meta API.
    /// </summary>
    Task<IReadOnlyList<FourSellerCategoryNodeDto>> GetCategoryListAsync(
        Guid userId,
        FourSellerCategoryListRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks 4Seller AI to recommend a TikTok category from a product title.
    /// </summary>
    Task<FourSellerAiCategoryResultDto> GenerateCategoryAsync(
        Guid userId,
        FourSellerAiCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches TikTok category attribute schema
    /// (<c>GET /api/meta/tiktok/get-category-attribute-list-by-id</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerCategoryAttributeDto>> GetCategoryAttributeListByIdAsync(
        Guid userId,
        string categoryId,
        long shopId,
        string? site = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits product URLs for 4Seller crawl.
    /// data: [] = accepted to pool; string URLs = confirm re-scrape; draft objects = already scraped.
    /// </summary>
    Task<FourSellerCrawlSubmitResult> SubmitCrawlUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes product-page HTML into 4Seller store drafts
    /// (<c>get-crawl-url-repeat</c> best-effort, then <c>postHtml-file-shop</c>).
    /// Cookie stays server-side — never returned to clients.
    /// </summary>
    Task<FourSellerPostHtmlResultDto> PostHtmlFileShopAsync(
        Guid userId,
        FourSellerPostHtmlRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists scraped move-draft products on 4Seller.
    /// </summary>
    Task<FourSellerMoveDraftPageDto> GetMoveDraftPageAsync(
        Guid userId,
        FourSellerMoveDraftPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves scraped drafts onto platform shops; returns a schedule code for polling.
    /// </summary>
    Task<FourSellerSaveToPlatformResponse> SaveToPlatformAsync(
        Guid userId,
        FourSellerSaveToPlatformRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls progress of a crawl/save schedule job.
    /// </summary>
    Task<FourSellerScheduleProgressDto> GetScheduleAsync(
        Guid userId,
        string scheduleCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes move-draft products on 4Seller (single or batch).
    /// </summary>
    Task DeleteMoveDraftsAsync(
        Guid userId,
        IReadOnlyList<long> moveDraftIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page of listings from 4Seller (<c>/api/listing/{platform}/page</c>).
    /// </summary>
    Task<FourSellerListingPageDto> GetListingPageAsync(
        Guid userId,
        string platform,
        FourSellerListingPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches listing details (batch-detail; optional per-id detail fallback).
    /// </summary>
    /// <param name="allowPerIdFallback">
    /// When true (default), falls back to sequential <c>/detail</c> if batch-detail is empty/fails.
    /// Use false for bulk sync — per-id fallback is too slow and triggers client timeouts.
    /// </param>
    Task<IReadOnlyList<FourSellerListingRecordDto>> GetListingBatchDetailAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default,
        bool allowPerIdFallback = true);

    /// <summary>
    /// Deletes listings on 4Seller (<c>POST /api/listing/{platform}/delete</c>, body = id array).
    /// </summary>
    Task DeleteListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches listing status counts (<c>POST /api/listing/get-status-count</c>).
    /// </summary>
    Task<FourSellerListingStatusCountDto> GetListingStatusCountAsync(
        Guid userId,
        string platform,
        string? shopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a listing draft on 4Seller (<c>POST /api/listing/{platform}/save-draft</c>).
    /// <paramref name="payload"/> should be the detail JSON object (or wrapper) 4Seller expects.
    /// </summary>
    Task<FourSellerListingPushResultDto> SaveListingDraftAsync(
        Guid userId,
        string platform,
        object payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a listing on 4Seller (<c>POST /api/listing/{platform}/publish</c>).
    /// </summary>
    Task<FourSellerListingPushResultDto> PublishListingAsync(
        Guid userId,
        string platform,
        object payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an already-published listing on 4Seller (<c>POST /api/listing/{platform}/update</c>).
    /// Content-only — does not activate/deactivate. <paramref name="siteStatus"/> is used for the
    /// edit-page referer (e.g. <c>active</c> / <c>inactive</c>).
    /// <paramref name="payload"/> must include the listing <c>id</c> (4Seller listing id).
    /// </summary>
    Task<FourSellerListingPushResultDto> UpdateListingAsync(
        Guid userId,
        string platform,
        object payload,
        string? siteStatus = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates published listings on 4Seller (<c>POST /api/listing/{platform}/batch-deactivate</c>).
    /// Body is the 4Seller listing ids as a string array.
    /// </summary>
    Task<FourSellerListingPushResultDto> DeactivateListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates inactive listings on 4Seller (<c>POST /api/listing/{platform}/batch-activate</c>).
    /// Body is the 4Seller listing ids as an array.
    /// </summary>
    Task<FourSellerListingPushResultDto> ActivateListingsAsync(
        Guid userId,
        string platform,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a listing on 4Seller (<c>GET /api/listing/{platform}/copy?listingId=</c>).
    /// Response <c>data</c> is the new 4Seller listing id.
    /// </summary>
    Task<FourSellerListingPushResultDto> CopyListingAsync(
        Guid userId,
        string platform,
        long listingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs one published listing from the marketplace into 4Seller local
    /// (<c>POST /api/listing/{platform}/sync-one-local-listing</c>).
    /// Body: <c>{ "productId": "&lt;tikTokProductId&gt;", "shopId": &lt;fourSellerShopId&gt; }</c>.
    /// </summary>
    Task SyncOneLocalListingAsync(
        Guid userId,
        string platform,
        string productId,
        long shopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads move-source / category-match info for draft listings that still need a TikTok category
    /// (<c>POST /api/listing/tiktok/batch-get-move-source-listing</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerMoveSourceListingDto>> BatchGetMoveSourceListingAsync(
        Guid userId,
        IReadOnlyList<long> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent TikTok categories used for a shop
    /// (<c>GET /api/ai/recent-use-category?shopId=&amp;site=</c>).
    /// Bind uses each row's <c>id</c>, not TikTok <c>categoryId</c>.
    /// </summary>
    Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentUseCategoriesAsync(
        Guid userId,
        long shopId,
        string? site = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds TikTok categories onto draft listings
    /// (<c>POST /api/listing/tiktok/batch-binging-category</c>).
    /// Each item's <c>categoryId</c> is a recent-use row id.
    /// </summary>
    Task BatchBindCategoryAsync(
        Guid userId,
        IReadOnlyList<FourSellerBindCategoryItemDto> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorized TikTok brands for a shop (<c>GET /api/meta/tiktok/get-brand-list-by-shop-id</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerBrandDto>> GetBrandListByShopIdAsync(
        Guid userId,
        long shopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Brand suggestions for a shop (<c>GET /api/meta/tiktok/get-brand-list-by-shop-id-brand-suggest</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsByShopIdAsync(
        Guid userId,
        long shopId,
        string brandName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// TikTok warehouses for a shop (<c>GET /api/meta/tiktok/get-warehouse-list-by-shop-id</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerWarehouseDto>> GetWarehouseListByShopIdAsync(
        Guid userId,
        long shopId,
        int fbt = 0,
        string? allocationMode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Brands + warehouses for a shop in one round-trip of cookie/is-login validation,
    /// then parallel HTTP to 4Seller (avoids sequential brand→warehouse latency).
    /// </summary>
    Task<(IReadOnlyList<FourSellerBrandDto> Brands, IReadOnlyList<FourSellerWarehouseDto> Warehouses)>
        GetBrandAndWarehouseListsByShopIdAsync(
            Guid userId,
            long shopId,
            int fbt = 0,
            string? allocationMode = null,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page of orders from 4Seller (<c>POST /api/v2/order/page</c>).
    /// </summary>
    Task<FourSellerOrderPageDto> GetOrderPageAsync(
        Guid userId,
        FourSellerOrderPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks 4Seller to pull fresh TikTok return / refund cases into its own database
    /// (<c>GET /api/returns/tiktok/sync</c>). Returns once 4Seller accepted the request — the pull
    /// itself is asynchronous on their side, so
    /// <see cref="GetTikTokReturnPageAsync"/> sees the result only on a later run.
    /// </summary>
    Task TriggerTikTokReturnSyncAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page of TikTok return / refund cases from 4Seller
    /// (<c>POST /api/returns/tiktok/page</c>), newest change first.
    /// </summary>
    /// <param name="userId">Seller whose cookie is used.</param>
    /// <param name="pageCurrent">1-based page.</param>
    /// <param name="pageSize">Rows per page (4Seller caps at 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FourSellerReturnPageDto> GetTikTokReturnPageAsync(
        Guid userId,
        int pageCurrent,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches order amount breakdown from 4Seller (<c>GET /api/v2/order/detail?id=</c>).
    /// </summary>
    Task<FourSellerOrderAmountDetailDto> GetOrderDetailAsync(
        Guid userId,
        long fourSellerOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-fetches order amount breakdowns. Validates cookie/proxy once, then GETs in parallel
    /// (safe for scoped DbContext). Failed ids are omitted from the result.
    /// </summary>
    Task<IReadOnlyDictionary<long, FourSellerOrderAmountDetailDto>> GetOrderDetailsAsync(
        Guid userId,
        IReadOnlyList<long> fourSellerOrderIds,
        int maxConcurrency = 2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists platform shipping carriers (<c>GET /api/v2/order/list-platform-shipping-service</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerShippingServiceDto>> ListPlatformShippingServicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists TikTok product-discount activities
    /// (<c>POST /api/promotion/tiktok/activity/get-page</c>).
    /// </summary>
    Task<FourSellerProductDiscountPageDto> GetProductDiscountActivityPageAsync(
        Guid userId,
        FourSellerProductDiscountPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a TikTok product-discount / flash-deal activity
    /// (<c>GET /api/promotion/tiktok/activity/deactivate-activity?id=</c>).
    /// Uses flash-deal referer when <paramref name="discountType"/> is FLASHSALE.
    /// </summary>
    Task DeactivateProductDiscountActivityAsync(
        Guid userId,
        long activityId,
        string? discountType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks orders shipped on 4Seller (<c>POST /api/v2/order/to-ship-mark-ship</c>).
    /// Returns the async request id from <c>data</c>.
    /// </summary>
    Task<string> MarkShipAsync(
        Guid userId,
        IReadOnlyList<FourSellerMarkShipItemDto> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls async mark-ship result (<c>GET /api/v2/order/get-async-ship-order-result</c>).
    /// </summary>
    Task<FourSellerAsyncShipOrderResultDto> GetAsyncShipOrderResultAsync(
        Guid userId,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates order notes on 4Seller (<c>POST /api/v2/order/update-note</c>).
    /// </summary>
    Task UpdateOrderNoteAsync(
        Guid userId,
        FourSellerUpdateOrderNoteDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file to 4Seller COS: <c>POST /api/cos/get-sign</c> then PUT to CDN.
    /// Returns the public URL for immediate UI display (e.g. Size Chart / Product video).
    /// </summary>
    Task<FourSellerCosUploadResultDto> UploadCosAsync(
        Guid userId,
        Stream content,
        string fileName,
        string contentType,
        string referenceType = "tiktok",
        CancellationToken cancellationToken = default);
}
