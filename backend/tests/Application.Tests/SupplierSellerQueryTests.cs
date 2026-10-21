using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class SupplierSellerQueryTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ShopA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ShopB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Order1 = Guid.Parse("01010101-0101-0101-0101-010101010101");
    private static readonly Guid Order2 = Guid.Parse("02020202-0202-0202-0202-020202020202");

    [Fact]
    public void DisplayTitle_FallsBackToSoldByTips()
    {
        Assert.Equal("Real Shop", SupplierSellerQuery.DisplayTitle("Real Shop", "Sold by kcruab-kp"));
        Assert.Equal("kcruab-kp", SupplierSellerQuery.DisplayTitle(null, "Sold by kcruab-kp"));
        Assert.Null(SupplierSellerQuery.DisplayTitle("  ", null));
    }

    [Fact]
    public void MatchesSearch_UsesTitleCodeAndParsedTips()
    {
        Assert.True(SupplierSellerQuery.MatchesSearch(null, "SC01", "Sold by kcruab-kp", "kcruab"));
        Assert.True(SupplierSellerQuery.MatchesSearch("Alpha Store", "SC01", null, "alpha"));
        Assert.True(SupplierSellerQuery.MatchesSearch(null, "SC01", null, "sc01"));
        Assert.False(SupplierSellerQuery.MatchesSearch("Alpha", "SC01", null, "beta"));
        Assert.True(SupplierSellerQuery.MatchesSearch("Alpha", "SC01", null, null));
    }

    [Fact]
    public void MatchesPlatform_IsCaseInsensitive()
    {
        Assert.True(SupplierSellerQuery.MatchesPlatform("SHEIN", "shein"));
        Assert.False(SupplierSellerQuery.MatchesPlatform("SHEIN", "TEMU"));
        Assert.True(SupplierSellerQuery.MatchesPlatform("SHEIN", null));
    }

    [Fact]
    public void NormalizePaging_ClampsPageAndSize()
    {
        Assert.Equal((1, 20, 0), SupplierSellerQuery.NormalizePaging(0, 0));
        Assert.Equal((2, 20, 20), SupplierSellerQuery.NormalizePaging(2, 20));
        Assert.Equal((1, 100, 0), SupplierSellerQuery.NormalizePaging(1, 500));
    }

    [Fact]
    public void IsOrderStatusOk_DropsCancelAndRefund()
    {
        Assert.False(SupplierSellerQuery.IsOrderStatusOk("CANCELLED", "completed"));
        Assert.False(SupplierSellerQuery.IsOrderStatusOk("REFUND", "completed"));
        Assert.False(SupplierSellerQuery.IsOrderStatusOk(null, "cancelled"));
        Assert.True(SupplierSellerQuery.IsOrderStatusOk("AWAITING_SHIPMENT", "completed"));
    }

    [Fact]
    public void IsMoneyEligibleShop_RequiresActiveAndBank()
    {
        Assert.True(SupplierSellerQuery.IsMoneyEligibleShop(ShopStatuses.Active, Guid.NewGuid()));
        Assert.False(SupplierSellerQuery.IsMoneyEligibleShop(ShopStatuses.Active, null));
        Assert.False(SupplierSellerQuery.IsMoneyEligibleShop(ShopStatuses.Deactivated, Guid.NewGuid()));
    }

    [Fact]
    public void ResolveUtcRange_CustomDates_AreHalfOpenVietnamDays()
    {
        var utcNow = new DateTime(2026, 8, 6, 3, 0, 0, DateTimeKind.Utc);
        var (start, end, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(
            utcNow,
            period: "day",
            fromDate: "2026-08-01",
            toDate: "2026-08-05");

        Assert.Equal(new DateOnly(2026, 8, 1), fromLocal);
        Assert.Equal(new DateOnly(2026, 8, 5), toLocal);
        Assert.Equal(TimeSpan.FromDays(5), end - start);
        Assert.True(start.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public void ResolveUtcRange_DefaultPeriodIsRolling30Days()
    {
        var utcNow = new DateTime(2026, 8, 6, 3, 0, 0, DateTimeKind.Utc);
        var (_, _, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(utcNow, null, null, null);
        Assert.Equal(new DateOnly(2026, 8, 6), toLocal);
        Assert.Equal(new DateOnly(2026, 7, 8), fromLocal);
    }

    [Fact]
    public void ResolveUtcRange_MonthPeriodEndsTodayNotEndOfMonth()
    {
        var utcNow = new DateTime(2026, 8, 6, 3, 0, 0, DateTimeKind.Utc);
        var (_, _, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(utcNow, "month", null, null);
        Assert.Equal(new DateOnly(2026, 8, 1), fromLocal);
        Assert.Equal(new DateOnly(2026, 8, 6), toLocal);
    }

    [Fact]
    public void ResolveUtcRange_ClampsFutureToDateToToday()
    {
        var utcNow = new DateTime(2026, 8, 6, 3, 0, 0, DateTimeKind.Utc);
        var (_, _, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(
            utcNow,
            null,
            "2026-08-01",
            "2026-08-31");
        Assert.Equal(new DateOnly(2026, 8, 1), fromLocal);
        Assert.Equal(new DateOnly(2026, 8, 6), toLocal);
    }

    [Fact]
    public void AggregateMoney_MatchesSkuCaseInsensitiveAndDedupsOrders()
    {
        var skuMap = SupplierSellerQuery.BuildSkuSellerMap(
        [
            (Owner, "SKU-A", ShopA),
            (Owner, "SKU-B", ShopA),
            (Owner, "SKU-C", ShopB),
            (Other, "SKU-A", ShopB)
        ]);

        var aggs = SupplierSellerQuery.AggregateMoney(
            [
                new SupplierSellerOrderLine(Order1, Owner, "sku-a", 2, 10m),
                new SupplierSellerOrderLine(Order1, Owner, "SKU-B", 1, 5m),
                new SupplierSellerOrderLine(Order2, Owner, "sku-c", 3, 4m),
                new SupplierSellerOrderLine(Order2, Owner, "NOPE", 9, 9m),
                new SupplierSellerOrderLine(Order2, Other, "sku-a", 1, 100m)
            ],
            skuMap);

        Assert.Equal(1, aggs[ShopA].OrderCount);
        Assert.Equal(3, aggs[ShopA].ItemQty);
        Assert.Equal(25m, aggs[ShopA].ItemRevenue);
        Assert.Equal(1, aggs[ShopB].OrderCount);
        Assert.Equal(4, aggs[ShopB].ItemQty);
        Assert.Equal(112m, aggs[ShopB].ItemRevenue);
    }

    [Fact]
    public void BuildSkuSellerMap_FirstLinkWins()
    {
        var map = SupplierSellerQuery.BuildSkuSellerMap(
        [
            (Owner, "SKU-A", ShopA),
            (Owner, "sku-a", ShopB)
        ]);

        Assert.Equal(ShopA, map[(Owner, "sku-a")]);
        Assert.Single(map);
    }
}
