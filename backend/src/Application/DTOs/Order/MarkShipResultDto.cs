namespace Application.DTOs.Order;

/// <summary>
/// Result of mark-ship after async poll completes.
/// </summary>
public class MarkShipResultDto
{
    /// <summary>Whether mark-ship succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Status message.</summary>
    public string? Message { get; set; }

    /// <summary>Tracking number written.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>4Seller async request id.</summary>
    public string? RequestId { get; set; }

    /// <summary>Updated local platform order status when success.</summary>
    public string? PlatformOrderStatus { get; set; }
}
