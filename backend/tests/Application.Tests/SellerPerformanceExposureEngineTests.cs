using Application.Services;
using Xunit;

namespace Application.Tests;

public class SellerPerformanceExposureEngineTests
{
    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly To = new(2026, 7, 10);

    [Fact]
    public void ClampToRange_OpenPeriod_UsesReportEnd()
    {
        var clamped = SellerPerformanceExposureEngine.ClampToRange(
            From,
            To,
            startedOn: new DateOnly(2026, 6, 1),
            endedOn: null);

        Assert.Equal(new DateInterval(From, To), clamped);
    }

    [Fact]
    public void ClampToRange_MidPeriodStart_Prorates()
    {
        var clamped = SellerPerformanceExposureEngine.ClampToRange(
            From,
            To,
            startedOn: new DateOnly(2026, 7, 6),
            endedOn: null);

        Assert.NotNull(clamped);
        Assert.Equal(5, clamped!.Value.DayCount);
    }

    [Fact]
    public void ClampToRange_ClosedBeforeRange_ReturnsNull()
    {
        var clamped = SellerPerformanceExposureEngine.ClampToRange(
            From,
            To,
            startedOn: new DateOnly(2026, 5, 1),
            endedOn: new DateOnly(2026, 6, 30));

        Assert.Null(clamped);
    }

    [Fact]
    public void Merge_OverlappingAndAdjacent_DoesNotDoubleCount()
    {
        var days = SellerPerformanceExposureEngine.CountDays(
        [
            new DateInterval(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5)),
            new DateInterval(new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 7)),
            new DateInterval(new DateOnly(2026, 7, 8), new DateOnly(2026, 7, 8)), // adjacent
        ]);

        Assert.Equal(8, days);
    }

    [Fact]
    public void CountShopDays_SumsPerShop_NotUnion()
    {
        // Two shops both active full 10 days → 20 shop-days
        var shopA = new[] { (new DateOnly(2026, 1, 1), (DateOnly?)null) };
        var shopB = new[] { (new DateOnly(2026, 7, 1), (DateOnly?)null) };
        var total =
            SellerPerformanceExposureEngine.CountShopDays(From, To, shopA) +
            SellerPerformanceExposureEngine.CountShopDays(From, To, shopB);

        Assert.Equal(20, total);
    }

    [Fact]
    public void CountSellerOperationalDays_UnionsAcrossShops()
    {
        // Shop A: 1–10, Shop B: 6–10 → union still 10 seller-days
        var periods = new (DateOnly, DateOnly?)[]
        {
            (new DateOnly(2026, 7, 1), null),
            (new DateOnly(2026, 7, 6), null)
        };

        var sellerDays = SellerPerformanceExposureEngine.CountSellerOperationalDays(
            From,
            To,
            periods);

        Assert.Equal(10, sellerDays);
    }

    [Fact]
    public void CountSellerOperationalDays_ToggleOffReducesExposure()
    {
        // Active Jul 1–5 only
        var periods = new (DateOnly, DateOnly?)[]
        {
            (new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5))
        };

        Assert.Equal(
            5,
            SellerPerformanceExposureEngine.CountSellerOperationalDays(From, To, periods));
    }

    [Fact]
    public void CountOperationalShops_IgnoresZeroDayShops()
    {
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var count = SellerPerformanceExposureEngine.CountOperationalShops(
            From,
            To,
            [
                (shopA, new DateOnly(2026, 7, 1), null),
                (shopB, new DateOnly(2026, 8, 1), null) // after range
            ]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void AveragePerDay_NullWhenNoExposure()
    {
        Assert.Null(SellerPerformanceExposureEngine.AveragePerDay(5, 0));
        Assert.Equal(2.00m, SellerPerformanceExposureEngine.AveragePerDay(20, 10));
    }

    [Fact]
    public void AveragePerDay_RoundsAwayFromZero()
    {
        Assert.Equal(0.33m, SellerPerformanceExposureEngine.AveragePerDay(1, 3));
        Assert.Equal(0.67m, SellerPerformanceExposureEngine.AveragePerDay(2, 3));
    }

    [Fact]
    public void WeightedTotals_PreferSumOverMeanOfAverages()
    {
        var a = SellerPerformanceExposureEngine.AveragePerDay(10, 20)!.Value;
        var b = SellerPerformanceExposureEngine.AveragePerDay(30, 10)!.Value;
        var mean = Math.Round((a + b) / 2m, 2, MidpointRounding.AwayFromZero);
        var weighted = SellerPerformanceExposureEngine.AveragePerDay(40, 30)!.Value;

        Assert.Equal(1.75m, mean);
        Assert.Equal(1.33m, weighted);
        Assert.NotEqual(mean, weighted);
    }

    [Fact]
    public void HistoricalBackfill_StartBeforeConnect_UsesFirstOrderDay()
    {
        // Period opened by backfill on first order day Jul 2, still open
        var days = SellerPerformanceExposureEngine.CountShopDays(
            From,
            To,
            [(new DateOnly(2026, 7, 2), null)]);

        Assert.Equal(9, days);
    }

    [Fact]
    public void Reactivate_CreatesSeparateIntervals_MergedWhenAdjacent()
    {
        var periods = new (DateOnly, DateOnly?)[]
        {
            (new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3)),
            (new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 6)) // adjacent reopen
        };

        Assert.Equal(
            6,
            SellerPerformanceExposureEngine.CountShopDays(From, To, periods));
    }
}
