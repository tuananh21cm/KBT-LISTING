using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// <c>ApplicationDbContext.SaveChangesAsync</c> là chốt cuối của snapshot giá mua
/// <c>Orders.Charged*</c>. Trước đây snapshot phải gọi tay ở ~8 chỗ, và vì
/// <c>Order.SupplierPurchases</c> khởi tạo sẵn list rỗng nên không chỗ nào phân biệt được "đơn
/// không có lần mua" với "navigation chưa nạp" — một lần gọi từ navigation rỗng oan xoá sạch giá
/// mua của đơn và không lượt sync nào tự sửa lại.
/// </summary>
public class OrderChargedAggregatesSaveChangesTests
{
    [Fact]
    public async Task SaveChanges_MaterializesChargedColumnsFromPurchaseMoney()
    {
        var options = NewOptions();
        var orderId = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            var order = NewOrder(orderId);
            order.SupplierPurchases.Add(NewPurchase(orderId));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var order = await db.Orders
                .Include(o => o.SupplierPurchases)
                .FirstAsync(o => o.Id == orderId);

            Assert.True(order.SupplierPurchases.Single().IsCharged);
            Assert.Equal(15.14m, order.ChargedSupplierSubtotal);
            Assert.Equal(18.11m, order.ChargedSupplierTotal);
            Assert.Equal(18.11m, order.ChargedTrueSpend);
            Assert.Equal(20.38m, OrderSupplierCost.ResolveOrderTotalCost(order));
        }
    }

    /// <summary>
    /// Ca đã gây lỗi thật trên 9 đơn: một luồng ghi nạp đơn mà không <c>Include</c> link rồi
    /// materialize snapshot, xoá sạch giá mua. Hook phải tính lại từ tập link nạp đủ.
    /// </summary>
    [Fact]
    public async Task SaveChanges_RestoresSnapshotClearedFromUnloadedNavigation()
    {
        var options = NewOptions();
        var orderId = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            var order = NewOrder(orderId);
            order.SupplierPurchases.Add(NewPurchase(orderId));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            // Không Include: navigation rỗng, nên ApplyAggregates kết luận "đơn không có lần mua".
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            Assert.Empty(order.SupplierPurchases);

            OrderSupplierCost.ApplyAggregates(order);
            Assert.Null(order.ChargedSupplierSubtotal);

            await db.SaveChangesAsync();

            Assert.Equal(15.14m, order.ChargedSupplierSubtotal);
            Assert.Equal(20.38m, OrderSupplierCost.ResolveOrderTotalCost(order));
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            Assert.Equal(15.14m, order.ChargedSupplierSubtotal);
            Assert.Equal(18.11m, order.ChargedSupplierTotal);
        }
    }

    /// <summary>
    /// Breakdown đến muộn (phase detail sau khi đơn đã khớp từ list sparse) vẫn phải vào snapshot,
    /// dù luồng sync không chạy lại nhánh Attach.
    /// </summary>
    [Fact]
    public async Task SaveChanges_PicksUpMoneyArrivingAfterTheLinkWasAttached()
    {
        var options = NewOptions();
        var orderId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            var order = NewOrder(orderId);
            var sparse = NewPurchase(orderId, purchaseId);
            sparse.SupplierSubtotal = null;
            sparse.SupplierShippingFee = null;
            sparse.SupplierTax = null;
            sparse.SupplierTotal = null;
            order.SupplierPurchases.Add(sparse);
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            Assert.Null(order.ChargedSupplierSubtotal);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var purchase = await db.SupplierPurchases.FirstAsync(p => p.Id == purchaseId);
            purchase.SupplierSubtotal = 15.14m;
            purchase.SupplierShippingFee = 3.99m;
            purchase.SupplierTax = 1.25m;
            purchase.SupplierTotal = 18.11m;
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            Assert.Equal(20.38m, OrderSupplierCost.ResolveOrderTotalCost(order));
        }
    }

    /// <summary>Gỡ lần mua cuối là đơn mất giá mua thật — hook không được giữ số cũ.</summary>
    [Fact]
    public async Task SaveChanges_ClearsSnapshotWhenLastPurchaseIsDeleted()
    {
        var options = NewOptions();
        var orderId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            var order = NewOrder(orderId);
            order.SupplierPurchases.Add(NewPurchase(orderId, purchaseId));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            db.SupplierPurchases.Remove(
                await db.SupplierPurchases.FirstAsync(p => p.Id == purchaseId));
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            Assert.Null(order.ChargedSupplierSubtotal);
            Assert.Null(order.ChargedSupplierTotal);
            Assert.Null(order.ChargedTrueSpend);
            Assert.Null(OrderSupplierCost.ResolveOrderTotalCost(order));
        }
    }

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static Order NewOrder(Guid id) => new()
    {
        Id = id,
        OwnerId = Guid.NewGuid(),
        PlatformOrderId = "577592679686312424",
        BuyerPhone = "8312295220",
        PostalCode = "93960",
        BuyerName = "Test Buyer",
        OrderCreateTime = DateTime.UtcNow.AddHours(-2),
        PlatformOrderStatus = "AWAITING_SHIPMENT",
        OrderStatus = "to_ship",
        Currency = "USD",
    };

    private static SupplierPurchase NewPurchase(Guid orderId, Guid? id = null)
    {
        var now = DateTime.UtcNow;
        return new SupplierPurchase
        {
            Id = id ?? Guid.NewGuid(),
            OrderId = orderId,
            Source = SupplierPurchaseSources.Shein,
            MatchState = SupplierPurchaseMatchStates.Matched,
            SupplierOrderNo = "GSU1SH28T00NJKR",
            SupplierStatus = SupplierStatusGroups.Shipped,
            SupplierSubtotal = 15.14m,
            SupplierShippingFee = 3.99m,
            SupplierTax = 1.25m,
            SupplierCouponPrice = 2.27m,
            SupplierTotal = 18.11m,
            FirstSeenAt = now,
            LastSeenAt = now,
            CreatedAt = now,
        };
    }
}
