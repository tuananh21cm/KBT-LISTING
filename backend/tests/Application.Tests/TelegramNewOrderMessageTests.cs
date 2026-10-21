using Domain.Entities;
using Infrastructure.Integrations.Telegram;
using Xunit;

namespace Application.Tests;

public class TelegramNewOrderMessageTests
{
    [Fact]
    public void BuildNewOrderMessage_IncludesSellerProfileAndShop()
    {
        var ownerId = Guid.NewGuid();
        var shop = new Shop
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = "BE Off-Road",
            ProfileName = "KBT_SHOP_138"
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ShopId = shop.Id,
            PlatformOrderId = "577588627535729585",
            Amount = 37.41m,
            TotalPaidAmount = 37.41m,
            OrderCreateTime = new DateTime(2026, 9, 26, 3, 50, 0, DateTimeKind.Utc),
            Items =
            [
                new OrderItem { Title = "CovetEZ Women's V-Neck L", ItemPrice = 37.41m }
            ]
        };

        var text = TelegramNotifier.BuildNewOrderMessage(
            order,
            shop,
            shopNameFallback: null,
            managerFullName: "Minh",
            _: false);

        Assert.Contains("Minh", text);
        Assert.Contains("vừa có đơn hàng mới", text);
        Assert.Contains("KBT_SHOP_138", text);
        Assert.Contains("BE Off-Road", text);
        Assert.Contains("577588627535729585", text);
        Assert.DoesNotContain("• — • —", text);
    }

    [Fact]
    public void ShouldAttemptNewOrderNotify_RequiresResolvedShop()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Amount = 10m,
            Items = [new OrderItem { Title = "Product", ItemPrice = 10m }]
        };

        Assert.False(TelegramNotifier.ShouldAttemptNewOrderNotify(order, shop: null));

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = order.OwnerId, Name = "Shop" };
        Assert.True(TelegramNotifier.ShouldAttemptNewOrderNotify(order, shop));

        order.TelegramNewOrderNotifiedAt = DateTime.UtcNow;
        Assert.False(TelegramNotifier.ShouldAttemptNewOrderNotify(order, shop));
    }
}
