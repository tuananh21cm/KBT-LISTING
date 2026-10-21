using System.Reflection;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Per-order 4Seller commerce status refresh (exact orderNo — bypasses lookback).
/// </summary>
public class OrderPlatformStatusSyncTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _ownerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public OrderPlatformStatusSyncTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Users.Add(new ApplicationUser
        {
            Id = _ownerId,
            UserName = "seller",
            NormalizedUserName = "SELLER",
            Email = "seller@test.local",
            NormalizedEmail = "SELLER@TEST.LOCAL",
            FullName = "Seller"
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SyncPlatformStatus_UpdatesToInTransit_AndStampsLastSynced()
    {
        var order = SeedOrder(
            platformStatus: PlatformOrderStatuses.AwaitingCollection,
            trackingNo: "GFUS01075219592451");
        var beforeLastSynced = order.LastSyncedAt;

        var fourSeller = StubFourSeller.Create(recordForStatus: "shipped", record: new FourSellerOrderRecordDto
        {
            OrderId = order.FourSellerOrderId,
            PlatformOrderId = order.PlatformOrderId,
            PlatformOrderNo = order.PlatformOrderId,
            OrderStatus = "shipped",
            PlatformOrderStatus = PlatformOrderStatuses.InTransit,
            TrackingNo = "GFUS01075219592451",
            ShippingServiceInfo = new FourSellerOrderShippingServiceDto { ShippingService = "GOFO." },
            RawJson = "{\"platformOrderStatus\":\"IN_TRANSIT\"}"
        });

        var svc = NewService(fourSeller);
        var result = await svc.SyncPlatformStatusAsync(order.Id);

        Assert.True(result.Found);
        Assert.True(result.Changed);
        Assert.Equal(PlatformOrderStatuses.AwaitingCollection, result.PreviousPlatformOrderStatus);
        Assert.Equal(PlatformOrderStatuses.InTransit, result.PlatformOrderStatus);
        Assert.Equal(OrderWorkflowTabs.InTransit, result.WorkflowTab);
        Assert.Contains("Đang giao", result.Summary, StringComparison.Ordinal);

        var reloaded = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(PlatformOrderStatuses.InTransit, reloaded.PlatformOrderStatus);
        Assert.Equal(OrderWorkflowTabs.InTransit, reloaded.WorkflowTab);
        Assert.True(reloaded.LastSyncedAt > beforeLastSynced);
    }

    [Fact]
    public async Task SyncPlatformStatus_LatchesRefund_WhenRemoteReportsCompleted()
    {
        var order = SeedOrder(platformStatus: PlatformOrderStatuses.Refund, trackingNo: "1Z999");
        var fourSeller = StubFourSeller.Create(recordForStatus: "shipped", record: new FourSellerOrderRecordDto
        {
            OrderId = order.FourSellerOrderId,
            PlatformOrderId = order.PlatformOrderId,
            OrderStatus = "shipped",
            PlatformOrderStatus = "COMPLETED",
            TrackingNo = "1Z999",
            RawJson = "{\"platformOrderStatus\":\"COMPLETED\"}"
        });

        var result = await NewService(fourSeller).SyncPlatformStatusAsync(order.Id);

        Assert.True(result.Found);
        Assert.Equal(PlatformOrderStatuses.Refund, result.PlatformOrderStatus);

        var reloaded = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(PlatformOrderStatuses.Refund, reloaded.PlatformOrderStatus);
        Assert.Equal(OrderWorkflowTabs.Refund, reloaded.WorkflowTab);
    }

    [Fact]
    public async Task SyncPlatformStatus_NotFound_IsNoOp()
    {
        var order = SeedOrder(
            platformStatus: PlatformOrderStatuses.AwaitingCollection,
            trackingNo: "GFUS1");
        var statusBefore = order.PlatformOrderStatus;
        var lastSyncedBefore = order.LastSyncedAt;
        var trackingBefore = order.TrackingNo;

        var fourSeller = StubFourSeller.Create(recordForStatus: null, record: null);
        var result = await NewService(fourSeller).SyncPlatformStatusAsync(order.Id);

        Assert.False(result.Found);
        Assert.False(result.Changed);
        Assert.Contains("không tìm thấy", result.Summary, StringComparison.OrdinalIgnoreCase);

        var reloaded = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(statusBefore, reloaded.PlatformOrderStatus);
        Assert.Equal(trackingBefore, reloaded.TrackingNo);
        Assert.Equal(lastSyncedBefore, reloaded.LastSyncedAt);
    }

    [Fact]
    public async Task SyncPlatformStatus_DoesNotSeedWarehouseTrackingOntoOrder()
    {
        var order = SeedOrder(
            platformStatus: PlatformOrderStatuses.AwaitingShipment,
            trackingNo: null);
        var fourSeller = StubFourSeller.Create(recordForStatus: "to_ship", record: new FourSellerOrderRecordDto
        {
            OrderId = order.FourSellerOrderId,
            PlatformOrderId = order.PlatformOrderId,
            OrderStatus = "to_ship",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
            TrackingNo = "C1234567890123",
            ShippingServiceInfo = new FourSellerOrderShippingServiceDto
            {
                ShippingService = "International Warehouse"
            },
            RawJson = "{}"
        });

        var result = await NewService(fourSeller).SyncPlatformStatusAsync(order.Id);

        Assert.True(result.Found);
        var reloaded = await _db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.True(string.IsNullOrWhiteSpace(reloaded.TrackingNo));
    }

    [Fact]
    public void MatchRemoteOrderRecord_MatchesPlatformOrderId()
    {
        var match = OrderSyncService.MatchRemoteOrderRecord(
            [
                new FourSellerOrderRecordDto
                {
                    OrderId = 99,
                    PlatformOrderId = "PO-1",
                    PlatformOrderNo = "PO-1"
                }
            ],
            fourSellerOrderId: 1,
            orderNo: "PO-1");

        Assert.NotNull(match);
        Assert.Equal(99, match!.OrderId);
    }

    private Order SeedOrder(string platformStatus, string? trackingNo)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OwnerId = _ownerId,
            FourSellerOrderId = 42_001,
            PlatformOrderId = "577590677497811481",
            Platform = "tiktok",
            OrderStatus = "shipped",
            PlatformOrderStatus = platformStatus,
            TrackingNo = trackingNo,
            ShippingService = trackingNo is null ? null : "GOFO.",
            Currency = "USD",
            LastSyncedAt = DateTime.UtcNow.AddHours(-6),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            OrderCreateTime = DateTime.UtcNow.AddDays(-1)
        };
        OrderWorkflowSignals.Apply(order);
        _db.Orders.Add(order);
        _db.SaveChanges();
        return order;
    }

    private OrderSyncService NewService(IFourSellerApiService fourSeller) =>
        new(
            _db,
            fourSeller,
            null!,
            null!,
            null!,
            null!,
            new NullFulfilQueueRealtimePublisher(),
            NullLogger<OrderSyncService>.Instance);

    /// <summary>
    /// Minimal <see cref="IFourSellerApiService"/> that only implements GetOrderPageAsync.
    /// </summary>
    private class StubFourSeller : DispatchProxy
    {
        private string? _statusWithRecord;
        private FourSellerOrderRecordDto? _record;

        public static IFourSellerApiService Create(
            string? recordForStatus,
            FourSellerOrderRecordDto? record)
        {
            object proxy = Create<IFourSellerApiService, StubFourSeller>();
            var stub = (StubFourSeller)proxy;
            stub._statusWithRecord = recordForStatus;
            stub._record = record;
            return (IFourSellerApiService)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException();
            }

            if (targetMethod.Name == nameof(IFourSellerApiService.GetOrderPageAsync))
            {
                var request = (FourSellerOrderPageRequest)args![1]!;

                // Bucket "all" trả đơn bất kể trạng thái của chính nó — đúng hành vi thật của
                // 4Seller (kiểm chứng bằng dev probe `order-page-search-probe`). Bucket cụ thể
                // chỉ trúng khi trùng trạng thái của record.
                var hit = _record is not null
                    && (string.Equals(request.OrderStatus, "all", StringComparison.OrdinalIgnoreCase)
                        || (_statusWithRecord is not null
                            && string.Equals(
                                request.OrderStatus,
                                _statusWithRecord,
                                StringComparison.OrdinalIgnoreCase)));

                var page = new FourSellerOrderPageDto
                {
                    PageCurrent = 1,
                    PageSize = 10,
                    Total = hit ? 1 : 0,
                    Records = hit ? [_record!] : []
                };
                return Task.FromResult(page);
            }

            throw new NotSupportedException(
                $"StubFourSeller does not implement {targetMethod.Name}");
        }
    }
}
