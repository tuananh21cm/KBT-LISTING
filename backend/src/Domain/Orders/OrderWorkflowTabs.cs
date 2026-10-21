using System.Text.RegularExpressions;
using Domain.Entities;

namespace Domain.Orders;

/// <summary>
/// Composite admin workflow tabs (suy diễn). Raw TikTok status stays on
/// <see cref="Order.PlatformOrderStatus"/>; tabs are derived for list filters/counts.
/// </summary>
public static class OrderWorkflowTabs
{
    /// <summary>ON_HOLD + AWAITING_SHIPMENT, not yet purchased.</summary>
    public const string PendingProcess = "PENDING_PROCESS";

    /// <summary>Purchased on Shein, waiting for carrier track.</summary>
    public const string Purchased = "PURCHASED";

    /// <summary>
    /// Chờ lấy: sàn <c>AWAITING_COLLECTION</c>, hoặc đã có mã carrier mà sàn chưa
    /// <c>IN_TRANSIT</c> (Mark Ship chưa lên / đang chờ carrier lấy).
    /// </summary>
    public const string AwaitingCollectionGroup = "AWAITING_COLLECTION_GROUP";

    /// <summary>Đang giao: sàn TikTok <c>IN_TRANSIT</c>.</summary>
    public const string InTransit = "IN_TRANSIT";

    /// <summary>
    /// Filter-only union of <see cref="AwaitingCollectionGroup"/> + <see cref="InTransit"/>.
    /// Never written by <see cref="Resolve"/> — kept so deep-links / bookmarks still match both tabs.
    /// </summary>
    public const string InTransitGroup = "IN_TRANSIT_GROUP";

    /// <summary>TikTok DELIVERED only.</summary>
    public const string Delivered = "DELIVERED";

    /// <summary>TikTok CANCELLED.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>TikTok REFUND (cancelReason on commerce status).</summary>
    public const string Refund = "REFUND";

    /// <summary>
    /// Filter-only: USPS last-mile present and <c>UspsProcessedAt</c> still null.
    /// Not part of <see cref="Resolve"/> mutual exclusion.
    /// </summary>
    public const string UspsUnprocessed = "USPS_UNPROCESSED";

    /// <summary>Filter-only: USPS last-mile present and stamped (<c>UspsProcessedAt</c> set).</summary>
    public const string UspsProcessed = "USPS_PROCESSED";

    /// <summary>
    /// Filter-only: warehouse Thank Card still needed — tín hiệu kho, chưa stamp,
    /// chưa last-mile carrier / mark ship (<c>AWAITING_COLLECTION</c>+).
    /// </summary>
    public const string ThankCardPending = "THANK_CARD_PENDING";

    /// <summary>Filter-only: Thank Card stamped (<c>ThankCardProcessedAt</c> set).</summary>
    public const string ThankCardProcessed = "THANK_CARD_PROCESSED";

    /// <summary>
    /// Filter-only: TikTok REFUND and <c>RefundProcessedAt</c> still null.
    /// Not part of <see cref="Resolve"/> mutual exclusion.
    /// </summary>
    public const string RefundUnprocessed = "REFUND_UNPROCESSED";

    /// <summary>Filter-only: TikTok REFUND and stamped (<c>RefundProcessedAt</c> set).</summary>
    public const string RefundProcessed = "REFUND_PROCESSED";

    /// <summary>
    /// Filter-only: TikTok CANCELLED and <c>RefundProcessedAt</c> still null
    /// (cùng stamp ops với Refund trên sider Xử lý hoàn/hủy).
    /// </summary>
    public const string CancelUnprocessed = "CANCEL_UNPROCESSED";

    /// <summary>Filter-only: TikTok CANCELLED and stamped (<c>RefundProcessedAt</c> set).</summary>
    public const string CancelProcessed = "CANCEL_PROCESSED";

    /// <summary>
    /// Filter-only: REFUND hoặc CANCELLED (mọi stamp ops) — tab Tất cả sider hoàn/hủy.
    /// </summary>
    public const string RefundQueueAll = "REFUND_QUEUE_ALL";

    /// <summary>
    /// Filter-only: REFUND hoặc CANCELLED, <c>RefundProcessedAt</c> còn null
    /// (legacy handling tabs).
    /// </summary>
    public const string RefundQueueUnprocessed = "REFUND_QUEUE_UNPROCESSED";

    /// <summary>
    /// Filter-only: REFUND hoặc CANCELLED, đã stamp <c>RefundProcessedAt</c>.
    /// </summary>
    public const string RefundQueueProcessed = "REFUND_QUEUE_PROCESSED";

    /// <summary>
    /// Filter-only (legacy combined): USPS and/or Thank Card queue — còn việc.
    /// Prefer <see cref="UspsUnprocessed"/> / <see cref="ThankCardPending"/>.
    /// </summary>
    public const string UspsThankCardPending = "USPS_THANK_CARD_PENDING";

    /// <summary>
    /// Filter-only (legacy combined): USPS and/or Thank Card queue — đã xử lý đủ.
    /// Prefer <see cref="UspsProcessed"/> / <see cref="ThankCardProcessed"/>.
    /// </summary>
    public const string UspsThankCardProcessed = "USPS_THANK_CARD_PROCESSED";

    private static readonly Regex SheinWarehouseTrackingRegex =
        new(@"^[CN]\d{10,}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Resolves the single workflow tab for an order (mutual exclusion, priority top-down).
    /// </summary>
    public static string Resolve(Order order)
    {
        var platform = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
        if (platform == PlatformOrderStatuses.Refund)
        {
            return Refund;
        }

        if (platform == PlatformOrderStatuses.Cancelled)
        {
            return Cancelled;
        }

        if (platform == PlatformOrderStatuses.Delivered)
        {
            return Delivered;
        }

        // Sàn đã xác nhận đang giao — trước cả carrier-only "chờ lấy".
        if (platform == PlatformOrderStatuses.InTransit)
        {
            return InTransit;
        }

        var hasCarrier = HasCarrierTracking(order);
        if (hasCarrier || platform == PlatformOrderStatuses.AwaitingCollection)
        {
            return AwaitingCollectionGroup;
        }

        if (IsPurchased(order))
        {
            return Purchased;
        }

        if (platform is PlatformOrderStatuses.OnHold or PlatformOrderStatuses.AwaitingShipment
            || string.IsNullOrEmpty(platform))
        {
            return PendingProcess;
        }

        // Fallback: unknown/open statuses without purchase → chờ xử lý
        return PendingProcess;
    }

    /// <summary>Whether the order has been purchased on supplier (Shein link or fulfil stamp).</summary>
    public static bool IsPurchased(Order order) =>
        order.FulfilPurchasedAt.HasValue
        || order.FulfilAccountId.HasValue
        || (order.SupplierPurchases is { Count: > 0 });

    /// <summary>
    /// Carrier (last-mile) tracking on order or any supplier link.
    /// Warehouse codes C…/N… do not count.
    /// </summary>
    public static bool HasCarrierTracking(Order order)
    {
        if (order.SupplierPurchases is { Count: > 0 })
        {
            foreach (var link in order.SupplierPurchases)
            {
                if (string.Equals(link.TrackingKind, SupplierTrackingKinds.Carrier, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(link.TrackingNo))
                {
                    return true;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(order.TrackingNo))
        {
            return false;
        }

        var t = order.TrackingNo.Trim();
        if (SheinWarehouseTrackingRegex.IsMatch(t))
        {
            return false;
        }

        var service = order.ShippingService ?? string.Empty;
        if (service.Contains("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>True when <paramref name="tab"/> is a known composite / terminal workflow key.</summary>
    public static bool IsKnownTab(string? tab)
    {
        var t = tab?.Trim().ToUpperInvariant();
        return t is PendingProcess
            or Purchased
            or AwaitingCollectionGroup
            or InTransit
            or InTransitGroup
            or Delivered
            or Cancelled
            or Refund;
    }
}
