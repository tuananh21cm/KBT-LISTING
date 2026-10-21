namespace Application.DTOs.Order;

/// <summary>
/// Extension auto-sync config for SHEIN orders (resolved from Settings).
/// </summary>
public class SheinSyncConfigDto
{
    /// <summary>Whether auto-sync is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Auto-sync interval in seconds (value+unit resolved).</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// <c>new</c> = chỉ khớp pool fulfil (awaiting/on_hold/collection);
    /// <c>all</c> = khớp/cập nhật mọi đơn không huỷ/hoàn trong cửa sổ Lookback (kể cả IN_TRANSIT/DELIVERED).
    /// </summary>
    public string UpdateMode { get; set; } = "new";

    /// <summary>
    /// Số luồng song song khi lấy chi tiết đơn SHEIN (get_order_info). Clamp 1–20.
    /// </summary>
    public int ConcurrentThreads { get; set; } = 10;

    /// <summary>
    /// Cửa sổ ngày: phân trang <c>order/list</c> SHEIN + pool TikTok khi <c>updateMode=all</c>.
    /// </summary>
    public int RefreshLookbackDays { get; set; } = 14;

    /// <summary>
    /// Chỉ còn <c>force_billnos</c> (chẩn đoán). Detail lấy từ <c>needsDetailBillnos</c>
    /// (đơn mới / thiếu cost / thiếu carrier sau ship / thiếu milestone clock).
    /// </summary>
    public List<string> RefreshBillnos { get; set; } = [];

    /// <summary>
    /// Billno còn việc Auto Mark Ship (chờ publish, hết cooldown lỗi, hoặc phải reconcile) trên đơn
    /// vẫn <c>AWAITING_SHIPMENT</c> và chưa có mã trên sàn.
    ///
    /// Auto Sync bỏ qua cả lượt khi fingerprint <c>order/list</c> không đổi, mà mã vận đơn SHEIN
    /// đứng yên chính là lúc fingerprint ngừng đổi — không có danh sách này thì mọi link bị hoãn
    /// theo ngân sách request (<c>OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest</c>) hoặc kẹt
    /// <c>unconfirmed</c> sẽ không bao giờ được thử lại. Extension ép POST những billno này.
    /// </summary>
    public List<string> PendingAutoMarkShipBillnos { get; set; } = [];

    /// <summary>
    /// Khi bật, extension đính kèm JSON gốc SHEIN vào payload sync để backend lưu lại phục vụ
    /// chẩn đoán. Mặc định tắt vì làm phình body POST.
    /// </summary>
    public bool CaptureRawPayload { get; set; }

    /// <summary>
    /// Always <c>realtime</c> — extension gọi SHEIN trên trình duyệt.
    /// </summary>
    public string HeaderSource { get; set; } = "realtime";
}
