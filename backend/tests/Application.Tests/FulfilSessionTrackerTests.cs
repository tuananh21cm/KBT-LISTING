using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>In-memory SignalR fulfil-session presence tracker.</summary>
public class FulfilSessionTrackerTests
{
    private static readonly Guid SessionA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SessionB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid User1 = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ReconnectWithinGrace_DoesNotExpire()
    {
        var tracker = new FulfilSessionTracker();
        var now = DateTime.UtcNow;

        tracker.MarkConnected(SessionA, User1);
        tracker.MarkDisconnected(SessionA);
        tracker.MarkConnected(SessionA, User1);

        var expired = tracker.TakeExpired(TimeSpan.FromSeconds(90), now.AddSeconds(120));
        Assert.Empty(expired);
    }

    [Fact]
    public void DisconnectedPastGrace_IsTakenOnce()
    {
        var tracker = new FulfilSessionTracker();
        var now = DateTime.UtcNow;

        tracker.MarkConnected(SessionA, User1);
        tracker.MarkDisconnected(SessionA);

        var first = tracker.TakeExpired(TimeSpan.FromSeconds(90), now.AddSeconds(91));
        Assert.Single(first);
        Assert.Equal(SessionA, first[0].SessionId);
        Assert.Equal(User1, first[0].UserId);

        var second = tracker.TakeExpired(TimeSpan.FromSeconds(90), now.AddSeconds(200));
        Assert.Empty(second);
    }

    [Fact]
    public void MultipleConnections_OnlyExpiresWhenAllDrop()
    {
        var tracker = new FulfilSessionTracker();
        var now = DateTime.UtcNow;

        tracker.MarkConnected(SessionA, User1);
        tracker.MarkConnected(SessionA, User1);
        tracker.MarkDisconnected(SessionA);

        Assert.Empty(tracker.TakeExpired(TimeSpan.FromSeconds(1), now.AddSeconds(10)));

        tracker.MarkDisconnected(SessionA);
        var expired = tracker.TakeExpired(TimeSpan.FromSeconds(1), now.AddSeconds(10));
        Assert.Single(expired);
        Assert.Equal(SessionA, expired[0].SessionId);
    }

    [Fact]
    public void StillWithinGrace_NotExpired()
    {
        var tracker = new FulfilSessionTracker();
        var now = DateTime.UtcNow;

        tracker.MarkConnected(SessionA, User1);
        tracker.MarkDisconnected(SessionA);

        Assert.Empty(tracker.TakeExpired(TimeSpan.FromSeconds(90), now.AddSeconds(30)));
    }

    [Fact]
    public void IndependentSessions_ExpireSeparately()
    {
        var tracker = new FulfilSessionTracker();
        var now = DateTime.UtcNow;

        tracker.MarkConnected(SessionA, User1);
        tracker.MarkConnected(SessionB, User1);
        tracker.MarkDisconnected(SessionA);

        var expired = tracker.TakeExpired(TimeSpan.FromSeconds(90), now.AddSeconds(100));
        Assert.Single(expired);
        Assert.Equal(SessionA, expired[0].SessionId);
    }
}
