using Application.DTOs.Order;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Extension tab «Đã mua»: ListAsync + PurchasedToday + PaidByUserId chỉ trả đơn của fulfil đó.
/// </summary>
public class OrderPurchasedTodayListTests
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OwnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task List_PurchasedToday_PaidByUserId_ReturnsOnlyThatBuyer()
    {
        await using var db = await SeedAsync();
        var svc = NewService(db);

        var mine = await svc.ListAsync(new OrderListQuery
        {
            PurchasedToday = true,
            PaidByUserId = UserA,
            PageSize = 50,
            SortDir = "desc",
        });

        Assert.Equal(1, mine.Total);
        var only = Assert.Single(mine.Items);
        Assert.Equal(UserA, only.PaidByUserId);
        Assert.Equal("ORD-A", only.PlatformOrderId);

        var all = await svc.ListAsync(new OrderListQuery
        {
            PurchasedToday = true,
            PageSize = 50,
            SortDir = "desc",
        });
        Assert.Equal(2, all.Total);
        Assert.Contains(all.Items, o => o.PlatformOrderId == "ORD-A");
        Assert.Contains(all.Items, o => o.PlatformOrderId == "ORD-B");
    }

    [Fact]
    public async Task List_PurchasedToday_ExcludesOlderThanTodayVn()
    {
        await using var db = await SeedAsync();
        var old = NewOrder("ORD-OLD", UserA, DateTime.UtcNow.AddDays(-2));
        db.Orders.Add(old);
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var page = await svc.ListAsync(new OrderListQuery
        {
            PurchasedToday = true,
            PaidByUserId = UserA,
            PageSize = 50,
        });

        Assert.DoesNotContain(page.Items, o => o.PlatformOrderId == "ORD-OLD");
        Assert.Equal(1, page.Total);
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = NewDb();
        db.Users.AddRange(
            new ApplicationUser
            {
                Id = UserA,
                UserName = "fulfil-a",
                NormalizedUserName = "FULFIL-A",
                FullName = "Fulfil A",
                Email = "a@test.local",
                NormalizedEmail = "A@TEST.LOCAL",
            },
            new ApplicationUser
            {
                Id = UserB,
                UserName = "fulfil-b",
                NormalizedUserName = "FULFIL-B",
                FullName = "Fulfil B",
                Email = "b@test.local",
                NormalizedEmail = "B@TEST.LOCAL",
            },
            new ApplicationUser
            {
                Id = OwnerId,
                UserName = "seller-owner",
                NormalizedUserName = "SELLER-OWNER",
                FullName = "Seller Owner",
                Email = "owner@test.local",
                NormalizedEmail = "OWNER@TEST.LOCAL",
            });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.Orders.Add(NewOrder("ORD-A", UserA, now));
        db.Orders.Add(NewOrder("ORD-B", UserB, now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        return db;
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static OrderSyncService NewService(IApplicationDbContext db) =>
        new(
            db,
            null!,
            null!,
            new StubSettings(),
            null!,
            null!,
            new NullFulfilQueueRealtimePublisher(),
            NullLogger<OrderSyncService>.Instance);

    private static Order NewOrder(string platformOrderId, Guid paidByUserId, DateTime purchasedAt) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = OwnerId,
        PlatformOrderId = platformOrderId,
        BuyerPhone = "8312295220",
        PostalCode = "93960",
        BuyerName = "Test Buyer",
        OrderCreateTime = purchasedAt.AddHours(-2),
        PlatformOrderStatus = "AWAITING_SHIPMENT",
        OrderStatus = "to_ship",
        Currency = "USD",
        FulfilPurchasedAt = purchasedAt,
        PaidByUserId = paidByUserId,
    };

    private sealed class StubSettings : ISettingProvider
    {
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<Dictionary<string, string>> GetValuesAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
