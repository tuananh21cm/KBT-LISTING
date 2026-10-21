namespace Domain.Entities;

/// <summary>
/// Nhật ký mỗi lần extension sync một loại dữ liệu TikTok Shop (profile, payout, keywords, analytics).
/// Giữ vĩnh viễn để thống kê — chỉ trạng thái + thời gian, không lưu payload chi tiết.
/// </summary>
public class TikTokShopSyncLog
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Mã lượt do extension sinh — gộp các bước trong cùng một lần bấm Sync.
    /// </summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Owner shop (JWT).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Shop local khi đã match; null khi chưa có shop hoặc skip sớm.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>TikTok seller id (<c>Shop.ExternalShopId</c>).</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>
    /// Loại sync: <c>profile</c>, <c>payout</c>, <c>opportunity</c>, <c>analytics</c>
    /// — xem <c>Shared.Constants.TikTokShopSyncTypes</c>.
    /// </summary>
    public string SyncType { get; set; } = string.Empty;

    /// <summary>
    /// <c>completed</c>, <c>failed</c>, hoặc <c>skipped</c>
    /// — xem <c>Shared.Constants.TikTokShopSyncStatuses</c>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Lỗi rút gọn khi <see cref="Status"/> là failed/skipped.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Thời điểm kết thúc (UTC).</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>Nguồn kích hoạt — mặc định <c>manual</c>.</summary>
    public string Source { get; set; } = string.Empty;
}
