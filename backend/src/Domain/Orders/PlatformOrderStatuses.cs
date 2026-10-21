namespace Domain.Orders;

/// <summary>
/// Canonical TikTok/platform order statuses stored on <c>Order.PlatformOrderStatus</c>.
/// 4Seller workflow <c>orderStatus</c> (to_ship, shipped, …) is not used for local UI/filters.
/// Admin tabs may use composite keys in <see cref="OrderWorkflowTabs"/>; raw TikTok values stay here.
/// </summary>
public static class PlatformOrderStatuses
{
    /// <summary>On hold.</summary>
    public const string OnHold = "ON_HOLD";

    /// <summary>Awaiting shipment (Mark Ship eligible).</summary>
    public const string AwaitingShipment = "AWAITING_SHIPMENT";

    /// <summary>Awaiting carrier collection after mark-ship.</summary>
    public const string AwaitingCollection = "AWAITING_COLLECTION";

    /// <summary>In transit.</summary>
    public const string InTransit = "IN_TRANSIT";

    /// <summary>Delivered to buyer.</summary>
    public const string Delivered = "DELIVERED";

    /// <summary>Cancelled (TikTok spelling).</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>
    /// Refunded on TikTok. Owned exclusively by the after-sales sync
    /// (<c>/api/returns/tiktok/page</c>); the order sync never derives it.
    /// Excluded from revenue KPIs like cancelled.
    /// </summary>
    public const string Refund = "REFUND";

    /// <summary>Known raw platform keys (not composite workflow tabs).</summary>
    public static readonly string[] TabKeys =
    [
        OnHold,
        AwaitingShipment,
        AwaitingCollection,
        InTransit,
        Delivered,
        Cancelled,
        Refund
    ];

    /// <summary>Statuses still in the fulfil / SHEIN match pool.</summary>
    public static readonly string[] FulfilCandidateStatuses =
    [
        OnHold,
        AwaitingShipment,
        AwaitingCollection
    ];

    /// <summary>Normalize platform status for storage/compare (trim + upper). Null/blank → null.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().ToUpperInvariant();
        // US spelling alias → TikTok CANCELLED
        if (trimmed is "CANCELED") return Cancelled;
        if (trimmed is "REFUNDED") return Refund;
        // TikTok closes the lifecycle with COMPLETED after delivery. Left unmapped it matches no
        // terminal branch in OrderWorkflowTabs.Resolve, so an order that still carries carrier
        // tracking falls back to AWAITING_COLLECTION_GROUP and shows "Chờ lấy" forever.
        if (trimmed is "COMPLETED") return Delivered;
        return trimmed;
    }

    /// <summary>Whether the status means the order was canceled.</summary>
    public static bool IsCanceled(string? platformOrderStatus)
    {
        var n = Normalize(platformOrderStatus);
        return n is Cancelled or "CANCELED";
    }

    /// <summary>Whether the status is TikTok refund (not Shein supplier refunded).</summary>
    public static bool IsRefund(string? platformOrderStatus)
    {
        var n = Normalize(platformOrderStatus);
        return n is Refund or "REFUNDED";
    }

    /// <summary>
    /// Excluded from revenue / sales KPIs: cancelled or TikTok refund.
    /// </summary>
    public static bool IsNonRevenue(string? platformOrderStatus)
    {
        var n = Normalize(platformOrderStatus);
        return n is Cancelled or "CANCELED" or Refund or "REFUNDED";
    }

    /// <summary>
    /// Cancelled for EF filters during transition: platform cancel, or legacy
    /// <c>OrderStatus</c> cancel when platform status is still empty.
    /// </summary>
    public static bool IsCanceledCommerce(string? platformOrderStatus, string? legacyOrderStatus)
    {
        if (IsCanceled(platformOrderStatus)) return true;
        if (!string.IsNullOrWhiteSpace(platformOrderStatus)) return false;
        return string.Equals(legacyOrderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(legacyOrderStatus, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Non-revenue for KPIs: platform cancel/refund, or legacy cancel when platform empty.
    /// </summary>
    public static bool IsNonRevenueCommerce(string? platformOrderStatus, string? legacyOrderStatus)
    {
        if (IsNonRevenue(platformOrderStatus)) return true;
        if (!string.IsNullOrWhiteSpace(platformOrderStatus)) return false;
        return string.Equals(legacyOrderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(legacyOrderStatus, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the status an order-sync record should persist.
    ///
    /// The order list API keeps reporting a refunded order under a plain commerce status
    /// (<c>shipped</c> / <c>COMPLETED</c>), so taking the record at face value would drag every
    /// order the after-sales sync marked <see cref="Refund"/> back to tab Đang giao on the next
    /// pass. <see cref="Refund"/> is owned by the after-sales sync alone: it is latched here and
    /// only that flow may clear it (return request closed).
    /// </summary>
    /// <param name="rawPlatformStatus">Platform status carried by the 4Seller order record.</param>
    /// <param name="localPlatformStatus">Status currently persisted on the order.</param>
    /// <returns>Resolved platform status to persist; <c>null</c> when neither side carries one.</returns>
    public static string? ResolveCommerceStatus(string? rawPlatformStatus, string? localPlatformStatus)
    {
        var raw = Normalize(rawPlatformStatus);

        // TikTok settling the order as cancelled is a real terminal outcome and outranks the latch.
        if (raw is Cancelled)
        {
            return Cancelled;
        }

        return IsRefund(localPlatformStatus) ? Refund : raw;
    }

    /// <summary>Trim + cap cancel reason text.</summary>
    public static string? NormalizeCancelReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var trimmed = reason.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
