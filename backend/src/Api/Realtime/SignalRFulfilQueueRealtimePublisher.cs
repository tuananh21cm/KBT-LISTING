using Application.DTOs.Order;
using Application.Interfaces;
using Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Api.Realtime;

/// <summary>
/// Event payload for hub method <c>orderClaimChanged</c> (camelCase JSON).
/// </summary>
public sealed class OrderClaimChangedEvent
{
    /// <summary>Claim snapshots (one message may cover a customer-group batch).</summary>
    public List<FulfilClaimItemDto> Items { get; set; } = [];

    /// <summary>Server UTC clock for client countdown.</summary>
    public DateTime ServerTime { get; set; }
}

/// <summary>
/// Publishes fulfil-queue claim events via SignalR <see cref="NotificationsHub"/>.
/// Failures are logged and swallowed so claim persistence is not rolled back.
/// </summary>
public sealed class SignalRFulfilQueueRealtimePublisher : IFulfilQueueRealtimePublisher
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly ILogger<SignalRFulfilQueueRealtimePublisher> _logger;

    /// <summary>Initializes the publisher.</summary>
    public SignalRFulfilQueueRealtimePublisher(
        IHubContext<NotificationsHub> hub,
        ILogger<SignalRFulfilQueueRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishClaimChangedAsync(
        IReadOnlyList<FulfilClaimItemDto> items,
        DateTime serverTime,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            await _hub.Clients
                .Group(RealtimeGroups.FulfilQueue)
                .SendAsync(
                    "orderClaimChanged",
                    new OrderClaimChangedEvent
                    {
                        Items = items.ToList(),
                        ServerTime = serverTime,
                    },
                    cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR orderClaimChanged failed for {Count} item(s)",
                items.Count);
        }
    }
}
