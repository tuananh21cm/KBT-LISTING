using System.Collections.Concurrent;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>In-memory cancellation for auto publish runs.</summary>
public sealed class ListingAutoPublishCancellationCoordinator : IListingAutoPublishCancellationCoordinator
{
    private readonly object _globalGate = new();
    private CancellationTokenSource _globalCts = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _shopCts = new();
    private readonly ConcurrentDictionary<Guid, object> _shopLocks = new();
    private readonly ConcurrentDictionary<Guid, Guid> _runShops = new();
    private readonly IListingShopPublishLease _shopLease;
    private readonly ILogger<ListingAutoPublishCancellationCoordinator> _logger;

    public ListingAutoPublishCancellationCoordinator(
        IListingShopPublishLease shopLease,
        ILogger<ListingAutoPublishCancellationCoordinator> logger)
    {
        _shopLease = shopLease;
        _logger = logger;
    }

    public CancellationToken LinkRun(Guid runId, Guid shopId, CancellationToken outer)
    {
        _runShops[runId] = shopId;
        var shopLock = _shopLocks.GetOrAdd(shopId, _ => new object());
        lock (shopLock)
        {
            var shopCts = _shopCts.GetOrAdd(shopId, _ => new CancellationTokenSource());
            lock (_globalGate)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(
                    outer,
                    _globalCts.Token,
                    shopCts.Token).Token;
            }
        }
    }

    public void CancelAll(string reason)
    {
        foreach (var shopId in _shopCts.Keys.ToList())
        {
            CancelShop(shopId, reason);
        }

        lock (_globalGate)
        {
            try
            {
                _globalCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }

            _globalCts.Dispose();
            _globalCts = new CancellationTokenSource();
        }

        _logger.LogInformation("Auto publish CancelAll: {Reason}", reason);
    }

    public void CancelShop(Guid shopId, string reason)
    {
        var shopLock = _shopLocks.GetOrAdd(shopId, _ => new object());
        lock (shopLock)
        {
            var cts = _shopCts.GetOrAdd(shopId, _ => new CancellationTokenSource());
            if (!cts.IsCancellationRequested)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // disposed concurrently
                }
            }
        }

        _shopLease.ForceRelease(shopId);
        _logger.LogInformation("Auto publish CancelShop {ShopId}: {Reason}", shopId, reason);
    }

    public void ResetShop(Guid shopId)
    {
        var shopLock = _shopLocks.GetOrAdd(shopId, _ => new object());
        lock (shopLock)
        {
            if (_shopCts.TryRemove(shopId, out var old))
            {
                old.Dispose();
            }

            _shopCts[shopId] = new CancellationTokenSource();
        }

        _shopLease.ForceRelease(shopId);
    }

    public void UnregisterRun(Guid runId, Guid shopId)
    {
        _runShops.TryRemove(runId, out _);
    }
}
