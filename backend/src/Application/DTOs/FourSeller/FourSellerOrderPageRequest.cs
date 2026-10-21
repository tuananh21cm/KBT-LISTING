namespace Application.DTOs.FourSeller;

/// <summary>
/// Request for 4Seller <c>POST /api/v2/order/page</c>.
/// </summary>
public class FourSellerOrderPageRequest
{
    /// <summary>Current page (1-based).</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (max 100).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Order status filter (e.g. to_ship, shipped).</summary>
    public string OrderStatus { get; set; } = "to_ship";

    /// <summary>Platform filter (empty = all).</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Search type (default orderNo).</summary>
    public string SearchType { get; set; } = "orderNo";

    /// <summary>Search method (exact / fuzzy).</summary>
    public string SearchMethod { get; set; } = "exact";

    /// <summary>Search value.</summary>
    public string SearchValue { get; set; } = string.Empty;

    /// <summary>Sort field.</summary>
    public string OrderBy { get; set; } = "orderPaidTime";

    /// <summary>Sort direction.</summary>
    public string Desc { get; set; } = "desc";

    /// <summary>
    /// Optional inclusive lower bound (UTC). When both bounds are set,
    /// fills all 4Seller <c>dateQuery*StartTime</c> / <c>dateQuery*EndTime</c> fields.
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>Optional inclusive upper bound (UTC).</summary>
    public DateTime? DateTo { get; set; }
}
