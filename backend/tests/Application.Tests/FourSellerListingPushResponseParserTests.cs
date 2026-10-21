using Domain.Exceptions;
using Infrastructure.Integrations.FourSeller;
using Xunit;

namespace Application.Tests;

public class FourSellerListingPushResponseParserTests
{
    [Fact]
    public void Parse_NestedSuccess_ReturnsSuccess()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"code":0,"listingId":851868670824,"errorMessage":null}}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.True(result.Success);
        Assert.Equal(851868670824, result.ListingId);
        Assert.Equal("success", result.Message);
    }

    [Fact]
    public void Parse_NestedCodeFailure_ReturnsFailure()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"code":4100,"listingId":851868670824,"errorMessage":"业务异常参数错误"}}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.False(result.Success);
        Assert.Equal("业务异常参数错误", result.Message);
    }

    [Fact]
    public void Parse_NestedErrorMessage_ReturnsFailure()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"code":0,"listingId":851868670824,"errorMessage":"【Publish Failed】product image upload fail"}}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.False(result.Success);
        Assert.Contains("Publish Failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureAccepted_ThrowsWhenPushRejected()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"code":4100,"errorMessage":"Rejected"}}
            """;

        var parsed = FourSellerListingPushResponseParser.Parse(body);

        var ex = Assert.Throws<BusinessException>(() =>
            FourSellerListingPushResponseParser.EnsureAccepted(parsed));

        Assert.Contains("Rejected", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DataFalse_ReturnsFailure()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":false}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.False(result.Success);
        Assert.Contains("data=false", result.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<BusinessException>(() =>
            FourSellerListingPushResponseParser.EnsureAccepted(result));
    }

    [Fact]
    public void Parse_DataNull_ReturnsFailure()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":null}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.False(result.Success);
        Assert.Contains("data=null", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DataTrue_ReturnsSuccess()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":true}
            """;

        var result = FourSellerListingPushResponseParser.Parse(body);

        Assert.True(result.Success);
    }
}
