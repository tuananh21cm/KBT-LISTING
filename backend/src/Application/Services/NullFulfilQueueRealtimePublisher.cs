using Application.DTOs.Order;
using Application.Interfaces;

namespace Application.Services;

/// <summary>
/// No-op claim publisher used until SignalR publisher is registered by the API host.
/// </summary>
public sealed class NullFulfilQueueRealtimePublisher : IFulfilQueueRealtimePublisher
{
    /// <inheritdoc />
    public Task PublishClaimChangedAsync(
        IReadOnlyList<FulfilClaimItemDto> items,
        DateTime serverTime,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
