using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// One TikTok Compass "Product analysis" row captured for a shop on a Vietnam calendar day.
/// Keyed by <see cref="ShopId"/> + <see cref="TikTokProductId"/> + <see cref="SnapshotDate"/> so a
/// second sync on the same day overwrites the metrics instead of appending a row.
///
/// <para><b>Not daily metrics.</b> Each row stores the aggregate for a Compass <c>last7days</c>
/// rolling window (<see cref="RangeStart"/>…<see cref="RangeEnd"/>, Pacific). Syncing every day
/// shifts that window by one calendar day; re-syncing the same VN day overwrites the row.</para>
/// </summary>
public class ListingAnalyticsSnapshot : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shop the metrics belong to (resolved from the extension seller id).</summary>
    public Guid ShopId { get; set; }

    /// <summary>Seller who owns the shop at sync time (denormalized for seller-scoped reads).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>TikTok <c>product_id</c> from Compass.</summary>
    public string TikTokProductId { get; set; } = string.Empty;

    /// <summary>Local listing with the same TikTok product id; null until the listing is synced.</summary>
    public Guid? ListingId { get; set; }

    /// <summary>Vietnam calendar day the sync landed on (dedupe key — not a metric day).</summary>
    public DateOnly SnapshotDate { get; set; }

    /// <summary>Inclusive first day of the Compass <c>last7days</c> window (shop Pacific).</summary>
    public DateOnly RangeStart { get; set; }

    /// <summary>Inclusive last day of the Compass <c>last7days</c> window (shop Pacific).</summary>
    public DateOnly RangeEnd { get; set; }

    /// <summary>Compass range preset the numbers came from (<c>last7days</c>).</summary>
    public string Preset { get; set; } = "last7days";

    /// <summary>Product title as shown in Compass (fallback when the listing is missing).</summary>
    public string? ProductName { get; set; }

    /// <summary>Product thumbnail URL from Compass.</summary>
    public string? MainImage { get; set; }

    /// <summary>Currency of <see cref="Gmv"/>.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>GMV for the Compass window (not a single calendar day).</summary>
    public decimal Gmv { get; set; }

    /// <summary>Paid orders (SKU orders) for the Compass window.</summary>
    public int Orders { get; set; }

    /// <summary>Product impressions for the Compass window.</summary>
    public long Impressions { get; set; }

    /// <summary>Product page views / clicks for the Compass window.</summary>
    public long Clicks { get; set; }

    /// <summary>Click-through rate as a fraction (0.0123 = 1.23%).</summary>
    public decimal? Ctr { get; set; }

    /// <summary>Units added to cart for the range.</summary>
    public int? AddToCartUnits { get; set; }

    /// <summary>Distinct buyers who added to cart for the range.</summary>
    public int? AddToCartBuyers { get; set; }

    /// <summary>GMV change vs the previous range, as a fraction (0.12 = +12%).</summary>
    public decimal? GmvRatio { get; set; }

    /// <summary>Orders change vs the previous range, as a fraction.</summary>
    public decimal? OrdersRatio { get; set; }

    /// <summary>Impressions change vs the previous range, as a fraction.</summary>
    public decimal? ImpressionsRatio { get; set; }

    /// <summary>Clicks change vs the previous range, as a fraction.</summary>
    public decimal? ClicksRatio { get; set; }

    /// <summary>CTR change vs the previous range, as a fraction.</summary>
    public decimal? CtrRatio { get; set; }

    /// <summary>Add-to-cart units change vs the previous range, as a fraction.</summary>
    public decimal? AddToCartUnitsRatio { get; set; }

    /// <summary>Add-to-cart buyers change vs the previous range, as a fraction.</summary>
    public decimal? AddToCartBuyersRatio { get; set; }

    /// <summary>
    /// Raw <c>stats_v3</c> blob (jsonb) so a TikTok schema change never silently drops metrics
    /// we have not mapped into columns yet.
    /// </summary>
    public string? RawStatsJson { get; set; }

    /// <summary>Last time this row was overwritten by a sync.</summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Shop navigation.</summary>
    public Shop Shop { get; set; } = null!;

    /// <summary>Listing navigation when linked.</summary>
    public Listing? Listing { get; set; }
}
