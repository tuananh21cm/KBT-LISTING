using Application.Interfaces;
using Application.Options;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// PrintPackShip USPS track-scan push: eligibility, charge safety, fee stamp.
/// </summary>
public class UspsScanPushTests
{
    private static readonly Guid ActingUserId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public async Task Push_Skips_NoTracking_NotUsps_AlreadyPushed_Duplicate()
    {
        await using var db = NewDb();
        var ok = NewUspsOrder("OK-1", "9200190355087628224274");
        var noTrack = NewUspsOrder("NT-1", tracking: null);
        var notUsps = NewOrder("NU-1");
        notUsps.ShippingService = "FedEx";
        notUsps.TrackingNo = "123456789012";
        notUsps.HasUspsSignal = false;
        var already = NewUspsOrder("AP-1", "9261290352019958606452");
        already.UspsScanBatchId = 999;
        already.UspsProcessedAt = DateTime.UtcNow;
        var dupTrack = NewUspsOrder("DUP-1", "9200190355087628224274");

        db.Orders.AddRange(ok, noTrack, notUsps, already, dupTrack);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(12442),
            SendResult = OkSend(0.5m),
        };
        var service = NewService(db, client);

        var result = await service.PushAsync(
            [ok.Id, noTrack.Id, notUsps.Id, already.Id, dupTrack.Id],
            notes: null,
            ActingUserId,
            ownerUserId: null);

        Assert.Equal(UspsScanBatch.StatusSent, result.Status);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(12442, result.BatchId);
        Assert.Contains(result.Skipped, s => s.Reason == UspsScanSkipReasons.NoTracking);
        Assert.Contains(result.Skipped, s => s.Reason == UspsScanSkipReasons.NotUsps);
        Assert.Contains(result.Skipped, s => s.Reason == UspsScanSkipReasons.AlreadyPushed);
        Assert.Contains(result.Skipped, s => s.Reason == UspsScanSkipReasons.DuplicateTracking);

        var stamped = await db.Orders.SingleAsync(o => o.Id == ok.Id);
        Assert.Equal(12442, stamped.UspsScanBatchId);
        Assert.NotNull(stamped.UspsProcessedAt);
        Assert.Equal(0.5m, stamped.BuyTrackAmount);
    }

    [Fact]
    public async Task Push_Skips_AlreadyInTransit()
    {
        await using var db = NewDb();
        var ok = NewUspsOrder("OK-IT", "9200190355087628224280");
        var inTransit = NewUspsOrder("IT-1", "9200190355087628224281");
        inTransit.InTransitAt = DateTime.UtcNow.AddHours(-2);
        var delivered = NewUspsOrder("DL-1", "9200190355087628224282");
        delivered.DeliveredAt = DateTime.UtcNow.AddHours(-1);
        var platformInTransit = NewUspsOrder("PIT-1", "9200190355087628224283");
        platformInTransit.PlatformOrderStatus = PlatformOrderStatuses.InTransit;

        db.Orders.AddRange(ok, inTransit, delivered, platformInTransit);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(200),
            SendResult = OkSend(0.5m),
        };
        var service = NewService(db, client);

        var result = await service.PushAsync(
            [ok.Id, inTransit.Id, delivered.Id, platformInTransit.Id],
            notes: null,
            ActingUserId,
            ownerUserId: null);

        Assert.Equal(UspsScanBatch.StatusSent, result.Status);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(3, result.Skipped.Count(s => s.Reason == UspsScanSkipReasons.AlreadyInTransit));

        var stamped = await db.Orders.SingleAsync(o => o.Id == ok.Id);
        Assert.NotNull(stamped.UspsProcessedAt);
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == inTransit.Id)).UspsProcessedAt);
    }

    [Fact]
    public async Task Push_Skips_TerminalCommerce_RefundAndCancelled()
    {
        await using var db = NewDb();
        var ok = NewUspsOrder("OK-TC", "9200190355087628224300");
        var refund = NewUspsOrder("RF-1", "9200190355087628224301");
        refund.PlatformOrderStatus = PlatformOrderStatuses.Refund;
        refund.WorkflowTab = OrderWorkflowTabs.Refund;
        var cancelled = NewUspsOrder("CN-1", "9200190355087628224302");
        cancelled.PlatformOrderStatus = PlatformOrderStatuses.Cancelled;
        cancelled.WorkflowTab = OrderWorkflowTabs.Cancelled;

        db.Orders.AddRange(ok, refund, cancelled);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(201),
            SendResult = OkSend(0.5m),
        };
        var service = NewService(db, client);

        var result = await service.PushAsync(
            [ok.Id, refund.Id, cancelled.Id],
            notes: null,
            ActingUserId,
            ownerUserId: null);

        Assert.Equal(UspsScanBatch.StatusSent, result.Status);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(2, result.Skipped.Count(s => s.Reason == UspsScanSkipReasons.TerminalCommerce));
        Assert.Null((await db.Orders.SingleAsync(o => o.Id == refund.Id)).UspsProcessedAt);
    }

    [Fact]
    public async Task Push_SendFailed_DoesNotStampOrders()
    {
        await using var db = NewDb();
        var order = NewUspsOrder("F-1", "9200190355087628224274");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(100),
            SendResult = new PrintPackShipBatchSendResult
            {
                Kind = PrintPackShipCallKind.Failed,
                Success = false,
                Message = "Insufficient balance",
                Error = "Insufficient balance",
            },
        };
        var service = NewService(db, client);
        var result = await service.PushAsync([order.Id], null, ActingUserId, null);

        Assert.Equal(UspsScanBatch.StatusSendFailed, result.Status);
        Assert.Equal(0, result.SentCount);

        var reloaded = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Null(reloaded.UspsScanBatchId);
        Assert.Null(reloaded.UspsProcessedAt);
        Assert.Equal(0m, reloaded.BuyTrackAmount);

        var batch = await db.UspsScanBatches.SingleAsync();
        Assert.Equal(UspsScanBatch.StatusSendFailed, batch.Status);
    }

    [Fact]
    public async Task Push_SendUnknown_BlocksRepush()
    {
        await using var db = NewDb();
        var order = NewUspsOrder("U-1", "9200190355087628224274");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(200),
            SendResult = new PrintPackShipBatchSendResult
            {
                Kind = PrintPackShipCallKind.Unknown,
                Error = "Timeout khi gọi PrintPackShip.",
            },
        };
        var service = NewService(db, client);
        var first = await service.PushAsync([order.Id], null, ActingUserId, null);
        Assert.Equal(UspsScanBatch.StatusSendUnknown, first.Status);
        Assert.Equal(0, first.SentCount);

        // Second push: create would charge again if not blocked — fake would succeed if called.
        client.CreateResult = OkCreate(201);
        client.SendResult = OkSend(0.5m);
        var second = await service.PushAsync([order.Id], null, ActingUserId, null);
        Assert.Equal(0, second.SentCount);
        Assert.Contains(second.Skipped, s => s.Reason == UspsScanSkipReasons.PendingBatch);
        Assert.Equal(1, client.CreateCallCount); // only first push called create
    }

    [Fact]
    public async Task Push_Success_UsesPricePerLabel_AndKeepsCreateBatchId()
    {
        await using var db = NewDb();
        var order = NewUspsOrder("S-1", "9200190355087628224274");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(12442),
            SendResult = OkSend(0.5m, responseBatchId: 12443),
        };
        var service = NewService(db, client);
        var result = await service.PushAsync([order.Id], null, ActingUserId, null);

        Assert.Equal(12442, result.BatchId);
        Assert.Equal(0.5m, result.PricePerLabel);

        var reloaded = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(12442, reloaded.UspsScanBatchId);
        Assert.Equal(0.5m, reloaded.BuyTrackAmount);
        Assert.NotNull(reloaded.UspsScanSentAt);
    }

    [Fact]
    public async Task Push_OwnerScope_SkipsOtherSeller()
    {
        await using var db = NewDb();
        var mine = NewUspsOrder("M-1", "9200190355087628224274");
        var other = NewUspsOrder("O-1", "9261290352019958606452");
        other.OwnerId = Guid.NewGuid();
        db.Orders.AddRange(mine, other);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = OkCreate(1),
            SendResult = OkSend(0.5m),
        };
        var service = NewService(db, client);
        var result = await service.PushAsync(
            [mine.Id, other.Id],
            null,
            ActingUserId,
            ownerUserId: OwnerId);

        Assert.Equal(1, result.SentCount);
        Assert.Contains(result.Skipped, s => s.OrderId == other.Id && s.Reason == UspsScanSkipReasons.NotOwned);
    }

    [Fact]
    public async Task Push_ZeroEligible_DoesNotCallApi()
    {
        await using var db = NewDb();
        var order = NewUspsOrder("Z-1", tracking: null);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient();
        var service = NewService(db, client);
        var result = await service.PushAsync([order.Id], null, ActingUserId, null);

        Assert.Equal(0, result.SentCount);
        Assert.Equal(0, client.CreateCallCount);
        Assert.Equal(0, client.SendCallCount);
    }

    [Fact]
    public async Task Push_CreateFailed_Throws_NoBatchPersisted()
    {
        await using var db = NewDb();
        var order = NewUspsOrder("C-1", "9200190355087628224274");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var client = new FakePrintPackShipClient
        {
            CreateResult = new PrintPackShipBatchCreateResult
            {
                Kind = PrintPackShipCallKind.Failed,
                Success = false,
                Error = "Unauthorized",
            },
        };
        var service = NewService(db, client);

        await Assert.ThrowsAsync<BusinessException>(() =>
            service.PushAsync([order.Id], null, ActingUserId, null));

        Assert.Empty(await db.UspsScanBatches.ToListAsync());
        var reloaded = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Null(reloaded.UspsScanBatchId);
    }

    [Fact]
    public void ResolveCarrierTracking_PrefersOrderTrackingNo()
    {
        var order = NewUspsOrder("R-1", "ORDER-TRACK");
        order.SupplierPurchases =
        [
            new SupplierPurchase
            {
                Id = Guid.NewGuid(),
                TrackingNo = "LINK-TRACK",
                TrackingKind = SupplierTrackingKinds.Carrier,
                Carrier = "USPS",
            },
        ];

        Assert.Equal("ORDER-TRACK", UspsScanPushService.ResolveCarrierTracking(order));
    }

    private static PrintPackShipBatchCreateResult OkCreate(long batchId) =>
        new()
        {
            Kind = PrintPackShipCallKind.Ok,
            Success = true,
            BatchId = batchId,
            Status = "draft",
            RawJson = $"{{\"success\":true,\"data\":{{\"batch_id\":{batchId}}}}}",
        };

    private static PrintPackShipBatchSendResult OkSend(decimal price, long? responseBatchId = null) =>
        new()
        {
            Kind = PrintPackShipCallKind.Ok,
            Success = true,
            ResponseBatchId = responseBatchId,
            Status = "scanning",
            PricePerLabel = price,
            ScanFee = price * 2,
            PaidPages = 2,
            PagesTotal = 2,
            PagesSuccess = 2,
            PagesFailed = 0,
            BalancePrevious = 6,
            BalanceCurrent = 5,
            RawJson = "{\"success\":true}",
        };

    private static Order NewUspsOrder(string platformId, string? tracking)
    {
        var order = NewOrder(platformId);
        order.ShippingService = "USPS Priority Mail";
        order.HasUspsSignal = true;
        order.TrackingNo = tracking;
        return order;
    }

    private static Order NewOrder(string platformId) =>
        new()
        {
            Id = Guid.NewGuid(),
            FourSellerOrderId = Random.Shared.NextInt64(1, long.MaxValue),
            PlatformOrderId = platformId,
            OwnerId = OwnerId,
            OrderStatus = "awaiting_shipment",
            PlatformOrderStatus = "AWAITING_SHIPMENT",
            Currency = "USD",
            CreatedAt = DateTime.UtcNow,
            LastSyncedAt = DateTime.UtcNow,
            WorkflowTab = "USPS_UNPROCESSED",
        };

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UspsScanPushService NewService(IApplicationDbContext db, FakePrintPackShipClient client)
    {
        var settings = new StubSettingProvider();
        settings.Values[OrderHandlingSettingKeys.BuyTrack] = "0.5";
        return new UspsScanPushService(
            db,
            client,
            new UspsScanPushGate(),
            settings,
            Microsoft.Extensions.Options.Options.Create(new PrintPackShipOptions
            {
                MaxTrackingPerBatch = 100,
                BearerToken = "test",
            }),
            NullLogger<UspsScanPushService>.Instance);
    }

    private sealed class FakePrintPackShipClient : IPrintPackShipScanClient
    {
        public PrintPackShipBatchCreateResult CreateResult { get; set; } = OkCreate(1);
        public PrintPackShipBatchSendResult SendResult { get; set; } = OkSend(0.5m);
        public int CreateCallCount { get; private set; }
        public int SendCallCount { get; private set; }

        public Task<PrintPackShipBatchCreateResult> CreateTrackingBatchAsync(
            IReadOnlyList<string> trackingNumbers,
            string? notes,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return Task.FromResult(CreateResult);
        }

        public Task<PrintPackShipBatchSendResult> SendBatchAsync(
            long batchId,
            CancellationToken cancellationToken = default)
        {
            SendCallCount++;
            return Task.FromResult(SendResult);
        }
    }

    /// <summary>Minimal gate for tests (not the Infrastructure singleton).</summary>
    private sealed class UspsScanPushGate : IUspsScanPushGate
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public Task WaitAsync(CancellationToken cancellationToken = default) =>
            _lock.WaitAsync(cancellationToken);

        public void Release() => _lock.Release();
    }
}
