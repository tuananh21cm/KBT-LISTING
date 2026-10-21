namespace Shared.Constants;

/// <summary>
/// Stable skip codes when an order is excluded from a PrintPackShip USPS scan push.
/// Contract between backend and admin/seller UI for result Modals.
/// </summary>
public static class UspsScanSkipReasons
{
    /// <summary>Order id not found in DB.</summary>
    public const string NotFound = "not_found";

    /// <summary>Seller-scoped push: order belongs to another owner.</summary>
    public const string NotOwned = "not_owned";

    /// <summary>No resolvable carrier tracking number.</summary>
    public const string NoTracking = "no_tracking";

    /// <summary>Order has no USPS last-mile signal.</summary>
    public const string NotUsps = "not_usps";

    /// <summary>Already stamped with a successful scan batch.</summary>
    public const string AlreadyPushed = "already_pushed";

    /// <summary>Same tracking appears twice in the request (case-insensitive).</summary>
    public const string DuplicateTracking = "duplicate_tracking";

    /// <summary>
    /// Tracking sits on a batch whose send result is unknown (timeout/5xx) —
    /// pushing again would risk a double charge.
    /// </summary>
    public const string PendingBatch = "pending_batch";

    /// <summary>
    /// Order already in transit or delivered — scan is only useful before the package moves.
    /// </summary>
    public const string AlreadyInTransit = "already_in_transit";

    /// <summary>TikTok refund or cancelled — no reason to scan tracking.</summary>
    public const string TerminalCommerce = "terminal_commerce";
}
