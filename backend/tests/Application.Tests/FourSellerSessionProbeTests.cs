using Infrastructure.Integrations.FourSeller;
using Xunit;

namespace Application.Tests;

public class FourSellerSessionProbeTests
{
    [Fact]
    public void Classify_Code0DataTrue_IsLoggedIn()
    {
        var state = FourSellerSessionProbe.Classify(200, """{"code":0,"data":true}""", out var detail);

        Assert.Equal(FourSellerSessionState.LoggedIn, state);
        Assert.Equal(string.Empty, detail);
    }

    [Fact]
    public void Classify_Code0DataFalse_IsExpired()
    {
        var state = FourSellerSessionProbe.Classify(200, """{"code":0,"data":false}""", out var detail);

        Assert.Equal(FourSellerSessionState.Expired, state);
        Assert.Contains("hết hạn", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Code0MissingData_IsExpired()
    {
        var state = FourSellerSessionProbe.Classify(200, """{"code":0}""", out _);

        Assert.Equal(FourSellerSessionState.Expired, state);
    }

    [Fact]
    public void Classify_Code500_IsUnverified()
    {
        var state = FourSellerSessionProbe.Classify(
            200,
            """{"code":500,"messages":"系统繁忙"}""",
            out var detail);

        Assert.Equal(FourSellerSessionState.Unverified, state);
        Assert.Contains("KHÔNG phải cookie hết hạn", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_HtmlBody_IsUnverified()
    {
        var state = FourSellerSessionProbe.Classify(200, "<html>blocked</html>", out var detail);

        Assert.Equal(FourSellerSessionState.Unverified, state);
        Assert.Contains("JSON", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_EmptyBody_IsUnverified()
    {
        var state = FourSellerSessionProbe.Classify(200, "   ", out _);

        Assert.Equal(FourSellerSessionState.Unverified, state);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(502)]
    [InlineData(503)]
    public void Classify_Non2xx_IsUnverified(int status)
    {
        var state = FourSellerSessionProbe.Classify(
            status,
            """{"code":0,"data":true}""",
            out var detail);

        Assert.Equal(FourSellerSessionState.Unverified, state);
        Assert.Contains("KHÔNG phải cookie hết hạn", detail, StringComparison.Ordinal);
    }
}
