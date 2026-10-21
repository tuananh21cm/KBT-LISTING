using Infrastructure.Integrations.FourSeller;
using Xunit;

namespace Application.Tests;

public class FourSellerAsyncShipOrderResultParserTests
{
    [Fact]
    public void Parse_StringSuccessCounters_ReturnsSuccess()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"data":"[]","success":"1","total":"1","failed":"0"}}
            """;

        var result = FourSellerAsyncShipOrderResultParser.Parse(body);

        Assert.Equal("Success", result.Status);
    }

    [Fact]
    public void Parse_BooleanSuccess_ReturnsSuccess()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"success":true,"failed":0}}
            """;

        var result = FourSellerAsyncShipOrderResultParser.Parse(body);

        Assert.Equal("Success", result.Status);
    }

    [Fact]
    public void Parse_StringFailure_ReturnsFailed()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"success":"0","failed":"1","data":"[{\"msg\":\"Rejected\"}]"}}
            """;

        var result = FourSellerAsyncShipOrderResultParser.Parse(body);

        Assert.Equal("Failed", result.Status);
    }

    [Fact]
    public void Parse_EmptyDataObject_ReturnsPending()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{}}
            """;

        var result = FourSellerAsyncShipOrderResultParser.Parse(body);

        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public void Parse_ProcessingStatus_ReturnsPending()
    {
        const string body =
            """
            {"code":0,"msg":"success","data":{"status":"processing","finished":false}}
            """;

        var result = FourSellerAsyncShipOrderResultParser.Parse(body);

        Assert.Equal("Pending", result.Status);
    }
}
