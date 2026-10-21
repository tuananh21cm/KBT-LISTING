namespace Application.DTOs.Order;

/// <summary>
/// Marks whether USPS last-mile extra handling for an order was completed.
/// Does not call 4Seller. Optionally updates <c>BuyTrackAmount</c> from Settings; does not create ledger rows
/// (fees show as columns on CostUpdate rows via Order amounts).
/// </summary>
public class UpdateOrderUspsProcessedRequest
{
    /// <summary>
    /// When true, stamps <c>UspsProcessedAt</c> to UTC now if currently null (keeps existing timestamp).
    /// When false, clears stamp and <c>BuyTrackAmount = 0</c>. Also removes any legacy Manual
    /// <c>[BuyTrack]</c>/<c>[ThankCard]</c> ledger rows for the order.
    /// </summary>
    public bool IsUspsProcessed { get; set; }

    /// <summary>
    /// Default true. When true with <see cref="IsUspsProcessed"/>, sets <c>BuyTrackAmount</c> from
    /// Settings <c>order.handling.buy_track</c>. When false, stamps only (leaves amount unchanged).
    /// Ignored when clearing (<see cref="IsUspsProcessed"/> false).
    /// </summary>
    public bool ApplyFee { get; set; } = true;
}
