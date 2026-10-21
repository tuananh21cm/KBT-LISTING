namespace Application.DTOs.Listing;

/// <summary>Query for local listing list.</summary>
public class ListingListQuery
{
    /// <summary>Page (1-based).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary><c>draft</c> or <c>published</c>.</summary>
    public string? PageType { get; set; }

    /// <summary>Site status filter (active, inactive, …).</summary>
    public string? SiteStatus { get; set; }

    /// <summary>Seller owner filter.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Platform code (tiktok).</summary>
    public string? Platform { get; set; }

    /// <summary>Local shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>
    /// Free-text search across listing title, SKUs, ids (4Seller / TikTok / local / shop / SKU),
    /// category, source URL, seller, TikTok shop profile, supplier store.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Inclusive lower bound for listing time
    /// (<c>PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt</c>).
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>
    /// Inclusive upper bound for listing time
    /// (<c>PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt</c>).
    /// </summary>
    public DateTime? DateTo { get; set; }
}
