namespace Shared.Constants;

/// <summary>
/// Số ngày giữ các bảng chỉ-ghi-thêm mà không có ai xoá: nhật ký sync, snapshot phân tích, refresh
/// token đã hết vòng đời. Không bảng nào ở đây là dữ liệu nghiệp vụ — sổ cái, listing, SKU, lần mua
/// NCC đều không có retention.
///
/// Đặt <c>0</c> cho một key nào là tắt việc dọn bảng đó.
/// </summary>
public static class DataRetentionSettingKeys
{
    /// <summary>Nhóm cấu hình trong trang Settings.</summary>
    public const string Group = "Dọn dữ liệu cũ";

    /// <summary>Snapshot TikTok Compass theo ngày (<c>ListingAnalyticsSnapshots</c>).</summary>
    public const string AnalyticsSnapshotDays = "data_retention.analytics_snapshot_days";

    /// <summary>Nhật ký sync extension TikTok Shop (<c>TikTokShopSyncLogs</c>).</summary>
    public const string TikTokSyncLogDays = "data_retention.tiktok_sync_log_days";

    /// <summary>Refresh token đã revoke hoặc đã hết hạn (<c>RefreshTokens</c>).</summary>
    public const string RefreshTokenDays = "data_retention.refresh_token_days";

    /// <summary>Lịch sử phiên online extension (<c>ShopOnlineSessions</c>).</summary>
    public const string ShopOnlineSessionDays = "data_retention.shop_online_session_days";

    /// <summary>UI chỉ vẽ 90 ngày gần nhất, nên giữ đúng chừng đó.</summary>
    public const string DefaultAnalyticsSnapshotDays = "90";

    /// <summary>Nhật ký sync chỉ dùng để chẩn đoán một lượt vừa chạy.</summary>
    public const string DefaultTikTokSyncLogDays = "30";

    /// <summary>Token đã revoke chỉ còn giá trị điều tra phiên đăng nhập gần nhất.</summary>
    public const string DefaultRefreshTokenDays = "7";

    /// <summary>Giữ khoảng 3 tháng phút online để thống kê.</summary>
    public const string DefaultShopOnlineSessionDays = "90";

    /// <summary>Đọc số ngày, trả <c>null</c> khi tắt hoặc giá trị không hợp lệ.</summary>
    public static int? ResolveDays(string? raw, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        if (!int.TryParse(text, out var days) || days < 1)
        {
            return null;
        }

        return days;
    }
}
