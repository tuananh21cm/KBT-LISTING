using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>
/// Detects USPS / Shein warehouse fulfil-handling signals for Telegram realtime notify.
/// Mirrors <see cref="OrderWorkflowTabFilter"/> USPS + Thank Card pending rules.
/// </summary>
public static class OrderFulfilHandlingSignals
{
    /// <summary>
    /// True when order currently needs USPS BuyTrack work: unprocessed USPS signal,
    /// not refund/cancel, and package not already in transit / delivered.
    /// </summary>
    public static bool HasUspsUnprocessed(Order order)
    {
        if (order.UspsProcessedAt.HasValue) return false;
        if (!HasUspsSignal(order)) return false;
        if (IsTerminalCommerce(order)) return false;
        if (IsAlreadyInTransitOrDelivered(order)) return false;
        return true;
    }

    /// <summary>TikTok refund/cancelled — no remaining USPS scan / BuyTrack work.</summary>
    public static bool IsTerminalCommerce(Order order)
    {
        if (order.WorkflowTab is OrderWorkflowTabs.Refund or OrderWorkflowTabs.Cancelled)
        {
            return true;
        }

        return PlatformOrderStatuses.IsNonRevenue(order.PlatformOrderStatus);
    }

    /// <summary>
    /// Package already moving or delivered — scan / USPS queue not useful.
    /// Uses fulfil clocks + platform / workflow <c>IN_TRANSIT</c> / <c>DELIVERED</c>
    /// (not <c>AWAITING_COLLECTION_GROUP</c> / «Chờ lấy»).
    /// </summary>
    public static bool IsAlreadyInTransitOrDelivered(Order order)
    {
        if (order.InTransitAt is not null || order.DeliveredAt is not null)
        {
            return true;
        }

        if (order.WorkflowTab is OrderWorkflowTabs.InTransit or OrderWorkflowTabs.Delivered)
        {
            return true;
        }

        var platform = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
        return platform is PlatformOrderStatuses.InTransit or PlatformOrderStatuses.Delivered;
    }

    /// <summary>True when any Shein warehouse / local tracking signal is present.</summary>
    public static bool HasWarehouseTrack(Order order)
    {
        if (order.ShippingService is not null
            && order.ShippingService.Contains("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (LooksLikeWarehouseCode(order.TrackingNo))
        {
            return true;
        }

        if (order.SupplierPurchases is null) return false;

        foreach (var p in order.SupplierPurchases)
        {
            if (!string.IsNullOrWhiteSpace(p.WarehouseTrackingNo)) return true;
            if (string.Equals(p.TrackingKind, SupplierTrackingKinds.Warehouse, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.Carrier is not null
                && p.Carrier.Contains("warehouse", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (LooksLikeWarehouseCode(p.TrackingNo)) return true;
        }

        return false;
    }

    /// <summary>
    /// Thank Card còn việc: tín hiệu kho, chưa stamp, chưa có last-mile / mark ship.
    /// Mirrors <c>OrderWorkflowTabFilter</c> <c>THANK_CARD_PENDING</c>.
    /// </summary>
    public static bool NeedsThankCard(Order order)
    {
        if (order.ThankCardProcessedAt.HasValue) return false;
        if (!HasWarehouseTrack(order)) return false;
        if (OrderWorkflowTabs.HasCarrierTracking(order)) return false;

        var platform = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
        if (platform is PlatformOrderStatuses.AwaitingCollection
            or PlatformOrderStatuses.InTransit
            or PlatformOrderStatuses.Delivered)
        {
            return false;
        }

        return true;
    }

    /// <summary>Best USPS tracking number for display.</summary>
    public static string? ResolveUspsTrackingNo(Order order)
    {
        if (order.ShippingService is not null
            && order.ShippingService.Contains("usps", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(order.TrackingNo)
            && !LooksLikeWarehouseCode(order.TrackingNo))
        {
            return order.TrackingNo.Trim();
        }

        if (order.SupplierPurchases is null) return null;

        foreach (var p in order.SupplierPurchases)
        {
            if (p.Carrier is null || !p.Carrier.Contains("usps", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(p.TrackingNo) && !LooksLikeWarehouseCode(p.TrackingNo))
                return p.TrackingNo.Trim();
        }

        if (!string.IsNullOrWhiteSpace(order.TrackingNo) && !LooksLikeWarehouseCode(order.TrackingNo))
            return order.TrackingNo.Trim();

        return null;
    }

    /// <summary>Best warehouse tracking number for display.</summary>
    public static string? ResolveWarehouseTrackingNo(Order order)
    {
        if (order.SupplierPurchases is not null)
        {
            foreach (var p in order.SupplierPurchases)
            {
                if (!string.IsNullOrWhiteSpace(p.WarehouseTrackingNo))
                    return p.WarehouseTrackingNo.Trim();
            }

            foreach (var p in order.SupplierPurchases)
            {
                if (LooksLikeWarehouseCode(p.TrackingNo))
                    return p.TrackingNo!.Trim();
            }
        }

        return LooksLikeWarehouseCode(order.TrackingNo) ? order.TrackingNo!.Trim() : null;
    }

    /// <summary>
    /// True when a USPS last-mile signal is present, regardless of the ops stamp:
    /// the order shipping service or any supplier link carrier mentions USPS.
    /// </summary>
    public static bool HasUspsSignal(Order order)
    {
        if (order.ShippingService is not null
            && order.ShippingService.Contains("usps", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (order.SupplierPurchases is null) return false;

        foreach (var p in order.SupplierPurchases)
        {
            if (p.Carrier is not null
                && p.Carrier.Contains("usps", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeWarehouseCode(string? trackingNo)
    {
        if (string.IsNullOrWhiteSpace(trackingNo)) return false;
        var t = trackingNo.Trim();
        if (t.Length < 11) return false;
        var c = t[0];
        if (c is not ('C' or 'c' or 'N' or 'n')) return false;
        for (var i = 1; i < t.Length; i++)
        {
            if (!char.IsDigit(t[i])) return false;
        }

        return t.Length - 1 >= 10;
    }
}
