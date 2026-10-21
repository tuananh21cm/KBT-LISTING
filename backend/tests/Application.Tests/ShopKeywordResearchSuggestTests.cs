using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ShopKeywordResearchSuggestTests
{
    private static readonly string[] DressCategories = ["Casual Dresses", "Women / Dresses"];

    [Fact]
    public void TryAccept_PlusVintageMidi_IsKept()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ShopKeywordResearchSuggest.TryAccept(
            "Plus Size Vintage Midi Dress",
            DressCategories,
            [],
            seen,
            out var keyword));
        Assert.Equal("plus size vintage midi dress", keyword);
        Assert.Single(seen);
    }

    [Theory]
    [InlineData("dress")]
    [InlineData("maxi dress")]
    [InlineData("plus size")]
    [InlineData("outfit")]
    [InlineData("casual dresses")]
    public void TryAccept_Generic_IsRejected(string keyword)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ShopKeywordResearchSuggest.TryAccept(keyword, DressCategories, [], seen, out _));
        Assert.Empty(seen);
    }

    [Fact]
    public void TryAccept_TooLongTitle_IsRejected()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ShopKeywordResearchSuggest.TryAccept(
            "plus size vintage floral wrap midi dress women extra",
            DressCategories,
            [],
            seen,
            out _));
    }

    [Fact]
    public void TryAccept_DedupesHyphenAndSpaces()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ShopKeywordResearchSuggest.TryAccept(
            "plus size a-line midi dress",
            DressCategories,
            [],
            seen,
            out _));
        Assert.False(ShopKeywordResearchSuggest.TryAccept(
            "plus size a line midi dress",
            DressCategories,
            [],
            seen,
            out _));
    }

    [Fact]
    public void TryAccept_OverlappingLiveListing_IsRejected()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ShopKeywordResearchSuggest.TryAccept(
            "plus size wrap midi dress",
            DressCategories,
            ["Women's Plus Size Wrap Midi Dress for Sunday Brunch"],
            seen,
            out _));
    }

    [Fact]
    public void Take_DropsDuplicatesAndGeneric_PreservesOrder()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = ShopKeywordResearchSuggest.Take(
            [
                ("maxi dress", "generic", "x"),
                ("plus size vintage midi dress", "midi vintage", "plus vintage"),
                ("PLUS SIZE VINTAGE MIDI DRESS", "dup", "x"),
                ("plus size wrap midi dress", "wrap", "plus wrap")
            ],
            take: 5,
            DressCategories,
            [],
            seen);

        Assert.Equal(2, taken.Count);
        Assert.Equal("plus size vintage midi dress", taken[0].Keyword);
        Assert.Equal("plus size wrap midi dress", taken[1].Keyword);
        Assert.Equal("midi vintage", taken[0].Rationale);
    }

    [Fact]
    public void NormalizeDisplay_StripsNoiseKeepsHyphen()
    {
        Assert.Equal(
            "plus size a-line midi dress",
            ShopKeywordResearchSuggest.NormalizeDisplay("  Plus Size A-Line Midi Dress!!!  "));
        Assert.Equal(string.Empty, ShopKeywordResearchSuggest.NormalizeDisplay("   "));
    }

    [Fact]
    public void BackfillNowFromUpcoming_MovesUntilMinNow()
    {
        var now = new List<ShopKeywordResearchSuggest.AcceptedKeyword>
        {
            new("plus size vintage midi dress", "a", "x")
        };
        var upcoming = new List<ShopKeywordResearchSuggest.AcceptedKeyword>
        {
            new("plus size wrap midi dress", "b", "x"),
            new("plus size linen midi dress", "c", "x"),
            new("plus size floral tunic dress", "d", "x")
        };

        ShopKeywordResearchSuggest.BackfillNowFromUpcoming(now, upcoming, minNow: 5);

        Assert.Equal(4, now.Count);
        Assert.Empty(upcoming);
        Assert.Equal("plus size wrap midi dress", now[1].Keyword);
    }

    [Fact]
    public void TryAccept_SevenWordPlusSizePhrase_IsKept()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ShopKeywordResearchSuggest.TryAccept(
            "plus size vintage floral wrap midi dress",
            DressCategories,
            [],
            seen,
            out var keyword,
            ShopBodySizeCodes.Plus));
        Assert.Equal("plus size vintage floral wrap midi dress", keyword);
    }

    [Fact]
    public void TryAccept_EightWords_IsKept()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ShopKeywordResearchSuggest.TryAccept(
            "plus size vintage floral wrap midi dress women",
            DressCategories,
            [],
            seen,
            out var keyword,
            ShopBodySizeCodes.Plus));
        Assert.Equal("plus size vintage floral wrap midi dress women", keyword);
    }

    [Fact]
    public void TryAccept_PlusShop_DropsKeywordWithoutPlusSize()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ShopKeywordResearchSuggest.TryAccept(
            "swing floral a-line dress",
            DressCategories,
            [],
            seen,
            out _,
            ShopBodySizeCodes.Plus));
        Assert.Empty(seen);
    }

    [Fact]
    public void Take_PlusShop_KeepsOnlyPlusSizePhrases()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = ShopKeywordResearchSuggest.Take(
            [
                ("swing floral a-line dress", "generic aline", "x"),
                ("plus size vintage midi dress", "midi vintage", "plus vintage"),
                ("plus size wrap midi dress", "wrap", "plus wrap")
            ],
            take: 5,
            DressCategories,
            [],
            seen,
            ShopBodySizeCodes.Plus);

        Assert.Equal(2, taken.Count);
        Assert.Equal("plus size vintage midi dress", taken[0].Keyword);
        Assert.Equal("plus size wrap midi dress", taken[1].Keyword);
    }

    [Fact]
    public void FallbackWarnings_AreDistinctAndActionable()
    {
        Assert.Contains("Chưa có snapshot", ShopKeywordResearchSuggest.EmptySnapshotWarning, StringComparison.Ordinal);
        Assert.Contains("generic", ShopKeywordResearchSuggest.GenericOnlySnapshotWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lệch tệp", ShopKeywordResearchSuggest.OffNicheSnapshotWarning, StringComparison.Ordinal);
        Assert.NotEqual(
            ShopKeywordResearchSuggest.GenericOnlySnapshotWarning,
            ShopKeywordResearchSuggest.OffNicheSnapshotWarning);
    }
}
