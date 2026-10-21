namespace Application.DTOs.Shop;

/// <summary>Một hình thức phạt từ sync extension (unix seconds).</summary>
public class SyncHealthActionDto
{
    /// <summary><c>action_title</c>.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Unix seconds <c>action_start_time</c>.</summary>
    public long? ActionStartTime { get; set; }

    /// <summary>Unix seconds <c>action_end_time</c>.</summary>
    public long? ActionEndTime { get; set; }
}

/// <summary>Một hình thức phạt cho UI board (UTC instants).</summary>
public class HealthActionDto
{
    /// <summary><c>action_title</c>.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Thời điểm bắt đầu phạt (UTC).</summary>
    public DateTime? ActionStartTime { get; set; }

    /// <summary>Thời điểm kết thúc phạt (UTC).</summary>
    public DateTime? ActionEndTime { get; set; }
}

/// <summary>Payload extension POST lên <c>/shops/tiktok-health-records/sync</c>.</summary>
public class SyncTikTokShopHealthRecordsDto
{
    /// <summary>TikTok seller id.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Alias của <see cref="ExternalShopId"/>.</summary>
    public string? SellerId { get; set; }

    /// <summary>Mã lượt unified Sync.</summary>
    public string? RunId { get; set; }

    /// <summary>Danh sách record đã map từ Growth Center.</summary>
    public List<SyncTikTokShopHealthRecordItemDto>? Items { get; set; }
}

/// <summary>Một violation hoặc warning record.</summary>
public class SyncTikTokShopHealthRecordItemDto
{
    /// <summary>TikTok <c>record_id</c>.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary><c>violation</c> hoặc <c>warning</c>.</summary>
    public string RecordType { get; set; } = string.Empty;

    /// <summary><c>static_expression.reason</c>.</summary>
    public string? Reason { get; set; }

    /// <summary><c>static_expression.detail</c> (plain text).</summary>
    public string? Detail { get; set; }

    /// <summary><c>static_expression.type</c>.</summary>
    public string? CategoryType { get; set; }

    /// <summary>Hình thức phạt từ <c>action_informations</c>.</summary>
    public List<SyncHealthActionDto>? Actions { get; set; }

    /// <summary><c>state.record_status</c>.</summary>
    public short RecordStatus { get; set; }

    /// <summary>Unix seconds <c>create_time</c>.</summary>
    public long CreateTime { get; set; }
}

/// <summary>Kết quả sync health records.</summary>
public class SyncTikTokShopHealthRecordsResultDto
{
    /// <summary>Shop local id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Số record insert mới.</summary>
    public int Inserted { get; set; }

    /// <summary>Số record cập nhật (đã tồn tại).</summary>
    public int Updated { get; set; }

    /// <summary>Số dòng payload bị bỏ (trùng recordId trong cùng request hoặc recordId rỗng).</summary>
    public int Skipped { get; set; }
}

/// <summary>Tóm tắt violation/warning active cho một shop trên bảng nhiệm vụ.</summary>
public class ShopHealthSummaryDto
{
    /// <summary>Số violation active chưa đánh dấu xử lý.</summary>
    public int ActiveViolationCount { get; set; }

    /// <summary>Số warning active chưa đánh dấu xử lý.</summary>
    public int ActiveWarningCount { get; set; }

    /// <summary>Số violation đã đánh dấu xử lý.</summary>
    public int HandledViolationCount { get; set; }

    /// <summary>Số warning đã đánh dấu xử lý.</summary>
    public int HandledWarningCount { get; set; }

    /// <summary>Bản ghi cần xử lý (active, chưa handled) — mới nhất trước.</summary>
    public List<ShopHealthRecordBriefDto> PendingRecent { get; set; } = [];

    /// <summary>Bản ghi đã đánh dấu xử lý — mới nhất trước.</summary>
    public List<ShopHealthRecordBriefDto> HandledRecent { get; set; } = [];
}

/// <summary>Một dòng violation/warning rút gọn cho UI.</summary>
public class ShopHealthRecordBriefDto
{
    /// <summary>TikTok record id.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary><c>violation</c> hoặc <c>warning</c>.</summary>
    public string RecordType { get; set; } = string.Empty;

    /// <summary><c>static_expression.reason</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary><c>static_expression.detail</c> (plain text).</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary><c>static_expression.type</c>.</summary>
    public string CategoryType { get; set; } = string.Empty;

    /// <summary>Hình thức phạt kèm thời hạn.</summary>
    public List<HealthActionDto> Actions { get; set; } = [];

    /// <summary>Thời điểm TikTok tạo record (UTC).</summary>
    public DateTime CreateTime { get; set; }

    /// <summary>Thời điểm người dùng đánh dấu đã xử lý (UTC), null nếu chưa.</summary>
    public DateTime? HandledAt { get; set; }
}

/// <summary>Body đánh dấu health record đã xử lý.</summary>
public class MarkShopHealthRecordHandledDto
{
    /// <summary>Shop local id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>TikTok record id.</summary>
    public string RecordId { get; set; } = string.Empty;
}
