namespace Shared.Constants;

/// <summary>SignalR group names for realtime broadcasts.</summary>
public static class RealtimeGroups
{
    /// <summary>
    /// Fulfil / Admin clients watching the purchase queue — receive <c>orderClaimChanged</c>.
    /// Joined when the connection has permission <c>admin.orders.view</c>.
    /// </summary>
    public const string FulfilQueue = "fulfil-queue";
}
