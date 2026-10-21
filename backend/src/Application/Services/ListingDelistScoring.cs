namespace Application.Services;

using Shared.Helpers;

/// <summary>
/// Formula behind «đề xuất hủy list»: turns one Compass snapshot row plus the shop benchmarks of the
/// same snapshot day into a 0–100 <em>delist pressure</em> score (higher = stronger case for freeing
/// the slot). Pure so the weights, guards and thresholds stay unit-testable.
///
/// <para>Each snapshot row is a <b>rolling 7-day</b> Compass aggregate (<see cref="WindowDays"/>),
/// not a single calendar day. History reads <c>MAX</c> over recent sync rows (never <c>SUM</c>) —
/// consecutive sync days overlap on six of seven window days.</para>
/// </summary>
public static class ListingDelistScoring
{
    /// <summary>No sales despite occupying a slot — the dominant pillar.</summary>
    public const int SalesWeight = 40;

    /// <summary>CTR shortfall vs the shop median.</summary>
    public const int CtrWeight = 10;

    /// <summary>Add-to-cart rate shortfall vs the shop median.</summary>
    public const int AtcWeight = 8;

    /// <summary>Order rate (orders ÷ add-to-cart) shortfall vs the shop median.</summary>
    public const int OrderRateWeight = 7;

    /// <summary>Impressions far below the shop median — TikTok already buried the product.</summary>
    public const int ReachWeight = 15;

    /// <summary>Declining GMV / impressions vs the previous range.</summary>
    public const int TrendWeight = 10;

    /// <summary>Title does not match any keyword the shop is targeting.</summary>
    public const int KeywordWeight = 10;

    /// <summary>Largest amount a product that is taking off can win back.</summary>
    public const int RisingReliefMax = 20;

    /// <summary>Compass <c>last7days</c> span — each snapshot row covers this window.</summary>
    public const int WindowDays = ListingAnalyticsMetrics.WindowDays;

    /// <summary>Snapshot sync rows back the peak lookup covers (≈ four weekly windows).</summary>
    public const int TrendWindowDays = 28;

    /// <summary>
    /// Minimum distinct snapshot sync rows before judging. Each row is already a full
    /// <see cref="WindowDays"/>‑day Compass aggregate with <c>total_prev</c> ratios, so one sync
    /// is enough to score — a second sync only shifts the window by one day (6/7 overlap).
    /// </summary>
    public const int MinTrackedDays = 1;

    /// <summary>
    /// Minimum calendar days live before the Compass <c>last7days</c> window is representative — a
    /// product listed 3 days ago only has ~3 days of real exposure inside a 7-day aggregate.
    /// </summary>
    public const int MinListedDays = WindowDays;

    /// <summary>Peak 7-day orders that already make a product worth keeping.</summary>
    public const int WinnerOrders = 4;

    /// <summary>Share of shop GMV that makes a product worth keeping regardless of funnel health.</summary>
    public const decimal WinnerGmvShare = 0.02m;

    /// <summary>Peak orders where the sales pillar reaches zero.</summary>
    public const int OrdersHealthy = 6;

    /// <summary>Impressions a product needs before its CTR is compared to the shop.</summary>
    public const long MinCtrImpressions = 100;

    /// <summary>Clicks a product needs before its ATC rate is compared to the shop.</summary>
    public const long MinAtcClicks = 20;

    /// <summary>Add-to-cart units a product needs before its order rate is compared to the shop.</summary>
    public const int MinOrderRateAtc = 5;

    /// <summary>Decline (fraction) where the trend pillar saturates.</summary>
    public const decimal DeclineFull = 0.5m;

    /// <summary>Growth (fraction) where the rising relief saturates.</summary>
    public const decimal RisingFull = 0.5m;

    /// <summary>Keyword token coverage that clears the keyword pillar.</summary>
    public const double KeywordFitTarget = 0.5;

    /// <summary>Score at or above which the product is proposed for delisting.</summary>
    public const int DelistThreshold = 70;

    /// <summary>Score at or above which the product is worth a manual review.</summary>
    public const int ReviewThreshold = 50;

    /// <summary>Proposed for delisting now.</summary>
    public const string VerdictDelist = "delist";

    /// <summary>Weak but not conclusive — seller decides.</summary>
    public const string VerdictReview = "review";

    /// <summary>Selling well enough to keep the slot.</summary>
    public const string VerdictKeep = "keep";

    /// <summary>Not tracked long enough to judge.</summary>
    public const string VerdictNew = "new";

    /// <summary>Scored but below <see cref="ReviewThreshold"/>.</summary>
    public const string VerdictHealthy = "healthy";

    /// <summary>One product on the snapshot day, plus its peak over the trend window.</summary>
    public sealed record Candidate(
        decimal Gmv,
        int Orders,
        long Impressions,
        long Clicks,
        int AddToCartUnits,
        decimal? Ctr,
        decimal? GmvRatio,
        decimal? OrdersRatio,
        decimal? ImpressionsRatio,
        int PeakOrders,
        decimal PeakGmv,
        /// <summary>Distinct snapshot sync rows in the trend window (not calendar metric days).</summary>
        int DaysTracked,
        /// <summary>Calendar days live as of the snapshot day (VN); null = unknown.</summary>
        int? DaysListed);

    /// <summary>Shop medians of the same snapshot day — what "normal" looks like for this shop.</summary>
    public sealed record Benchmarks(
        decimal MedianCtr,
        decimal MedianAtcRate,
        decimal MedianOrderRate,
        long MedianImpressions,
        decimal TotalGmv);

    /// <summary>
    /// Per-pillar contribution so the UI can explain the score. <paramref name="Available"/> is the
    /// weight that could actually be judged — the final score is the earned share of it, so a
    /// product with no traffic to measure is not quietly capped below the delist threshold.
    /// </summary>
    public sealed record Breakdown(
        int Sales,
        int Funnel,
        int Reach,
        int Trend,
        int Keyword,
        int RisingRelief,
        int Available);

    /// <summary>Scoring outcome for one product.</summary>
    public sealed record Verdict(
        int Score,
        string Kind,
        Breakdown Breakdown,
        double? KeywordFit,
        string? BestKeyword,
        List<string> Reasons);

    /// <summary>
    /// Median of the shop's per-product CTR / ATC rate / order rate / impressions. Rows without
    /// enough volume for a rate are skipped so a handful of dead products cannot drag the bar down.
    /// </summary>
    public static Benchmarks BuildBenchmarks(IReadOnlyList<Candidate> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var ctrs = new List<decimal>();
        var atcRates = new List<decimal>();
        var orderRates = new List<decimal>();
        var impressions = new List<decimal>();
        var totalGmv = 0m;

        foreach (var row in rows)
        {
            if (row.DaysListed is < MinListedDays)
            {
                continue;
            }

            totalGmv += row.Gmv;
            impressions.Add(row.Impressions);

            if (row.Impressions >= MinCtrImpressions)
            {
                ctrs.Add(row.Ctr ?? (decimal)row.Clicks / row.Impressions);
            }

            if (row.Clicks >= MinAtcClicks)
            {
                atcRates.Add((decimal)row.AddToCartUnits / row.Clicks);
            }

            if (row.AddToCartUnits >= MinOrderRateAtc)
            {
                orderRates.Add((decimal)row.Orders / row.AddToCartUnits);
            }
        }

        return new Benchmarks(
            Median(ctrs),
            Median(atcRates),
            Median(orderRates),
            (long)Math.Round(Median(impressions), MidpointRounding.AwayFromZero),
            totalGmv);
    }

    /// <summary>
    /// Delist pressure for one product. <paramref name="keywordTokenSets"/> holds the content tokens
    /// of each keyword the shop targets (empty = keyword pillar stays neutral).
    /// </summary>
    public static Verdict Score(
        Candidate row,
        Benchmarks shop,
        string? productName,
        IReadOnlyList<IReadOnlyList<string>>? keywordTokenSets,
        IReadOnlyList<string>? keywordLabels)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(shop);

        var reasons = new List<string>();
        var (keywordFit, bestKeyword) = MatchKeyword(productName, keywordTokenSets, keywordLabels);

        var gmvShare = shop.TotalGmv > 0m ? row.PeakGmv / shop.TotalGmv : 0m;
        if (row.PeakOrders >= WinnerOrders || gmvShare >= WinnerGmvShare)
        {
            reasons.Add($"Đang bán được ({row.PeakOrders} đơn / cửa sổ {WindowDays} ngày tốt nhất) — giữ slot.");
            return new Verdict(0, VerdictKeep, Zero, keywordFit, bestKeyword, reasons);
        }

        if (row.DaysTracked < MinTrackedDays)
        {
            reasons.Add(
                $"Chưa có snapshot Compass — cần ít nhất {MinTrackedDays} lần sync (mỗi lần đã là tổng {WindowDays} ngày).");
            return new Verdict(0, VerdictNew, Zero, keywordFit, bestKeyword, reasons);
        }

        if (row.DaysListed is < MinListedDays)
        {
            reasons.Add(
                $"Mới list {row.DaysListed} ngày — chờ đủ {MinListedDays} ngày để cửa sổ {WindowDays} ngày phản ánh đủ.");
            return new Verdict(0, VerdictNew, Zero, keywordFit, bestKeyword, reasons);
        }

        var sales = ScoreSales(row, reasons);
        var funnel = ScoreFunnel(row, shop, reasons, out var funnelAvailable);
        var reach = ScoreReach(row, shop, reasons, out var reachAvailable);
        var trend = ScoreTrend(row, reasons, out var trendAvailable);
        var keyword = ScoreKeyword(keywordFit, bestKeyword, reasons, out var keywordAvailable);
        var relief = ScoreRisingRelief(row, reasons);

        var available = SalesWeight + funnelAvailable + reachAvailable + trendAvailable + keywordAvailable;
        var earned = sales + funnel + reach + trend + keyword;
        var normalized = (int)Math.Round(100d * earned / available, MidpointRounding.AwayFromZero);
        var score = Math.Clamp(normalized - relief, 0, 100);
        var kind = score >= DelistThreshold
            ? VerdictDelist
            : score >= ReviewThreshold
                ? VerdictReview
                : VerdictHealthy;

        return new Verdict(
            score,
            kind,
            new Breakdown(sales, funnel, reach, trend, keyword, relief, available),
            keywordFit,
            bestKeyword,
            reasons);
    }

    private static readonly Breakdown Zero = new(0, 0, 0, 0, 0, 0, SalesWeight);

    /// <summary>No orders anywhere in the trend window is the strongest signal we have.</summary>
    private static int ScoreSales(Candidate row, List<string> reasons)
    {
        var strength = Math.Sqrt(Math.Clamp((double)row.PeakOrders / OrdersHealthy, 0d, 1d));
        var points = (int)Math.Round(SalesWeight * (1d - strength), MidpointRounding.AwayFromZero);

        if (row.PeakOrders == 0)
        {
            reasons.Add(row.Impressions > 0
                ? $"0 đơn trong cửa sổ {WindowDays} ngày dù có {FormatCount(row.Impressions)} hiển thị."
                : $"0 đơn và 0 hiển thị trong cửa sổ {WindowDays} ngày.");
        }
        else if (points > 0)
        {
            reasons.Add($"Chỉ {row.PeakOrders} đơn ở cửa sổ {WindowDays} ngày tốt nhất gần đây.");
        }

        return points;
    }

    /// <summary>How far each funnel step sits below the shop median (steps without volume are skipped).</summary>
    private static int ScoreFunnel(Candidate row, Benchmarks shop, List<string> reasons, out int available)
    {
        var points = 0;
        available = 0;

        if (shop.MedianCtr > 0m && row.Impressions >= MinCtrImpressions)
        {
            available += CtrWeight;
            var ctr = row.Ctr ?? (decimal)row.Clicks / row.Impressions;
            var shortfall = Shortfall(ctr, shop.MedianCtr);
            points += Weighted(CtrWeight, shortfall);
            if (shortfall >= 0.4)
            {
                reasons.Add($"CTR {FormatRate(ctr)} thấp hơn trung vị shop {FormatRate(shop.MedianCtr)}.");
            }
        }

        if (shop.MedianAtcRate > 0m && row.Clicks >= MinAtcClicks)
        {
            available += AtcWeight;
            var atcRate = (decimal)row.AddToCartUnits / row.Clicks;
            var shortfall = Shortfall(atcRate, shop.MedianAtcRate);
            points += Weighted(AtcWeight, shortfall);
            if (shortfall >= 0.4)
            {
                reasons.Add(
                    $"Tỷ lệ thêm giỏ {FormatRate(atcRate)} thấp hơn trung vị {FormatRate(shop.MedianAtcRate)}.");
            }
        }

        if (shop.MedianOrderRate > 0m && row.AddToCartUnits >= MinOrderRateAtc)
        {
            available += OrderRateWeight;
            var orderRate = (decimal)row.Orders / row.AddToCartUnits;
            var shortfall = Shortfall(orderRate, shop.MedianOrderRate);
            points += Weighted(OrderRateWeight, shortfall);
            if (shortfall >= 0.4)
            {
                reasons.Add(
                    $"Chốt đơn {FormatRate(orderRate)} trên giỏ, thấp hơn trung vị {FormatRate(shop.MedianOrderRate)}.");
            }
        }

        return points;
    }

    /// <summary>A product the algorithm stopped showing cannot recover on its own.</summary>
    private static int ScoreReach(Candidate row, Benchmarks shop, List<string> reasons, out int available)
    {
        available = 0;
        if (shop.MedianImpressions <= 0)
        {
            return 0;
        }

        available = ReachWeight;
        var ratio = (double)row.Impressions / shop.MedianImpressions;
        var points = Weighted(ReachWeight, 1d - Math.Clamp(ratio, 0d, 1d));
        if (ratio <= 0.35)
        {
            reasons.Add(
                $"Hiển thị {FormatCount(row.Impressions)} chỉ bằng {ratio * 100:0}% trung vị shop.");
        }

        return points;
    }

    /// <summary>Compass deltas: the sharper of GMV / impressions decline drives the pillar.</summary>
    private static int ScoreTrend(Candidate row, List<string> reasons, out int available)
    {
        // No delta at all means Compass has nothing to compare — judging the trend would be a guess.
        available = row.GmvRatio.HasValue || row.OrdersRatio.HasValue || row.ImpressionsRatio.HasValue
            ? TrendWeight
            : 0;

        var gmvDrop = row.GmvRatio.HasValue ? -row.GmvRatio.Value : 0m;
        var impressionsDrop = row.ImpressionsRatio.HasValue ? -row.ImpressionsRatio.Value : 0m;
        var decline = Math.Max(Math.Max(gmvDrop, impressionsDrop), 0m);
        if (decline <= 0m)
        {
            return 0;
        }

        var points = Weighted(TrendWeight, (double)Math.Min(decline / DeclineFull, 1m));
        if (decline >= 0.3m)
        {
            reasons.Add(gmvDrop >= impressionsDrop
                ? $"GMV giảm {decline * 100:0}% so kỳ trước."
                : $"Hiển thị giảm {decline * 100:0}% so kỳ trước.");
        }

        return points;
    }

    /// <summary>Off-niche titles are the first thing to swap out for a keyword the shop is targeting.</summary>
    private static int ScoreKeyword(double? fit, string? bestKeyword, List<string> reasons, out int available)
    {
        available = 0;
        if (fit is null)
        {
            return 0;
        }

        available = KeywordWeight;
        var points = Weighted(KeywordWeight, 1d - Math.Clamp(fit.Value / KeywordFitTarget, 0d, 1d));
        if (fit.Value <= 0.2)
        {
            reasons.Add("Tên sản phẩm không khớp keyword shop đang nhắm.");
        }
        else if (points > 0 && bestKeyword is not null)
        {
            reasons.Add($"Khớp keyword «{bestKeyword}» yếu ({fit.Value * 100:0}% token).");
        }

        return points;
    }

    /// <summary>Never propose killing a product that just started taking off.</summary>
    private static int ScoreRisingRelief(Candidate row, List<string> reasons)
    {
        var growth = Max3(row.GmvRatio, row.OrdersRatio, row.ImpressionsRatio);
        if (growth <= 0m)
        {
            return 0;
        }

        var relief = Weighted(RisingReliefMax, (double)Math.Min(growth / RisingFull, 1m));
        if (relief > 0)
        {
            reasons.Add($"Đang tăng {growth * 100:0}% so kỳ trước — theo dõi thêm trước khi hủy.");
        }

        return relief;
    }

    /// <summary>
    /// Best keyword coverage: share of a keyword's content tokens that appear in the product title.
    /// Null when the shop has no analyzed keywords (pillar stays neutral instead of guessing).
    /// </summary>
    private static (double? Fit, string? Keyword) MatchKeyword(
        string? productName,
        IReadOnlyList<IReadOnlyList<string>>? keywordTokenSets,
        IReadOnlyList<string>? keywordLabels)
    {
        if (keywordTokenSets is null || keywordTokenSets.Count == 0)
        {
            return (null, null);
        }

        var titleTokens = ShopKeywordResearchScoring
            .ContentTokens(ShopKeywordResearchScoring.Normalize(productName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (titleTokens.Count == 0)
        {
            return (0d, null);
        }

        var bestFit = 0d;
        string? bestKeyword = null;
        for (var i = 0; i < keywordTokenSets.Count; i++)
        {
            var tokens = keywordTokenSets[i];
            if (tokens.Count == 0)
            {
                continue;
            }

            var hits = tokens.Count(titleTokens.Contains);
            var fit = (double)hits / tokens.Count;
            if (fit <= bestFit)
            {
                continue;
            }

            bestFit = fit;
            bestKeyword = keywordLabels is not null && i < keywordLabels.Count ? keywordLabels[i] : null;
        }

        return (bestFit, bestKeyword);
    }

    private static double Shortfall(decimal actual, decimal median) =>
        median <= 0m ? 0d : Math.Clamp(1d - (double)(actual / median), 0d, 1d);

    private static int Weighted(int weight, double share) =>
        (int)Math.Round(weight * Math.Clamp(share, 0d, 1d), MidpointRounding.AwayFromZero);

    private static decimal Max3(decimal? a, decimal? b, decimal? c) =>
        Math.Max(Math.Max(a ?? 0m, b ?? 0m), c ?? 0m);

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2m;
    }

    private static string FormatRate(decimal fraction)
    {
        var percent = fraction * 100m;
        return percent >= 1m
            ? $"{percent:0.#}%"
            : $"{percent:0.##}%";
    }

    private static string FormatCount(long value) =>
        value >= 1_000_000
            ? $"{value / 1_000_000m:0.#}M"
            : value >= 1_000
                ? $"{value / 1_000m:0.#}K"
                : value.ToString();

    /// <summary>
    /// Listed clock for delist scoring — same precedence as dashboard listing trends.
    /// </summary>
    public static DateTime? ResolveListedAtUtc(
        DateTime? platformCreateTime,
        DateTime? platformUpdateTime,
        DateTime? listingCreatedAt) =>
        platformCreateTime ?? platformUpdateTime ?? listingCreatedAt;

    /// <summary>
    /// Inclusive calendar days live on the snapshot day (VN). Uses listing platform time when
    /// mirrored; otherwise the first Compass snapshot date in the trend window.
    /// </summary>
    public static int? ComputeDaysListed(
        DateOnly snapshotDate,
        DateTime? listedAtUtc,
        DateOnly? firstSnapshotDate)
    {
        DateOnly? listedOn = null;
        if (listedAtUtc.HasValue)
        {
            listedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(listedAtUtc.Value, DateTimeKind.Utc),
                AppTimeZones.Vietnam));
        }
        else if (firstSnapshotDate.HasValue)
        {
            listedOn = firstSnapshotDate;
        }

        if (!listedOn.HasValue)
        {
            return null;
        }

        return snapshotDate.DayNumber - listedOn.Value.DayNumber + 1;
    }
}
