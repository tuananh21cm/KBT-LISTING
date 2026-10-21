using Application.DTOs.Listing;
using Application.DTOs.ProductOpportunity;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Assembles «đề xuất hủy list» from data already in the database: the Compass snapshot day being
/// viewed, the shop's own medians of that day, the peak of the last
/// <see cref="ListingDelistScoring.TrendWindowDays"/> snapshot days, the TikTok publish limit, and
/// the keywords stored by «Shop này bán gì». Scoring lives in <see cref="ListingDelistScoring"/>.
/// Counters are also persisted to <see cref="ListingDelistDaySummary"/> for the shop-task board.
/// </summary>
public class ListingDelistSuggestionService : IListingDelistSuggestionService
{
    /// <summary>Products of one snapshot day we are willing to score in a single request.</summary>
    private const int MaxScanRows = 2000;

    /// <summary>Keywords offered as replacements.</summary>
    private const int MaxKeywords = 12;

    /// <summary>Explanation lines kept per row.</summary>
    private const int MaxReasons = 3;

    private readonly IApplicationDbContext _db;

    /// <summary>Creates the service.</summary>
    public ListingDelistSuggestionService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ListingDelistSuggestionDto> SuggestAsync(
        ListingDelistSuggestionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit < 1 ? 12 : query.Limit, 1, 50);

        // Floor at ReviewThreshold so `items` is always delist ∪ review — the two buckets the panel
        // and the counters below know how to talk about. A caller can only raise the bar.
        var minScore = Math.Clamp(
            query.MinScore ?? ListingDelistScoring.ReviewThreshold,
            ListingDelistScoring.ReviewThreshold,
            100);

        var result = new ListingDelistSuggestionDto
        {
            ShopId = query.ShopId,
            Benchmarks = new ListingDelistBenchmarksDto
            {
                WindowDays = ListingAnalyticsMetrics.WindowDays,
                TrendWindowDays = ListingDelistScoring.TrendWindowDays,
                MinTrackedDays = ListingDelistScoring.MinTrackedDays,
                MinListedDays = ListingDelistScoring.MinListedDays
            }
        };

        var scope = _db.ListingAnalyticsSnapshots.AsNoTracking().AsQueryable();
        if (query.OwnerUserId.HasValue)
        {
            scope = scope.Where(x => x.OwnerId == query.OwnerUserId.Value);
        }

        if (query.ShopId.HasValue)
        {
            scope = scope.Where(x => x.ShopId == query.ShopId.Value);
        }

        var day = query.Date ?? await scope.MaxAsync(x => (DateOnly?)x.SnapshotDate, cancellationToken);
        if (!day.HasValue)
        {
            result.Warnings.Add("Chưa có snapshot Compass nào — bấm Analytics trên extension TikTok Shop.");
            return result;
        }

        result.SnapshotDate = day;

        var shopId = query.ShopId ?? await ResolveSingleShopAsync(scope, day.Value, cancellationToken);
        if (!shopId.HasValue)
        {
            result.Warnings.Add("Chọn 1 shop — công thức cần trung vị, slot và keyword của riêng shop đó.");
            return result;
        }

        result.ShopId = shopId;

        // Ownership gate: a client-supplied shopId + date must not reach another seller's snapshots
        // (an explicit `date` bypasses the scoped day lookup above).
        var shop = await LoadShopAsync(shopId.Value, query.OwnerUserId, cancellationToken);
        if (shop is null && query.OwnerUserId.HasValue)
        {
            result.Warnings.Add("Shop không thuộc tài khoản này.");
            return result;
        }

        var scored = await ScoreShopDayAsync(
            shopId.Value,
            day.Value,
            ownerUserId: query.OwnerUserId,
            shop,
            includeItems: true,
            itemMinScore: minScore,
            itemLimit: limit,
            cancellationToken);

        if (scored is null)
        {
            result.Warnings.Add("Ngày snapshot này chưa có sản phẩm nào cho shop đã chọn.");
            return result;
        }

        ApplyScoreResult(result, scored);
        await UpsertSummaryAsync(shopId.Value, day.Value, scored, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task ComputeAndPersistAsync(
        Guid shopId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await LoadShopAsync(shopId, ownerUserId: null, cancellationToken);
        var scored = await ScoreShopDayAsync(
            shopId,
            snapshotDate,
            ownerUserId: null,
            shop,
            includeItems: false,
            itemMinScore: ListingDelistScoring.ReviewThreshold,
            itemLimit: 0,
            cancellationToken);

        if (scored is null)
        {
            return;
        }

        await UpsertSummaryAsync(shopId, snapshotDate, scored, cancellationToken);
    }

    /// <summary>
    /// Scores one shop/day. Returns null when there are no snapshot rows.
    /// Counters always bucket by verdict kind; <paramref name="itemMinScore"/> only filters
    /// <see cref="ScoreResult.Items"/>.
    /// </summary>
    private async Task<ScoreResult?> ScoreShopDayAsync(
        Guid shopId,
        DateOnly day,
        Guid? ownerUserId,
        ShopSlots? shop,
        bool includeItems,
        int itemMinScore,
        int itemLimit,
        CancellationToken cancellationToken)
    {
        var rows = await LoadDayRowsAsync(shopId, ownerUserId, day, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var peaks = await LoadPeaksAsync(shopId, ownerUserId, day, cancellationToken);
        var research = await LoadResearchAsync(shopId, cancellationToken);
        var (keywordTokenSets, keywordLabels) = BuildKeywordTargets(research);
        var keywords = BuildKeywordList(research);

        var remainingSlots = shop?.PublishLimitTotal is > 0
            ? Math.Max(0, shop.PublishLimitTotal.Value - (shop.PublishLimitPublished ?? 0))
            : 0;

        var candidates = rows
            .Select(r =>
            {
                peaks.TryGetValue(r.TikTokProductId, out var peak);
                var listedAt = ListingDelistScoring.ResolveListedAtUtc(
                    r.PlatformCreateTime,
                    r.PlatformUpdateTime,
                    r.ListingCreatedAt);
                var daysListed = ListingDelistScoring.ComputeDaysListed(
                    day,
                    listedAt,
                    peak?.FirstSnapshotDate);
                return new ListingDelistScoring.Candidate(
                    r.Gmv,
                    r.Orders,
                    r.Impressions,
                    r.Clicks,
                    r.AddToCartUnits ?? 0,
                    r.Ctr,
                    r.GmvRatio,
                    r.OrdersRatio,
                    r.ImpressionsRatio,
                    PeakOrdersOf(peaks, r),
                    PeakGmvOf(peaks, r),
                    DaysTrackedOf(peaks, r),
                    daysListed);
            })
            .ToList();

        var benchmarks = ListingDelistScoring.BuildBenchmarks(candidates);
        var scoredItems = includeItems ? new List<ListingDelistItemDto>() : null;
        var delistCount = 0;
        var reviewCount = 0;
        var skippedWinners = 0;
        var skippedTooNew = 0;
        var skippedHealthy = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var verdict = ListingDelistScoring.Score(
                candidates[i],
                benchmarks,
                row.ProductName ?? row.ListingProductName,
                keywordTokenSets,
                keywordLabels);

            switch (verdict.Kind)
            {
                case ListingDelistScoring.VerdictKeep:
                    skippedWinners++;
                    continue;
                case ListingDelistScoring.VerdictNew:
                    skippedTooNew++;
                    continue;
                case ListingDelistScoring.VerdictDelist:
                    delistCount++;
                    break;
                case ListingDelistScoring.VerdictReview:
                    reviewCount++;
                    break;
                case ListingDelistScoring.VerdictHealthy:
                default:
                    skippedHealthy++;
                    continue;
            }

            if (scoredItems is null || verdict.Score < itemMinScore)
            {
                continue;
            }

            scoredItems.Add(ToItem(row, candidates[i], verdict));
        }

        var keywordsWaiting = keywords.Count(k => k.Lane == ShopKeywordResearchPersistence.LaneListNow);
        var recommended = Math.Min(delistCount, Math.Max(0, keywordsWaiting - remainingSlots));

        List<ListingDelistItemDto> items = [];
        if (scoredItems is not null)
        {
            items = scoredItems
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Gmv)
                .ThenBy(x => x.Impressions)
                .Take(itemLimit)
                .ToList();
        }

        return new ScoreResult(
            rows.Count,
            delistCount,
            reviewCount,
            skippedWinners,
            skippedTooNew,
            skippedHealthy,
            recommended,
            remainingSlots,
            shop?.Name,
            shop?.PublishLimitTotal,
            shop?.PublishLimitPublished,
            rows.Find(r => !string.IsNullOrWhiteSpace(r.Currency))?.Currency ?? "USD",
            rows.Min(r => r.RangeStart),
            rows.Max(r => r.RangeEnd),
            benchmarks,
            keywords,
            keywordsWaiting,
            keywordTokenSets.Count == 0,
            shop?.PublishLimitTotal is null or <= 0,
            items);
    }

    private static void ApplyScoreResult(ListingDelistSuggestionDto result, ScoreResult scored)
    {
        result.Scanned = scored.Scanned;
        result.DelistCount = scored.DelistCount;
        result.ReviewCount = scored.ReviewCount;
        result.SkippedWinners = scored.SkippedWinners;
        result.SkippedTooNew = scored.SkippedTooNew;
        result.SkippedHealthy = scored.SkippedHealthy;
        result.RecommendedDelistCount = scored.RecommendedDelistCount;
        result.RemainingSlots = scored.RemainingSlots;
        result.ShopName = scored.ShopName;
        result.PublishLimitTotal = scored.PublishLimitTotal;
        result.PublishLimitPublished = scored.PublishLimitPublished;
        result.Currency = scored.Currency;
        result.RangeStart = scored.RangeStart;
        result.RangeEnd = scored.RangeEnd;
        result.Benchmarks.MedianCtr = scored.Benchmarks.MedianCtr;
        result.Benchmarks.MedianAtcRate = scored.Benchmarks.MedianAtcRate;
        result.Benchmarks.MedianOrderRate = scored.Benchmarks.MedianOrderRate;
        result.Benchmarks.MedianImpressions = scored.Benchmarks.MedianImpressions;
        result.Benchmarks.Gmv = scored.Benchmarks.TotalGmv;
        result.Keywords = scored.Keywords;
        result.Items = scored.Items;

        if (scored.MissingPublishLimit)
        {
            result.Warnings.Add(
                "Chưa có publish_limit từ TikTok — sync shop trên extension để biết còn bao nhiêu slot.");
        }

        if (scored.MissingKeywords)
        {
            result.Warnings.Add(
                "Chưa phân tích «Shop này bán gì» — điểm keyword bỏ trống, chỉ chấm theo chỉ số.");
        }

        result.Headline = BuildHeadline(result, scored.KeywordsWaiting);
    }

    private async Task UpsertSummaryAsync(
        Guid shopId,
        DateOnly snapshotDate,
        ScoreResult scored,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.ListingDelistDaySummaries
            .FirstOrDefaultAsync(
                x => x.ShopId == shopId && x.SnapshotDate == snapshotDate,
                cancellationToken);

        if (existing is null)
        {
            existing = new ListingDelistDaySummary
            {
                ShopId = shopId,
                SnapshotDate = snapshotDate,
                CreatedAt = now
            };
            _db.ListingDelistDaySummaries.Add(existing);
        }
        else
        {
            existing.UpdatedAt = now;
        }

        existing.Scanned = scored.Scanned;
        existing.DelistCount = scored.DelistCount;
        existing.ReviewCount = scored.ReviewCount;
        existing.SkippedWinners = scored.SkippedWinners;
        existing.SkippedTooNew = scored.SkippedTooNew;
        existing.SkippedHealthy = scored.SkippedHealthy;
        existing.RecommendedDelistCount = scored.RecommendedDelistCount;
        existing.PublishLimitTotal = scored.PublishLimitTotal;
        existing.PublishLimitPublished = scored.PublishLimitPublished;
        existing.ComputedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Only judge when the scope narrows to a single shop — medians/slots/keywords are per shop.</summary>
    private static async Task<Guid?> ResolveSingleShopAsync(
        IQueryable<ListingAnalyticsSnapshot> scope,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var shopIds = await scope
            .Where(x => x.SnapshotDate == day)
            .Select(x => x.ShopId)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);
        return shopIds.Count == 1 ? shopIds[0] : null;
    }

    /// <summary>
    /// Slim projection of the snapshot day. Capped at <see cref="MaxScanRows"/> by impressions — far
    /// above any TikTok publish limit, so the medians are never computed from a truncated set in
    /// practice.
    /// </summary>
    private async Task<List<DayRow>> LoadDayRowsAsync(
        Guid shopId,
        Guid? ownerUserId,
        DateOnly day,
        CancellationToken cancellationToken) =>
        await _db.ListingAnalyticsSnapshots.AsNoTracking()
            .Where(x => x.ShopId == shopId
                && x.SnapshotDate == day
                && (ownerUserId == null || x.OwnerId == ownerUserId.Value)
                // Snapshot giữ lại sau hard-delete listing (FK SetNull) — chỉ chấm product
                // còn mirror local và đang active (đã deactivate/xóa thì không đề xuất nữa).
                && x.ListingId != null
                && x.Listing != null
                && x.Listing.SiteStatus == ListingSiteStatuses.Active)
            .OrderByDescending(x => x.Impressions)
            .Take(MaxScanRows)
            .Select(x => new DayRow(
                x.TikTokProductId,
                x.ListingId,
                x.Listing != null ? (long?)x.Listing.FourSellerListingId : null,
                x.Listing != null ? x.Listing.SiteStatus : null,
                x.ProductName,
                x.Listing != null ? x.Listing.ProductName : null,
                x.MainImage,
                x.Listing != null ? x.Listing.MainImage : null,
                x.Listing != null ? x.Listing.PlatformCreateTime : null,
                x.Listing != null ? x.Listing.PlatformUpdateTime : null,
                x.Listing != null ? (DateTime?)x.Listing.CreatedAt : null,
                x.Currency,
                x.RangeStart,
                x.RangeEnd,
                x.Gmv,
                x.Orders,
                x.Impressions,
                x.Clicks,
                x.Ctr,
                x.AddToCartUnits,
                x.GmvRatio,
                x.OrdersRatio,
                x.ImpressionsRatio))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Peak over the trend window, never a sum: each Compass row already covers a rolling 7-day
    /// window, so adjacent snapshot days double-count the same orders.
    /// </summary>
    private async Task<Dictionary<string, PeakRow>> LoadPeaksAsync(
        Guid shopId,
        Guid? ownerUserId,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var windowStart = day.AddDays(-(ListingDelistScoring.TrendWindowDays - 1));
        var rows = await _db.ListingAnalyticsSnapshots.AsNoTracking()
            .Where(x => x.ShopId == shopId
                && x.SnapshotDate >= windowStart
                && x.SnapshotDate <= day
                && (ownerUserId == null || x.OwnerId == ownerUserId.Value))
            .GroupBy(x => x.TikTokProductId)
            .Select(g => new PeakRow(
                g.Key,
                g.Count(),
                g.Min(x => x.SnapshotDate),
                g.Max(x => x.Orders),
                g.Max(x => x.Gmv)))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.TikTokProductId, StringComparer.Ordinal);
    }

    private async Task<ShopSlots?> LoadShopAsync(
        Guid shopId,
        Guid? ownerUserId,
        CancellationToken cancellationToken) =>
        await _db.Shops.AsNoTracking()
            .Where(s => s.Id == shopId && (ownerUserId == null || s.OwnerId == ownerUserId.Value))
            .Select(s => new ShopSlots(s.Name, s.PublishLimitTotal, s.PublishLimitPublished))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<ShopKeywordResearchDto?> LoadResearchAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var payload = await _db.ShopKeywordResearches.AsNoTracking()
            .Where(x => x.ShopId == shopId)
            .Select(x => x.PayloadJson)
            .FirstOrDefaultAsync(cancellationToken);
        return ShopKeywordResearchPersistence.Deserialize(payload);
    }

    /// <summary>
    /// Tokens the shop wants to rank for. AI <c>mustTokens</c> win when present because they already
    /// dropped the filler words; otherwise the keyword's own content tokens are used.
    /// </summary>
    private static (List<IReadOnlyList<string>> TokenSets, List<string> Labels) BuildKeywordTargets(
        ShopKeywordResearchDto? research)
    {
        var tokenSets = new List<IReadOnlyList<string>>();
        var labels = new List<string>();
        if (research is null)
        {
            return (tokenSets, labels);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in research.ListNow.Concat(research.Upcoming))
        {
            var key = ShopKeywordResearchPersistence.Normalize(item.Keyword);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            var tokens = item.MustTokens is { Count: > 0 }
                ? item.MustTokens
                    .Select(t => ShopKeywordResearchScoring.Normalize(t))
                    .Where(t => t.Length > 1)
                    .ToList()
                : ShopKeywordResearchScoring.ContentTokens(
                    ShopKeywordResearchScoring.Normalize(item.Keyword));
            if (tokens.Count == 0)
            {
                continue;
            }

            tokenSets.Add(tokens);
            labels.Add(item.Keyword);
        }

        return (tokenSets, labels);
    }

    private static List<ListingDelistKeywordDto> BuildKeywordList(ShopKeywordResearchDto? research)
    {
        if (research is null)
        {
            return [];
        }

        var list = new List<ListingDelistKeywordDto>();
        Append(research.ListNow, ShopKeywordResearchPersistence.LaneListNow);
        Append(research.Upcoming, ShopKeywordResearchPersistence.LaneUpcoming);
        return list;

        void Append(IEnumerable<ShopKeywordResearchItemDto> items, string lane)
        {
            foreach (var item in items)
            {
                if (list.Count >= MaxKeywords)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(item.Keyword))
                {
                    continue;
                }

                list.Add(new ListingDelistKeywordDto
                {
                    Keyword = item.Keyword.Trim(),
                    SearchVolume = item.SearchVolume,
                    Score = item.Score,
                    Lane = lane
                });
            }
        }
    }

    private static ListingDelistItemDto ToItem(
        DayRow row,
        ListingDelistScoring.Candidate candidate,
        ListingDelistScoring.Verdict verdict) =>
        new()
        {
            TikTokProductId = row.TikTokProductId,
            ListingId = row.ListingId,
            FourSellerListingId = row.FourSellerListingId,
            SiteStatus = row.SiteStatus,
            ProductName = FirstNonEmpty(row.ProductName, row.ListingProductName),
            MainImage = FirstImage(FirstNonEmpty(row.MainImage, row.ListingMainImage)),
            Gmv = row.Gmv,
            Orders = row.Orders,
            Impressions = row.Impressions,
            Clicks = row.Clicks,
            Ctr = row.Ctr ?? ListingAnalyticsMetrics.CtrFrom(row.Clicks, row.Impressions),
            AddToCartUnits = row.AddToCartUnits ?? 0,
            GmvRatio = row.GmvRatio,
            OrdersRatio = row.OrdersRatio,
            ImpressionsRatio = row.ImpressionsRatio,
            PeakOrders = candidate.PeakOrders,
            PeakGmv = candidate.PeakGmv,
            DaysTracked = candidate.DaysTracked,
            DaysListed = candidate.DaysListed,
            Score = verdict.Score,
            Verdict = verdict.Kind,
            KeywordFit = verdict.KeywordFit,
            BestKeyword = verdict.BestKeyword,
            Breakdown = new ListingDelistScoreBreakdownDto
            {
                Sales = verdict.Breakdown.Sales,
                Funnel = verdict.Breakdown.Funnel,
                Reach = verdict.Breakdown.Reach,
                Trend = verdict.Breakdown.Trend,
                Keyword = verdict.Breakdown.Keyword,
                RisingRelief = verdict.Breakdown.RisingRelief,
                Available = verdict.Breakdown.Available
            },
            Reasons = verdict.Reasons.Take(MaxReasons).ToList()
        };

    private static string? BuildHeadline(ListingDelistSuggestionDto result, int keywordsWaiting)
    {
        if (result.Items.Count == 0)
        {
            if (result.Scanned <= 0)
            {
                return null;
            }

            if (result.SkippedTooNew > 0)
            {
                return
                    $"Đã soi {result.Scanned} sản phẩm — {result.SkippedTooNew} mới list hoặc chưa có snapshot, " +
                    $"{result.SkippedHealthy} ổn, {result.SkippedWinners} đang bán.";
            }

            if (result.SkippedHealthy > 0)
            {
                return
                    $"Đã soi {result.Scanned} sản phẩm — {result.SkippedHealthy} listing chưa yếu đủ " +
                    $"(ngưỡng {ListingDelistScoring.ReviewThreshold}+), {result.SkippedWinners} đang bán.";
            }

            return result.Scanned > 0
                ? $"Đã soi {result.Scanned} sản phẩm — chưa có listing nào đủ yếu để đề xuất hủy."
                : null;
        }

        var parts = new List<string>
        {
            $"{result.DelistCount} listing nên hủy, {result.ReviewCount} cần xem xét trên {result.Scanned} sản phẩm."
        };

        if (result.PublishLimitTotal is > 0)
        {
            parts.Add($"Slot: {result.PublishLimitPublished ?? 0}/{result.PublishLimitTotal} đã dùng, còn {result.RemainingSlots}.");
        }

        if (keywordsWaiting > 0)
        {
            parts.Add(result.RecommendedDelistCount > 0
                ? $"Hủy {result.RecommendedDelistCount} cái là đủ chỗ cho {keywordsWaiting} keyword đang chờ list."
                : $"{keywordsWaiting} keyword đang chờ list đã có đủ slot trống.");
        }

        return string.Join(" ", parts);
    }

    private static int PeakOrdersOf(Dictionary<string, PeakRow> peaks, DayRow row) =>
        peaks.TryGetValue(row.TikTokProductId, out var peak)
            ? Math.Max(peak.PeakOrders, row.Orders)
            : row.Orders;

    private static decimal PeakGmvOf(Dictionary<string, PeakRow> peaks, DayRow row) =>
        peaks.TryGetValue(row.TikTokProductId, out var peak)
            ? Math.Max(peak.PeakGmv, row.Gmv)
            : row.Gmv;

    private static int DaysTrackedOf(Dictionary<string, PeakRow> peaks, DayRow row) =>
        peaks.TryGetValue(row.TikTokProductId, out var peak) ? Math.Max(peak.DaysTracked, 1) : 1;

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim()
        : !string.IsNullOrWhiteSpace(second) ? second.Trim()
        : null;

    /// <summary>Listing images can be pipe-separated; the panel only needs the first one.</summary>
    private static string? FirstImage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var first = value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private sealed record ScoreResult(
        int Scanned,
        int DelistCount,
        int ReviewCount,
        int SkippedWinners,
        int SkippedTooNew,
        int SkippedHealthy,
        int RecommendedDelistCount,
        int RemainingSlots,
        string? ShopName,
        int? PublishLimitTotal,
        int? PublishLimitPublished,
        string Currency,
        DateOnly RangeStart,
        DateOnly RangeEnd,
        ListingDelistScoring.Benchmarks Benchmarks,
        List<ListingDelistKeywordDto> Keywords,
        int KeywordsWaiting,
        bool MissingKeywords,
        bool MissingPublishLimit,
        List<ListingDelistItemDto> Items);

    private sealed record DayRow(
        string TikTokProductId,
        Guid? ListingId,
        long? FourSellerListingId,
        string? SiteStatus,
        string? ProductName,
        string? ListingProductName,
        string? MainImage,
        string? ListingMainImage,
        DateTime? PlatformCreateTime,
        DateTime? PlatformUpdateTime,
        DateTime? ListingCreatedAt,
        string Currency,
        DateOnly RangeStart,
        DateOnly RangeEnd,
        decimal Gmv,
        int Orders,
        long Impressions,
        long Clicks,
        decimal? Ctr,
        int? AddToCartUnits,
        decimal? GmvRatio,
        decimal? OrdersRatio,
        decimal? ImpressionsRatio);

    private sealed record PeakRow(
        string TikTokProductId,
        int DaysTracked,
        DateOnly FirstSnapshotDate,
        int PeakOrders,
        decimal PeakGmv);

    private sealed record ShopSlots(string Name, int? PublishLimitTotal, int? PublishLimitPublished);
}
