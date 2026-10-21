using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khoá quy tắc suy trạng thái nhiệm vụ hệ thống: chỉ báo xong khi thật sự xong, thiếu dữ liệu thì
/// nói rõ là chặn chứ không im lặng báo đạt, và URL cuối phải khớp đích mới được tính hoàn thành.
/// </summary>
public class ShopTaskStatusRulesTests
{
    private static readonly string[] AllSyncTypes = ShopTaskStatusRules.RequiredSyncTypes;

    [Fact]
    public void SyncTask_Done_KhiDuBonBuoc()
    {
        var task = ShopTaskStatusRules.EvaluateSyncTask("7495", AllSyncTypes, DateTime.UtcNow);

        Assert.Equal(ShopTaskStatuses.Done, task.Status);
        Assert.Equal(4, task.Current);
        Assert.Equal(4, task.Target);
    }

    [Fact]
    public void SyncTask_Pending_VaLietKeBuocConThieu()
    {
        var task = ShopTaskStatusRules.EvaluateSyncTask(
            "7495",
            [TikTokShopSyncTypes.Profile, TikTokShopSyncTypes.Payout],
            DateTime.UtcNow);

        Assert.Equal(ShopTaskStatuses.Pending, task.Status);
        Assert.Equal(2, task.Current);
        Assert.Contains("Keywords", task.Detail);
        Assert.Contains("Analytics", task.Detail);
    }

    [Fact]
    public void SyncTask_Blocked_KhiShopChuaCoSellerId()
    {
        var task = ShopTaskStatusRules.EvaluateSyncTask(null, AllSyncTypes, DateTime.UtcNow);

        Assert.Equal(ShopTaskStatuses.Blocked, task.Status);
        Assert.Equal(0, task.Current);
    }

    [Fact]
    public void CountsTowardDailyProgress_LoaiListingFill()
    {
        var listingFill = ShopTaskStatusRules.EvaluateListingFill(120, 120);
        var sync = ShopTaskStatusRules.EvaluateSyncTask("7495", AllSyncTypes, DateTime.UtcNow);

        Assert.False(ShopTaskStatusRules.CountsTowardDailyProgress(listingFill));
        Assert.True(ShopTaskStatusRules.CountsTowardDailyProgress(sync));
    }

    [Theory]
    [InlineData(120, 120, ShopTaskStatuses.Done)]
    [InlineData(140, 120, ShopTaskStatuses.Done)]
    [InlineData(90, 120, ShopTaskStatuses.Pending)]
    public void ListingFill_TheoPublishLimit(int published, int total, string expected)
    {
        var task = ShopTaskStatusRules.EvaluateListingFill(published, total);

        Assert.Equal(expected, task.Status);
    }

    [Theory]
    [InlineData(null, 120)]
    [InlineData(90, null)]
    [InlineData(90, 0)]
    public void ListingFill_Blocked_KhiThieuPublishLimit(int? published, int? total)
    {
        var task = ShopTaskStatusRules.EvaluateListingFill(published, total);

        Assert.Equal(ShopTaskStatuses.Blocked, task.Status);
    }

    [Fact]
    public void DelistCleanup_Attention_KhiHetSlot()
    {
        var task = ShopTaskStatusRules.EvaluateDelistCleanup(120, 120);

        Assert.Equal(ShopTaskStatuses.Attention, task.Status);
        Assert.Equal(ShopTaskGroups.Optimize, task.Group);
        Assert.Null(task.Current);
        Assert.Null(task.Target);
    }

    [Fact]
    public void DelistCleanup_Done_KhiVanConSlot()
    {
        var task = ShopTaskStatusRules.EvaluateDelistCleanup(100, 120);

        Assert.Equal(ShopTaskStatuses.Done, task.Status);
    }

    [Fact]
    public void DelistCleanup_GanDelistCountScannedVaoCurrentTarget()
    {
        var task = ShopTaskStatusRules.EvaluateDelistCleanup(
            published: 120,
            total: 120,
            delistCount: 18,
            scanned: 240,
            recommendedDelistCount: 5);

        Assert.Equal(ShopTaskStatuses.Attention, task.Status);
        Assert.Equal(18, task.Current);
        Assert.Equal(240, task.Target);
        Assert.Contains("18/240", task.Detail);
        Assert.Contains("Hủy 5 cái", task.Detail);
    }

    [Fact]
    public void DelistCleanup_DoneVanGiuDelistCountKhiConSlot()
    {
        var task = ShopTaskStatusRules.EvaluateDelistCleanup(
            published: 100,
            total: 120,
            delistCount: 12,
            scanned: 200);

        Assert.Equal(ShopTaskStatuses.Done, task.Status);
        Assert.Equal(12, task.Current);
        Assert.Equal(200, task.Target);
        Assert.Contains("12/200", task.Detail);
    }

    [Fact]
    public void UrlVisit_GiuLyDoThatBaiCuaLanGanNhat()
    {
        var task = ShopTaskStatusRules.EvaluateUrlVisit(
            Guid.NewGuid(),
            "Vào Seller Center",
            "https://seller-us.tiktok.com/homepage",
            "mô tả",
            ShopTaskStatuses.Failed,
            DateTime.UtcNow,
            "Trang load quá lâu.");

        Assert.Equal(ShopTaskStatuses.Failed, task.Status);
        Assert.Equal("Trang load quá lâu.", task.Detail);
    }

    [Fact]
    public void UrlVisit_Pending_DungMoTaLamHuongDan()
    {
        var task = ShopTaskStatusRules.EvaluateUrlVisit(
            Guid.NewGuid(),
            "Vào Seller Center",
            "https://seller-us.tiktok.com/homepage",
            "Mở mỗi ngày.",
            null,
            null,
            null);

        Assert.Equal(ShopTaskStatuses.Pending, task.Status);
        Assert.Equal("Mở mỗi ngày.", task.Detail);
    }

    [Fact]
    public void BuildDefaultMatchPattern_BoQueryVaSlashCuoi()
    {
        var pattern = ShopTaskStatusRules.BuildDefaultMatchPattern(
            "https://seller-us.tiktok.com/homepage/?from=menu#tab");

        Assert.Equal("https://seller-us.tiktok.com/homepage", pattern);
    }

    [Theory]
    [InlineData("https://seller-us.tiktok.com/homepage")]
    [InlineData("https://seller-us.tiktok.com/homepage/")]
    [InlineData("https://seller-us.tiktok.com/homepage?shop_region=US")]
    [InlineData("https://seller-us.tiktok.com/homepage/overview")]
    [InlineData("https://SELLER-US.tiktok.com/homepage")]
    public void MatchesTargetUrl_ChapNhanRedirectGiuNguyenPath(string finalUrl)
    {
        Assert.True(ShopTaskStatusRules.MatchesTargetUrl(
            finalUrl,
            "https://seller-us.tiktok.com/homepage",
            null));
    }

    [Theory]
    [InlineData("https://seller-us.tiktok.com/login")]
    [InlineData("https://www.tiktok.com/homepage")]
    [InlineData("http://seller-us.tiktok.com/homepage")]
    [InlineData("khong-phai-url")]
    [InlineData(null)]
    public void MatchesTargetUrl_TuChoiKhiLechDich(string? finalUrl)
    {
        Assert.False(ShopTaskStatusRules.MatchesTargetUrl(
            finalUrl,
            "https://seller-us.tiktok.com/homepage",
            null));
    }

    [Fact]
    public void MatchesTargetUrl_UuTienPatternDoAdminDat()
    {
        Assert.True(ShopTaskStatusRules.MatchesTargetUrl(
            "https://seller-us.tiktok.com/product/manage",
            "https://seller-us.tiktok.com/homepage",
            "https://seller-us.tiktok.com/product"));
    }
}
