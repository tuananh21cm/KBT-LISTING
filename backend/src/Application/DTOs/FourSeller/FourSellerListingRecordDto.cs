namespace Application.DTOs.FourSeller;

/// <summary>
/// One listing row from 4Seller listing page (and merged detail fields when available).
/// </summary>
public class FourSellerListingRecordDto
{
    /// <summary>4Seller listing id.</summary>
    public long Id { get; set; }

    /// <summary>Product title.</summary>
    public string? ProductName { get; set; }

    /// <summary>Pipe-separated main image URLs.</summary>
    public string? MainImage { get; set; }

    /// <summary>4Seller shop id.</summary>
    public long? ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Site / marketplace (US, …).</summary>
    public string? Site { get; set; }

    /// <summary>Currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>TikTok category external id.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Category display name when present.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Lowest variant price.</summary>
    public decimal? LowPrice { get; set; }

    /// <summary>Highest variant price.</summary>
    public decimal? HighPrice { get; set; }

    /// <summary>Total available stock.</summary>
    public int? AvailableStock { get; set; }

    /// <summary>Variation count.</summary>
    public int VariationCount { get; set; }

    /// <summary>ERP / listing status (draft, active, remove, …).</summary>
    public string? ErpStatus { get; set; }

    /// <summary>
    /// TikTok product status code from 4Seller page (<c>productStatus</c>):
    /// 1 Draft, 2 Reviewing, 3 Failure, 4 Active, 6 Inactive, 8 Remove.
    /// </summary>
    public int? ProductStatus { get; set; }

    /// <summary>Publish status.</summary>
    public string? PublishStatus { get; set; }

    /// <summary>
    /// Publish / validation failure text from listing page (<c>failedMessage</c>).
    /// Shown on draft rows as a warning (e.g. 【Publish Failed】…).
    /// </summary>
    public string? FailedMessage { get; set; }

    /// <summary>
    /// Marketplace product id from 4Seller payload field <c>productId</c>
    /// (TikTok product id when published). Stored locally as <c>Listing.TikTokProductId</c>.
    /// </summary>
    public string? MarketplaceProductId { get; set; }

    /// <summary>Source product URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Warehouse id.</summary>
    public string? WarehouseId { get; set; }

    /// <summary>Create time string from 4Seller.</summary>
    public string? CreateTime { get; set; }

    /// <summary>Update time string from 4Seller.</summary>
    public string? UpdateTime { get; set; }

    /// <summary>HTML description (from detail when available).</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>Package weight from detail.</summary>
    public decimal? PackageWeight { get; set; }

    /// <summary>Package length from detail.</summary>
    public decimal? PackageLength { get; set; }

    /// <summary>Package width from detail.</summary>
    public decimal? PackageWidth { get; set; }

    /// <summary>Package height from detail.</summary>
    public decimal? PackageHeight { get; set; }

    /// <summary>Weight unit.</summary>
    public string? WeightUnit { get; set; }

    /// <summary>Dimension unit.</summary>
    public string? DimensionUnit { get; set; }

    /// <summary>Variant rows.</summary>
    public List<FourSellerListingVariationDto> VariationList { get; set; } = [];

    /// <summary>Extra image URLs from detail (beyond mainImage).</summary>
    public List<string> DetailImageUrls { get; set; } = [];

    /// <summary>
    /// Variant images keyed by color (from salePropImageMap / color-tagged imageList items).
    /// </summary>
    public Dictionary<string, List<string>> ColorImageUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw detail JSON snapshot when fetched.</summary>
    public string? RawDetailJson { get; set; }
}
