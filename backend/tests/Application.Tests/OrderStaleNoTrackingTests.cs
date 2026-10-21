using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class OrderStaleNoTrackingTests
{
    [Theory]
    [InlineData(null, 72)]
    [InlineData("", 72)]
    [InlineData("abc", 72)]
    [InlineData("0", 72)]
    [InlineData("-1", 72)]
    [InlineData("72", 72)]
    [InlineData("24", 24)]
    [InlineData("720", 720)]
    [InlineData("721", 720)]
    public void ResolveHours_ParsesOrDefaults(string? raw, int expected)
    {
        Assert.Equal(expected, OrderStaleNoTracking.ResolveHours(raw));
    }

    [Fact]
    public void ResolvePurchasedAt_PrefersFulfilStamp()
    {
        var order = new Order
        {
            FulfilPurchasedAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc),
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    OrderedAt = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
                },
            ],
        };

        Assert.Equal(order.FulfilPurchasedAt, OrderStaleNoTracking.ResolvePurchasedAt(order));
    }

    [Fact]
    public void ResolvePurchasedAt_FallsBackToEarliestSupplierClock()
    {
        var order = new Order
        {
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    OrderedAt = new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc),
                },
                new SupplierPurchase
                {
                    CreatedAt = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc),
                },
            ],
        };

        Assert.Equal(
            new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc),
            OrderStaleNoTracking.ResolvePurchasedAt(order));
    }

    [Fact]
    public void IsEligible_True_WhenPurchasedPastThresholdWithoutCarrier()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-72));

        Assert.True(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_False_WhenStillWithinThreshold()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-71));

        Assert.False(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_False_WhenAlreadyNotifiedDoesNotMatter()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-80));
        order.TelegramStaleNoTrackingNotifiedAt = now.AddHours(-1);

        Assert.True(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_False_WhenHasCarrierTracking()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-80));
        order.SupplierPurchases =
        [
            new SupplierPurchase
            {
                TrackingKind = SupplierTrackingKinds.Carrier,
                TrackingNo = "1Z999AA10123456784",
            },
        ];

        Assert.False(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_True_WhenOnlyWarehouseTracking()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-80));
        order.TrackingNo = "C12345678901";
        order.SupplierPurchases =
        [
            new SupplierPurchase
            {
                TrackingKind = SupplierTrackingKinds.Warehouse,
                TrackingNo = "C12345678901",
                WarehouseTrackingNo = "C12345678901",
            },
        ];

        Assert.True(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_False_WhenInTransit()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-80));
        order.PlatformOrderStatus = PlatformOrderStatuses.InTransit;

        Assert.False(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Fact]
    public void IsEligible_False_WhenPendingProcessNotPurchased()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
        };

        Assert.False(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    [Theory]
    [InlineData(PlatformOrderStatuses.Cancelled)]
    [InlineData(PlatformOrderStatuses.Refund)]
    [InlineData(PlatformOrderStatuses.Delivered)]
    [InlineData(PlatformOrderStatuses.AwaitingCollection)]
    public void IsEligible_False_WhenNotPurchasedWaitingTab(string status)
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var order = OpenOrder(now.AddHours(-80));
        order.PlatformOrderStatus = status;

        Assert.False(OrderStaleNoTracking.IsEligible(order, now, 72));
    }

    private static Order OpenOrder(DateTime purchasedAt) =>
        new()
        {
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
            FulfilPurchasedAt = purchasedAt,
        };
}
