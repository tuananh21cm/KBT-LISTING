namespace Application.Services;

using Application.DTOs.ShopTask;
using Shared.Constants;

/// <summary>
/// Suy trạng thái nhiệm vụ hệ thống từ dữ liệu đã load sẵn (sync log trong ngày, publish limit,
/// kết quả truy cập URL). Thuần và không chạm DB nên giữ được unit test cho toàn bộ quy tắc.
/// </summary>
public static class ShopTaskStatusRules
{
    /// <summary>Bốn bước extension phải chạy mỗi ngày, theo đúng thứ tự hiển thị.</summary>
    public static readonly string[] RequiredSyncTypes =
    [
        TikTokShopSyncTypes.Profile,
        TikTokShopSyncTypes.Payout,
        TikTokShopSyncTypes.Opportunity,
        TikTokShopSyncTypes.Analytics
    ];

    private static readonly Dictionary<string, string> SyncTypeLabels = new()
    {
        [TikTokShopSyncTypes.Profile] = "Info",
        [TikTokShopSyncTypes.Payout] = "Finance",
        [TikTokShopSyncTypes.Opportunity] = "Keywords",
        [TikTokShopSyncTypes.Analytics] = "Analytics"
    };

    /// <summary>
    /// Nhiệm vụ vẫn hiển thị trên tab hàng ngày nhưng không vào <c>completedCount</c> / <c>totalCount</c>
    /// và tổng hợp board.
    /// </summary>
    public static bool CountsTowardDailyProgress(ShopTaskItemDto task) =>
        task.Group == ShopTaskGroups.Daily && task.Key != ShopTaskKeys.ListingFill;

    /// <summary>
    /// Nhiệm vụ "mở shop đồng bộ dữ liệu qua extension". Done khi đủ cả 4 bước sync trong ngày VN;
    /// thiếu bước nào thì liệt kê ra để seller biết phải chạy lại.
    /// </summary>
    public static ShopTaskItemDto EvaluateSyncTask(
        string? externalShopId,
        IReadOnlyCollection<string> completedTypesToday,
        DateTime? lastCompletedAt)
    {
        var task = new ShopTaskItemDto
        {
            Key = ShopTaskKeys.SyncExtension,
            Title = "Đồng bộ dữ liệu shop qua extension",
            Group = ShopTaskGroups.Daily,
            Target = RequiredSyncTypes.Length
        };

        if (string.IsNullOrWhiteSpace(externalShopId))
        {
            task.Status = ShopTaskStatuses.Blocked;
            task.Detail = "Shop chưa có TikTok seller id — đăng nhập extension và Sync một lần để liên kết.";
            task.Current = 0;
            return task;
        }

        var done = RequiredSyncTypes.Where(completedTypesToday.Contains).ToList();
        task.Current = done.Count;

        if (done.Count >= RequiredSyncTypes.Length)
        {
            task.Status = ShopTaskStatuses.Done;
            task.Detail = "Đã đồng bộ đủ 4 nhóm dữ liệu hôm nay.";
            task.CompletedAt = lastCompletedAt;
            return task;
        }

        var missing = RequiredSyncTypes
            .Where(t => !completedTypesToday.Contains(t))
            .Select(t => SyncTypeLabels[t]);

        task.Status = ShopTaskStatuses.Pending;
        task.Detail = done.Count == 0
            ? "Chưa đồng bộ hôm nay — mở extension KBT TikTok Shop và bấm Sync."
            : $"Còn thiếu: {string.Join(", ", missing)}.";
        task.CompletedAt = done.Count > 0 ? lastCompletedAt : null;
        return task;
    }

    /// <summary>
    /// Nhiệm vụ "list đủ listing so với limit của shop". Chỉ dựa trên publish_limit đã sync từ TikTok
    /// nên không cần đếm lại listing.
    /// </summary>
    public static ShopTaskItemDto EvaluateListingFill(int? published, int? total)
    {
        var task = new ShopTaskItemDto
        {
            Key = ShopTaskKeys.ListingFill,
            Title = "List đủ listing so với limit của shop",
            Group = ShopTaskGroups.Daily,
            Current = published,
            Target = total
        };

        if (published is null || total is null or <= 0)
        {
            task.Status = ShopTaskStatuses.Blocked;
            task.Detail = "Chưa có publish_limit từ TikTok — chạy Sync extension để lấy giới hạn shop.";
            return task;
        }

        var remaining = total.Value - published.Value;
        if (remaining <= 0)
        {
            task.Status = ShopTaskStatuses.Done;
            task.Detail = $"Đã dùng hết slot ({published}/{total}).";
            return task;
        }

        task.Status = ShopTaskStatuses.Pending;
        task.Detail = $"Còn trống {remaining} slot ({published}/{total}) — list thêm để dùng hết giới hạn.";
        return task;
    }

    /// <summary>
    /// Nhiệm vụ tối ưu "hủy list rác để lấy slot". Chỉ bật khi shop đã hết slot.
    /// <paramref name="delistCount"/>/<paramref name="scanned"/> lấy từ
    /// <c>ListingDelistDaySummary</c> (chấm sau sync analytics) để chip board hiện tỷ lệ
    /// cần hủy / tổng đã soi — không chạy scoring trên bảng.
    /// </summary>
    public static ShopTaskItemDto EvaluateDelistCleanup(
        int? published,
        int? total,
        int? delistCount = null,
        int? scanned = null,
        int? recommendedDelistCount = null)
    {
        var task = new ShopTaskItemDto
        {
            Key = ShopTaskKeys.DelistCleanup,
            Title = "Hủy list rác để lấy slot mới",
            Group = ShopTaskGroups.Optimize,
            Current = delistCount,
            Target = scanned
        };

        if (published is null || total is null or <= 0)
        {
            task.Status = ShopTaskStatuses.Blocked;
            task.Detail = "Chưa có publish_limit từ TikTok — chưa xác định được slot còn trống.";
            return task;
        }

        var remaining = total.Value - published.Value;
        if (remaining > 0)
        {
            task.Status = ShopTaskStatuses.Done;
            task.Detail = delistCount is not null && scanned is not null
                ? $"Còn {remaining} slot trống — đề xuất hủy {delistCount}/{scanned} listing (chưa cần)."
                : $"Còn {remaining} slot trống, chưa cần hủy list.";
            return task;
        }

        task.Status = ShopTaskStatuses.Attention;
        if (delistCount is not null && scanned is not null)
        {
            var detail =
                $"Hết slot ({published}/{total}) — đề xuất hủy {delistCount}/{scanned} listing.";
            if (recommendedDelistCount is > 0)
            {
                detail += $" Hủy {recommendedDelistCount} cái là đủ chỗ keyword chờ list.";
            }

            task.Detail = detail;
            return task;
        }

        task.Detail =
            $"Hết slot ({published}/{total}) — xem đề xuất hủy list để lấy chỗ cho listing mới.";
        return task;
    }

    /// <summary>Nhiệm vụ truy cập URL, dựng từ định nghĩa và kết quả đã ghi nhận trong ngày.</summary>
    public static ShopTaskItemDto EvaluateUrlVisit(
        Guid definitionId,
        string name,
        string url,
        string? description,
        string? completionStatus,
        DateTime? completedAt,
        string? errorMessage)
    {
        var task = new ShopTaskItemDto
        {
            Key = ShopTaskKeys.UrlVisit,
            DefinitionId = definitionId,
            Title = name,
            Group = ShopTaskGroups.Daily,
            Url = url
        };

        if (completionStatus == ShopTaskStatuses.Done)
        {
            task.Status = ShopTaskStatuses.Done;
            task.Detail = "Đã truy cập hôm nay.";
            task.CompletedAt = completedAt;
            return task;
        }

        if (completionStatus == ShopTaskStatuses.Failed)
        {
            task.Status = ShopTaskStatuses.Failed;
            task.Detail = string.IsNullOrWhiteSpace(errorMessage)
                ? "Lần ghi nhận gần nhất không khớp URL nhiệm vụ — vào lại trang đúng."
                : errorMessage.Trim();
            return task;
        }

        task.Status = ShopTaskStatuses.Pending;
        task.Detail = string.IsNullOrWhiteSpace(description)
            ? "Chưa truy cập hôm nay — mở trang này trên extension hoặc menu TikTok."
            : description.Trim();
        return task;
    }

    /// <summary>
    /// Prefix mặc định để so khớp URL cuối: origin + path của URL đích (bỏ query và fragment).
    /// Trả về chuỗi rỗng khi URL không hợp lệ.
    /// </summary>
    public static string BuildDefaultMatchPattern(string? targetUrl)
    {
        if (!Uri.TryCreate(targetUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/');
    }

    /// <summary>
    /// Trang được coi là đã tới đúng nơi khi URL cuối (sau redirect) bắt đầu bằng prefix so khớp.
    /// Query thừa được chấp nhận; đổi host hoặc path khác thì không.
    /// </summary>
    public static bool MatchesTargetUrl(string? finalUrl, string? targetUrl, string? pattern)
    {
        var expected = string.IsNullOrWhiteSpace(pattern)
            ? BuildDefaultMatchPattern(targetUrl)
            : pattern.Trim().TrimEnd('/');

        if (expected.Length == 0 || string.IsNullOrWhiteSpace(finalUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(finalUrl.Trim(), UriKind.Absolute, out var actual))
        {
            return false;
        }

        var normalized = $"{actual.Scheme}://{actual.Authority}{actual.AbsolutePath}".TrimEnd('/');
        return normalized.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
    }
}
