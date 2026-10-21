namespace Application.DTOs.Listing;

/// <summary>
/// Dry-run of the 4Seller push body: RawPayload patched + sale-prop maps rebuilt + sanitized,
/// but nothing sent and nothing saved. Used to verify that a Color/Size edit on the form ends up
/// in the payload before trusting Save Draft / Publish.
/// </summary>
public class ListingPushPreviewDto
{
    /// <summary>Local listing id.</summary>
    public Guid ListingId { get; set; }

    /// <summary>4Seller listing id in the payload.</summary>
    public long FourSellerListingId { get; set; }

    /// <summary>Axis name → <c>option{N}</c> slot resolved for this payload.</summary>
    public Dictionary<string, int> AxisSlots { get; set; } = [];

    /// <summary>Root sale-prop maps as they would be sent (<c>option1</c>, <c>option1Value</c>, …).</summary>
    public Dictionary<string, string?> SaleProps { get; set; } = [];

    /// <summary>One row per SKU in the payload, in push order.</summary>
    public List<ListingPushPreviewVariationDto> Variations { get; set; } = [];

    /// <summary>Local variation rows that produced the payload.</summary>
    public List<ListingPushPreviewLocalVariationDto> LocalVariations { get; set; } = [];

    /// <summary>Errors that would block the push.</summary>
    public List<string> ValidationErrors { get; set; } = [];

    /// <summary><c>skuImgs</c> as it would be sent (JSON string of Color rows).</summary>
    public string? SkuImgs { get; set; }

    /// <summary>Full payload JSON — only when explicitly requested (it is large).</summary>
    public string? Payload { get; set; }
}

/// <summary>One <c>variation[]</c> row of the dry-run payload.</summary>
public class ListingPushPreviewVariationDto
{
    /// <summary>Zero-based position in <c>variation[]</c>.</summary>
    public int Index { get; set; }

    /// <summary>TikTok sku id, null for a SKU 4Seller has to create.</summary>
    public long? SkuId { get; set; }

    /// <summary>Seller SKU (MSKU).</summary>
    public string? SellerSku { get; set; }

    /// <summary>Variant title.</summary>
    public string? Title { get; set; }

    /// <summary>Sale-prop label in slot 1.</summary>
    public string? Option1 { get; set; }

    /// <summary>Sale-prop label in slot 2.</summary>
    public string? Option2 { get; set; }

    /// <summary>Sale-prop label in slot 3.</summary>
    public string? Option3 { get; set; }

    /// <summary><c>attrs</c> array — must mirror the option labels above.</summary>
    public List<string> Attrs { get; set; } = [];

    /// <summary>Price field 4Seller reads (<c>originalPrice</c>).</summary>
    public decimal? OriginalPrice { get; set; }

    /// <summary>Stock resolved from the row.</summary>
    public decimal? AvailableStock { get; set; }

    /// <summary>First variant image URL.</summary>
    public string? MainImage { get; set; }
}

/// <summary>Local <c>ListingVariations</c> row for side-by-side comparison with the payload.</summary>
public class ListingPushPreviewLocalVariationDto
{
    /// <summary>Local row id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller sku id bound to this row.</summary>
    public long? FourSellerSkuId { get; set; }

    /// <summary>Seller SKU (MSKU).</summary>
    public string? SellerSku { get; set; }

    /// <summary>Variant title.</summary>
    public string? Title { get; set; }

    /// <summary>Structured combo JSON stored locally.</summary>
    public string? VariantAttributes { get; set; }

    /// <summary>Sale price.</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>Stock.</summary>
    public int? Quantity { get; set; }
}
