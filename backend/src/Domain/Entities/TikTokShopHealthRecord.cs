namespace Domain.Entities;

/// <summary>
/// Violation hoặc warning từ TikTok Shop Growth Center — sync qua extension, dedupe theo
/// <see cref="RecordId"/> trên từng shop.
/// </summary>
public class TikTokShopHealthRecord
{
    /// <summary>Khoá chính.</summary>
    public Guid Id { get; set; }

    /// <summary>Shop local.</summary>
    public Guid ShopId { get; set; }

    /// <summary>TikTok record id — khóa dedup (tương đương id message trên Seller Center).</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary><c>violation</c> hoặc <c>warning</c> — xem <c>TikTokShopHealthRecordTypes</c>.</summary>
    public string RecordType { get; set; } = string.Empty;

    /// <summary><c>static_expression.reason</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary><c>static_expression.detail</c> (plain text).</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary><c>static_expression.type</c>.</summary>
    public string CategoryType { get; set; } = string.Empty;

    /// <summary>JSON array hình thức phạt từ <c>action_informations</c> (title + optional unix times).</summary>
    public string ActionTitlesJson { get; set; } = "[]";

    /// <summary><c>state.record_status</c> — <c>1</c> = active trên TikTok.</summary>
    public short RecordStatus { get; set; }

    /// <summary>Thời điểm TikTok tạo record (UTC).</summary>
    public DateTime CreateTime { get; set; }

    /// <summary>Lần ghi đầu vào DB.</summary>
    public DateTime FirstSyncedAt { get; set; }

    /// <summary>Lần sync gần nhất.</summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>Thời điểm người dùng đánh dấu đã xử lý trên bảng nhiệm vụ (UTC).</summary>
    public DateTime? HandledAt { get; set; }
}
