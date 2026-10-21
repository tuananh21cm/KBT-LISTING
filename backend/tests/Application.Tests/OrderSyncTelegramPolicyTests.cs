using Application.Services;
using Domain.Entities;
using Infrastructure.Integrations.Telegram;
using Xunit;

namespace Application.Tests;

public class OrderSyncTelegramPolicyTests
{
    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 0, false)]
    public void ShouldNotifyNewOrders_RespectsSkipFlagAndCount(
        bool skip,
        int inserted,
        bool expected)
    {
        Assert.Equal(expected, OrderSyncTelegramPolicy.ShouldNotifyNewOrders(skip, inserted));
    }

    [Fact]
    public void ApplyNewOrderNotifyClaim_SucceedsOnlyWhenOneRowUpdated()
    {
        var order = new Order { Id = Guid.NewGuid() };
        var claimedAt = DateTime.UtcNow;

        Assert.False(TelegramNotifier.ApplyNewOrderNotifyClaim(order, rowsAffected: 0, claimedAt));
        Assert.Null(order.TelegramNewOrderNotifiedAt);

        Assert.True(TelegramNotifier.ApplyNewOrderNotifyClaim(order, rowsAffected: 1, claimedAt));
        Assert.Equal(claimedAt, order.TelegramNewOrderNotifiedAt);
        Assert.Equal(claimedAt, order.UpdatedAt);

        Assert.False(TelegramNotifier.ApplyNewOrderNotifyClaim(order, rowsAffected: 2, claimedAt));
    }

    [Fact]
    public void ClearNewOrderNotifyClaim_NullsStamp()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TelegramNewOrderNotifiedAt = DateTime.UtcNow
        };

        TelegramNotifier.ClearNewOrderNotifyClaim(order);
        Assert.Null(order.TelegramNewOrderNotifiedAt);
    }

    [Fact]
    public void ShouldAttemptNewOrderNotify_FalseAfterClaimApplied()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Amount = 10m,
            Items = [new OrderItem { Title = "Product", ItemPrice = 10m }]
        };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = order.OwnerId, Name = "Shop" };

        Assert.True(TelegramNotifier.ShouldAttemptNewOrderNotify(order, shop));
        Assert.True(TelegramNotifier.ApplyNewOrderNotifyClaim(order, 1, DateTime.UtcNow));
        Assert.False(TelegramNotifier.ShouldAttemptNewOrderNotify(order, shop));
    }
}
