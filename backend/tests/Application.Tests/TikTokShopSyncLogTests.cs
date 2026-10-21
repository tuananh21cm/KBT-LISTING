using Application.DTOs.Shop;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class TikTokShopSyncLogTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TikTokShopSyncLogWriter _writer;

    public TikTokShopSyncLogTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _writer = new TikTokShopSyncLogWriter(_db, NullLogger<TikTokShopSyncLogWriter>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteAsync_PersistsStatusAndType()
    {
        var ownerId = Guid.NewGuid();
        const string runId = "run-abc";

        await _writer.WriteAsync(
            ownerId,
            runId,
            "seller-1",
            null,
            TikTokShopSyncTypes.Profile,
            TikTokShopSyncStatuses.Completed);

        var row = await _db.TikTokShopSyncLogs.SingleAsync();
        Assert.Equal(runId, row.RunId);
        Assert.Equal(ownerId, row.OwnerId);
        Assert.Equal("seller-1", row.ExternalShopId);
        Assert.Equal(TikTokShopSyncTypes.Profile, row.SyncType);
        Assert.Equal(TikTokShopSyncStatuses.Completed, row.Status);
        Assert.Equal(TikTokShopSyncSources.Manual, row.Source);
        Assert.True(row.SyncedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task WriteAsync_EmptyRunId_SkipsInsert()
    {
        await _writer.WriteAsync(
            Guid.NewGuid(),
            "  ",
            "seller-1",
            null,
            TikTokShopSyncTypes.Payout,
            TikTokShopSyncStatuses.Skipped);

        Assert.Empty(await _db.TikTokShopSyncLogs.ToListAsync());
    }

    [Fact]
    public async Task ListAsync_FiltersByExternalShopIdAndSyncType()
    {
        var ownerId = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _db.TikTokShopSyncLogs.AddRange(
            new TikTokShopSyncLog
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                ShopId = shopId,
                ExternalShopId = "shop-a",
                RunId = "r1",
                SyncType = TikTokShopSyncTypes.Profile,
                Status = TikTokShopSyncStatuses.Completed,
                SyncedAt = DateTime.UtcNow.AddMinutes(-2),
                Source = TikTokShopSyncSources.Manual
            },
            new TikTokShopSyncLog
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                ShopId = shopId,
                ExternalShopId = "shop-a",
                RunId = "r1",
                SyncType = TikTokShopSyncTypes.Payout,
                Status = TikTokShopSyncStatuses.Failed,
                ErrorMessage = "no tab",
                SyncedAt = DateTime.UtcNow.AddMinutes(-1),
                Source = TikTokShopSyncSources.Manual
            },
            new TikTokShopSyncLog
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                ExternalShopId = "shop-b",
                RunId = "r2",
                SyncType = TikTokShopSyncTypes.Profile,
                Status = TikTokShopSyncStatuses.Skipped,
                SyncedAt = DateTime.UtcNow,
                Source = TikTokShopSyncSources.Manual
            },
            new TikTokShopSyncLog
            {
                Id = Guid.NewGuid(),
                OwnerId = otherOwner,
                ExternalShopId = "shop-a",
                RunId = "r3",
                SyncType = TikTokShopSyncTypes.Profile,
                Status = TikTokShopSyncStatuses.Completed,
                SyncedAt = DateTime.UtcNow,
                Source = TikTokShopSyncSources.Manual
            });
        await _db.SaveChangesAsync();

        var service = new TikTokShopSyncLogService(_db, _writer);
        var page = await service.ListAsync(
            ownerId,
            new TikTokShopSyncLogQuery
            {
                ExternalShopId = "shop-a",
                SyncType = TikTokShopSyncTypes.Payout,
                Page = 1,
                PageSize = 20
            });

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
        Assert.Equal(TikTokShopSyncTypes.Payout, page.Items[0].SyncType);
        Assert.Equal(TikTokShopSyncStatuses.Failed, page.Items[0].Status);
        Assert.Equal("no tab", page.Items[0].ErrorMessage);
    }
}
