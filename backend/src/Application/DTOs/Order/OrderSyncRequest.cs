using System.Text.Json.Serialization;

namespace Application.DTOs.Order;

/// <summary>
/// Request body for order sync.
/// </summary>
public class OrderSyncRequest
{
    /// <summary>Optional: sync only this seller's cookie. Null = all sellers with cookie.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Order statuses to pull from 4Seller — one paginated query per entry.
    /// Empty/null defaults to the single <c>all</c> bucket, which returns every status mixed
    /// together. Chỉ truyền bucket cụ thể khi sync từ một tab hẹp của UI.
    /// </summary>
    public List<string>? OrderStatuses { get; set; }

    /// <summary>
    /// Optional inclusive lower bound (UTC, typically a Vietnam business-day start).
    /// When set with <see cref="DateTo"/>, forwarded to 4Seller
    /// <c>dateQueryCreateStartTime</c> / <c>dateQueryCreateEndTime</c> as
    /// America/Los_Angeles wall-clock (same absolute window; create-date only).
    /// </summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>
    /// Optional inclusive upper bound (UTC).
    /// </summary>
    public DateTime? DateTo { get; set; }

    /// <summary>
    /// When true with <see cref="DateFrom"/>/<see cref="DateTo"/> set: apply the create-date
    /// filter only to high-volume terminal buckets (<c>shipped</c>, <c>canceled</c>).
    /// Open buckets (<c>pending</c>, <c>on_hold</c>, <c>to_ship</c>, <c>in_process</c>) sync
    /// without a date filter so status / field updates on older open orders are not missed.
    /// Manual OrdersPage sync sets this when a date range is present. Background auto loops
    /// keep it <c>false</c> and rely on their lookback window instead (light new-orders loop
    /// must stay bounded; 4Seller filters by create-date, not update-date).
    /// Không có tác dụng với bucket <c>all</c> (mặc định): bucket đó trộn cả nhóm mở lẫn nhóm
    /// terminal nên bỏ lọc ngày sẽ kéo toàn bộ lịch sử đơn.
    /// </summary>
    public bool OpenStatusesIgnoreDateFilter { get; set; }

    /// <summary>
    /// Internal: cap how many 4Seller accounts run in parallel inside one <c>SyncAsync</c>.
    /// Null / ≤0 means "all at once". Background loops and the manual <c>OrderSyncJobRunner</c>
    /// set this from Settings (<c>fourseller.order_sync.new.batch_size</c>) so a large seller
    /// roster does not hammer 4Seller + the proxy pool.
    /// </summary>
    [JsonIgnore]
    public int? MaxParallelOwners { get; set; }

    /// <summary>
    /// Internal: skip the end-of-owner sweep that retries paid orders still missing pricing
    /// breakdown. Status loop sets this — it only refreshes <c>platformOrderStatus</c>; the
    /// light new-orders loop already runs the sweep every few minutes.
    /// </summary>
    [JsonIgnore]
    public bool SkipStalePricingSweep { get; set; }

    /// <summary>
    /// Internal: skip Telegram "new order" notifications for rows inserted this pass.
    /// Status loop sets this — new-order alerts belong to the light new-orders loop (+ pending sweep).
    /// </summary>
    [JsonIgnore]
    public bool SkipNewOrderTelegram { get; set; }
}
