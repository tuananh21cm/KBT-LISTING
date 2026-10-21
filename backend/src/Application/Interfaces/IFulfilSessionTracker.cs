namespace Application.Interfaces;

/// <summary>
/// In-memory SignalR presence per Chrome-profile fulfil session (single API instance).
/// Used to auto-release claims when a session disconnects longer than the grace window.
/// </summary>
public interface IFulfilSessionTracker
{
    /// <summary>
    /// Registers a hub connection for <paramref name="sessionId"/>.
    /// Clears any pending disconnect timer (reconnect within grace).
    /// </summary>
    void MarkConnected(Guid sessionId, Guid userId);

    /// <summary>
    /// Unregisters one hub connection. When connection count hits zero, starts the grace timer.
    /// </summary>
    void MarkDisconnected(Guid sessionId);

    /// <summary>
    /// Removes and returns sessions whose last connection dropped at least
    /// <paramref name="grace"/> ago (count = 0).
    /// </summary>
    IReadOnlyList<FulfilSessionExpiry> TakeExpired(TimeSpan grace, DateTime utcNow);
}

/// <summary>A fulfil session that exceeded the disconnect grace window.</summary>
public readonly record struct FulfilSessionExpiry(Guid SessionId, Guid UserId);
