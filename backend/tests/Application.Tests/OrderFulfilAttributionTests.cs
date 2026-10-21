using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Quy kết tài khoản mua hàng cấp đơn (<c>Orders.FulfilAccountId</c>).
///
/// Cột này là bản sao phi chuẩn hoá của tài khoản trên lần mua, tồn tại để danh sách đơn hiện cột
/// «TK mua» mà không phải join bảng con. Nó chỉ sai theo một kiểu và kiểu đó rất khó thấy: sổ cái
/// đọc thẳng <c>SupplierPurchases.FulfilAccountId</c> nên tiền vẫn đúng tài khoản, chỉ thẻ đơn là
/// trống — đúng ca gắn tay billno <c>GSU1SG240001UUP</c> vào đơn <c>577590806038024769</c>.
/// </summary>
public class OrderFulfilAttributionTests
{
    private static readonly Guid ActingUserId = Guid.NewGuid();

    [Fact]
    public async Task Attach_CopiesPurchaseAccountOntoOrder()
    {
        await using var db = NewDb();
        var account = NewAccount("SHE-03");
        var order = NewOrder("577590806038024769");
        var purchase = NewPurchase("GSU1SG240001UUP", 14.33m);
        purchase.FulfilAccountId = account.Id;
        purchase.PaidByUserId = Guid.NewGuid();

        db.FulfilAccounts.Add(account);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        await NewService(db).AttachSupplierPurchaseAsync(purchase.Id, order.Id, ActingUserId);

        var attached = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(account.Id, attached.FulfilAccountId);
        Assert.Equal(purchase.PaidByUserId, attached.PaidByUserId);

        // Sổ cái phải nói cùng một tài khoản với thẻ đơn — hai chỗ lệch nhau là cách đối chiếu sao
        // kê thẻ mất nghĩa.
        var ledger = await db.FulfilTransactions.SingleAsync(t => t.SupplierPurchaseId == purchase.Id);
        Assert.Equal(account.Id, ledger.FulfilAccountId);
    }

    [Fact]
    public async Task Attach_MovesAttributionWithPurchase()
    {
        await using var db = NewDb();
        var account = NewAccount("SHE-03");
        var orderA = NewOrder("ORDER-A");
        var orderB = NewOrder("ORDER-B");
        var purchase = NewPurchase("GSU-MOVE", 15.17m);
        purchase.FulfilAccountId = account.Id;

        db.FulfilAccounts.Add(account);
        db.Orders.AddRange(orderA, orderB);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderA.Id, ActingUserId);
        await service.AttachSupplierPurchaseAsync(purchase.Id, orderB.Id, ActingUserId);

        Assert.Null((await db.Orders.SingleAsync(o => o.Id == orderA.Id)).FulfilAccountId);
        Assert.Equal(account.Id, (await db.Orders.SingleAsync(o => o.Id == orderB.Id)).FulfilAccountId);
    }

    [Fact]
    public async Task Detach_ClearsOrderAttribution()
    {
        await using var db = NewDb();
        var account = NewAccount("SHE-03");
        var order = NewOrder("ORDER-A");
        var purchase = NewPurchase("GSU-ONE", 11.42m);
        purchase.FulfilAccountId = account.Id;

        db.FulfilAccounts.Add(account);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.AttachSupplierPurchaseAsync(purchase.Id, order.Id, ActingUserId);
        await service.DetachSupplierPurchaseAsync(purchase.Id, ActingUserId);

        var detached = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Null(detached.FulfilAccountId);
        Assert.Null(detached.FulfilCardId);
    }

    [Fact]
    public async Task Attach_WithOperatorChosenAccount_FillsUnattributedPurchase()
    {
        await using var db = NewDb();
        var account = NewAccount("SHE-07");
        account.ActiveCardId = null;
        var order = NewOrder("ORDER-A");
        var purchase = NewPurchase("GSU-NOACCT", 8.20m);

        db.FulfilAccounts.Add(account);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        await NewService(db).AttachSupplierPurchaseAsync(
            purchase.Id, order.Id, ActingUserId, account.Id);

        Assert.Equal(account.Id, (await db.SupplierPurchases.SingleAsync(p => p.Id == purchase.Id)).FulfilAccountId);
        Assert.Equal(account.Id, (await db.Orders.SingleAsync(o => o.Id == order.Id)).FulfilAccountId);
    }

    [Fact]
    public async Task Attach_WithConflictingAccount_IsRejected()
    {
        await using var db = NewDb();
        var owning = NewAccount("SHE-03");
        var other = NewAccount("SHE-07");
        var order = NewOrder("ORDER-A");
        var purchase = NewPurchase("GSU-OWNED", 8.20m);
        purchase.FulfilAccountId = owning.Id;

        db.FulfilAccounts.AddRange(owning, other);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            NewService(db).AttachSupplierPurchaseAsync(purchase.Id, order.Id, ActingUserId, other.Id));
    }

    [Fact]
    public async Task Attach_WithUnknownAccount_IsRejected()
    {
        await using var db = NewDb();
        var order = NewOrder("ORDER-A");
        var purchase = NewPurchase("GSU-NOACCT", 8.20m);
        db.Orders.Add(order);
        db.SupplierPurchases.Add(purchase);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            NewService(db).AttachSupplierPurchaseAsync(purchase.Id, order.Id, ActingUserId, Guid.NewGuid()));
    }

    [Fact]
    public void Sync_ReplacesAccountThatNoLongerHasAnyPurchase()
    {
        var gone = Guid.NewGuid();
        var remaining = Guid.NewGuid();
        var order = new Order
        {
            FulfilAccountId = gone,
            FulfilCardId = Guid.NewGuid(),
            SupplierPurchases = [new SupplierPurchase { Id = Guid.NewGuid(), FulfilAccountId = remaining }]
        };

        OrderPurchaseEvidence.SyncAttributionWithPurchases(order);

        Assert.Equal(remaining, order.FulfilAccountId);
    }

    [Fact]
    public void Sync_KeepsAccountStillBackedByAPurchase()
    {
        var kept = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var order = new Order
        {
            FulfilAccountId = kept,
            FulfilCardId = cardId,
            SupplierPurchases =
            [
                new SupplierPurchase { Id = Guid.NewGuid(), FulfilAccountId = Guid.NewGuid() },
                new SupplierPurchase { Id = Guid.NewGuid(), FulfilAccountId = kept }
            ]
        };

        OrderPurchaseEvidence.SyncAttributionWithPurchases(order);

        Assert.Equal(kept, order.FulfilAccountId);
        Assert.Equal(cardId, order.FulfilCardId);
    }

    [Fact]
    public void Sync_PrefersChargedPurchase()
    {
        var chargedAccount = Guid.NewGuid();
        var order = new Order
        {
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    FulfilAccountId = Guid.NewGuid(),
                    IsCharged = false,
                    SupplierPaidAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new SupplierPurchase
                {
                    Id = Guid.NewGuid(),
                    FulfilAccountId = chargedAccount,
                    IsCharged = true,
                    SupplierPaidAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        };

        OrderPurchaseEvidence.SyncAttributionWithPurchases(order);

        Assert.Equal(chargedAccount, order.FulfilAccountId);
    }

    [Fact]
    public void Sync_LeavesAttributionAloneWhenNoPurchaseKnowsAnAccount()
    {
        var chosen = Guid.NewGuid();
        var order = new Order
        {
            FulfilAccountId = chosen,
            SupplierPurchases = [new SupplierPurchase { Id = Guid.NewGuid() }]
        };

        // Không lần mua nào biết tài khoản: quy kết đang lưu là thứ duy nhất còn lại (người xử lý
        // nhập tay qua Cập nhật phí), gỡ nó đi là mất thông tin mà không thu lại được gì.
        OrderPurchaseEvidence.SyncAttributionWithPurchases(order);

        Assert.Equal(chosen, order.FulfilAccountId);
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>Chỉ nhánh gắn/gỡ tay chạy ở đây và nó chỉ chạm DbContext — xem
    /// <see cref="SupplierPurchaseAttachLedgerTests"/>.</summary>
    private static OrderSyncService NewService(IApplicationDbContext db) =>
        new(db, null!, null!, null!, null!, null!, new NullFulfilQueueRealtimePublisher(), NullLogger<OrderSyncService>.Instance);

    private static FulfilAccount NewAccount(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name
    };

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
