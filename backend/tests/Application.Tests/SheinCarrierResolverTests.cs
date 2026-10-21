using Application.Services;
using Application.Services.Shein;
using Xunit;

namespace Application.Tests;

public class SheinCarrierResolverTests
{
    [Fact]
    public void Infer_PrefersTrackInfoName()
    {
        var carrier = SheinCarrierResolver.Infer(
            "GOFO",
            "ASnGOFOxCNO-DID-PB-BN-Na",
            "GFUS01071879965825");

        Assert.Equal("GOFO", carrier);
    }

    [Fact]
    public void Infer_IgnoresWarehouseTrackInfoName()
    {
        var carrier = SheinCarrierResolver.Infer(
            "International Warehouse",
            "ASnGOFOxCNO-DID-PB-BN-Na",
            "GFUS01071879965825");

        Assert.Equal("GOFO", carrier);
    }

    [Theory]
    [InlineData("ASnGOFOxCNO-DID-PB-BN-Na", "GOFO")]
    [InlineData("ASnSpeedXxCNO", "SpeedX")]
    [InlineData("something-USPS-else", "USPS")]
    public void Infer_FromShippingMethodReal(string methodReal, string expected)
    {
        Assert.Equal(expected, SheinCarrierResolver.InferFromShippingMethodReal(methodReal));
    }

    [Theory]
    [InlineData("GFUS01071879965825", "GOFO")]
    [InlineData("GFU1234567890123", "GOFO")]
    [InlineData("SPXBPA013642609090021885", "SpeedX")]
    [InlineData("SPXSFO007852609090004150", "SpeedX")]
    [InlineData("9261290347969270443816", "USPS")]
    [InlineData("9361290347969270443816", "USPS")]
    // USPS Tracking / Ground Advantage — prefix e-commerce hay gặp nhất, từng bị allowlist bỏ sót.
    [InlineData("9400111899223344556677", "USPS")]
    [InlineData("9400111899223197428490", "USPS")]
    // Dải IMpb chưa có bằng chứng trong dữ liệu: không đoán, đoán sai là publish nhầm carrier.
    [InlineData("9500111899223344556677", null)]
    [InlineData("YT260101234567890", "YunExpress")]
    [InlineData("C26090802149087", null)]
    [InlineData("N26090800077406", null)]
    public void Infer_FromTrackingPrefix(string trackingNo, string? expected)
    {
        Assert.Equal(expected, SheinCarrierResolver.InferFromTrackingPrefix(trackingNo));
    }

    [Fact]
    public void Infer_GSU1SC4740020UP_WithoutTrackInfoName()
    {
        // Billno mẫu: shipping_no GFUS… + shipping_method_real chứa GOFO, name có thể null sớm.
        var carrier = SheinCarrierResolver.Infer(
            null,
            "ASnGOFOxCNO-DID-PB-BN-Na",
            "GFUS01071879965825");

        Assert.Equal("GOFO", carrier);
        Assert.True(OrderAutoMarkShipPolicy.IsExactCarrierMatch(carrier, "GOFO."));
    }

    [Fact]
    public void MatchCatalog_PicksTikTokGofoDot()
    {
        var matched = SheinCarrierResolver.MatchCatalog(
            "GOFO",
            ["Gofo Express US", "GOFO France", "GOFO.", "Speedx", "USPS"]);

        Assert.Equal("GOFO.", matched);
    }

    [Fact]
    public void MatchCatalog_PicksSpeedxOnTikTok()
    {
        var matched = SheinCarrierResolver.MatchCatalog(
            "SpeedX",
            ["Speedx", "USPS", "FedEx"]);

        Assert.Equal("Speedx", matched);
    }
}
