using Application.DTOs.Shop;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class ShopOnlineSessionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ShopOnlineSessionService _sut;
    private readonly Guid _shopId = Guid.NewGuid();
    private const string ExternalShopId = "7496154235269843519";

    public ShopOnlineSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Shops.Add(new Shop
        {
            Id = _shopId,
            OwnerId = Guid.NewGuid(),
            Name = "Test Shop",
            PlatformId = Guid.NewGuid(),
            ExternalShopId = ExternalShopId,
            Status = ShopStatuses.Active,
        });
        _db.SaveChanges();
        _sut = new ShopOnlineSessionService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Heartbeat_OpensSession()
    {
        var started = DateTime.UtcNow.AddMinutes(-5);
        var seen = DateTime.UtcNow;

        var result = await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-1",
            StartedAt = started,
            LastSeenAt = seen,
        });

        Assert.True(result.IsOpen);
        Assert.Equal(_shopId, result.ShopId);
        Assert.Equal("client-1", result.ClientSessionId);
        Assert.Null(result.EndedAt);
        Assert.Equal(1, await _db.ShopOnlineSessions.CountAsync());
    }

    [Fact]
    public async Task Heartbeat_UpdatesLastSeen_Monotonic()
    {
        var started = DateTime.UtcNow.AddMinutes(-2);
        await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-2",
            StartedAt = started,
            LastSeenAt = started.AddSeconds(30),
        });

        var later = DateTime.UtcNow;
        var result = await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-2",
            StartedAt = started,
            LastSeenAt = later,
        });

        Assert.True(result.IsOpen);
        Assert.Equal(AppTimeZones.EnsureUtc(later), result.LastSeenAt);
    }

    [Fact]
    public async Task Close_IsIdempotent()
    {
        var started = DateTime.UtcNow.AddMinutes(-3);
        await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-3",
            StartedAt = started,
            LastSeenAt = started.AddMinutes(1),
        });

        var ended = started.AddMinutes(2);
        var first = await _sut.CloseAsync(new ShopOnlineCloseDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-3",
            EndedAt = ended,
            Reason = ShopOnlineCloseReasons.TabClosed,
        });
        var second = await _sut.CloseAsync(new ShopOnlineCloseDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "client-3",
            EndedAt = ended.AddMinutes(10),
            Reason = ShopOnlineCloseReasons.BrowserRestartReconcile,
        });

        Assert.False(first.IsOpen);
        Assert.Equal(ShopOnlineCloseReasons.TabClosed, first.CloseReason);
        Assert.Equal(first.EndedAt, second.EndedAt);
        Assert.Equal(ShopOnlineCloseReasons.TabClosed, second.CloseReason);
    }

    [Fact]
    public async Task Heartbeat_ClosesStaleOpenSession_AsTimeout()
    {
        _db.ShopOnlineSessions.Add(new ShopOnlineSession
        {
            Id = Guid.NewGuid(),
            ShopId = _shopId,
            ExternalShopId = ExternalShopId,
            ClientSessionId = "stale-old",
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            LastSeenAt = DateTime.UtcNow.AddMinutes(-10),
        });
        await _db.SaveChangesAsync();

        var result = await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "fresh",
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            LastSeenAt = DateTime.UtcNow,
        });

        Assert.True(result.IsOpen);
        Assert.Equal("fresh", result.ClientSessionId);

        var stale = await _db.ShopOnlineSessions.SingleAsync(s => s.ClientSessionId == "stale-old");
        Assert.NotNull(stale.EndedAt);
        Assert.Equal(ShopOnlineCloseReasons.HeartbeatTimeout, stale.CloseReason);
    }

    [Fact]
    public async Task Heartbeat_SplitsAcrossVietnamMidnight()
    {
        // 22:00 VN previous calendar day → 01:00 VN next day (UTC+7).
        var startLocal = new DateTime(2026, 9, 11, 22, 0, 0, DateTimeKind.Unspecified);
        var seenLocal = new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Unspecified);
        var startedAt = TimeZoneInfo.ConvertTimeToUtc(startLocal, AppTimeZones.Vietnam);
        var lastSeenAt = TimeZoneInfo.ConvertTimeToUtc(seenLocal, AppTimeZones.Vietnam);

        var result = await _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
        {
            ExternalShopId = ExternalShopId,
            ClientSessionId = "cross-day",
            StartedAt = startedAt,
            LastSeenAt = lastSeenAt,
        });

        Assert.True(result.IsOpen);
        Assert.NotEqual("cross-day", result.ClientSessionId);

        var rows = await _db.ShopOnlineSessions.OrderBy(s => s.StartedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(ShopOnlineCloseReasons.DayBoundary, rows[0].CloseReason);
        Assert.NotNull(rows[0].EndedAt);
        Assert.Null(rows[1].EndedAt);
    }

    [Fact]
    public async Task Heartbeat_UnknownShop_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.HeartbeatAsync(new ShopOnlineHeartbeatDto
            {
                ExternalShopId = "missing-shop",
                ClientSessionId = "x",
                StartedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            }));
    }
}
