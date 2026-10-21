using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Resolves which seller's 4Seller cookie to use for Mark Ship / shipping-services.
/// Prefers live <see cref="Shop.OwnerId"/> after shop transfer; falls back to denorm <see cref="Order.OwnerId"/>.
/// </summary>
public static class OrderFourSellerOwner
{
    /// <summary>
    /// In-memory resolve when <see cref="Order.Shop"/> may already be loaded.
    /// </summary>
    public static Guid Resolve(Order order)
        => order.Shop?.OwnerId ?? order.OwnerId;

    /// <summary>
    /// Loads shop owner when navigation is missing but <see cref="Order.ShopId"/> is set.
    /// </summary>
    public static async Task<Guid> ResolveAsync(
        Order order,
        IQueryable<Shop> shops,
        CancellationToken cancellationToken = default)
    {
        if (order.Shop is not null)
        {
            return order.Shop.OwnerId;
        }

        if (order.ShopId is Guid shopId)
        {
            var ownerId = await shops
                .AsNoTracking()
                .Where(s => s.Id == shopId)
                .Select(s => (Guid?)s.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (ownerId.HasValue)
            {
                return ownerId.Value;
            }
        }

        return order.OwnerId;
    }
}
