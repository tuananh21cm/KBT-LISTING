using System.Text.Json;

namespace Application.DTOs.Listing;

/// <summary>
/// Batch of TikTok Compass "Product analysis" rows scraped by the browser extension.
/// The server decides the snapshot day, so a replay never lands on the wrong date.
/// </summary>
public class SyncListingAnalyticsDto
{
    /// <summary>Run id from extension unified Sync (optional).</summary>
    public string? RunId { get; set; }

    /// <summary>TikTok seller / OEC id — matches <c>Shop.ExternalShopId</c>.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>Alias of <see cref="ExternalShopId"/> (extension may send sellerId).</summary>
    public string? SellerId { get; set; }

    /// <summary>Compass range preset the numbers came from (<c>last7days</c>).</summary>
    public string? Preset { get; set; }

    /// <summary>Inclusive first day of the Compass range (shop timezone).</summary>
    public DateOnly? RangeStart { get; set; }

    /// <summary>Inclusive last day of the Compass range (shop timezone).</summary>
    public DateOnly? RangeEnd { get; set; }

    /// <summary>Mapped product rows (already field-stripped).</summary>
    public List<SyncListingAnalyticsItemDto> Items { get; set; } = [];
}

/// <summary>One Compass product row. Ratios are fractions (0.12 = +12%).</summary>
public class SyncListingAnalyticsItemDto
{
    /// <summary>TikTok <c>product_id</c>.</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Product title from Compass.</summary>
    public string? ProductName { get; set; }

    /// <summary>Product thumbnail URL.</summary>
    public string? MainImage { get; set; }

    /// <summary>Currency of <see cref="Gmv"/> (defaults to USD).</summary>
    public string? Currency { get; set; }

    /// <summary>GMV for the range.</summary>
    public decimal? Gmv { get; set; }

    /// <summary>Paid orders (SKU orders).</summary>
    public int? Orders { get; set; }

    /// <summary>Product impressions.</summary>
    public long? Impressions { get; set; }

    /// <summary>Product page views / clicks.</summary>
    public long? Clicks { get; set; }

    /// <summary>Click-through rate as a fraction.</summary>
    public decimal? Ctr { get; set; }

    /// <summary>Units added to cart.</summary>
    public int? AddToCartUnits { get; set; }

    /// <summary>Distinct buyers who added to cart.</summary>
    public int? AddToCartBuyers { get; set; }

    /// <summary>GMV change vs the previous range (fraction).</summary>
    public decimal? GmvRatio { get; set; }

    /// <summary>Orders change vs the previous range (fraction).</summary>
    public decimal? OrdersRatio { get; set; }

    /// <summary>Impressions change vs the previous range (fraction).</summary>
    public decimal? ImpressionsRatio { get; set; }

    /// <summary>Clicks change vs the previous range (fraction).</summary>
    public decimal? ClicksRatio { get; set; }

    /// <summary>CTR change vs the previous range (fraction).</summary>
    public decimal? CtrRatio { get; set; }

    /// <summary>Add-to-cart units change vs the previous range (fraction).</summary>
    public decimal? AddToCartUnitsRatio { get; set; }

    /// <summary>Add-to-cart buyers change vs the previous range (fraction).</summary>
    public decimal? AddToCartBuyersRatio { get; set; }

    /// <summary>
    /// Raw TikTok <c>stats_v3.total</c> object, re-serialized server side into the jsonb column so
    /// metrics we do not map into columns yet are still recoverable.
    /// </summary>
    public JsonElement? RawStats { get; set; }
}

/// <summary>Result of one analytics ingest.</summary>
public class SyncListingAnalyticsResultDto
{
    /// <summary>Matched shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Vietnam calendar day the rows were written to.</summary>
    public DateOnly SnapshotDate { get; set; }

    /// <summary>Rows created for this day.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows overwritten for this day.</summary>
    public int Updated { get; set; }

    /// <summary>Rows whose <c>ListingId</c> is now linked to a local listing.</summary>
    public int Linked { get; set; }

    /// <summary>Payload rows dropped (empty / duplicate product id).</summary>
    public int Skipped { get; set; }
}

/// <summary>Query filters for the analytics table.</summary>
public class ListingAnalyticsListQuery
{
    /// <summary>1-based page (alias of <see cref="PageCurrent"/>).</summary>
    public int? Page { get; set; }

    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (max 100).</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Snapshot day; null = latest day that has data in scope.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Filter by local shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Seller filter (forced from JWT on the seller API).</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Free-text search on product name / TikTok product id.</summary>
    public string? Search { get; set; }

    /// <summary><c>gmv</c> (default) / <c>orders</c> / <c>impressions</c> / <c>clicks</c> / <c>ctr</c> / <c>atc</c> / <c>name</c>.</summary>
    public string? Sort { get; set; }

    /// <summary><c>desc</c> (default) / <c>asc</c>.</summary>
    public string? SortDir { get; set; }
}

/// <summary>Paged analytics rows for one snapshot day.</summary>
public class ListingAnalyticsPageDto
{
    /// <summary>Rows.</summary>
    public List<ListingAnalyticsItemDto> Items { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Snapshot day the rows belong to (null when the scope has no data).</summary>
    public DateOnly? SnapshotDate { get; set; }

    /// <summary>
    /// Snapshot day used as the baseline for computed % deltas when Compass did not send ratios —
    /// <see cref="ListingAnalyticsMetrics.WindowDays"/> VN days before <see cref="SnapshotDate"/>,
    /// not the previous calendar sync day (each row is a rolling 7-day window).
    /// </summary>
    public DateOnly? PreviousSnapshotDate { get; set; }

    /// <summary>Compass range of the returned rows.</summary>
    public DateOnly? RangeStart { get; set; }

    /// <summary>Compass range of the returned rows.</summary>
    public DateOnly? RangeEnd { get; set; }

    /// <summary>Latest ingest time in the filtered scope.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Totals across the whole filtered scope (not just the current page).</summary>
    public ListingAnalyticsTotalsDto Totals { get; set; } = new();
}

/// <summary>Scope-wide totals for the summary cards.</summary>
public class ListingAnalyticsTotalsDto
{
    /// <summary>Products in scope.</summary>
    public int Products { get; set; }

    /// <summary>Total GMV.</summary>
    public decimal Gmv { get; set; }

    /// <summary>Total orders.</summary>
    public int Orders { get; set; }

    /// <summary>Total impressions.</summary>
    public long Impressions { get; set; }

    /// <summary>Total clicks.</summary>
    public long Clicks { get; set; }

    /// <summary>Total add-to-cart units.</summary>
    public int AddToCartUnits { get; set; }

    /// <summary>Clicks / impressions across the scope (fraction).</summary>
    public decimal? Ctr { get; set; }
}

/// <summary>One analytics row for the dense table.</summary>
public class ListingAnalyticsItemDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>TikTok shop profile name.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Shop owner id.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner username.</summary>
    public string? OwnerUserName { get; set; }

    /// <summary>Owner display name.</summary>
    public string? OwnerFullName { get; set; }

    /// <summary>Local listing when linked.</summary>
    public Guid? ListingId { get; set; }

    /// <summary>TikTok product id.</summary>
    public string TikTokProductId { get; set; } = string.Empty;

    /// <summary>Product title (listing title wins when linked).</summary>
    public string? ProductName { get; set; }

    /// <summary>Product thumbnail.</summary>
    public string? MainImage { get; set; }

    /// <summary>Listing origin when linked (<c>recommended</c> | <c>manual</c>).</summary>
    public string? AddedFrom { get; set; }

    /// <summary>Source review count when listing linked.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Source average rating when listing linked.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Supplier store title when listing linked.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Supplier store URL when listing linked.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Supplier shop rank when listing linked.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Supplier store follow count when listing linked.</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Supplier shipping SLA days when listing linked.</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Supplier shipping SLA percent when listing linked.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Local US product flag when listing linked.</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Quick ship flag when listing linked.</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>GMV currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Snapshot day.</summary>
    public DateOnly SnapshotDate { get; set; }

    /// <summary>Compass range start.</summary>
    public DateOnly RangeStart { get; set; }

    /// <summary>Compass range end.</summary>
    public DateOnly RangeEnd { get; set; }

    /// <summary>Compass range preset.</summary>
    public string Preset { get; set; } = string.Empty;

    /// <summary>GMV for the range.</summary>
    public decimal Gmv { get; set; }

    /// <summary>Paid orders.</summary>
    public int Orders { get; set; }

    /// <summary>Impressions.</summary>
    public long Impressions { get; set; }

    /// <summary>Clicks.</summary>
    public long Clicks { get; set; }

    /// <summary>CTR as a fraction.</summary>
    public decimal? Ctr { get; set; }

    /// <summary>Add-to-cart units.</summary>
    public int? AddToCartUnits { get; set; }

    /// <summary>Add-to-cart buyers.</summary>
    public int? AddToCartBuyers { get; set; }

    /// <summary>GMV delta vs previous range (fraction).</summary>
    public decimal? GmvRatio { get; set; }

    /// <summary>Orders delta vs previous range (fraction).</summary>
    public decimal? OrdersRatio { get; set; }

    /// <summary>Impressions delta vs previous range (fraction).</summary>
    public decimal? ImpressionsRatio { get; set; }

    /// <summary>Clicks delta vs previous range (fraction).</summary>
    public decimal? ClicksRatio { get; set; }

    /// <summary>CTR delta vs previous range (fraction).</summary>
    public decimal? CtrRatio { get; set; }

    /// <summary>Add-to-cart units delta vs previous range (fraction).</summary>
    public decimal? AddToCartUnitsRatio { get; set; }

    /// <summary>Add-to-cart buyers delta vs previous range (fraction).</summary>
    public decimal? AddToCartBuyersRatio { get; set; }

    /// <summary>
    /// Where the deltas came from: <c>tiktok</c> (sent by Compass) or <c>computed</c>
    /// (derived from <see cref="ListingAnalyticsPageDto.PreviousSnapshotDate"/>). Null when unknown.
    /// </summary>
    public string? RatioSource { get; set; }

    /// <summary>Last ingest time for this row.</summary>
    public DateTime SyncedAt { get; set; }
}

/// <summary>Snapshot days that have data, for the date picker.</summary>
public class ListingAnalyticsDatesDto
{
    /// <summary>Days with data, newest first (max 90).</summary>
    public List<DateOnly> Dates { get; set; } = [];

    /// <summary>Newest day with data.</summary>
    public DateOnly? Latest { get; set; }
}
