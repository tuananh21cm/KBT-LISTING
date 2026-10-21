namespace Shared.Constants;

/// <summary>Status values for <see cref="Domain.Entities.TikTokShopSyncLog"/>.</summary>
public static class TikTokShopSyncStatuses
{
    /// <summary>Sync thành công.</summary>
    public const string Completed = "completed";

    /// <summary>Đã thử nhưng lỗi.</summary>
    public const string Failed = "failed";

    /// <summary>Bỏ qua vì thiếu điều kiện (chưa login, chưa có tab, …).</summary>
    public const string Skipped = "skipped";
}
