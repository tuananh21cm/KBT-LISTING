using System.Collections.Concurrent;
using Application.Interfaces;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// In-memory per-seller leases for listing sync (single API process).
/// Auto publish uses a separate concurrent counter so multiple shops of the same seller can publish in parallel.
/// </summary>
public sealed class ListingSellerSyncLease : IListingSellerSyncLease
{
    private readonly ConcurrentDictionary<Guid, SellerState> _states = new();

    /// <inheritdoc />
    public void TouchExtensionLease(Guid ownerId, TimeSpan ttl)
    {
        if (ownerId == Guid.Empty) return;
        if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(90);

        var state = _states.GetOrAdd(ownerId, static _ => new SellerState());
        lock (state.Gate)
        {
            var until = DateTime.UtcNow.Add(ttl);
            if (until > state.ExtensionLeaseExpiresUtc)
            {
                state.ExtensionLeaseExpiresUtc = until;
            }
        }
    }

    /// <inheritdoc />
    public bool HasActiveExtensionLease(Guid ownerId)
    {
        if (ownerId == Guid.Empty) return false;
        if (!_states.TryGetValue(ownerId, out var state)) return false;

        lock (state.Gate)
        {
            return state.ExtensionLeaseExpiresUtc > DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public bool TryBegin(Guid ownerId, ListingSyncLeaseSource source, out IDisposable? handle)
    {
        handle = null;
        if (ownerId == Guid.Empty) return false;

        var state = _states.GetOrAdd(ownerId, static _ => new SellerState());
        lock (state.Gate)
        {
            if (source == ListingSyncLeaseSource.AutoPublish)
            {
                if (state.ActiveSource.HasValue)
                {
                    return false;
                }

                if (state.ExtensionLeaseExpiresUtc > DateTime.UtcNow)
                {
                    return false;
                }

                state.AutoPublishCount++;
                handle = new AutoPublishReleaseHandle(this, ownerId);
                return true;
            }

            if (state.ActiveSource.HasValue || state.AutoPublishCount > 0)
            {
                return false;
            }

            if (source == ListingSyncLeaseSource.Background
                && state.ExtensionLeaseExpiresUtc > DateTime.UtcNow)
            {
                return false;
            }

            // Manual preempts extension presence so UI Sync is not blocked by a debounce window.
            if (source == ListingSyncLeaseSource.Manual)
            {
                state.ExtensionLeaseExpiresUtc = DateTime.MinValue;
            }

            state.ActiveSource = source;
            handle = new ExclusiveReleaseHandle(this, ownerId);
            return true;
        }
    }

    private void ReleaseExclusive(Guid ownerId)
    {
        if (!_states.TryGetValue(ownerId, out var state)) return;

        lock (state.Gate)
        {
            state.ActiveSource = null;
        }
    }

    private void ReleaseAutoPublish(Guid ownerId)
    {
        if (!_states.TryGetValue(ownerId, out var state)) return;

        lock (state.Gate)
        {
            if (state.AutoPublishCount > 0)
            {
                state.AutoPublishCount--;
            }
        }
    }

    private sealed class SellerState
    {
        public object Gate { get; } = new();
        public ListingSyncLeaseSource? ActiveSource { get; set; }
        public int AutoPublishCount { get; set; }
        public DateTime ExtensionLeaseExpiresUtc { get; set; }
    }

    private sealed class ExclusiveReleaseHandle : IDisposable
    {
        private ListingSellerSyncLease? _owner;
        private readonly Guid _ownerId;

        public ExclusiveReleaseHandle(ListingSellerSyncLease owner, Guid ownerId)
        {
            _owner = owner;
            _ownerId = ownerId;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseExclusive(_ownerId);
        }
    }

    private sealed class AutoPublishReleaseHandle : IDisposable
    {
        private ListingSellerSyncLease? _owner;
        private readonly Guid _ownerId;

        public AutoPublishReleaseHandle(ListingSellerSyncLease owner, Guid ownerId)
        {
            _owner = owner;
            _ownerId = ownerId;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseAutoPublish(_ownerId);
        }
    }
}
