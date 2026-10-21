using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishPrepServiceImagesTests
{
    private static readonly Guid SkuRed = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SkuBlue = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public void PrepareImages_DoesNotDuplicateVariantColorImages()
    {
        // Variant color images are stored with MediaType "Main" plus ListingSkuId/ColorValue,
        // so they match both the shared-product and the variant predicate.
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/shared-1.jpg"),
                Image("https://cdn/shared-2.jpg", mediaType: "Detail"),
                Image("https://cdn/red.jpg", skuId: SkuRed, color: "Red"),
                Image("https://cdn/blue.jpg", skuId: SkuBlue, color: "Blue")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Equal(4, detail.Images.Count);
        Assert.Equal(
            detail.Images.Select(i => i.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            detail.Images.Count);
        Assert.Single(detail.Images, i => i.Url == "https://cdn/red.jpg");
        Assert.Single(detail.Images, i => i.Url == "https://cdn/blue.jpg");
    }

    [Fact]
    public void PrepareImages_MainImageIsNotAVariantColorImage()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/red.jpg", skuId: SkuRed, color: "Red"),
                Image("https://cdn/shared.jpg")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Equal("https://cdn/shared.jpg", detail.MainImage);
    }

    [Fact]
    public void PrepareImages_OrdersSharedThenVariantThenExtrasWithSequentialSortOrder()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/chart.jpg", mediaType: "SizeChart", sortOrder: 7),
                Image("https://cdn/red.jpg", skuId: SkuRed, color: "Red", sortOrder: 5),
                Image("https://cdn/shared.jpg", sortOrder: 3)
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Equal(
            ["https://cdn/shared.jpg", "https://cdn/red.jpg", "https://cdn/chart.jpg"],
            detail.Images.Select(i => i.Url).ToList());
        Assert.Equal([0, 1, 2], detail.Images.Select(i => i.SortOrder).ToList());
    }

    [Fact]
    public void PrepareImages_KeepsSizeChartMediaInGallery()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/shared.jpg"),
                Image("https://cos/size-chart.png", mediaType: "SizeChart")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.True(prep.HasSizeChartInGallery(detail));
    }

    [Fact]
    public void PrepareImages_AppendsSizeChartCaptureOnceAsDetail()
    {
        var detail = new ListingDetailDto
        {
            Images = [Image("https://cdn/shared.jpg")]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, "https://supplier/chart.jpg");
        prep.PrepareImages(detail, "https://supplier/chart.jpg");

        Assert.Single(detail.Images, i => i.Url == "https://supplier/chart.jpg");
        Assert.Equal(
            "Detail",
            detail.Images.First(i => i.Url == "https://supplier/chart.jpg").MediaType);
    }

    [Fact]
    public void PrepareImages_DoesNotAppendCaptureAlreadyPresentUnderAnotherMediaType()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/shared.jpg"),
                Image("https://supplier/chart.jpg", mediaType: "SizeChart")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, "https://supplier/chart.jpg");

        Assert.Single(detail.Images, i => i.Url == "https://supplier/chart.jpg");
    }

    [Fact]
    public void PrepareImages_UpgradesHttpUrlsToHttps()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("http://cdn/shared.jpg"),
                Image("http://cdn/red.jpg", skuId: SkuRed, color: "Red")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, "http://supplier/chart.jpg");

        Assert.All(detail.Images, i => Assert.StartsWith("https://", i.Url, StringComparison.Ordinal));
        Assert.Contains(detail.Images, i => i.Url == "https://supplier/chart.jpg");
    }

    [Fact]
    public void PrepareImages_DropsDuplicateUrls()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/shared.jpg"),
                Image("http://cdn/shared.jpg"),
                Image("https://cdn/shared.jpg", mediaType: "Detail")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Single(detail.Images);
    }

    [Fact]
    public void PrepareImages_RemovesGifFromGalleryAndVariantColors()
    {
        var detail = new ListingDetailDto
        {
            Images =
            [
                Image("https://cdn/shared.gif"),
                Image("https://cdn/shared.jpg"),
                Image("https://cdn/red.gif", skuId: SkuRed, color: "Red"),
                Image("https://cdn/red.jpg", skuId: SkuRed, color: "Red")
            ],
            Variations =
            [
                new ListingVariationDto
                {
                    Id = SkuRed,
                    MainImage = "https://cdn/red.gif|https://cdn/red.jpg",
                    VariantAttributes = "{\"Color\":\"Red\"}"
                }
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.DoesNotContain(detail.Images, i => i.Url.Contains(".gif", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("https://cdn/shared.jpg", detail.MainImage);
        Assert.Equal("https://cdn/red.jpg", detail.Variations[0].MainImage);
    }

    [Fact]
    public void PrepareImages_DoesNotRemoveJpgWithGifQueryParam()
    {
        var detail = new ListingDetailDto
        {
            Images = [Image("https://cdn/shared.jpg?format=gif")]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Single(detail.Images);
        Assert.Equal("https://cdn/shared.jpg?format=gif", detail.Images[0].Url);
    }

    [Fact]
    public void PrepareImages_DoesNotAppendGifSizeChartCapture()
    {
        var detail = new ListingDetailDto
        {
            Images = [Image("https://cdn/shared.jpg")]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, "https://supplier/chart.gif");

        Assert.DoesNotContain(detail.Images, i => i.Url == "https://supplier/chart.gif");
    }

    [Fact]
    public void PrepareImages_RemovesProductVideoAndClearsErpVideoId()
    {
        var detail = new ListingDetailDto
        {
            ErpVideoId = "material-42",
            Images =
            [
                Image("https://cdn/shared.jpg"),
                Image("https://cdn/promo.mp4", mediaType: "Video"),
                Image("https://cos/size-chart.png", mediaType: "SizeChart")
            ]
        };

        var prep = CreateService();
        prep.PrepareImages(detail, sizeChartUrl: null);

        Assert.Null(detail.ErpVideoId);
        Assert.DoesNotContain(detail.Images, i => i.MediaType == "Video");
        Assert.Contains(detail.Images, i => i.MediaType == "Main");
        Assert.Contains(detail.Images, i => i.MediaType == "SizeChart");
    }

    private static ListingImageDto Image(
        string url,
        string mediaType = "Main",
        Guid? skuId = null,
        string? color = null,
        int sortOrder = 0) =>
        new()
        {
            Url = url,
            MediaType = mediaType,
            ListingSkuId = skuId,
            ColorValue = color,
            SortOrder = sortOrder
        };

    private static ListingAutoPublishPrepService CreateService() =>
        new(
            new StubPricingTemplateService(),
            new StubShippingTemplateService(),
            new StubListingSyncService(new ListingDetailDto()),
            new StubHttpClientFactory());
}
