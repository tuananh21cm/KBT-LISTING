using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class OrderSupplierCostAggregateTests
{
    [Fact]
    public void TrueSpend_AddsCardWalletAndClub()
    {
        Assert.Equal(25.43m, OrderSupplierCost.TrueSpend(20m, 5m, 0.43m));
        Assert.Equal(25m, OrderSupplierCost.TrueSpend(20m, 5m));
        Assert.Equal(5m, OrderSupplierCost.TrueSpend(null, 5m));
        Assert.Equal(20m, OrderSupplierCost.TrueSpend(20m, null));
        Assert.Equal(0.43m, OrderSupplierCost.TrueSpend(null, null, 0.43m));
        Assert.Null(OrderSupplierCost.TrueSpend(null, null));
        Assert.Null(OrderSupplierCost.TrueSpend(null, null, null));
    }

    [Fact]
    public void ApplyAggregates_WritesChargedColumnsAndTrueSpend()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderStatus = "to_ship",
            Currency = "USD",
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    Source = SupplierPurchaseSources.Shein,
                    MatchState = SupplierPurchaseMatchStates.Matched,
                    SupplierOrderNo = "A",
                    SupplierTotal = 10m,
                    SupplierWalletPrice = 2m,
                    SupplierMembershipDiscount = 0.43m,
                    SupplierSubtotal = 12m,
                    SupplierStatus = SupplierStatusGroups.Processing
                },
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    Source = SupplierPurchaseSources.Shein,
                    MatchState = SupplierPurchaseMatchStates.Matched,
                    SupplierOrderNo = "B",
                    SupplierTotal = 5m,
                    SupplierWalletPrice = 0m,
                    SupplierSubtotal = 5m,
                    SupplierStatus = SupplierStatusGroups.Refunded
                }
            ]
        };

        OrderSupplierCost.ApplyAggregates(order);

        var purchases = order.SupplierPurchases!.ToList();
        Assert.True(purchases[0].IsCharged);
        Assert.False(purchases[1].IsCharged); // refunded
        Assert.Equal(10m, order.ChargedSupplierTotal);
        Assert.Equal(2m, order.ChargedSupplierWalletPrice);
        Assert.Equal(0.43m, order.ChargedSupplierMembershipDiscount);
        Assert.Equal(12.43m, order.ChargedTrueSpend);
        Assert.Equal(12m, order.ChargedSupplierSubtotal);
    }

    [Fact]
    public void GrossPurchaseCostFromBreakdown_SubtotalShipTax()
    {
        var purchase = new SupplierPurchase
        {
            SupplierSubtotal = 8.75m,
            SupplierShippingFee = 3.99m,
            SupplierTax = 1.14m,
        };
        Assert.Equal(13.88m, OrderSupplierCost.GrossPurchaseCostFromBreakdown(purchase));
    }

    [Fact]
    public void GrossPurchaseCostFromLedger_AddsDiscountsBack()
    {
        Assert.Equal(13.88m, OrderSupplierCost.GrossPurchaseCostFromLedger(13.01m, null, 0.87m, null, null));
    }

    [Fact]
    public void ResolveGrossPurchaseCost_PrefersBreakdownOverLedger()
    {
        var purchase = new SupplierPurchase
        {
            SupplierSubtotal = 8.75m,
            SupplierShippingFee = 3.99m,
            SupplierTax = 1.14m,
        };
        Assert.Equal(
            13.88m,
            OrderSupplierCost.ResolveGrossPurchaseCost(purchase, 13.01m, null, 0m, null, null));
    }

    [Fact]
    public void ResolveGrossPurchaseCost_Order_UsesChargedBreakdown()
    {
        var order = new Order
        {
            ChargedSupplierSubtotal = 8.75m,
            ChargedSupplierShippingFee = 3.99m,
            ChargedSupplierTax = 1.14m,
            ChargedSupplierTotal = 13.01m,
            ChargedSupplierCouponPrice = 0.87m,
        };
        Assert.Equal(13.88m, OrderSupplierCost.ResolveGrossPurchaseCost(order));
    }

    [Fact]
    public void ResolveGrossPurchaseCost_Order_FallsBackToLedger()
    {
        var order = new Order
        {
            ChargedSupplierTotal = 13.01m,
            ChargedSupplierCouponPrice = 0.87m,
        };
        Assert.Equal(13.88m, OrderSupplierCost.ResolveGrossPurchaseCost(order));
    }

    [Fact]
    public void ResolveOrderTotalCost_UsesGrossPlusHandlingFees()
    {
        var order = new Order
        {
            ChargedSupplierSubtotal = 8.75m,
            ChargedSupplierShippingFee = 3.99m,
            ChargedSupplierTax = 1.14m,
            BuyTrackAmount = 0.5m,
            ThankCardAmount = 1m,
        };
        Assert.Equal(15.38m, OrderSupplierCost.ResolveOrderTotalCost(order));
    }

    /// <summary>
    /// Đơn 577592679686312424: snapshot rỗng sạch trong khi dòng mua GSU1SH28T00NJKR vẫn đủ tiền —
    /// card đơn từng hiện Cost/Profit là "—" trong khi drawer ngay cạnh hiện đủ breakdown.
    /// </summary>
    [Fact]
    public void ResolveGrossPurchaseCost_Order_FallsBackToChargedLinksWhenSnapshotEmpty()
    {
        var order = new Order
        {
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    Source = SupplierPurchaseSources.Shein,
                    MatchState = SupplierPurchaseMatchStates.Matched,
                    SupplierOrderNo = "GSU1SH28T00NJKR",
                    IsCharged = true,
                    SupplierSubtotal = 15.14m,
                    SupplierShippingFee = 3.99m,
                    SupplierTax = 1.25m,
                    SupplierCouponPrice = 2.27m,
                    SupplierTotal = 18.11m,
                }
            ]
        };

        Assert.Null(order.ChargedSupplierSubtotal);
        Assert.Equal(20.38m, OrderSupplierCost.ResolveGrossPurchaseCost(order));
        Assert.Equal(20.38m, OrderSupplierCost.ResolveOrderTotalCost(order));
    }

    /// <summary>
    /// Lưới an toàn không được nới tập được tính tiền: billno đã hoàn vẫn phải đứng ngoài giá mua.
    /// </summary>
    [Fact]
    public void ResolveGrossPurchaseCost_Order_FallbackSkipsUnchargedLinks()
    {
        var order = new Order
        {
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    Source = SupplierPurchaseSources.Shein,
                    MatchState = SupplierPurchaseMatchStates.Matched,
                    SupplierOrderNo = "REFUNDED",
                    IsCharged = false,
                    SupplierSubtotal = 99m,
                    SupplierTotal = 99m,
                }
            ]
        };

        Assert.Null(OrderSupplierCost.ResolveGrossPurchaseCost(order));
        Assert.Null(OrderSupplierCost.ResolveOrderTotalCost(order));
    }

    /// <summary>
    /// Tập link tường minh thắng navigation — dòng vừa được gắn sang đơn khác không được tính cho
    /// đơn cũ, kể cả khi navigation của đơn cũ chưa kịp fix-up.
    /// </summary>
    [Fact]
    public void ApplyAggregates_WithExplicitLinks_ClearsWhenLinkSetIsEmpty()
    {
        var moved = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            Source = SupplierPurchaseSources.Shein,
            MatchState = SupplierPurchaseMatchStates.Matched,
            SupplierOrderNo = "MOVED",
            IsCharged = true,
            SupplierSubtotal = 12m,
            SupplierTotal = 12m,
        };
        var order = new Order
        {
            ChargedSupplierSubtotal = 12m,
            ChargedSupplierTotal = 12m,
            SupplierPurchases = [moved]
        };

        OrderSupplierCost.ApplyAggregates(order, []);

        Assert.Null(order.ChargedSupplierSubtotal);
        Assert.Null(order.ChargedSupplierTotal);
        Assert.Null(order.ChargedTrueSpend);

        // Sau khi EF fix-up navigation theo khoá ngoại vừa đổi, lưới an toàn cũng không còn gì để
        // cộng — cùng một khoản chi không nằm trên hai đơn.
        order.SupplierPurchases.Remove(moved);
        Assert.Null(OrderSupplierCost.ResolveOrderTotalCost(order));
    }

    [Fact]
    public void ResolveOrderTotalCost_UserExample_ProfitEqualsRevenueMinusCost()
    {
        var order = new Order
        {
            ChargedSupplierSubtotal = 15.34m,
            ChargedSupplierShippingFee = 3.99m,
            ChargedSupplierTax = 0.51m,
            BuyTrackAmount = 0m,
            ThankCardAmount = 0m,
        };
        var cost = OrderSupplierCost.ResolveOrderTotalCost(order);
        Assert.Equal(19.84m, cost);
        Assert.Equal(19.84m, 39.68m - cost!.Value);
    }
}
