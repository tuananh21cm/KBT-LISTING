namespace Application.DTOs.Order;

/// <summary>
/// Marks whether ThankCard was purchased for an order (warehouse-track / Shein C…/N…).
/// Does not call 4Seller. Optionally updates <c>ThankCardAmount</c> from Settings; does not create ledger rows.
/// </summary>
public class UpdateOrderThankCardProcessedRequest
{
    /// <summary>
    /// When true, stamps <c>ThankCardProcessedAt</c> to UTC now if currently null (keeps existing timestamp).
    /// When false, clears stamp and <c>ThankCardAmount = 0</c>. Also removes any legacy Manual
    /// <c>[BuyTrack]</c>/<c>[ThankCard]</c> ledger rows for the order.
    /// </summary>
    public bool IsThankCardProcessed { get; set; }

    /// <summary>
    /// Default true. When true with <see cref="IsThankCardProcessed"/>, sets <c>ThankCardAmount</c> from
    /// Settings <c>order.handling.thank_card</c>. When false, stamps only (leaves amount unchanged).
    /// Ignored when clearing (<see cref="IsThankCardProcessed"/> false).
    /// </summary>
    public bool ApplyFee { get; set; } = true;
}
