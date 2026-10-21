using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Cửa sổ pool khớp SHEIN mode <c>all</c>.
///
/// Đơn sàn đặt <b>trước</b> lần mua NCC (đến hạn ship thường vài ngày). Chỉ lùi 3 ngày từ mốc mua
/// thì GSU18L24400NKV4 (mua 23/07) không thấy đơn 577484082473112269 (đặt 19/07) dù phone+zip trùng —
/// evidence ghi <c>phoneMatches=0, zipMatches=1</c> (đơn khác cùng zip vẫn trong pool).
/// </summary>
public class SheinMatchPoolSinceTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 6, 38, 0, DateTimeKind.Utc);

    [Fact]
    public void LeadCoversTikTokOrderPlacedDaysBeforeSheinPurchase()
    {
        // Đúng ca GSU18L24400NKV4: mua 23/07 01:42, đơn TikTok đặt 19/07 04:11.
        var earliestPurchase = new DateTime(2026, 7, 23, 1, 42, 14, DateTimeKind.Utc);
        var tiktokOrder = new DateTime(2026, 7, 19, 4, 11, 27, DateTimeKind.Utc);

        var poolSince = OrderSyncService.ComputeSheinMatchPoolSince(earliestPurchase, lookbackDays: 18, Now);

        Assert.True(tiktokOrder >= poolSince);
        // Lùi tối thiểu 14 ngày từ mốc mua, không phải 3.
        Assert.Equal(new DateTime(2026, 7, 5, 1, 42, 14, DateTimeKind.Utc), poolSince);
    }

    [Fact]
    public void UsesLookbackWhenLargerThanMinimumLead()
    {
        var earliestPurchase = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc);

        var poolSince = OrderSyncService.ComputeSheinMatchPoolSince(earliestPurchase, lookbackDays: 30, Now);

        Assert.Equal(new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc), poolSince);
    }

    [Fact]
    public void FloorsAtNinetyDays()
    {
        var earliestPurchase = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc);

        var poolSince = OrderSyncService.ComputeSheinMatchPoolSince(earliestPurchase, lookbackDays: 200, Now);

        Assert.Equal(Now.AddDays(-90), poolSince);
    }

    [Fact]
    public void ThreeDaySlackWouldHaveMissedEmilyOrder()
    {
        // Khoá lại vì sao bản -3 ngày sai: poolSince = 20/07 01:42, đơn Emily 19/07 04:11 nằm ngoài.
        var earliestPurchase = new DateTime(2026, 7, 23, 1, 42, 14, DateTimeKind.Utc);
        var tiktokOrder = new DateTime(2026, 7, 19, 4, 11, 27, DateTimeKind.Utc);
        var tooTight = earliestPurchase.AddDays(-3);

        Assert.True(tiktokOrder < tooTight);
        Assert.True(tiktokOrder >= OrderSyncService.ComputeSheinMatchPoolSince(earliestPurchase, 18, Now));
    }

    [Fact]
    public void NewModeUsesSameLeadWindowAsAll_ForFulfilCandidates()
    {
        // Mode new giờ cũng gọi ComputeSheinMatchPoolSince (không còn UtcNow-90 làm biên dưới).
        // Đơn fulfil đặt trong lead vẫn vào pool khớp mới; chỉ khác all ở chỗ lọc fulfil + incremental.
        var earliestPurchase = new DateTime(2026, 7, 23, 1, 42, 14, DateTimeKind.Utc);
        var fulfilOrderCreate = new DateTime(2026, 7, 19, 4, 11, 27, DateTimeKind.Utc);
        var poolSince = OrderSyncService.ComputeSheinMatchPoolSince(earliestPurchase, 14, Now);

        Assert.True(fulfilOrderCreate >= poolSince);
        Assert.True(OrderSyncService.IsNewModeFulfilWindowCandidate(fulfilOrderCreate, poolSince));
        Assert.False(OrderSyncService.IsNewModeFulfilWindowCandidate(
            poolSince.AddTicks(-1), poolSince));
    }
}
