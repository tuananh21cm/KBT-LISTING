using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Shared.Constants;

namespace Application.Services;

/// <summary>Shop audience used to score TikTok opportunity keywords.</summary>
public sealed record ShopKeywordAudience(
    IReadOnlyList<string> CategoryNames,
    int? AgeMin,
    int? AgeMax,
    string? BodySizeCode,
    IReadOnlyList<string> StyleCodes,
    string? PainPoint,
    string? Desire,
    string? ProductGroups,
    IReadOnlyList<string> ListingTitles);

/// <summary>
/// User-facing pillar contributions (tooltip). Sums to 100 when every pillar maxes out.
/// </summary>
public readonly record struct ShopKeywordDisplayParts(
    int Audience,
    int Season,
    int Demand,
    int Growth,
    int Competition);

/// <summary>Raw parts plus the blended 0–100 score for one keyword.</summary>
public readonly record struct ShopKeywordScoreBreakdown(
    int Score,
    int Audience,
    int Season,
    int Competition,
    int Listing,
    ShopKeywordDisplayParts Parts);

/// <summary>One non-generic snapshot keyword handed to the scorer.</summary>
public sealed record ShopKeywordCandidate(
    Guid Id,
    string LeadName,
    int SearchVolume,
    int? OnlineProducts,
    string? OnlineProductsRaw,
    string? SearchVolumeRingRatio,
    string? OnlineProductsRingRatio,
    IReadOnlyList<string> LeadTagTexts,
    string? PicUrl,
    IReadOnlyList<string> PicUrls,
    IReadOnlyList<string> KeywordCategoryNames);

/// <summary>
/// One snapshot keyword after scoring. <see cref="Score"/> is the blended 0–100 rank key
/// shown in the drawer; <see cref="AudienceScore"/> stays raw because it drives the fit gate.
/// </summary>
public sealed record ShopKeywordScored(
    Guid Id,
    string LeadName,
    int SearchVolume,
    int? OnlineProducts,
    string? OnlineProductsRaw,
    string? SearchVolumeRingRatio,
    string? OnlineProductsRingRatio,
    IReadOnlyList<string> LeadTagTexts,
    string? PicUrl,
    IReadOnlyList<string> PicUrls,
    int Score,
    int AudienceScore,
    bool LowAudienceFit,
    ShopKeywordDisplayParts Parts = default);

/// <summary>
/// Deterministic generic-filter + blended scoring for shop keyword research.
/// Audience fit is a hard gate (off-niche rows are dropped outright), then surviving rows rank on
/// a single 0–100 blend so a high-volume, fast-growing keyword is not buried by stacked adjectives.
/// </summary>
public static class ShopKeywordResearchScoring
{
    /// <summary>Top rows sent to the model.</summary>
    public const int FitShortlistSize = 100;

    /// <summary>High-volume mismatches named in SKIP_THESE (no ids — model cannot pick them).</summary>
    public const int MismatchShortlistSize = 20;

    /// <summary>On-niche rows that lost the rank, named in SKIP_THESE so the copy can explain them.</summary>
    public const int OutrankedShortlistSize = 10;

    /// <summary>Raw audience points that map to a full niche-fit pillar.</summary>
    public const int AudienceScoreMax = 40;

    /// <summary>Niche-fit weight in the 0–100 score.</summary>
    public const int AudienceWeight100 = 45;

    /// <summary>Demand (search volume) weight in the 0–100 score.</summary>
    public const int DemandWeight100 = 30;

    /// <summary>Recent-growth (search-volume ring) weight in the 0–100 score.</summary>
    public const int GrowthWeight100 = 15;

    /// <summary>Season / weather weight in the 0–100 score.</summary>
    public const int SeasonWeight100 = 5;

    /// <summary>Competition weight in the 0–100 score.</summary>
    public const int CompetitionWeight100 = 5;

    /// <summary>Season raw range used when normalizing the season pillar.</summary>
    public const int DisplaySeasonMin = -16;

    /// <summary>Season raw range used when normalizing the season pillar.</summary>
    public const int DisplaySeasonMax = 20;

    /// <summary>log10 of the search volume that saturates the absolute demand term (1M).</summary>
    private const double VolumeLogCeiling = 6.0;

    /// <summary>Share of the demand pillar taken from the shop's own volume distribution.</summary>
    private const double VolumePercentileWeight = 0.65;

    /// <summary>Ring percent that saturates the growth pillar.</summary>
    private const double GrowthFullPercent = 60.0;

    /// <summary>Cap on the lead-tag nudge inside the demand pillar.</summary>
    private const double TagBonusCap = 0.10;

    /// <summary>Cap on the shrinking-search penalty.</summary>
    private const int GrowthDropPenaltyMax = 10;

    /// <summary>Cap on the off-season penalty.</summary>
    private const int SeasonPenaltyMax = 15;

    /// <summary>Penalty per weather-penalized token.</summary>
    private const int SeasonPenaltyPerHit = 8;

    /// <summary>Flat penalty when a live listing already covers the keyword.</summary>
    private const int ListingOverlapPenalty = 10;

    /// <summary>Cap on the off-audience penalty.</summary>
    private const int AudiencePenaltyMax = 40;

    /// <summary>Cap on AgeRange guidance points — guidance alone must not win the rank.</summary>
    private const int GuidanceBucketCap = 12;

    /// <summary>Cap on body/size points.</summary>
    private const int BodyBucketCap = 12;

    /// <summary>Cap on style points across every selected style.</summary>
    private const int StyleBucketCap = 14;

    private const string BackfillRationale =
        "Khớp tệp khách (category / tuổi / body / style) hơn keyword volume cao lệch ngách.";

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "for", "of", "with", "in", "on", "to", "at",
        "women", "womens", "woman", "woman's", "ladies", "lady", "girl", "girls"
    };

    private static readonly HashSet<string> GenericPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "outfit", "outfits", "dress", "dresses", "top", "tops", "shirt", "shirts",
        "pants", "jeans", "skirt", "skirts", "blouse", "blouses", "set", "sets",
        "fashion", "clothes", "clothing", "maxi dress", "mini dress", "midi dress",
        "casual dress", "casual dresses", "plus size", "plus size dress", "vintage",
        "women clothing", "womens clothing", "women's clothing", "women dress",
        "womens dress", "summer dress", "fall dress", "spring dress", "winter dress",
        "matching set", "two piece", "2 piece", "clothes for women", "dresses for women",
        "womens dresses", "women's dresses", "plus size clothing", "casual outfit",
        "curvy dresses plus size", "plus size dresses", "plus size casual dress"
    };

    /// <summary>
    /// Tokens that name the shopper, not the product — not enough to list against.
    /// </summary>
    private static readonly HashSet<string> GenericFillerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "plus", "size", "women", "womens", "woman", "ladies", "lady", "girl", "girls"
    };

    /// <summary>
    /// AgeRange product-group tokens that name a whole garment class — too broad to boost every row.
    /// </summary>
    private static readonly HashSet<string> GenericGuidanceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "dress", "dresses", "top", "tops", "pants", "jeans", "skirt", "skirts",
        "shirt", "shirts", "set", "sets", "clothes", "clothing", "outfit", "outfits",
        "fashion", "denim"
    };

    private static readonly (string Family, string[] Phrases)[] GarmentFamilies =
    [
        ("swim", ["bikini", "swimsuit", "swimwear", "swim"]),
        ("set", ["matching set", "two piece", "2 piece", "co ord", "coord"]),
        ("jumpsuit", ["jumpsuit", "romper", "overall"]),
        ("outerwear", ["jacket", "coat", "cardigan", "blazer", "hoodie", "puffer", "parka", "trench"]),
        ("skirt", ["skirt", "skirts"]),
        ("bottom", ["pants", "jeans", "trousers", "leggings", "shorts", "palazzo"]),
        ("dress", ["dress", "dresses", "gown", "sundress"]),
        ("top", ["blouse", "blouses", "tunic", "tunics", "tank top", "t shirt", "camisole",
            "tee", "shirt", "shirts", "top", "tops"])
    ];

    private static readonly string[] SexyCutPhrases =
    [
        "bodycon", "mini", "y2k", "sexy", "cutout", "cut out", "club", "going out",
        "crop top", "ruffle hem", "teen", "junior", "see through", "see-through",
        "sheer", "mesh", "backless", "slit"
    ];

    /// <summary>Vietnamese fallback when the model skipped a backfilled id.</summary>
    public static string DefaultBackfillRationale => BackfillRationale;

    /// <summary>
    /// True when ≥70% of the keyword's content tokens already appear in a live listing title.
    /// </summary>
    public static bool OverlapsListing(string? keyword, IReadOnlyList<string>? listingTitles)
    {
        if (listingTitles is null || listingTitles.Count == 0)
        {
            return false;
        }

        return ScoreListingOverlap(Normalize(keyword), listingTitles) < 0;
    }

    /// <summary>True when the keyword is too generic / short to list against.</summary>
    public static bool IsGeneric(string? keyword, IReadOnlyList<string>? shopCategoryNames = null)
    {
        var normalized = Normalize(keyword);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (GenericPhrases.Contains(normalized))
        {
            return true;
        }

        var tokens = ContentTokens(normalized);
        if (tokens.Count < 3 || CountDistinctiveTokens(tokens) < 2)
        {
            return true;
        }

        if (shopCategoryNames is { Count: > 0 })
        {
            foreach (var name in shopCategoryNames)
            {
                var cat = Normalize(name);
                if (cat.Length == 0)
                {
                    continue;
                }

                if (normalized == cat || normalized == StripCategoryPath(cat))
                {
                    return true;
                }

                var leaf = cat.Contains('/') ? Normalize(cat[(cat.LastIndexOf('/') + 1)..]) : cat;
                if (leaf.Length > 0 && normalized == leaf)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Scores every non-generic candidate of one shop. Demand is measured against this shop's own
    /// volume distribution, so a keyword earns the demand pillar only by out-searching the rest of
    /// the snapshot instead of saturating a fixed log ceiling.
    /// </summary>
    public static List<ShopKeywordScored> ScoreAll(
        IReadOnlyList<ShopKeywordCandidate> candidates,
        ShopKeywordAudience audience,
        UsSeasonWeatherInfo weather)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var ascendingVolumes = candidates.Select(c => c.SearchVolume).OrderBy(v => v).ToArray();
        var scored = new List<ShopKeywordScored>(candidates.Count);
        foreach (var row in candidates)
        {
            var breakdown = ScoreBreakdown(
                row.LeadName,
                row.SearchVolume,
                row.OnlineProducts,
                row.SearchVolumeRingRatio,
                row.LeadTagTexts,
                audience,
                weather,
                row.KeywordCategoryNames,
                row.OnlineProductsRingRatio,
                VolumePercentile(ascendingVolumes, row.SearchVolume));
            scored.Add(new ShopKeywordScored(
                row.Id,
                row.LeadName.Trim(),
                row.SearchVolume,
                row.OnlineProducts,
                row.OnlineProductsRaw,
                row.SearchVolumeRingRatio,
                row.OnlineProductsRingRatio,
                row.LeadTagTexts,
                row.PicUrl,
                row.PicUrls,
                breakdown.Score,
                breakdown.Audience,
                IsLowAudienceFit(breakdown.Audience, audience),
                breakdown.Parts));
        }

        return scored;
    }

    /// <summary>
    /// Blended 0–100 score for one keyword: niche fit 45 + demand 30 + recent growth 15 + season 5
    /// + competition 5, minus off-audience / off-season / shrinking-search / duplicate-listing
    /// penalties. A null <paramref name="volumePercentile"/> falls back to the absolute volume curve.
    /// </summary>
    public static ShopKeywordScoreBreakdown ScoreBreakdown(
        string keyword,
        int searchVolume,
        int? onlineProducts,
        string? volumeRing,
        IReadOnlyList<string>? leadTagTexts,
        ShopKeywordAudience audience,
        UsSeasonWeatherInfo weather,
        IReadOnlyList<string>? keywordCategoryNames = null,
        string? onlineProductsRing = null,
        double? volumePercentile = null)
    {
        var normalized = Normalize(keyword);
        var audienceScore = ScoreAudience(normalized, audience, keywordCategoryNames);
        var seasonScore = ScoreSeason(normalized, weather);
        var competition = ScoreCompetition(onlineProducts);
        var listingScore = ScoreListingOverlap(normalized, audience.ListingTitles);
        var (growth01, growthPenalty) = ScoreGrowth(volumeRing);
        var parts = ToDisplayParts(
            ToAudience01(audienceScore),
            ToSeason01(seasonScore),
            ScoreDemand01(searchVolume, volumePercentile, leadTagTexts),
            growth01,
            ScoreCompetition01(competition, onlineProductsRing));

        var score = parts.Audience + parts.Season + parts.Demand + parts.Growth + parts.Competition;
        if (audienceScore < 0)
        {
            score -= Math.Min(AudiencePenaltyMax, -audienceScore);
        }

        score -= Math.Min(
            SeasonPenaltyMax,
            Hits(normalized, weather.PenalizeTokens) * SeasonPenaltyPerHit);
        score -= growthPenalty;
        if (listingScore < 0)
        {
            score -= ListingOverlapPenalty;
        }

        return new ShopKeywordScoreBreakdown(
            Math.Clamp(score, 0, 100),
            audienceScore,
            seasonScore,
            competition,
            listingScore,
            parts);
    }

    /// <summary>Pillar contributions for the score tooltip (before penalties).</summary>
    public static ShopKeywordDisplayParts ToDisplayParts(
        double audience01,
        double season01,
        double demand01,
        double growth01,
        double competition01)
        => new(
            Weigh(AudienceWeight100, audience01),
            Weigh(SeasonWeight100, season01),
            Weigh(DemandWeight100, demand01),
            Weigh(GrowthWeight100, growth01),
            Weigh(CompetitionWeight100, competition01));

    /// <summary>Raw audience points mapped to 0–1 for the niche-fit pillar.</summary>
    public static double ToAudience01(int audienceScore)
        => Math.Clamp(audienceScore, 0, AudienceScoreMax) / (double)AudienceScoreMax;

    /// <summary>Raw season points mapped to 0–1 for the season pillar.</summary>
    public static double ToSeason01(int seasonScore)
        => Math.Clamp(
            (seasonScore - DisplaySeasonMin) / (double)(DisplaySeasonMax - DisplaySeasonMin),
            0.0,
            1.0);

    private static int Weigh(int weight, double part01)
        => (int)Math.Round(weight * Math.Clamp(part01, 0.0, 1.0));

    /// <summary>
    /// Demand blends the keyword's rank inside this shop's snapshot with an absolute volume curve,
    /// so it is strictly increasing in volume and never flattens out at a ceiling.
    /// </summary>
    private static double ScoreDemand01(
        int searchVolume,
        double? volumePercentile,
        IReadOnlyList<string>? leadTagTexts)
    {
        var volume = Math.Max(0.0, searchVolume);
        var absolute = Math.Clamp(Math.Log10(volume + 1.0) / VolumeLogCeiling, 0.0, 1.0);
        var relative = volumePercentile is null
            ? absolute
            : Math.Clamp(volumePercentile.Value, 0.0, 1.0);
        var blended = (VolumePercentileWeight * relative)
            + ((1.0 - VolumePercentileWeight) * absolute);
        return Math.Clamp(blended + TagBonus01(leadTagTexts), 0.0, 1.0);
    }

    private static double TagBonus01(IReadOnlyList<string>? leadTagTexts)
    {
        var bonus = 0.0;
        foreach (var tag in leadTagTexts ?? [])
        {
            var t = tag.Trim();
            if (t.Contains("Trending", StringComparison.OrdinalIgnoreCase)
                || t.Contains("TikTok", StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.05;
            }
            else if (t.Contains("Top search", StringComparison.OrdinalIgnoreCase)
                     || t.Contains("hot search", StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.04;
            }
            else if (t.Contains("low competition", StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.03;
            }
        }

        return Math.Min(TagBonusCap, bonus);
    }

    /// <summary>
    /// Recent search growth from the volume ring. A shrinking keyword earns nothing and is
    /// additionally penalized, so «đang tăng gần đây» beats a flat keyword of the same size.
    /// </summary>
    private static (double Growth01, int Penalty) ScoreGrowth(string? volumeRing)
    {
        var pct = ParseRingPercent(volumeRing);
        if (pct is null || pct.Value == 0)
        {
            return (0.0, 0);
        }

        if (pct.Value > 0)
        {
            return (Math.Clamp(pct.Value / GrowthFullPercent, 0.0, 1.0), 0);
        }

        return (0.0, (int)Math.Round(Math.Min(GrowthDropPenaltyMax, -pct.Value / 8.0)));
    }

    /// <summary>Competition sweet-spot, nudged down when rival listings are still piling in.</summary>
    private static double ScoreCompetition01(int competitionPoints, string? onlineProductsRing)
    {
        var basis = NormalizeCompetition(competitionPoints);
        var ring = ParseRingPercent(onlineProductsRing);
        if (ring is > 0)
        {
            basis -= Math.Min(0.2, ring.Value / 200.0);
        }
        else if (ring is < 0)
        {
            basis += Math.Min(0.1, -ring.Value / 200.0);
        }

        return Math.Clamp(basis, 0.0, 1.0);
    }

    private static double NormalizeCompetition(int competitionPoints) => competitionPoints switch
    {
        >= 10 => 1.0,
        4 => 0.4,
        < 0 => 0.0,
        _ => 0.5
    };

    /// <summary>
    /// Share of the snapshot this keyword out-searches. Ties share the mid-rank, so a snapshot
    /// where every keyword has the same volume lands everyone in the middle instead of the floor.
    /// Null when the snapshot is too small for a distribution to mean anything.
    /// </summary>
    private static double? VolumePercentile(int[] ascendingVolumes, int searchVolume)
    {
        if (ascendingVolumes.Length < 2)
        {
            return null;
        }

        var less = LowerBound(ascendingVolumes, searchVolume);
        var lessOrEqual = LowerBound(ascendingVolumes, searchVolume + 1);
        var midRank = (less + lessOrEqual - 1) / 2.0;
        return Math.Clamp(midRank / (ascendingVolumes.Length - 1), 0.0, 1.0);
    }

    private static int LowerBound(int[] ascending, int value)
    {
        var lo = 0;
        var hi = ascending.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (ascending[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>
    /// Hard mismatch when the shop has a real audience profile and the keyword scores ≤ 0;
    /// otherwise keep the older strongly-negative cutoff.
    /// </summary>
    public static bool IsLowAudienceFit(int audienceScore, ShopKeywordAudience audience)
        => HasAudienceProfile(audience) ? audienceScore <= 0 : audienceScore <= -15;

    /// <summary>
    /// Rows that clear the audience gate, ranked by the blended score.
    /// Audience decides who is eligible; demand and growth decide the order.
    /// </summary>
    public static List<ShopKeywordScored> BuildShortlist(IReadOnlyList<ShopKeywordScored> scored)
    {
        if (scored.Count == 0)
        {
            return [];
        }

        var positive = scored.Where(s => s.AudienceScore > 0).ToList();
        var pool = positive.Count > 0
            ? positive
            : scored.Where(s => !s.LowAudienceFit).ToList();

        return Rank(pool).Take(FitShortlistSize).ToList();
    }

    /// <summary>
    /// Stable listNow / upcoming ids: rank is deterministic so 10 identical calls return the same keywords.
    /// </summary>
    public static (List<ShopKeywordScored> Now, List<ShopKeywordScored> Upcoming) PickLockedLists(
        IReadOnlyList<ShopKeywordScored> fit,
        int listNowCount,
        int upcomingCount)
    {
        var ranked = Rank(fit).ToList();
        var nowTake = Math.Clamp(listNowCount, 0, ranked.Count);
        var now = ranked.Take(nowTake).ToList();
        var nowIds = now.Select(s => s.Id).ToHashSet();
        var upcoming = ranked
            .Where(s => !nowIds.Contains(s.Id))
            .Take(Math.Max(0, upcomingCount))
            .ToList();
        return (now, upcoming);
    }

    /// <summary>
    /// Deterministic order: blended score, then volume, then name so ties never shuffle.
    /// </summary>
    private static IOrderedEnumerable<ShopKeywordScored> Rank(IReadOnlyList<ShopKeywordScored> rows)
        => rows
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.SearchVolume)
            .ThenBy(s => s.LeadName, StringComparer.OrdinalIgnoreCase);

    /// <summary>High-volume mismatches for SKIP_THESE (names only).</summary>
    public static List<ShopKeywordScored> BuildMismatchExamples(
        IReadOnlyList<ShopKeywordScored> scored,
        IReadOnlySet<Guid> fitIds)
        => scored
            .Where(s => s.LowAudienceFit && !fitIds.Contains(s.Id))
            .OrderByDescending(s => s.SearchVolume)
            .Take(MismatchShortlistSize)
            .ToList();

    /// <summary>
    /// On-niche rows that cleared the gate but lost the rank, highest volume first.
    /// Named in SKIP_THESE so the analysis can explain a missing high-volume keyword.
    /// </summary>
    public static List<ShopKeywordScored> BuildOutrankedExamples(
        IReadOnlyList<ShopKeywordScored> shortlist,
        IReadOnlySet<Guid> lockedIds)
        => shortlist
            .Where(s => !lockedIds.Contains(s.Id))
            .OrderByDescending(s => s.SearchVolume)
            .Take(OutrankedShortlistSize)
            .ToList();

    /// <summary>
    /// Keep model ids that exist in the allowed set, then fill remaining slots from audience rank.
    /// </summary>
    public static List<Guid> FillMissingIds(
        IReadOnlyList<Guid> picked,
        IReadOnlyList<ShopKeywordScored> fitPool,
        int take)
    {
        if (take <= 0 || fitPool.Count == 0)
        {
            return [];
        }

        var allowed = fitPool.Select(s => s.Id).ToHashSet();
        var result = new List<Guid>(take);
        var seen = new HashSet<Guid>();
        foreach (var id in picked)
        {
            if (!allowed.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
            if (result.Count >= take)
            {
                return result;
            }
        }

        foreach (var row in fitPool)
        {
            if (!seen.Add(row.Id))
            {
                continue;
            }

            result.Add(row.Id);
            if (result.Count >= take)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Compact line for the model: id|name|vol|online|ring|tags|aud|score|tok.</summary>
    public static string ToCandidateLine(ShopKeywordScored row)
    {
        var tags = string.Join(",", row.LeadTagTexts.Take(3));
        var online = row.OnlineProducts?.ToString(CultureInfo.InvariantCulture)
            ?? row.OnlineProductsRaw
            ?? "-";
        var ring = string.IsNullOrWhiteSpace(row.SearchVolumeRingRatio) ? "-" : row.SearchVolumeRingRatio.Trim();
        var name = row.LeadName.Replace('|', '/').Replace('\n', ' ').Trim();
        var tok = string.Join(",", KeywordMatchPhrases.ContentTokens(row.LeadName));
        return $"{row.Id:N}|{name}|{row.SearchVolume}|{online}|{ring}|{tags}|{row.AudienceScore}|{row.Score}|{tok}";
    }

    /// <summary>
    /// Keep model ids that exist in the allowed set, are unique, and are not generic.
    /// </summary>
    public static List<Guid> FilterValidIds(
        IEnumerable<string?> rawIds,
        IReadOnlyDictionary<Guid, ShopKeywordScored> byId,
        IReadOnlyList<string> shopCategoryNames,
        int take)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var raw in rawIds)
        {
            if (!TryParseId(raw, out var id) || !seen.Add(id) || !byId.TryGetValue(id, out var row))
            {
                continue;
            }

            if (row.LowAudienceFit)
            {
                continue;
            }

            if (IsGeneric(row.LeadName, shopCategoryNames))
            {
                continue;
            }

            result.Add(id);
            if (result.Count >= take)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Parse a Guid from the model (N or D format).</summary>
    public static bool TryParseId(string? raw, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        return Guid.TryParse(text, out id) || Guid.TryParseExact(text, "N", out id);
    }

    /// <summary>Lowercase collapsed keyword for matching.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var prevSpace = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                prevSpace = false;
            }
            else if (ch is '\'' or '&')
            {
                continue;
            }
            else
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Content tokens after dropping stop-words.</summary>
    public static List<string> ContentTokens(string normalized)
    {
        var tokens = new List<string>();
        foreach (var part in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (StopWords.Contains(part) || part.Length == 1)
            {
                continue;
            }

            tokens.Add(part);
        }

        return tokens;
    }

    private static int CountDistinctiveTokens(IReadOnlyList<string> tokens)
    {
        var n = 0;
        foreach (var token in tokens)
        {
            if (GenericGuidanceTokens.Contains(token) || GenericFillerTokens.Contains(token))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    private static string StripCategoryPath(string normalized)
    {
        var cut = normalized.LastIndexOf('·');
        if (cut < 0)
        {
            return normalized;
        }

        return normalized[(cut + 1)..].Trim();
    }

    private static bool HasAudienceProfile(ShopKeywordAudience audience)
    {
        var body = (audience.BodySizeCode ?? string.Empty).Trim();
        if (body.Length > 0
            && !string.Equals(body, ShopBodySizeCodes.Regular, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (audience.StyleCodes.Count > 0)
        {
            return true;
        }

        return DetectGarmentFamilies(audience.CategoryNames).Count > 0;
    }

    private static int ScoreAudience(
        string normalized,
        ShopKeywordAudience audience,
        IReadOnlyList<string>? keywordCategoryNames)
    {
        var score = 0;
        var body = (audience.BodySizeCode ?? string.Empty).Trim().ToLowerInvariant();
        var styles = audience.StyleCodes
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mature = audience.AgeMin is >= 35;
        var young = audience.AgeMax is <= 34;
        var allowsSexy = styles.Contains(ShopStyleCodes.Sexy) || styles.Contains(ShopStyleCodes.Party);
        var conservative = styles.Contains(ShopStyleCodes.Vintage)
            || styles.Contains(ShopStyleCodes.Modest)
            || styles.Contains(ShopStyleCodes.Elegant)
            || styles.Contains(ShopStyleCodes.Casual);

        score += ScoreGarmentCategory(normalized, audience.CategoryNames, keywordCategoryNames);

        var guidance = (CountHits(normalized, SplitGuidance(audience.ProductGroups)) * 6)
            + (CountHits(normalized, SplitGuidance(audience.Desire)) * 3)
            + (CountHits(normalized, SplitGuidance(audience.PainPoint)) * 2);
        score += Math.Min(GuidanceBucketCap, guidance);

        var (bodyBonus, bodyPenalty) = ScoreBodySize(normalized, body, allowsSexy);
        score += Math.Min(BodyBucketCap, bodyBonus) - bodyPenalty;

        score += Math.Min(StyleBucketCap, ScoreStyles(normalized, styles, mature));

        var sexyCuts = ContainsAny(normalized, SexyCutPhrases);
        if (sexyCuts)
        {
            if (allowsSexy)
            {
                score += mature ? 2 : 6;
            }
            else if (conservative || mature)
            {
                score -= mature && conservative ? 40 : 24;
                score = Math.Min(score, 0);
            }
            else if (young)
            {
                score += 4;
            }
            else
            {
                score -= 8;
            }
        }

        if (HasUnselectedStyleClash(normalized, styles, allowsSexy))
        {
            score -= 40;
            score = Math.Min(score, 0);
        }

        return score;
    }

    /// <summary>
    /// Body/size evidence split into a capped bonus and an uncapped penalty, so a keyword cannot
    /// climb the rank by repeating size words while a real mismatch can still sink it.
    /// </summary>
    private static (int Bonus, int Penalty) ScoreBodySize(
        string normalized,
        string body,
        bool allowsSexy)
    {
        if (body == ShopBodySizeCodes.Plus)
        {
            var plusHits = Hits(
                normalized,
                ["plus size", "curvy", "tunic", "empire", "tummy", "wide leg"]);
            var penalty = plusHits == 0 ? 12 : 0;
            if (ContainsAny(normalized, ["petite"]))
            {
                penalty += 20;
            }

            if (ContainsAny(normalized, ["skinny", "bodycon"]) && !allowsSexy)
            {
                penalty += 22;
            }

            return (plusHits * 10, penalty);
        }

        if (body == ShopBodySizeCodes.Petite)
        {
            return (
                Hits(normalized, ["petite"]) * 8,
                ContainsAny(normalized, ["plus size", "tall"]) ? 16 : 0);
        }

        if (body == ShopBodySizeCodes.Tall)
        {
            return (
                Hits(normalized, ["tall", "longline"]) * 8,
                ContainsAny(normalized, ["petite"]) ? 16 : 0);
        }

        if (body == ShopBodySizeCodes.Midsize)
        {
            return (Hits(normalized, ["midsize", "mid size", "tunic", "stretch"]) * 6, 0);
        }

        if (body == ShopBodySizeCodes.Curvy)
        {
            return (Hits(normalized, ["curvy", "hourglass", "fit and flare", "wrap"]) * 8, 0);
        }

        if (body == ShopBodySizeCodes.Regular)
        {
            return (0, ContainsAny(normalized, ["plus size", "petite", "tall"]) ? 8 : 0);
        }

        return (0, 0);
    }

    private static int ScoreStyles(string normalized, HashSet<string> styles, bool mature)
    {
        var score = 0;
        if (styles.Contains(ShopStyleCodes.Vintage))
        {
            score += Hits(normalized, ["vintage", "retro", "floral", "prairie", "puff sleeve", "a-line"]) * 8;
        }

        if (styles.Contains(ShopStyleCodes.Casual))
        {
            score += Hits(normalized, ["casual", "comfortable", "everyday", "tunic", "loose", "lounge"]) * 5;
        }

        if (styles.Contains(ShopStyleCodes.Elegant) || styles.Contains(ShopStyleCodes.Modest))
        {
            score += Hits(normalized, ["midi", "wrap", "elegant", "modest", "long sleeve", "high neck"]) * 6;
        }

        if (styles.Contains(ShopStyleCodes.Boho))
        {
            score += Hits(normalized, ["boho", "maxi", "peasant", "crochet", "flowy"]) * 6;
        }

        if (styles.Contains(ShopStyleCodes.Glamorous))
        {
            score += Hits(normalized, ["satin", "velvet", "pearl", "regal", "luxe", "sequin", "glamorous"]) * 6;
        }

        if (styles.Contains(ShopStyleCodes.Cute) && !mature)
        {
            score += Hits(normalized, ["cute", "bow", "ruffle"]) * 4;
        }

        return score;
    }

    private static bool HasUnselectedStyleClash(
        string normalized,
        HashSet<string> styles,
        bool allowsSexy)
    {
        if (ContainsAny(normalized, ["boho", "bohemian"]) && !styles.Contains(ShopStyleCodes.Boho))
        {
            return true;
        }

        if (ContainsAny(normalized, ["sequin", "evening", "cocktail"])
            && !allowsSexy
            && !styles.Contains(ShopStyleCodes.Party))
        {
            return true;
        }

        return false;
    }

    private static int ScoreGarmentCategory(
        string normalized,
        IReadOnlyList<string> shopCategoryNames,
        IReadOnlyList<string>? keywordCategoryNames)
    {
        var shopFamilies = DetectGarmentFamilies(shopCategoryNames);
        if (shopFamilies.Count == 0)
        {
            return 0;
        }

        var textFamilies = DetectGarmentFamilies([normalized]);
        var keywordFamilies = textFamilies.Count > 0
            ? textFamilies
            : DetectGarmentFamilies(keywordCategoryNames ?? []);

        if (keywordFamilies.Count == 0)
        {
            return 0;
        }

        if (keywordFamilies.IsSubsetOf(shopFamilies))
        {
            return 14;
        }

        return -40;
    }

    private static HashSet<string> DetectGarmentFamilies(IReadOnlyList<string> texts)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (texts.Count == 0)
        {
            return families;
        }

        var combined = Normalize(string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))));
        if (combined.Length == 0)
        {
            return families;
        }

        foreach (var (family, phrases) in GarmentFamilies)
        {
            if (ContainsAny(combined, phrases))
            {
                families.Add(family);
            }
        }

        return families;
    }

    private static int ScoreSeason(string normalized, UsSeasonWeatherInfo weather)
    {
        var score = 0;
        score += Hits(normalized, weather.BoostTokens) * 5;
        score -= Hits(normalized, weather.PenalizeTokens) * 16;
        return score;
    }

    private static int ScoreCompetition(int? onlineProducts)
    {
        if (onlineProducts is >= 10 and <= 800)
        {
            return 10;
        }

        if (onlineProducts is > 5000)
        {
            return -8;
        }

        if (onlineProducts is >= 0 and < 10)
        {
            return 4;
        }

        return 0;
    }

    private static int ScoreListingOverlap(string normalized, IReadOnlyList<string> listingTitles)
    {
        var tokens = ContentTokens(normalized);
        if (tokens.Count == 0 || listingTitles.Count == 0)
        {
            return 0;
        }

        foreach (var title in listingTitles)
        {
            var t = Normalize(title);
            if (t.Length == 0)
            {
                continue;
            }

            var hits = tokens.Count(tok => t.Contains(tok, StringComparison.Ordinal));
            if (hits * 10 >= tokens.Count * 7)
            {
                return -16;
            }
        }

        return 0;
    }

    /// <summary>Parses TikTok ring text such as <c>+6.23%</c> / <c>-12%</c>.</summary>
    public static double? ParseRingPercent(string? ring)
    {
        if (string.IsNullOrWhiteSpace(ring))
        {
            return null;
        }

        var compact = ring.Trim().Replace(",", "").Replace("−", "-").Replace(" ", "");
        var match = Regex.Match(compact, @"^([+-])?(\d+(?:\.\d+)?)%?$");
        if (!match.Success)
        {
            return null;
        }

        if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        if (match.Groups[1].Value == "-")
        {
            n = -n;
        }

        return n;
    }

    /// <summary>
    /// Counts matching phrases after normalizing and de-duplicating them, so spelling variants
    /// of the same phrase (<c>plus size</c> / <c>plus-size</c>) cannot score the keyword twice.
    /// </summary>
    private static int Hits(string normalized, IReadOnlyList<string> phrases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var phrase in phrases)
        {
            var p = Normalize(phrase);
            if (p.Length == 0 || !seen.Add(p))
            {
                continue;
            }

            if (ContainsNormalizedPhrase(normalized, p))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountHits(string normalized, IReadOnlyList<string> phrases) =>
        Hits(normalized, phrases);

    private static bool ContainsAny(string normalized, IReadOnlyList<string> phrases) =>
        Hits(normalized, phrases) > 0;

    /// <summary>
    /// Multi-word phrases use substring; single tokens must match a whole content token
    /// so <c>short</c> does not fire on <c>short sleeve</c> and <c>mini</c> stays a length word.
    /// </summary>
    private static bool ContainsNormalizedPhrase(string normalized, string phrase)
    {
        if (phrase.Contains(' '))
        {
            return normalized.Contains(phrase, StringComparison.Ordinal);
        }

        foreach (var token in ContentTokens(normalized))
        {
            if (string.Equals(token, phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitGuidance(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', ';', '/', '·', '+', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(s => s.Length > 2 && !GenericGuidanceTokens.Contains(s) && IsEnglishPhrase(s))
            .ToList();
    }

    private static bool IsEnglishPhrase(string normalized)
    {
        var hasLetter = false;
        foreach (var ch in normalized)
        {
            if (ch == ' ' || char.IsDigit(ch))
            {
                continue;
            }

            if (ch is < 'a' or > 'z')
            {
                return false;
            }

            hasLetter = true;
        }

        return hasLetter;
    }
}
