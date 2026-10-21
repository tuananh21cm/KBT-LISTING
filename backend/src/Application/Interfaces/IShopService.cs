using Application.DTOs.Auth;
using Application.DTOs.FourSeller;
using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Shop management service.
/// </summary>
public interface IShopService
{
    /// <summary>Lists shops for the current user.</summary>
    Task<List<ShopDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active shops with 4Seller id for Product Scraper (includes listing published/active counts).
    /// </summary>
    Task<List<ScraperShopOptionDto>> GetScraperOptionsAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a shop by ID.</summary>
    Task<ShopDto> GetByIdAsync(Guid ownerId, Guid shopId, CancellationToken cancellationToken = default);

    /// <summary>Updates a shop owned by the current user.</summary>
    Task<ShopDto> UpdateAsync(Guid ownerId, Guid shopId, UpdateShopDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates the operational catalog profile of a shop owned by the current user.</summary>
    Task<ShopCatalogProfileDto> UpdateCatalogProfileAsync(
        Guid ownerId,
        Guid shopId,
        UpdateShopCatalogProfileDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Updates auto publish settings of a shop owned by the current user.</summary>
    Task<ShopAutoPublishDto> UpdateAutoPublishAsync(
        Guid ownerId,
        Guid shopId,
        UpdateShopAutoPublishDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts TikTok seller-us profile onto an existing shop owned by <paramref name="ownerId"/>
    /// (match <c>ExternalShopId</c> = <c>sellerId</c>). Never creates shops.
    /// </summary>
    Task<ShopDto> SyncTikTokProfileAsync(
        Guid ownerId,
        SyncTikTokShopProfileDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extension auto-sync config for TikTok Shop profile (enabled + intervalSeconds from Settings).
    /// </summary>
    Task<TikTokShopSyncConfigDto> GetTikTokShopSyncConfigAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which Sync All / Sync Fast steps are enabled (from Settings; missing → on).
    /// </summary>
    Task<TikTokUnifiedSyncConfigDto> GetTikTokUnifiedSyncConfigAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight auto publish scheduler badges for shop list rows (background poll — no full list reload).
    /// </summary>
    Task<IReadOnlyList<ShopAutoPublishSchedulerStatusItemDto>> GetAutoPublishSchedulerStatusAsync(
        IReadOnlyList<Guid> shopIds,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Orders with null <c>ShopId</c>, grouped by 4Seller shop id — surfaces reconnect / transfer gaps.
    /// </summary>
    Task<ShopMappingHealthDto> GetMappingHealthForAdminAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Lists all shops for admin (slim list rows).</summary>
    Task<List<AdminShopListItemDto>> GetAllForAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>Minimal shop options (id/name/owner/platform/status) for admin filter dropdowns.</summary>
    Task<List<AdminShopOptionDto>> GetOptionsForAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a shop by ID for admin.</summary>
    Task<AdminShopDto> GetByIdForAdminAsync(Guid shopId, CancellationToken cancellationToken = default);

    /// <summary>Creates a shop for a seller.</summary>
    Task<AdminShopDto> CreateForOwnerAsync(CreateAdminShopDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a shop for admin.</summary>
    Task<AdminShopDto> UpdateForAdminAsync(Guid shopId, UpdateAdminShopDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backfill: move Orders/Listings (and denorm) still owned by another seller onto the shop's current owner.
    /// Use after a historical owner change that did not cascade.
    /// </summary>
    Task<ShopOwnershipTransferResultDto> ReassignOwnedDataForAdminAsync(
        Guid shopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backfill ownership for every shop that still has mismatched Order/Listing owners
    /// (or duplicate TikTok listing shells).
    /// </summary>
    Task<ShopOwnershipTransferResultDto> ReassignAllOwnedDataForAdminAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Updates the operational catalog profile of any shop (admin).</summary>
    Task<ShopCatalogProfileDto> UpdateCatalogProfileForAdminAsync(
        Guid shopId,
        UpdateShopCatalogProfileDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Updates auto publish settings of any shop (admin).</summary>
    Task<ShopAutoPublishDto> UpdateAutoPublishForAdminAsync(
        Guid shopId,
        UpdateShopAutoPublishDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a shop for admin.</summary>
    Task DeleteForAdminAsync(Guid shopId, CancellationToken cancellationToken = default);

    /// <summary>Reorders all shops by the given ordered ID list (first = SortOrder 0).</summary>
    Task ReorderAsync(ReorderShopsDto dto, CancellationToken cancellationToken = default);

    /// <summary>Syncs 4Seller connection fields from a matched 4Seller shop record.</summary>
    Task<AdminShopDto> SyncFourSellerConnectionAsync(
        Guid shopId,
        FourSellerShopRecordDto matchedShop,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs 4Seller tidy-list onto shops already on the system (granted shops only).
    /// Updates matched locals; never creates new shops. Unmatched 4Seller records are skipped.
    /// </summary>
    Task<FourSellerSyncResponse> UpsertFromFourSellerListAsync(
        Guid ownerIdForCreates,
        IReadOnlyList<FourSellerShopRecordDto> fourSellerShops,
        bool scopeToOwnerOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin multi-cookie sync: users với cookie <c>logged_in</c> → tidy-list 4Seller → cập nhật shop local (theo OwnerId).
    /// Accounts run in parallel (one DI scope / cookie / proxy each).
    /// </summary>
    Task<FourSellerSyncResponse> SyncAllFromLoggedInCookiesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs shops for a single logged-in cookie owner (no global SyncGate).
    /// Used by parallel multi-account <see cref="SyncAllFromLoggedInCookiesAsync"/>.
    /// </summary>
    Task<FourSellerSyncResponse> SyncOwnerFromCookieAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
}
