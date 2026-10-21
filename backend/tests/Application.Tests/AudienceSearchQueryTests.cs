using Application.DTOs.ProductOpportunity;
using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class AudienceSearchQueryTests
{
    [Fact]
    public void Build_PlusSize_PrefixesMissingToken()
    {
        Assert.Equal(
            "plus size swing floral a-line dress",
            AudienceSearchQuery.Build("Swing Floral A-Line Dress", ShopBodySizeCodes.Plus));
    }

    [Fact]
    public void Build_PlusSize_KeepsExistingToken()
    {
        Assert.Equal(
            "plus size a-line lantern sleeve dress",
            AudienceSearchQuery.Build("Plus Size A-Line Lantern Sleeve Dress", ShopBodySizeCodes.Plus));
    }

    [Fact]
    public void Build_PlusSize_HyphenatedAlreadyHasToken()
    {
        Assert.Equal(
            "plus-size wrap midi dress",
            AudienceSearchQuery.Build("Plus-Size Wrap Midi Dress", ShopBodySizeCodes.Plus));
    }

    [Fact]
    public void Build_PlusSize_SkipsPetiteConflict()
    {
        Assert.Equal(
            "petite floral midi dress",
            AudienceSearchQuery.Build("Petite Floral Midi Dress", ShopBodySizeCodes.Plus));
    }

    [Theory]
    [InlineData(ShopBodySizeCodes.Petite, "floral midi dress", "petite floral midi dress")]
    [InlineData(ShopBodySizeCodes.Tall, "wide leg pants", "tall wide leg pants")]
    [InlineData(ShopBodySizeCodes.Curvy, "wrap midi dress", "curvy wrap midi dress")]
    public void Build_OtherBodySizes_PrefixWhenMissing(string body, string keyword, string expected)
        => Assert.Equal(expected, AudienceSearchQuery.Build(keyword, body));

    [Theory]
    [InlineData(ShopBodySizeCodes.Midsize)]
    [InlineData(ShopBodySizeCodes.Regular)]
    [InlineData(null)]
    [InlineData("")]
    public void Build_MidsizeRegularOrMissing_DoesNotPrefix(string? body)
        => Assert.Equal(
            "swing floral a-line dress",
            AudienceSearchQuery.Build("Swing Floral A-Line Dress", body));

    [Fact]
    public void BuildSheinUrl_MatchesOrganicPdsearch()
    {
        var url = AudienceSearchQuery.BuildSheinUrl("plus size swing floral a-line dress");
        Assert.NotNull(url);
        Assert.StartsWith("https://us.shein.com/pdsearch/", url);
        Assert.Contains("source=historyWord", url);
        Assert.DoesNotContain("min_price", url);
        Assert.DoesNotContain("sort=", url);
        // Tham số gắn với một lượt xem list cụ thể → SHEIN đối chiếu phiên và bắn /risk/.
        Assert.DoesNotContain("src_identifier", url);
        Assert.DoesNotContain("src_tab_page_id", url);
        Assert.Equal(
            WhatToSellTodayService.BuildSheinSearchUrl("plus size swing floral a-line dress"),
            url);
    }

    [Fact]
    public void HasRequiredBodyModifier_PlusRequiresToken()
    {
        Assert.False(AudienceSearchQuery.HasRequiredBodyModifier(
            "swing floral a-line dress", ShopBodySizeCodes.Plus));
        Assert.True(AudienceSearchQuery.HasRequiredBodyModifier(
            "plus size swing floral a-line dress", ShopBodySizeCodes.Plus));
        Assert.True(AudienceSearchQuery.HasRequiredBodyModifier(
            "swing floral a-line dress", ShopBodySizeCodes.Regular));
    }

    [Fact]
    public void Apply_StampsSearchQueryAndSheinUrl()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto { Keyword = "Swing Floral A-Line Dress" }
            ],
            Upcoming =
            [
                new ShopKeywordResearchItemDto { Keyword = "Plus Size Vintage Midi Dress" }
            ]
        };

        AudienceSearchQuery.Apply(dto, ShopBodySizeCodes.Plus);

        Assert.Equal("plus size swing floral a-line dress", dto.ListNow[0].SearchQuery);
        Assert.Contains("pdsearch", dto.ListNow[0].SheinUrl);
        Assert.Equal("plus size vintage midi dress", dto.Upcoming[0].SearchQuery);
    }
}
