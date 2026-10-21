namespace Domain.Entities;

/// <summary>
/// Mốc thời gian của lần sync listing thành công gần nhất cho một (seller, platform, status).
///
/// 4Seller chỉ đổi <c>updateTime</c> của một listing khi bản thân nó thay đổi, nên lần sync sau chỉ
/// cần hỏi những dòng mới hơn mốc này thay vì kéo lại toàn bộ trang. Nếu không có bảng này thì mọi
/// lượt sync đều phải tải lại cả trang từ 4Seller rồi nạp từng listing (kèm RawPayload) từ DB chỉ để
/// phát hiện là không có gì đổi.
///
/// Quy tắc an toàn: chỉ nâng <see cref="LastUpdateTimeUtc"/> khi seller đó chạy trọn vẹn không lỗi.
/// Trang 4Seller sắp xếp giảm dần theo update_time, nên nếu trang đầu xong mà trang sau hỏng thì
/// việc nâng mốc sẽ bỏ sót vĩnh viễn các dòng cũ hơn.
/// </summary>
public class ListingSyncWatermark
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; }

    /// <summary>Seller sở hữu cookie 4Seller dùng để sync.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Nền tảng (<c>tiktok</c>, …).</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Bucket trạng thái trên trang 4Seller (<c>draft</c>, <c>active</c>, …).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// <c>updateTime</c> lớn nhất đã xử lý trọn vẹn. Lần sync sau gửi giá trị này (trừ đi một biên
    /// an toàn) làm <c>updateTimeStart</c>. Null = chưa từng chạy trọn vẹn, phải quét đầy đủ.
    /// </summary>
    public DateTime? LastUpdateTimeUtc { get; set; }

    /// <summary>
    /// Lần gần nhất quét đầy đủ (không lọc delta). Dùng để định kỳ quét lại toàn bộ nhằm tự chữa
    /// lành khi một lượt delta bỏ sót dòng.
    /// </summary>
    public DateTime? LastFullScanAtUtc { get; set; }

    /// <summary>Lần cập nhật bản ghi mốc này.</summary>
    public DateTime UpdatedAt { get; set; }
}
