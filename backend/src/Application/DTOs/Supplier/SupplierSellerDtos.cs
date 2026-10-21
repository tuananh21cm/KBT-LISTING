namespace Application.DTOs.Supplier;

/// <summary>Paged supplier shop list with period KPI columns.</summary>
public class SupplierSellerListPageDto
{
    /// <summary>Rows.</summary>
    public List<SupplierSellerListItemDto> Items { get; set; } = [];

    /// <summary>Total matching filters.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Resolved inclusive VN start (yyyy-MM-dd).</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Resolved inclusive VN end (yyyy-MM-dd).</summary>
    public string ToDate { get; set; } = string.Empty;
}

/// <summary>Supplier shop row + scoped counts / item GMV in the KPI window.</summary>
public class SupplierSellerListItemDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>ProductSource code.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Source store code.</summary>
    public string StoreCode { get; set; } = string.Empty;

    /// <summary>Store home URL.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Logo URL.</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Display title (fallback from soldByTips).</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Shein shopRank.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Follow count.</summary>
    public int? StoreAttention { get; set; }

    /// <summary>SLA days.</summary>
    public int? ShippingDays { get; set; }

    /// <summary>SLA hit percent.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Ships-from tip.</summary>
    public string? ShipsFromTips { get; set; }

    /// <summary>Linked supplier products in scope.</summary>
    public int ProductCount { get; set; }

    /// <summary>Linked listings in scope.</summary>
    public int ListingCount { get; set; }

    /// <summary>Distinct money-eligible orders with ≥1 matched line.</summary>
    public int OrderCount { get; set; }

    /// <summary>Sum of matched line quantities.</summary>
    public int ItemQty { get; set; }

    /// <summary>Σ ItemPrice × Quantity of matched lines.</summary>
    public decimal ItemRevenue { get; set; }
}

/// <summary>Supplier shop detail with KPI + paged source products.</summary>
public class SupplierSellerDetailDto : SupplierSellerListItemDto
{
    /// <summary>Resolved inclusive VN start (yyyy-MM-dd).</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Resolved inclusive VN end (yyyy-MM-dd).</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>Paged source products.</summary>
    public List<SupplierSellerProductDto> Products { get; set; } = [];

    /// <summary>Total source products in scope.</summary>
    public int ProductTotal { get; set; }

    /// <summary>Product page.</summary>
    public int ProductPageCurrent { get; set; }

    /// <summary>Product page size.</summary>
    public int ProductPageSize { get; set; }
}

/// <summary>Source product snapshot under a supplier shop.</summary>
public class SupplierSellerProductDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Original source URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Stable source key.</summary>
    public string SourceUrlKey { get; set; } = string.Empty;

    /// <summary>Source stock.</summary>
    public int? Stock { get; set; }

    /// <summary>Review count.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Average rating.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Local US stock flag.</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Quick-ship flag.</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Linked listings in scope.</summary>
    public int ListingCount { get; set; }

    /// <summary>Listings in scope (slim).</summary>
    public List<SupplierSellerListingDto> Listings { get; set; } = [];
}

/// <summary>Slim listing linked to a source product.</summary>
public class SupplierSellerListingDto
{
    /// <summary>Listing id.</summary>
    public Guid Id { get; set; }

    /// <summary>Title.</summary>
    public string? ProductName { get; set; }

    /// <summary>draft / published.</summary>
    public string PageType { get; set; } = string.Empty;

    /// <summary>Site status.</summary>
    public string? SiteStatus { get; set; }

    /// <summary>Main image (may be pipe-separated).</summary>
    public string? MainImage { get; set; }

    /// <summary>TikTok shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Admin only — listing owner display name.</summary>
    public string? OwnerName { get; set; }
}
