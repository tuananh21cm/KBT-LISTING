using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Shop finance overview: TTS On hold snapshot + receiving-bank ledger tiền đã về.
/// </summary>
public interface IShopFinanceService
{
    /// <summary>
    /// Admin overview for all shops (optional owner + status + ledger date window).
    /// </summary>
    Task<ShopFinanceDto> GetForAdminAsync(
        ShopFinanceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seller overview scoped to <paramref name="ownerId"/> shops.
    /// </summary>
    Task<ShopFinanceDto> GetForSellerAsync(
        Guid ownerId,
        ShopFinanceQuery query,
        CancellationToken cancellationToken = default);
}
