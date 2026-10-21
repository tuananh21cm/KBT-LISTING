namespace Application.DTOs.Order;

/// <summary>
/// Updates the local operational notice shown under the order-card status line.
/// Empty or whitespace clears the field (stored as null). Max 500 characters.
/// </summary>
public class UpdateOrderMessageRequest
{
    /// <summary>Operational notice text (local only; not synced to 4Seller).</summary>
    public string? Message { get; set; }
}
