using System.Text.Json.Serialization;

namespace Application.DTOs.Listing;

/// <summary>Paged local listings with optional status counts.</summary>
public class ListingListPageDto
{
    /// <summary>Rows.</summary>
    public List<ListingListItemDto> Items { get; set; } = [];

    /// <summary>Total matching current filters (including siteStatus).</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Counts by siteStatus within the same filter scope (excluding siteStatus tab).</summary>
    public ListingStatusCountsDto StatusCounts { get; set; } = new();
}

/// <summary>Aggregate counts for Published tabs.</summary>
public class ListingStatusCountsDto
{
    /// <summary>All listings in scope (pageType filter, no siteStatus).</summary>
    public int All { get; set; }

    /// <summary>Draft count (pageType=draft).</summary>
    public int Draft { get; set; }

    /// <summary>Published total.</summary>
    public int Published { get; set; }

    /// <summary>Active.</summary>
    public int Active { get; set; }

    /// <summary>Inactive.</summary>
    public int Inactive { get; set; }

    /// <summary>Removed.</summary>
    public int Removed { get; set; }

    /// <summary>Incomplete.</summary>
    public int Incomplete { get; set; }

    /// <summary>Suspended.</summary>
    public int Suspended { get; set; }

    /// <summary>Private.</summary>
    public int Private { get; set; }

    /// <summary>Pending.</summary>
    public int Pending { get; set; }

    /// <summary>Reject.</summary>
    public int Reject { get; set; }

    /// <summary>Unsaleable.</summary>
    public int Unsaleable { get; set; }
}

/// <summary>Listing card row.</summary>
public class ListingListItemDto
{
    /// <summary>Local id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller listing id.</summary>
    public long FourSellerListingId { get; set; }

    /// <summary>Platform.</summary>
    public string Platform { get; set; } = "tiktok";

    /// <summary>draft | published.</summary>
    public string PageType { get; set; } = "draft";

    /// <summary>Site status.</summary>
    public string? SiteStatus { get; set; }

    /// <summary>Product name.</summary>
    public string? ProductName { get; set; }

    /// <summary>Main image.</summary>
    public string? MainImage { get; set; }

    /// <summary>Site.</summary>
    public string? Site { get; set; }

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Low price.</summary>
    public decimal? LowPrice { get; set; }

    /// <summary>High price.</summary>
    public decimal? HighPrice { get; set; }

    /// <summary>Stock.</summary>
    public int? AvailableStock { get; set; }

    /// <summary>Variation count.</summary>
    public int VariationCount { get; set; }

    /// <summary>Category name.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Erp status.</summary>
    public string? ErpStatus { get; set; }

    /// <summary>Publish status.</summary>
    public string? PublishStatus { get; set; }

    /// <summary>TikTok product id.</summary>
    public string? TikTokProductId { get; set; }

    /// <summary>Platform update time.</summary>
    public DateTime? PlatformUpdateTime { get; set; }

    /// <summary>Last synced.</summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>Last push.</summary>
    public DateTime? LastPushAt { get; set; }

    /// <summary>Sync error.</summary>
    public string? SyncError { get; set; }

    /// <summary>manual | auto publish source.</summary>
    public string? PublishSource { get; set; }

    /// <summary>Scheduler skips until retry/manual.</summary>
    public bool AutoPublishSkipped { get; set; }

    /// <summary>When auto publish was skipped.</summary>
    public DateTime? AutoPublishSkippedAt { get; set; }

    /// <summary>Skip reason summary.</summary>
    public string? AutoPublishSkipReason { get; set; }

    /// <summary>Latest auto publish run log id.</summary>
    public Guid? LastAutoPublishRunId { get; set; }

    /// <summary>Draft only: waiting | processing | failed — auto publish queue UI.</summary>
    public string? AutoPublishQueueStatus { get; set; }

    /// <summary>Draft waiting: UTC when shop interval ends (countdown badge).</summary>
    public DateTime? AutoPublishNextPickAt { get; set; }

    /// <summary>Status of the latest auto publish run (failed/success/…).</summary>
    public string? LastAutoPublishRunStatus { get; set; }

    /// <summary>Owner id.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>4Seller shop id (extension multi-select / unmark per shop).</summary>
    public long? FourSellerShopId { get; set; }

    /// <summary>Shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Shop profile name.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Source product URL (for icon / open source).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Local-only: <c>recommended</c> (extension Search) or <c>manual</c> (PDP add). Null = unknown/legacy.
    /// </summary>
    public string? AddedFrom { get; set; }

    /// <summary>Source review count (Shein comment_num). Local-only.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Source average rating (Shein comment_rank_average). Local-only.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Supplier shop logo (Shein storeLogo).</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Supplier shop title.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Canonical supplier shop URL.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Shein shopRank.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Shein storeAttention (followers).</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Raw soldByTips — fill StoreTitle when missing; not serialized.</summary>
    [JsonIgnore]
    public string? SoldByTips { get; set; }

    /// <summary>Supplier shop shipping SLA days.</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Supplier shop % delivered within SLA.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Shein warehouse stock (not 4Seller availableStock).</summary>
    public int? SourceStock { get; set; }

    /// <summary>Local US stock (Shein localMadeTips / isLocalProduct).</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>At least one SKU mall_stock supports quick ship.</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Supplier platform code (SHEIN / TEMU).</summary>
    public string? SellerPlatform { get; set; }

    /// <summary>Supplier store code.</summary>
    public string? StoreCode { get; set; }

    /// <summary>
    /// True when <c>SupplierProductInfos.SizeChartImageUrl</c> is non-empty.
    /// False → auto publish pick skips this draft (same gate as queue badge).
    /// </summary>
    public bool HasSizeChart { get; set; }

    /// <summary>Local create time (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>First few variations for card.</summary>
    public List<ListingVariationDto> Variations { get; set; } = [];
}

/// <summary>Full listing detail (entity columns + fields hydrated from RawPayload).</summary>
public class ListingDetailDto : ListingListItemDto
{
    /// <summary>Category id.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Warehouse id.</summary>
    public string? WarehouseId { get; set; }

    /// <summary>Description HTML.</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>4Seller searchTerms (SEO keywords).</summary>
    public List<string>? SearchTerms { get; set; }

    /// <summary>4Seller productHighlights (short selling points).</summary>
    public List<string>? ProductHighlights { get; set; }

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

    /// <summary>Create time.</summary>
    public DateTime? PlatformCreateTime { get; set; }

    /// <summary>Whether RawPayload is present (needed for push).</summary>
    public bool HasRawPayload { get; set; }

    /// <summary>Brand id (from RawPayload).</summary>
    public string? BrandId { get; set; }

    /// <summary>Brand name.</summary>
    public string? BrandName { get; set; }

    /// <summary>Product type SingleSku / MultiSku.</summary>
    public string? ProductType { get; set; }

    /// <summary>Size chart id.</summary>
    public string? SizeChartId { get; set; }

    /// <summary>
    /// Normalized Shein size guide from supplier snapshot (cm rows, US size, measure hints).
    /// </summary>
    public System.Text.Json.JsonElement? SizeGuideJson { get; set; }

    /// <summary>
    /// Bunny CDN URL of user-captured size chart from extension (staging before 4Seller COS apply).
    /// </summary>
    public string? SizeChartCaptureUrl { get; set; }

    /// <summary>4Seller product video material id (<c>erpVideoId</c>).</summary>
    public string? ErpVideoId { get; set; }

    /// <summary>Safety warning.</summary>
    public string? SafetyWarning { get; set; }

    /// <summary>4Seller site id.</summary>
    public string? SiteId { get; set; }

    /// <summary>Listing source URL (4Seller set).</summary>
    public string? ListingSourceUrl { get; set; }

    /// <summary>4Seller group id.</summary>
    public string? FourSellerGroupId { get; set; }

    /// <summary>4Seller group name.</summary>
    public string? FourSellerGroupName { get; set; }

    /// <summary>Applied rules JSON.</summary>
    public string? AppliedRules { get; set; }

    /// <summary>
    /// Last applied batch pricing formula (local-only). Used to restore calculator / preview
    /// on edit; not for sales or profit reporting.
    /// </summary>
    public ListingAppliedPricingDto? AppliedPricing { get; set; }

    /// <summary>Local-only: AI Rewrite đã áp dụng (Publish checklist).</summary>
    public bool DescriptionRewritten { get; set; }

    /// <summary>Local-only: Insert Images đã chạy sau Rewrite (tùy chọn, không chặn Publish).</summary>
    public bool DescriptionImagesInserted { get; set; }

    /// <summary>Reference listing id.</summary>
    public string? RefListingId { get; set; }

    /// <summary>Reference shop id.</summary>
    public string? RefShopId { get; set; }

    /// <summary>Images / videos hydrated from RawPayload.</summary>
    public List<ListingImageDto> Images { get; set; } = [];

    /// <summary>Category attributes (specifics).</summary>
    public List<ListingAttributeDto> Attributes { get; set; } = [];

    /// <summary>Certifications.</summary>
    public List<ListingCertificationDto> Certifications { get; set; } = [];
}

/// <summary>Variation DTO.</summary>
public class ListingVariationDto
{
    /// <summary>Local id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller sku id.</summary>
    public long? FourSellerSkuId { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Title.</summary>
    public string? Title { get; set; }

    /// <summary>Image.</summary>
    public string? MainImage { get; set; }

    /// <summary>Sale price.</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>
    /// First-seen unit price (set once on sync; not overwritten when sale price changes).
    /// </summary>
    public decimal? InitialPrice { get; set; }

    /// <summary>Original / list price.</summary>
    public decimal? OriginalPrice { get; set; }

    /// <summary>Global price.</summary>
    public decimal? GlobalPrice { get; set; }

    /// <summary>Quantity.</summary>
    public int? Quantity { get; set; }

    /// <summary>Barcode.</summary>
    public string? Barcode { get; set; }

    /// <summary>Variant attributes JSON.</summary>
    public string? VariantAttributes { get; set; }

    /// <summary>Sort order.</summary>
    public int SortOrder { get; set; }

    /// <summary>Is default SKU.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>Listing media item.</summary>
public class ListingImageDto
{
    /// <summary>Media type.</summary>
    public string MediaType { get; set; } = "Main";

    /// <summary>URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Linked local variation id when color-bound.</summary>
    public Guid? ListingSkuId { get; set; }

    /// <summary>
    /// Color axis for Variant Image (from skuImgs key / custom_value).
    /// Survives when ListingSkuId cannot be resolved to a variation.
    /// </summary>
    public string? ColorValue { get; set; }

    /// <summary>TikTok image id.</summary>
    public string? TikTokImageId { get; set; }

    /// <summary>Sort order.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Category attribute (specifics).</summary>
public class ListingAttributeDto
{
    /// <summary>Attribute id.</summary>
    public string AttrId { get; set; } = string.Empty;

    /// <summary>Attribute name.</summary>
    public string? AttrName { get; set; }

    /// <summary>Attribute value (single or comma-joined).</summary>
    public string? AttrValue { get; set; }

    /// <summary>TikTok value id when known (enum option); first id when multi.</summary>
    public string? ValueId { get; set; }

    /// <summary>Multi-select values when <c>productAttributes.values[]</c> has more than one entry.</summary>
    public List<ListingAttributeValueDto>? Values { get; set; }

    /// <summary>Value type.</summary>
    public string? ValueType { get; set; }

    /// <summary>Is required.</summary>
    public bool IsRequired { get; set; }
}

/// <summary>One value under a Specifics attribute.</summary>
public class ListingAttributeValueDto
{
    /// <summary>TikTok value id.</summary>
    public string? ValueId { get; set; }

    /// <summary>Display value name.</summary>
    public string? ValueName { get; set; }
}

/// <summary>Certification row.</summary>
public class ListingCertificationDto
{
    /// <summary>Certification type.</summary>
    public string CertType { get; set; } = string.Empty;

    /// <summary>External id.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Name / value.</summary>
    public string? Name { get; set; }

    /// <summary>Phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Email.</summary>
    public string? Email { get; set; }

    /// <summary>Address.</summary>
    public string? Address { get; set; }

    /// <summary>File URLs JSON.</summary>
    public string? FileUrls { get; set; }
}
