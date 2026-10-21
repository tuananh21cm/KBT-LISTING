using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Chuyển một lần mua NCC đã tính tiền sang đơn khác trong **một** thao tác (nút Chuyển đơn ở sổ
/// cái Lịch sử mua hàng).
///
/// Dòng chi phải đi theo lần mua chứ không được xoá rồi dựng lại: unique
/// <c>IX_FulfilTransactions_PurchaseId_CostUpdate</c> là theo lần mua, nên nếu đơn cũ xoá dòng
/// trước thì đơn mới tìm lại đúng entity đó nhưng nó đã ở state <c>Deleted</c> — một khoản đã quẹt
/// thẻ thật biến mất khỏi sổ cái trong khi giá mua vẫn nằm trên đơn.
/// </summary>
public class SupplierPurchaseAttachLedgerTests
{
    private static readonly Guid ActingUserId = Guid.NewGuid();

    [Fact]
    public async Task Attach_ToAnotherOrder_MovesLedgerRowInsteadOfDroppingIt()
    {
        await using var db = NewDb();
        var orderA = NewOrder("ORDER-A");
        var orderB = NewOrder("ORDER-B");
        var purchase = NewPurchase("GSU1S803500232X", 15.17m);
        db.Orders.AddRange(orderA, orderB);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderA.Id, ActingUserId);

        var ledgerId = await db.FulfilTransactions
            .Where(t => t.SupplierPurchaseId == purchase.Id)
            .Select(t => t.Id)
            .SingleAsync();

        await service.AttachSupplierPurchaseAsync(purchase.Id, orderB.Id, ActingUserId);

        var rows = await db.FulfilTransactions
            .Where(t => t.SupplierPurchaseId == purchase.Id)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(ledgerId, row.Id);
        Assert.Equal(orderB.Id, row.OrderId);
        Assert.Equal(15.17m, row.Amount);
        Assert.Equal(FulfilTransaction.SourceCostUpdate, row.Source);
        Assert.Equal(purchase.SupplierPaidAt, row.OccurredAt);

        // Đơn cũ mất cả giá mua lẫn dòng tiền — cùng một khoản chi không được nằm trên hai đơn.
        var movedFrom = await db.Orders.SingleAsync(o => o.Id == orderA.Id);
        Assert.Null(movedFrom.ChargedSupplierTotal);
        Assert.Empty(await db.FulfilTransactions.Where(t => t.OrderId == orderA.Id).ToListAsync());

        var movedTo = await db.Orders.SingleAsync(o => o.Id == orderB.Id);
        Assert.Equal(15.17m, movedTo.ChargedSupplierTotal);
    }

    [Fact]
    public async Task Attach_StampsFulfilPurchasedAt_OnTarget_AndClearsSource()
    {
        await using var db = NewDb();
        var paidAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc);
        var orderA = NewOrder("ORDER-A");
        orderA.FulfilPurchasedAt = paidAt;
        orderA.FulfilTimelineSources = "{\"fulfilPurchasedAt\":\"shein\"}";
        var orderB = NewOrder("ORDER-B");
        var purchase = NewPurchase("GSU1S803500232X", 15.17m);
        purchase.SupplierPaidAt = paidAt;
        purchase.OrderedAt = paidAt;

        db.Orders.AddRange(orderA, orderB);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderA.Id, ActingUserId);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderB.Id, ActingUserId);

        var movedFrom = await db.Orders.SingleAsync(o => o.Id == orderA.Id);
        var movedTo = await db.Orders.SingleAsync(o => o.Id == orderB.Id);

        Assert.Null(movedFrom.FulfilPurchasedAt);
        Assert.Equal(paidAt, movedTo.FulfilPurchasedAt);
    }

    [Fact]
    public async Task Attach_KeepsManualFulfilPurchasedAt_OnSource()
    {
        await using var db = NewDb();
        var paidAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc);
        var manualAt = new DateTime(2026, 9, 19, 5, 0, 0, DateTimeKind.Utc);
        var orderA = NewOrder("ORDER-A");
        orderA.FulfilPurchasedAt = manualAt;
        orderA.FulfilTimelineSources = "{\"fulfilPurchasedAt\":\"manual\"}";
        var orderB = NewOrder("ORDER-B");
        var purchase = NewPurchase("GSU1S803500232X", 15.17m);
        purchase.SupplierPaidAt = paidAt;
        purchase.OrderedAt = paidAt;

        db.Orders.AddRange(orderA, orderB);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderA.Id, ActingUserId);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderB.Id, ActingUserId);

        var movedFrom = await db.Orders.SingleAsync(o => o.Id == orderA.Id);
        Assert.Equal(manualAt, movedFrom.FulfilPurchasedAt);
    }

    [Fact]
    public async Task Attach_KeepsSourcePurchasedAt_WhenAnotherPaidPurchaseRemains()
    {
        await using var db = NewDb();
        var paidAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc);
        var orderA = NewOrder("ORDER-A");
        orderA.FulfilPurchasedAt = paidAt;
        orderA.FulfilTimelineSources = "{\"fulfilPurchasedAt\":\"shein\"}";
        var orderB = NewOrder("ORDER-B");
        var moving = NewPurchase("GSU-MOVE", 15.17m);
        moving.SupplierPaidAt = paidAt;
        moving.OrderedAt = paidAt;
        var staying = NewPurchase("GSU-STAY", 9.99m);
        staying.SupplierPaidAt = paidAt.AddMinutes(1);
        staying.OrderedAt = paidAt.AddMinutes(1);

        db.Orders.AddRange(orderA, orderB);
        db.SupplierPurchases.AddRange(moving, staying);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(staying.Id, orderA.Id, ActingUserId);
        await service.AttachSupplierPurchaseAsync(moving.Id, orderA.Id, ActingUserId);
        await service.AttachSupplierPurchaseAsync(moving.Id, orderB.Id, ActingUserId);

        var source = await db.Orders.SingleAsync(o => o.Id == orderA.Id);
        Assert.Equal(paidAt, source.FulfilPurchasedAt);
        Assert.Contains(await db.SupplierPurchases.Where(p => p.OrderId == orderA.Id).ToListAsync(),
            p => p.Id == staying.Id);
    }

    [Fact]
    public async Task Attach_MarksManualLink_AndDetachClearsIt()
    {
        await using var db = NewDb();
        var order = NewOrder("ORDER-A");
        var purchase = NewPurchase("GSU1S821500MYUA", 11.42m);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, order.Id, ActingUserId);
        Assert.NotNull((await db.SupplierPurchases.SingleAsync(p => p.Id == purchase.Id)).ManualLinkedAt);

        await service.DetachSupplierPurchaseAsync(purchase.Id, ActingUserId);

        var detached = await db.SupplierPurchases.SingleAsync(p => p.Id == purchase.Id);
        Assert.Null(detached.OrderId);
        Assert.Null(detached.ManualLinkedAt);
        Assert.Empty(await db.FulfilTransactions.ToListAsync());
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Chỉ nhánh gắn/gỡ tay được chạy ở đây và nó chỉ chạm DbContext, nên các phụ thuộc còn lại để
    /// <c>null</c>: một stub rỗng cho sáu interface kia chỉ làm test dài ra mà không kiểm gì thêm.
    /// </summary>
    private static OrderSyncService NewService(IApplicationDbContext db) =>
        new(db, null!, null!, null!, null!, null!, new NullFulfilQueueRealtimePublisher(), NullLogger<OrderSyncService>.Instance);

    private static Order NewOrder(string platformOrderId) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        PlatformOrderId = platformOrderId,
        BuyerPhone = "8312295220",
        PostalCode = "93960",
        OrderCreateTime = new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
        Currency = "USD"
    };

    private static SupplierPurchase NewPurchase(string billno, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        SupplierOrderNo = billno,
        Source = SupplierPurchaseSources.Shein,
        MatchState = SupplierPurchaseMatchStates.Unmatched,
        SupplierStatus = SupplierStatusGroups.Processing,
        SupplierSubtotal = total,
        SupplierTotal = total,
        Currency = "USD",
        SupplierPaidAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc),
        OrderedAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc)
    };
}
