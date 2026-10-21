using System.Linq.Expressions;
using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>
/// EF-translatable filters for composite admin workflow tabs.
///
/// These read the denormalized columns on <c>Orders</c> (<see cref="Order.WorkflowTab"/> and the
/// <c>Has…</c> signal flags) instead of re-deriving the tab in SQL, which turns each tab filter and
/// each badge count from a correlated <c>EXISTS</c> over <c>SupplierPurchases</c> plus
/// <c>LOWER(...) LIKE '%usps%'</c> into an index scan.
///
/// The columns are written in one place — see <see cref="OrderWorkflowSignals"/> — and guarded
/// against drift by <see cref="Interfaces.IOrderWorkflowSignalsVerifier"/>. That also removes an old
/// discrepancy: the previous SQL treated any 11+ character code starting with C/N as a warehouse
/// code, while the C# rules require the rest to be digits, so a tab shown on a row could disagree
/// with the tab that row was filtered into.
///
/// Every tab is exposed as an <see cref="Expression"/> so the list filter and its badge count in
/// <c>GetStatusCountsAsync</c> read from the same definition and cannot drift apart.
/// </summary>
public static class OrderWorkflowTabFilter
{
    internal static readonly Expression<Func<Order, bool>> IsRefund = o =>
        o.WorkflowTab == OrderWorkflowTabs.Refund;

    internal static readonly Expression<Func<Order, bool>> IsCancelled = o =>
        o.WorkflowTab == OrderWorkflowTabs.Cancelled;

    internal static readonly Expression<Func<Order, bool>> IsDelivered = o =>
        o.WorkflowTab == OrderWorkflowTabs.Delivered;

    internal static readonly Expression<Func<Order, bool>> IsAwaitingCollectionGroup = o =>
        o.WorkflowTab == OrderWorkflowTabs.AwaitingCollectionGroup;

    internal static readonly Expression<Func<Order, bool>> IsInTransit = o =>
        o.WorkflowTab == OrderWorkflowTabs.InTransit;

    /// <summary>
    /// Filter-only union: Chờ lấy + Đang giao (legacy bookmark <c>IN_TRANSIT_GROUP</c>).
    /// </summary>
    internal static readonly Expression<Func<Order, bool>> IsInTransitGroup = o =>
        o.WorkflowTab == OrderWorkflowTabs.AwaitingCollectionGroup
        || o.WorkflowTab == OrderWorkflowTabs.InTransit;

    internal static readonly Expression<Func<Order, bool>> IsPurchased = o =>
        o.WorkflowTab == OrderWorkflowTabs.Purchased;

    internal static readonly Expression<Func<Order, bool>> IsPendingProcess = o =>
        o.WorkflowTab == OrderWorkflowTabs.PendingProcess;

    /// <summary>
    /// USPS last-mile còn việc: chưa stamp, có tín hiệu USPS, không refund/cancel,
    /// chưa Đang giao / Đã giao (clock / platform / workflow). Giữ «Chờ lấy».
    /// </summary>
    internal static readonly Expression<Func<Order, bool>> IsUspsUnprocessed = o =>
        o.UspsProcessedAt == null
        && o.HasUspsSignal
        && o.PlatformOrderStatus != PlatformOrderStatuses.Refund
        && o.PlatformOrderStatus != PlatformOrderStatuses.Cancelled
        && o.WorkflowTab != OrderWorkflowTabs.Refund
        && o.WorkflowTab != OrderWorkflowTabs.Cancelled
        && o.InTransitAt == null
        && o.DeliveredAt == null
        && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
        && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered
        && o.WorkflowTab != OrderWorkflowTabs.InTransit
        && o.WorkflowTab != OrderWorkflowTabs.Delivered;

    internal static readonly Expression<Func<Order, bool>> IsUspsProcessed = o =>
        o.UspsProcessedAt != null && o.HasUspsSignal;

    /// <summary>
    /// Thank Card còn việc: có tín hiệu kho + chưa stamp + chưa có track carrier / mark ship.
    /// Đơn đã add track thật (last-mile) không cần mua Thank Card nữa dù còn WarehouseTrackingNo.
    ///
    /// Trạng thái sàn phải so trực tiếp thay vì qua <see cref="Order.WorkflowTab"/>: đơn
    /// AWAITING_COLLECTION đã hoàn/huỷ mang tab REFUND/CANCELLED nhưng vẫn phải bị loại ở đây.
    /// </summary>
    internal static readonly Expression<Func<Order, bool>> IsThankCardPending = o =>
        o.ThankCardProcessedAt == null
        && o.HasWarehouseSignal
        && !o.HasCarrierTracking
        && o.PlatformOrderStatus != PlatformOrderStatuses.AwaitingCollection
        && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
        && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered;

    internal static readonly Expression<Func<Order, bool>> IsThankCardProcessed = o =>
        o.ThankCardProcessedAt != null;

    internal static readonly Expression<Func<Order, bool>> IsRefundUnprocessed = o =>
        o.RefundProcessedAt == null && o.WorkflowTab == OrderWorkflowTabs.Refund;

    internal static readonly Expression<Func<Order, bool>> IsRefundProcessed = o =>
        o.RefundProcessedAt != null && o.WorkflowTab == OrderWorkflowTabs.Refund;

    internal static readonly Expression<Func<Order, bool>> IsCancelUnprocessed = o =>
        o.RefundProcessedAt == null && o.WorkflowTab == OrderWorkflowTabs.Cancelled;

    internal static readonly Expression<Func<Order, bool>> IsCancelProcessed = o =>
        o.RefundProcessedAt != null && o.WorkflowTab == OrderWorkflowTabs.Cancelled;

    internal static readonly Expression<Func<Order, bool>> IsRefundQueueAll = o =>
        o.WorkflowTab == OrderWorkflowTabs.Refund
        || o.WorkflowTab == OrderWorkflowTabs.Cancelled;

    internal static readonly Expression<Func<Order, bool>> IsRefundQueueUnprocessed = o =>
        o.RefundProcessedAt == null
        && (o.WorkflowTab == OrderWorkflowTabs.Refund
            || o.WorkflowTab == OrderWorkflowTabs.Cancelled);

    internal static readonly Expression<Func<Order, bool>> IsRefundQueueProcessed = o =>
        o.RefundProcessedAt != null
        && (o.WorkflowTab == OrderWorkflowTabs.Refund
            || o.WorkflowTab == OrderWorkflowTabs.Cancelled);

    /// <summary>Legacy combined pending (USPS hoặc Thank Card còn việc).</summary>
    internal static readonly Expression<Func<Order, bool>> IsUspsThankCardPending = o =>
        (o.UspsProcessedAt == null
            && o.HasUspsSignal
            && o.PlatformOrderStatus != PlatformOrderStatuses.Refund
            && o.PlatformOrderStatus != PlatformOrderStatuses.Cancelled
            && o.WorkflowTab != OrderWorkflowTabs.Refund
            && o.WorkflowTab != OrderWorkflowTabs.Cancelled
            && o.InTransitAt == null
            && o.DeliveredAt == null
            && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
            && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered
            && o.WorkflowTab != OrderWorkflowTabs.InTransit
            && o.WorkflowTab != OrderWorkflowTabs.Delivered)
        || (o.ThankCardProcessedAt == null
            && o.HasWarehouseSignal
            && !o.HasCarrierTracking
            && o.PlatformOrderStatus != PlatformOrderStatuses.AwaitingCollection
            && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
            && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered);

    /// <summary>Legacy combined processed: từng thuộc hàng đợi và giờ không còn việc nào.</summary>
    internal static readonly Expression<Func<Order, bool>> IsUspsThankCardProcessed = o =>
        (
            o.HasUspsSignal
            || o.ThankCardProcessedAt != null
            || (o.HasWarehouseSignal
                && !o.HasCarrierTracking
                && o.PlatformOrderStatus != PlatformOrderStatuses.AwaitingCollection
                && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
                && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered)
        )
        && !(
            (o.UspsProcessedAt == null
                && o.HasUspsSignal
                && o.PlatformOrderStatus != PlatformOrderStatuses.Refund
                && o.PlatformOrderStatus != PlatformOrderStatuses.Cancelled
                && o.WorkflowTab != OrderWorkflowTabs.Refund
                && o.WorkflowTab != OrderWorkflowTabs.Cancelled
                && o.InTransitAt == null
                && o.DeliveredAt == null
                && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
                && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered
                && o.WorkflowTab != OrderWorkflowTabs.InTransit
                && o.WorkflowTab != OrderWorkflowTabs.Delivered)
            || (o.ThankCardProcessedAt == null
                && o.HasWarehouseSignal
                && !o.HasCarrierTracking
                && o.PlatformOrderStatus != PlatformOrderStatuses.AwaitingCollection
                && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
                && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered)
        );

    /// <summary>
    /// Resolves the EF predicate for a workflow tab, or <c>null</c> when the tab is blank.
    /// Unknown keys fall back to a raw <see cref="Order.PlatformOrderStatus"/> match.
    /// </summary>
    public static Expression<Func<Order, bool>>? PredicateFor(string? statusTab)
    {
        if (string.IsNullOrWhiteSpace(statusTab))
        {
            return null;
        }

        var tab = statusTab.Trim().ToUpperInvariant();
        return tab switch
        {
            OrderWorkflowTabs.Refund => IsRefund,
            OrderWorkflowTabs.Cancelled => IsCancelled,
            OrderWorkflowTabs.Delivered => IsDelivered,
            OrderWorkflowTabs.AwaitingCollectionGroup => IsAwaitingCollectionGroup,
            OrderWorkflowTabs.InTransit => IsInTransit,
            OrderWorkflowTabs.InTransitGroup => IsInTransitGroup,
            OrderWorkflowTabs.Purchased => IsPurchased,
            OrderWorkflowTabs.PendingProcess => IsPendingProcess,

            OrderWorkflowTabs.UspsUnprocessed => IsUspsUnprocessed,
            OrderWorkflowTabs.UspsProcessed => IsUspsProcessed,
            OrderWorkflowTabs.ThankCardPending => IsThankCardPending,
            OrderWorkflowTabs.ThankCardProcessed => IsThankCardProcessed,
            OrderWorkflowTabs.RefundUnprocessed => IsRefundUnprocessed,
            OrderWorkflowTabs.RefundProcessed => IsRefundProcessed,
            OrderWorkflowTabs.CancelUnprocessed => IsCancelUnprocessed,
            OrderWorkflowTabs.CancelProcessed => IsCancelProcessed,
            OrderWorkflowTabs.RefundQueueAll => IsRefundQueueAll,
            OrderWorkflowTabs.RefundQueueUnprocessed => IsRefundQueueUnprocessed,
            OrderWorkflowTabs.RefundQueueProcessed => IsRefundQueueProcessed,
            OrderWorkflowTabs.UspsThankCardPending => IsUspsThankCardPending,
            OrderWorkflowTabs.UspsThankCardProcessed => IsUspsThankCardProcessed,

            PlatformOrderStatuses.OnHold => o => o.PlatformOrderStatus == PlatformOrderStatuses.OnHold,
            PlatformOrderStatuses.AwaitingShipment => o => o.PlatformOrderStatus == PlatformOrderStatuses.AwaitingShipment,
            // Raw AWAITING_COLLECTION still matches platform column (key differs from AWAITING_COLLECTION_GROUP).
            // Raw IN_TRANSIT is intentionally omitted — same string as OrderWorkflowTabs.InTransit (tab).
            PlatformOrderStatuses.AwaitingCollection => o => o.PlatformOrderStatus == PlatformOrderStatuses.AwaitingCollection,

            _ => o => o.PlatformOrderStatus == tab
        };
    }

    /// <summary>Applies a workflow tab filter (no-op when tab blank).</summary>
    public static IQueryable<Order> WhereWorkflowTab(IQueryable<Order> q, string? statusTab)
    {
        var predicate = PredicateFor(statusTab);
        return predicate is null ? q : q.Where(predicate);
    }
}
