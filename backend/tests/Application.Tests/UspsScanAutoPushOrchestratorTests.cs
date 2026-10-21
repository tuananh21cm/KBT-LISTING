using Application.DTOs.Order;
using Application.Interfaces;
using Application.Options;
using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Auto USPS scan orchestrator: settings gate, TrackingAddedAt delay, chunking.
/// </summary>
public class UspsScanAutoPushOrchestratorTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public async Task RunOnce_Disabled_DoesNotPush()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 3, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: false, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Empty(push.Calls);
    }

    [Fact]
    public async Task RunOnce_DelayUsesTrackingAddedAt_Only()
    {
        await using var db = NewDb();
        var oldCreate = DateTime.UtcNow.AddDays(-10);
        var recentTrack = DateTime.UtcNow.AddHours(-2); // delay 12h → not eligible
        var agedTrack = DateTime.UtcNow.AddHours(-13);

        var tooYoung = NewUspsOrder("YOUNG", "9200190355087628224271", agedTrack);
        tooYoung.TrackingAddedAt = recentTrack;
        tooYoung.OrderCreateTime = oldCreate;
        tooYoung.CreatedAt = oldCreate;

        var ready = NewUspsOrder("READY", "9200190355087628224272", agedTrack);
        ready.OrderCreateTime = oldCreate;

        var missingClock = NewUspsOrder("NOCLOCK", "9200190355087628224273", agedTrack);
        missingClock.TrackingAddedAt = null;
        missingClock.OrderCreateTime = oldCreate;

        db.Orders.AddRange(tooYoung, ready, missingClock);
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, delayHours: 12, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal([ready.Id], push.Calls[0]);
    }

    [Fact]
    public async Task RunOnce_ChunkSize10_SplitsIntoThreeCalls()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 25, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Equal(3, push.Calls.Count);
        Assert.Equal(10, push.Calls[0].Count);
        Assert.Equal(10, push.Calls[1].Count);
        Assert.Equal(5, push.Calls[2].Count);
        Assert.Equal(25, push.Calls.Sum(c => c.Count));
    }

    [Fact]
    public async Task RunOnce_ChunkSize0_SinglePushWithAllIds()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 25, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 0), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal(25, push.Calls[0].Count);
    }

    [Fact]
    public async Task RunOnce_NoAdmin_DoesNotPush()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 2, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 10), push, actingUserId: null);

        await orch.RunOnceAsync();

        Assert.Empty(push.Calls);
    }

    [Fact]
    public async Task RunOnce_Skips_InTransitAndDelivered()
    {
        await using var db = NewDb();
        var aged = DateTime.UtcNow.AddHours(-24);
        var ready = NewUspsOrder("READY-IT", "9200190355087628224290", aged);
        var inTransit = NewUspsOrder("IT", "9200190355087628224291", aged);
        inTransit.InTransitAt = DateTime.UtcNow.AddHours(-3);
        var platformIt = NewUspsOrder("PIT", "9200190355087628224292", aged);
        platformIt.PlatformOrderStatus = PlatformOrderStatuses.InTransit;
        var delivered = NewUspsOrder("DLV", "9200190355087628224293", aged);
        delivered.DeliveredAt = DateTime.UtcNow.AddHours(-1);

        db.Orders.AddRange(ready, inTransit, platformIt, delivered);
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal([ready.Id], push.Calls[0]);
    }

    [Fact]
    public async Task RunOnce_Skips_RefundAndCancelled()
    {
        await using var db = NewDb();
        var aged = DateTime.UtcNow.AddHours(-24);
        var ready = NewUspsOrder("READY-RF", "9200190355087628224310", aged);
        var refund = NewUspsOrder("RF", "9200190355087628224311", aged);
        refund.PlatformOrderStatus = PlatformOrderStatuses.Refund;
        refund.WorkflowTab = OrderWorkflowTabs.Refund;
        var cancelled = NewUspsOrder("CN", "9200190355087628224312", aged);
        cancelled.PlatformOrderStatus = PlatformOrderStatuses.Cancelled;
        cancelled.WorkflowTab = OrderWorkflowTabs.Cancelled;

        db.Orders.AddRange(ready, refund, cancelled);
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal([ready.Id], push.Calls[0]);
    }

    [Fact]
    public async Task RunOnce_Skips_WorkflowTabInTransit()
    {
        await using var db = NewDb();
        var aged = DateTime.UtcNow.AddHours(-24);
        var ready = NewUspsOrder("READY-WT", "9200190355087628224320", aged);
        var dangGiao = NewUspsOrder("DG", "9200190355087628224321", aged);
        dangGiao.PlatformOrderStatus = PlatformOrderStatuses.InTransit;
        dangGiao.WorkflowTab = OrderWorkflowTabs.InTransit;

        db.Orders.AddRange(ready, dangGiao);
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, chunkSize: 10), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal([ready.Id], push.Calls[0]);
    }

    [Fact]
    public async Task RunOnce_OutsideUsBusinessHours_DoesNotPush()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 3, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        // Cutoff margin wider than the window → closed on every calendar day, whatever "now" is.
        var settings = NewSettings(
            enabled: true,
            windowEnabled: true,
            weekdayStart: "08:00",
            weekdayEnd: "09:00",
            cutoffMarginMinutes: 60);
        var orch = NewOrch(db, settings, push);

        await orch.RunOnceAsync();

        Assert.Empty(push.Calls);
    }

    [Fact]
    public async Task RunOnce_WindowDisabled_PushesRoundTheClock()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 2, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var orch = NewOrch(db, NewSettings(enabled: true, windowEnabled: false), push);

        await orch.RunOnceAsync();

        Assert.Single(push.Calls);
        Assert.Equal(2, push.Calls[0].Count);
    }

    [Fact]
    public async Task RunOnce_WithConfigOverload_SkipsSettingsRead()
    {
        await using var db = NewDb();
        SeedEligible(db, count: 2, trackingAddedAt: DateTime.UtcNow.AddHours(-24));
        await db.SaveChangesAsync();

        var push = new FakePush();
        var settings = NewSettings(enabled: false);
        var orch = NewOrch(db, settings, push);
        var config = UspsScanAutoSettingsReader.From(
            NewSettings(enabled: true, windowEnabled: false).Values);

        await orch.RunOnceAsync(config);

        // The disabled stub would have blocked the push had the overload re-read settings.
        Assert.Single(push.Calls);
        Assert.Equal(2, push.Calls[0].Count);
    }

    private static void SeedEligible(ApplicationDbContext db, int count, DateTime trackingAddedAt)
    {
        for (var i = 0; i < count; i++)
        {
            var track = $"9200190355087628224{i:D3}";
            db.Orders.Add(NewUspsOrder($"P-{i}", track, trackingAddedAt));
        }
    }

    private static Order NewUspsOrder(string platformId, string tracking, DateTime trackingAddedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            FourSellerOrderId = Random.Shared.NextInt64(1, long.MaxValue),
            PlatformOrderId = platformId,
            OwnerId = OwnerId,
            OrderStatus = "awaiting_shipment",
            PlatformOrderStatus = "AWAITING_SHIPMENT",
            Currency = "USD",
            CreatedAt = trackingAddedAt.AddDays(-1),
            LastSyncedAt = DateTime.UtcNow,
            WorkflowTab = "USPS_UNPROCESSED",
            ShippingService = "USPS Priority Mail",
            HasUspsSignal = true,
            TrackingNo = tracking,
            TrackingAddedAt = trackingAddedAt,
        };

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Settings stub. <paramref name="windowEnabled"/> defaults to <c>false</c> so the assertions
    /// below stay deterministic — the US business-hours gate itself is covered by
    /// <see cref="UspsScanWindowTests"/> against fixed instants.
    /// </summary>
    private static StubSettingProvider NewSettings(
        bool enabled,
        int chunkSize = 10,
        int delayHours = 12,
        int intervalMinutes = 10,
        bool windowEnabled = false,
        string weekdayStart = "08:00",
        string weekdayEnd = "17:00",
        int cutoffMarginMinutes = 30)
    {
        var s = new StubSettingProvider();
        s.Values[UspsScanAutoSettingKeys.AutoEnabled] = enabled ? "true" : "false";
        s.Values[UspsScanAutoSettingKeys.DelayHours] = delayHours.ToString();
        s.Values[UspsScanAutoSettingKeys.ChunkSize] = chunkSize.ToString();
        s.Values[UspsScanAutoSettingKeys.IntervalMinutes] = intervalMinutes.ToString();
        s.Values[UspsScanAutoSettingKeys.WindowEnabled] = windowEnabled ? "true" : "false";
        s.Values[UspsScanAutoSettingKeys.WeekdayStart] = weekdayStart;
        s.Values[UspsScanAutoSettingKeys.WeekdayEnd] = weekdayEnd;
        s.Values[UspsScanAutoSettingKeys.SaturdayEnabled] = "true";
        s.Values[UspsScanAutoSettingKeys.SaturdayStart] = "08:00";
        s.Values[UspsScanAutoSettingKeys.SaturdayEnd] = "12:00";
        s.Values[UspsScanAutoSettingKeys.CutoffMarginMinutes] = cutoffMarginMinutes.ToString();
        return s;
    }

    private static UspsScanAutoPushOrchestrator NewOrch(
        IApplicationDbContext db,
        ISettingProvider settings,
        FakePush push) =>
        NewOrch(db, settings, push, AdminId);

    private static UspsScanAutoPushOrchestrator NewOrch(
        IApplicationDbContext db,
        ISettingProvider settings,
        FakePush push,
        Guid? actingUserId) =>
        new(
            db,
            settings,
            push,
            new StubActingUser(actingUserId),
            Microsoft.Extensions.Options.Options.Create(new PrintPackShipOptions
            {
                MaxTrackingPerBatch = 100,
                BearerToken = "test",
            }),
            NullLogger<UspsScanAutoPushOrchestrator>.Instance);

    private sealed class StubActingUser(Guid? id) : IUspsScanAutoActingUserResolver
    {
        public Task<Guid?> ResolveActingAdminUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(id);
    }

    private sealed class FakePush : IUspsScanPushService
    {
        public List<List<Guid>> Calls { get; } = [];

        public Task<UspsScanPushResultDto> PushAsync(
            IReadOnlyList<Guid> orderIds,
            string? notes,
            Guid actingUserId,
            Guid? ownerUserId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(orderIds.ToList());
            return Task.FromResult(new UspsScanPushResultDto
            {
                Status = UspsScanBatch.StatusSent,
                SentCount = orderIds.Count,
                Skipped = [],
            });
        }

        public Task<UspsScanBatchListPageDto> ListBatchesAsync(
            UspsScanBatchListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
