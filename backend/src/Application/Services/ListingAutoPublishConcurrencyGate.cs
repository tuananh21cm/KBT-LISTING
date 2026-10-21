namespace Application.Services;

using Application.Interfaces;

/// <summary>Global semaphore limiting concurrent auto publish pipelines.</summary>
public sealed class ListingAutoPublishConcurrencyGate : IListingAutoPublishConcurrencyGate
{
    private readonly object _gate = new();
    private SemaphoreSlim _semaphore = new(10, 10);
    private int _capacity = 10;
    private int _inFlight;
    private int? _pendingCapacity;

    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }
    }

    public int InFlight => Volatile.Read(ref _inFlight);

    public int AvailableSlots
    {
        get
        {
            lock (_gate)
            {
                return Math.Max(0, _capacity - _inFlight);
            }
        }
    }

    public void Configure(int maxConcurrentPublishes)
    {
        var next = maxConcurrentPublishes < 1 ? 1 : maxConcurrentPublishes;
        lock (_gate)
        {
            if (next == _capacity)
            {
                _pendingCapacity = null;
                return;
            }

            _pendingCapacity = next;
            if (_inFlight == 0)
            {
                ApplyPendingCapacity();
            }
        }
    }

    public bool TryAcquire(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_semaphore.Wait(0))
        {
            return false;
        }

        Interlocked.Increment(ref _inFlight);
        return true;
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        Interlocked.Increment(ref _inFlight);
    }

    public void Release()
    {
        var inFlight = Interlocked.Decrement(ref _inFlight);
        if (inFlight < 0)
        {
            Interlocked.Increment(ref _inFlight);
            return;
        }

        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // ignore double-release
        }

        lock (_gate)
        {
            if (_inFlight == 0 && _pendingCapacity.HasValue)
            {
                ApplyPendingCapacity();
            }
        }
    }

    private void ApplyPendingCapacity()
    {
        if (!_pendingCapacity.HasValue)
        {
            return;
        }

        var next = _pendingCapacity.Value;
        _pendingCapacity = null;
        if (next == _capacity)
        {
            return;
        }

        _capacity = next;
        var old = _semaphore;
        _semaphore = new SemaphoreSlim(next, next);
        old.Dispose();
    }
}
