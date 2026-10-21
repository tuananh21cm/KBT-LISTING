using Application.DTOs.ProductOpportunity;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class KeywordMatchPhrasesTests
{
    [Fact]
    public void FromKeyword_FloralMaxi_AllImportantInMust()
    {
        var set = KeywordMatchPhrases.FromKeyword("floral maxi dress");
        Assert.Equal(["floral", "maxi", "dress"], Phrases(set.Must));
        Assert.Empty(Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
    }

    [Fact]
    public void FromKeyword_MidiDress_LengthIsDistinctive()
    {
        var set = KeywordMatchPhrases.FromKeyword("midi dress");
        Assert.Equal(["midi", "dress"], Phrases(set.Must));
        Assert.Empty(Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
    }

    [Fact]
    public void FromKeyword_HardwareStyle_RankingOnly_NotInCoverageN()
    {
        var denim = KeywordMatchPhrases.FromKeyword("Retro Button Placket Denim Vest Dress");
        Assert.Equal(["denim", "dress"], Phrases(denim.Must));
        Assert.Equal(["retro", "button", "placket", "vest"], Phrases(denim.Should));
        Assert.Equal(1, denim.MinDistinctiveHits);

        var sleeveless = KeywordMatchPhrases.FromKeyword("Sleeveless X-Ray Skeleton Midi Dress");
        Assert.Equal(["x-ray", "skeleton", "dress"], Phrases(sleeveless.Must));
        Assert.Equal(["sleeveless", "midi"], Phrases(sleeveless.Should));
        Assert.Equal(1, sleeveless.MinDistinctiveHits);
    }

    [Fact]
    public void FromKeyword_ALineLanternSleeve_CapsAtTwoDemandModifiers()
    {
        var set = KeywordMatchPhrases.FromKeyword("Plus Size A-Line Lantern Sleeve Dress");
        Assert.Equal(["a-line", "lantern", "dress"], Phrases(set.Must));
        Assert.Equal(["sleeve"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
    }

    [Fact]
    public void PassesCoverage_RuffledOffShoulder_OneOfTwoDemandPlusGarment()
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Ruffled Off-Shoulder Midi Dress",
            ["dress"]);
        Assert.Equal(["ruffled", "off-shoulder", "dress"], Phrases(set.Must));
        Assert.Equal(["midi"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);

        Assert.False(Pass(set, "Women Casual Summer Dress"));
        Assert.False(Pass(set, "Women Casual Summer Midi Dress"));
        Assert.True(Pass(set, "Ruffle Bardot Dress"));
        Assert.True(Pass(set, "Off-Shoulder Midi Dress"));
        Assert.True(Pass(set, "Women Ruffled Off the Shoulder Dress"));
        Assert.True(Pass(set, "Ruffled Midi Dress"));
        Assert.True(Pass(set, "Ruffled Off-Shoulder Midi Dress"));
    }

    [Fact]
    public void PassesCoverage_MaxiDress_FailsMidiKeyword()
    {
        var set = KeywordMatchPhrases.FromKeyword("midi dress");
        Assert.False(Pass(set, "Women Maxi Dress Summer Casual"));
        Assert.True(Pass(set, "Women Midi Dress Summer Casual"));
        Assert.Equal(
            1,
            KeywordMatchPhrases.TitleScore(
                "Women Midi Dress Summer Casual",
                slug: null,
                cateName: null,
                set.Must,
                set.Should),
            3);
    }

    [Theory]
    [InlineData(
        "SHEIN LUNE Plus-Size Plaid Dresses,Sleeveless Vest Mini Dress,Red And White,Autumn,Casual,School,Back-To-School,Retro-Style Loose-Fitting For Christmas,Parties")]
    [InlineData(
        "Franclia Elegant Sleeveless Yellow Blouse, Chic & Slimming Summer Top For Women Sleeveless Dress Vest Dress Ladies Dresses Red Dress For Women Button Up Dress")]
    [InlineData(
        "SHEIN EZwear Plus Size Gray Button Half-Placket Round Neck Waist Dress, Graduation Brunch Date Night Bussines Formal Summer Casual Elegant")]
    [InlineData(
        "SHEIN CURVE+ Curve Size US22Music Festival, Seaside Vacation, Bohemia Women's Contrasting Lace Elegant Vest Dress, Sexy Cami Contrast Lace Dress")]
    public void PassesCoverage_UserFalsePositives_FailWithoutDenim(string title)
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Retro Button Placket Denim Vest Dress",
            ["denim", "dress"]);
        Assert.False(Pass(set, title, "Plus Size Dresses"));
    }

    [Theory]
    [InlineData("EMERY ROSE Plus Size Retro Denim Button Vest Dress")]
    [InlineData("Plus Size Denim Pinafore Dress")]
    [InlineData("Vintage Blue Jean Vest Mini Dress")]
    [InlineData("Women Denim Dungaree Dress Button Front")]
    public void PassesCoverage_TrueDenim_PassWithoutButtonOrRetro(string title)
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Retro Button Placket Denim Vest Dress",
            ["denim", "dress"]);
        Assert.True(Pass(set, title, "Plus Size Dresses"));
    }

    [Fact]
    public void TitleScore_UsesPhraseWeights()
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Retro Button Placket Denim Vest Dress",
            ["denim", "dress"]);
        var full = KeywordMatchPhrases.TitleScore(
            "Retro Denim Button Placket Vest Dress",
            slug: null,
            cateName: null,
            set.Must,
            set.Should);
        var noneShould = KeywordMatchPhrases.TitleScore(
            "Denim Pinafore Dress",
            slug: null,
            cateName: null,
            set.Must,
            set.Should);
        Assert.Equal(1, full, 3);
        Assert.Equal(4.0 / 9.0, noneShould, 3);
    }

    [Fact]
    public void FromKeyword_StopwordsOnly_EmptyMustKeepsLegacyGate()
    {
        var set = KeywordMatchPhrases.FromKeyword("plus size");
        Assert.Empty(set.Must);
        Assert.Empty(set.Should);
        Assert.Equal(0, set.MinDistinctiveHits);
        Assert.True(KeywordMatchPhrases.PassesCoverage("any dress", null, null, set));
    }

    [Fact]
    public void SanitizeMustTokens_KeepsTokenOrAdjacentPair_DropsUnknown()
    {
        var kept = KeywordMatchPhrases.SanitizeMustTokens(
            "plus size vintage midi dress",
            ["dress", "midi dress", "skirt", "DRESS"]);
        Assert.Equal(["dress", "midi dress"], kept);
    }

    [Fact]
    public void SanitizeMustTokens_AllowsAdjacentPairWithoutDictionary()
    {
        var kept = KeywordMatchPhrases.SanitizeMustTokens(
            "Retro Button Placket Denim Vest Dress",
            ["button placket", "lantern sleeve"]);
        Assert.Equal(["button placket"], kept);
    }

    [Fact]
    public void FromKeyword_IgnoresAi_UsesCoverageSplit()
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Sleeveless X-Ray Skeleton Midi Dress",
            ["sleeveless", "dress"]);
        Assert.Equal(["x-ray", "skeleton", "dress"], Phrases(set.Must));
        Assert.Equal(["sleeveless", "midi"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
        Assert.False(Pass(set, "Women Midi Dress Long Sleeve"));
        Assert.False(Pass(set, "Women Sleeveless Midi Dress"));
        Assert.True(Pass(set, "Women Midi Dress Long Sleeve Skeleton Print"));
        Assert.True(Pass(set, "Women Sleeveless Midi Dress Skeleton Print"));
    }

    [Fact]
    public void FromKeyword_DenimVestDress_MaterialBeatsGarmentSubtype()
    {
        var set = KeywordMatchPhrases.FromKeyword("denim vest dress", ["vest", "dress"]);
        Assert.Equal(["denim", "dress"], Phrases(set.Must));
        Assert.Equal(["vest"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
    }

    [Fact]
    public void Apply_StampsResearchItem_CoverageNotGarmentOnly()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto { Keyword = "Retro Button Placket Denim Vest Dress" }
            ]
        };
        KeywordMatchPhrases.Apply(dto);
        Assert.Equal(["denim", "dress"], Phrases(dto.ListNow[0].MustPhrases));
        Assert.Equal(["denim", "dress"], dto.ListNow[0].MustTokens);
        Assert.Equal(1, dto.ListNow[0].MinDistinctiveHits);
    }

    [Fact]
    public void Apply_StrengthensPersistedGarmentOnlyAiTokens()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Keyword = "midi dress",
                    MustTokens = ["dress", "gown"]
                }
            ]
        };
        KeywordMatchPhrases.Apply(dto);
        Assert.Equal(["midi", "dress"], dto.ListNow[0].MustTokens);
        Assert.Equal(["midi", "dress"], Phrases(dto.ListNow[0].MustPhrases));
        Assert.Empty(Phrases(dto.ListNow[0].ShouldPhrases));
        Assert.Equal(1, dto.ListNow[0].MinDistinctiveHits);
    }

    [Fact]
    public void SanitizeMustTokens_MapsSynonymOntoKeywordToken()
    {
        var kept = KeywordMatchPhrases.SanitizeMustTokens(
            "denim vest dress",
            ["jean", "dresses", "skirt"]);
        Assert.Equal(["denim", "dress"], kept);
    }

    [Fact]
    public void FromKeyword_MustIgnoresAiBigram_UsesCoverageSplit()
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Retro Button Placket Denim Vest Dress",
            ["button placket", "dress"]);
        Assert.Equal(["denim", "dress"], Phrases(set.Must));
        Assert.Equal(["retro", "button", "placket", "vest"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
    }

    [Fact]
    public void FromKeyword_LongKeyword_KeepsTwoDemandModifiersPlusGarment()
    {
        var set = KeywordMatchPhrases.FromKeyword(
            "Ruffled Off-Shoulder Sleeveless Floral Midi Dress");
        Assert.Equal(["off-shoulder", "floral", "dress"], Phrases(set.Must));
        Assert.Equal(["ruffled", "sleeveless", "midi"], Phrases(set.Should));
        Assert.Equal(1, set.MinDistinctiveHits);
        Assert.True(Pass(set, "Women Floral Midi Dress"));
        Assert.True(Pass(set, "Off-Shoulder Summer Dress"));
        Assert.False(Pass(set, "Women Casual Summer Dress"));
        Assert.False(Pass(set, "Ruffled Midi Dress"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    public void MinDistinctiveHitsFor_IsStable(int n, int expected)
        => Assert.Equal(expected, KeywordMatchPhrases.MinDistinctiveHitsFor(n));

    [Fact]
    public void FlexibleStringList_ReadsStringOrArray()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleStringListJsonConverter());

        var fromArray = System.Text.Json.JsonSerializer.Deserialize<List<string>?>(
            """["sleeveless","dress"]""", options);
        var fromString = System.Text.Json.JsonSerializer.Deserialize<List<string>?>(
            "\"dress\"", options);
        Assert.Equal(["sleeveless", "dress"], fromArray);
        Assert.Equal(["dress"], fromString);
    }

    private static bool Pass(KeywordMatchPhraseSet set, string title, string? cate = "Dresses")
        => KeywordMatchPhrases.PassesCoverage(title, slug: null, cateName: cate, set);

    private static List<string> Phrases(IEnumerable<KeywordMatchPhraseDto> items)
        => items.Select(p => p.Phrase).ToList();
}
