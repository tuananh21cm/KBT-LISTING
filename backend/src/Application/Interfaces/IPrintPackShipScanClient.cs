namespace Application.Interfaces;

/// <summary>
/// Outbound HTTP client for PrintPackShip USPS track-scan batches.
/// </summary>
public interface IPrintPackShipScanClient
{
    /// <summary>
    /// Step 1 — create a draft tracking batch (no charge).
    /// </summary>
    Task<PrintPackShipBatchCreateResult> CreateTrackingBatchAsync(
        IReadOnlyList<string> trackingNumbers,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 2 — send/confirm a draft batch (charges the account). Not idempotent — do not retry
    /// blindly on Unknown outcomes.
    /// </summary>
    Task<PrintPackShipBatchSendResult> SendBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome class for PrintPackShip HTTP calls.</summary>
public enum PrintPackShipCallKind
{
    /// <summary>HTTP 2xx and body parsed with success flag interpretable.</summary>
    Ok,

    /// <summary>Clear failure: 4xx or success:false — safe to retry after fix.</summary>
    Failed,

    /// <summary>
    /// Ambiguous: timeout, 5xx, cancel, unparseable body — may already have charged on send.
    /// </summary>
    Unknown,
}

/// <summary>Result of POST /batches/tracking.</summary>
public sealed class PrintPackShipBatchCreateResult
{
    /// <summary>Call classification.</summary>
    public PrintPackShipCallKind Kind { get; init; }

    /// <summary>API success flag when parsed.</summary>
    public bool Success { get; init; }

    /// <summary>API message.</summary>
    public string? Message { get; init; }

    /// <summary>External batch id from create response (authoritative for send URL).</summary>
    public long? BatchId { get; init; }

    /// <summary>Remote status string (e.g. draft).</summary>
    public string? Status { get; init; }

    /// <summary>Remote processing_status.</summary>
    public string? ProcessingStatus { get; init; }

    /// <summary>Tracking count reported by API.</summary>
    public int? TrackingCount { get; init; }

    /// <summary>Raw response body (for audit).</summary>
    public string? RawJson { get; init; }

    /// <summary>HTTP status when available.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Local error detail (timeout, parse, …).</summary>
    public string? Error { get; init; }
}

/// <summary>Result of POST /batches/{id}/send.</summary>
public sealed class PrintPackShipBatchSendResult
{
    /// <summary>Call classification.</summary>
    public PrintPackShipCallKind Kind { get; init; }

    /// <summary>API success flag when parsed.</summary>
    public bool Success { get; init; }

    /// <summary>API message.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Batch id from send response — may differ from the create id; do not trust for identity.
    /// </summary>
    public long? ResponseBatchId { get; init; }

    /// <summary>Remote status (e.g. scanning).</summary>
    public string? Status { get; init; }

    /// <summary>Pages.total.</summary>
    public int? PagesTotal { get; init; }

    /// <summary>Pages.success.</summary>
    public int? PagesSuccess { get; init; }

    /// <summary>Pages.failed.</summary>
    public int? PagesFailed { get; init; }

    /// <summary>Pricing.price_per_label.</summary>
    public decimal? PricePerLabel { get; init; }

    /// <summary>Pricing.scan_fee.</summary>
    public decimal? ScanFee { get; init; }

    /// <summary>Pricing.paid_pages.</summary>
    public int? PaidPages { get; init; }

    /// <summary>Balance.previous.</summary>
    public decimal? BalancePrevious { get; init; }

    /// <summary>Balance.current.</summary>
    public decimal? BalanceCurrent { get; init; }

    /// <summary>Raw response body (for audit).</summary>
    public string? RawJson { get; init; }

    /// <summary>HTTP status when available.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Local error detail.</summary>
    public string? Error { get; init; }
}
