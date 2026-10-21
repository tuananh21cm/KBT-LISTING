using Application.Services;
using Domain.Entities;
using Xunit;

namespace Application.Tests;

public class FulfilTransactionListSummaryApplyTests
{
    private static readonly Guid OrderA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrderB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void CardOfRow_Debit_IsPositiveSpend()
    {
        Assert.Equal(10m, FulfilTransactionListSummaryApply.CardOfRow(
            FulfilTransaction.DirectionDebit, 10m));
    }

    [Fact]
    public void CardOfRow_Credit_SubtractsFromSpend()
    {
        Assert.Equal(-3m, FulfilTransactionListSummaryApply.CardOfRow(
            FulfilTransaction.DirectionCredit, 3m));
        Assert.Equal(
            7m,
            FulfilTransactionListSummaryApply.CardOfRow(FulfilTransaction.DirectionDebit, 10m)
            + FulfilTransactionListSummaryApply.CardOfRow(FulfilTransaction.DirectionCredit, 3m));
    }

    [Fact]
    public void CostOfRow_CostUpdate_GrossNccPlusHandlingFees()
    {
        Assert.Equal(13.50m, FulfilTransactionListSummaryApply.CostOfRow(
            FulfilTransaction.SourceCostUpdate, 10m, 2.5m, null, 1m, null, 0m, 0m));
    }

    [Fact]
    public void CostOfRow_UserExample_IncludesCouponAndHandling()
    {
        // Thẻ 13.01 + coupon 0.87 = gross 13.88; + BT 0.5 + TK 1 = 15.38
        Assert.Equal(15.38m, FulfilTransactionListSummaryApply.CostOfRow(
            FulfilTransaction.SourceCostUpdate, 13.01m, null, 0.87m, null, null, 0.5m, 1m));
    }

    [Fact]
    public void CostOfRow_Manual_IsZeroEvenWithAmount()
    {
        Assert.Equal(0m, FulfilTransactionListSummaryApply.CostOfRow(
            FulfilTransaction.SourceManual, 40m, 1m, 0.5m, 1m, 0.25m, 0.5m, 1m));
    }

    [Fact]
    public void CostOfRow_NullOptionalLedgerParts_UsesZero()
    {
        Assert.Equal(10m, FulfilTransactionListSummaryApply.CostOfRow(
            FulfilTransaction.SourceCostUpdate, 10m, null, null, null, null, 0m, 0m));
    }

    [Fact]
    public void TrueSpendOfRow_StillCardWalletClub()
    {
        Assert.Equal(13.50m, FulfilTransactionListSummaryApply.TrueSpendOfRow(
            FulfilTransaction.SourceCostUpdate, 10m, 2.5m, 1m));
        Assert.Equal(0m, FulfilTransactionListSummaryApply.TrueSpendOfRow(
            FulfilTransaction.SourceManual, 10m, 2.5m, 1m));
    }

    [Fact]
    public void SumUniqueOrderProfit_JointShipment_CountsOnce()
    {
        var ledgerOrderIds = new[] { OrderA, OrderA };
        var orders = new Dictionary<Guid, (decimal Revenue, decimal? GrossPurchaseCost, decimal BuyTrack, decimal ThankCard)>
        {
            [OrderA] = (Revenue: 20m, GrossPurchaseCost: 12m, BuyTrack: 1m, ThankCard: 1m),
        };

        var profit = FulfilTransactionListSummaryApply.SumUniqueOrderProfit(ledgerOrderIds, orders);

        Assert.Equal(6m, profit);
    }

    [Fact]
    public void SumUniqueOrderProfit_TwoOrders_AddsEach()
    {
        var ledgerOrderIds = new[] { OrderA, OrderB };
        var orders = new Dictionary<Guid, (decimal Revenue, decimal? GrossPurchaseCost, decimal BuyTrack, decimal ThankCard)>
        {
            [OrderA] = (Revenue: 20m, GrossPurchaseCost: 12m, BuyTrack: 0m, ThankCard: 0m),
            [OrderB] = (Revenue: 15m, GrossPurchaseCost: 10m, BuyTrack: 0m, ThankCard: 0m),
        };

        var profit = FulfilTransactionListSummaryApply.SumUniqueOrderProfit(ledgerOrderIds, orders);

        Assert.Equal(13m, profit);
    }

    [Fact]
    public void SumUniqueOrderProfit_NoCostYet_Skipped()
    {
        var ledgerOrderIds = new[] { OrderA };
        var orders = new Dictionary<Guid, (decimal Revenue, decimal? GrossPurchaseCost, decimal BuyTrack, decimal ThankCard)>
        {
            [OrderA] = (Revenue: 20m, GrossPurchaseCost: null, BuyTrack: 0m, ThankCard: 0m),
        };

        var profit = FulfilTransactionListSummaryApply.SumUniqueOrderProfit(ledgerOrderIds, orders);

        Assert.Equal(0m, profit);
    }

    [Fact]
    public void SumUniqueOrderProfit_FeesOnly_UsesOrderBuyTrackThankCard_NotLedgerRows()
    {
        var ledgerOrderIds = new[] { OrderA };
        var orders = new Dictionary<Guid, (decimal Revenue, decimal? GrossPurchaseCost, decimal BuyTrack, decimal ThankCard)>
        {
            [OrderA] = (Revenue: 20m, GrossPurchaseCost: 10m, BuyTrack: 0.5m, ThankCard: 1m),
        };

        var profit = FulfilTransactionListSummaryApply.SumUniqueOrderProfit(ledgerOrderIds, orders);

        Assert.Equal(8.5m, profit);
        Assert.Equal(0m, FulfilTransactionListSummaryApply.CostOfRow(
            FulfilTransaction.SourceManual, 0.5m, null, null, null, null, 0.5m, 1m));
    }

    /// <summary>
    /// Đơn TikTok 1 mã, 2 lần mua Shein (joint / multi-account): Rev $53.69 − Cost $12.62+$10.79 = $30.28,
    /// dù ledger lặp OrderId 2 lần.
    /// </summary>
    [Fact]
    public void SumDistinctOrderProfits_MultiSheinSameOrder_CountsOnce()
    {
        var order = new FulfilTransactionListSummaryApply.OrderProfitRow(
            Id: OrderA,
            SellerNetAmount: 53.69m,
            TotalPaidAmount: 53.69m,
            Amount: 53.69m,
            ChargedSupplierSubtotal: 23.41m,
            ChargedSupplierShippingFee: 0m,
            ChargedSupplierTax: 0m,
            ChargedSupplierRetailDeliveryFee: 0m,
            ChargedSupplierOnTimeGuarantee: 0m,
            ChargedSupplierTotal: 23.41m,
            ChargedSupplierWalletPrice: null,
            ChargedSupplierCouponPrice: null,
            ChargedSupplierMembershipDiscount: null,
            ChargedSupplierPointsDiscount: null,
            BuyTrackAmount: 0m,
            ThankCardAmount: 0m);

        // Simulate loading Orders once even when filter has 2 CostUpdate rows for OrderA.
        var profit = FulfilTransactionListSummaryApply.SumDistinctOrderProfits([order]);

        Assert.Equal(30.28m, profit);
        Assert.Equal(
            30.28m,
            FulfilTransactionListSummaryApply.SumUniqueOrderProfit(
                [OrderA, OrderA],
                new Dictionary<Guid, (decimal, decimal?, decimal, decimal)>
                {
                    [OrderA] = (53.69m, 23.41m, 0m, 0m),
                }));
    }

    [Fact]
    public void SumDistinctOrderProfits_TwoOrders_AddsEach()
    {
        var orders = new[]
        {
            new FulfilTransactionListSummaryApply.OrderProfitRow(
                OrderA, 53.69m, 53.69m, 53.69m,
                23.41m, 0m, 0m, 0m, 0m, 23.41m,
                null, null, null, null, 0m, 0m),
            new FulfilTransactionListSummaryApply.OrderProfitRow(
                OrderB, 40m, 40m, 40m,
                15.12m, 0m, 0m, 0m, 0m, 15.12m,
                null, null, null, null, 0m, 0m),
        };

        Assert.Equal(30.28m + 24.88m, FulfilTransactionListSummaryApply.SumDistinctOrderProfits(orders));
    }
}
