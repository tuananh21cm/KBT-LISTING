namespace Application.DTOs.Order;

/// <summary>
/// Admin request to mark a local order as shipped on 4Seller.
/// </summary>
public class MarkShipRequest
{
    /// <summary>Carrier name from shipping-services list.</summary>
    public string ShippingService { get; set; } = string.Empty;

    /// <summary>Tracking number.</summary>
    public string TrackingNo { get; set; } = string.Empty;
}
