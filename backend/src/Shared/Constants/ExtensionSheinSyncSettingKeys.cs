namespace Shared.Constants;

/// <summary>
/// Setting keys for the fulfil-extension SHEIN auto-sync (managed via admin Settings).
/// Controls the "Sync Order" auto-run interval (extension chrome.alarms — runs with panel closed).
/// </summary>
public static class ExtensionSheinSyncSettingKeys
{
    /// <summary>When true, the extension auto-syncs SHEIN orders on the configured interval (background alarms).</summary>
    public const string Enabled = "extension.shein_sync.enabled";

    /// <summary>Interval magnitude (paired with <see cref="IntervalUnit"/>). Minimum 1.</summary>
    public const string IntervalValue = "extension.shein_sync.interval_value";

    /// <summary>Interval unit: <c>seconds</c> or <c>minutes</c>.</summary>
    public const string IntervalUnit = "extension.shein_sync.interval_unit";

    /// <summary>
    /// Nguồn đồng bộ SHEIN (legacy): luôn <see cref="HeaderSourceRealtime"/>.
    /// Settings UI no longer exposes Local.
    /// </summary>
    public const string HeaderSource = "extension.shein_sync.header_source";

    /// <summary>
    /// Update mode: <see cref="UpdateModeNew"/> (pool fulfil) hoặc <see cref="UpdateModeAll"/>
    /// (mọi đơn không huỷ trong cửa sổ Lookback — cập nhật chính xác, không xóa link hàng loạt).
    /// </summary>
    public const string UpdateMode = "extension.shein_sync.update_mode";

    /// <summary>
    /// Số luồng song song khi gọi SHEIN get_order_info (phase detail). Clamp 1–20.
    /// </summary>
    public const string ConcurrentThreads = "extension.shein_sync.concurrent_threads";

    /// <summary>
    /// Cửa sổ ngày theo <c>orderedAt</c> khi phân trang <c>order/list</c>
    /// (dừng sớm khi trang toàn đơn ngoài cửa sổ). Không dùng để build open-set từ DB.
    /// Clamp 1–60, mặc định 14.
    /// </summary>
    public const string RefreshLookbackDays = "extension.shein_sync.refresh_lookback_days";

    /// <summary>
    /// Giữ tương thích Settings. Không còn dùng để cap open-set refresh từ DB.
    /// </summary>
    public const string RefreshMaxBillnos = "extension.shein_sync.refresh_max_billnos";

    /// <summary>
    /// Khi bật, extension đính kèm JSON gốc SHEIN (list item + get_order_info) vào payload sync
    /// và backend lưu vào <c>OrderSupplierLinks.RawPayload</c>. Mọi field khác đều là kết quả
    /// suy diễn — không có bản gốc thì không kiểm chứng được tên field hay múi giờ. Tốn băng
    /// thông nên chỉ bật khi đang chẩn đoán.
    /// </summary>
    public const string CaptureRawPayload = "extension.shein_sync.capture_raw_payload";

    /// <summary>
    /// Danh sách billno (phân tách bởi dấu phẩy) ép lấy lại chi tiết ngoài list SHEIN lần này.
    /// Chỉ dùng chẩn đoán. Để trống khi vận hành thường.
    /// </summary>
    public const string ForceBillnos = "extension.shein_sync.force_billnos";

    /// <summary>Trần số billno ép lấy lại, chặn việc dán nhầm cả nghìn mã vào ô setting.</summary>
    public const int MaxForceBillnos = 50;

    /// <summary>Default concurrent detail fetches when setting missing/invalid.</summary>
    public const int DefaultConcurrentThreads = 10;

    /// <summary>Minimum concurrent detail fetches.</summary>
    public const int MinConcurrentThreads = 1;

    /// <summary>Maximum concurrent detail fetches.</summary>
    public const int MaxConcurrentThreads = 20;

    /// <summary>Default list lookback days (mode <c>new</c>).</summary>
    public const int DefaultRefreshLookbackDays = 14;

    /// <summary>Minimum lookback days.</summary>
    public const int MinRefreshLookbackDays = 1;

    /// <summary>Maximum lookback days.</summary>
    public const int MaxRefreshLookbackDays = 60;

    /// <summary>Default cap of refresh billnos returned per config fetch.</summary>
    public const int DefaultRefreshMaxBillnos = 300;

    /// <summary>Minimum refresh billno cap.</summary>
    public const int MinRefreshMaxBillnos = 50;

    /// <summary>
    /// Hard ceiling for the refresh billno cap. Phase detail chạy 10 request/lô nên đặt quá cao
    /// sẽ kéo dài một lượt sync và dễ ăn Cloudflare 524.
    /// </summary>
    public const int MaxRefreshMaxBillnos = 1000;

    /// <summary>Interval unit value: seconds.</summary>
    public const string UnitSeconds = "seconds";

    /// <summary>Interval unit value: minutes.</summary>
    public const string UnitMinutes = "minutes";

    /// <summary>List SHEIN trong lookback — match đơn mới (phone+zip); cập nhật đơn đã có billno.</summary>
    public const string UpdateModeNew = "new";

    /// <summary>List SHEIN sâu hơn — rematch wipe links trong batch; detail chỉ khi thiếu cost/carrier/milestone.</summary>
    public const string UpdateModeAll = "all";

    /// <summary>
    /// Backend gọi SHEIN bằng cookie + header đã lưu trên tài khoản fulfil (qua proxy).
    /// </summary>
    public const string HeaderSourceLocal = "local";

    /// <summary>Extension gọi SHEIN trên trình duyệt (hành vi mặc định).</summary>
    public const string HeaderSourceRealtime = "realtime";

    /// <summary>
    /// Marker: đã flip nghĩa <c>local</c>/<c>realtime</c> một lần (v1).
    /// </summary>
    public const string HeaderSourceRenamedMarker = "maintenance.shein_header_source_renamed_v1";
}
