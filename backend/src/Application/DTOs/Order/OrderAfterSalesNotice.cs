using Domain.Orders;

namespace Application.DTOs.Order;

/// <summary>
/// An order whose return / refund state changed on the current after-sales sync run, together with
/// the return-case context. The context is carried alongside instead of persisted: only the outcome
/// (REFUND status, refund clock, reason) belongs on the order, the rest is detail ops needs once,
/// in the message.
/// </summary>
public class OrderAfterSalesNotice
{
    /// <summary>Local order, with <c>Items</c> loaded.</summary>
    public required Domain.Entities.Order Order { get; init; }

    /// <summary>Shop display name when <see cref="Domain.Entities.Order.ShopId"/> is unmatched.</summary>
    public string? ShopNameFallback { get; init; }

    /// <summary>What this run concluded about the case.</summary>
    public required AfterSalesOutcome Outcome { get; init; }

    /// <summary>Human-readable return status from 4Seller ("Refund complete", "Request closed", …).</summary>
    public string? ReturnStatusText { get; init; }

    /// <summary>Buyer-supplied return reason.</summary>
    public string? ReturnReason { get; init; }

    /// <summary><c>RETURN_AND_REFUND</c> / <c>REFUND</c>.</summary>
    public string? ReturnType { get; init; }

    /// <summary>Refund amount on the case.</summary>
    public decimal RefundTotal { get; init; }

    /// <summary>Refund currency.</summary>
    public string? Currency { get; init; }

    /// <summary>Return shipment tracking number when the buyer shipped the item back.</summary>
    public string? ReturnTrackingNumber { get; init; }
}
