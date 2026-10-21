namespace Application.DTOs.FourSeller;

/// <summary>
/// Request body for 4Seller listing page (proxy filters).
/// </summary>
public class FourSellerListingPageRequest
{
    /// <summary>Current page (1-based).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (max 100).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Listing status filter (e.g. draft, online).</summary>
    public string Status { get; set; } = "draft";

    /// <summary>4Seller internal shop id (empty = all shops).</summary>
    public string ShopId { get; set; } = string.Empty;

    /// <summary>Search type (msku, title, …).</summary>
    public string SearchType { get; set; } = "msku";

    /// <summary>Search values.</summary>
    public List<string> SearchValue { get; set; } = [];

    /// <summary>Search method (exact / fuzzy).</summary>
    public string SearchMethod { get; set; } = "exact";

    /// <summary>Sort field.</summary>
    public string OrderBy { get; set; } = "update_time";

    /// <summary>Sort direction.</summary>
    public string Desc { get; set; } = "desc";

    /// <summary>
    /// Optional inclusive lower bound (UTC). When set with <see cref="DateTo"/>,
    /// fills 4Seller <c>createTime</c> / <c>createTimeStart</c> / <c>createTimeEnd</c>
    /// as America/Los_Angeles wall-clock (same absolute window).
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>Optional inclusive upper bound (UTC).</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>
    /// Optional inclusive lower bound for 4Seller <c>updateTimeStart</c>, used to pull only rows the
    /// platform changed since the last successful sync.
    /// <para>
    /// Sent as naive digits with no timezone conversion, i.e. the same convention 4Seller uses in the
    /// <c>updateTime</c> it returns. This deliberately differs from <see cref="DateFrom"/>, which the
    /// create-time filter expects as Pacific wall-clock — the two filters were verified separately.
    /// </para>
    /// </summary>
    public DateTime? UpdatedFrom { get; set; }

    /// <summary>Optional inclusive upper bound paired with <see cref="UpdatedFrom"/>.</summary>
    public DateTime? UpdatedTo { get; set; }

    /// <summary>
    /// Optional explicit 4Seller listing ids. Lets a caller ask about specific rows instead of paging a
    /// whole status bucket, which is the only affordable way to re-check a listing that is buried deep
    /// in a bucket sorted by update time.
    /// </summary>
    public List<string> ListingIds { get; set; } = [];
}
