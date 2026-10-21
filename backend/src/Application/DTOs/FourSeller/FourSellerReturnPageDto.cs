namespace Application.DTOs.FourSeller;

/// <summary>
/// One page of TikTok return / refund cases from 4Seller (<c>/api/returns/tiktok/page</c>).
/// </summary>
public class FourSellerReturnPageDto
{
    /// <summary>Return records on this page.</summary>
    public List<FourSellerReturnRecordDto> Records { get; set; } = [];

    /// <summary>Total matching records.</summary>
    public int Total { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }
}

/// <summary>
/// One TikTok return / refund case.
///
/// Only the fields that decide the local order state are mapped: buyer, product list and images
/// already live on the order, and re-reading them here would just add payload for no new fact.
/// </summary>
public class FourSellerReturnRecordDto
{
    /// <summary>4Seller internal id of the return row (tie-breaker when two cases share a timestamp).</summary>
    public long Id { get; set; }

    /// <summary>TikTok return case id.</summary>
    public string? ReturnId { get; set; }

    /// <summary>TikTok order id this case belongs to.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>4Seller internal order id (<c>Order.FourSellerOrderId</c>).</summary>
    public long ErpOrderId { get; set; }

    /// <summary>4Seller shop id.</summary>
    public long? ShopId { get; set; }

    /// <summary><c>RETURN_AND_REFUND</c> / <c>REFUND</c>.</summary>
    public string? ReturnType { get; set; }

    /// <summary>Commerce status of the order as TikTok reports it on the case (Delivered, Completed, …).</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>
    /// TikTok return state machine: <c>AWAITING_BUYER_SHIP</c>, <c>BUYER_SHIPPED_ITEM</c>,
    /// <c>RETURN_OR_REFUND_REQUEST_COMPLETE</c>, <c>RETURN_OR_REFUND_REQUEST_CANCEL</c>.
    /// </summary>
    public string? PlatformReturnStatus { get; set; }

    /// <summary>Human-readable return status ("Refund complete", "Request closed", …).</summary>
    public string? ReturnStatus { get; set; }

    /// <summary>4Seller case status (<c>completed</c>, <c>awaiting_return_package_receipt</c>, <c>disputed</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Settlement status (<c>settled</c> / <c>to_settled</c>).</summary>
    public string? SettlementStatus { get; set; }

    /// <summary>Buyer-supplied reason.</summary>
    public string? ReturnReason { get; set; }

    /// <summary>Return shipment tracking number when the buyer has shipped the item back.</summary>
    public string? ReturnTrackingNumber { get; set; }

    /// <summary>Refund amount.</summary>
    public decimal RefundTotal { get; set; }

    /// <summary>Refund currency.</summary>
    public string? Currency { get; set; }

    /// <summary>When the case was created on the platform (UTC).</summary>
    public DateTime? PlatformCreateTime { get; set; }

    /// <summary>When the case last changed on the platform (UTC) — the pull cursor.</summary>
    public DateTime? PlatformUpdateTime { get; set; }
}
