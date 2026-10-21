namespace Application.DTOs.ShopTask;

using Application.DTOs.Shop;

/// <summary>Một nhiệm vụ của shop trong ngày đang xem.</summary>
public class ShopTaskItemDto
{
    /// <summary><c>sync_extension</c> / <c>url_visit</c> / <c>listing_fill</c> / <c>delist_cleanup</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Id định nghĩa khi <see cref="Key"/> là <c>url_visit</c>.</summary>
    public Guid? DefinitionId { get; set; }

    /// <summary>Tiêu đề hiển thị.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary><c>daily</c> hoặc <c>optimize</c>.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary><c>done</c> / <c>pending</c> / <c>attention</c> / <c>blocked</c> / <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Câu giải thích ngắn vì sao đang ở trạng thái này.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// Giá trị hiện tại khi nhiệm vụ có định lượng:
    /// <c>sync_extension</c> = số bước sync xong; <c>listing_fill</c> = published;
    /// <c>delist_cleanup</c> = <c>delistCount</c> từ summary sau sync analytics.
    /// </summary>
    public int? Current { get; set; }

    /// <summary>
    /// Mục tiêu tương ứng <see cref="Current"/>:
    /// <c>delist_cleanup</c> = <c>scanned</c> (tổng sản phẩm đã soi).
    /// </summary>
    public int? Target { get; set; }

    /// <summary>URL đích của nhiệm vụ truy cập URL.</summary>
    public string? Url { get; set; }

    /// <summary>Thời điểm hoàn thành (UTC) khi đã xong.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Bảng nhiệm vụ của một shop trong ngày.</summary>
public class ShopTaskShopDto
{
    /// <summary>Shop local id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Tên shop.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Tên profile trình duyệt (nếu có).</summary>
    public string? ProfileName { get; set; }

    /// <summary>Avatar shop (TikTok profile sync).</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>TikTok seller id — rỗng khi shop chưa liên kết extension.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Trạng thái ops shop: Active / Deactivated / Suspended.</summary>
    public string ShopStatus { get; set; } = string.Empty;

    /// <summary>Chủ shop.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Tên hiển thị của chủ shop.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Username chủ shop.</summary>
    public string OwnerUserName { get; set; } = string.Empty;

    /// <summary>TikTok publish_limit.published_num.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>TikTok publish_limit.total_limit.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>Slot còn trống; null khi chưa sync publish_limit.</summary>
    public int? RemainingSlots { get; set; }

    /// <summary>Số nhiệm vụ hàng ngày đã xong (không gồm <c>listing_fill</c>).</summary>
    public int CompletedCount { get; set; }

    /// <summary>Tổng số nhiệm vụ hàng ngày (không gồm <c>listing_fill</c>).</summary>
    public int TotalCount { get; set; }

    /// <summary>Số nhiệm vụ đang ở trạng thái cần xử lý ngay.</summary>
    public int AttentionCount { get; set; }

    /// <summary>Số đơn tab Đã giao (<c>DELIVERED</c>) — đếm theo <c>WorkflowTab</c>.</summary>
    public int DeliveredOrderCount { get; set; }

    /// <summary>Số đơn tab Refund (<c>REFUND</c>) — đếm theo <c>WorkflowTab</c>.</summary>
    public int RefundOrderCount { get; set; }

    /// <summary>
    /// Có ít nhất một Product Discount đang <c>ONGOING</c> trên shop (mirror local; không gồm FLASHSALE).
    /// </summary>
    public bool HasProductDiscount { get; set; }

    /// <summary>
    /// <c>BeginTimeUtc</c> của Product Discount ONGOING sắp hết hạn nhất (earliest <c>EndTimeUtc</c>); null khi không có.
    /// </summary>
    public DateTime? ProductDiscountBeginTimeUtc { get; set; }

    /// <summary>
    /// <c>EndTimeUtc</c> sớm nhất trong các Product Discount ONGOING; null khi không có.
    /// </summary>
    public DateTime? ProductDiscountEndTimeUtc { get; set; }

    /// <summary>Có ít nhất một Flash Deal (<c>FLASHSALE</c>) đang <c>ONGOING</c> trên shop.</summary>
    public bool HasFlashDeal { get; set; }

    /// <summary>
    /// <c>BeginTimeUtc</c> của Flash Deal ONGOING sắp hết hạn nhất (earliest <c>EndTimeUtc</c>); null khi không có.
    /// </summary>
    public DateTime? FlashDealBeginTimeUtc { get; set; }

    /// <summary>
    /// <c>EndTimeUtc</c> sớm nhất trong các Flash Deal ONGOING; null khi không có.
    /// </summary>
    public DateTime? FlashDealEndTimeUtc { get; set; }

    /// <summary>
    /// Số listing nên hủy (<c>delist</c>) từ <c>ListingDelistDaySummary</c> mới nhất; null khi chưa sync analytics.
    /// </summary>
    public int? DelistCount { get; set; }

    /// <summary>
    /// Số listing cần xem xét (<c>review</c>) từ <c>ListingDelistDaySummary</c> mới nhất; null khi chưa sync analytics.
    /// </summary>
    public int? ReviewCount { get; set; }

    /// <summary>
    /// Thời điểm sync Compass Product analysis mới nhất (UTC) — <c>MAX(SyncedAt)</c>
    /// trên <c>SnapshotDate</c> mới nhất của shop; null khi chưa có snapshot.
    /// </summary>
    public DateTime? AnalyticsSyncedAt { get; set; }

    /// <summary>Tổng GMV trên snapshot Compass mới nhất; null khi chưa có snapshot.</summary>
    public decimal? AnalyticsGmv { get; set; }

    /// <summary>Currency của <see cref="AnalyticsGmv"/> (thường USD).</summary>
    public string? AnalyticsCurrency { get; set; }

    /// <summary>
    /// Tổng Impressions các product trên snapshot Compass mới nhất của shop; null khi chưa có snapshot.
    /// </summary>
    public long? AnalyticsImpressions { get; set; }

    /// <summary>CTR shop-level = clicks ÷ impressions; null khi chưa có snapshot hoặc impressions = 0.</summary>
    public decimal? AnalyticsCtr { get; set; }

    /// <summary>Tổng Clicks trên snapshot Compass mới nhất; null khi chưa có snapshot.</summary>
    public long? AnalyticsClicks { get; set; }

    /// <summary>Tổng Add-to-cart units trên snapshot Compass mới nhất; null khi chưa có snapshot.</summary>
    public long? AnalyticsAddToCartUnits { get; set; }

    /// <summary>Tổng Orders (Compass) trên snapshot mới nhất; null khi chưa có snapshot.</summary>
    public int? AnalyticsOrders { get; set; }

    /// <summary>
    /// Tổng giây online trong ngày board (VN) từ <c>ShopOnlineSessions</c>.
    /// Phiên đang mở: nếu board = hôm nay thì tính đến UTC now; ngày quá khứ dùng LastSeenAt.
    /// </summary>
    public long TodayOnlineSeconds { get; set; }

    /// <summary>Danh sách nhiệm vụ (cả daily lẫn optimize).</summary>
    public List<ShopTaskItemDto> Tasks { get; set; } = [];

    /// <summary>Tóm tắt violation/warning TikTok Shop Growth Center (active).</summary>
    public ShopHealthSummaryDto? HealthSummary { get; set; }

    /// <summary>Health Center AHR score.</summary>
    public int? AhrScore { get; set; }

    /// <summary>Shop Performance Score (0–5).</summary>
    public decimal? ShopPerformanceScore { get; set; }

    /// <summary>SPS — Product satisfaction (0–5).</summary>
    public decimal? ProductSatisfactionScore { get; set; }

    /// <summary>SPS — Fulfillment and logistics (0–5).</summary>
    public decimal? FulfillmentScore { get; set; }

    /// <summary>SPS — Customer service (0–5).</summary>
    public decimal? CustomerServiceScore { get; set; }
}

/// <summary>Tổng hợp toàn bảng.</summary>
public class ShopTaskSummaryDto
{
    /// <summary>Số shop đang được đánh giá.</summary>
    public int ShopCount { get; set; }

    /// <summary>Số shop đã xong toàn bộ nhiệm vụ hàng ngày.</summary>
    public int CompletedShopCount { get; set; }

    /// <summary>Tổng số nhiệm vụ hàng ngày trên toàn bảng.</summary>
    public int TotalTaskCount { get; set; }

    /// <summary>Số nhiệm vụ hàng ngày đã xong.</summary>
    public int CompletedTaskCount { get; set; }

    /// <summary>Số nhiệm vụ cần xử lý ngay (gồm cả nhóm tối ưu).</summary>
    public int AttentionTaskCount { get; set; }

    /// <summary>Tổng publish limit (được phép list) trên các shop trong board.</summary>
    public int PublishLimitTotalSum { get; set; }

    /// <summary>Tổng listing đã published trên các shop trong board.</summary>
    public int PublishLimitPublishedSum { get; set; }

    /// <summary>Tổng listing nên hủy từ summary analytics mới nhất của từng shop.</summary>
    public int DelistCountSum { get; set; }

    /// <summary>Tổng listing cần xem xét từ summary analytics mới nhất của từng shop.</summary>
    public int ReviewCountSum { get; set; }
}

/// <summary>Kết quả <c>GET shop-tasks/board</c>.</summary>
public class ShopTaskBoardDto
{
    /// <summary>Ngày lịch VN của bảng (yyyy-MM-dd).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Tổng hợp.</summary>
    public ShopTaskSummaryDto Summary { get; set; } = new();

    /// <summary>Danh sách shop kèm nhiệm vụ.</summary>
    public List<ShopTaskShopDto> Shops { get; set; } = [];

    /// <summary>Cảnh báo cấu hình (ví dụ chưa có nhiệm vụ URL nào bật).</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Bộ lọc bảng nhiệm vụ.</summary>
public class ShopTaskBoardQuery
{
    /// <summary>Ngày lịch VN; bỏ trống = hôm nay.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Lọc theo chủ shop (chỉ admin).</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Lọc theo một shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Tìm theo tên shop hoặc TikTok seller id.</summary>
    public string? Search { get; set; }
}

/// <summary>Định nghĩa nhiệm vụ truy cập URL.</summary>
public class ShopTaskDefinitionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL đích.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Prefix so khớp URL cuối; null = dùng origin + path của <see cref="Url"/>.</summary>
    public string? UrlMatchPattern { get; set; }

    /// <summary>Mô tả.</summary>
    public string? Description { get; set; }

    /// <summary>Số giây phải xem trang sau khi load xong; <c>0</c> = tính ngay.</summary>
    public int ViewDurationSeconds { get; set; }

    /// <summary>Thứ tự.</summary>
    public int SortOrder { get; set; }

    /// <summary>Đang bật hay không.</summary>
    public bool IsActive { get; set; }

    /// <summary>Thời điểm tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Sắp xếp lại toàn bộ nhiệm vụ truy cập URL (id đầu = SortOrder 0).</summary>
public class ReorderShopTaskDefinitionsDto
{
    /// <summary>Danh sách id theo thứ tự mới, phải khớp mọi định nghĩa hiện có.</summary>
    public List<Guid> OrderedIds { get; set; } = [];
}

/// <summary>Body tạo / sửa nhiệm vụ truy cập URL.</summary>
public class SaveShopTaskDefinitionDto
{
    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL đích (https, host thuộc allowlist).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Prefix so khớp URL cuối.</summary>
    public string? UrlMatchPattern { get; set; }

    /// <summary>Mô tả.</summary>
    public string? Description { get; set; }

    /// <summary>Số giây phải xem trang sau khi load xong; <c>0</c> = tính ngay.</summary>
    public int ViewDurationSeconds { get; set; }

    /// <summary>Thứ tự.</summary>
    public int SortOrder { get; set; }

    /// <summary>Bật / tắt.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Một nhiệm vụ truy cập URL của shop trong hôm nay (extension hiển thị trên side panel).</summary>
public class ShopTaskExtensionItemDto
{
    /// <summary>Id định nghĩa — gửi lại khi báo kết quả.</summary>
    public Guid Id { get; set; }

    /// <summary>Tên hiển thị trên side panel.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL cần truy cập.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Prefix để extension đối chiếu URL người dùng đang mở.</summary>
    public string UrlMatchPattern { get; set; } = string.Empty;

    /// <summary>Số giây phải ở lại trang sau khi load xong; <c>0</c> = tính ngay.</summary>
    public int ViewDurationSeconds { get; set; }

    /// <summary>Đã hoàn thành hôm nay.</summary>
    public bool Done { get; set; }
}

/// <summary>Kết quả <c>GET shop-tasks/extension/daily</c>.</summary>
public class ShopTaskExtensionPlanDto
{
    /// <summary>Ngày lịch VN.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Shop local đã khớp; null khi chưa có shop cho seller id này.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Danh sách nhiệm vụ.</summary>
    public List<ShopTaskExtensionItemDto> Items { get; set; } = [];
}

/// <summary>Body extension báo người dùng vừa truy cập trang của một nhiệm vụ.</summary>
public class RecordShopTaskVisitDto
{
    /// <summary>Id định nghĩa.</summary>
    public Guid DefinitionId { get; set; }

    /// <summary>TikTok seller id của shop đang mở.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>URL người dùng đang mở khi trang đã load xong.</summary>
    public string? VisitedUrl { get; set; }

    /// <summary><c>done</c> hoặc <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Lý do khi failed.</summary>
    public string? ErrorMessage { get; set; }
}
