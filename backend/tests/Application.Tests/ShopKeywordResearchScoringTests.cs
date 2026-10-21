using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ShopKeywordResearchScoringTests
{
    private static ShopKeywordAudience MaturePlusVintage() => new(
        ["Casual Dresses"],
        35,
        44,
        ShopBodySizeCodes.Plus,
        [ShopStyleCodes.Vintage, ShopStyleCodes.Casual],
        "Body change, fit khó, bụng/eo/hông, plus size",
        "Flattering + comfortable + confidence",
        "Plus-size tops, dresses, wide-leg pants, tunics, stretchy jeans",
        []);

    [Theory]
    [InlineData("outfit")]
    [InlineData("maxi dress")]
    [InlineData("casual dress")]
    [InlineData("plus size")]
    [InlineData("dress")]
    [InlineData("Casual Dresses")]
    [InlineData("curvy dresses plus size")]
    public void IsGeneric_DropsShortOrCategoryPhrases(string keyword)
        => Assert.True(ShopKeywordResearchScoring.IsGeneric(keyword, ["Casual Dresses"]));

    [Theory]
    [InlineData("plus size vintage midi dress")]
    [InlineData("floral tunic dress plus size")]
    [InlineData("wide leg pants tummy control")]
    public void IsGeneric_KeepsRealShopperQueries(string keyword)
        => Assert.False(ShopKeywordResearchScoring.IsGeneric(keyword, ["Casual Dresses"]));

    [Fact]
    public void Score_PenalizesBodyconForMaturePlusVintage()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var bodycon = ShopKeywordResearchScoring.ScoreBreakdown(
            "Leopard Print Ruffle Hem Bodycon Dress",
            4007,
            11,
            "+6.23%",
            ["Top search", "Trending on TikTok"],
            audience,
            weather);
        var midi = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            1200,
            40,
            "+4.02%",
            ["Top search"],
            audience,
            weather);

        Assert.True(bodycon.Audience <= -15);
        Assert.True(midi.Audience > bodycon.Audience);
        Assert.True(midi.Score > bodycon.Score);
    }

    [Fact]
    public void AugustWeather_PenalizesPufferCoat()
    {
        var audience = MaturePlusVintage();
        var august = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var puffer = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size puffer coat winter",
            8000,
            20,
            "+10%",
            [],
            audience,
            august);
        var midi = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size linen midi dress",
            800,
            30,
            "+5%",
            [],
            audience,
            august);

        Assert.Contains("puffer", august.PenalizeTokens, StringComparer.OrdinalIgnoreCase);
        Assert.True(puffer.Score < midi.Score);
    }

    [Fact]
    public void FilterValidIds_DropsUnknownGenericAndLowFit()
    {
        var goodId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var lowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var fakeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var byId = new Dictionary<Guid, ShopKeywordScored>
        {
            [goodId] = new(
                goodId,
                "plus size vintage floral midi dress",
                1200,
                40,
                null,
                "+4%",
                null,
                ["Top search"],
                null,
                [],
                40,
                20,
                false),
            [lowId] = new(
                lowId,
                "Leopard Print Ruffle Hem Bodycon Dress",
                4007,
                11,
                null,
                "+6.23%",
                null,
                ["Top search"],
                null,
                [],
                10,
                -28,
                true)
        };

        var picked = ShopKeywordResearchScoring.FilterValidIds(
            [goodId.ToString("N"), lowId.ToString(), fakeId.ToString(), "not-a-guid", goodId.ToString()],
            byId,
            ["Casual Dresses"],
            take: 5);

        Assert.Equal([goodId], picked);
    }

    [Fact]
    public void BuildShortlist_IncludesHighVolumeMismatch()
    {
        var fit = new ShopKeywordScored(
            Guid.NewGuid(),
            "plus size vintage floral midi dress",
            200,
            20,
            null,
            "+2%",
            null,
            [],
            null,
            [],
            50,
            20,
            false);
        var mismatch = new ShopKeywordScored(
            Guid.NewGuid(),
            "Leopard Print Ruffle Hem Bodycon Dress",
            4007,
            11,
            null,
            "+6%",
            null,
            [],
            null,
            [],
            5,
            -28,
            true);

        var shortlist = ShopKeywordResearchScoring.BuildShortlist([fit, mismatch]);
        Assert.Contains(shortlist, s => s.Id == fit.Id);
        Assert.DoesNotContain(shortlist, s => s.Id == mismatch.Id);

        var skipped = ShopKeywordResearchScoring.BuildMismatchExamples(
            [fit, mismatch],
            shortlist.Select(s => s.Id).ToHashSet());
        Assert.Contains(skipped, s => s.Id == mismatch.Id && s.LowAudienceFit);
    }

    [Fact]
    public void BuildShortlist_EmptyWhenEveryRowIsOffAudience()
    {
        var mismatch = new ShopKeywordScored(
            Guid.NewGuid(),
            "Leopard Print Ruffle Hem Bodycon Dress",
            4007,
            11,
            null,
            "+6%",
            null,
            [],
            null,
            [],
            5,
            -28,
            true);

        Assert.Empty(ShopKeywordResearchScoring.BuildShortlist([mismatch]));
        Assert.Empty(ShopKeywordResearchScoring.BuildShortlist([]));
    }

    [Fact]
    public void FillMissingIds_BackfillsFromRankedPool()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var third = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var pool = new List<ShopKeywordScored>
        {
            new(first, "plus size vintage floral midi dress", 200, 20, null, "+2%", null, [], null, [], 50, 30, false),
            new(second, "plus size linen midi dress", 180, 20, null, "+2%", null, [], null, [], 40, 22, false),
            new(third, "floral wrap midi dress plus size", 90, 15, null, "+1%", null, [], null, [], 30, 18, false)
        };

        var filled = ShopKeywordResearchScoring.FillMissingIds([third], pool, take: 3);
        Assert.Equal([third, first, second], filled);
    }

    [Fact]
    public void Score_PenalizesOffCategoryGarmentForDressShop()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var pants = ShopKeywordResearchScoring.ScoreBreakdown(
            "wide leg pants tummy control",
            9000,
            40,
            "+12%",
            ["Trending on TikTok"],
            audience,
            weather);
        var midi = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            1200,
            40,
            "+4.02%",
            ["Top search"],
            audience,
            weather);

        Assert.True(ShopKeywordResearchScoring.IsLowAudienceFit(pants.Audience, audience));
        Assert.False(ShopKeywordResearchScoring.IsLowAudienceFit(midi.Audience, audience));
        Assert.True(midi.Audience > pants.Audience);
        Assert.True(midi.Score > pants.Score);
    }

    [Fact]
    public void Score_KeywordTextBeatsTikTokCategoryLabel()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var pants = ShopKeywordResearchScoring.ScoreBreakdown(
            "wide leg pants tummy control",
            9000,
            40,
            "+12%",
            ["Trending on TikTok"],
            audience,
            weather,
            ["Casual Dresses"]);

        Assert.True(ShopKeywordResearchScoring.IsLowAudienceFit(pants.Audience, audience));
    }

    [Fact]
    public void Score_PenalizesBodyconForYoungVintageShop()
    {
        var audience = new ShopKeywordAudience(
            ["Casual Dresses"],
            25,
            34,
            ShopBodySizeCodes.Plus,
            [ShopStyleCodes.Vintage, ShopStyleCodes.Casual],
            "Thiếu thời gian",
            "Cute + practical + versatile",
            "Casual tops, dresses, wide-leg pants, sets",
            []);
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var bodycon = ShopKeywordResearchScoring.ScoreBreakdown(
            "Leopard Print Ruffle Hem Bodycon Dress",
            4007,
            11,
            "+6.23%",
            ["Top search", "Trending on TikTok"],
            audience,
            weather);
        var midi = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            1200,
            40,
            "+4.02%",
            ["Top search"],
            audience,
            weather);

        Assert.True(ShopKeywordResearchScoring.IsLowAudienceFit(bodycon.Audience, audience));
        Assert.False(ShopKeywordResearchScoring.IsLowAudienceFit(midi.Audience, audience));
        Assert.True(midi.Audience > bodycon.Audience);
    }

    [Fact]
    public void Score_AllowsBodyconForYoungSexyShop()
    {
        var audience = new ShopKeywordAudience(
            ["Casual Dresses"],
            18,
            24,
            ShopBodySizeCodes.Regular,
            [ShopStyleCodes.Sexy, ShopStyleCodes.Party],
            "Muốn đẹp, bắt trend",
            "Trendy + nổi bật",
            "Y2K, mini/maxi dress, matching sets",
            []);
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var bodycon = ShopKeywordResearchScoring.ScoreBreakdown(
            "leopard print bodycon mini dress",
            4007,
            11,
            "+6.23%",
            ["Top search"],
            audience,
            weather);

        Assert.True(bodycon.Audience > 0);
        Assert.False(ShopKeywordResearchScoring.IsLowAudienceFit(bodycon.Audience, audience));
    }

    [Fact]
    public void ParseRingPercent_ReadsSignedPercent()
    {
        Assert.Equal(6.23, ShopKeywordResearchScoring.ParseRingPercent("+6.23%"));
        Assert.Equal(-12, ShopKeywordResearchScoring.ParseRingPercent("-12%"));
        Assert.Equal(4.02, ShopKeywordResearchScoring.ParseRingPercent("4.02%"));
        Assert.Null(ShopKeywordResearchScoring.ParseRingPercent(null));
    }

    [Fact]
    public void AugustWeatherNote_WarnsAgainstWinterCoats()
    {
        var info = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        Assert.Contains("nóng", info.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(info.PenalizeTokens, t => t.Contains("puffer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(info.BoostTokens, t => t.Contains("linen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_DropsSheerMeshForMaturePlusVintage()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var mesh = ShopKeywordResearchScoring.ScoreBreakdown(
            "See-through Mesh Polka Dot A-line Dress",
            3931,
            7,
            "+5%",
            ["Top search"],
            audience,
            weather);
        Assert.True(ShopKeywordResearchScoring.IsLowAudienceFit(mesh.Audience, audience));
    }

    [Fact]
    public void Score_DropsBohoWhenShopIsNotBoho()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var boho = ShopKeywordResearchScoring.ScoreBreakdown(
            "Boho Ruched A Line Dress",
            3458,
            20,
            "+4%",
            ["Top search"],
            audience,
            weather);
        Assert.True(ShopKeywordResearchScoring.IsLowAudienceFit(boho.Audience, audience));
    }

    [Fact]
    public void PickLockedLists_IsStableAcrossCalls()
    {
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var c = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var rows = new List<ShopKeywordScored>
        {
            new(c, "vintage floral midi dress plus size", 100, 10, null, "+1%", null, [], null, [], 30, 18, false),
            new(a, "plus size vintage floral midi dress", 200, 20, null, "+2%", null, [], null, [], 50, 30, false),
            new(b, "plus size linen midi dress", 180, 20, null, "+2%", null, [], null, [], 40, 22, false)
        };

        var first = ShopKeywordResearchScoring.PickLockedLists(rows, 2, 1);
        var second = ShopKeywordResearchScoring.PickLockedLists(rows, 2, 1);
        Assert.Equal(first.Now.Select(x => x.Id), second.Now.Select(x => x.Id));
        Assert.Equal(first.Upcoming.Select(x => x.Id), second.Upcoming.Select(x => x.Id));
        Assert.Equal([a, b], first.Now.Select(x => x.Id));
        Assert.Equal([c], first.Upcoming.Select(x => x.Id));
    }

    [Fact]
    public void OverlapsListing_WhenMostTokensAlreadyLive()
    {
        Assert.True(ShopKeywordResearchScoring.OverlapsListing(
            "plus size wrap midi dress",
            ["Women's Plus Size Wrap Midi Dress for Sunday Brunch"]));
        Assert.False(ShopKeywordResearchScoring.OverlapsListing(
            "plus size vintage floral midi",
            ["Women's Plus Size Wrap Midi Dress for Sunday Brunch"]));
        Assert.False(ShopKeywordResearchScoring.OverlapsListing("plus size vintage midi dress", []));
    }

    [Fact]
    public void Score_ALineWithoutPlusToken_RanksBelowExplicitPlusSize()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var genericAline = ShopKeywordResearchScoring.ScoreBreakdown(
            "swing floral a-line dress",
            5000,
            20,
            "+8%",
            ["Top search"],
            audience,
            weather);
        var plusAline = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size swing floral a-line dress",
            5000,
            20,
            "+8%",
            ["Top search"],
            audience,
            weather);

        Assert.True(plusAline.Audience > genericAline.Audience);
        Assert.True(plusAline.Score > genericAline.Score);
    }

    [Fact]
    public void Score_PrefersAudienceFitOverHighVolumeMismatch()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var bodycon = ShopKeywordResearchScoring.ScoreBreakdown(
            "Leopard Print Ruffle Hem Bodycon Dress",
            9000,
            11,
            "+12%",
            ["Trending on TikTok", "Top search"],
            audience,
            weather,
            onlineProductsRing: "+8%");
        var midi = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            1200,
            40,
            "+4.02%",
            ["Top search"],
            audience,
            weather,
            onlineProductsRing: "+2%");

        Assert.True(midi.Audience > bodycon.Audience);
        Assert.True(midi.Score > bodycon.Score);
        Assert.InRange(midi.Score, 0, 100);
        Assert.InRange(bodycon.Score, 0, 100);
    }

    [Fact]
    public void ToDisplayParts_WeightsSumTo100()
    {
        var parts = ShopKeywordResearchScoring.ToDisplayParts(1.0, 1.0, 1.0, 1.0, 1.0);
        Assert.Equal(ShopKeywordResearchScoring.AudienceWeight100, parts.Audience);
        Assert.Equal(ShopKeywordResearchScoring.SeasonWeight100, parts.Season);
        Assert.Equal(ShopKeywordResearchScoring.DemandWeight100, parts.Demand);
        Assert.Equal(ShopKeywordResearchScoring.GrowthWeight100, parts.Growth);
        Assert.Equal(ShopKeywordResearchScoring.CompetitionWeight100, parts.Competition);
        Assert.Equal(
            100,
            parts.Audience + parts.Season + parts.Demand + parts.Growth + parts.Competition);
    }

    [Fact]
    public void ToDisplayParts_ClampsOutOfRangeInputs()
    {
        var empty = ShopKeywordResearchScoring.ToDisplayParts(-1.0, -1.0, -1.0, -1.0, -1.0);
        Assert.Equal(0, empty.Audience + empty.Season + empty.Demand + empty.Growth + empty.Competition);

        var over = ShopKeywordResearchScoring.ToDisplayParts(9.0, 9.0, 9.0, 9.0, 9.0);
        Assert.Equal(100, over.Audience + over.Season + over.Demand + over.Growth + over.Competition);
    }

    [Fact]
    public void ScoreBreakdown_StaysWithin0To100()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var best = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            900_000,
            40,
            "+250%",
            ["Trending on TikTok", "Top search", "Low competition"],
            audience,
            weather,
            volumePercentile: 1.0);
        var worst = ShopKeywordResearchScoring.ScoreBreakdown(
            "petite sheer bodycon puffer coat",
            1,
            90_000,
            "-400%",
            [],
            audience,
            weather,
            volumePercentile: 0.0);

        Assert.InRange(best.Score, 0, 100);
        Assert.InRange(worst.Score, 0, 100);
        Assert.Equal(0, worst.Score);
    }

    [Fact]
    public void ScoreAudience_CountsHyphenVariantOfPlusSizeOnce()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));

        // garment 14 + painPoint «plus size» 2 + one plus-size hit 10.
        // Counting «plus size» and «plus-size» separately would push the body bucket to its cap.
        var breakdown = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size linen midi dress",
            800,
            30,
            "+5%",
            [],
            audience,
            weather);

        Assert.Equal(26, breakdown.Audience);
    }

    [Fact]
    public void ScoreAudience_CapsStackedStyleAdjectives()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var twoAdjectives = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage floral midi dress",
            120,
            30,
            "+2%",
            [],
            audience,
            weather);
        var sixAdjectives = ShopKeywordResearchScoring.ScoreBreakdown(
            "plus size vintage retro floral prairie puff sleeve a-line midi dress",
            120,
            30,
            "+2%",
            [],
            audience,
            weather);

        // Six style hits are worth 48 raw points before the bucket cap; stacking must add nothing.
        Assert.Equal(twoAdjectives.Audience, sixAdjectives.Audience);
        Assert.Equal(
            ShopKeywordResearchScoring.AudienceWeight100,
            sixAdjectives.Parts.Audience);
    }

    [Fact]
    public void ScoreAll_RanksHighVolumeOnNicheAboveTokenStuffedLongTail()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var bigVolume = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var stuffed = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var middle = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var scored = ShopKeywordResearchScoring.ScoreAll(
            [
                Candidate(bigVolume, "plus size denim midi dress", 200_000, "+35%"),
                Candidate(stuffed, "plus size vintage floral prairie a-line midi dress", 150, "+2%"),
                Candidate(middle, "plus size wrap midi dress", 4_000, "+8%")
            ],
            audience,
            weather);

        var big = scored.Single(s => s.Id == bigVolume);
        var longTail = scored.Single(s => s.Id == stuffed);

        Assert.False(big.LowAudienceFit);
        Assert.False(longTail.LowAudienceFit);
        Assert.True(longTail.AudienceScore > big.AudienceScore);
        Assert.True(big.Score > longTail.Score);

        var (now, _) = ShopKeywordResearchScoring.PickLockedLists(
            ShopKeywordResearchScoring.BuildShortlist(scored),
            1,
            0);
        Assert.Equal([bigVolume], now.Select(x => x.Id));
    }

    [Fact]
    public void ScoreAll_DemandRisesWithVolume()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var small = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var mid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var large = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var scored = ShopKeywordResearchScoring.ScoreAll(
            [
                Candidate(small, "plus size wrap midi dress", 1_000),
                Candidate(mid, "plus size wrap midi dress", 50_000),
                Candidate(large, "plus size wrap midi dress", 500_000)
            ],
            audience,
            weather);

        var demandBySize = scored.ToDictionary(s => s.Id, s => s.Parts.Demand);
        Assert.True(demandBySize[mid] > demandBySize[small]);
        Assert.True(demandBySize[large] > demandBySize[mid]);
    }

    [Fact]
    public void ScoreAll_RecentSurgeBeatsFlatKeywordOfSameSize()
    {
        var audience = MaturePlusVintage();
        var weather = UsSeasonWeather.Get(new DateTime(2026, 8, 14));
        var surging = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var flat = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var shrinking = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var scored = ShopKeywordResearchScoring.ScoreAll(
            [
                Candidate(surging, "plus size wrap midi dress", 5_000, "+45%"),
                Candidate(flat, "plus size wrap midi dress", 5_000, "0%"),
                Candidate(shrinking, "plus size wrap midi dress", 5_000, "-45%")
            ],
            audience,
            weather);

        var byId = scored.ToDictionary(s => s.Id);
        Assert.True(byId[surging].Parts.Growth > byId[flat].Parts.Growth);
        Assert.True(byId[surging].Score > byId[flat].Score);
        Assert.True(byId[flat].Score > byId[shrinking].Score);
    }

    [Fact]
    public void BuildOutrankedExamples_NamesOnNicheRowsLeftOut()
    {
        var picked = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var leftOut = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var shortlist = new List<ShopKeywordScored>
        {
            new(picked, "plus size wrap midi dress", 9_000, 20, null, "+9%", null, [], null, [], 70, 30, false),
            new(leftOut, "plus size linen midi dress", 12_000, 20, null, "+3%", null, [], null, [], 55, 24, false)
        };

        var outranked = ShopKeywordResearchScoring.BuildOutrankedExamples(
            shortlist,
            new HashSet<Guid> { picked });

        Assert.Equal([leftOut], outranked.Select(x => x.Id));
    }

    private static ShopKeywordCandidate Candidate(
        Guid id,
        string keyword,
        int searchVolume,
        string? volumeRing = "+2%",
        int? onlineProducts = 40)
        => new(id, keyword, searchVolume, onlineProducts, null, volumeRing, null, [], null, [], []);
}
