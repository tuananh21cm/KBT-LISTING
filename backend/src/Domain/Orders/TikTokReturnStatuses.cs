namespace Domain.Orders;

/// <summary>What an after-sales sync run concluded about a return / refund case.</summary>
public enum AfterSalesOutcome
{
    /// <summary>Case is still running (buyer returning the item, seller yet to refund).</summary>
    Open = 0,

    /// <summary>Money went back to the buyer — the order stops counting as revenue.</summary>
    Refunded = 1,

    /// <summary>Case ended without a refund, so the order keeps its commerce status.</summary>
    Closed = 2
}

/// <summary>
/// TikTok return state machine as 4Seller relays it on <c>/api/returns/tiktok/page</c>.
/// </summary>
public static class TikTokReturnStatuses
{
    /// <summary>Buyer must ship the item back.</summary>
    public const string AwaitingBuyerShip = "AWAITING_BUYER_SHIP";

    /// <summary>Buyer shipped it back; seller has to issue the refund.</summary>
    public const string BuyerShippedItem = "BUYER_SHIPPED_ITEM";

    /// <summary>Refund issued — terminal.</summary>
    public const string RequestComplete = "RETURN_OR_REFUND_REQUEST_COMPLETE";

    /// <summary>Request closed without a refund (buyer withdrew, platform rejected) — terminal.</summary>
    public const string RequestCancel = "RETURN_OR_REFUND_REQUEST_CANCEL";

    /// <summary>
    /// Classifies a case. Anything unrecognised counts as <see cref="AfterSalesOutcome.Open"/>: a
    /// status TikTok adds later must not silently refund an order or release one, and an open case
    /// only writes the reason.
    /// </summary>
    public static AfterSalesOutcome Classify(string? platformReturnStatus)
    {
        if (string.IsNullOrWhiteSpace(platformReturnStatus))
        {
            return AfterSalesOutcome.Open;
        }

        return platformReturnStatus.Trim().ToUpperInvariant() switch
        {
            RequestComplete => AfterSalesOutcome.Refunded,
            RequestCancel => AfterSalesOutcome.Closed,
            _ => AfterSalesOutcome.Open
        };
    }
}
