using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class TikTokAccountStatusMapperTests
{
    [Theory]
    [InlineData(2, null, ShopStatuses.Active, ShopStatuses.Active)]
    [InlineData(2, null, ShopStatuses.Deactivated, ShopStatuses.Active)]
    [InlineData(3, null, ShopStatuses.Active, ShopStatuses.Deactivated)]
    [InlineData(3, null, ShopStatuses.Suspended, ShopStatuses.Deactivated)]
    [InlineData(null, 2, ShopStatuses.Deactivated, ShopStatuses.Active)]
    [InlineData(null, 3, ShopStatuses.Active, ShopStatuses.Deactivated)]
    [InlineData(null, 4, ShopStatuses.Active, ShopStatuses.Deactivated)]
    [InlineData(2, 3, ShopStatuses.Active, ShopStatuses.Deactivated)]
    [InlineData(2, 4, ShopStatuses.Active, ShopStatuses.Deactivated)]
    public void ResolveOpsStatus_MapsAccountAndShopCodes(
        int? sellerStatus,
        int? shopStatus,
        string current,
        string expected)
        => Assert.Equal(
            expected,
            TikTokAccountStatusMapper.ResolveOpsStatus(sellerStatus, shopStatus, current));

    [Fact]
    public void ResolveOpsStatus_DoesNotUnsuspendActiveAccount()
        => Assert.Null(
            TikTokAccountStatusMapper.ResolveOpsStatus(2, null, ShopStatuses.Suspended));

    [Fact]
    public void ResolveOpsStatus_PrefersWorseOfAccountAndShop()
    {
        Assert.Equal(
            ShopStatuses.Deactivated,
            TikTokAccountStatusMapper.ResolveOpsStatus(3, 2, ShopStatuses.Active));
        Assert.Equal(
            ShopStatuses.Deactivated,
            TikTokAccountStatusMapper.ResolveOpsStatus(2, 3, ShopStatuses.Active));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, null)]
    [InlineData(null, 1)]
    public void ResolveOpsStatus_IgnoresUnknownOrMissing(int? sellerStatus, int? shopStatus)
        => Assert.Null(
            TikTokAccountStatusMapper.ResolveOpsStatus(sellerStatus, shopStatus, ShopStatuses.Active));

    [Fact]
    public void ResolveOpsStatus_IdentityLock_SuspendsActiveAccount()
        => Assert.Equal(
            ShopStatuses.Suspended,
            TikTokAccountStatusMapper.ResolveOpsStatus(2, null, ShopStatuses.Active, true));

    [Fact]
    public void ResolveOpsStatus_IdentityLock_SuspendsDeactivatedAccount()
        => Assert.Equal(
            ShopStatuses.Suspended,
            TikTokAccountStatusMapper.ResolveOpsStatus(3, null, ShopStatuses.Deactivated, true));

    [Fact]
    public void ResolveOpsStatus_IdentityLockFalse_DoesNotUnsuspend()
        => Assert.Null(
            TikTokAccountStatusMapper.ResolveOpsStatus(2, null, ShopStatuses.Suspended, false));

    [Fact]
    public void IsDuplicateOpenPeriod_DetectsUniqueIndexName()
    {
        var inner = new InvalidOperationException(
            "duplicate key value violates unique constraint \"IX_ShopOperationalPeriods_ShopId_Open\"");
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException("save failed", inner);
        Assert.True(ShopOperationalPeriodService.IsDuplicateOpenPeriod(ex));
        Assert.False(
            ShopOperationalPeriodService.IsDuplicateOpenPeriod(
                new Microsoft.EntityFrameworkCore.DbUpdateException(
                    "save failed",
                    new InvalidOperationException("other"))));
    }
}
