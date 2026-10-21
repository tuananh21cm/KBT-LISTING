namespace Application.DTOs.Order;

/// <summary>
/// Query filters for local order list.
/// </summary>
public class OrderListQuery
{
    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Filter by composite workflow tab (PENDING_PROCESS, PURCHASED, AWAITING_COLLECTION_GROUP,
    /// IN_TRANSIT, DELIVERED, CANCELLED, REFUND), legacy union IN_TRANSIT_GROUP, or raw platformOrderStatus.
    /// </summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>Alias for <see cref="PlatformOrderStatus"/> workflow / raw tab key.</summary>
    public string? StatusTab
    {
        get => PlatformOrderStatus;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                PlatformOrderStatus = value;
            }
        }
    }

    /// <summary>Filter by seller owner.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Filter by platform code (e.g. TikTok) — matched case-insensitively to order.Platform.</summary>
    public string? Platform { get; set; }

    /// <summary>Filter by local shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>
    /// Search platform order id / shipment / tracking / supplier order no /
    /// buyer name·phone·email·address (city / state / postal) / fourSellerOrderId.
    /// When set, list ignores <see cref="DateFrom"/> / <see cref="DateTo"/> only.
    /// <see cref="PlatformOrderStatus"/> and <see cref="PendingFilter"/> still apply when sent
    /// (queue pages search within the active tab; main <c>/orders</c> omits them for global lookup).
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Inclusive lower bound for order time (OrderPaidTime ?? OrderCreateTime ?? CreatedAt).
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>
    /// Inclusive upper bound for order time (OrderPaidTime ?? OrderCreateTime ?? CreatedAt).
    /// </summary>
    public DateTime? DateTo { get; set; }

    /// <summary>
    /// When true, only orders not yet marked purchased locally (<c>FulfilPurchasedAt</c> is null).
    /// Ignored when <see cref="PurchasedToday"/> is true.
    /// </summary>
    public bool? NeedsPurchase { get; set; }

    /// <summary>
    /// When true, only orders whose <c>FulfilPurchasedAt</c> falls in today's Vietnam calendar day.
    /// Takes precedence over <see cref="NeedsPurchase"/>. Used by extension KINHBAC FULFIL tab «Đã mua».
    /// </summary>
    public bool? PurchasedToday { get; set; }

    /// <summary>
    /// Filter by the fulfil user who purchased (<c>Order.PaidByUserId</c>).
    /// Extension tab «Đã mua»: non-Admin is forced to self on the controller; Admin may omit (all) or set explicitly.
    /// </summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>
    /// When set, filter by customer paid-on-platform flag (<c>IsCustomerPaid</c>).
    /// Extension KINHBAC FULFIL gửi <c>true</c> cùng <c>needsPurchase</c>.
    /// </summary>
    public bool? IsCustomerPaid { get; set; }

    /// <summary>
    /// Filter orders that have at least one line item whose <c>SourceUrl</c> host matches
    /// the domains of this product source (SHEIN / Temu / …).
    /// </summary>
    public Guid? ProductSourceId { get; set; }

    /// <summary>
    /// Sort direction. <c>asc</c> = sớm nhất trước; mặc định / khác = mới nhất trước.
    /// Default key = <c>OrderPaidTime ?? OrderCreateTime ?? CreatedAt</c>.
    /// When <see cref="PurchasedToday"/> or workflow tab <c>PURCHASED</c>: key = <c>FulfilPurchasedAt</c>.
    /// </summary>
    public string? SortDir { get; set; }

    /// <summary>
    /// Keyset cursor from the previous page's <c>nextCursor</c>. When set, the list seeks straight
    /// to the next row instead of paying for <c>OFFSET</c>, and <see cref="PageCurrent"/> is ignored.
    ///
    /// Only valid while the sort and the filters stay the same; changing either invalidates it.
    /// Ignored when sorting on <c>FulfilPurchasedAt</c> (<see cref="PurchasedToday"/> or tab
    /// <c>PURCHASED</c>). Leave it empty to keep classic page-number paging.
    /// </summary>
    public string? Cursor { get; set; }

    /// <summary>
    /// Sub-filter for <c>/orders/pending</c> queue (on top of <c>PENDING_PROCESS</c>):
    /// <c>new</c> (đơn trong ngày VN) / <c>out_stock</c> (<c>IssueStatus=Processing</c>) /
    /// <c>24h</c> (tuổi 0–&lt;24h) / <c>48h</c> (tuổi 24–&lt;48h) / <c>over_48h</c> (tuổi ≥48h).
    /// Blank = tất cả chờ mua.
    /// </summary>
    public string? PendingFilter { get; set; }
}
