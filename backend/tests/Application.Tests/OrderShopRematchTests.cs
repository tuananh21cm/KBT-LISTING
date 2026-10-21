using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

/// <summary>
/// <see cref="OrderSyncService.RematchOrderShopLinksAsync"/> must restore <c>ShopId</c> from
/// current or previous 4Seller shop ids without rewriting <c>OwnerId</c>.
/// </summary>
public class OrderShopRematchTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly OrderSyncService _orders;

    public OrderShopRematchTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _orders = new OrderSyncService(
            _db, null!, null!, null!, null!, null!, new NullFulfilQueueRealtimePublisher(),
            NullLogger<OrderSyncService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Rematch_UsesPreviousFourSellerShopId_DoesNotChangeOwner()
    {
        var orderOwner = Guid.NewGuid();
        var shopOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(shopOwner, fourSellerShopId: "241316", previousId: "100");

        _db.Orders.Add(new Order
        {
            OwnerId = orderOwner,
            ShopId = null,
            FourSellerOrderId = 1,
            FourSellerShopId = 100,
            PlatformOrderId = "PO-PREV",
            OrderStatus = "to_ship"
        });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync();
        Assert.Equal(1, changed);

        var order = await _db.Orders.SingleAsync();
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal(orderOwner, order.OwnerId);
    }

    [Fact]
    public async Task Rematch_CrossOwner_ByCurrentFourSellerShopId()
    {
        var orderOwner = Guid.NewGuid();
        var shopOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(shopOwner, fourSellerShopId: "241315", previousId: null);

        _db.Orders.Add(new Order
        {
            OwnerId = orderOwner,
            ShopId = null,
            FourSellerOrderId = 2,
            FourSellerShopId = 241315,
            PlatformOrderId = "PO-CUR",
            OrderStatus = "shipped"
        });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync();
        Assert.Equal(1, changed);

        var order = await _db.Orders.SingleAsync();
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal(orderOwner, order.OwnerId);
    }

    [Fact]
    public async Task Rematch_AlreadyLinkedOrders_TouchesNothing()
    {
        var owner = Guid.NewGuid();
        var shop = await SeedShopAsync(owner, fourSellerShopId: "241315", previousId: "100");

        var updatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.Orders.Add(new Order
        {
            OwnerId = owner,
            ShopId = shop.Id,
            FourSellerOrderId = 4,
            FourSellerShopId = 241315,
            PlatformOrderId = "PO-OK",
            OrderStatus = "shipped",
            UpdatedAt = updatedAt
        });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync();
        Assert.Equal(0, changed);

        var order = await _db.Orders.SingleAsync();
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal(updatedAt, order.UpdatedAt);
    }

    [Fact]
    public async Task Rematch_SkipsAmbiguousFourSellerShopId()
    {
        var owner = Guid.NewGuid();
        await SeedShopAsync(owner, fourSellerShopId: "50", previousId: null, name: "A");
        await SeedShopAsync(owner, fourSellerShopId: "50", previousId: null, name: "B");

        _db.Orders.Add(new Order
        {
            OwnerId = owner,
            ShopId = null,
            FourSellerOrderId = 3,
            FourSellerShopId = 50,
            PlatformOrderId = "PO-AMB",
            OrderStatus = "to_ship"
        });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync();
        Assert.Equal(0, changed);
        Assert.Null(await _db.Orders.Select(o => o.ShopId).SingleAsync());
    }

    [Fact]
    public async Task Rematch_ScopedToOneShop_LinksOwnIdsAndLeavesOtherShopsAlone()
    {
        var orderOwner = Guid.NewGuid();
        var shopOwner = Guid.NewGuid();
        var target = await SeedShopAsync(shopOwner, fourSellerShopId: "241316", previousId: "100");
        var other = await SeedShopAsync(shopOwner, fourSellerShopId: "241315", previousId: null);

        _db.Orders.AddRange(
            new Order
            {
                OwnerId = orderOwner,
                ShopId = null,
                FourSellerOrderId = 10,
                FourSellerShopId = 241316,
                PlatformOrderId = "PO-TARGET-CUR",
                OrderStatus = "shipped"
            },
            new Order
            {
                OwnerId = orderOwner,
                ShopId = null,
                FourSellerOrderId = 11,
                FourSellerShopId = 100,
                PlatformOrderId = "PO-TARGET-PREV",
                OrderStatus = "shipped"
            },
            new Order
            {
                OwnerId = orderOwner,
                ShopId = null,
                FourSellerOrderId = 12,
                FourSellerShopId = 241315,
                PlatformOrderId = "PO-OTHER",
                OrderStatus = "shipped"
            });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync(shopId: target.Id);
        Assert.Equal(2, changed);

        var byPlatformOrderId = await _db.Orders.ToDictionaryAsync(o => o.PlatformOrderId!, o => o.ShopId);
        Assert.Equal(target.Id, byPlatformOrderId["PO-TARGET-CUR"]);
        Assert.Equal(target.Id, byPlatformOrderId["PO-TARGET-PREV"]);
        Assert.Null(byPlatformOrderId["PO-OTHER"]);
        Assert.NotEqual(target.Id, other.Id);
    }

    [Fact]
    public async Task Rematch_ScopedToOneShop_SkipsIdClaimedByTwoShops()
    {
        var owner = Guid.NewGuid();
        var target = await SeedShopAsync(owner, fourSellerShopId: "50", previousId: null, name: "A");
        await SeedShopAsync(owner, fourSellerShopId: "50", previousId: null, name: "B");

        _db.Orders.Add(new Order
        {
            OwnerId = owner,
            ShopId = null,
            FourSellerOrderId = 13,
            FourSellerShopId = 50,
            PlatformOrderId = "PO-AMB-SCOPED",
            OrderStatus = "to_ship"
        });
        await _db.SaveChangesAsync();

        var changed = await _orders.RematchOrderShopLinksAsync(shopId: target.Id);
        Assert.Equal(0, changed);
        Assert.Null(await _db.Orders.Select(o => o.ShopId).SingleAsync());
    }

    private async Task<Shop> SeedShopAsync(
        Guid ownerId,
        string fourSellerShopId,
        string? previousId,
        string? name = null)
    {
        var platformId = Guid.NewGuid();
        _db.Platforms.Add(new Platform
        {
            Id = platformId,
            Code = "TIKTOK",
            Name = "TikTok",
            IsActive = true
        });
        var shop = new Shop
        {
            OwnerId = ownerId,
            Name = name ?? $"Shop-{fourSellerShopId}",
            PlatformId = platformId,
            FourSellerShopId = fourSellerShopId,
            PreviousFourSellerShopId = previousId,
            Status = "Active"
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();
        return shop;
    }
}
