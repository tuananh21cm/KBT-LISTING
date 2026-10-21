using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Orders;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Ứng viên của vòng quét chi tiết SHEIN chạy trên backend.
///
/// Hai nhóm phải vào: link chưa có mã last-mile thật (cần <c>get_order_info</c> để đọc
/// <c>goods_pkg_rel_list[].shipping_no</c>) và link đã có mã nhưng chưa Mark Ship xong. Sai
/// hướng nào cũng đắt: bỏ sót thì mã vận đơn không bao giờ lên sàn, lấy quá thì đốt request
/// SHEIN cho những đơn đã xong.
/// </summary>
public class SheinDetailSweepCandidateTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 2, 4, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _db;

    public SheinDetailSweepCandidateTests()
    {
        SheinOrderDetailSweepService.ResetCycleStateForTests();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        SheinOrderDetailSweepService.ResetCycleStateForTests();
        _db.Dispose();
    }

    [Fact]
    public async Task NeedsCarrierTracking_PicksWarehouseOnlyLink()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        AddLink(order, "SHEIN-WAREHOUSE", SupplierTrackingKinds.Warehouse, "CN123456789");

        var picked = await QueryNeedsTrackingAsync();

        Assert.Equal(["SHEIN-WAREHOUSE"], picked);
    }

    [Fact]
    public async Task NeedsCarrierTracking_PicksLinkWithNoTrackingAtAll()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        AddLink(order, "SHEIN-EMPTY", SupplierTrackingKinds.None, null);

        var picked = await QueryNeedsTrackingAsync();

        Assert.Equal(["SHEIN-EMPTY"], picked);
    }

    [Fact]
    public async Task NeedsCarrierTracking_SkipsOrderAlreadyPublishedOnPlatform()
    {
        // Sàn đã có mã (mark-ship tay hoặc lượt trước) → get_order_info không đổi được gì nữa.
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment, platformTracking: "9400100000000000000000");
        AddLink(order, "SHEIN-DONE", SupplierTrackingKinds.Warehouse, "CN123456789");

        Assert.Empty(await QueryNeedsTrackingAsync());
    }

    [Fact]
    public async Task NeedsCarrierTracking_SkipsNonRevenueStatuses()
    {
        foreach (var status in new[]
                 {
                     PlatformOrderStatuses.Cancelled,
                     PlatformOrderStatuses.Refund,
                     PlatformOrderStatuses.Delivered,
                     PlatformOrderStatuses.InTransit
                 })
        {
            var order = AddOrder(status);
            AddLink(order, $"SHEIN-{status}", SupplierTrackingKinds.Warehouse, "CN1");
        }

        Assert.Empty(await QueryNeedsTrackingAsync());
    }

    [Fact]
    public async Task NeedsCarrierTracking_SkipsLinkThatAlreadyHasCarrierTracking()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        AddLink(order, "SHEIN-CARRIER", SupplierTrackingKinds.Carrier, "9400100000000000000000");

        // Đã có mã thật → thuộc nhóm chờ mark-ship, không phải nhóm thiếu tracking.
        Assert.Empty(await QueryNeedsTrackingAsync());
        Assert.Equal(["SHEIN-CARRIER"], await QueryPendingMarkShipAsync());
    }

    [Fact]
    public async Task PendingMarkShip_SkipsLinkAlreadyPublished()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        var link = AddLink(order, "SHEIN-SHIPPED", SupplierTrackingKinds.Carrier, "9400100000000000000000");
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        await _db.SaveChangesAsync();

        Assert.Empty(await QueryPendingMarkShipAsync());
        Assert.Empty(await QueryNeedsTrackingAsync());
    }

    [Fact]
    public async Task PendingMarkShip_KeepsFailedLinkOnlyAfterCooldown()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        var link = AddLink(order, "SHEIN-RETRY", SupplierTrackingKinds.Carrier, "9400100000000000000000");
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        link.AutoMarkShipAttemptedAt = Now.AddMinutes(-1);
        await _db.SaveChangesAsync();

        Assert.Empty(await QueryPendingMarkShipAsync());

        link.AutoMarkShipAttemptedAt = Now - OrderAutoMarkShipPolicy.RetryCooldown.Add(TimeSpan.FromMinutes(1));
        await _db.SaveChangesAsync();

        Assert.Equal(["SHEIN-RETRY"], await QueryPendingMarkShipAsync());
    }

    [Fact]
    public async Task BothGroupsAreDisjoint()
    {
        var awaiting = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        AddLink(awaiting, "SHEIN-A", SupplierTrackingKinds.Warehouse, "CN1");

        var pending = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        AddLink(pending, "SHEIN-B", SupplierTrackingKinds.Carrier, "9400100000000000000000");

        var needsTracking = await QueryNeedsTrackingAsync();
        var pendingMarkShip = await QueryPendingMarkShipAsync();

        Assert.Equal(["SHEIN-A"], needsTracking);
        Assert.Equal(["SHEIN-B"], pendingMarkShip);
        Assert.Empty(needsTracking.Intersect(pendingMarkShip));
    }

    [Fact]
    public async Task PendingMarkShip_SkipsTikTokShippingOrders()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        order.ShippingMethod = "TikTok Shipping";
        AddLink(order, "SHEIN-TT-SHIP", SupplierTrackingKinds.Carrier, "9400100000000000000000");
        await _db.SaveChangesAsync();

        Assert.Empty(await QueryPendingMarkShipAsync());
    }

    [Fact]
    public async Task PendingMarkShip_KeepsSellerShippingOrders()
    {
        var order = AddOrder(PlatformOrderStatuses.AwaitingShipment);
        order.ShippingMethod = "Seller Shipping";
        AddLink(order, "SHEIN-SELLER", SupplierTrackingKinds.Carrier, "9400100000000000000000");
        await _db.SaveChangesAsync();

        Assert.Equal(["SHEIN-SELLER"], await QueryPendingMarkShipAsync());
    }

    [Fact]
    public void BuildCandidateSnapshot_IncludesAllAndPutsPendingFirst()
    {
        // LastSeenAt "mới" không được đẩy đơn cũ ra khỏi snapshot — snapshot lấy hết.
        var pending = new[]
        {
            new SheinOrderDetailSweepService.CandidateRow("PEND-OLD", null, Now.AddDays(-2)),
            new SheinOrderDetailSweepService.CandidateRow("PEND-NEW", null, Now.AddHours(-1)),
        };
        var needs = new[]
        {
            new SheinOrderDetailSweepService.CandidateRow("NEED-FRESH", null, Now),
            new SheinOrderDetailSweepService.CandidateRow("NEED-OLD", null, Now.AddDays(-5)),
            new SheinOrderDetailSweepService.CandidateRow("NEED-MID", null, Now.AddDays(-1)),
        };

        var snap = SheinOrderDetailSweepService.BuildCandidateSnapshot(pending, needs);

        Assert.Equal(5, snap.Count);
        Assert.Equal(["PEND-OLD", "PEND-NEW", "NEED-FRESH", "NEED-OLD", "NEED-MID"],
            snap.Select(r => r.SupplierOrderNo).ToList());
    }

    [Fact]
    public void BuildCandidateSnapshot_DedupesPreferringPendingGroup()
    {
        var pending = new[]
        {
            new SheinOrderDetailSweepService.CandidateRow("SHARED", Guid.Parse("11111111-1111-1111-1111-111111111111"), Now),
        };
        var needs = new[]
        {
            new SheinOrderDetailSweepService.CandidateRow("SHARED", Guid.Parse("22222222-2222-2222-2222-222222222222"), Now.AddDays(-1)),
            new SheinOrderDetailSweepService.CandidateRow("ONLY-NEED", null, Now),
        };

        var snap = SheinOrderDetailSweepService.BuildCandidateSnapshot(pending, needs);

        Assert.Equal(2, snap.Count);
        Assert.Equal("SHARED", snap[0].SupplierOrderNo);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), snap[0].FulfilAccountId);
        Assert.Equal("ONLY-NEED", snap[1].SupplierOrderNo);
    }

    [Fact]
    public void BuildCandidateSnapshot_DoesNotTruncateBeyondMaxBillnos()
    {
        var needs = Enumerable.Range(1, 40)
            .Select(i => new SheinOrderDetailSweepService.CandidateRow(
                $"B{i:D3}", null, Now.AddMinutes(-i)))
            .ToList();

        var snap = SheinOrderDetailSweepService.BuildCandidateSnapshot([], needs);

        Assert.Equal(40, snap.Count);
        // Chunking is the caller's job — snapshot itself keeps everyone.
        var batches = snap
            .Select((r, i) => (r, i))
            .GroupBy(x => x.i / 15)
            .Select(g => g.Count())
            .ToList();
        Assert.Equal([15, 15, 10], batches);
    }

    [Fact]
    public void CycleState_ResetAllowsFreshDue()
    {
        SheinOrderDetailSweepService.SetCycleCompletedAtForTests(DateTime.UtcNow);
        SheinOrderDetailSweepService.ResetCycleStateForTests();
        // After reset, completedAt is MinValue — RunIfDue treats cycle as due (when enabled).
        SheinOrderDetailSweepService.SetCycleCompletedAtForTests(DateTime.UtcNow.AddMinutes(-1));
        SheinOrderDetailSweepService.ResetCycleStateForTests();
    }

    private Task<List<string>> QueryNeedsTrackingAsync() =>
        _db.SupplierPurchases
            .AsNoTracking()
            .Where(SheinOrderDetailSweepService.NeedsCarrierTrackingPredicate())
            .Select(l => l.SupplierOrderNo!)
            .ToListAsync();

    private Task<List<string>> QueryPendingMarkShipAsync() =>
        _db.SupplierPurchases
            .AsNoTracking()
            .Where(OrderAutoMarkShipPolicy.PendingQueuePredicate(Now))
            .Select(l => l.SupplierOrderNo!)
            .ToListAsync();

    private Order AddOrder(string platformStatus, string? platformTracking = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            PlatformOrderStatus = platformStatus,
            TrackingNo = platformTracking
        };
        _db.Orders.Add(order);
        _db.SaveChanges();
        return order;
    }

    private SupplierPurchase AddLink(Order order, string billno, string trackingKind, string? trackingNo)
    {
        var link = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            SupplierOrderNo = billno,
            TrackingKind = trackingKind,
            TrackingNo = trackingNo,
            LastSeenAt = Now,
            FirstSeenAt = Now
        };
        _db.SupplierPurchases.Add(link);
        _db.SaveChanges();
        return link;
    }
}
