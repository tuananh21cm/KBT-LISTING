using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Records shop operational windows used as the seller-performance shop-day denominator.
/// </summary>
public class ShopOperationalPeriodService : IShopOperationalPeriodService
{
    private readonly IApplicationDbContext _db;

    /// <summary>Initializes the service.</summary>
    public ShopOperationalPeriodService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public DateOnly ToVietnamDate(DateTime utcInstant)
    {
        var utc = AppTimeZones.EnsureUtc(utcInstant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, AppTimeZones.Vietnam);
        return DateOnly.FromDateTime(local);
    }

    /// <inheritdoc />
    public async Task OnShopCreatedAsync(Shop shop, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(shop.Status, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var today = TodayVietnam();
        await EnsureOpenPeriodAsync(
            shop.Id,
            shop.OwnerId,
            today,
            ShopOperationalPeriodSources.Active,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task OnShopUpdatedAsync(
        Shop shop,
        string previousStatus,
        Guid previousOwnerId,
        CancellationToken cancellationToken = default)
    {
        var today = TodayVietnam();
        var wasActive = IsActive(previousStatus);
        var isActive = IsActive(shop.Status);
        var ownerChanged = previousOwnerId != shop.OwnerId;

        if (ownerChanged)
        {
            var closed = await CloseOpenPeriodAsync(
                shop.Id,
                today,
                ShopOperationalPeriodSources.OwnerChanged,
                cancellationToken);

            if (closed)
            {
                // EF inserts before it updates, so the close has to reach the database first or the
                // new open row would collide on IX_ShopOperationalPeriods_ShopId_Open.
                await _db.SaveChangesAsync(cancellationToken);
            }

            if (isActive)
            {
                await EnsureOpenPeriodAsync(
                    shop.Id,
                    shop.OwnerId,
                    today,
                    ShopOperationalPeriodSources.Active,
                    cancellationToken);
            }

            return;
        }

        if (wasActive && !isActive)
        {
            var closeSource = string.Equals(shop.Status, ShopStatuses.Suspended, StringComparison.OrdinalIgnoreCase)
                ? ShopOperationalPeriodSources.Suspended
                : ShopOperationalPeriodSources.Deactivated;
            await CloseOpenPeriodAsync(shop.Id, today, closeSource, cancellationToken);
            return;
        }

        if (!wasActive && isActive)
        {
            await EnsureOpenPeriodAsync(
                shop.Id,
                shop.OwnerId,
                today,
                ShopOperationalPeriodSources.Active,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task OnShopDeletingAsync(Shop shop, CancellationToken cancellationToken = default)
    {
        await CloseOpenPeriodAsync(
            shop.Id,
            TodayVietnam(),
            ShopOperationalPeriodSources.Deleted,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExtendStartsFromOrderHistoryAsync(
        IReadOnlyCollection<(Guid ShopId, Guid OwnerId, DateOnly FirstOrderDay)> rows,
        IReadOnlyDictionary<Guid, string> shopStatusesById,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var shopIds = rows.Select(r => r.ShopId).Distinct().ToList();
        var periods = await _db.ShopOperationalPeriods
            .Where(p => shopIds.Contains(p.ShopId))
            .ToListAsync(cancellationToken);

        var byShop = periods
            .GroupBy(p => p.ShopId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartedOn).ToList());

        foreach (var row in rows)
        {
            shopStatusesById.TryGetValue(row.ShopId, out var status);
            byShop.TryGetValue(row.ShopId, out var shopPeriods);
            shopPeriods ??= [];

            if (shopPeriods.Count == 0)
            {
                if (!IsActive(status))
                {
                    continue;
                }

                await EnsureOpenPeriodAsync(
                    row.ShopId,
                    row.OwnerId,
                    row.FirstOrderDay,
                    ShopOperationalPeriodSources.OrderHistory,
                    cancellationToken);
                continue;
            }

            var earliest = shopPeriods[0];
            if (row.FirstOrderDay < earliest.StartedOn)
            {
                earliest.StartedOn = row.FirstOrderDay;
                earliest.UpdatedAt = DateTime.UtcNow;
                if (!string.Equals(
                        earliest.OpenSource,
                        ShopOperationalPeriodSources.OrderHistory,
                        StringComparison.Ordinal))
                {
                    // Keep original open source; order history is recorded via UpdatedAt + earlier start.
                    // Tag only when the period was opened purely by order history.
                }
            }
        }
    }

    /// <summary>
    /// Unique index <c>IX_ShopOperationalPeriods_ShopId_Open</c> — at most one <c>EndedOn IS NULL</c> row per shop.
    /// </summary>
    /// <param name="ex">SaveChanges failure to inspect.</param>
    public static bool IsDuplicateOpenPeriod(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.Message.Contains("IX_ShopOperationalPeriods_ShopId_Open", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureOpenPeriodAsync(
        Guid shopId,
        Guid ownerId,
        DateOnly startedOn,
        string openSource,
        CancellationToken cancellationToken)
    {
        var open = await FindOpenPeriodAsync(shopId, cancellationToken);
        if (open is not null)
        {
            if (open.OwnerId == ownerId)
            {
                if (startedOn < open.StartedOn)
                {
                    open.StartedOn = startedOn;
                    open.UpdatedAt = DateTime.UtcNow;
                }

                return;
            }

            if (await CloseOpenPeriodAsync(
                    shopId,
                    startedOn,
                    ShopOperationalPeriodSources.OwnerChanged,
                    cancellationToken))
            {
                // Flush the close before inserting a new open row — EF inserts before updates,
                // so a same-UoW insert would hit IX_ShopOperationalPeriods_ShopId_Open.
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        if (await FindOpenPeriodAsync(shopId, cancellationToken) is not null)
        {
            return;
        }

        if (_db.ShopOperationalPeriods.Local.Any(p => p.ShopId == shopId && p.EndedOn is null))
        {
            return;
        }

        _db.ShopOperationalPeriods.Add(new ShopOperationalPeriod
        {
            ShopId = shopId,
            OwnerId = ownerId,
            StartedOn = startedOn,
            EndedOn = null,
            OpenSource = openSource,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <returns><c>true</c> when an open row was closed.</returns>
    private async Task<bool> CloseOpenPeriodAsync(
        Guid shopId,
        DateOnly endedOn,
        string closeSource,
        CancellationToken cancellationToken)
    {
        var open = await FindOpenPeriodAsync(shopId, cancellationToken);
        if (open is null)
        {
            return false;
        }

        // Zero-length guard: if closed before start, collapse to start day.
        open.EndedOn = endedOn < open.StartedOn ? open.StartedOn : endedOn;
        open.CloseSource = closeSource;
        open.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Prefer tracked rows (including <c>Added</c>), then load the shop's periods and pick
    /// <c>EndedOn == null</c> in memory so a leftover open row is never missed.
    /// </summary>
    private async Task<ShopOperationalPeriod?> FindOpenPeriodAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var local = _db.ShopOperationalPeriods.Local
            .FirstOrDefault(p => p.ShopId == shopId && p.EndedOn is null);
        if (local is not null)
        {
            return local;
        }

        var rows = await _db.ShopOperationalPeriods
            .Where(p => p.ShopId == shopId)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault(p => p.EndedOn is null);
    }

    private DateOnly TodayVietnam()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppTimeZones.Vietnam);
        return DateOnly.FromDateTime(local);
    }

    private static bool IsActive(string? status) =>
        string.Equals(status, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase);
}
