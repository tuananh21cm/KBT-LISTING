using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>
/// Materializes the derived workflow columns on <see cref="Order"/> so the list can filter and
/// count with an index scan instead of a correlated <c>EXISTS</c> over <c>SupplierPurchases</c>
/// plus <c>LOWER(...) LIKE '%usps%'</c>.
///
/// This class holds no rules of its own — it only calls the existing sources of truth
/// (<see cref="OrderWorkflowTabs"/>, <see cref="OrderFulfilHandlingSignals"/>) and stores what they
/// return. Every rule change stays in those helpers.
///
/// Single writer: the <c>SaveChangesAsync</c> override in <c>ApplicationDbContext</c> is the only
/// caller in the request path (plus the one-off backfill). Do not set these columns by hand.
/// </summary>
public static class OrderWorkflowSignals
{
    /// <summary>
    /// Recomputes the denormalized columns from the order's own fields and its loaded
    /// <see cref="Order.SupplierPurchases"/>.
    ///
    /// The caller must have the supplier links loaded — an order with links left unloaded looks
    /// like an order with no links and would be stamped with the wrong tab.
    /// </summary>
    /// <returns><c>true</c> when at least one column actually changed.</returns>
    public static bool Apply(Order order) => Apply(order, null);

    /// <summary>
    /// Same as <see cref="Apply(Order)"/> but evaluates against <paramref name="effectiveLinks"/>
    /// instead of the navigation. Used by the <c>SaveChangesAsync</c> writer, where the navigation
    /// still holds links that are about to be deleted or reassigned to another order.
    ///
    /// The navigation is swapped only for the duration of the call and always restored, so the EF
    /// change tracker never observes it.
    /// </summary>
    public static bool Apply(Order order, IReadOnlyCollection<SupplierPurchase>? effectiveLinks)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (effectiveLinks is null)
        {
            return ApplyCore(order);
        }

        var tracked = order.SupplierPurchases;
        try
        {
            order.SupplierPurchases = effectiveLinks as ICollection<SupplierPurchase>
                ?? effectiveLinks.ToList();
            return ApplyCore(order);
        }
        finally
        {
            order.SupplierPurchases = tracked;
        }
    }

    private static bool ApplyCore(Order order)
    {
        var tab = OrderWorkflowTabs.Resolve(order);
        var hasCarrier = OrderWorkflowTabs.HasCarrierTracking(order);
        var hasPurchase = order.SupplierPurchases is { Count: > 0 };
        var hasUsps = OrderFulfilHandlingSignals.HasUspsSignal(order);
        var hasWarehouse = OrderFulfilHandlingSignals.HasWarehouseTrack(order);

        var changed = !string.Equals(order.WorkflowTab, tab, StringComparison.Ordinal)
            || order.HasCarrierTracking != hasCarrier
            || order.HasSupplierPurchase != hasPurchase
            || order.HasUspsSignal != hasUsps
            || order.HasWarehouseSignal != hasWarehouse;

        if (!changed)
        {
            return false;
        }

        order.WorkflowTab = tab;
        order.HasCarrierTracking = hasCarrier;
        order.HasSupplierPurchase = hasPurchase;
        order.HasUspsSignal = hasUsps;
        order.HasWarehouseSignal = hasWarehouse;
        return true;
    }

    /// <summary>
    /// Order columns that feed <see cref="Apply(Order)"/>. A change to any of these (or to the
    /// order's supplier links) invalidates the stamped signals.
    /// </summary>
    public static readonly string[] SourceProperties =
    [
        nameof(Order.PlatformOrderStatus),
        nameof(Order.FulfilPurchasedAt),
        nameof(Order.FulfilAccountId),
        nameof(Order.TrackingNo),
        nameof(Order.ShippingService)
    ];
}
