namespace Application.DTOs.FourSeller;

/// <summary>
/// Payload for 4Seller <c>POST /api/v2/order/update-note</c>.
/// </summary>
public class FourSellerUpdateOrderNoteDto
{
    /// <summary>4Seller order ids.</summary>
    public IReadOnlyList<long> OrderIdList { get; set; } = [];

    /// <summary>Buyer memo.</summary>
    public string? BuyerMemo { get; set; }

    /// <summary>Seller memo.</summary>
    public string? SellerMemo { get; set; }

    /// <summary>Internal memo.</summary>
    public string? InternalMemo { get; set; }
}
