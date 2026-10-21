namespace Application.DTOs.Shein;

/// <summary>Outcome of Sync track for one platform order (fetch SHEIN detail + cost + Mark Ship).</summary>
public sealed class SheinOrderSyncTrackResult
{
    /// <summary>Platform order id.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Billnos actually fetched from SHEIN.</summary>
    public int Fetched { get; set; }

    /// <summary>Supplier links whose tracking changed.</summary>
    public int UpdatedTracking { get; set; }

    /// <summary>Supplier links whose cost breakdown was refreshed from detail.</summary>
    public int UpdatedCost { get; set; }

    /// <summary>Mark Ship succeeded.</summary>
    public int AutoShipped { get; set; }

    /// <summary>Mark Ship failed (retryable).</summary>
    public int AutoShipFailed { get; set; }

    /// <summary>Mark Ship blocked (needs human).</summary>
    public int AutoShipBlocked { get; set; }

    /// <summary>Per-billno outcomes for toast detail.</summary>
    public List<SheinOrderSyncTrackBillnoResult> Billnos { get; set; } = [];

    /// <summary>Human-readable notes (manual cost, session gaps).</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>One-line summary for logs and toast.</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>One billno inside <see cref="SheinOrderSyncTrackResult"/>.</summary>
public sealed class SheinOrderSyncTrackBillnoResult
{
    /// <summary>SHEIN billno.</summary>
    public string Billno { get; set; } = string.Empty;

    /// <summary>Fulfil account display name when resolved.</summary>
    public string? FulfilAccountName { get; set; }

    /// <summary>True when <c>get_order_info</c> returned a usable payload.</summary>
    public bool FetchOk { get; set; }

    /// <summary>Stable skip code when not fetched/synced — see <see cref="SheinDetailSweepSkipReasons"/>.</summary>
    public string? SkipReason { get; set; }

    /// <summary>Vietnamese error detail for toast.</summary>
    public string? Error { get; set; }

    /// <summary>True when local tracking changed this run.</summary>
    public bool UpdatedTracking { get; set; }

    /// <summary>Tracking after sync.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Tracking kind after sync.</summary>
    public string? TrackingKind { get; set; }

    /// <summary>Carrier after sync.</summary>
    public string? Carrier { get; set; }

    /// <summary>True when cost breakdown was refreshed from detail this run.</summary>
    public bool UpdatedCost { get; set; }

    /// <summary>Supplier total after sync.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>Supplier subtotal after sync.</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>True when subtotal or total is still null after sync.</summary>
    public bool CostIncomplete { get; set; }

    /// <summary>Auto Mark Ship status on the link after this run.</summary>
    public string? AutoMarkShipStatus { get; set; }

    /// <summary>Auto Mark Ship error when failed/blocked.</summary>
    public string? AutoMarkShipError { get; set; }
}
