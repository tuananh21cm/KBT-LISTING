using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Xunit;

namespace Application.Tests;

public class OrderTerminalEvidenceTests
{
    [Fact]
    public void ClearConflictingTerminals_ClearsRefundWhenPlatformDelivered()
    {
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.Delivered,
            RefundedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            CanceledAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            FulfilTimelineSources = """{"refundedAt":"manual","canceledAt":"manual"}"""
        };

        Assert.True(OrderTerminalEvidence.HasConflictingTerminal(order));
        Assert.True(OrderTerminalEvidence.ClearConflictingTerminals(order));
        Assert.Null(order.RefundedAt);
        Assert.Null(order.CanceledAt);
        Assert.False(OrderTerminalEvidence.HasConflictingTerminal(order));
    }

    [Fact]
    public void ClearConflictingTerminals_KeepsRefundWhenPlatformRefund()
    {
        var refundAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.Refund,
            RefundedAt = refundAt,
            CanceledAt = refundAt,
            FulfilTimelineSources = """{"refundedAt":"platform","canceledAt":"platform"}"""
        };

        Assert.False(OrderTerminalEvidence.HasConflictingTerminal(order));
        Assert.False(OrderTerminalEvidence.ClearConflictingTerminals(order));
        Assert.Equal(refundAt, order.RefundedAt);
        Assert.Equal(refundAt, order.CanceledAt);
    }

    /// <summary>
    /// Mốc Refund do Sync 4Seller stamp là bằng chứng hoàn của sàn: khi nó lệch cột trạng thái thì
    /// cột mới sai (4Seller thôi gửi <c>cancelReason</c> sau khi case hoàn đóng). Xoá nó đi là xoá
    /// đúng bằng chứng mà latch REFUND dùng để dựng lại status.
    /// </summary>
    [Fact]
    public void ClearConflictingTerminals_KeepsPlatformRefundClockOnCommerceStatus()
    {
        var refundAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.Delivered,
            RefundedAt = refundAt,
            FulfilTimelineSources = """{"refundedAt":"platform"}"""
        };

        Assert.False(OrderTerminalEvidence.HasConflictingTerminal(order));
        Assert.False(OrderTerminalEvidence.ClearConflictingTerminals(order));
        Assert.Equal(refundAt, order.RefundedAt);
    }

    [Fact]
    public void ClearConflictingTerminals_KeepsCanceledWhenPlatformCancelled()
    {
        var cancelAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.Cancelled,
            CanceledAt = cancelAt,
            FulfilTimelineSources = """{"canceledAt":"platform"}"""
        };

        Assert.False(OrderTerminalEvidence.ClearConflictingTerminals(order));
        Assert.Equal(cancelAt, order.CanceledAt);
    }

    [Fact]
    public void ClearConflictingTerminals_ClearsRefundClockOnCancelledPlatform()
    {
        var cancelAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            PlatformOrderStatus = PlatformOrderStatuses.Cancelled,
            CanceledAt = cancelAt,
            RefundedAt = cancelAt,
            FulfilTimelineSources = """{"canceledAt":"shein","refundedAt":"shein"}"""
        };

        Assert.True(OrderTerminalEvidence.ClearConflictingTerminals(order));
        Assert.Null(order.RefundedAt);
        Assert.Equal(cancelAt, order.CanceledAt);
    }
}
