using Domain.Entities;
using Domain.Orders;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Eligibility for the daily "purchased N hours ago, still no last-mile tracking" digest.
/// Matches the admin <see cref="OrderWorkflowTabs.Purchased"/> list (not cancel/refund).
/// </summary>
public static class OrderStaleNoTracking
{
    /// <summary>Parses the hours setting; invalid / out of range → default 72, clamped 1–720.</summary>
    public static int ResolveHours(string? raw)
    {
        if (!int.TryParse(raw, out var hours))
        {
            return TelegramSettingKeys.DefaultStaleNoTrackingHours;
        }

        if (hours < TelegramSettingKeys.MinStaleNoTrackingHours)
        {
            return TelegramSettingKeys.DefaultStaleNoTrackingHours;
        }

        return Math.Min(hours, TelegramSettingKeys.MaxStaleNoTrackingHours);
    }

    /// <summary>
    /// Purchase clock: <see cref="Order.FulfilPurchasedAt"/>, else earliest supplier
    /// <c>OrderedAt</c> / <c>CreatedAt</c>.
    /// </summary>
    public static DateTime? ResolvePurchasedAt(Order order)
    {
        if (order.FulfilPurchasedAt is { } purchased && purchased != default)
        {
            return purchased;
        }

        DateTime? best = null;
        if (order.SupplierPurchases is not { Count: > 0 })
        {
            return null;
        }

        foreach (var purchase in order.SupplierPurchases)
        {
            DateTime? candidate = purchase.OrderedAt;
            if (candidate is null && purchase.CreatedAt != default)
            {
                candidate = purchase.CreatedAt;
            }

            if (candidate is { } at && (best is null || at < best))
            {
                best = at;
            }
        }

        return best;
    }

    /// <summary>
    /// True when the order is on the Purchased workflow tab (bought, not cancel/refund,
    /// no last-mile tracking yet) and purchase age is at least <paramref name="hours"/>.
    /// </summary>
    public static bool IsEligible(Order order, DateTime utcNow, int hours)
    {
        if (OrderWorkflowTabs.Resolve(order) != OrderWorkflowTabs.Purchased)
        {
            return false;
        }

        var purchasedAt = ResolvePurchasedAt(order);
        if (purchasedAt is null)
        {
            return false;
        }

        return utcNow - purchasedAt.Value >= TimeSpan.FromHours(hours);
    }
}
