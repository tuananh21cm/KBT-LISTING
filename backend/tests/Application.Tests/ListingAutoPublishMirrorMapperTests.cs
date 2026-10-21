using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishMirrorMapperTests
{
    [Fact]
    public void ApplyRewriteResult_SetsSearchTermsAndHighlightsWhenNonEmpty()
    {
        var detail = new ListingDetailDto
        {
            ProductName = "Old title",
            DescriptionHtml = "<p>Old</p>",
            SearchTerms = ["old term"],
            ProductHighlights = ["old highlight"]
        };

        var rewriteResult = new RewriteListingDescriptionResultDto
        {
            Title = "New SEO title",
            DescriptionHtml = "<p>New</p>",
            SearchTerms = ["women dress", "summer outfit"],
            ProductHighlights = ["Soft fabric", "Easy care"]
        };

        ListingAutoPublishMirrorMapper.ApplyRewriteResult(detail, rewriteResult);

        Assert.Equal("New SEO title", detail.ProductName);
        Assert.Equal("<p>New</p>", detail.DescriptionHtml);
        Assert.True(detail.DescriptionRewritten);
        Assert.Equal(["women dress", "summer outfit"], detail.SearchTerms);
        Assert.Equal(["Soft fabric", "Easy care"], detail.ProductHighlights);
    }

    [Fact]
    public void ApplyRewriteResult_PreservesExistingMetaWhenAiReturnsEmpty()
    {
        var detail = new ListingDetailDto
        {
            ProductName = "Keep title",
            DescriptionHtml = "<p>Keep</p>",
            SearchTerms = ["keep term"],
            ProductHighlights = ["keep highlight"]
        };

        var rewriteResult = new RewriteListingDescriptionResultDto
        {
            Title = "Rewritten title",
            DescriptionHtml = "<p>Rewritten</p>",
            SearchTerms = [],
            ProductHighlights = []
        };

        ListingAutoPublishMirrorMapper.ApplyRewriteResult(detail, rewriteResult);

        Assert.Equal("Rewritten title", detail.ProductName);
        Assert.Equal("<p>Rewritten</p>", detail.DescriptionHtml);
        Assert.True(detail.DescriptionRewritten);
        Assert.Equal(["keep term"], detail.SearchTerms);
        Assert.Equal(["keep highlight"], detail.ProductHighlights);
    }

    [Fact]
    public void ToUpdateDto_AfterApplyRewriteResult_IncludesSearchMeta()
    {
        var detail = new ListingDetailDto
        {
            ProductName = "Title",
            DescriptionHtml = "<p>Body</p>",
            DescriptionRewritten = true,
            SearchTerms = ["term a"],
            ProductHighlights = ["highlight a"]
        };

        var dto = ListingAutoPublishMirrorMapper.ToUpdateDto(detail);

        Assert.Equal(["term a"], dto.SearchTerms);
        Assert.Equal(["highlight a"], dto.ProductHighlights);
        Assert.True(dto.DescriptionRewritten);
    }
}
