using Application.DTOs.Order;
using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Unit tests for FindOrderByPurchaseMark helpers (static / index).
/// </summary>
public class PurchaseBuyerAttributionTests
{
    [Fact]
    public void FindOrderByPurchaseMark_E2_OnlyEmptyOrders()
    {
        var empty = Order("EMPTY");
        empty.FulfilPurchasedAt = DateTime.UtcNow.AddMinutes(-5);
        OrderFulfilTimeline.Stamp(
            empty,
            OrderFulfilTimeline.Milestone.FulfilPurchased,
            empty.FulfilPurchasedAt,
            OrderFulfilTimeline.Source.Manual);

        var full = Order("FULL");
        full.FulfilPurchasedAt = DateTime.UtcNow.AddMinutes(-3);
        OrderFulfilTimeline.Stamp(
            full,
            OrderFulfilTimeline.Milestone.FulfilPurchased,
            full.FulfilPurchasedAt,
            OrderFulfilTimeline.Source.Manual);
        full.SupplierPurchases =
        [
            new SupplierPurchase
            {
                Id = Guid.NewGuid(),
                Source = SupplierPurchaseSources.Shein,
                SupplierUnpaid = false,
                SupplierStatus = SupplierStatusGroups.Processing,
                SupplierOrderNo = "GSU-1",
                CreatedAt = DateTime.UtcNow,
            }
        ];

        var payAt = DateTime.UtcNow;
        var shein = new SheinOrderDto
        {
            Billno = "GSU-2",
            SheinPaidAt = payAt,
            OrderedAt = payAt,
        };

        var index = OrderSyncService.PurchaseMarkIndex.Empty;
        var (picked, _) = OrderSyncService.FindOrderByPurchaseMark([empty, full], shein, index);
        Assert.Same(empty, picked);
    }

    [Fact]
    public void PurchaseMarkIndex_Empty_IsEmpty()
    {
        Assert.True(OrderSyncService.PurchaseMarkIndex.Empty.IsEmpty);
    }

    private static Order Order(string platformId) => new()
    {
        Id = Guid.NewGuid(),
        PlatformOrderId = platformId,
        BuyerPhone = "8312295220",
        PostalCode = "93960",
        OrderCreateTime = DateTime.UtcNow.AddHours(-2),
        Items = [new OrderItem { SellerSku = "sku" }],
        SupplierPurchases = [],
    };
}
