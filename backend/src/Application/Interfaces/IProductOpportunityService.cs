using Application.DTOs.ProductOpportunity;

namespace Application.Interfaces;

/// <summary>
/// Syncs TikTok Shop Product opportunities (trending keywords) from the browser extension
/// as canonical rows unique by lead_id, with per-shop links for admin/seller portals.
/// </summary>
public interface IProductOpportunityService
{
    /// <summary>Resolved Settings for extension Auto Sync.</summary>
    Task<ProductOpportunitySyncConfigDto> GetSyncConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Niche <c>category_infos</c> groups for the owner's shop matched by externalShopId.
    /// Throws when the shop is missing or belongs to another owner.
    /// </summary>
    Task<ProductOpportunitySyncContextDto> GetSyncContextAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert canonical keywords by TikTok <c>lead_id</c> and replace this shop's links.
    /// Duplicate <c>lead_id</c> is not inserted again; metrics refresh in place.
    /// </summary>
    Task<SyncProductOpportunitiesResultDto> SyncAsync(
        Guid ownerId,
        SyncProductOpportunitiesDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged list. Seller callers must set <c>OwnerId</c> to the JWT user (server-enforced).
    /// </summary>
    Task<ProductOpportunityListPageDto> ListAsync(
        ProductOpportunityListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlinks or deletes keywords. When <paramref name="shopId"/> is set, only that shop's link.
    /// Seller (<paramref name="ownerId"/> set, no shop) unlinks every owned shop.
    /// Admin with no shop deletes the canonical row (all links).
    /// That shop's bookmarks and «Shop này bán gì» items are pruned; analysis stays.
    /// </summary>
    Task<BulkDeleteProductOpportunitiesResultDto> BulkDeleteAsync(
        IReadOnlyList<Guid> ids,
        Guid? ownerId,
        Guid? shopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or unsave a snapshot keyword for one shop (survives the next sync).
    /// Unsave removes that shop's bookmark only — the snapshot row and shop-research item stay.
    /// When <paramref name="shopId"/> is set, the bookmark is for that shop (must be linked).
    /// When omitted, the representative linked shop is used. When <paramref name="ownerScope"/>
    /// is set, the shop must belong to that seller.
    /// </summary>
    Task<ProductOpportunitySavedResultDto> SetSavedAsync(
        Guid opportunityId,
        bool saved,
        Guid? ownerScope,
        Guid savedByUserId,
        Guid? shopId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save or unsave a keyword by text when there is no snapshot row (audience suggest).
    /// Unsave also drops the keyword from that shop's «Shop này bán gì» payload when no snapshot remains.
    /// When <paramref name="ownerScope"/> is set, the shop must belong to that seller.
    /// </summary>
    Task<ProductOpportunitySavedResultDto> SetSavedByKeywordAsync(
        Guid shopId,
        string keyword,
        bool saved,
        Guid? ownerScope,
        Guid savedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Light list of saved keywords for a shop (bookmark rows with this <c>ShopId</c>).
    /// When <paramref name="ownerScope"/> is set, the shop must belong to that seller.
    /// </summary>
    Task<SavedKeywordListDto> ListSavedKeywordsAsync(
        Guid shopId,
        Guid? ownerScope,
        CancellationToken cancellationToken = default);
}
