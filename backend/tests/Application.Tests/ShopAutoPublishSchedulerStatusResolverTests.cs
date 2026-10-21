using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ShopAutoPublishSchedulerStatusResolverTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ShopAutoPublishSchedulerStatusResolver _resolver;

    public ShopAutoPublishSchedulerStatusResolverTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _resolver = new ShopAutoPublishSchedulerStatusResolver(
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
    public async Task RunningListing_ShopProcessing()
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

        var result = await _resolver.ResolveAsync([shopId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Processing, result[shopId].SchedulerStatus);
        Assert.Null(result[shopId].NextPickAt);
        Assert.Equal(1, result[shopId].EligibleDraftCount);
    }

    [Fact]
    public async Task IntervalNotElapsed_WaitingWithNextPickAt()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        var lastPublish = DateTime.UtcNow.AddSeconds(-30);
        await SeedShopAsync(shopId, ownerId, lastAutoPublishAt: lastPublish, intervalSeconds: 120);
        await SeedSupplierAsync(spiId);
        await SeedListingAsync(Guid.NewGuid(), shopId, ownerId, spiId, DateTime.UtcNow.AddHours(-2));

        var result = await _resolver.ResolveAsync([shopId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[shopId].SchedulerStatus);
        Assert.NotNull(result[shopId].NextPickAt);
        var expected = lastPublish.AddSeconds(120);
        Assert.True(Math.Abs((result[shopId].NextPickAt!.Value - expected).TotalSeconds) < 2);
        Assert.Equal(1, result[shopId].EligibleDraftCount);
    }

    [Fact]
    public async Task ThreeEligibleDrafts_EligibleCountThree()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var spiId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);
        await SeedSupplierAsync(spiId);

        var baseTime = DateTime.UtcNow.AddHours(-3);
        for (var i = 0; i < 3; i++)
        {
            await SeedListingAsync(Guid.NewGuid(), shopId, ownerId, spiId, baseTime.AddMinutes(i));
        }

        var result = await _resolver.ResolveAsync([shopId]);

        Assert.Equal(ListingAutoPublishQueueStatuses.Waiting, result[shopId].SchedulerStatus);
        Assert.Equal(3, result[shopId].EligibleDraftCount);
    }

    [Fact]
    public async Task AutoPublishDisabledShop_Omitted()
    {
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _db.Shops.Add(new Shop
        {
            Id = shopId,
            OwnerId = ownerId,
            Name = "Shop",
            PlatformId = Guid.NewGuid(),
            Status = ShopStatuses.Active,
            AutoPublishEnabled = false,
            FourSellerShopId = "12345",
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync([shopId]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SystemDisabled_Empty()
    {
        var disabledResolver = new ShopAutoPublishSchedulerStatusResolver(
            _db,
            new FakeSettingProvider(new Dictionary<string, string>
            {
                [ListingAutoPublishSettingKeys.Enabled] = "false",
            }));

        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await SeedShopAsync(shopId, ownerId);

        var result = await disabledResolver.ResolveAsync([shopId]);

        Assert.Empty(result);
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
