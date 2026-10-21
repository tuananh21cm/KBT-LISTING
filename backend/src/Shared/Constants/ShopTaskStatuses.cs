namespace Shared.Constants;

/// <summary>Trạng thái một nhiệm vụ của shop trên bảng nhiệm vụ.</summary>
public static class ShopTaskStatuses
{
    /// <summary>Đã hoàn thành trong ngày (hoặc không còn việc phải làm).</summary>
    public const string Done = "done";

    /// <summary>Chưa làm — vẫn còn thời gian trong ngày.</summary>
    public const string Pending = "pending";

    /// <summary>Cần xử lý ngay (ví dụ hết slot đăng bán).</summary>
    public const string Attention = "attention";

    /// <summary>Không đánh giá được vì thiếu dữ liệu đầu vào.</summary>
    public const string Blocked = "blocked";

    /// <summary>Lần thực hiện gần nhất thất bại.</summary>
    public const string Failed = "failed";
}
