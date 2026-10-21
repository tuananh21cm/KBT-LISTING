namespace Domain.Entities;

/// <summary>
/// Một dòng cho mỗi POST <c>admin/orders/shein-sync</c> — nhật ký sau lượt của Sync Order.
///
/// Kết quả sync trước đây chỉ tồn tại trong response HTTP: extension in ra một dòng tóm tắt rồi
/// bỏ đi. Khi có một con số đáng ngờ (bao nhiêu đơn không khớp pool, vì sao đơn đã khớp vẫn bị
/// gọi lại detail) thì không còn gì để tra, phải chạy lại lượt mới và hy vọng lặp lại được.
/// Bảng này giữ đủ chi tiết theo billno để trả lời trực tiếp, và tự xoá dòng cũ hơn 7 ngày.
/// </summary>
public class SheinSyncRunLog
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã lượt do extension sinh, dùng chung cho mọi lô list + detail của cùng một lần bấm
    /// Sync Order. Rỗng khi client cũ không gửi.
    /// </summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Phase của lô: <c>list</c> hoặc <c>detail</c>.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>Thứ tự lô trong lượt (theo phase), <c>null</c> khi client không gửi.</summary>
    public int? BatchIndex { get; set; }

    /// <summary>Chế độ cập nhật đã áp dụng: <c>new</c> hoặc <c>all</c>.</summary>
    public string UpdateMode { get; set; } = string.Empty;

    /// <summary>Cửa sổ lookback (ngày) đã dùng để dựng pool.</summary>
    public int LookbackDays { get; set; }

    /// <summary>Số đơn local trong pool khớp sau khi lọc theo tài khoản mua hàng.</summary>
    public int PoolSize { get; set; }

    /// <summary>Số billno SHEIN trong lô này.</summary>
    public int Fetched { get; set; }

    /// <summary>Số billno đã gắn/cập nhật vào đơn local.</summary>
    public int Matched { get; set; }

    /// <summary>Số billno không gắn được (bằng tổng <see cref="SkipCountsJson"/>).</summary>
    public int Unmatched { get; set; }

    /// <summary>Số billno đã gắn sẵn và không có gì để ghi (chỉ ở chế độ <c>new</c>).</summary>
    public int Unchanged { get; set; }

    /// <summary>Số đơn được ghi thêm mã vận đơn.</summary>
    public int UpdatedTracking { get; set; }

    /// <summary>Số đơn được ghi thêm giá mua.</summary>
    public int UpdatedCost { get; set; }

    /// <summary>JSON <c>{reason: count}</c> theo <c>Shared.Constants.SheinSkipReasons</c>.</summary>
    public string? SkipCountsJson { get; set; }

    /// <summary>JSON danh sách <c>{billno, reason, detail}</c> của mọi billno bị bỏ qua.</summary>
    public string? SkipOutcomesJson { get; set; }

    /// <summary>JSON <c>{billno: reason}</c> của các billno phase list yêu cầu gọi detail.</summary>
    public string? NeedsDetailReasonsJson { get; set; }

    /// <summary>Tài khoản mua hàng đã resolve cho lô (nếu có).</summary>
    public string? FulfilAccountName { get; set; }

    /// <summary>
    /// <c>true</c> khi lô quyết định xong nhưng <c>SaveChanges</c> vỡ (xung đột unique index) nên
    /// không dòng nào xuống DB. Không có cờ này thì nhật ký của một lô mất trắng trông giống hệt
    /// một lô thành công, và mọi con số trong dòng đó đều là số ảo.
    /// </summary>
    public bool SaveFailed { get; set; }

    /// <summary>Thời điểm ghi (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời gian xử lý lô ở backend (ms).</summary>
    public int DurationMs { get; set; }
}
