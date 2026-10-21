using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Local mirror of a 4Seller listing (Draft / Published). Unique by <see cref="FourSellerListingId"/>.
/// Distinct from <see cref="ShopProduct"/> (local TikTok edit form).
/// </summary>
public class Listing : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>4Seller internal listing id.</summary>
    public long FourSellerListingId { get; set; }

    /// <summary>Seller owning the 4Seller cookie used to sync this listing.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Local shop when matched via FourSeller shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>4Seller shop id from the listing payload.</summary>
    public long? FourSellerShopId { get; set; }

    /// <summary>Platform code (e.g. tiktok).</summary>
    public string Platform { get; set; } = "tiktok";

    /// <summary>Draft vs published bucket (<c>draft</c> | <c>published</c>).</summary>
    public string PageType { get; set; } = "draft";

    /// <summary>
    /// Site / marketplace status: draft, active, inactive, pending (Reviewing), removed, suspended, …
    /// Synced from 4Seller <c>erpStatus</c> + TikTok <c>productStatus</c>.
    /// </summary>
    public string? SiteStatus { get; set; }

    /// <summary>Product title.</summary>
    public string? ProductName { get; set; }

    /// <summary>Main image URL (may be pipe-separated).</summary>
    public string? MainImage { get; set; }

    /// <summary>Site / marketplace (US, …).</summary>
    public string? Site { get; set; }

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Lowest variant price.</summary>
    public decimal? LowPrice { get; set; }

    /// <summary>Highest variant price.</summary>
    public decimal? HighPrice { get; set; }

    /// <summary>Total available stock.</summary>
    public int? AvailableStock { get; set; }

    /// <summary>Variation count.</summary>
    public int VariationCount { get; set; }

    /// <summary>TikTok category id.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Category display name.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Warehouse id.</summary>
    public string? WarehouseId { get; set; }

    /// <summary>Source product URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Local-only: how this listing was added (<c>recommended</c> | <c>manual</c>).
    /// Null = unknown / created before origin tracking. Write-once; sync does not overwrite.
    /// </summary>
    public string? AddedFrom { get; set; }

    /// <summary>Source review / comment count (e.g. Shein <c>comment_num</c>). Local-only.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Source average rating (e.g. Shein <c>comment_rank_average</c>). Local-only.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Staged supplier PDP snapshot (rating / stock / seller) linked after sync.</summary>
    public Guid? SupplierProductInfoId { get; set; }

    /// <summary>HTML description.</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>
    /// 4Seller <c>searchTerms</c> (JSON string array). Fill-once from RawPayload when empty;
    /// patched back on save-draft / publish / update.
    /// </summary>
    public string? SearchTermsJson { get; set; }

    /// <summary>
    /// 4Seller <c>productHighlights</c> (JSON string array). Fill-once from RawPayload when empty;
    /// patched back on save-draft / publish / update.
    /// </summary>
    public string? ProductHighlightsJson { get; set; }

    /// <summary>
    /// The supplier's own description, captured the first time 4Seller sends one and never
    /// written again. <see cref="DescriptionHtml"/> holds whatever the shopper will see, so an
    /// AI rewrite replaces the <c>Key: Value</c> spec rows that Auto Specifics parses; keeping
    /// the original here is what lets Specifics and Rewrite run in either order.
    ///
    /// Write-once rather than tracking 4Seller: once a rewritten description is pushed, the
    /// remote copy is our own copy, so refreshing this field would overwrite the source data
    /// with the text derived from it.
    /// </summary>
    public string? SupplierDescriptionHtml { get; set; }

    /// <summary>Package weight.</summary>
    public decimal? PackageWeight { get; set; }

    /// <summary>Package length.</summary>
    public decimal? PackageLength { get; set; }

    /// <summary>Package width.</summary>
    public decimal? PackageWidth { get; set; }

    /// <summary>Package height.</summary>
    public decimal? PackageHeight { get; set; }

    /// <summary>Weight unit.</summary>
    public string? WeightUnit { get; set; }

    /// <summary>Dimension unit.</summary>
    public string? DimensionUnit { get; set; }

    /// <summary>4Seller erp / listing status raw.</summary>
    public string? ErpStatus { get; set; }

    /// <summary>4Seller publish status raw.</summary>
    public string? PublishStatus { get; set; }

    /// <summary>TikTok product id when published.</summary>
    public string? TikTokProductId { get; set; }

    /// <summary>Platform create time (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? PlatformCreateTime { get; set; }

    /// <summary>Platform update time (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? PlatformUpdateTime { get; set; }

    /// <summary>Raw 4Seller detail JSON (required for save-draft / publish push).</summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// Local-only jsonb snapshot of the last applied batch pricing formula
    /// (supplier fees, promo %, target margin/profit, optional template id).
    /// Survives 4Seller re-sync; not included in RawPayload / marketplace push.
    /// Preview promo/profit on edit UI is reference-only — not used for sales reporting.
    /// </summary>
    public string? AppliedPricingJson { get; set; }

    /// <summary>
    /// Local-only: seller applied AI Rewrite to description (Save Draft / Publish).
    /// Not pushed to 4Seller. When true (or <see cref="DescriptionImagesInserted"/>),
    /// sync keeps <see cref="DescriptionHtml"/> and does not overwrite from 4Seller.
    /// </summary>
    public bool DescriptionRewritten { get; set; }

    /// <summary>
    /// Local-only: seller ran Insert Images after Rewrite (optional; does not block Publish).
    /// Not pushed to 4Seller. When true (or <see cref="DescriptionRewritten"/>),
    /// sync keeps <see cref="DescriptionHtml"/> and does not overwrite from 4Seller.
    /// </summary>
    public bool DescriptionImagesInserted { get; set; }

    /// <summary>Last successful pull sync from 4Seller.</summary>
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last successful push (save-draft / publish).</summary>
    public DateTime? LastPushAt { get; set; }

    /// <summary>
    /// Last sync/push error, or 4Seller page <c>failedMessage</c> (e.g. publish failed on draft).
    /// </summary>
    public string? SyncError { get; set; }

    /// <summary>How listing reached TikTok: <c>manual</c> or <c>auto</c>.</summary>
    public string? PublishSource { get; set; }

    /// <summary>Scheduler skips listing until retry or manual publish.</summary>
    public bool AutoPublishSkipped { get; set; }

  /// <summary>When auto publish was skipped due to failure.</summary>
    public DateTime? AutoPublishSkippedAt { get; set; }

    /// <summary>Reason auto publish was skipped.</summary>
    public string? AutoPublishSkipReason { get; set; }

    /// <summary>Most recent auto publish run log.</summary>
    public Guid? LastAutoPublishRunId { get; set; }

    /// <summary>
    /// jsonb array of auto publish step codes already completed for the current draft prep cycle.
    /// Persisted after each successful step so retry resumes without re-running prep.
    /// </summary>
    public string? AutoPublishCompletedStepsJson { get; set; }

    /// <summary>Owner navigation.</summary>
    public ApplicationUser Owner { get; set; } = null!;

    /// <summary>Shop navigation.</summary>
    public Shop? Shop { get; set; }

    /// <summary>Supplier PDP snapshot navigation.</summary>
    public SupplierProductInfo? SupplierProductInfo { get; set; }

    /// <summary>SKU variations.</summary>
    public ICollection<ListingVariation> Variations { get; set; } = new List<ListingVariation>();
}
