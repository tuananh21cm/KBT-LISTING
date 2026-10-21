namespace Application.DTOs.FourSeller;

/// <summary>
/// Extension / seller request to push product-page HTML into 4Seller store drafts
/// (<c>POST /api/crawl/postHtml-file-shop</c>).
/// </summary>
public class FourSellerPostHtmlRequest
{
    /// <summary>Product page URL on the source site.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Full HTML of the product page (captured in the browser).</summary>
    public string Html { get; set; } = string.Empty;

    /// <summary>4Seller internal shop ids (<c>shopIdList</c>).</summary>
    public List<long> ShopIdList { get; set; } = [];

    /// <summary>Primary marketplace site / region (e.g. US).</summary>
    public string Site { get; set; } = string.Empty;

    /// <summary>Marketplace sites for multi-shop push.</summary>
    public List<string> SiteList { get; set; } = [];

    /// <summary>Optional Shein <c>comment_num</c> — local Listing only, not forwarded to 4Seller.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Optional Shein <c>comment_rank_average</c> — local Listing only, not forwarded to 4Seller.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>ProductSource.Code (SHEIN / TEMU / …). Local-only.</summary>
    public string? Platform { get; set; }

    /// <summary>Shein <c>productInfo.stock</c>. Local-only.</summary>
    public int? Stock { get; set; }

    /// <summary>Shein SLA days from <c>dayPercentDesc</c> (fallback <c>shippingDays</c>). Local-only.</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Parsed % from <c>dayPercentDesc</c> / <c>dayPercents</c>. Local-only.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Raw <c>dayPercentDesc</c> HTML. Local-only. Not <c>shippingDayPercentDesc</c> (sàn-wide).</summary>
    public string? ShippingDayPercentDesc { get; set; }

    /// <summary>Shein <c>storeCode</c>. Local-only.</summary>
    public string? StoreCode { get; set; }

    /// <summary>Shop logo. Local-only.</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Shop title. Local-only.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Shein <c>soldByTips</c>. Local-only.</summary>
    public string? SoldByTips { get; set; }

    /// <summary>Shein <c>shipsFromTips</c>. Local-only.</summary>
    public string? ShipsFromTips { get; set; }

    /// <summary>Shein <c>shopRank</c>. Local-only.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Shein <c>storeAttention</c> (followers). Local-only.</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Optional store URL. Local-only; backend may build canonical Shein URL.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Shein Local US (<c>localMadeTips</c> / <c>isLocalProduct</c>). Local-only.</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Shein mall_stock quick ship (product-level). Local-only.</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>
    /// Local-only listing origin: <c>recommended</c> (extension Search) or <c>manual</c> (PDP add).
    /// Not forwarded to 4Seller. Omitted = do not stage origin.
    /// </summary>
    public string? AddedFrom { get; set; }

    /// <summary>
    /// Normalized Shein size guide (cm rows + US size + measure hints). Local-only, not forwarded to 4Seller.
    /// </summary>
    public System.Text.Json.JsonElement? SizeGuideJson { get; set; }
}
