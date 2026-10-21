using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingStockSumClampedTests
{
    [Fact]
    public void SumQuantitiesClamped_NormalTotal_ReturnsExactSum()
    {
        var result = ListingSyncService.SumQuantitiesClamped([10, 20, 30]);
        Assert.Equal(60, result);
    }

    [Fact]
    public void SumQuantitiesClamped_Overflow_ClampsToIntMaxValue()
    {
        // Three SKUs near int.MaxValue — Enumerable.Sum() would throw OverflowException.
        var result = ListingSyncService.SumQuantitiesClamped(
            [999_999_999, 999_999_999, 999_999_999]);
        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void SumQuantitiesClamped_Empty_ReturnsZero()
    {
        Assert.Equal(0, ListingSyncService.SumQuantitiesClamped([]));
    }

    [Fact]
    public void SumQuantitiesClamped_ExactIntMaxValue_ReturnsIntMaxValue()
    {
        Assert.Equal(int.MaxValue, ListingSyncService.SumQuantitiesClamped([int.MaxValue]));
    }
}
