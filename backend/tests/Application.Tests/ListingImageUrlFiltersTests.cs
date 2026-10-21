using System.Text.Json.Nodes;
using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingImageUrlFiltersTests
{
    [Theory]
    [InlineData("https://img.ltwebstatic.com/swatches/red.gif", true)]
    [InlineData("https://cdn/x.GIF", true)]
    [InlineData("https://cdn/x.jpg?type=gif", false)]
    [InlineData("https://cdn/x.jpg", false)]
    [InlineData("", false)]
    public void IsGifImageUrl_DetectsPathExtension(string url, bool expected)
    {
        Assert.Equal(expected, ListingImageUrlFilters.IsGifImageUrl(url));
    }

    [Fact]
    public void StripGifFromPipeJoined_KeepsNonGifUrls()
    {
        var raw = "https://cdn/a.gif|https://cdn/b.jpg|https://cdn/c.gif";
        Assert.Equal("https://cdn/b.jpg", ListingImageUrlFilters.StripGifFromPipeJoined(raw));
    }

    [Fact]
    public void StripGifUrlsFromPayload_CleansMainImageAndSkuImgsObject()
    {
        var obj = new JsonObject
        {
            ["mainImage"] = "https://cdn/a.gif|https://cdn/b.jpg",
            ["skuImgs"] = new JsonObject
            {
                ["Red"] = new JsonArray("https://cdn/red.gif", "https://cdn/red.jpg"),
                ["Blue"] = new JsonArray("https://cdn/blue.gif")
            }
        };

        ListingImageUrlFilters.StripGifUrlsFromPayload(obj);

        Assert.Equal("https://cdn/b.jpg", obj["mainImage"]!.GetValue<string>());
        var skuImgs = (JsonObject)obj["skuImgs"]!;
        Assert.Equal("https://cdn/red.jpg", skuImgs["Red"]![0]!.GetValue<string>());
        Assert.False(skuImgs.ContainsKey("Blue"));
    }

    [Fact]
    public void StripGifUrlsFromPayload_CleansSkuImgsStringRows()
    {
        var rows = new JsonArray
        {
            new JsonObject
            {
                ["attrName"] = "Color",
                ["custom_value"] = "Red",
                ["img_url"] = "https://cdn/red.gif|https://cdn/red.jpg"
            },
            new JsonObject
            {
                ["attrName"] = "Color",
                ["custom_value"] = "Blue",
                ["img_url"] = "https://cdn/blue.gif"
            }
        };
        var obj = new JsonObject
        {
            ["skuImgs"] = JsonValue.Create(rows.ToJsonString())
        };

        ListingImageUrlFilters.StripGifUrlsFromPayload(obj);

        var parsed = JsonNode.Parse(obj["skuImgs"]!.GetValue<string>()) as JsonArray;
        Assert.NotNull(parsed);
        Assert.Single(parsed!);
        Assert.Equal("https://cdn/red.jpg", parsed[0]!["img_url"]!.GetValue<string>());
    }

    [Fact]
    public void StripGifUrlsFromPayload_RemovesGifImgTagsFromDescription()
    {
        var obj = new JsonObject
        {
            ["descriptionHtml"] =
                "<p>Hello</p><img src=\"https://cdn/a.gif\" width=\"800\"><img src=\"https://cdn/b.jpg\">"
        };

        ListingImageUrlFilters.StripGifUrlsFromPayload(obj);

        var html = obj["descriptionHtml"]!.GetValue<string>();
        Assert.DoesNotContain(".gif", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://cdn/b.jpg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void StripProductVideoFromPayload_ClearsAllVideoKeys()
    {
        var obj = new JsonObject
        {
            ["erpVideoUrl"] = "https://cdn/video.mp4",
            ["videoUrl"] = "https://cdn/video.mp4",
            ["productVideo"] = "https://cdn/video.mp4",
            ["videoPostUrl"] = "https://cdn/post.mp4",
            ["erpVideoId"] = "12345",
            ["videoId"] = "67890"
        };

        ListingImageUrlFilters.StripProductVideoFromPayload(obj);

        Assert.Equal(string.Empty, obj["erpVideoUrl"]!.GetValue<string>());
        Assert.Equal(string.Empty, obj["videoUrl"]!.GetValue<string>());
        Assert.Equal(string.Empty, obj["productVideo"]!.GetValue<string>());
        Assert.Equal(string.Empty, obj["videoPostUrl"]!.GetValue<string>());
        Assert.Equal(string.Empty, obj["erpVideoId"]!.GetValue<string>());
        Assert.Equal(string.Empty, obj["videoId"]!.GetValue<string>());
    }

    [Fact]
    public void RemoveProductVideo_ClearsImagesAndErpVideoId()
    {
        var detail = new ListingDetailDto
        {
            ErpVideoId = "999",
            Images =
            [
                new ListingImageDto { MediaType = "Main", Url = "https://cdn/a.jpg" },
                new ListingImageDto { MediaType = "Video", Url = "https://cdn/v.mp4" },
                new ListingImageDto { MediaType = "SizeChart", Url = "https://cdn/chart.png" }
            ]
        };

        Assert.True(ListingImageUrlFilters.HasProductVideo(detail));
        ListingImageUrlFilters.RemoveProductVideo(detail);

        Assert.False(ListingImageUrlFilters.HasProductVideo(detail));
        Assert.Null(detail.ErpVideoId);
        Assert.Equal(2, detail.Images.Count);
        Assert.DoesNotContain(detail.Images, i => i.MediaType == "Video");
        Assert.Contains(detail.Images, i => i.MediaType == "SizeChart");
    }
}
