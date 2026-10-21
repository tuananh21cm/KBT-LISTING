using System.Text.Json;
using Application.Services;
using Infrastructure.Integrations.FourSeller;
using Xunit;

namespace Application.Tests;

public class FourSellerFailedMessageReaderTests
{
    [Fact]
    public void Read_TopLevelFailedMessage_ReturnsText()
    {
        using var doc = JsonDocument.Parse(
            """{"failedMessage":"【Unpublishable】Product attributes are unavailable."}""");

        var result = FourSellerFailedMessageReader.Read(doc.RootElement);

        Assert.Equal("【Unpublishable】Product attributes are unavailable.", result);
    }

    [Fact]
    public void Read_NestedPublishInfo_ReturnsText()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "publishInfo": {
                "warningMessage": "【Publish Failed】product image upload fail"
              }
            }
            """);

        var result = FourSellerFailedMessageReader.Read(doc.RootElement);

        Assert.Contains("Publish Failed", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_ArrayFailedMessage_JoinsWithSemicolon()
    {
        using var doc = JsonDocument.Parse(
            """{"failedMessage":["Missing Size Chart","Invalid brand"]}""");

        var result = FourSellerFailedMessageReader.Read(doc.RootElement);

        Assert.Equal("Missing Size Chart; Invalid brand", result);
    }

    [Fact]
    public void Read_EmptyRequireMessage_Ignored()
    {
        using var doc = JsonDocument.Parse(
            """{"requireMessage":"","publishStatus":"unpublishable"}""");

        var result = FourSellerFailedMessageReader.Read(doc.RootElement);

        Assert.Null(result);
    }
}

public class ListingSyncSyncErrorTests
{
    [Theory]
    [InlineData("【Unpublishable】4Seller vẫn ở draft (unpublishable).", true)]
    [InlineData(
        "Listing vẫn ở Draft — 4Seller báo publishStatus=unpublishable nhưng không trả lý do chi tiết.",
        true)]
    [InlineData("【Unpublishable】Product attributes are unavailable.", false)]
    [InlineData("【Publish Failed】product image upload fail", false)]
    public void IsSystemPublishBlockedFallback_DetectsPlaceholder(string syncError, bool expected)
    {
        Assert.Equal(expected, ListingSyncService.IsSystemPublishBlockedFallback(syncError));
    }
}
