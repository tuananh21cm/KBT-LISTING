using Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishCancellationCoordinatorTests
{
    private static ListingAutoPublishCancellationCoordinator CreateCoordinator()
    {
        return new ListingAutoPublishCancellationCoordinator(
            new ListingShopPublishLease(),
            NullLogger<ListingAutoPublishCancellationCoordinator>.Instance);
    }

    [Fact]
    public void CancelAll_CancelsLinkedRunToken()
    {
        var coordinator = CreateCoordinator();
        var runId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        using var outer = new CancellationTokenSource();
        var linked = coordinator.LinkRun(runId, shopId, outer.Token);

        coordinator.CancelAll("test_disable");

        Assert.True(linked.IsCancellationRequested);
    }

    [Fact]
    public void CancelShop_CancelsOnlyTargetShop()
    {
        var coordinator = CreateCoordinator();
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        using var outer = new CancellationTokenSource();
        var tokenA = coordinator.LinkRun(Guid.NewGuid(), shopA, outer.Token);
        var tokenB = coordinator.LinkRun(Guid.NewGuid(), shopB, outer.Token);

        coordinator.CancelShop(shopA, "shop_disabled");

        Assert.True(tokenA.IsCancellationRequested);
        Assert.False(tokenB.IsCancellationRequested);
    }

    [Fact]
    public void CancelShop_BeforeLinkRun_CancelsNewLinkedToken()
    {
        var coordinator = CreateCoordinator();
        var shopId = Guid.NewGuid();
        coordinator.CancelShop(shopId, "shop_disabled");

        using var outer = new CancellationTokenSource();
        var linked = coordinator.LinkRun(Guid.NewGuid(), shopId, outer.Token);

        Assert.True(linked.IsCancellationRequested);
    }

    [Fact]
    public void ResetShop_AllowsNewRunAfterReEnable()
    {
        var coordinator = CreateCoordinator();
        var shopId = Guid.NewGuid();
        coordinator.CancelShop(shopId, "shop_disabled");
        coordinator.ResetShop(shopId);

        using var outer = new CancellationTokenSource();
        var linked = coordinator.LinkRun(Guid.NewGuid(), shopId, outer.Token);

        Assert.False(linked.IsCancellationRequested);
    }

    [Fact]
    public void UnregisterRun_DoesNotCancelShopForOtherRuns()
    {
        var coordinator = CreateCoordinator();
        var shopId = Guid.NewGuid();
        using var outer = new CancellationTokenSource();
        var run1 = Guid.NewGuid();
        var run2 = Guid.NewGuid();
        var token1 = coordinator.LinkRun(run1, shopId, outer.Token);
        var token2 = coordinator.LinkRun(run2, shopId, outer.Token);

        coordinator.UnregisterRun(run1, shopId);

        Assert.False(token1.IsCancellationRequested);
        Assert.False(token2.IsCancellationRequested);
    }

    [Fact]
    public void CancelShop_ForceReleasesShopLease()
    {
        var shopLease = new ListingShopPublishLease();
        var coordinator = new ListingAutoPublishCancellationCoordinator(
            shopLease,
            NullLogger<ListingAutoPublishCancellationCoordinator>.Instance);
        var shopId = Guid.NewGuid();

        Assert.True(shopLease.TryBegin(shopId, out var handle));
        Assert.NotNull(handle);

        coordinator.CancelShop(shopId, "shop_disabled");

        Assert.True(shopLease.TryBegin(shopId, out var secondHandle));
        secondHandle!.Dispose();
    }
}

public class ListingAutoPublishConcurrencyGateTests
{
    [Fact]
    public async Task ConfigureWhileHoldingPermit_DoesNotLoseCapacity()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(2);
        await gate.WaitAsync();
        gate.Configure(1);
        gate.Release();
        await gate.WaitAsync();
        gate.Release();
    }

    [Fact]
    public void AvailableSlots_ReflectsCapacityAndInFlight()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(5);

        Assert.Equal(5, gate.Capacity);
        Assert.Equal(0, gate.InFlight);
        Assert.Equal(5, gate.AvailableSlots);
    }

    [Fact]
    public async Task WaitAndRelease_UpdatesAvailableSlots()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(3);

        await gate.WaitAsync();
        await gate.WaitAsync();

        Assert.Equal(2, gate.InFlight);
        Assert.Equal(1, gate.AvailableSlots);

        gate.Release();
        Assert.Equal(1, gate.InFlight);
        Assert.Equal(2, gate.AvailableSlots);
    }

    [Fact]
    public void TryAcquire_WhenAtCapacity_ReturnsFalse()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(2);

        Assert.True(gate.TryAcquire());
        Assert.True(gate.TryAcquire());
        Assert.False(gate.TryAcquire());
        Assert.Equal(2, gate.InFlight);
        Assert.Equal(0, gate.AvailableSlots);
    }

    [Fact]
    public void TryAcquire_UpdatesInFlightSynchronously()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(3);

        Assert.True(gate.TryAcquire());
        Assert.Equal(1, gate.InFlight);
        Assert.Equal(2, gate.AvailableSlots);

        gate.Release();
        Assert.Equal(0, gate.InFlight);
        Assert.Equal(3, gate.AvailableSlots);
    }

    [Fact]
    public void TryAcquire_AndRelease_MaintainsCapacityInvariant()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(5);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(gate.TryAcquire());
        }

        Assert.False(gate.TryAcquire());
        Assert.Equal(5, gate.InFlight);

        for (var i = 0; i < 5; i++)
        {
            gate.Release();
        }

        Assert.Equal(0, gate.InFlight);
        Assert.Equal(5, gate.AvailableSlots);
    }

    [Fact]
    public void Configure_DeferredUntilInFlightZero()
    {
        var gate = new ListingAutoPublishConcurrencyGate();
        gate.Configure(2);
        gate.WaitAsync().GetAwaiter().GetResult();

        gate.Configure(10);
        Assert.Equal(2, gate.Capacity);
        Assert.Equal(1, gate.AvailableSlots);

        gate.Release();
        Assert.Equal(10, gate.Capacity);
        Assert.Equal(10, gate.AvailableSlots);
    }
}

public class AutoPublishSystemConfigBuilderTests
{
    [Fact]
    public void FromSettings_ParsesDefaults()
    {
        var dto = AutoPublishSystemConfigBuilder.FromSettings(
            new Dictionary<string, string?>
            {
                [ListingAutoPublishSettingKeys.Enabled] = "true",
                [ListingAutoPublishSettingKeys.DefaultIntervalSeconds] = "90",
                [ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes] = "15",
            });

        Assert.True(dto.SystemAutoPublishEnabled);
        Assert.Equal(90, dto.DefaultIntervalSeconds);
        Assert.Equal(15, dto.DefaultMinDraftAgeMinutes);
    }
}
