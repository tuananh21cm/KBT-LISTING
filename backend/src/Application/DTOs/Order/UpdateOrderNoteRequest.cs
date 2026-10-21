namespace Application.DTOs.Order;

/// <summary>
/// Updates buyer / seller / internal notes on 4Seller and local DB.
/// Empty or whitespace clears the corresponding field (stored as null).
/// </summary>
public class UpdateOrderNoteRequest
{
    /// <summary>Buyer-facing note (4Seller buyerMemo).</summary>
    public string? BuyerMemo { get; set; }

    /// <summary>Seller note (4Seller sellerMemo).</summary>
    public string? SellerMemo { get; set; }

    /// <summary>Internal note (4Seller internalMemo).</summary>
    public string? InternalMemo { get; set; }
}
