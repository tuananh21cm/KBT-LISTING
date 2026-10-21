namespace Application.DTOs.Order;

/// <summary>
/// Marks whether shop payout for an order was received on the linked receiving bank card.
/// Separate from cost/fulfil fee updates.
/// </summary>
public class UpdateOrderSettlementRequest
{
    /// <summary>
    /// When true, upserts a SettlementPaid receiving-bank transaction for the shop's card
    /// using order received amount. When false, deletes that transaction.
    /// </summary>
    public bool IsSettlementPaid { get; set; }
}
