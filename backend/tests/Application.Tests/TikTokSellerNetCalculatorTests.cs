using Domain.Orders;
using Xunit;

namespace Application.Tests;

public class TikTokSellerNetCalculatorTests
{
    [Fact]
    public void HasMeaningfulBreakdown_AllZero_IsFalse()
    {
        Assert.False(TikTokSellerNetCalculator.HasMeaningfulBreakdown(0, 0, 0, 0));
        Assert.False(TikTokSellerNetCalculator.HasMeaningfulBreakdown(0, null, 0, 0));
    }

    [Theory]
    [InlineData(12.5, 0, 0, 0)]
    [InlineData(0, 9.99, 0, 0)]
    [InlineData(0, 0, 3, 0)]
    [InlineData(0, 0, 0, 1.5)]
    public void HasMeaningfulBreakdown_AnyMoney_IsTrue(
        decimal productBefore,
        decimal totalPaid,
        decimal shipBefore,
        decimal sellerDisc)
    {
        Assert.True(TikTokSellerNetCalculator.HasMeaningfulBreakdown(
            productBefore, totalPaid, shipBefore, sellerDisc));
    }

    [Fact]
    public void KeepLastNonZero_IncomingZero_PreservesCurrent()
    {
        Assert.Equal(25.4m, TikTokSellerNetCalculator.KeepLastNonZero(25.4m, 0));
    }

    [Fact]
    public void KeepLastNonZero_BothZero_StaysZero()
    {
        Assert.Equal(0m, TikTokSellerNetCalculator.KeepLastNonZero(0, 0));
    }

    [Fact]
    public void KeepLastNonZero_IncomingPositive_TakesIncoming()
    {
        Assert.Equal(30m, TikTokSellerNetCalculator.KeepLastNonZero(25m, 30m));
        Assert.Equal(12m, TikTokSellerNetCalculator.KeepLastNonZero(0, 12m));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Seller Shipping", false)]
    [InlineData("TikTok Shipping", true)]
    [InlineData("tiktok shipping", true)]
    [InlineData("Platform Shipping", true)]
    [InlineData("PLATFORM SHIPPING", true)]
    public void IsTikTokPlatformShipping_DetectsLabels(string? method, bool expected)
    {
        Assert.Equal(expected, TikTokSellerNetCalculator.IsTikTokPlatformShipping(method));
        Assert.Equal(
            expected ? TikTokSellerNetCalculator.ShipTypeTikTok : TikTokSellerNetCalculator.ShipTypeSeller,
            TikTokSellerNetCalculator.ResolveShipType(method));
    }

    [Fact]
    public void Compute_SellerShipping_IgnoresPlatformDiscount()
    {
        // 577561266492904264: 33.99 − 6.12 + 4.99 − 2.26 = 30.60 (không trừ platform 0.59)
        var net = TikTokSellerNetCalculator.Compute(
            productAmountBeforeDiscount: 33.99m,
            productSellerDiscount: 6.12m,
            productPlatformDiscount: 0.59m,
            shippingFeeBeforeDiscount: 4.99m,
            shippingFeeSellerDiscount: 0m,
            totalTax: 2.26m,
            otherFee: 0m,
            shippingMethod: "Seller shipping(Standard Shipping)");

        Assert.Equal(30.60m, net);
    }

    [Fact]
    public void Compute_SellerShipping_HeavyPlatformDiscount_StillIgnoresIt()
    {
        // 577560438405305221: 51.64 − 20.49 + 7.99 − 7.99 = 31.15 (không trừ platform 31.14)
        var net = TikTokSellerNetCalculator.Compute(
            productAmountBeforeDiscount: 51.64m,
            productSellerDiscount: 20.49m,
            productPlatformDiscount: 31.14m,
            shippingFeeBeforeDiscount: 7.99m,
            shippingFeeSellerDiscount: 7.99m,
            totalTax: 0m,
            otherFee: 0m,
            shippingMethod: "Seller shipping(Standard Shipping)");

        Assert.Equal(31.15m, net);
    }

    [Fact]
    public void Compute_TikTokShipping_IgnoresNetShip()
    {
        // 577559091347690171: 40.84 − 13.06 − 2.44 = 25.34 (không trừ ship 8.17)
        var net = TikTokSellerNetCalculator.Compute(
            productAmountBeforeDiscount: 40.84m,
            productSellerDiscount: 13.06m,
            productPlatformDiscount: 0m,
            shippingFeeBeforeDiscount: 8.17m,
            shippingFeeSellerDiscount: 0m,
            totalTax: 2.44m,
            otherFee: 0m,
            shippingMethod: "TikTok shipping(Standard shipping)");

        Assert.Equal(25.34m, net);
    }

    [Fact]
    public void Compute_TikTokShipping_IgnoresPlatformDiscountAndNetShip()
    {
        // 33.99 − 6.12 − 2.26 = 25.61 (không trừ platform 0.59, không trừ ship 4.99)
        var net = TikTokSellerNetCalculator.Compute(
            productAmountBeforeDiscount: 33.99m,
            productSellerDiscount: 6.12m,
            productPlatformDiscount: 0.59m,
            shippingFeeBeforeDiscount: 4.99m,
            shippingFeeSellerDiscount: 0m,
            totalTax: 2.26m,
            otherFee: 0m,
            shippingMethod: "TikTok Shipping");

        Assert.Equal(25.61m, net);
    }

    [Theory]
    [InlineData("Seller Shipping", 30.60)]
    [InlineData("TikTok Shipping", 25.61)]
    public void Compute_ZeroPlatformDiscount_MatchesLegacyProductNet(string method, decimal expected)
    {
        var net = TikTokSellerNetCalculator.Compute(
            productAmountBeforeDiscount: 33.99m,
            productSellerDiscount: 6.12m,
            productPlatformDiscount: 0m,
            shippingFeeBeforeDiscount: 4.99m,
            shippingFeeSellerDiscount: 0m,
            totalTax: 2.26m,
            otherFee: 0m,
            shippingMethod: method);

        Assert.Equal(expected, net);
    }
}
