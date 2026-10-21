using Application.DTOs.Promotion;

namespace Application.Interfaces;

/// <summary>
/// Lists and syncs TikTok product-discount / flash-deal activities from 4Seller into local DB.
/// </summary>
public interface IProductDiscountSyncService
{
    /// <summary>Paged list from local DB.</summary>
    Task<ProductDiscountListPageDto> ListAsync(
        ProductDiscountListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls ONGOING activities from 4Seller for logged-in sellers and upserts rows for Active shops only.
    /// When <see cref="ProductDiscountSyncRequest.DiscountType"/> is set (e.g. FLASHSALE),
    /// 4Seller is queried with that filter; otherwise product-discount page (empty type).
    /// After upsert, deletes local rows in the same seller + type scope that are not in the synced ONGOING set.
    /// </summary>
    Task<ProductDiscountSyncResultDto> SyncAsync(
        ProductDiscountSyncRequest request,
        IProgress<ProductDiscountSyncSellerProgressDto>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates an ONGOING activity on 4Seller and removes the local row.
    /// When <paramref name="restrictOwnerUserId"/> is set, the row must belong to that owner.
    /// When <paramref name="requiredDiscountType"/> is set, the row must match that type;
    /// when null, FLASHSALE rows are rejected (product-discount endpoints only).
    /// </summary>
    Task DeactivateAsync(
        Guid activityId,
        Guid? restrictOwnerUserId = null,
        string? requiredDiscountType = null,
        CancellationToken cancellationToken = default);
}
