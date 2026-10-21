namespace Application.DTOs.FourSeller;

/// <summary>
/// Parsed result from 4Seller <c>get-async-ship-order-result</c>.
/// </summary>
public class FourSellerAsyncShipOrderResultDto
{
    /// <summary>Pending | Success | Failed.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Human-readable message from 4Seller when available.</summary>
    public string? Message { get; set; }
}
