using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.ProductOpportunity;

namespace Application.Services;

/// <summary>
/// Serialize / hydrate persisted «Shop này bán gì» payloads.
/// </summary>
public static class ShopKeywordResearchPersistence
{
    /// <summary>camelCase JSON matching the HTTP API.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex DuplicateVolumeMetric = new(
        @"[\s,;:—–-]*\(?\bvol(?:ume)?\s*:?\s*\d[\d.,]*\)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Snapshot fields copied onto a saved keyword when the current lead still exists.</summary>
    public sealed record OpportunitySnapshot(
        Guid Id,
        string LeadName,
        int SearchVolume,
        int? OnlineProducts,
        string? OnlineProductsRaw,
        string? SearchVolumeRingRatio,
        IReadOnlyList<string> LeadTagTexts,
        string? PicUrl,
        IReadOnlyList<string> PicUrls);

    /// <summary>Serialize a DTO for jsonb storage (live flags cleared).</summary>
    public static string Serialize(ShopKeywordResearchDto dto)
    {
        dto.SnapshotChanged = false;
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>Deserialize a stored payload; null when empty or corrupt.</summary>
    public static ShopKeywordResearchDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ShopKeywordResearchDto>(json, JsonOptions);
            if (dto is not null && string.IsNullOrWhiteSpace(dto.Source))
            {
                dto.Source = ShopKeywordResearchSuggest.SourceSnapshot;
            }

            return dto;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Refresh <c>isSaved</c>, rematch opportunity ids/metrics by keyword, stamp analyzedAt / snapshotChanged.
    /// </summary>
    public static void Hydrate(
        ShopKeywordResearchDto dto,
        IReadOnlyDictionary<string, OpportunitySnapshot> byKeyword,
        IReadOnlySet<string> savedKeys,
        DateTime analyzedAt,
        DateTime? currentSyncedAt)
    {
        dto.AnalyzedAt = analyzedAt;
        dto.SnapshotChanged = !SameInstant(dto.LastSyncedAt, currentSyncedAt);
        HydrateList(dto.ListNow, byKeyword, savedKeys);
        HydrateList(dto.Upcoming, byKeyword, savedKeys);
        StampLaneRanks(dto);
    }

    /// <summary>
    /// 1-based rank within each lane after hydrate / keyword removals.
    /// Does not invent <c>score</c> for legacy payloads.
    /// </summary>
    public static void StampLaneRanks(ShopKeywordResearchDto dto)
    {
        StampListRanks(dto.ListNow);
        StampListRanks(dto.Upcoming);
    }

    private static void StampListRanks(List<ShopKeywordResearchItemDto> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Rank = i + 1;
        }
    }

    private static void HydrateList(
        List<ShopKeywordResearchItemDto> items,
        IReadOnlyDictionary<string, OpportunitySnapshot> byKeyword,
        IReadOnlySet<string> savedKeys)
    {
        foreach (var item in items)
        {
            item.Rationale = StripDuplicateVolumeMetric(item.Rationale);
            var key = Normalize(item.Keyword);
            item.IsSaved = key.Length > 0 && savedKeys.Contains(key);
            if (key.Length == 0 || !byKeyword.TryGetValue(key, out var snap))
            {
                continue;
            }

            item.Id = snap.Id;
            item.Keyword = snap.LeadName;
            item.SearchVolume = snap.SearchVolume;
            item.OnlineProducts = snap.OnlineProducts;
            item.OnlineProductsRaw = snap.OnlineProductsRaw;
            item.SearchVolumeRingRatio = snap.SearchVolumeRingRatio;
            item.LeadTagTexts = snap.LeadTagTexts.ToList();
            item.PicUrl = snap.PicUrl;
            item.PicUrls = snap.PicUrls.ToList();
        }
    }

    /// <summary>
    /// Drop <c>listNow</c> / <c>upcoming</c> items whose normalized keyword is in
    /// <paramref name="keys"/>. Shop-level copy (analysis, weather, categories) stays.
    /// </summary>
    public static int RemoveKeywords(ShopKeywordResearchDto dto, IReadOnlySet<string> keys)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (keys is null || keys.Count == 0)
        {
            return 0;
        }

        var removed = dto.ListNow.RemoveAll(Matches);
        removed += dto.Upcoming.RemoveAll(Matches);
        if (removed > 0)
        {
            StampLaneRanks(dto);
        }

        return removed;

        bool Matches(ShopKeywordResearchItemDto item) =>
            keys.Contains(Normalize(item.Keyword));
    }

    /// <summary>
    /// Rewrite a stored jsonb payload without the given keywords.
    /// Null when the payload is empty, corrupt, or already clean.
    /// </summary>
    public static string? TryRemoveKeywords(string? payloadJson, IReadOnlySet<string> keys)
    {
        var dto = Deserialize(payloadJson);
        if (dto is null || RemoveKeywords(dto, keys) == 0)
        {
            return null;
        }

        return Serialize(dto);
    }

    /// <summary>Trimmed lower-case keyword key.</summary>
    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Drop trailing/embedded <c>vol 12345</c> from rationale — volume already shows on the keyword row.
    /// </summary>
    public static string? StripDuplicateVolumeMetric(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            return null;
        }

        var raw = rationale.Trim();
        var hadTerminalPunct = raw.EndsWith('.') || raw.EndsWith('!') || raw.EndsWith('?');
        var text = DuplicateVolumeMetric.Replace(raw, string.Empty).Trim();
        text = text.TrimEnd(',', ';', ':', '—', '–', '-', ' ').Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (hadTerminalPunct && text[^1] is not '.' and not '!' and not '?')
        {
            text += ".";
        }

        return text;
    }

    /// <summary><c>listNow</c> — drawer label «List ngay».</summary>
    public const string LaneListNow = "listNow";

    /// <summary><c>upcoming</c> — drawer label «Đón đầu».</summary>
    public const string LaneUpcoming = "upcoming";

    /// <summary>Rationale / fit / research lane / AI must tokens keyed by normalized keyword.</summary>
    public sealed record KeywordNiche(
        string? Rationale,
        string? Fit,
        string? Lane,
        IReadOnlyList<string> MustTokens);

    /// <summary>
    /// Index «Shop này bán gì» by keyword. List-now wins when the same keyword is also upcoming.
    /// </summary>
    public static Dictionary<string, KeywordNiche> IndexKeywordNiche(ShopKeywordResearchDto? research)
    {
        var map = new Dictionary<string, KeywordNiche>(StringComparer.Ordinal);
        if (research is null)
        {
            return map;
        }

        Add(research.ListNow, LaneListNow);
        Add(research.Upcoming, LaneUpcoming);
        return map;

        void Add(IEnumerable<ShopKeywordResearchItemDto> rows, string lane)
        {
            foreach (var item in rows)
            {
                var key = Normalize(item.Keyword);
                if (key.Length == 0 || map.ContainsKey(key))
                {
                    continue;
                }

                map[key] = new KeywordNiche(
                    StripDuplicateVolumeMetric(item.Rationale),
                    item.Fit,
                    lane,
                    item.MustTokens ?? []);
            }
        }
    }

    private static bool SameInstant(DateTime? stored, DateTime? current)
    {
        if (stored is null && current is null)
        {
            return true;
        }

        if (stored is null || current is null)
        {
            return false;
        }

        var a = DateTime.SpecifyKind(stored.Value, DateTimeKind.Utc);
        var b = DateTime.SpecifyKind(current.Value, DateTimeKind.Utc);
        return Math.Abs((a - b).TotalSeconds) < 1;
    }
}
