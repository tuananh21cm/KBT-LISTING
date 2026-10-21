namespace Application.DTOs.Listing;

/// <summary>Update local listing fields before save-draft / publish (mirrors ProductEdit surface).</summary>
public class UpdateListingMirrorDto
{
    /// <summary>Product name.</summary>
    public string? ProductName { get; set; }

    /// <summary>Description HTML.</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>
    /// 4Seller searchTerms. Null = leave unchanged; empty list = clear remote placeholders.
    /// </summary>
    public List<string>? SearchTerms { get; set; }

    /// <summary>
    /// 4Seller productHighlights. Null = leave unchanged; empty list = clear remote placeholders.
    /// </summary>
    public List<string>? ProductHighlights { get; set; }

    /// <summary>Category ID.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Category name.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Brand ID.</summary>
    public string? BrandId { get; set; }

    /// <summary>Brand name.</summary>
    public string? BrandName { get; set; }

    /// <summary>Product type (SingleSku / MultiSku).</summary>
    public string? ProductType { get; set; }

    /// <summary>Warehouse ID.</summary>
    public string? WarehouseId { get; set; }

    /// <summary>Source URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Listing-level source URL (4Seller set).</summary>
    public string? ListingSourceUrl { get; set; }

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

    /// <summary>Size chart ID.</summary>
    public string? SizeChartId { get; set; }

    /// <summary>4Seller product video material id (<c>erpVideoId</c>).</summary>
    public string? ErpVideoId { get; set; }

    /// <summary>Safety warning.</summary>
    public string? SafetyWarning { get; set; }

    /// <summary>4Seller site ID.</summary>
    public string? SiteId { get; set; }

    /// <summary>4Seller group ID.</summary>
    public string? FourSellerGroupId { get; set; }

    /// <summary>4Seller group name.</summary>
    public string? FourSellerGroupName { get; set; }

    /// <summary>Applied rules JSON.</summary>
    public string? AppliedRules { get; set; }

    /// <summary>
    /// Batch pricing formula snapshot to persist on the listing (local jsonb).
    /// Omitted / null = leave existing value unchanged.
    /// </summary>
    public ListingAppliedPricingDto? AppliedPricing { get; set; }

    /// <summary>
    /// Description prep: AI Rewrite applied. Null = leave unchanged.
    /// </summary>
    public bool? DescriptionRewritten { get; set; }

    /// <summary>
    /// Description prep: Insert Images after Rewrite (optional). Null = leave unchanged.
    /// </summary>
    public bool? DescriptionImagesInserted { get; set; }

    /// <summary>Reference listing ID.</summary>
    public string? RefListingId { get; set; }

    /// <summary>Reference shop ID.</summary>
    public string? RefShopId { get; set; }

    /// <summary>
    /// Local shop Guid for Publish (4Seller). When set, remaps <c>Listing.ShopId</c>
    /// + <c>FourSellerShopId</c> and patches RawPayload <c>shopId</c> / <c>currentShopId</c>.
    /// </summary>
    public Guid? ShopId { get; set; }

    /// <summary>Main image URL (first / joined).</summary>
    public string? MainImage { get; set; }

    /// <summary>Variation updates (full replace when provided).</summary>
    public List<UpdateListingVariationDto>? Variations { get; set; }

    /// <summary>Images and videos.</summary>
    public List<UpdateListingMirrorImageDto>? Images { get; set; }

    /// <summary>Category attributes (specifics).</summary>
    public List<UpdateListingMirrorAttributeDto>? Attributes { get; set; }

    /// <summary>Certifications.</summary>
    public List<UpdateListingMirrorCertificationDto>? Certifications { get; set; }
}

/// <summary>Per-variation update (id optional for new SKU).</summary>
public class UpdateListingVariationDto
{
    /// <summary>Local variation id (null for new).</summary>
    public Guid? Id { get; set; }

    /// <summary>4Seller sku id when known.</summary>
    public long? FourSellerSkuId { get; set; }

    /// <summary>Sale price.</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>
    /// Cost / first-seen unit price (InitialPrice). When set, updates local InitialPrice
    /// (listing edit "Giá nhập" / draft cost — not copied onto orders).
    /// </summary>
    public decimal? InitialPrice { get; set; }

    /// <summary>Original price.</summary>
    public decimal? OriginalPrice { get; set; }

    /// <summary>Quantity.</summary>
    public int? Quantity { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Variant title.</summary>
    public string? Title { get; set; }

    /// <summary>Main image.</summary>
    public string? MainImage { get; set; }

    /// <summary>Barcode.</summary>
    public string? Barcode { get; set; }

    /// <summary>Variant attributes JSON.</summary>
    public string? VariantAttributes { get; set; }

    /// <summary>Sort order.</summary>
    public int SortOrder { get; set; }

    /// <summary>Is default SKU.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>Mirror image update.</summary>
public class UpdateListingMirrorImageDto
{
    /// <summary>Media type (Main / Detail / Video / SizeChart).</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Linked local variation id.</summary>
    public Guid? ListingSkuId { get; set; }

    /// <summary>TikTok image id.</summary>
    public string? TikTokImageId { get; set; }

    /// <summary>Sort order.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Mirror attribute update.</summary>
public class UpdateListingMirrorAttributeDto
{
    /// <summary>Attribute id.</summary>
    public string AttrId { get; set; } = string.Empty;

    /// <summary>Attribute name.</summary>
    public string? AttrName { get; set; }

    /// <summary>Attribute value (single or comma-joined when <see cref="Values"/> has multiple).</summary>
    public string? AttrValue { get; set; }

    /// <summary>TikTok value id when known (enum option); first id when multi.</summary>
    public string? ValueId { get; set; }

    /// <summary>
    /// Multi-select values (<c>isMultipleSelected=1</c>). When present and non-empty, push emits
    /// all entries under one <c>productAttributes</c> row <c>values[]</c>.
    /// </summary>
    public List<UpdateListingMirrorAttributeValueDto>? Values { get; set; }

    /// <summary>Value type.</summary>
    public string? ValueType { get; set; }

    /// <summary>Is required.</summary>
    public bool IsRequired { get; set; }
}

/// <summary>One value under a multi-select Specifics attribute.</summary>
public class UpdateListingMirrorAttributeValueDto
{
    /// <summary>TikTok value id (empty for custom free-text).</summary>
    public string? ValueId { get; set; }

    /// <summary>Display value name.</summary>
    public string? ValueName { get; set; }
}

/// <summary>Mirror certification update.</summary>
public class UpdateListingMirrorCertificationDto
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
