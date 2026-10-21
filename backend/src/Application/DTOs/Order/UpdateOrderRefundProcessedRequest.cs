namespace Application.DTOs.Order;

/// <summary>
/// Marks whether ops finished handling a TikTok REFUND order.
/// Does not call 4Seller; does not touch ledger fees.
/// </summary>
public class UpdateOrderRefundProcessedRequest
{
    /// <summary>
    /// When true, stamps <c>RefundProcessedAt</c> to UTC now if currently null (keeps existing timestamp).
    /// When false, clears <c>RefundProcessedAt</c>.
    /// </summary>
    public bool IsRefundProcessed { get; set; }
}
