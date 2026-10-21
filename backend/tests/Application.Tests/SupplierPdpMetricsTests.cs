using Application.Services;
using Domain.Entities;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

/// <summary>Parse / apply supplier PDP metrics from Shein realtime JSON fields.</summary>
public class SupplierPdpMetricsTests
{
    [Fact]
    public void ParseShippingDayPercent_ExtractsNumberFromHtml()
    {
        const string desc = "<font color=\"#007D49\">88% are ≤ <b>7</b> business days</font>";
        Assert.Equal(88m, SupplierPdpMetrics.ParseShippingDayPercent(desc));
    }

    [Fact]
    public void ParseShippingDayPercent_PlainText()
    {
        Assert.Equal(100m, SupplierPdpMetrics.ParseShippingDayPercent("100% are ≤ 5 business days"));
    }

    [Fact]
    public void ParseShippingDaysFromDesc_ReadsBoldDay()
    {
        const string desc = "<font color=\"#007D49\">88% are ≤ <b>7</b> business days</font>";
        Assert.Equal(7, SupplierPdpMetrics.ParseShippingDaysFromDesc(desc));
        Assert.Equal(5, SupplierPdpMetrics.ParseShippingDaysFromDesc("69% are ≤ 5 business days"));
        Assert.Null(SupplierPdpMetrics.ParseShippingDaysFromDesc(null));
    }

    [Fact]
    public void SumOnTimeDayPercents_SkipsOverflowBucket()
    {
        var buckets = new (string? Day, string? Percent)[]
        {
            ("<4 business days", "22"),
            ("4 business days", "18"),
            ("5 business days", "29"),
            ("6 business days", "10"),
            ("7 business days", "9"),
            (">7 business days", "12"),
        };
        Assert.Equal(88m, SupplierPdpMetrics.SumOnTimeDayPercents(buckets));
    }

    [Fact]
    public void ParseShippingDayPercent_Invalid_ReturnsNull()
    {
        Assert.Null(SupplierPdpMetrics.ParseShippingDayPercent(null));
        Assert.Null(SupplierPdpMetrics.ParseShippingDayPercent("no percent here"));
        Assert.Null(SupplierPdpMetrics.ParseShippingDayPercent("120% too high"));
    }

    [Fact]
    public void BuildStoreUrl_SheinCanonical()
    {
        var url = SupplierPdpMetrics.BuildStoreUrl("SHEIN", "8210139331");
        Assert.Equal("https://us.shein.com/store/home?store_code=8210139331", url);
    }

    [Fact]
    public void BuildStoreUrl_PrefersProvided()
    {
        var url = SupplierPdpMetrics.BuildStoreUrl("SHEIN", "1", "https://us.shein.com/store/home?store_code=1");
        Assert.Equal("https://us.shein.com/store/home?store_code=1", url);
    }

    [Fact]
    public void NormalizeRating_ClampsToFive()
    {
        Assert.Equal(4.86m, SupplierPdpMetrics.NormalizeRating(4.861m));
        Assert.Null(SupplierPdpMetrics.NormalizeRating(5.1m));
        Assert.Null(SupplierPdpMetrics.NormalizeRating(-1m));
    }

    [Fact]
    public void NormalizeAbsoluteUrl_ProtocolRelative()
    {
        Assert.Equal(
            "https://img.ltwebstatic.com/a.png",
            SupplierPdpMetrics.NormalizeAbsoluteUrl("//img.ltwebstatic.com/a.png"));
        Assert.Equal(
            "https://img.ltwebstatic.com/a.png",
            SupplierPdpMetrics.NormalizeAbsoluteUrl("https://img.ltwebstatic.com/a.png"));
    }

    [Fact]
    public void InferPlatformFromUrl_SheinHost()
    {
        Assert.Equal("SHEIN", SupplierPdpMetrics.InferPlatformFromUrl("https://us.shein.com/foo-p-1.html"));
        Assert.Equal("TEMU", SupplierPdpMetrics.InferPlatformFromUrl("https://www.temu.com/goods.html?goods_id=1"));
        Assert.Null(SupplierPdpMetrics.InferPlatformFromUrl("https://example.com/x"));
    }

    [Fact]
    public void MatchPlatformFromDomains_SheinHost()
    {
        var platform = SupplierPdpMetrics.MatchPlatformFromDomains(
            "https://us.shein.com/foo-p-123.html",
            [("SHEIN", "shein.com, us.shein.com"), ("TEMU", "temu.com")]);
        Assert.Equal("SHEIN", platform);
    }

    [Fact]
    public void IsTruthyFlag_SkuLevelStringZero_IsFalse()
    {
        Assert.False(SupplierPdpMetrics.IsTruthyFlag("0"));
        Assert.False(SupplierPdpMetrics.IsTruthyFlag(0));
        Assert.False(SupplierPdpMetrics.IsTruthyFlag(false));
        Assert.True(SupplierPdpMetrics.IsTruthyFlag(true));
        Assert.True(SupplierPdpMetrics.IsTruthyFlag(1));
        Assert.True(SupplierPdpMetrics.IsTruthyFlag("1"));
        Assert.True(SupplierPdpMetrics.IsTruthyFlag("true"));
    }

    [Fact]
    public void ParseIsLocalProduct_TipsLocal_WinsOverZeroFlag()
    {
        Assert.True(SupplierPdpMetrics.ParseIsLocalProduct("Local", "0"));
        Assert.True(SupplierPdpMetrics.ParseIsLocalProduct(" local ", null));
        Assert.False(SupplierPdpMetrics.ParseIsLocalProduct(null, "0"));
        Assert.True(SupplierPdpMetrics.ParseIsLocalProduct(null, "1"));
        Assert.Null(SupplierPdpMetrics.ParseIsLocalProduct(null, null));
    }

    [Fact]
    public void AggregateMallQuickShip_UsesMallStock_NotSkuLevelZero()
    {
        // Sample 409956167: sku.isSupportQuickShip == "0" nhưng mall_stock == true.
        var malls = new (string? MallCode, object? IsSupportQuickShip)[]
        {
            ("1", true),
            ("1", false),
            ("2", false),
        };
        Assert.True(SupplierPdpMetrics.AggregateMallQuickShip(malls, "1"));
        Assert.False(SupplierPdpMetrics.IsTruthyFlag("0"));
    }

    [Fact]
    public void AggregateMallQuickShip_AllFalse_IsFalse()
    {
        var malls = new (string? MallCode, object? IsSupportQuickShip)[]
        {
            ("1", false),
            ("1", "0"),
        };
        Assert.False(SupplierPdpMetrics.AggregateMallQuickShip(malls, "1"));
    }

    [Fact]
    public void AggregateMallQuickShip_NoSelectedMallMatch_OrsAllMalls()
    {
        var malls = new (string? MallCode, object? IsSupportQuickShip)[]
        {
            ("2", true),
            ("3", false),
        };
        Assert.True(SupplierPdpMetrics.AggregateMallQuickShip(malls, "1"));
        Assert.Null(SupplierPdpMetrics.AggregateMallQuickShip(
            Array.Empty<(string? MallCode, object? IsSupportQuickShip)>(),
            "1"));
    }

    [Fact]
    public void ParseStoreTitleFromSoldByTips_StripsPrefix()
    {
        Assert.Equal("kcruab-kp", SupplierPdpMetrics.ParseStoreTitleFromSoldByTips("Sold by kcruab-kp"));
        Assert.Equal("kcruab-kp", SupplierPdpMetrics.ParseStoreTitleFromSoldByTips("sold by: kcruab-kp"));
        Assert.Equal("Shop ABC", SupplierPdpMetrics.ParseStoreTitleFromSoldByTips("Sold by \"Shop ABC\""));
        Assert.Null(SupplierPdpMetrics.ParseStoreTitleFromSoldByTips(null));
        Assert.Null(SupplierPdpMetrics.ParseStoreTitleFromSoldByTips("Ships from US"));
    }

    [Fact]
    public void ResolveStoreTitle_PrefersExplicitTitle()
    {
        Assert.Equal("Real Shop", SupplierPdpMetrics.ResolveStoreTitle("Real Shop", "Sold by kcruab-kp"));
        Assert.Equal("kcruab-kp", SupplierPdpMetrics.ResolveStoreTitle(null, "Sold by kcruab-kp"));
        Assert.Null(SupplierPdpMetrics.ResolveStoreTitle("  ", null));
    }

    [Fact]
    public void NormalizeShopRank_AndStoreAttention()
    {
        Assert.Equal(4.86m, SupplierPdpMetrics.NormalizeShopRank(4.861m));
        Assert.Equal(12m, SupplierPdpMetrics.NormalizeShopRank(12m));
        Assert.Null(SupplierPdpMetrics.NormalizeShopRank(-1m));
        Assert.Equal(1523, SupplierPdpMetrics.NormalizeStoreAttention(1523));
        Assert.Null(SupplierPdpMetrics.NormalizeStoreAttention(-3));
    }

    [Fact]
    public void ApplyInfo_SetsFkAndCopiesRating_DoesNotClearWithNull()
    {
        var listing = new Listing
        {
            ReviewCount = 9,
            AverageRating = 4.1m
        };
        var info = new SupplierProductInfo
        {
            Id = Guid.NewGuid(),
            ReviewCount = 120,
            AverageRating = null
        };

        SupplierProductInfoService.ApplyInfo(listing, info);

        Assert.Equal(info.Id, listing.SupplierProductInfoId);
        Assert.Equal(120, listing.ReviewCount);
        Assert.Equal(4.1m, listing.AverageRating);
    }

    [Fact]
    public void ApplyInfo_CopiesBothWhenPresent()
    {
        var listing = new Listing();
        var info = new SupplierProductInfo
        {
            Id = Guid.NewGuid(),
            ReviewCount = 50,
            AverageRating = 4.5m
        };

        SupplierProductInfoService.ApplyInfo(listing, info);

        Assert.Equal(50, listing.ReviewCount);
        Assert.Equal(4.5m, listing.AverageRating);
    }
}
