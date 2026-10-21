using Application.DTOs.FourSeller;
using Application.DTOs.Listing;

namespace Application.Interfaces;

/// <summary>
/// Admin listing list / sync / push against local Listings mirror.
/// </summary>
public interface IListingSyncService
{
    /// <summary>Lists synced listings from local DB.</summary>
    Task<ListingListPageDto> ListAsync(ListingListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged recent listings for one owner (local DB only) — used by browser extension.
    /// Filter by <paramref name="pageType"/> and optional 4Seller shop ids; paging is Skip/Take in SQL.
    /// </summary>
    Task<ListingRecentPageDto> ListRecentListingsAsync(
        Guid ownerId,
        string? pageType = null,
        IReadOnlyList<long>? fourSellerShopIds = null,
        int pageCurrent = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extension tab badges: local draft COUNT + TikTok publish_limit for selected shops (no rows).
    /// </summary>
    Task<ListingTabCountsDto> GetTabCountsAsync(
        Guid ownerId,
        IReadOnlyList<long>? fourSellerShopIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight rows for extension badge sync: owned listings with SourceUrl + FourSellerShopId only.
    /// </summary>
    Task<IReadOnlyList<AddedSourceShopDto>> ListOwnedAddedSourceShopsAsync(
        Guid ownerId,
        int take = 5000,
        CancellationToken cancellationToken = default);

    /// <summary>Status counts from local DB (same filters as list, excluding siteStatus).</summary>
    Task<ListingStatusCountsDto> GetStatusCountsAsync(ListingListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight auto publish queue badges for draft listing cards (list poll — no variations / supplier join).
    /// </summary>
    Task<IReadOnlyList<ListingAutoPublishQueueStatusItemDto>> GetAutoPublishQueueStatusAsync(
        IReadOnlyList<Guid> listingIds,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets listing detail with variations.
    /// When <paramref name="refreshFromFourSeller"/> is true, best-effort refresh RawPayload from
    /// 4Seller per-id detail before mapping; otherwise returns local DB snapshot only (fast edit open).
    /// </summary>
    Task<ListingDetailDto> GetByIdAsync(
        Guid id,
        bool refreshFromFourSeller = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls listings from 4Seller and upserts local mirror.
    /// Multi-account runs fan out in parallel (one DI scope / cookie / proxy per seller).
    /// </summary>
    /// <param name="progress">
    /// Optional per-seller progress sink. Every seller is reported once as <c>pending</c> as soon as
    /// the owner list is known, then again on start and completion, so a caller can show partial
    /// results while slower accounts are still running.
    /// </param>
    Task<ListingSyncResultDto> SyncAsync(
        ListingSyncRequest request,
        IProgress<ListingSyncSellerProgressDto>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs listings for a single seller (caller holds per-seller lease). Used by parallel multi-account <see cref="SyncAsync"/>.
    /// </summary>
    Task<ListingOwnerSyncResultDto> SyncOwnerAsync(
        Guid ownerUserId,
        string platform,
        IReadOnlyList<string> statuses,
        string? shopId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? maxRecordsPerSeller,
        string? syncScope = null,
        IReadOnlyList<string>? listingIds = null,
        int? reconcileMaxPerSeller = null,
        bool purgeMissingOnReconcile = false,
        int reconcileMaxRounds = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Updates local fields (and patches RawPayload when present).</summary>
    Task<ListingDetailDto> UpdateAsync(Guid id, UpdateListingMirrorDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Local-only: persist description HTML, the AI SEO title and the Rewrite/Insert Images
    /// checklist flags. Patches the matching RawPayload keys — no 4Seller push.
    /// </summary>
    Task<ListingDescriptionPrepResultDto> SaveDescriptionPrepAsync(
        Guid id,
        UpdateListingDescriptionPrepDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Local-only: set <c>SourceUrl</c>, patch RawPayload source keys, and link an existing
    /// supplier snapshot when the URL key matches. No 4Seller push.
    /// </summary>
    Task<ListingSourceUrlResultDto> UpdateSourceUrlAsync(
        Guid id,
        UpdateListingSourceUrlDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches RawPayload from optional form DTO, pushes save-draft to 4Seller, then persists local.
    /// </summary>
    Task<ListingPushResultDto> SaveDraftAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches RawPayload from optional form DTO, pushes publish to 4Seller, then persists local.
    /// </summary>
    Task<ListingPushResultDto> PublishAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        string? publishSource = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dry-run of the push body: applies the same patch → sale-prop rebuild → sanitize pipeline as
    /// <see cref="SaveDraftAsync"/> but sends nothing to 4Seller and saves nothing locally.
    /// Diagnostics only — used to confirm a Color/Size edit reached the payload.
    /// </summary>
    Task<ListingPushPreviewDto> BuildPushPreviewAsync(
        Guid id,
        bool includePayload = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches RawPayload from optional form DTO, pushes content update (already-published listing) to
    /// 4Seller (<c>/api/listing/{platform}/update</c>), then persists local.
    /// Does not change Active/Inactive — use <see cref="ActivateAsync"/> / <see cref="DeactivateAsync"/>.
    /// </summary>
    Task<ListingPushResultDto> PushUpdateAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a published listing on 4Seller (<c>/api/listing/{platform}/batch-deactivate</c>)
    /// and marks it inactive locally.
    /// </summary>
    Task<ListingPushResultDto> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-deactivates Active listings with a 4Seller id (Hủy list). Non-Active / unlinked ids are skipped.
    /// Groups by owner+platform for one <c>batch-deactivate</c> call per group.
    /// </summary>
    Task<ListingDeactivateResultDto> DeactivateByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an inactive listing on 4Seller (<c>/api/listing/{platform}/batch-activate</c>)
    /// and marks it active locally.
    /// </summary>
    Task<ListingPushResultDto> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a listing on 4Seller (<c>GET /api/listing/{platform}/copy</c>), pulls the new draft
    /// detail, upserts a local mirror, and returns the new listing detail.
    /// </summary>
    Task<ListingDetailDto> DuplicateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks 4Seller to sync one listing from the marketplace
    /// (<c>/api/listing/{platform}/sync-one-local-listing</c>), then refreshes local RawPayload/detail.
    /// </summary>
    Task<ListingDetailDto> SyncOneAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads move-source / category-match info for a draft that still needs a TikTok category
    /// (<c>batch-get-move-source-listing</c>).
    /// </summary>
    Task<FourSellerMoveSourceListingDto> GetMoveSourceAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent TikTok categories for the listing's shop (<c>recent-use-category</c>).
    /// Optional <paramref name="shopId"/> / <paramref name="site"/> override listing defaults
    /// (typically from move-source <c>targetShopId</c>).
    /// </summary>
    Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentCategoriesAsync(
        Guid id,
        long? shopId = null,
        string? site = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds a recent-use category onto the listing via 4Seller <c>batch-binging-category</c>,
    /// then best-effort updates local <c>CategoryId</c> / <c>CategoryName</c>.
    /// </summary>
    Task BindCategoryAsync(
        Guid id,
        BindListingCategoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads shop-scoped TikTok brand + warehouse metadata via the listing owner's 4Seller cookie.
    /// </summary>
    Task<ListingShopMetadataDto> GetShopMetadataAsync(
        Guid listingId,
        Guid shopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests TikTok brands for a shop (listing-scoped ownership check).
    /// </summary>
    Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsAsync(
        Guid listingId,
        Guid shopId,
        string brandName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads Size Chart / Product video (or other media) to 4Seller COS via the
    /// listing owner's cookie. Returns CDN URL + localMaterialId for form display.
    /// </summary>
    Task<FourSellerCosUploadResultDto> UploadCosMediaAsync(
        Guid listingId,
        Stream content,
        string fileName,
        string contentType,
        string? referenceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a listing on 4Seller (<c>POST /api/listing/{platform}/delete</c>) then removes the local mirror.
    /// </summary>
    Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seller-scoped: deletes an owned draft listing on 4Seller then removes the local mirror.
    /// </summary>
    Task DeleteOwnedDraftAsync(Guid ownerId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes matching listings on 4Seller then removes local mirrors (ignores paging).
    /// </summary>
    Task<ListingDeleteResultDto> DeleteByFilterAsync(ListingListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the given local listing ids on 4Seller (when linked) then removes local mirrors.
    /// </summary>
    Task<ListingDeleteResultDto> DeleteByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}
