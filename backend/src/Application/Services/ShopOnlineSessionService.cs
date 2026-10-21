using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Persists seller-us tab online windows. End time is LastSeen-based when Chrome dies without
/// an explicit close — heartbeat grace is <see cref="HeartbeatGrace"/>.
/// </summary>
public sealed class ShopOnlineSessionService : IShopOnlineSessionService
{
    /// <summary>Close open sessions whose last heartbeat is older than this.</summary>
    public static readonly TimeSpan HeartbeatGrace = TimeSpan.FromMinutes(3);

    private readonly IApplicationDbContext _db;

    /// <summary>Initializes the service.</summary>
    public ShopOnlineSessionService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ShopOnlineSessionDto> HeartbeatAsync(
        ShopOnlineHeartbeatDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = dto.ExternalShopId.Trim();
        var clientSessionId = dto.ClientSessionId.Trim();
        var startedAt = AppTimeZones.EnsureUtc(dto.StartedAt);
        var lastSeenAt = AppTimeZones.EnsureUtc(dto.LastSeenAt);
        if (lastSeenAt < startedAt)
        {
            lastSeenAt = startedAt;
        }

        var shop = await ResolveShopAsync(externalShopId, cancellationToken);
        var now = DateTime.UtcNow;

        await CloseStaleOpenSessionsAsync(shop.Id, now, cancellationToken);

        var existing = await _db.ShopOnlineSessions
            .FirstOrDefaultAsync(
                s => s.ShopId == shop.Id && s.ClientSessionId == clientSessionId,
                cancellationToken);

        if (existing is not null && existing.EndedAt is null)
        {
            return await RefreshOpenAsync(existing, lastSeenAt, cancellationToken);
        }

        if (existing is not null)
        {
            // Same client id already closed — client must mint a new id.
            return Map(existing);
        }

        await CloseOtherOpenSessionsAsync(shop.Id, now, cancellationToken);

        var external = shop.ExternalShopId ?? externalShopId;
        var startDay = ToVietnamDate(startedAt);
        var seenDay = ToVietnamDate(lastSeenAt);

        if (seenDay > startDay)
        {
            var (_, dayEndUtc) = AppTimeZones.GetVietnamDayUtcRange(startDay);
            if (dayEndUtc < startedAt)
            {
                dayEndUtc = startedAt;
            }

            var closed = new ShopOnlineSession
            {
                Id = Guid.NewGuid(),
                ShopId = shop.Id,
                ExternalShopId = external,
                ClientSessionId = clientSessionId,
                StartedAt = startedAt,
                LastSeenAt = dayEndUtc,
                EndedAt = dayEndUtc,
                CloseReason = ShopOnlineCloseReasons.DayBoundary,
            };
            _db.ShopOnlineSessions.Add(closed);

            var next = CreateContinuation(shop.Id, external, seenDay, dayEndUtc, lastSeenAt);
            _db.ShopOnlineSessions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(next);
        }

        var session = new ShopOnlineSession
        {
            Id = Guid.NewGuid(),
            ShopId = shop.Id,
            ExternalShopId = external,
            ClientSessionId = clientSessionId,
            StartedAt = startedAt,
            LastSeenAt = lastSeenAt,
        };
        _db.ShopOnlineSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(session);
    }

    /// <inheritdoc />
    public async Task<ShopOnlineSessionDto> CloseAsync(
        ShopOnlineCloseDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = dto.ExternalShopId.Trim();
        var clientSessionId = dto.ClientSessionId.Trim();
        var endedAt = AppTimeZones.EnsureUtc(dto.EndedAt);
        var reason = NormalizeReason(dto.Reason);

        var shop = await ResolveShopAsync(externalShopId, cancellationToken);

        var session = await _db.ShopOnlineSessions
            .FirstOrDefaultAsync(
                s => s.ShopId == shop.Id && s.ClientSessionId == clientSessionId,
                cancellationToken);

        if (session is null)
        {
            throw new NotFoundException("Không tìm thấy phiên online.");
        }

        if (session.EndedAt is not null)
        {
            return Map(session);
        }

        if (endedAt < session.StartedAt)
        {
            endedAt = session.StartedAt;
        }

        if (endedAt < session.LastSeenAt)
        {
            endedAt = session.LastSeenAt;
        }

        session.EndedAt = endedAt;
        session.LastSeenAt = endedAt;
        session.CloseReason = reason;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(session);
    }

    private async Task<ShopOnlineSessionDto> RefreshOpenAsync(
        ShopOnlineSession session,
        DateTime lastSeenAt,
        CancellationToken cancellationToken)
    {
        var startDay = ToVietnamDate(session.StartedAt);
        var seenDay = ToVietnamDate(lastSeenAt);

        if (seenDay > startDay)
        {
            var (_, dayEndUtc) = AppTimeZones.GetVietnamDayUtcRange(startDay);
            if (dayEndUtc < session.StartedAt)
            {
                dayEndUtc = session.StartedAt;
            }

            session.EndedAt = dayEndUtc;
            session.LastSeenAt = dayEndUtc;
            session.CloseReason = ShopOnlineCloseReasons.DayBoundary;

            var next = CreateContinuation(
                session.ShopId,
                session.ExternalShopId,
                seenDay,
                dayEndUtc,
                lastSeenAt);
            _db.ShopOnlineSessions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(next);
        }

        if (lastSeenAt > session.LastSeenAt)
        {
            session.LastSeenAt = lastSeenAt;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(session);
    }

    private static ShopOnlineSession CreateContinuation(
        Guid shopId,
        string externalShopId,
        DateOnly seenDay,
        DateTime previousDayEndUtc,
        DateTime lastSeenAt)
    {
        var (nextStartUtc, _) = AppTimeZones.GetVietnamDayUtcRange(seenDay);
        if (nextStartUtc < previousDayEndUtc)
        {
            nextStartUtc = previousDayEndUtc;
        }

        var seen = lastSeenAt < nextStartUtc ? nextStartUtc : lastSeenAt;
        return new ShopOnlineSession
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            ExternalShopId = externalShopId,
            ClientSessionId = Guid.NewGuid().ToString("N"),
            StartedAt = nextStartUtc,
            LastSeenAt = seen,
        };
    }

    private async Task CloseStaleOpenSessionsAsync(
        Guid shopId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now - HeartbeatGrace;
        var stale = await _db.ShopOnlineSessions
            .Where(s => s.ShopId == shopId && s.EndedAt == null && s.LastSeenAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var session in stale)
        {
            session.EndedAt = session.LastSeenAt;
            session.CloseReason = ShopOnlineCloseReasons.HeartbeatTimeout;
        }

        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task CloseOtherOpenSessionsAsync(
        Guid shopId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var open = await _db.ShopOnlineSessions
            .Where(s => s.ShopId == shopId && s.EndedAt == null)
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            return;
        }

        foreach (var session in open)
        {
            var end = session.LastSeenAt > now ? now : session.LastSeenAt;
            if (end < session.StartedAt)
            {
                end = session.StartedAt;
            }

            session.EndedAt = end;
            session.CloseReason = ShopOnlineCloseReasons.Explicit;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Shop> ResolveShopAsync(string externalShopId, CancellationToken cancellationToken)
    {
        var matches = await _db.Shops
            .Where(s => s.ExternalShopId == externalShopId)
            .OrderByDescending(s => s.Status == ShopStatuses.Active)
            .ThenByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            throw new NotFoundException("Không tìm thấy shop với externalShopId này.");
        }

        return matches[0];
    }

    private static DateOnly ToVietnamDate(DateTime utcInstant)
    {
        var utc = AppTimeZones.EnsureUtc(utcInstant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, AppTimeZones.Vietnam);
        return DateOnly.FromDateTime(local);
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ShopOnlineCloseReasons.Explicit;
        }

        foreach (var known in ShopOnlineCloseReasons.All)
        {
            if (string.Equals(known, reason.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return ShopOnlineCloseReasons.Explicit;
    }

    private static ShopOnlineSessionDto Map(ShopOnlineSession row) => new()
    {
        Id = row.Id,
        ShopId = row.ShopId,
        ExternalShopId = row.ExternalShopId,
        ClientSessionId = row.ClientSessionId,
        StartedAt = row.StartedAt,
        LastSeenAt = row.LastSeenAt,
        EndedAt = row.EndedAt,
        CloseReason = row.CloseReason,
        IsOpen = row.EndedAt is null,
    };
}
