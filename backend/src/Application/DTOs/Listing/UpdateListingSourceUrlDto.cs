namespace Application.DTOs.Listing;

/// <summary>
/// Lightweight local-only persist of the supplier source URL (no 4Seller push).
/// </summary>
public class UpdateListingSourceUrlDto
{
    /// <summary>Supplier product URL (SHEIN / Temu / …).</summary>
    public string SourceUrl { get; set; } = string.Empty;
}

/// <summary>Result of a source-url local save (enough to patch the list card).</summary>
public class ListingSourceUrlResultDto
{
    /// <summary>Listing id.</summary>
    public Guid ListingId { get; set; }

    /// <summary>Canonical source URL stored on the listing.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Source review count when a supplier snapshot was linked.</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Source average rating when a supplier snapshot was linked.</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Supplier shop logo.</summary>
    public string? StoreLogo { get; set; }

    /// <summary>Supplier shop title.</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Supplier shop URL.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Supplier shop rank.</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Supplier shop follower count.</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Supplier shop SLA days.</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Supplier shop % delivered within SLA.</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Supplier warehouse stock.</summary>
    public int? SourceStock { get; set; }

    /// <summary>Local US stock flag.</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Quick-ship flag.</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Supplier platform code.</summary>
    public string? SellerPlatform { get; set; }

    /// <summary>Supplier store code.</summary>
    public string? StoreCode { get; set; }

    /// <summary>True when linked supplier snapshot has a non-empty SizeChartImageUrl.</summary>
    public bool HasSizeChart { get; set; }
}
