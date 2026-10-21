using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.ProductOpportunity;
using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Scores every TikTok opportunity keyword for a shop, then asks OpenAI to write copy.
/// When the snapshot is empty or has no on-niche keywords, invents audience-fit search keywords instead.
/// </summary>
public class ShopKeywordResearchService : IShopKeywordResearchService
{
    private const int ListingSampleLimit = 20;
    private const int MaxModelAttempts = 2;
    private const int UpcomingKeywordCount = 5;
    private const int MinListNow = 5;
    private const int MaxListNow = 12;
    private const string BackfillFit = "audience";
    private const string SnapshotSource = ShopKeywordResearchSuggest.SourceSnapshot;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationDbContext _db;
    private readonly IOpenAiService _openAi;
    private readonly ILogger<ShopKeywordResearchService> _logger;

    /// <summary>Creates the shop keyword research service.</summary>
    public ShopKeywordResearchService(
        IApplicationDbContext db,
        IOpenAiService openAi,
        ILogger<ShopKeywordResearchService> logger)
    {
        _db = db;
        _openAi = openAi;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ShopKeywordResearchDto?> GetCachedAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default)
    {
        if (shopId == Guid.Empty)
        {
            throw new BusinessException("shopId không hợp lệ.");
        }

        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerId.HasValue)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerId.Value);
        }

        var shop = await shopQuery
            .Select(s => new { s.Id, s.OwnerId, s.BodySizeCode, s.ProductOpportunitiesSyncedAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var stored = await _db.ShopKeywordResearches.AsNoTracking()
            .Where(x => x.ShopId == shop.Id)
            .Select(x => new { x.PayloadJson, x.AnalyzedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var dto = ShopKeywordResearchPersistence.Deserialize(stored.PayloadJson);
        if (dto is null)
        {
            _logger.LogWarning("ShopKeywordResearch payload corrupt for shop {ShopId}", shop.Id);
            return null;
        }

        var keywords = dto.ListNow.Concat(dto.Upcoming).Select(i => i.Keyword).ToList();
        var byKeyword = await LoadOpportunitySnapshotsAsync(shop.Id, keywords, cancellationToken);
        var savedKeys = await LoadSavedKeysAsync(shop.Id, keywords, cancellationToken);
        ShopKeywordResearchPersistence.Hydrate(
            dto,
            byKeyword,
            savedKeys,
            stored.AnalyzedAt,
            shop.ProductOpportunitiesSyncedAt);
        AudienceSearchQuery.Apply(dto, shop.BodySizeCode);
        KeywordMatchPhrases.Apply(dto);
        return dto;
    }

    /// <inheritdoc />
    public async Task<ShopKeywordResearchDto> ResearchAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default)
    {
        if (shopId == Guid.Empty)
        {
            throw new BusinessException("shopId không hợp lệ.");
        }

        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerId.HasValue)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerId.Value);
        }

        var shop = await shopQuery
            .Select(s => new
            {
                s.Id,
                s.OwnerId,
                s.Name,
                s.ProfileName,
                s.Region,
                s.CatalogCategories,
                s.CategoryId,
                s.CategoryName,
                s.AgeMin,
                s.AgeMax,
                s.PriceMinUsd,
                s.PriceMaxUsd,
                s.BodySizeCode,
                s.StyleCodes,
                s.PublishLimitPublished,
                s.PublishLimitTotal,
                s.ProductOpportunitiesSyncedAt
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var categories = ReadCatalogCategories(shop.CatalogCategories, shop.CategoryId, shop.CategoryName);
        if (categories.Count == 0)
        {
            throw new BusinessException(
                "Shop chưa gán danh mục vận hành — mở modal Danh mục rồi mới phân tích keyword.");
        }

        var categoryNames = categories.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var styleCodes = DeserializeStringList(shop.StyleCodes);
        var warnings = new List<string>();
        if (shop.AgeMin is null || shop.AgeMax is null)
        {
            warnings.Add("Chưa chọn khoảng tuổi — khớp ngách sẽ yếu hơn.");
        }

        if (string.IsNullOrWhiteSpace(shop.BodySizeCode))
        {
            warnings.Add("Chưa chọn Body/size.");
        }

        if (styleCodes.Count == 0)
        {
            warnings.Add("Chưa chọn Style.");
        }

        var rows = await _db.ProductOpportunities.AsNoTracking()
            .Where(x => x.ShopLinks.Any(l => l.ShopId == shop.Id))
            .Select(x => new OpportunityRow(
                x.Id,
                x.LeadName,
                x.SearchVolume,
                x.OnlineProducts,
                x.OnlineProductsRaw,
                x.SearchVolumeRingRatio,
                x.OnlineProductsRingRatio,
                x.LeadTagTexts,
                x.PicUrl,
                x.PicUrls,
                x.Categories,
                x.Level1CateName,
                x.Level2CateName,
                x.Level3CateName))
            .ToListAsync(cancellationToken);

        var ageRange = await LoadAgeRangeAsync(shop.AgeMin, shop.AgeMax, cancellationToken);
        var listingTitles = await LoadListingSampleAsync(shop.Id, cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var season = UsRetailCalendar.GetSeason(nowUtc);
        var events = UsRetailCalendar.GetUpcomingEvents(nowUtc);
        var weather = UsSeasonWeather.Get(nowUtc);
        var published = shop.PublishLimitPublished ?? 0;
        var totalLimit = shop.PublishLimitTotal ?? 0;
        var remainingCap = rows.Count == 0 ? MinListNow : MaxListNow;
        var remaining = totalLimit > 0 ? Math.Max(0, totalLimit - published) : remainingCap;
        if (totalLimit > 0 && remaining == 0)
        {
            warnings.Add("Shop đã hết slot listing — đề xuất để thay thế / kỳ tới.");
        }

        var audienceRun = new AudienceSuggestRun(
            shop.Id,
            shop.OwnerId,
            shop.Name,
            shop.ProfileName,
            shop.Region,
            categoryNames,
            shop.AgeMin,
            shop.AgeMax,
            shop.BodySizeCode,
            styleCodes,
            shop.PriceMinUsd,
            shop.PriceMaxUsd,
            ageRange,
            published,
            totalLimit,
            remaining,
            listingTitles,
            season,
            weather,
            events,
            nowUtc,
            shop.ProductOpportunitiesSyncedAt,
            warnings);

        if (rows.Count == 0)
        {
            warnings.Insert(0, ShopKeywordResearchSuggest.EmptySnapshotWarning);
            return await ResearchFromAudienceAsync(audienceRun, cancellationToken);
        }

        var audience = new ShopKeywordAudience(
            categoryNames,
            shop.AgeMin,
            shop.AgeMax,
            shop.BodySizeCode,
            styleCodes,
            ageRange?.PainPoint,
            ageRange?.Desire,
            ageRange?.ProductGroups,
            listingTitles);

        var droppedGeneric = 0;
        var candidates = new List<ShopKeywordCandidate>(rows.Count);
        foreach (var row in rows)
        {
            if (ShopKeywordResearchScoring.IsGeneric(row.LeadName, categoryNames))
            {
                droppedGeneric++;
                continue;
            }

            candidates.Add(new ShopKeywordCandidate(
                row.Id,
                row.LeadName,
                row.SearchVolume,
                row.OnlineProducts,
                row.OnlineProductsRaw,
                row.SearchVolumeRingRatio,
                row.OnlineProductsRingRatio,
                DeserializeStringList(row.LeadTagTexts),
                row.PicUrl,
                DeserializeStringList(row.PicUrls),
                CollectKeywordCategoryNames(
                    row.Categories,
                    row.Level1CateName,
                    row.Level2CateName,
                    row.Level3CateName)));
        }

        var scored = ShopKeywordResearchScoring.ScoreAll(candidates, audience, weather);
        var shortlist = ShopKeywordResearchScoring.BuildShortlist(scored);
        if (scored.Count == 0 || shortlist.Count == 0)
        {
            warnings.Insert(
                0,
                scored.Count == 0
                    ? ShopKeywordResearchSuggest.GenericOnlySnapshotWarning
                    : ShopKeywordResearchSuggest.OffNicheSnapshotWarning);
            _logger.LogInformation(
                "ShopKeywordResearch shop {ShopId} falling back to audience suggest: snapshot={Snapshot} generic={Generic} scored={Scored} shortlist={Shortlist}",
                shop.Id,
                rows.Count,
                droppedGeneric,
                scored.Count,
                shortlist.Count);
            return await ResearchFromAudienceAsync(
                audienceRun with
                {
                    SnapshotCount = rows.Count,
                    DroppedGeneric = droppedGeneric,
                    CandidatesScored = scored.Count
                },
                cancellationToken);
        }

        var mismatches = ShopKeywordResearchScoring.BuildMismatchExamples(
            scored,
            shortlist.Select(s => s.Id).ToHashSet());

        var listNowCount = Math.Min(
            MaxListNow,
            Math.Max(MinListNow, remaining == 0 ? MinListNow : remaining));
        listNowCount = Math.Min(listNowCount, shortlist.Count);

        var upcomingCount = Math.Min(
            UpcomingKeywordCount,
            Math.Max(0, shortlist.Count - listNowCount));

        var (lockedNow, lockedUpcoming) = ShopKeywordResearchScoring.PickLockedLists(
            shortlist,
            listNowCount,
            upcomingCount);
        listNowCount = lockedNow.Count;
        upcomingCount = lockedUpcoming.Count;
        var lockedPool = lockedNow.Concat(lockedUpcoming).ToList();
        var byLockedId = lockedPool.ToDictionary(s => s.Id);
        var outranked = ShopKeywordResearchScoring.BuildOutrankedExamples(
            shortlist,
            byLockedId.Keys.ToHashSet());

        var context = BuildShopContext(
            shop.Name,
            shop.ProfileName,
            shop.Region,
            categoryNames,
            shop.AgeMin,
            shop.AgeMax,
            shop.BodySizeCode,
            styleCodes,
            shop.PriceMinUsd,
            shop.PriceMaxUsd,
            ageRange,
            published,
            totalLimit,
            remaining,
            listNowCount,
            upcomingCount,
            rows.Count,
            droppedGeneric,
            scored.Count,
            listingTitles,
            season,
            weather,
            events,
            nowUtc,
            lockedNow,
            lockedUpcoming,
            mismatches,
            outranked);

        var picked = await RunModelAsync(
            context,
            lockedNow,
            lockedUpcoming,
            cancellationToken);

        var savedKeys = await LoadSavedKeysAsync(
            shop.Id,
            picked.Now.Concat(picked.Upcoming).Select(id => byLockedId[id].LeadName).ToList(),
            cancellationToken);

        var analyzedAt = DateTime.UtcNow;
        var dto = new ShopKeywordResearchDto
        {
            Source = SnapshotSource,
            AnalysisPoints = picked.Points,
            Warnings = warnings,
            Categories = categoryNames,
            AgeMin = shop.AgeMin,
            AgeMax = shop.AgeMax,
            BodySizeCode = shop.BodySizeCode,
            StyleCodes = styleCodes,
            PublishLimitPublished = shop.PublishLimitPublished,
            PublishLimitTotal = shop.PublishLimitTotal,
            RemainingSlots = remaining,
            SnapshotCount = rows.Count,
            DroppedGeneric = droppedGeneric,
            CandidatesScored = scored.Count,
            Season = season,
            WeatherNote = weather.Note,
            UpcomingEvents = events.Select(e => e.ToContextLine()).ToList(),
            AsOfDate = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LastSyncedAt = shop.ProductOpportunitiesSyncedAt,
            AnalyzedAt = analyzedAt,
            SnapshotChanged = false,
            ListNow = MapItems(picked.Now, byLockedId, picked.NowMeta, savedKeys),
            Upcoming = MapItems(picked.Upcoming, byLockedId, picked.UpcomingMeta, savedKeys)
        };

        AudienceSearchQuery.Apply(dto, shop.BodySizeCode);
        KeywordMatchPhrases.Apply(dto);
        await UpsertCachedAsync(shop.Id, shop.ProductOpportunitiesSyncedAt, analyzedAt, dto, cancellationToken);
        return dto;
    }

    private async Task<ShopKeywordResearchDto> ResearchFromAudienceAsync(
        AudienceSuggestRun run,
        CancellationToken cancellationToken)
    {
        var listNowCount = Math.Min(
            ShopKeywordResearchSuggest.MaxListNow,
            Math.Max(MinListNow, run.Remaining == 0 ? MinListNow : run.Remaining));
        var upcomingCount = ShopKeywordResearchSuggest.UpcomingCount;

        var context = BuildAudienceSuggestContext(run, listNowCount, upcomingCount);
            var picked = await RunSuggestModelAsync(
            context,
            listNowCount,
            upcomingCount,
            run.CategoryNames,
            run.ListingTitles,
            run.BodySizeCode,
            run.Warnings,
            cancellationToken);

        var savedKeys = await LoadSavedKeysAsync(
            run.ShopId,
            picked.Now.Concat(picked.Upcoming).Select(i => i.Keyword).ToList(),
            cancellationToken);

        var analyzedAt = DateTime.UtcNow;
        var dto = new ShopKeywordResearchDto
        {
            Source = ShopKeywordResearchSuggest.SourceAudienceSuggest,
            AnalysisPoints = picked.Points,
            Warnings = run.Warnings,
            Categories = run.CategoryNames.ToList(),
            AgeMin = run.AgeMin,
            AgeMax = run.AgeMax,
            BodySizeCode = run.BodySizeCode,
            StyleCodes = run.StyleCodes.ToList(),
            PublishLimitPublished = run.Published,
            PublishLimitTotal = run.TotalLimit,
            RemainingSlots = run.Remaining,
            SnapshotCount = run.SnapshotCount,
            DroppedGeneric = run.DroppedGeneric,
            CandidatesScored = run.CandidatesScored,
            Season = run.Season,
            WeatherNote = run.Weather.Note,
            UpcomingEvents = run.Events.Select(e => e.ToContextLine()).ToList(),
            AsOfDate = run.NowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LastSyncedAt = run.LastSyncedAt,
            AnalyzedAt = analyzedAt,
            SnapshotChanged = false,
            ListNow = MapSuggestedItems(picked.Now, savedKeys),
            Upcoming = MapSuggestedItems(picked.Upcoming, savedKeys)
        };

        AudienceSearchQuery.Apply(dto, run.BodySizeCode);
        KeywordMatchPhrases.Apply(dto);
        await UpsertCachedAsync(run.ShopId, run.LastSyncedAt, analyzedAt, dto, cancellationToken);
        return dto;
    }

    private async Task<SuggestPicks> RunSuggestModelAsync(
        string context,
        int listNowCount,
        int upcomingCount,
        IReadOnlyList<string> categoryNames,
        IReadOnlyList<string> listingTitles,
        string? bodySizeCode,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        SuggestPicks? best = null;

        for (var attempt = 1; attempt <= MaxModelAttempts; attempt++)
        {
            var raw = await _openAi.CompleteShopKeywordResearchSuggestAsync(context, cancellationToken);
            var parsed = ParseSuggestModelJson(raw);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = ShopKeywordResearchSuggest.Take(
                EnumerateSuggestItems(parsed.ListNow),
                listNowCount,
                categoryNames,
                listingTitles,
                seen,
                bodySizeCode);
            var upcoming = ShopKeywordResearchSuggest.Take(
                EnumerateSuggestItems(parsed.Upcoming),
                upcomingCount,
                categoryNames,
                listingTitles,
                seen,
                bodySizeCode);
            ShopKeywordResearchSuggest.BackfillNowFromUpcoming(now, upcoming, MinListNow);
            var points = NormalizeAnalysisPoints(new ModelResponse
            {
                AnalysisPoints = parsed.AnalysisPoints
            });

            var rawNow = parsed.ListNow?.Count ?? 0;
            var rawUpcoming = parsed.Upcoming?.Count ?? 0;
            best = new SuggestPicks(points, now, upcoming);
            if (points.Count > 0 && now.Count >= MinListNow)
            {
                if (now.Count < listNowCount || upcoming.Count < upcomingCount)
                {
                    warnings.Add(
                        $"Chỉ giữ được {now.Count}/{listNowCount} keyword list ngay và {upcoming.Count}/{upcomingCount} đón đầu sau khi lọc generic / trùng listing.");
                }

                return best;
            }

            _logger.LogWarning(
                "ShopKeywordResearch suggest attempt {Attempt}: raw {RawNow}/{RawUpcoming}, kept {Now}/{Upcoming} (need listNow>={MinNow})",
                attempt,
                rawNow,
                rawUpcoming,
                now.Count,
                upcoming.Count,
                MinListNow);
        }

        if (best is null || best.Points.Count == 0)
        {
            throw new BusinessException("OpenAI không trả phân tích ngách. Thử lại.");
        }

        if (best.Now.Count > 0)
        {
            warnings.Add(
                $"Chỉ giữ được {best.Now.Count} keyword hợp lệ (mục tiêu {listNowCount} list ngay + {upcomingCount} đón đầu).");
            return best;
        }

        throw new BusinessException(
            "OpenAI không trả keyword cụ thể khớp ngách (bị lọc generic). Thử Phân tích lại.");
    }

    private SuggestModelResponse ParseSuggestModelJson(string raw)
    {
        var json = ExtractJsonObject(raw);
        try
        {
            return JsonSerializer.Deserialize<SuggestModelResponse>(json, JsonOptions)
                ?? throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ShopKeywordResearch suggest JSON parse failed: {Snippet}", Truncate(raw, 400));
            throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
    }

    private static IEnumerable<(string? Keyword, string? Rationale, string? Fit, IReadOnlyList<string>? MustTokens)> EnumerateSuggestItems(
        List<SuggestModelItem>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            yield return (item.Keyword, item.Rationale, item.Fit, item.MustTokens);
        }
    }

    private static List<ShopKeywordResearchItemDto> MapSuggestedItems(
        IReadOnlyList<ShopKeywordResearchSuggest.AcceptedKeyword> items,
        IReadOnlySet<string> savedKeys)
    {
        var result = new List<ShopKeywordResearchItemDto>(items.Count);
        var rank = 1;
        foreach (var item in items)
        {
            result.Add(new ShopKeywordResearchItemDto
            {
                Id = Guid.NewGuid(),
                Keyword = item.Keyword,
                SearchVolume = 0,
                Rationale = string.IsNullOrWhiteSpace(item.Rationale)
                    ? ShopKeywordResearchSuggest.DefaultRationale
                    : item.Rationale,
                Fit = string.IsNullOrWhiteSpace(item.Fit) ? BackfillFit : item.Fit,
                MustTokens = item.MustTokens?.ToList() ?? [],
                IsSaved = savedKeys.Contains(item.Keyword),
                Rank = rank++,
                Score = null,
                ScoreBreakdown = null
            });
        }

        return result;
    }

    private static string BuildAudienceSuggestContext(
        AudienceSuggestRun run,
        int listNowCount,
        int upcomingCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"shop: {run.ShopName}");
        if (!string.IsNullOrWhiteSpace(run.ProfileName))
        {
            sb.AppendLine($"profile: {run.ProfileName}");
        }

        sb.AppendLine($"region: {(string.IsNullOrWhiteSpace(run.Region) ? "US" : run.Region)}");
        sb.AppendLine($"todayUtc: {run.NowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"season: {run.Season}");
        sb.AppendLine($"weather: {run.Weather.Note}");
        sb.AppendLine($"categories: {string.Join(" · ", run.CategoryNames)}");
        sb.AppendLine($"age: {(run.AgeMin is null || run.AgeMax is null ? "(missing)" : $"{run.AgeMin}-{run.AgeMax}")}");
        sb.AppendLine($"bodySize: {run.BodySizeCode ?? "(missing)"}");
        sb.AppendLine($"styles: {(run.StyleCodes.Count == 0 ? "(missing)" : string.Join(", ", run.StyleCodes))}");
        if (run.AgeRange is not null)
        {
            sb.AppendLine($"painPoint: {run.AgeRange.PainPoint}");
            sb.AppendLine($"desire: {run.AgeRange.Desire}");
            sb.AppendLine($"productGroups: {run.AgeRange.ProductGroups}");
        }

        if (run.PriceMinUsd is not null || run.PriceMaxUsd is not null)
        {
            sb.AppendLine(
                $"priceBandUsd: {run.PriceMinUsd?.ToString(CultureInfo.InvariantCulture) ?? "-"}-{run.PriceMaxUsd?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        }

        sb.AppendLine($"listings: {run.Published}/{run.TotalLimit} (remainingSlots={run.Remaining})");
        sb.AppendLine("snapshotCount=0 mode=audienceSuggest — invent TikTok Shop search keywords; never invent volume");
        sb.AppendLine(
            $"listNowTarget={listNowCount} upcomingTarget={upcomingCount} — return AT LEAST {listNowCount + ShopKeywordResearchSuggest.ExtraBuffer} listNow and {upcomingCount + ShopKeywordResearchSuggest.ExtraBuffer} upcoming; backend drops generic");
        sb.AppendLine(
            "mustTokens: copy up to 2 demand tok words + garment for JSON only. Backend stamps coverage (garment + ceil(N/2) of those). Do not AND-all mustTokens on the title.");
        sb.AppendLine(
            "priority: each keyword must quote a real audience match (plus/vintage/floral/A-line/midi). When bodySize is plus, every keyword MUST contain plus size; petite → petite; tall → tall. Never suggest sheer/mesh/bodycon/boho when styles are vintage/casual.");

        sb.AppendLine();
        sb.AppendLine(
            $"UPCOMING_EVENTS (window {UsRetailCalendar.MinDaysAhead}-{UsRetailCalendar.MaxDaysAhead} days)");
        if (run.Events.Count == 0)
        {
            sb.AppendLine("(none — lean on season + weather)");
        }
        else
        {
            foreach (var item in run.Events)
            {
                sb.AppendLine($"- {item.ToContextLine()}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("LISTING_SAMPLE (already live titles — do not repeat these directions)");
        if (run.ListingTitles.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var title in run.ListingTitles)
            {
                sb.AppendLine($"- {title}");
            }
        }

        return sb.ToString();
    }

    private async Task<ModelPicks> RunModelAsync(
        string context,
        IReadOnlyList<ShopKeywordScored> lockedNow,
        IReadOnlyList<ShopKeywordScored> lockedUpcoming,
        CancellationToken cancellationToken)
    {
        var nowIds = lockedNow.Select(s => s.Id).ToList();
        var upcomingIds = lockedUpcoming.Select(s => s.Id).ToList();
        ModelPicks? best = null;

        for (var attempt = 1; attempt <= MaxModelAttempts; attempt++)
        {
            var raw = await _openAi.CompleteShopKeywordResearchAsync(context, cancellationToken);
            var parsed = ParseModelJson(raw);
            var points = NormalizeAnalysisPoints(parsed);
            best = new ModelPicks(
                points,
                nowIds,
                upcomingIds,
                IndexMeta(parsed.ListNow),
                IndexMeta(parsed.Upcoming));

            if (points.Count > 0)
            {
                return best;
            }

            _logger.LogWarning(
                "ShopKeywordResearch attempt {Attempt} returned no analysisPoints",
                attempt);
        }

        if (best is null || best.Points.Count == 0)
        {
            throw new BusinessException("OpenAI không trả phân tích ngách. Thử lại.");
        }

        return best;
    }

    private ModelResponse ParseModelJson(string raw)
    {
        var json = ExtractJsonObject(raw);
        try
        {
            return JsonSerializer.Deserialize<ModelResponse>(json, JsonOptions)
                ?? throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ShopKeywordResearch JSON parse failed: {Snippet}", Truncate(raw, 400));
            throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
    }

    private async Task<AgeRangeDto?> LoadAgeRangeAsync(
        int? ageMin,
        int? ageMax,
        CancellationToken cancellationToken)
    {
        if (ageMin is null || ageMax is null)
        {
            return null;
        }

        return await _db.AgeRanges.AsNoTracking()
            .Where(x => x.IsActive && x.AgeMin == ageMin && x.AgeMax == ageMax)
            .OrderBy(x => x.SortOrder)
            .Select(x => new AgeRangeDto
            {
                Id = x.Id,
                Code = x.Code,
                Label = x.Label,
                AgeMin = x.AgeMin,
                AgeMax = x.AgeMax,
                PainPoint = x.PainPoint,
                Desire = x.Desire,
                ProductGroups = x.ProductGroups,
                SortOrder = x.SortOrder
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<string>> LoadListingSampleAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        return await _db.Listings.AsNoTracking()
            .Where(l =>
                l.ShopId == shopId &&
                l.ProductName != null &&
                l.ProductName != string.Empty)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(ListingSampleLimit)
            .Select(l => l.ProductName!)
            .ToListAsync(cancellationToken);
    }

    private async Task UpsertCachedAsync(
        Guid shopId,
        DateTime? snapshotSyncedAt,
        DateTime analyzedAt,
        ShopKeywordResearchDto dto,
        CancellationToken cancellationToken)
    {
        var payload = ShopKeywordResearchPersistence.Serialize(dto);
        var existing = await _db.ShopKeywordResearches
            .FirstOrDefaultAsync(x => x.ShopId == shopId, cancellationToken);
        if (existing is null)
        {
            await _db.ShopKeywordResearches.AddAsync(
                new ShopKeywordResearch
                {
                    ShopId = shopId,
                    PayloadJson = payload,
                    SnapshotSyncedAt = snapshotSyncedAt,
                    AnalyzedAt = analyzedAt,
                    CreatedAt = analyzedAt
                },
                cancellationToken);
        }
        else
        {
            existing.PayloadJson = payload;
            existing.SnapshotSyncedAt = snapshotSyncedAt;
            existing.AnalyzedAt = analyzedAt;
            existing.UpdatedAt = analyzedAt;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>> LoadOpportunitySnapshotsAsync(
        Guid shopId,
        IReadOnlyList<string> leadNames,
        CancellationToken cancellationToken)
    {
        var names = leadNames
            .Select(ShopKeywordResearchPersistence.Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(
                StringComparer.OrdinalIgnoreCase);
        }

        var rows = await _db.ProductOpportunities.AsNoTracking()
            .Where(x => x.ShopLinks.Any(l => l.ShopId == shopId) && names.Contains(x.LeadName.ToLower()))
            .Select(x => new
            {
                x.Id,
                x.LeadName,
                x.SearchVolume,
                x.OnlineProducts,
                x.OnlineProductsRaw,
                x.SearchVolumeRingRatio,
                x.LeadTagTexts,
                x.PicUrl,
                x.PicUrls
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, ShopKeywordResearchPersistence.OpportunitySnapshot>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = ShopKeywordResearchPersistence.Normalize(row.LeadName);
            if (key.Length == 0 || map.ContainsKey(key))
            {
                continue;
            }

            map[key] = new ShopKeywordResearchPersistence.OpportunitySnapshot(
                row.Id,
                row.LeadName,
                row.SearchVolume,
                row.OnlineProducts,
                row.OnlineProductsRaw,
                row.SearchVolumeRingRatio,
                DeserializeStringList(row.LeadTagTexts),
                row.PicUrl,
                DeserializeStringList(row.PicUrls));
        }

        return map;
    }

    private async Task<HashSet<string>> LoadSavedKeysAsync(
        Guid shopId,
        IReadOnlyList<string> leadNames,
        CancellationToken cancellationToken)
    {
        var names = leadNames
            .Select(n => n.Trim().ToLowerInvariant())
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var saved = await _db.ProductOpportunitySavedKeywords.AsNoTracking()
            .Where(s => s.ShopId == shopId && names.Contains(s.KeywordNormalized))
            .Select(s => s.KeywordNormalized)
            .ToListAsync(cancellationToken);
        return saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildShopContext(
        string shopName,
        string? profileName,
        string? region,
        IReadOnlyList<string> categoryNames,
        int? ageMin,
        int? ageMax,
        string? bodySizeCode,
        IReadOnlyList<string> styleCodes,
        decimal? priceMin,
        decimal? priceMax,
        AgeRangeDto? ageRange,
        int published,
        int totalLimit,
        int remaining,
        int listNowCount,
        int upcomingCount,
        int snapshotCount,
        int droppedGeneric,
        int scoredCount,
        IReadOnlyList<string> listingTitles,
        string season,
        UsSeasonWeatherInfo weather,
        IReadOnlyList<UsRetailEvent> events,
        DateTime nowUtc,
        IReadOnlyList<ShopKeywordScored> lockedNow,
        IReadOnlyList<ShopKeywordScored> lockedUpcoming,
        IReadOnlyList<ShopKeywordScored> mismatches,
        IReadOnlyList<ShopKeywordScored> outranked)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"shop: {shopName}");
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            sb.AppendLine($"profile: {profileName}");
        }

        sb.AppendLine($"region: {(string.IsNullOrWhiteSpace(region) ? "US" : region)}");
        sb.AppendLine($"todayUtc: {nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"season: {season}");
        sb.AppendLine($"weather: {weather.Note}");
        sb.AppendLine($"categories: {string.Join(" · ", categoryNames)}");
        sb.AppendLine($"age: {(ageMin is null || ageMax is null ? "(missing)" : $"{ageMin}-{ageMax}")}");
        sb.AppendLine($"bodySize: {bodySizeCode ?? "(missing)"}");
        sb.AppendLine($"styles: {(styleCodes.Count == 0 ? "(missing)" : string.Join(", ", styleCodes))}");
        if (ageRange is not null)
        {
            sb.AppendLine($"painPoint: {ageRange.PainPoint}");
            sb.AppendLine($"desire: {ageRange.Desire}");
            sb.AppendLine($"productGroups: {ageRange.ProductGroups}");
        }

        if (priceMin is not null || priceMax is not null)
        {
            sb.AppendLine($"priceBandUsd: {priceMin?.ToString(CultureInfo.InvariantCulture) ?? "-"}-{priceMax?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        }

        sb.AppendLine($"listings: {published}/{totalLimit} (remainingSlots={remaining})");
        sb.AppendLine($"snapshotCount={snapshotCount} droppedGeneric={droppedGeneric} scored={scoredCount}");
        sb.AppendLine($"listNowCount={listNowCount} upcomingCount={upcomingCount}");
        sb.AppendLine(
            "strategy: ids are LOCKED by backend rank (audience gate, then score = niche 45 + demand 30 + growth 15 + season 5 + competition 5) — write Vietnamese copy only, never swap a keyword");
        sb.AppendLine("mustTokens: copy up to 2 demand tok + garment for JSON only. Backend stamps coverage (garment + ceil(N/2) of those). Do not AND-all. Never hardware/style-era in the gate.");
        sb.AppendLine("priority: rationale must name the REJECT-OTHERS slot in the keyword (denim/off-shoulder/floral/midi). Never call sheer/mesh/see-through 'kín đáo'. Never mention vol/volume — UI already shows metrics.");

        sb.AppendLine();
        sb.AppendLine(
            $"UPCOMING_EVENTS (window {UsRetailCalendar.MinDaysAhead}-{UsRetailCalendar.MaxDaysAhead} days)");
        if (events.Count == 0)
        {
            sb.AppendLine("(none — lean on season + weather)");
        }
        else
        {
            foreach (var item in events)
            {
                sb.AppendLine($"- {item.ToContextLine()}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("LISTING_SAMPLE (already live titles — avoid repeating these directions)");
        if (listingTitles.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var title in listingTitles)
            {
                sb.AppendLine($"- {title}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("SKIP_THESE (not listed — explain in analysisPoints; no ids, cannot be picked)");
        if (mismatches.Count == 0 && outranked.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var row in mismatches)
            {
                sb.AppendLine($"- {SkipLine(row)} — off-audience");
            }

            foreach (var row in outranked)
            {
                sb.AppendLine($"- {SkipLine(row)} — on-niche but scored lower");
            }
        }

        sb.AppendLine();
        sb.AppendLine("LOCKED_LIST_NOW (exact order, exact ids — write rationale, fit, mustTokens from tok)");
        foreach (var row in lockedNow)
        {
            sb.AppendLine(ShopKeywordResearchScoring.ToCandidateLine(row));
        }

        sb.AppendLine();
        sb.AppendLine("LOCKED_UPCOMING (exact order, exact ids — write rationale, fit, mustTokens from tok)");
        if (lockedUpcoming.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var row in lockedUpcoming)
            {
                sb.AppendLine(ShopKeywordResearchScoring.ToCandidateLine(row));
            }
        }

        return sb.ToString();
    }

    private static string SkipLine(ShopKeywordScored row)
    {
        var name = row.LeadName.Replace('|', '/').Replace('\n', ' ').Trim();
        return $"{name} (vol {row.SearchVolume}, score {row.Score})";
    }

    private static List<ShopKeywordResearchItemDto> MapItems(
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, ShopKeywordScored> byId,
        IReadOnlyDictionary<Guid, ModelItemMeta> meta,
        IReadOnlySet<string> savedKeys)
    {
        var items = new List<ShopKeywordResearchItemDto>(ids.Count);
        var rank = 1;
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var row))
            {
                continue;
            }

            meta.TryGetValue(id, out var itemMeta);
            items.Add(new ShopKeywordResearchItemDto
            {
                Id = row.Id,
                Keyword = row.LeadName,
                SearchVolume = row.SearchVolume,
                OnlineProducts = row.OnlineProducts,
                OnlineProductsRaw = row.OnlineProductsRaw,
                SearchVolumeRingRatio = row.SearchVolumeRingRatio,
                LeadTagTexts = row.LeadTagTexts.ToList(),
                PicUrl = row.PicUrl,
                PicUrls = row.PicUrls.ToList(),
                Rationale = string.IsNullOrWhiteSpace(itemMeta.Rationale)
                    ? ShopKeywordResearchScoring.DefaultBackfillRationale
                    : itemMeta.Rationale,
                Fit = string.IsNullOrWhiteSpace(itemMeta.Fit) ? BackfillFit : itemMeta.Fit,
                MustTokens = itemMeta.MustTokens?.ToList() ?? [],
                IsSaved = savedKeys.Contains(row.LeadName.Trim().ToLowerInvariant()),
                Rank = rank++,
                Score = row.Score,
                ScoreBreakdown = new ShopKeywordScoreBreakdownDto
                {
                    Audience = row.Parts.Audience,
                    Season = row.Parts.Season,
                    Demand = row.Parts.Demand,
                    Growth = row.Parts.Growth,
                    Competition = row.Parts.Competition
                }
            });
        }

        return items;
    }

    private static Dictionary<Guid, ModelItemMeta> IndexMeta(List<ModelItem>? items)
    {
        var map = new Dictionary<Guid, ModelItemMeta>();
        if (items is null)
        {
            return map;
        }

        foreach (var item in items)
        {
            if (!ShopKeywordResearchScoring.TryParseId(item.Id, out var id) || map.ContainsKey(id))
            {
                continue;
            }

            map[id] = new ModelItemMeta(
                ShopKeywordResearchPersistence.StripDuplicateVolumeMetric(
                    string.IsNullOrWhiteSpace(item.Rationale) ? null : item.Rationale.Trim()),
                string.IsNullOrWhiteSpace(item.Fit) ? null : item.Fit.Trim(),
                item.MustTokens ?? []);
        }

        return map;
    }

    private static List<string> NormalizeAnalysisPoints(ModelResponse parsed)
    {
        var points = new List<string>();
        if (parsed.AnalysisPoints is { Count: > 0 })
        {
            foreach (var p in parsed.AnalysisPoints)
            {
                var t = CleanBullet(p);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    points.Add(t);
                }
            }
        }

        return points.Take(6).ToList();
    }

    private static string CleanBullet(string? value)
        => (value ?? string.Empty).Trim().TrimStart('-', '•', '*', ' ').Trim();

    private static List<ShopCatalogCategoryDto> ReadCatalogCategories(
        string? json,
        string? categoryId,
        string? categoryName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<List<ShopCatalogCategoryDto>>(json, JsonOptions) ?? [];
                var rows = parsed
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .GroupBy(c => c.Id.Trim(), StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var first = g.First();
                        first.Id = first.Id.Trim();
                        first.Name = string.IsNullOrWhiteSpace(first.Name) ? first.Id : first.Name.Trim();
                        return first;
                    })
                    .ToList();
                if (rows.Count > 0)
                {
                    return rows;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to legacy fields
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var id = categoryId.Trim();
            return
            [
                new ShopCatalogCategoryDto
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(categoryName) ? id : categoryName.Trim()
                }
            ];
        }

        return [];
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> CollectKeywordCategoryNames(
        string? categoriesJson,
        string? level1,
        string? level2,
        string? level3)
    {
        var names = new List<string>();
        AddCategoryName(names, level1);
        AddCategoryName(names, level2);
        AddCategoryName(names, level3);
        if (string.IsNullOrWhiteSpace(categoriesJson))
        {
            return names;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ProductOpportunityCategoryDto>>(categoriesJson, JsonOptions)
                ?? [];
            foreach (var item in parsed)
            {
                AddCategoryName(names, item.CateName);
            }
        }
        catch (JsonException)
        {
            // keep level names only
        }

        return names;
    }

    private static void AddCategoryName(List<string> names, string? value)
    {
        var t = value?.Trim();
        if (!string.IsNullOrWhiteSpace(t)
            && !names.Contains(t, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(t);
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl > 0)
            {
                text = text[(firstNl + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new BusinessException("OpenAI không trả về JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed record AudienceSuggestRun(
        Guid ShopId,
        Guid OwnerId,
        string ShopName,
        string? ProfileName,
        string? Region,
        IReadOnlyList<string> CategoryNames,
        int? AgeMin,
        int? AgeMax,
        string? BodySizeCode,
        IReadOnlyList<string> StyleCodes,
        decimal? PriceMinUsd,
        decimal? PriceMaxUsd,
        AgeRangeDto? AgeRange,
        int Published,
        int TotalLimit,
        int Remaining,
        IReadOnlyList<string> ListingTitles,
        string Season,
        UsSeasonWeatherInfo Weather,
        IReadOnlyList<UsRetailEvent> Events,
        DateTime NowUtc,
        DateTime? LastSyncedAt,
        List<string> Warnings,
        int SnapshotCount = 0,
        int DroppedGeneric = 0,
        int CandidatesScored = 0);

    private sealed record OpportunityRow(
        Guid Id,
        string LeadName,
        int SearchVolume,
        int? OnlineProducts,
        string? OnlineProductsRaw,
        string? SearchVolumeRingRatio,
        string? OnlineProductsRingRatio,
        string? LeadTagTexts,
        string? PicUrl,
        string? PicUrls,
        string? Categories,
        string? Level1CateName,
        string? Level2CateName,
        string? Level3CateName);

    private sealed record ModelPicks(
        List<string> Points,
        List<Guid> Now,
        List<Guid> Upcoming,
        Dictionary<Guid, ModelItemMeta> NowMeta,
        Dictionary<Guid, ModelItemMeta> UpcomingMeta);

    private readonly record struct ModelItemMeta(
        string? Rationale,
        string? Fit,
        IReadOnlyList<string> MustTokens);

    private sealed class ModelResponse
    {
        public List<string>? AnalysisPoints { get; set; }

        public List<ModelItem>? ListNow { get; set; }

        public List<ModelItem>? Upcoming { get; set; }
    }

    private sealed class ModelItem
    {
        public string? Id { get; set; }

        public string? Rationale { get; set; }

        public string? Fit { get; set; }

        [JsonConverter(typeof(FlexibleStringListJsonConverter))]
        public List<string>? MustTokens { get; set; }
    }

    private sealed record SuggestPicks(
        List<string> Points,
        List<ShopKeywordResearchSuggest.AcceptedKeyword> Now,
        List<ShopKeywordResearchSuggest.AcceptedKeyword> Upcoming);

    private sealed class SuggestModelResponse
    {
        public List<string>? AnalysisPoints { get; set; }

        public List<SuggestModelItem>? ListNow { get; set; }

        public List<SuggestModelItem>? Upcoming { get; set; }
    }

    private sealed class SuggestModelItem
    {
        public string? Keyword { get; set; }

        public string? Rationale { get; set; }

        public string? Fit { get; set; }

        [JsonConverter(typeof(FlexibleStringListJsonConverter))]
        public List<string>? MustTokens { get; set; }
    }
}
