namespace Shared.Constants;

/// <summary>Nguồn ghi nhận <see cref="Domain.Entities.ShopTaskCompletion"/>.</summary>
public static class ShopTaskSources
{
    /// <summary>Extension TikTok Shop tự mở URL trong lượt Sync.</summary>
    public const string Extension = "extension";
}

/// <summary>Host được phép đặt cho nhiệm vụ truy cập URL (khớp host_permissions của extension).</summary>
public static class ShopTaskUrlHosts
{
    /// <summary>TikTok Shop US seller center.</summary>
    public const string SellerUs = "seller-us.tiktok.com";

    /// <summary>Danh sách host hợp lệ.</summary>
    public static readonly string[] Allowed = [SellerUs];
}
