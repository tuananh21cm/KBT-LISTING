using System.Collections.Concurrent;

namespace Application.Services;

using Application.Interfaces;

/// <summary>One auto publish per shop at a time.</summary>
public sealed class ListingShopPublishLease : IListingShopPublishLease
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public bool TryBegin(Guid shopId, out IDisposable? handle)
    {
        var gate = _locks.GetOrAdd(shopId, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            handle = null;
            return false;
        }

        handle = new ReleaseHandle(gate);
        return true;
    }

    public void ForceRelease(Guid shopId)
    {
        if (!_locks.TryGetValue(shopId, out var gate))
        {
            return;
        }

        // Best-effort: only release when the semaphore is held (count 0).
        if (gate.CurrentCount == 0)
        {
            try
            {
                gate.Release();
            }
            catch (SemaphoreFullException)
            {
                // already released
            }
        }
    }

    private sealed class ReleaseHandle : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public ReleaseHandle(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _gate.Release();
        }
    }
}
