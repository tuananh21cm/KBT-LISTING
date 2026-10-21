using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Ngân sách Mark Ship cho một lô.
///
/// Trần mặc định 2 lần / 45s tồn tại vì Cloudflare cắt request extension ở ~100s. Vòng quét nền
/// không đi qua request nào nên phải nâng được, nếu không một lượt đã trả tiền cho cả lô
/// <c>get_order_info</c> mà chỉ đẩy được 2 đơn lên sàn.
/// </summary>
public class OrderAutoMarkShipBudgetTests
{
    [Fact]
    public void NullRequest_KeepsExtensionDefault()
    {
        Assert.Equal(
            OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest,
            OrderAutoMarkShipPolicy.ResolveAttemptBudget(null));
    }

    [Fact]
    public void ExplicitZero_MeansPublishNothing()
    {
        // Vòng quét truyền 0 khi đã hết ngân sách lượt: lô vẫn ghi tracking, chỉ không gọi 4Seller.
        Assert.Equal(0, OrderAutoMarkShipPolicy.ResolveAttemptBudget(0));
    }

    [Fact]
    public void RequestedBudgetIsHonoured()
    {
        Assert.Equal(10, OrderAutoMarkShipPolicy.ResolveAttemptBudget(10));
    }

    [Fact]
    public void RequestedBudgetIsCapped()
    {
        Assert.Equal(
            OrderAutoMarkShipPolicy.MaxAttemptsOverride,
            OrderAutoMarkShipPolicy.ResolveAttemptBudget(10_000));
    }

    [Fact]
    public void NegativeBudgetClampsToZero()
    {
        Assert.Equal(0, OrderAutoMarkShipPolicy.ResolveAttemptBudget(-5));
    }

    [Fact]
    public void ExtensionPathKeepsExactlyTheOldDuration()
    {
        // Mark Ship là một pha trong request extension mà Cloudflare đã cắt ở ~100s. Nới thời
        // gian ở đây là mất cả response, nên đường mặc định phải giữ nguyên 45s.
        Assert.Equal(
            OrderAutoMarkShipPolicy.MaxDurationPerSyncRequest,
            OrderAutoMarkShipPolicy.ResolveDurationBudget(null));
    }

    [Fact]
    public void SmallExplicitBudget_StillGetsTheFloor()
    {
        Assert.Equal(
            OrderAutoMarkShipPolicy.MaxDurationPerSyncRequest,
            OrderAutoMarkShipPolicy.ResolveDurationBudget(1));
    }

    [Fact]
    public void DurationScalesWithAttempts()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(300),
            OrderAutoMarkShipPolicy.ResolveDurationBudget(10));
    }

    [Fact]
    public void ZeroAttempts_StillGetsNonZeroDuration()
    {
        // Budget 0 không được sinh TimeSpan.Zero: vòng lặp so `elapsed >= budget` sẽ hoãn cả lô
        // trước khi kịp ghi gì, ngay cả với lô không có ứng viên mark-ship nào.
        Assert.True(OrderAutoMarkShipPolicy.ResolveDurationBudget(0) > TimeSpan.Zero);
    }
}
