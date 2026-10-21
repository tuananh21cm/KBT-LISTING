namespace Application.DTOs.Shein;

/// <summary>Outcome of one backend SHEIN detail sweep pass.</summary>
public sealed class SheinDetailSweepResult
{
    /// <summary>Billnos selected as candidates before session resolution (full cycle snapshot).</summary>
    public int Candidates { get; set; }

    /// <summary>Number of max_billnos batches processed in this cycle.</summary>
    public int Batches { get; set; }

    /// <summary>Billnos actually fetched from SHEIN.</summary>
    public int Fetched { get; set; }

    /// <summary>Supplier links whose tracking changed.</summary>
    public int UpdatedTracking { get; set; }

    /// <summary>Supplier links whose cost breakdown was refreshed from detail.</summary>
    public int UpdatedCost { get; set; }

    /// <summary>Orders whose tracking was published to the platform via Mark Ship.</summary>
    public int AutoShipped { get; set; }

    /// <summary>Mark Ship attempts that failed and will retry after the cooldown.</summary>
    public int AutoShipFailed { get; set; }

    /// <summary>Mark Ship attempts blocked on missing data (carrier, shopId) — need a human.</summary>
    public int AutoShipBlocked { get; set; }

    /// <summary>Billnos skipped, keyed by reason, so a quiet pass is explainable.</summary>
    public Dictionary<string, int> Skipped { get; set; } = [];

    /// <summary>Human-readable notes (per-account errors, session gaps).</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>One-line summary for logs and the manual-run response.</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Reason codes for <see cref="SheinDetailSweepResult.Skipped"/>.</summary>
public static class SheinDetailSweepSkipReasons
{
    /// <summary>Link has no fulfil account and the deployment has more than one ready session.</summary>
    public const string AccountUnknown = "account_unknown";

    /// <summary>Owning account has no usable session (missing headers or cookie).</summary>
    public const string SessionMissing = "session_missing";

    /// <summary>Owning account is disabled.</summary>
    public const string AccountInactive = "account_inactive";

    /// <summary>SHEIN rejected or did not answer the detail call.</summary>
    public const string FetchFailed = "fetch_failed";
}
