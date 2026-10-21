using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Shared.Constants;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Api.Hubs;

/// <summary>
/// SignalR hub for fulfil-queue realtime (<c>orderClaimChanged</c>). The path stays
/// <c>/hubs/notifications</c> for client compatibility; in-app inbox/chat/presence were removed.
/// Clients connect with JWT (<c>access_token</c> query). Fulfil extension also passes
/// <c>fulfilSession</c> (Chrome-profile UUID) so orphaned sessions can auto-release claims
/// after a disconnect grace window.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    private static readonly ConcurrentDictionary<string, Guid> ConnectionSessions = new();

    private readonly IFulfilSessionTracker _fulfilSessions;

    /// <summary>Initializes the hub.</summary>
    public NotificationsHub(IFulfilSessionTracker fulfilSessions)
    {
        _fulfilSessions = fulfilSessions;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        // Fulfil / Admin watching the purchase queue receive orderClaimChanged.
        if (Context.User?.HasClaim(ClaimConstants.Permission, "admin.orders.view") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.FulfilQueue);
        }

        if (TryGetUserId(out var fulfilUserId)
            && TryGetFulfilSessionId(out var sessionId))
        {
            _fulfilSessions.MarkConnected(sessionId, fulfilUserId);
            ConnectionSessions[Context.ConnectionId] = sessionId;
        }

        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionSessions.TryRemove(Context.ConnectionId, out var sessionId))
        {
            _fulfilSessions.MarkDisconnected(sessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetFulfilSessionId(out Guid sessionId)
    {
        sessionId = default;
        var raw = Context.GetHttpContext()?.Request.Query["fulfilSession"].FirstOrDefault();
        return Guid.TryParse(raw, out sessionId) && sessionId != Guid.Empty;
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = default;
        var raw = Context.UserIdentifier
            ?? Context.User?.FindFirst(ClaimConstants.UserId)?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out userId);
    }
}

/// <summary>
/// Maps SignalR <see cref="HubCallerContext.UserIdentifier"/> from JWT <c>uid</c> claim.
/// </summary>
public sealed class JwtUserIdProvider : IUserIdProvider
{
    /// <inheritdoc />
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(ClaimConstants.UserId)?.Value
            ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? connection.User?.FindFirst("sub")?.Value;
    }
}