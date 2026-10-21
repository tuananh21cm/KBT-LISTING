using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishQueueStatusResolverTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ListingAutoPublishQueueStatusResolver _resolver;

    public ListingAutoPublishQueueStatusResolverTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _resolver = new ListingAutoPublishQueueStatusResolver(
            _db,
            new FakeSettingProvider(new Dictionary<string, string>
            {
                [ListingAutoPublishSettingKeys.Enabled] = "true",
                [ListingAutoPublishSettingKeys.DefaultIntervalSeconds] = "120",
                [ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes] = "0",
            }));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ThreeEligibleSameShop_OneWaitingTwoQueue()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var ids = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
        };
        var baseTime = DateTime.UtcNow.AddHours(-3);
        for (var i = 0; i < ids.Length; i++)
        {
            await SeedListingAsync(ids[i], shopId, ownerId, spiId, baseTime.AddMinutes(i));
        }

        var result = await _resolver.ResolveAsync(ids);

        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[ids[0]].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[ids[1]].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[ids[2]].QueueStatus);
    }

    [Fact]
    public async Task TwoShopsTwoDraftsEach_OneWaitingOneQueuePerShop()
    {
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiA = Guid.NewGuid();
        var spiB = Guid.NewGuid();
        await SeedShopAsync(shopA, ownerId);
        await SeedShopAsync(shopB, ownerId);
        await SeedSupplierAsync(spiA);
        await SeedSupplierAsync(spiB);

        var aWaiting = Guid.NewGuid();
        var aQueue = Guid.NewGuid();
        var bWaiting = Guid.NewGuid();
        var bQueue = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-2);
        await SeedListingAsync(aWaiting, shopA, ownerId, spiA, t);
        await SeedListingAsync(aQueue, shopA, ownerId, spiA, t.AddMinutes(1));
        await SeedListingAsync(bWaiting, shopB, ownerId, spiB, t);
        await SeedListingAsync(bQueue, shopB, ownerId, spiB, t.AddMinutes(1));

        var result = await _resolver.ResolveAsync([aWaiting, aQueue, bWaiting, bQueue]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[aWaiting].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[aQueue].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[bWaiting].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[bQueue].QueueStatus);
    }

    [Fact]
    public async Task RunningListing_Processing_NextFifoWaiting()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var processingId = Guid.NewGuid();
        var nextId = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-1);
        await SeedListingAsync(processingId, shopId, ownerId, spiId, t);
        await SeedListingAsync(nextId, shopId, ownerId, spiId, t.AddMinutes(5));
        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = processingId,
            ShopId = shopId,
            OwnerId = ownerId,
            Status = ListingAutoPublishRunStatuses.Running,
            StartedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync([processingId, nextId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Processing, result[processingId].QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[nextId].QueueStatus);
    }

    [Fact]
    public async Task IntervalNotElapsed_HeadIsWaitingWithNextPickAt()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        var lastPublish = DateTime.UtcNow.AddSeconds(-30);
        await SeedShopAsync(
            shopId,
            ownerId,
            lastAutoPublishAt: lastPublish,
            intervalSeconds: 120);
        await SeedSupplierAsync(spiId);

        var headId = Guid.NewGuid();
        var queueId = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-2);
        await SeedListingAsync(headId, shopId, ownerId, spiId, t);
        await SeedListingAsync(queueId, shopId, ownerId, spiId, t.AddMinutes(1));

        var result = await _resolver.ResolveAsync([headId, queueId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[headId].QueueStatus);
        Assert.NotNull(result[headId].NextPickAt);
        Assert.True(result[headId].NextPickAt!.Value > DateTime.UtcNow);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[queueId].QueueStatus);
        Assert.Null(result[queueId].NextPickAt);
    }

    [Fact]
    public async Task PartialBatch_HeadNotInBatch_SubsetGetsQueue()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var headId = Guid.NewGuid();
        var youngerInBatch = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddHours(-3);
        await SeedListingAsync(headId, shopId, ownerId, spiId, baseTime);
        await SeedListingAsync(youngerInBatch, shopId, ownerId, spiId, baseTime.AddMinutes(5));

        var result = await _resolver.ResolveAsync([youngerInBatch]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[youngerInBatch].QueueStatus);
    }

    [Fact]
    public async Task PartialBatch_OnlyYoungerListingInBatch_GetsQueue()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var headId = Guid.NewGuid();
        var youngerId = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-2);
        await SeedListingAsync(headId, shopId, ownerId, spiId, t);
        await SeedListingAsync(youngerId, shopId, ownerId, spiId, t.AddMinutes(10));

        var result = await _resolver.ResolveAsync([youngerId]);

        Assert.Single(result);
        Assert.Equal(ListingAutoPublishQueueStatuses.Queue, result[youngerId].QueueStatus);
        Assert.Null(result[youngerId].NextPickAt);
    }

    [Fact]
    public async Task HeadNotEligible_NextEligibleGetsWaiting()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiWithChart = Guid.NewGuid();
        var spiNoChart = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiWithChart);
        _db.SupplierProductInfos.Add(new SupplierProductInfo
        {
            Id = spiNoChart,
            SourceUrlKey = $"key-{spiNoChart:N}",
            SizeChartImageUrl = null,
        });
        await _db.SaveChangesAsync();

        var headNoChart = Guid.NewGuid();
        var nextEligible = Guid.NewGuid();
        var t = DateTime.UtcNow.AddHours(-2);
        await SeedListingAsync(headNoChart, shopId, ownerId, spiNoChart, t);
        await SeedListingAsync(nextEligible, shopId, ownerId, spiWithChart, t.AddMinutes(5));

        var result = await _resolver.ResolveAsync([headNoChart, nextEligible]);

        Assert.False(result.ContainsKey(headNoChart));
        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[nextEligible].QueueStatus);
        Assert.NotNull(result[nextEligible].NextPickAt);
    }

    [Fact]
    public async Task PublishStatusFailed_WithoutSkipOrFailedRun_IsNotQueueFailed()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var listingId = Guid.NewGuid();
        await SeedListingAsync(listingId, shopId, ownerId, spiId, DateTime.UtcNow.AddHours(-1));
        var listing = await _db.Listings.FirstAsync(l => l.Id == listingId);
        listing.PublishStatus = PublishStatuses.Failed;
        listing.SyncError = "Weight with Package;Product Dimensions";
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync([listingId]);

        Assert.True(
            !result.TryGetValue(listingId, out var status)
            || status.QueueStatus != ListingAutoPublishQueueStatuses.Failed);
    }

    [Fact]
    public async Task LatestRunFailed_WithoutSkip_IsNotQueueFailed()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var listingId = Guid.NewGuid();
        await SeedListingAsync(listingId, shopId, ownerId, spiId, DateTime.UtcNow.AddHours(-1));
        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = listingId,
            ShopId = shopId,
            OwnerId = ownerId,
            Status = ListingAutoPublishRunStatuses.Failed,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            FinishedAt = DateTime.UtcNow.AddMinutes(-9),
            ErrorMessage = "OpenAI rewrite thất bại",
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync([listingId]);

        Assert.True(result.TryGetValue(listingId, out var status));
        Assert.NotEqual(ListingAutoPublishQueueStatuses.Failed, status.QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, status.QueueStatus);
    }

    [Fact]
    public async Task RetryAutoPublish_ClearsFailedBadge_ThenWaiting()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var listingId = Guid.NewGuid();
        await SeedListingAsync(listingId, shopId, ownerId, spiId, DateTime.UtcNow.AddHours(-1));
        var listing = await _db.Listings.FirstAsync(l => l.Id == listingId);
        listing.AutoPublishSkipped = true;
        listing.AutoPublishSkipReason = "OpenAI rewrite thất bại";
        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = listingId,
            ShopId = shopId,
            OwnerId = ownerId,
            Status = ListingAutoPublishRunStatuses.Failed,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            FinishedAt = DateTime.UtcNow.AddMinutes(-9),
            ErrorMessage = "OpenAI rewrite thất bại",
        });
        await _db.SaveChangesAsync();

        var before = await _resolver.ResolveAsync([listingId]);
        Assert.Equal(ListingAutoPublishQueueStatuses.Failed, before[listingId].QueueStatus);

        await new ListingAutoPublishRunService(_db).RetryAutoPublishAsync(listingId);

        var after = await _resolver.ResolveAsync([listingId]);
        Assert.True(after.TryGetValue(listingId, out var status));
        Assert.NotEqual(ListingAutoPublishQueueStatuses.Failed, status.QueueStatus);
        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, status.QueueStatus);
    }

    [Fact]
    public async Task AutoPublishSkipped_Failed_NotQueue()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var skippedId = Guid.NewGuid();
        await SeedListingAsync(skippedId, shopId, ownerId, spiId, DateTime.UtcNow.AddHours(-1));
        var listing = await _db.Listings.FirstAsync(l => l.Id == skippedId);
        listing.AutoPublishSkipped = true;
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync([skippedId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Failed, result[skippedId].QueueStatus);
    }

    private async Task SeedShopAsync(
        Guid shopId,
        Guid ownerId,
        DateTime? lastAutoPublishAt = null,
        int? intervalSeconds = null)
    {
        _db.Shops.Add(new Shop
        {
            Id = shopId,
            OwnerId = ownerId,
            Name = "Shop",
            PlatformId = Guid.NewGuid(),
            Status = ShopStatuses.Active,
            AutoPublishEnabled = true,
            FourSellerShopId = "12345",
            LastAutoPublishAt = lastAutoPublishAt,
            AutoPublishIntervalSeconds = intervalSeconds,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedSupplierAsync(Guid spiId)
    {
        _db.SupplierProductInfos.Add(new SupplierProductInfo
        {
            Id = spiId,
            SourceUrlKey = $"key-{spiId:N}",
            SizeChartImageUrl = "https://example.com/chart.png",
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedListingAsync(
        Guid listingId,
        Guid shopId,
        Guid ownerId,
        Guid spiId,
        DateTime createdAt)
    {
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = ownerId,
            ShopId = shopId,
            PageType = ListingPageTypes.Draft,
            ProductName = "Test",
            SupplierProductInfoId = spiId,
            CreatedAt = createdAt,
        });
        await _db.SaveChangesAsync();
    }

    private sealed class FakeSettingProvider(IReadOnlyDictionary<string, string> values) : ISettingProvider
    {
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.TryGetValue(key, out var v) ? v : null);

        public Task<Dictionary<string, string>> GetValuesAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var v))
                {
                    result[key] = v;
                }
            }

            return Task.FromResult(result);
        }

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
