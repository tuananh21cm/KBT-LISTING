using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Recovers auto publish runs stuck in Running after process crash.</summary>
public sealed class ListingAutoPublishStaleRunRecovery : IListingAutoPublishStaleRunRecovery
{
    /// <summary>Runs older than this without finish are considered orphan.</summary>
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(30);

    private readonly IApplicationDbContext _db;
    private readonly IListingShopPublishLease _shopLease;

    public ListingAutoPublishStaleRunRecovery(
        IApplicationDbContext db,
        IListingShopPublishLease shopLease)
    {
        _db = db;
        _shopLease = shopLease;
    }

    public async Task<int> RecoverStaleRunsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - StaleThreshold;
        var stale = await _db.ListingAutoPublishRuns
            .Where(r => r.Status == ListingAutoPublishRunStatuses.Running && r.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var run in stale)
        {
            run.Status = ListingAutoPublishRunStatuses.Failed;
            run.ErrorMessage = "Run orphan (process restart hoặc treo) — đã recovery.";
            run.FinishedAt = now;
            run.DurationMs = (int)Math.Min(int.MaxValue, (now - run.StartedAt).TotalMilliseconds);
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var shopId in stale.Select(r => r.ShopId).Distinct())
        {
            _shopLease.ForceRelease(shopId);
        }

        return stale.Count;
    }
}
