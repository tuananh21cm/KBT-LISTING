namespace Application.DTOs.Listing;

/// <summary>Filters for «đề xuất hủy list» (same scope as the analytics table).</summary>
public class ListingDelistSuggestionQuery
{
    /// <summary>Snapshot day to judge; null = latest day that has data in scope.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Shop to judge. Null resolves to the only shop in scope, otherwise the call is a no-op.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Seller filter (forced from JWT on the seller API).</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Rows to return (default 12, max 50).</summary>
    public int Limit { get; set; } = 12;

    /// <summary>
    /// Lowest score to return. Defaults to — and is floored at — <c>ListingDelistScoring.ReviewThreshold</c>,
    /// so the result only ever holds <c>delist</c> and <c>review</c> rows.
    /// </summary>
    public int? MinScore { get; set; }
}

/// <summary>Weak products worth delisting to free TikTok publish slots, plus what to list instead.</summary>
public class ListingDelistSuggestionDto
{
    /// <summary>Shop the verdicts belong to; null when the scope could not be narrowed to one shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Snapshot day the verdicts were computed from.</summary>
    public DateOnly? SnapshotDate { get; set; }

    /// <summary>Compass range of that snapshot day.</summary>
    public DateOnly? RangeStart { get; set; }

    /// <summary>Compass range of that snapshot day.</summary>
    public DateOnly? RangeEnd { get; set; }

    /// <summary>Currency of the GMV figures.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Products on the snapshot day that were scored.</summary>
    public int Scanned { get; set; }

    /// <summary>Skipped because they still sell (peak orders / GMV share above the keep guard).</summary>
    public int SkippedWinners { get; set; }

    /// <summary>Skipped because they have no snapshot or are younger than 7 calendar days live.</summary>
    public int SkippedTooNew { get; set; }

    /// <summary>Scored but healthy enough to stay.</summary>
    public int SkippedHealthy { get; set; }

    /// <summary>Rows whose verdict is <c>delist</c>.</summary>
    public int DelistCount { get; set; }

    /// <summary>Rows whose verdict is <c>review</c>.</summary>
    public int ReviewCount { get; set; }

    /// <summary>TikTok <c>publish_limit.total_limit</c> of the shop.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>TikTok <c>publish_limit.published_num</c> of the shop.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>Slots still free (0 when the limit is unknown).</summary>
    public int RemainingSlots { get; set; }

    /// <summary>How many of <see cref="Items"/> to actually delist to fit the keywords waiting.</summary>
    public int RecommendedDelistCount { get; set; }

    /// <summary>One-line Vietnamese summary for the panel header.</summary>
    public string? Headline { get; set; }

    /// <summary>Non-fatal notes (no shop selected, keyword research missing, …).</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Shop medians the verdicts were compared against.</summary>
    public ListingDelistBenchmarksDto Benchmarks { get; set; } = new();

    /// <summary>Weakest products first.</summary>
    public List<ListingDelistItemDto> Items { get; set; } = [];

    /// <summary>Keywords from «Shop này bán gì» to list into the freed slots.</summary>
    public List<ListingDelistKeywordDto> Keywords { get; set; } = [];
}

/// <summary>Shop medians of the snapshot day — the bar each product is measured against.</summary>
public class ListingDelistBenchmarksDto
{
    /// <summary>Median CTR of products with enough impressions (fraction).</summary>
    public decimal MedianCtr { get; set; }

    /// <summary>Median add-to-cart ÷ clicks (fraction).</summary>
    public decimal MedianAtcRate { get; set; }

    /// <summary>Median orders ÷ add-to-cart (fraction).</summary>
    public decimal MedianOrderRate { get; set; }

    /// <summary>Median impressions per product.</summary>
    public long MedianImpressions { get; set; }

    /// <summary>Shop GMV in the snapshot's Compass window (not a single calendar day).</summary>
    public decimal Gmv { get; set; }

    /// <summary>Number of Pacific days each snapshot row aggregates (<c>last7days</c>).</summary>
    public int WindowDays { get; set; }

    /// <summary>Snapshot sync rows the peak lookup scans back (not calendar metric days).</summary>
    public int TrendWindowDays { get; set; }

    /// <summary>
    /// Minimum snapshot sync rows before judging (1 — each row is already a full
    /// <see cref="WindowDays"/>‑day Compass window).
    /// </summary>
    public int MinTrackedDays { get; set; }

    /// <summary>Minimum calendar days live before judging (matches Compass window length).</summary>
    public int MinListedDays { get; set; }
}

/// <summary>One weak product with its score, reasons, and the local listing to act on.</summary>
public class ListingDelistItemDto
{
    /// <summary>TikTok product id.</summary>
    public string TikTokProductId { get; set; } = string.Empty;

    /// <summary>Local listing when mirrored — enables the deactivate action.</summary>
    public Guid? ListingId { get; set; }

    /// <summary>4Seller listing id when mirrored.</summary>
    public long? FourSellerListingId { get; set; }

    /// <summary>Local listing site status when linked.</summary>
    public string? SiteStatus { get; set; }

    /// <summary>Product title.</summary>
    public string? ProductName { get; set; }

    /// <summary>Product thumbnail.</summary>
    public string? MainImage { get; set; }

    /// <summary>GMV in the snapshot's Compass window.</summary>
    public decimal Gmv { get; set; }

    /// <summary>Paid orders in the snapshot's Compass window.</summary>
    public int Orders { get; set; }

    /// <summary>Impressions in the snapshot's Compass window.</summary>
    public long Impressions { get; set; }

    /// <summary>Clicks in the snapshot's Compass window.</summary>
    public long Clicks { get; set; }

    /// <summary>CTR in the snapshot's Compass window (fraction).</summary>
    public decimal? Ctr { get; set; }

    /// <summary>Add-to-cart units on the snapshot day.</summary>
    public int AddToCartUnits { get; set; }

    /// <summary>GMV change vs prior Compass window (fraction, e.g. −0.37 = −37%).</summary>
    public decimal? GmvRatio { get; set; }

    /// <summary>Orders change vs prior Compass window (fraction).</summary>
    public decimal? OrdersRatio { get; set; }

    /// <summary>Impressions change vs prior Compass window (fraction).</summary>
    public decimal? ImpressionsRatio { get; set; }

    /// <summary>Best order count in any <c>last7days</c> window among recent sync rows.</summary>
    public int PeakOrders { get; set; }

    /// <summary>Best GMV in any <c>last7days</c> window among recent sync rows.</summary>
    public decimal PeakGmv { get; set; }

    /// <summary>Distinct snapshot sync rows in the trend window (not calendar metric days).</summary>
    public int DaysTracked { get; set; }

    /// <summary>Calendar days live on the snapshot day (VN); null when unknown.</summary>
    public int? DaysListed { get; set; }

    /// <summary>Delist pressure 0–100 (higher = stronger case for freeing the slot).</summary>
    public int Score { get; set; }

    /// <summary><c>delist</c> | <c>review</c>.</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>Per-pillar contribution behind <see cref="Score"/>.</summary>
    public ListingDelistScoreBreakdownDto Breakdown { get; set; } = new();

    /// <summary>Share of the best-matching shop keyword's tokens found in the title (0–1).</summary>
    public double? KeywordFit { get; set; }

    /// <summary>Keyword that matched best, when any.</summary>
    public string? BestKeyword { get; set; }

    /// <summary>Vietnamese explanation lines, strongest first.</summary>
    public List<string> Reasons { get; set; } = [];
}

/// <summary>Points each pillar contributed (weights 40 / 25 / 15 / 10 / 10, relief up to −20).</summary>
public class ListingDelistScoreBreakdownDto
{
    /// <summary>No sales despite holding a slot (max 40).</summary>
    public int Sales { get; set; }

    /// <summary>CTR + add-to-cart + order-rate shortfall vs shop medians (max 25).</summary>
    public int Funnel { get; set; }

    /// <summary>Impressions below the shop median (max 15).</summary>
    public int Reach { get; set; }

    /// <summary>Declining GMV / impressions (max 10).</summary>
    public int Trend { get; set; }

    /// <summary>Title off the shop's target keywords (max 10).</summary>
    public int Keyword { get; set; }

    /// <summary>Points won back because the product is growing (max 20).</summary>
    public int RisingRelief { get; set; }

    /// <summary>
    /// Weight that could actually be judged. The score is the earned share of this, so a product
    /// with no traffic to measure is not quietly capped below the delist threshold.
    /// </summary>
    public int Available { get; set; }
}

/// <summary>Keyword waiting for a slot, copied from the stored «Shop này bán gì» analysis.</summary>
public class ListingDelistKeywordDto
{
    /// <summary>Keyword to list against.</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>TikTok search volume of the keyword.</summary>
    public int SearchVolume { get; set; }

    /// <summary>Keyword research score 0–100 when the payload has one.</summary>
    public int? Score { get; set; }

    /// <summary><c>listNow</c> | <c>upcoming</c>.</summary>
    public string Lane { get; set; } = string.Empty;
}
