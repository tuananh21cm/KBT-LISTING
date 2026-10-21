using Application.DTOs.FourSeller;
using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Line-item reconcile helpers for 4Seller order upsert. Empty/omitted
/// <c>orderItemInfoList</c> must not wipe local rows (same rule as listing variations).
/// </summary>
public static class OrderItemSyncApply
{
    /// <summary>True when the payload has at least one real 4Seller line item id.</summary>
    public static bool HasRemoteItems(IReadOnlyList<FourSellerOrderItemDto> remoteItems)
    {
        foreach (var item in remoteItems)
        {
            if (item.OrderItemId > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// First row wins per <see cref="OrderItem.FourSellerOrderItemId"/> so a cartesian
    /// duplicate in the collection cannot throw on reconcile.
    /// </summary>
    public static Dictionary<long, OrderItem> IndexByRemoteId(IEnumerable<OrderItem> items)
    {
        var map = new Dictionary<long, OrderItem>();
        foreach (var item in items)
        {
            map.TryAdd(item.FourSellerOrderItemId, item);
        }

        return map;
    }
}
