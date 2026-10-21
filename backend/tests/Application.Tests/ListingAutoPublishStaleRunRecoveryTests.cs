using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishStaleRunRecoveryTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ListingAutoPublishStaleRunRecovery _recovery;

    public ListingAutoPublishStaleRunRecoveryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _recovery = new ListingAutoPublishStaleRunRecovery(_db, new ListingShopPublishLease());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecoverStaleRuns_MarksOldRunningAsFailed()
    {
        var stale = new ListingAutoPublishRun
        {
            ListingId = Guid.NewGuid(),
            ShopId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Status = ListingAutoPublishRunStatuses.Running,
            StartedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
        };
        _db.ListingAutoPublishRuns.Add(stale);
        await _db.SaveChangesAsync();

        var count = await _recovery.RecoverStaleRunsAsync();

        Assert.Equal(1, count);
        var updated = await _db.ListingAutoPublishRuns.FirstAsync(r => r.Id == stale.Id);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, updated.Status);
        Assert.NotNull(updated.FinishedAt);
    }

    [Fact]
    public async Task RecoverStaleRuns_IgnoresRecentRunning()
    {
        var recent = new ListingAutoPublishRun
        {
            ListingId = Guid.NewGuid(),
            ShopId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Status = ListingAutoPublishRunStatuses.Running,
            StartedAt = DateTime.UtcNow - TimeSpan.FromMinutes(5)
        };
        _db.ListingAutoPublishRuns.Add(recent);
        await _db.SaveChangesAsync();

        var count = await _recovery.RecoverStaleRunsAsync();

        Assert.Equal(0, count);
        var updated = await _db.ListingAutoPublishRuns.FirstAsync(r => r.Id == recent.Id);
        Assert.Equal(ListingAutoPublishRunStatuses.Running, updated.Status);
    }

    [Fact]
    public async Task RecoverStaleRuns_ForceReleasesShopLease()
    {
        var shopId = Guid.NewGuid();
        var shopLease = new ListingShopPublishLease();
        var recovery = new ListingAutoPublishStaleRunRecovery(_db, shopLease);

        Assert.True(shopLease.TryBegin(shopId, out var handle));
        Assert.NotNull(handle);

        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = Guid.NewGuid(),
            ShopId = shopId,
            OwnerId = Guid.NewGuid(),
            Status = ListingAutoPublishRunStatuses.Running,
            StartedAt = DateTime.UtcNow - TimeSpan.FromHours(2),
        });
        await _db.SaveChangesAsync();

        var count = await recovery.RecoverStaleRunsAsync();

        Assert.Equal(1, count);
        Assert.True(shopLease.TryBegin(shopId, out var secondHandle));
        secondHandle!.Dispose();
    }
}
