namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Process-wide gate that serializes the two listing-sync background loops.
/// Draft and Published keep independent clocks, but concurrent pulls on the same seller
/// would collide on <c>IListingSellerSyncLease</c> (exclusive per seller regardless of source)
/// and silently skip accounts — this gate prevents that.
/// </summary>
public sealed class ListingSyncLoopGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Acquires the loop gate. Caller must dispose the returned handle (typically via <c>await using</c>).
    /// </summary>
    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new ReleaseHandle(_gate);
    }

    /// <summary>True when another listing sync loop currently holds the gate.</summary>
    public bool IsHeld => _gate.CurrentCount == 0;

    private sealed class ReleaseHandle : IAsyncDisposable
    {
        private SemaphoreSlim? _gate;

        public ReleaseHandle(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public ValueTask DisposeAsync()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
