using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Cascades denormalized <c>OwnerId</c> (4Seller cookie owner) when a shop is handed to another seller.
/// </summary>
public interface IShopOwnershipTransferService
{
    /// <summary>
    /// Rematch shop links for <paramref name="fromOwnerId"/> onto the shop, then reassign
    /// Orders / Listings / analytics denorm rows from <paramref name="fromOwnerId"/> to
    /// <paramref name="toOwnerId"/> where <c>ShopId</c> matches (or 4Seller shop id for origins).
    /// No-op when owners are equal. Throws <see cref="Domain.Exceptions.BusinessException"/>
    /// on <c>(OwnerId, PlatformOrderId)</c> unique conflicts.
    /// </summary>
    Task<ShopOwnershipTransferResultDto> TransferAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backfill: reassign all Orders/Listings (and denorm) still owned by another seller
    /// but linked to this shop onto the shop's current <c>OwnerId</c>.
    /// Also collapses duplicate listings that share the same TikTok product id on the shop.
    /// </summary>
    Task<ShopOwnershipTransferResultDto> ReassignMismatchedOwnedDataAsync(
        Guid shopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <see cref="ReassignMismatchedOwnedDataAsync"/> for every shop that still has
    /// Orders/Listings owned by a seller other than <c>Shop.OwnerId</c>.
    /// </summary>
    Task<ShopOwnershipTransferResultDto> ReassignAllMismatchedOwnedDataAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Post-sync safety net: collapses listings that share a TikTok product id on the given shops
    /// (and their same-<c>ExternalShopId</c> siblings), merging the thin shell's facts onto the
    /// richest row. Persists on its own. Never throws — sync results must not depend on it.
    /// </summary>
    /// <returns>How many shell rows were removed.</returns>
    Task<int> CollapseShopDuplicatesAsync(
        IReadOnlyCollection<Guid> shopIds,
        CancellationToken cancellationToken = default);
}
