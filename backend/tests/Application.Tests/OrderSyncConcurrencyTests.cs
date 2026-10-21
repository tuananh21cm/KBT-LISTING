using Application.DTOs.FourSeller;
using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests;

public class OrderSyncConcurrencyTests
{
    [Theory]
    [InlineData(EntityState.Modified, false, OrderSyncConcurrencyAction.Reinsert)]
    [InlineData(EntityState.Added, false, OrderSyncConcurrencyAction.Reinsert)]
    [InlineData(EntityState.Deleted, false, OrderSyncConcurrencyAction.Detach)]
    [InlineData(EntityState.Unchanged, false, OrderSyncConcurrencyAction.Detach)]
    [InlineData(EntityState.Modified, true, OrderSyncConcurrencyAction.RefreshOriginals)]
    [InlineData(EntityState.Deleted, true, OrderSyncConcurrencyAction.RefreshOriginals)]
    public void OrderItem_MissingRow_ReinsertsWhenStillWriting(
        EntityState state,
        bool databaseRowExists,
        OrderSyncConcurrencyAction expected)
        => Assert.Equal(
            expected,
            OrderSyncConcurrency.Resolve(typeof(OrderItem), state, databaseRowExists));

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Deleted)]
    public void Order_MissingRow_DoesNotResurrect(EntityState state)
        => Assert.Equal(
            OrderSyncConcurrencyAction.Detach,
            OrderSyncConcurrency.Resolve(typeof(Order), state, databaseRowExists: false));

    [Fact]
    public void Order_ExistingRow_RefreshesOriginals()
        => Assert.Equal(
            OrderSyncConcurrencyAction.RefreshOriginals,
            OrderSyncConcurrency.Resolve(typeof(Order), EntityState.Modified, databaseRowExists: true));
}

public class OrderItemSyncApplyTests
{
    [Fact]
    public void HasRemoteItems_FalseWhenEmptyOrZeroIds()
    {
        Assert.False(OrderItemSyncApply.HasRemoteItems([]));
        Assert.False(OrderItemSyncApply.HasRemoteItems(
        [
            new FourSellerOrderItemDto { OrderItemId = 0, Title = "ghost" }
        ]));
    }

    [Fact]
    public void HasRemoteItems_TrueWhenAnyPositiveId()
        => Assert.True(OrderItemSyncApply.HasRemoteItems(
        [
            new FourSellerOrderItemDto { OrderItemId = 0 },
            new FourSellerOrderItemDto { OrderItemId = 99 }
        ]));

    [Fact]
    public void IndexByRemoteId_FirstWinsOnDuplicates()
    {
        var first = new OrderItem { FourSellerOrderItemId = 1, Title = "a" };
        var second = new OrderItem { FourSellerOrderItemId = 1, Title = "b" };
        var map = OrderItemSyncApply.IndexByRemoteId([first, second]);

        Assert.Single(map);
        Assert.Same(first, map[1]);
    }
}
