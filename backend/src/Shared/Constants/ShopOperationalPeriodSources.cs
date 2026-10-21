namespace Shared.Constants;

/// <summary>
/// Source tags for <c>ShopOperationalPeriod</c> open/close events.
/// </summary>
public static class ShopOperationalPeriodSources
{
    /// <summary>Shop became Active (create or status change).</summary>
    public const string Active = "Active";

    /// <summary>Inferred from migration / historical reconstruction.</summary>
    public const string Backfill = "Backfill";

    /// <summary>Start pulled earlier by a synced historical non-canceled order.</summary>
    public const string OrderHistory = "OrderHistory";

    /// <summary>Shop became Deactivated.</summary>
    public const string Deactivated = "Deactivated";

    /// <summary>Shop became Suspended.</summary>
    public const string Suspended = "Suspended";

    /// <summary>Shop owner was reassigned.</summary>
    public const string OwnerChanged = "OwnerChanged";

    /// <summary>Shop was deleted.</summary>
    public const string Deleted = "Deleted";
}
