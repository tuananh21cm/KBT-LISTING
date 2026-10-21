using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Local audit row for a PrintPackShip USPS track-scan batch (create → send).
/// </summary>
public class UspsScanBatch : BaseEntity
{
    /// <summary>Batch created remotely; send not yet attempted or in progress locally.</summary>
    public const string StatusDraft = "Draft";

    /// <summary>Send succeeded (success:true); orders stamped.</summary>
    public const string StatusSent = "Sent";

    /// <summary>Clear send failure — safe to push again.</summary>
    public const string StatusSendFailed = "SendFailed";

    /// <summary>
    /// Ambiguous send outcome (timeout/5xx) — do not re-push those tracking numbers
    /// until an admin resolves the remote batch.
    /// </summary>
    public const string StatusSendUnknown = "SendUnknown";

    /// <summary>Row id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// PrintPackShip batch id from the create step — authoritative identity for send URL
    /// and for <see cref="Order.UspsScanBatchId"/>.
    /// </summary>
    public long ExternalBatchId { get; set; }

    /// <summary><see cref="StatusDraft"/> / <see cref="StatusSent"/> / …</summary>
    public string Status { get; set; } = StatusDraft;

    /// <summary>Number of tracking numbers submitted.</summary>
    public int TrackingCount { get; set; }

    /// <summary>Pricing.price_per_label from send response.</summary>
    public decimal? PricePerLabel { get; set; }

    /// <summary>Pricing.scan_fee from send response.</summary>
    public decimal? ScanFee { get; set; }

    /// <summary>Pricing.paid_pages from send response.</summary>
    public int? PaidPages { get; set; }

    /// <summary>Pages.total.</summary>
    public int? PagesTotal { get; set; }

    /// <summary>Pages.success.</summary>
    public int? PagesSuccess { get; set; }

    /// <summary>Pages.failed.</summary>
    public int? PagesFailed { get; set; }

    /// <summary>Balance.previous.</summary>
    public decimal? BalancePrevious { get; set; }

    /// <summary>Balance.current.</summary>
    public decimal? BalanceCurrent { get; set; }

    /// <summary>Optional notes sent with create.</summary>
    public string? Notes { get; set; }

    /// <summary>Who triggered the push.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When send succeeded (UTC).</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Error / warning text (failed, unknown, or partial pages).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Raw create JSON.</summary>
    public string? RawCreateResponse { get; set; }

    /// <summary>Raw send JSON.</summary>
    public string? RawSendResponse { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser? CreatedByUser { get; set; }

    /// <summary>Items in this batch.</summary>
    public ICollection<UspsScanBatchItem> Items { get; set; } = new List<UspsScanBatchItem>();
}

/// <summary>One order/tracking pair inside a <see cref="UspsScanBatch"/>.</summary>
public class UspsScanBatchItem
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent batch.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Local order (nullable if order later deleted).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Tracking number submitted to PrintPackShip.</summary>
    public string TrackingNo { get; set; } = string.Empty;

    /// <summary>Parent batch navigation.</summary>
    public UspsScanBatch? Batch { get; set; }

    /// <summary>Order navigation.</summary>
    public Order? Order { get; set; }
}
