using System.Collections.Concurrent;
using Application.Interfaces;

namespace Application.Services;

/// <summary>
/// Thread-safe in-memory tracker for Chrome-profile fulfil sessions via SignalR connection counts.
/// </summary>
public sealed class FulfilSessionTracker : IFulfilSessionTracker
{
    /// <summary>Grace before an orphaned session's claims are released (~90s).</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    /// <inheritdoc />
    public void MarkConnected(Guid sessionId, Guid userId)
    {
        _sessions.AddOrUpdate(
            sessionId,
            _ => new SessionState(1, userId, null),
            (_, prev) => new SessionState(prev.Count + 1, userId, null));
    }

    /// <inheritdoc />
    public void MarkDisconnected(Guid sessionId)
    {
        while (true)
        {
            if (!_sessions.TryGetValue(sessionId, out var prev))
            {
                return;
            }

            SessionState next;
            if (prev.Count <= 1)
            {
                next = new SessionState(0, prev.UserId, DateTime.UtcNow);
            }
            else
            {
                next = new SessionState(prev.Count - 1, prev.UserId, null);
            }

            if (_sessions.TryUpdate(sessionId, next, prev))
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<FulfilSessionExpiry> TakeExpired(TimeSpan grace, DateTime utcNow)
    {
        var cutoff = utcNow - grace;
        var expired = new List<FulfilSessionExpiry>();

        foreach (var kv in _sessions)
        {
            var state = kv.Value;
            if (state.Count > 0 || state.DisconnectedAt is not { } at || at > cutoff)
            {
                continue;
            }

            if (_sessions.TryRemove(new KeyValuePair<Guid, SessionState>(kv.Key, state)))
            {
                expired.Add(new FulfilSessionExpiry(kv.Key, state.UserId));
            }
        }

        return expired;
    }

    private sealed record SessionState(int Count, Guid UserId, DateTime? DisconnectedAt);
}
