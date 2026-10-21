namespace Application.DTOs.Supplier;

/// <summary>Local-only PDP metrics from the browser extension (not forwarded to 4Seller).</summary>
public class SupplierProductInfoUpsertRequest
{
    /// <summary>Source product URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>ProductSource.Code (SHEIN, TEMU, …).</summary>
    public string? Platform { get; set; }

    /// <summary>Shein comment_num.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Shein comment_rank_average.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Shein productInfo.stock.</summary>
    public int? Stock { get; set; }

    /// <summary>SLA days from dayPercentDesc (fallback shippingDays).</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Parsed percent from dayPercentDesc / dayPercents.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Raw dayPercentDesc HTML (not sàn-wide shippingDayPercentDesc).</summary>
    public string? ShippingDayPercentDesc { get; set; }

    /// <summary>Shein storeCode.</summary>
    public string? StoreCode { get; set; }

    /// <summary>Shop logo URL.</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Shop title.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>soldByTips.</summary>
    public string? SoldByTips { get; set; }

    /// <summary>shipsFromTips.</summary>
    public string? ShipsFromTips { get; set; }

    /// <summary>Shein shopRank.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Shein storeAttention (followers).</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Optional store URL; backend builds canonical Shein URL when missing.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Local US stock flag.</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Mall-stock quick ship flag (product-level).</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Normalized Shein size guide JSON from extension (local-only).</summary>
    public System.Text.Json.JsonElement? SizeGuideJson { get; set; }
}
