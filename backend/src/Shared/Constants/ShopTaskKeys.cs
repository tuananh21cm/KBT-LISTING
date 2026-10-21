namespace Shared.Constants;

/// <summary>Mã nhiệm vụ hệ thống hiển thị trên bảng nhiệm vụ theo shop.</summary>
public static class ShopTaskKeys
{
    /// <summary>Mở shop và đồng bộ dữ liệu qua extension TikTok Shop.</summary>
    public const string SyncExtension = "sync_extension";

    /// <summary>Truy cập URL TikTok Shop do admin cấu hình (một mã dùng chung, phân biệt bằng definitionId).</summary>
    public const string UrlVisit = "url_visit";

    /// <summary>List đủ listing so với publish limit của shop.</summary>
    public const string ListingFill = "listing_fill";

    /// <summary>Hủy list rác để lấy lại slot đăng bán.</summary>
    public const string DelistCleanup = "delist_cleanup";
}

/// <summary>Nhóm nhiệm vụ trên UI.</summary>
public static class ShopTaskGroups
{
    /// <summary>Nhiệm vụ phải làm mỗi ngày.</summary>
    public const string Daily = "daily";

    /// <summary>Nhiệm vụ tối ưu shop (không theo ngày).</summary>
    public const string Optimize = "optimize";
}
