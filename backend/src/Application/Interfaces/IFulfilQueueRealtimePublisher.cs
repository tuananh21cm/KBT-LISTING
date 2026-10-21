using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Pushes fulfil-queue claim events to connected browsers (SignalR).
/// Application layer stays free of SignalR — API host registers the real publisher.
/// </summary>
public interface IFulfilQueueRealtimePublisher
{
    /// <summary>
    /// Broadcasts claim state changes to the <c>fulfil-queue</c> group.
    /// Failures must be logged and swallowed so claim persistence is not rolled back.
    /// </summary>
    Task PublishClaimChangedAsync(
        IReadOnlyList<FulfilClaimItemDto> items,
        DateTime serverTime,
        CancellationToken cancellationToken = default);
}
