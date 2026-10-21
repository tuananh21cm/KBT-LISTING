using Application.DTOs.Order;
using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Xunit;

namespace Application.Tests;

public class SheinLiveShipmentTests
{
    [Fact]
    public void LooksLive_WhenListStatusIsShipped_EvenIfGroupSaysRefunded()
    {
        var shein = new SheinOrderDto
        {
            SheinStatus = "5",
            SheinStatusGroup = SupplierStatusGroups.Refunded,
            TrackingNo = "GFUS01068114822657"
        };

        Assert.True(SheinLiveShipment.LooksLive(shein));
        Assert.Equal(SupplierStatusGroups.Shipped, SheinLiveShipment.RecoverGroup(shein));
    }

    [Fact]
    public void LooksLive_False_WhenRawIsReallyRefunded()
    {
        var shein = new SheinOrderDto
        {
            SheinStatus = "13",
            SheinStatusGroup = SupplierStatusGroups.Refunded
        };

        Assert.False(SheinLiveShipment.LooksLive(shein));
    }

    [Fact]
    public void HasSheinIncrementalDelta_True_WhenStoredTerminalWasRecovered()
    {
        var before = OrderSyncService.SheinPurchaseSnapshot.Of(new SupplierPurchase
        {
            SupplierStatus = SupplierStatusGroups.Refunded,
            SupplierTotal = 15.74m,
            TrackingNo = "GFUS01068114822657"
        });
        var purchase = new SupplierPurchase
        {
            SupplierStatus = SupplierStatusGroups.Shipped,
            SupplierTotal = 15.74m,
            TrackingNo = "GFUS01068114822657"
        };
        var shein = new SheinOrderDto
        {
            SheinStatus = "5",
            SheinStatusGroup = SupplierStatusGroups.Refunded,
            Total = 15.74m,
            TrackingNo = "GFUS01068114822657"
        };
        var order = new Order { FulfilPurchasedAt = DateTime.UtcNow.AddHours(-2) };

        Assert.True(OrderSyncService.HasSheinIncrementalDelta(purchase, before, shein, order));
    }

    [Fact]
    public void HasSheinIncrementalDelta_True_WhenBreakdownCompletedAfterListSparse()
    {
        var before = OrderSyncService.SheinPurchaseSnapshot.Of(new SupplierPurchase
        {
            SupplierTotal = 13.01m,
            SupplierSubtotal = null,
        });
        var purchase = new SupplierPurchase
        {
            SupplierTotal = 13.01m,
            SupplierSubtotal = 8.75m,
            SupplierShippingFee = 3.99m,
            SupplierTax = 1.14m,
            SupplierCouponPrice = 0.87m,
        };
        var shein = new SheinOrderDto
        {
            Total = 13.01m,
            Subtotal = 8.75m,
            ShippingFee = 3.99m,
            Tax = 1.14m,
            CouponPrice = 0.87m,
        };
        var order = new Order { FulfilPurchasedAt = DateTime.UtcNow.AddHours(-2) };

        Assert.True(OrderSyncService.HasSheinIncrementalDelta(purchase, before, shein, order));
    }
}
