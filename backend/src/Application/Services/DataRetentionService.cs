using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Dọn ba bảng tăng tuyến tính mà trước đây không có ai xoá.
///
/// Đây là phần <b>nhỏ</b> của bài dọn dung lượng (~40 MB trên tổng ~1 GB) — phần lớn dung lượng là
/// index bloat, xử lý ở <c>SupplierPurchaseIngest</c> + migration
/// <c>DropRedundantWriteHeavyIndexes</c>. Giá trị ở đây là chặn đà tăng: cả ba bảng đều thêm dòng
/// mỗi ngày và không bao giờ giảm.
///
/// Mọi lệnh xoá là <c>ExecuteDelete</c> — một câu <c>DELETE … WHERE</c>, không nạp entity nào về bộ
/// nhớ, vì lượt chạy đầu tiên sau khi bật retention là lượt phải xoá nhiều nhất.
///
/// Không có bảng nghiệp vụ nào ở đây: <c>Listings</c>, <c>ListingVariations</c>,
/// <c>SupplierPurchases</c>, <c>Orders</c>, sổ cái fulfil/ngân hàng và <c>RawPayload</c> đều không
/// có retention — bằng chứng một lần mua hay một SKU đã bán phải còn lại.
/// </summary>
public sealed class DataRetentionService : IDataRetentionService
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;

    /// <summary>Khởi tạo service dọn dữ liệu cũ.</summary>
    public DataRetentionService(IApplicationDbContext db, ISettingProvider settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> PurgeAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                DataRetentionSettingKeys.AnalyticsSnapshotDays,
                DataRetentionSettingKeys.TikTokSyncLogDays,
                DataRetentionSettingKeys.RefreshTokenDays,
                DataRetentionSettingKeys.ShopOnlineSessionDays
            ],
            cancellationToken);

        var deleted = new Dictionary<string, int>();
        var now = DateTime.UtcNow;

        var analyticsDays = Resolve(
            values,
            DataRetentionSettingKeys.AnalyticsSnapshotDays,
            DataRetentionSettingKeys.DefaultAnalyticsSnapshotDays);
        if (analyticsDays is { } snapshotDays)
        {
            // SnapshotDate là ngày VN (DateOnly), không phải mốc UTC — so bằng ngày để không lệch
            // một ngày ở hai đầu múi giờ.
            var cutoff = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(now, AppTimeZones.Vietnam).AddDays(-snapshotDays));
            var count = await _db.ListingAnalyticsSnapshots
                .Where(s => s.SnapshotDate < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (count > 0) deleted["ListingAnalyticsSnapshots"] = count;

            var summaryCount = await _db.ListingDelistDaySummaries
                .Where(s => s.SnapshotDate < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (summaryCount > 0) deleted["ListingDelistDaySummaries"] = summaryCount;
        }

        var syncLogDays = Resolve(
            values,
            DataRetentionSettingKeys.TikTokSyncLogDays,
            DataRetentionSettingKeys.DefaultTikTokSyncLogDays);
        if (syncLogDays is { } logDays)
        {
            var cutoff = now.AddDays(-logDays);
            var count = await _db.TikTokShopSyncLogs
                .Where(l => l.SyncedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (count > 0) deleted["TikTokShopSyncLogs"] = count;
        }

        var tokenDays = Resolve(
            values,
            DataRetentionSettingKeys.RefreshTokenDays,
            DataRetentionSettingKeys.DefaultRefreshTokenDays);
        if (tokenDays is { } refreshDays)
        {
            // Rotation ghi RevokedAt rồi thêm token mới, nên bảng chỉ lớn lên theo số lần refresh.
            // Xoá cả token hết hạn chưa từng bị revoke (đóng tab trước khi refresh) — cũng không
            // dùng lại được nữa. Token đang sống không bị chạm: đăng xuất người dùng là hồi quy
            // đắt hơn 10 MB tiết kiệm được.
            var cutoff = now.AddDays(-refreshDays);
            var count = await _db.RefreshTokens
                .Where(t => (t.RevokedAt != null && t.RevokedAt < cutoff) || t.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (count > 0) deleted["RefreshTokens"] = count;
        }

        var onlineDays = Resolve(
            values,
            DataRetentionSettingKeys.ShopOnlineSessionDays,
            DataRetentionSettingKeys.DefaultShopOnlineSessionDays);
        if (onlineDays is { } sessionDays)
        {
            var cutoff = now.AddDays(-sessionDays);
            var count = await _db.ShopOnlineSessions
                .Where(s => (s.EndedAt ?? s.LastSeenAt) < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (count > 0) deleted["ShopOnlineSessions"] = count;
        }

        return deleted;
    }

    private static int? Resolve(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        DataRetentionSettingKeys.ResolveDays(
            values.TryGetValue(key, out var raw) ? raw : null,
            fallback);
}
