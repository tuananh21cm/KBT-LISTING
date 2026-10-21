using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Maintains <see cref="ShopOperationalPeriod"/> history for seller-performance exposure.
/// Mutates the shared DbContext; callers own <c>SaveChangesAsync</c> unless noted.
/// </summary>
public interface IShopOperationalPeriodService
{
    /// <summary>
    /// Opens a period when a shop is created Active (or ensures one exists).
    /// </summary>
    Task OnShopCreatedAsync(Shop shop, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes/opens periods when status and/or owner change.
    /// </summary>
    Task OnShopUpdatedAsync(
        Shop shop,
        string previousStatus,
        Guid previousOwnerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes any open period before the shop row is removed.
    /// </summary>
    Task OnShopDeletingAsync(Shop shop, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the earliest period start earlier when historical non-canceled orders are synced.
    /// Does not reopen closed gaps; only extends the earliest period (or opens if Active + none).
    /// </summary>
    Task ExtendStartsFromOrderHistoryAsync(
        IReadOnlyCollection<(Guid ShopId, Guid OwnerId, DateOnly FirstOrderDay)> rows,
        IReadOnlyDictionary<Guid, string> shopStatusesById,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vietnam calendar day for a UTC instant.
    /// </summary>
    DateOnly ToVietnamDate(DateTime utcInstant);
}
