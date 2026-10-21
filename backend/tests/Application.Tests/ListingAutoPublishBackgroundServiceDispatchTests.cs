using System.Reflection;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Integrations.FourSeller;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishBackgroundServiceDispatchTests : IDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _provider;
    private readonly ListingAutoPublishConcurrencyGate _gate;
    private readonly ListingAutoPublishDispatchTrigger _trigger;
    private readonly CountingAutoPublishOrchestrator _orchestrator;

    public ListingAutoPublishBackgroundServiceDispatchTests()
    {
        _gate = new ListingAutoPublishConcurrencyGate();
        _trigger = new ListingAutoPublishDispatchTrigger();
        _orchestrator = new CountingAutoPublishOrchestrator(TimeSpan.FromMilliseconds(80));

        var services = new ServiceCollection();
        services.AddSingleton(_ => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options);
        services.AddScoped<IApplicationDbContext>(sp =>
            new ApplicationDbContext(sp.GetRequiredService<DbContextOptions<ApplicationDbContext>>()));
        services.AddScoped<IListingAutoPublishOrchestrator>(_ => _orchestrator);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task ShopFullByTtsPublished_IsNotDispatched()
    {
        _gate.Configure(10);
        var shopIds = await SeedReadyShopsAsync(1, returnShopIds: true);
        var shopId = shopIds[0];

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstAsync(s => s.Id == shopId);
            shop.PublishLimitTotal = 2;
            shop.PublishLimitPublished = 2;
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 10);
        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(0, dispatched);
        Assert.Equal(0, _orchestrator.StartedCount);
    }

    [Fact]
    public async Task ShopWithStaleLocalActiveButTtsRoom_IsDispatched()
    {
        _gate.Configure(10);
        var shopIds = await SeedReadyShopsAsync(1, returnShopIds: true);
        var shopId = shopIds[0];

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstAsync(s => s.Id == shopId);
            shop.PublishLimitTotal = 200;
            shop.PublishLimitPublished = 181;
            var ownerId = shop.OwnerId;
            for (var i = 0; i < 200; i++)
            {
                db.Listings.Add(new Listing
                {
                    Id = Guid.NewGuid(),
                    ShopId = shopId,
                    OwnerId = ownerId,
                    PageType = ListingPageTypes.Published,
                    SiteStatus = ListingSiteStatuses.Active,
                    ProductName = $"Stale active {i}",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                });
            }

            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 10);
        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(1, dispatched);
        await WaitUntilAsync(() => _orchestrator.StartedCount == 1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShopWithNullPublishLimitTotal_StillDispatched()
    {
        _gate.Configure(10);
        var shopIds = await SeedReadyShopsAsync(1, returnShopIds: true);
        var shopId = shopIds[0];

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstAsync(s => s.Id == shopId);
            shop.PublishLimitTotal = null;
            shop.PublishLimitPublished = null;
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 10);
        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(1, dispatched);
        await WaitUntilAsync(() => _orchestrator.StartedCount == 1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShopBlockedUntilFuture_IsNotDispatched()
    {
        _gate.Configure(10);
        var shopIds = await SeedReadyShopsAsync(1, returnShopIds: true);
        var shopId = shopIds[0];

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstAsync(s => s.Id == shopId);
            shop.AutoPublishBlockedUntil = DateTime.UtcNow.AddHours(6);
            shop.AutoPublishBlockReason = ListingPublishLimitErrors.DayLimitBlockReason;
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 10);
        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(0, dispatched);
    }

    [Fact]
    public async Task NineReadyShops_DispatchesAllWithinGateCapacity()
    {
        _gate.Configure(50);
        await SeedReadyShopsAsync(9);

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 50);

        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(9, dispatched);

        await WaitUntilAsync(() => _orchestrator.StartedCount == 9, TimeSpan.FromSeconds(5));
        Assert.Equal(9, _orchestrator.StartedCount);
        Assert.True(_orchestrator.PeakConcurrent >= 2);
    }

    [Fact]
    public async Task SixtyReadyShops_GateFifty_DispatchesExactlyFifty()
    {
        _gate.Configure(50);
        await SeedReadyShopsAsync(60);

        var slowOrchestrator = new CountingAutoPublishOrchestrator(TimeSpan.FromSeconds(30));
        var service = CreateService(slowOrchestrator);
        var settings = CreateSettings(maxConcurrentPublishes: 50);

        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);

        Assert.Equal(50, dispatched);
        Assert.Equal(50, _gate.InFlight);
        Assert.Equal(0, _gate.AvailableSlots);
        Assert.Equal(50, slowOrchestrator.StartedCount);
    }

    [Fact]
    public async Task EarlyPipelineFailure_RollsBackLastAutoPublishAt()
    {
        _gate.Configure(10);
        var shopIds = await SeedReadyShopsAsync(1, returnShopIds: true);
        var shopId = shopIds[0];
        var previousLastAutoPublishAt = DateTime.UtcNow.AddHours(-2);

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstAsync(s => s.Id == shopId);
            shop.LastAutoPublishAt = previousLastAutoPublishAt;
            await db.SaveChangesAsync();
        }

        var failingOrchestrator = new ThrowingAutoPublishOrchestrator(
            new InvalidOperationException("Seller busy simulation"));
        var service = CreateService(failingOrchestrator);
        var settings = CreateSettings(maxConcurrentPublishes: 10);

        var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(1, dispatched);

        await WaitUntilAsync(() => _gate.InFlight == 0, TimeSpan.FromSeconds(5));

        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var restored = await verifyDb.Shops.AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => s.LastAutoPublishAt)
            .FirstAsync();

        Assert.NotNull(restored);
        Assert.Equal(
            previousLastAutoPublishAt,
            restored!.Value,
            precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DispatchTrigger_WakesWaitBeforeDelayExpires()
    {
        var service = CreateService();
        var method = typeof(ListingAutoPublishBackgroundService).GetMethod(
            "WaitForNextRoundAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var waitTask = (Task)method!.Invoke(
            service,
            [TimeSpan.FromMinutes(5), CancellationToken.None])!;

        await Task.Delay(50);
        _trigger.Signal();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task InnerDispatchLoop_BackfillsWithinSameWakeWhenSlotsFree()
    {
        _gate.Configure(3);
        await SeedReadyShopsAsync(6);

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 3);

        var totalDispatched = 0;
        for (var round = 0; round < 10; round++)
        {
            var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
            totalDispatched += dispatched;
            if (dispatched <= 0 || _gate.AvailableSlots <= 0)
            {
                break;
            }
        }

        Assert.Equal(3, totalDispatched);
        Assert.Equal(3, _orchestrator.StartedCount);

        await WaitUntilAsync(() => _gate.InFlight == 0, TimeSpan.FromSeconds(8));

        for (var round = 0; round < 10; round++)
        {
            var dispatched = await InvokeDispatchAsync(service, settings, CancellationToken.None);
            totalDispatched += dispatched;
            if (dispatched <= 0 || _gate.AvailableSlots <= 0)
            {
                break;
            }
        }

        Assert.Equal(6, totalDispatched);
        Assert.Equal(6, _orchestrator.StartedCount);
    }

    [Fact]
    public async Task GateCapacityThree_OnlyStartsThreeUntilBackfill()
    {
        _gate.Configure(3);
        await SeedReadyShopsAsync(6);

        var service = CreateService();
        var settings = CreateSettings(maxConcurrentPublishes: 3);

        var firstRound = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(3, firstRound);
        Assert.Equal(3, _orchestrator.StartedCount);

        await WaitUntilAsync(() => _gate.InFlight == 0, TimeSpan.FromSeconds(8));

        var secondRound = await InvokeDispatchAsync(service, settings, CancellationToken.None);
        Assert.Equal(3, secondRound);
        Assert.Equal(6, _orchestrator.StartedCount);
    }

    [Fact]
    public void SettingKeys_NoMaxConcurrentShops()
    {
        Assert.DoesNotContain(
            typeof(ListingAutoPublishSettingKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Select(f => f.Name),
            name => string.Equals(name, "MaxConcurrentShops", StringComparison.Ordinal));
    }

    private ListingAutoPublishBackgroundService CreateService(
        IListingAutoPublishOrchestrator? orchestrator = null)
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        if (orchestrator is not null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_ => new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);
            services.AddScoped<IApplicationDbContext>(sp =>
                new ApplicationDbContext(sp.GetRequiredService<DbContextOptions<ApplicationDbContext>>()));
            services.AddScoped<IListingAutoPublishOrchestrator>(_ => orchestrator);
            var customProvider = services.BuildServiceProvider();
            scopeFactory = customProvider.GetRequiredService<IServiceScopeFactory>();
        }

        return new ListingAutoPublishBackgroundService(
            scopeFactory,
            NullLogger<ListingAutoPublishBackgroundService>.Instance,
            _gate,
            new ListingShopPublishLease(),
            _trigger);
    }

    private async Task<int> InvokeDispatchAsync(
        ListingAutoPublishBackgroundService service,
        object settings,
        CancellationToken ct)
    {
        var method = typeof(ListingAutoPublishBackgroundService).GetMethod(
            "DispatchReadyShopsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<int>)method!.Invoke(service, [settings, ct])!;
        return await task;
    }

    private object CreateSettings(int maxConcurrentPublishes)
    {
        var type = typeof(ListingAutoPublishBackgroundService)
            .GetNestedType("AutoPublishJobSettings", BindingFlags.NonPublic);
        Assert.NotNull(type);

        return Activator.CreateInstance(
            type!,
            true,
            maxConcurrentPublishes,
            120,
            0)!;
    }

    private async Task<List<Guid>> SeedReadyShopsAsync(int shopCount, bool returnShopIds = false)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var ownerId = Guid.NewGuid();
        var shopIds = new List<Guid>();
        for (var i = 0; i < shopCount; i++)
        {
            var shopId = Guid.NewGuid();
            if (returnShopIds)
            {
                shopIds.Add(shopId);
            }

            var spiId = Guid.NewGuid();
            db.Shops.Add(new Shop
            {
                Id = shopId,
                OwnerId = ownerId,
                Name = $"Shop {i}",
                PlatformId = Guid.NewGuid(),
                Status = ShopStatuses.Active,
                AutoPublishEnabled = true,
                FourSellerShopId = $"fs-{i}",
                LastAutoPublishAt = DateTime.UtcNow.AddHours(-2),
            });
            db.SupplierProductInfos.Add(new SupplierProductInfo
            {
                Id = spiId,
                SourceUrlKey = $"key-{spiId:N}",
                SizeChartImageUrl = "https://example.com/chart.png",
            });
            db.Listings.Add(new Listing
            {
                Id = Guid.NewGuid(),
                ShopId = shopId,
                OwnerId = ownerId,
                PageType = ListingPageTypes.Draft,
                ProductName = $"Draft {i}",
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                SupplierProductInfoId = spiId,
            });
        }

        await db.SaveChangesAsync();
        return shopIds;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }

    private sealed class ThrowingAutoPublishOrchestrator : IListingAutoPublishOrchestrator
    {
        private readonly Exception _exception;

        public ThrowingAutoPublishOrchestrator(Exception exception) => _exception = exception;

        public Task<Guid> RunAsync(Guid listingId, CancellationToken cancellationToken = default) =>
            Task.FromException<Guid>(_exception);
    }

    private sealed class CountingAutoPublishOrchestrator : IListingAutoPublishOrchestrator
    {
        private readonly TimeSpan _delay;
        private int _concurrent;

        public CountingAutoPublishOrchestrator(TimeSpan delay) => _delay = delay;

        public int StartedCount => Volatile.Read(ref _startedCount);

        public int PeakConcurrent => Volatile.Read(ref _peakConcurrent);

        private int _startedCount;
        private int _peakConcurrent;

        public async Task<Guid> RunAsync(Guid listingId, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _concurrent);
            UpdatePeak(current);

            Interlocked.Increment(ref _startedCount);
            try
            {
                await Task.Delay(_delay, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }

            return Guid.NewGuid();
        }

        private void UpdatePeak(int current)
        {
            while (true)
            {
                var peak = _peakConcurrent;
                if (current <= peak)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _peakConcurrent, current, peak) == peak)
                {
                    return;
                }
            }
        }
    }
}
