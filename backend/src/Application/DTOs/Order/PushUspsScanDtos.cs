namespace Application.DTOs.Order;

/// <summary>Request body for bulk USPS track-scan push to PrintPackShip.</summary>
public class PushUspsScanRequest
{
    /// <summary>Order ids selected on the USPS list (one page).</summary>
    public List<Guid> OrderIds { get; set; } = [];

    /// <summary>Optional notes forwarded to PrintPackShip create batch.</summary>
    public string? Notes { get; set; }
}

/// <summary>One order skipped during eligibility filtering.</summary>
public class UspsScanSkippedOrderDto
{
    /// <summary>Order id (empty Guid when not found).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Platform order id when known.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Tracking number when known.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Stable skip code from <c>UspsScanSkipReasons</c>.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Result of a USPS scan push attempt.</summary>
public class UspsScanPushResultDto
{
    /// <summary>PrintPackShip external batch id (create step) when a batch was created.</summary>
    public long? BatchId { get; set; }

    /// <summary>
    /// Local batch status: Draft / Sent / SendFailed / SendUnknown, or empty when nothing sent.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Orders stamped after a successful send.</summary>
    public int SentCount { get; set; }

    /// <summary>price_per_label from send response.</summary>
    public decimal? PricePerLabel { get; set; }

    /// <summary>scan_fee from send response.</summary>
    public decimal? ScanFee { get; set; }

    /// <summary>Estimated total charged (price_per_label × paid_pages, or scan_fee when present).</summary>
    public decimal? TotalCharged { get; set; }

    /// <summary>Balance.current after send.</summary>
    public decimal? BalanceCurrent { get; set; }

    /// <summary>Pages.failed from send response.</summary>
    public int PagesFailed { get; set; }

    /// <summary>Orders excluded before calling the API.</summary>
    public List<UspsScanSkippedOrderDto> Skipped { get; set; } = [];

    /// <summary>Error message for SendFailed / SendUnknown / create failure.</summary>
    public string? Error { get; set; }
}

/// <summary>Query for listing USPS scan batches (admin).</summary>
public class UspsScanBatchListQuery
{
    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (capped).</summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>Paged list of USPS scan batches.</summary>
public class UspsScanBatchListPageDto
{
    /// <summary>Rows.</summary>
    public List<UspsScanBatchDto> Items { get; set; } = [];

    /// <summary>Total rows.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }
}

/// <summary>USPS scan batch summary for admin list.</summary>
public class UspsScanBatchDto
{
    /// <summary>Local row id.</summary>
    public Guid Id { get; set; }

    /// <summary>PrintPackShip batch id.</summary>
    public long ExternalBatchId { get; set; }

    /// <summary>Draft / Sent / SendFailed / SendUnknown.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Tracking count.</summary>
    public int TrackingCount { get; set; }

    /// <summary>price_per_label.</summary>
    public decimal? PricePerLabel { get; set; }

    /// <summary>scan_fee.</summary>
    public decimal? ScanFee { get; set; }

    /// <summary>paid_pages.</summary>
    public int? PaidPages { get; set; }

    /// <summary>pages.failed.</summary>
    public int? PagesFailed { get; set; }

    /// <summary>Balance after send.</summary>
    public decimal? BalanceCurrent { get; set; }

    /// <summary>Creator user id.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creator display name.</summary>
    public string? CreatedByName { get; set; }

    /// <summary>When created (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When sent (UTC).</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Error / warning.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Items (order + tracking).</summary>
    public List<UspsScanBatchItemDto> Items { get; set; } = [];
}

/// <summary>One item inside a batch list row.</summary>
public class UspsScanBatchItemDto
{
    /// <summary>Order id when still present.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Platform order id.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Tracking number.</summary>
    public string TrackingNo { get; set; } = string.Empty;
}
