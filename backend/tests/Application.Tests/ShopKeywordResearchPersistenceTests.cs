using Application.DTOs.ProductOpportunity;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ShopKeywordResearchPersistenceTests
{
    [Fact]
    public void Serialize_RoundtripsCamelCasePayload()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var synced = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
        var dto = new ShopKeywordResearchDto
        {
            AnalysisPoints = ["Ngách plus vintage midi"],
            WeatherNote = "Cuối hè Mỹ còn nóng",
            LastSyncedAt = synced,
            SnapshotChanged = true,
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Id = id,
                    Keyword = "plus size vintage midi dress",
                    SearchVolume = 1200,
                    IsSaved = true
                }
            ]
        };

        var json = ShopKeywordResearchPersistence.Serialize(dto);
        Assert.Contains("\"analysisPoints\"", json, StringComparison.Ordinal);
        Assert.Contains("\"snapshotChanged\":false", json, StringComparison.Ordinal);

        var parsed = ShopKeywordResearchPersistence.Deserialize(json);
        Assert.NotNull(parsed);
        Assert.Equal("Ngách plus vintage midi", parsed!.AnalysisPoints[0]);
        Assert.Equal("plus size vintage midi dress", parsed.ListNow[0].Keyword);
        Assert.Equal(id, parsed.ListNow[0].Id);
        Assert.Equal("snapshot", parsed.Source);
        Assert.False(parsed.SnapshotChanged);
    }

    [Fact]
    public void Deserialize_MissingSource_DefaultsToSnapshot()
    {
        const string json = """{"analysisPoints":[],"listNow":[],"upcoming":[]}""";
        var parsed = ShopKeywordResearchPersistence.Deserialize(json);
        Assert.NotNull(parsed);
        Assert.Equal("snapshot", parsed!.Source);
    }

    [Fact]
    public void Serialize_PersistsAudienceSuggestSource()
    {
        var dto = new ShopKeywordResearchDto
        {
            Source = ShopKeywordResearchSuggest.SourceAudienceSuggest,
            AnalysisPoints = ["Shop chưa có snapshot"]
        };
        var json = ShopKeywordResearchPersistence.Serialize(dto);
        Assert.Contains("\"source\":\"audienceSuggest\"", json, StringComparison.Ordinal);

        var parsed = ShopKeywordResearchPersistence.Deserialize(json);
        Assert.Equal(ShopKeywordResearchSuggest.SourceAudienceSuggest, parsed!.Source);
    }

    [Fact]
    public void Deserialize_CorruptJson_ReturnsNull()
        => Assert.Null(ShopKeywordResearchPersistence.Deserialize("{not-json"));

    [Fact]
    public void Hydrate_RematchesSnapshotIdMetricsAndSavedFlag()
    {
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var storedSynced = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentSynced = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        var analyzedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        var dto = new ShopKeywordResearchDto
        {
            LastSyncedAt = storedSynced,
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Id = oldId,
                    Keyword = "Plus Size Vintage Midi Dress",
                    SearchVolume = 100,
                    OnlineProducts = 10,
                    Rationale = "khớp plus vintage",
                    IsSaved = false
                }
            ],
            Upcoming =
            [
                new ShopKeywordResearchItemDto
                {
                    Id = Guid.NewGuid(),
                    Keyword = "gone keyword",
                    SearchVolume = 50,
                    IsSaved = true
                }
            ]
        };

        var byKeyword = new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["plus size vintage midi dress"] = new(
                newId,
                "plus size vintage midi dress",
                2400,
                88,
                null,
                "+6%",
                ["Hot"],
                "https://img/new",
                ["https://img/new"])
        };
        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "plus size vintage midi dress"
        };

        ShopKeywordResearchPersistence.Hydrate(dto, byKeyword, saved, analyzedAt, currentSynced);

        Assert.Equal(analyzedAt, dto.AnalyzedAt);
        Assert.True(dto.SnapshotChanged);
        Assert.Equal(newId, dto.ListNow[0].Id);
        Assert.Equal(2400, dto.ListNow[0].SearchVolume);
        Assert.Equal(88, dto.ListNow[0].OnlineProducts);
        Assert.Equal("+6%", dto.ListNow[0].SearchVolumeRingRatio);
        Assert.True(dto.ListNow[0].IsSaved);
        Assert.Equal("khớp plus vintage", dto.ListNow[0].Rationale);
        Assert.False(dto.Upcoming[0].IsSaved);
        Assert.Equal(50, dto.Upcoming[0].SearchVolume);
        Assert.Equal(1, dto.ListNow[0].Rank);
        Assert.Equal(1, dto.Upcoming[0].Rank);
    }

    [Fact]
    public void Hydrate_DoesNotStampSearchQuery_ServiceAppliesLiveBodySize()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto { Keyword = "Swing Floral A-Line Dress" }
            ]
        };
        ShopKeywordResearchPersistence.Hydrate(
            dto,
            new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(),
            new HashSet<string>(),
            new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
            currentSyncedAt: null);

        Assert.Equal(string.Empty, dto.ListNow[0].SearchQuery);
        Assert.Null(dto.ListNow[0].SheinUrl);

        AudienceSearchQuery.Apply(dto, "plus");
        Assert.Equal("plus size swing floral a-line dress", dto.ListNow[0].SearchQuery);
        Assert.NotNull(dto.ListNow[0].SheinUrl);
    }

    [Fact]
    public void Hydrate_AudienceSuggest_SetsSavedWithoutSnapshotRematch()
    {
        var id = Guid.NewGuid();
        var dto = new ShopKeywordResearchDto
        {
            Source = ShopKeywordResearchSuggest.SourceAudienceSuggest,
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Id = id,
                    Keyword = "plus size vintage midi dress",
                    SearchVolume = 0,
                    IsSaved = false
                }
            ]
        };

        ShopKeywordResearchPersistence.Hydrate(
            dto,
            new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(
                StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "plus size vintage midi dress" },
            new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
            currentSyncedAt: null);

        Assert.True(dto.ListNow[0].IsSaved);
        Assert.Equal(id, dto.ListNow[0].Id);
        Assert.Equal(0, dto.ListNow[0].SearchVolume);
        Assert.False(dto.SnapshotChanged);
    }

    [Fact]
    public void Hydrate_SameSyncedAt_IsNotSnapshotChanged()
    {
        var synced = new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc);
        var dto = new ShopKeywordResearchDto { LastSyncedAt = synced };
        ShopKeywordResearchPersistence.Hydrate(
            dto,
            new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(),
            new HashSet<string>(),
            synced,
            synced);
        Assert.False(dto.SnapshotChanged);
    }

    [Fact]
    public void IndexKeywordNiche_ListNowWinsOverUpcomingAndSkipsBlank()
    {
        var research = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Keyword = "Plus Size Midi Dress",
                    Rationale = "list now",
                    Fit = "now"
                },
                new ShopKeywordResearchItemDto { Keyword = "  " }
            ],
            Upcoming =
            [
                new ShopKeywordResearchItemDto
                {
                    Keyword = "plus size midi dress",
                    Rationale = "upcoming dup",
                    Fit = "later"
                },
                new ShopKeywordResearchItemDto
                {
                    Keyword = "fall cardigan",
                    Rationale = "mùa",
                    Fit = "đón đầu"
                }
            ]
        };

        var map = ShopKeywordResearchPersistence.IndexKeywordNiche(research);
        Assert.Equal(2, map.Count);
        Assert.Equal(ShopKeywordResearchPersistence.LaneListNow, map["plus size midi dress"].Lane);
        Assert.Equal("list now", map["plus size midi dress"].Rationale);
        Assert.Equal(ShopKeywordResearchPersistence.LaneUpcoming, map["fall cardigan"].Lane);
        Assert.Equal("đón đầu", map["fall cardigan"].Fit);
        Assert.Empty(map["plus size midi dress"].MustTokens);
    }

    [Theory]
    [InlineData(
        "Floral maxi dress hợp cuối hè và tệp thích nổi bật, vol 10645.",
        "Floral maxi dress hợp cuối hè và tệp thích nổi bật.")]
    [InlineData("khớp plus vintage, vol 1,200", "khớp plus vintage")]
    [InlineData("midi floral (vol 880).", "midi floral.")]
    [InlineData("khớp plus vintage", "khớp plus vintage")]
    [InlineData("vol 10645.", null)]
    public void StripDuplicateVolumeMetric_RemovesVolNumber(string? input, string? expected)
        => Assert.Equal(expected, ShopKeywordResearchPersistence.StripDuplicateVolumeMetric(input));

    [Fact]
    public void Hydrate_StripsDuplicateVolumeFromRationale()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Keyword = "floral maxi dress",
                    Rationale = "Floral maxi dress hợp cuối hè và tệp thích nổi bật, vol 10645."
                }
            ]
        };

        ShopKeywordResearchPersistence.Hydrate(
            dto,
            new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(),
            new HashSet<string>(),
            DateTime.UtcNow,
            null);

        Assert.Equal(
            "Floral maxi dress hợp cuối hè và tệp thích nổi bật.",
            dto.ListNow[0].Rationale);
    }

    [Fact]
    public void Serialize_RoundtripsMustTokens()
    {
        var dto = new ShopKeywordResearchDto
        {
            ListNow =
            [
                new ShopKeywordResearchItemDto
                {
                    Keyword = "midi dress",
                    MustTokens = ["dress"]
                }
            ]
        };
        var json = ShopKeywordResearchPersistence.Serialize(dto);
        Assert.Contains("\"mustTokens\"", json, StringComparison.Ordinal);

        var parsed = ShopKeywordResearchPersistence.Deserialize(json);
        Assert.Equal(["dress"], parsed!.ListNow[0].MustTokens);

        var map = ShopKeywordResearchPersistence.IndexKeywordNiche(parsed);
        Assert.Equal(["dress"], map["midi dress"].MustTokens);
    }

    [Fact]
    public void RemoveKeywords_DropsListNowAndUpcoming_KeepsShopCopy()
    {
        var dto = new ShopKeywordResearchDto
        {
            AnalysisPoints = ["Ngách plus midi"],
            WeatherNote = "Cuối hè Mỹ còn nóng",
            Categories = ["Casual Dresses"],
            ListNow =
            [
                new ShopKeywordResearchItemDto { Keyword = "Plus Size Midi Dress", Rationale = "now" },
                new ShopKeywordResearchItemDto { Keyword = "keep me", Rationale = "stay" }
            ],
            Upcoming =
            [
                new ShopKeywordResearchItemDto { Keyword = "plus size midi dress", Rationale = "dup upcoming" },
                new ShopKeywordResearchItemDto { Keyword = "fall cardigan", Rationale = "mùa" }
            ]
        };

        var keys = new HashSet<string>(StringComparer.Ordinal) { "plus size midi dress" };
        var removed = ShopKeywordResearchPersistence.RemoveKeywords(dto, keys);

        Assert.Equal(2, removed);
        Assert.Equal(["keep me"], dto.ListNow.Select(x => x.Keyword));
        Assert.Equal(["fall cardigan"], dto.Upcoming.Select(x => x.Keyword));
        Assert.Equal(1, dto.ListNow[0].Rank);
        Assert.Equal(1, dto.Upcoming[0].Rank);
        Assert.Equal(["Ngách plus midi"], dto.AnalysisPoints);
        Assert.Equal("Cuối hè Mỹ còn nóng", dto.WeatherNote);
        Assert.Equal(["Casual Dresses"], dto.Categories);
    }

    [Fact]
    public void TryRemoveKeywords_RewritesPayload_NullWhenAlreadyClean()
    {
        var dto = new ShopKeywordResearchDto
        {
            AnalysisPoints = ["keep"],
            ListNow = [new ShopKeywordResearchItemDto { Keyword = "gone keyword" }]
        };
        var json = ShopKeywordResearchPersistence.Serialize(dto);
        var keys = new HashSet<string>(StringComparer.Ordinal) { "gone keyword" };

        var next = ShopKeywordResearchPersistence.TryRemoveKeywords(json, keys);
        Assert.NotNull(next);
        var parsed = ShopKeywordResearchPersistence.Deserialize(next);
        Assert.Empty(parsed!.ListNow);
        Assert.Equal(["keep"], parsed.AnalysisPoints);

        Assert.Null(ShopKeywordResearchPersistence.TryRemoveKeywords(next, keys));
        Assert.Null(ShopKeywordResearchPersistence.TryRemoveKeywords("{not-json", keys));
        Assert.Null(ShopKeywordResearchPersistence.TryRemoveKeywords(json, new HashSet<string>()));
    }

    [Fact]
    public void IndexKeywordNiche_NullResearch_IsEmpty()
        => Assert.Empty(ShopKeywordResearchPersistence.IndexKeywordNiche(null));
}
