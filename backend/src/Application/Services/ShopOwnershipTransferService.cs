using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Cascades cookie-owner denorm when admin reassigns a shop to another seller.
/// </summary>
public class ShopOwnershipTransferService : IShopOwnershipTransferService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ShopOwnershipTransferService> _logger;

    /// <summary>Creates the transfer service.</summary>
    public ShopOwnershipTransferService(
        IApplicationDbContext db,
        ILogger<ShopOwnershipTransferService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ShopOwnershipTransferResultDto> TransferAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        CancellationToken cancellationToken = default)
    {
        var result = new ShopOwnershipTransferResultDto
        {
            ShopId = shopId,
            FromOwnerId = fromOwnerId,
            ToOwnerId = toOwnerId,
            ShopsProcessed = 1
        };

        if (fromOwnerId == toOwnerId)
        {
            return result;
        }

        var fourSellerShopIds = await ResolveFourSellerShopIdsAsync(shopId, cancellationToken);

        var now = DateTime.UtcNow;

        result.OrdersRematched = await RematchOrdersAsync(
            shopId, fromOwnerId, fourSellerShopIds, now, cancellationToken);
        result.ListingsRematched = await RematchListingsAsync(
            shopId, fromOwnerId, fourSellerShopIds, now, cancellationToken);

        // Persist ShopId rematch before merge/collapse queries hit the store.
        if (result.OrdersRematched + result.ListingsRematched > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        result.OrdersMerged = await MergePlatformOrderConflictsAsync(
            shopId, fromOwnerId, toOwnerId, fourSellerShopIds, cancellationToken);

        result.OrdersTransferred = await ReassignOrdersAsync(
            shopId, fromOwnerId, toOwnerId, fourSellerShopIds, now, cancellationToken);
        result.ListingsTransferred = await ReassignListingsAsync(
            shopId, fromOwnerId, toOwnerId, fourSellerShopIds, now, cancellationToken);
        result.AnalyticsSnapshotsTransferred = await ReassignAnalyticsAsync(
            shopId, fromOwnerId, toOwnerId, now, cancellationToken);
        result.DiscountActivitiesTransferred = await ReassignDiscountsAsync(
            shopId, fromOwnerId, toOwnerId, fourSellerShopIds, now, cancellationToken);
        result.SavedKeywordsTransferred = await ReassignSavedKeywordsAsync(
            shopId, fromOwnerId, toOwnerId, now, cancellationToken);
        result.ListingAddOriginsTransferred = await ReassignListingAddOriginsAsync(
            fromOwnerId, toOwnerId, fourSellerShopIds, now, cancellationToken);
        await ReassignShopScopedOwnerDenormAsync(
            shopId, fromOwnerId, toOwnerId, cancellationToken);

        await BackfillListingSourceUrlsFromOrderItemsAsync(shopId, toOwnerId, now, cancellationToken);
        result.ListingsCollapsed = await CollapseDuplicateListingsByTikTokProductAsync(
            shopId, toOwnerId, now, cancellationToken);

        if (result.HasChanges)
        {
            _logger.LogInformation(
                "Shop {ShopId} ownership transfer {From} → {To}: orders={Orders} merged={Merged} listings={Listings} collapsed={Collapsed} analytics={Analytics} discounts={Discounts} keywords={Keywords} origins={Origins} (rematch orders={OrdRematch} listings={ListRematch})",
                shopId,
                fromOwnerId,
                toOwnerId,
                result.OrdersTransferred,
                result.OrdersMerged,
                result.ListingsTransferred,
                result.ListingsCollapsed,
                result.AnalyticsSnapshotsTransferred,
                result.DiscountActivitiesTransferred,
                result.SavedKeywordsTransferred,
                result.ListingAddOriginsTransferred,
                result.OrdersRematched,
                result.ListingsRematched);
        }

        // Caller persists (ShopService.UpdateForAdminAsync / ReassignMismatchedOwnedDataAsync).
        return result;
    }

    /// <inheritdoc />
    public async Task<ShopOwnershipTransferResultDto> ReassignMismatchedOwnedDataAsync(
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new { s.Id, s.OwnerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var fourSellerShopIds = await ResolveFourSellerShopIdsAsync(shopId, cancellationToken);

        // Pull in orphan rows (ShopId null) that still belong to another seller but match this shop's 4Seller id.
        var staleOwnerIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OwnerId != shop.OwnerId
                        && (o.ShopId == shopId
                            || (o.FourSellerShopId != null
                                && fourSellerShopIds.Contains(o.FourSellerShopId.Value))))
            .Select(o => o.OwnerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var listingStale = await _db.Listings
            .AsNoTracking()
            .Where(l => l.OwnerId != shop.OwnerId
                        && (l.ShopId == shopId
                            || (l.FourSellerShopId != null
                                && fourSellerShopIds.Contains(l.FourSellerShopId.Value))))
            .Select(l => l.OwnerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var id in listingStale)
        {
            if (!staleOwnerIds.Contains(id))
            {
                staleOwnerIds.Add(id);
            }
        }

        var aggregate = new ShopOwnershipTransferResultDto
        {
            ShopId = shopId,
            FromOwnerId = Guid.Empty,
            ToOwnerId = shop.OwnerId,
            ShopsProcessed = 1
        };

        foreach (var fromOwnerId in staleOwnerIds)
        {
            var part = await TransferAsync(shopId, fromOwnerId, shop.OwnerId, cancellationToken);
            aggregate.OrdersRematched += part.OrdersRematched;
            aggregate.ListingsRematched += part.ListingsRematched;
            aggregate.OrdersTransferred += part.OrdersTransferred;
            aggregate.OrdersMerged += part.OrdersMerged;
            aggregate.ListingsTransferred += part.ListingsTransferred;
            aggregate.ListingsCollapsed += part.ListingsCollapsed;
            aggregate.AnalyticsSnapshotsTransferred += part.AnalyticsSnapshotsTransferred;
            aggregate.DiscountActivitiesTransferred += part.DiscountActivitiesTransferred;
            aggregate.SavedKeywordsTransferred += part.SavedKeywordsTransferred;
            aggregate.ListingAddOriginsTransferred += part.ListingAddOriginsTransferred;
            if (aggregate.FromOwnerId == Guid.Empty)
            {
                aggregate.FromOwnerId = fromOwnerId;
            }
        }

        // Collapse remaining PlatformOrderId duplicates on this shop (e.g. both owners already linked).
        aggregate.OrdersMerged += await MergeAllShopPlatformDuplicatesAsync(
            shopId, shop.OwnerId, fourSellerShopIds, cancellationToken);

        // Denorm-only mismatches when order/listing OwnerId already matches shop owner.
        var analytics = await ReassignAllMismatchedAnalyticsAsync(shopId, shop.OwnerId, cancellationToken);
        var keywords = await ReassignAllMismatchedKeywordsAsync(shopId, shop.OwnerId, cancellationToken);
        var discounts = await ReassignAllMismatchedDiscountsAsync(
            shopId, shop.OwnerId, fourSellerShopIds, cancellationToken);
        var origins = await ReassignAllMismatchedOriginsAsync(
            shop.OwnerId, fourSellerShopIds, cancellationToken);

        aggregate.AnalyticsSnapshotsTransferred += analytics;
        aggregate.SavedKeywordsTransferred += keywords;
        aggregate.DiscountActivitiesTransferred += discounts;
        aggregate.ListingAddOriginsTransferred += origins;

        var now = DateTime.UtcNow;

        // Owner already matches shop but ShopId is null (or wrong): rematch by FourSellerShopId
        // so orphan shells enter the same shop as rich twins before collapse.
        aggregate.OrdersRematched += await RematchOrdersAsync(
            shopId, shop.OwnerId, fourSellerShopIds, now, cancellationToken);
        aggregate.ListingsRematched += await RematchListingsAsync(
            shopId, shop.OwnerId, fourSellerShopIds, now, cancellationToken);

        // Persist ShopId rematch so collapse/backfill SQL sees orphans on this shop.
        if (aggregate.OrdersRematched + aggregate.ListingsRematched > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        await BackfillListingSourceUrlsFromOrderItemsAsync(
            shopId, shop.OwnerId, now, cancellationToken);

        // Always collapse even when no stale owners (shells already under shop owner).
        aggregate.ListingsCollapsed += await CollapseDuplicateListingsByTikTokProductAsync(
            shopId, shop.OwnerId, now, cancellationToken);

        if (aggregate.HasChanges || analytics + keywords + discounts + origins > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Shop {ShopId} mismatched ownership backfill → {To}: orders={Orders} merged={Merged} listings={Listings} collapsed={Collapsed}",
                shopId,
                shop.OwnerId,
                aggregate.OrdersTransferred,
                aggregate.OrdersMerged,
                aggregate.ListingsTransferred,
                aggregate.ListingsCollapsed);
        }

        return aggregate;
    }

    /// <inheritdoc />
    public async Task<ShopOwnershipTransferResultDto> ReassignAllMismatchedOwnedDataAsync(
        CancellationToken cancellationToken = default)
    {
        var mismatchedShopIds = await (
                from o in _db.Orders.AsNoTracking()
                join s in _db.Shops.AsNoTracking() on o.ShopId equals s.Id
                where o.OwnerId != s.OwnerId
                select s.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var listingShopIds = await (
                from l in _db.Listings.AsNoTracking()
                join s in _db.Shops.AsNoTracking() on l.ShopId equals s.Id
                where l.OwnerId != s.OwnerId
                select s.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var id in listingShopIds)
        {
            if (!mismatchedShopIds.Contains(id))
            {
                mismatchedShopIds.Add(id);
            }
        }

        var duplicateTikTokShopIds = await _db.Listings
            .AsNoTracking()
            .Where(l => l.ShopId != null
                        && l.TikTokProductId != null
                        && l.TikTokProductId != "")
            .GroupBy(l => new { l.ShopId, l.TikTokProductId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ShopId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var id in duplicateTikTokShopIds)
        {
            if (!mismatchedShopIds.Contains(id))
            {
                mismatchedShopIds.Add(id);
            }
        }

        // Orphan listings (ShopId null) matching a shop's FourSellerShopId — often thin shells
        // after seller transfer that never got rematched because OwnerId already equals shop owner.
        var orphanFsShopIds = await _db.Listings
            .AsNoTracking()
            .Where(l => l.ShopId == null && l.FourSellerShopId != null && l.FourSellerShopId > 0)
            .Select(l => l.FourSellerShopId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Orphan orders (ShopId null) — same gap as listings; without this they stay invisible to
        // the join-based mismatch scan above.
        var orphanOrderFsShopIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == null && o.FourSellerShopId != null && o.FourSellerShopId > 0)
            .Select(o => o.FourSellerShopId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (orphanOrderFsShopIds.Count > 0)
        {
            foreach (var id in orphanOrderFsShopIds)
            {
                if (!orphanFsShopIds.Contains(id))
                {
                    orphanFsShopIds.Add(id);
                }
            }
        }

        if (orphanFsShopIds.Count > 0)
        {
            var fsIdKeys = orphanFsShopIds.Select(id => id.ToString()).ToList();
            var orphanLinkedShopIds = await _db.Shops
                .AsNoTracking()
                .Where(s =>
                    (s.FourSellerShopId != null && fsIdKeys.Contains(s.FourSellerShopId))
                    || (s.PreviousFourSellerShopId != null
                        && fsIdKeys.Contains(s.PreviousFourSellerShopId)))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in orphanLinkedShopIds)
            {
                if (!mismatchedShopIds.Contains(id))
                {
                    mismatchedShopIds.Add(id);
                }
            }
        }

        var aggregate = new ShopOwnershipTransferResultDto
        {
            ShopId = Guid.Empty,
            FromOwnerId = Guid.Empty,
            ToOwnerId = Guid.Empty,
            ShopsProcessed = mismatchedShopIds.Count
        };

        foreach (var shopId in mismatchedShopIds)
        {
            var part = await ReassignMismatchedOwnedDataAsync(shopId, cancellationToken);
            aggregate.OrdersRematched += part.OrdersRematched;
            aggregate.ListingsRematched += part.ListingsRematched;
            aggregate.OrdersTransferred += part.OrdersTransferred;
            aggregate.OrdersMerged += part.OrdersMerged;
            aggregate.ListingsTransferred += part.ListingsTransferred;
            aggregate.ListingsCollapsed += part.ListingsCollapsed;
            aggregate.AnalyticsSnapshotsTransferred += part.AnalyticsSnapshotsTransferred;
            aggregate.DiscountActivitiesTransferred += part.DiscountActivitiesTransferred;
            aggregate.SavedKeywordsTransferred += part.SavedKeywordsTransferred;
            aggregate.ListingAddOriginsTransferred += part.ListingAddOriginsTransferred;
            if (aggregate.ToOwnerId == Guid.Empty)
            {
                aggregate.ToOwnerId = part.ToOwnerId;
            }

            if (aggregate.FromOwnerId == Guid.Empty && part.FromOwnerId != Guid.Empty)
            {
                aggregate.FromOwnerId = part.FromOwnerId;
            }
        }

        _logger.LogInformation(
            "Bulk ownership reassign: shops={Shops} orders={Orders} listings={Listings} collapsed={Collapsed} merged={Merged}",
            aggregate.ShopsProcessed,
            aggregate.OrdersTransferred,
            aggregate.ListingsTransferred,
            aggregate.ListingsCollapsed,
            aggregate.OrdersMerged);

        return aggregate;
    }

    /// <inheritdoc />
    public async Task<int> CollapseShopDuplicatesAsync(
        IReadOnlyCollection<Guid> shopIds,
        CancellationToken cancellationToken = default)
    {
        if (shopIds.Count == 0)
        {
            return 0;
        }

        var ids = shopIds.Distinct().ToList();
        var owners = await _db.Shops
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.OwnerId })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var collapsed = 0;

        foreach (var shop in owners)
        {
            try
            {
                var removed = await CollapseDuplicateListingsByTikTokProductAsync(
                    shop.Id, shop.OwnerId, now, cancellationToken);
                if (removed > 0)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    collapsed += removed;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The caller shares this DbContext, so half-applied merges and pending deletes must
                // not survive to ride along on someone else's SaveChanges.
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Post-sync collapse failed for shop {ShopId}", shop.Id);
            }
        }

        return collapsed;
    }

    /// <summary>
    /// When from/to both have the same PlatformOrderId, merge into one row owned by toOwner.
    /// </summary>
    private async Task<int> MergePlatformOrderConflictsAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        CancellationToken cancellationToken)
    {
        var movingPlatformIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OwnerId == fromOwnerId
                        && (o.ShopId == shopId
                            || (o.FourSellerShopId != null
                                && fourSellerShopIds.Contains(o.FourSellerShopId.Value)))
                        && o.PlatformOrderId != null
                        && o.PlatformOrderId != string.Empty)
            .Select(o => o.PlatformOrderId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (movingPlatformIds.Count == 0)
        {
            return 0;
        }

        var conflictIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OwnerId == toOwnerId
                        && o.PlatformOrderId != null
                        && movingPlatformIds.Contains(o.PlatformOrderId))
            .Select(o => o.PlatformOrderId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (conflictIds.Count == 0)
        {
            return 0;
        }

        var merged = 0;
        foreach (var platformOrderId in conflictIds)
        {
            var pair = await _db.Orders
                .Include(o => o.Items)
                .Where(o => o.PlatformOrderId == platformOrderId
                            && (o.OwnerId == fromOwnerId || o.OwnerId == toOwnerId))
                .ToListAsync(cancellationToken);

            if (pair.Count < 2)
            {
                continue;
            }

            var keep = pair.Aggregate((a, b) => OrderOwnershipMerge.ChooseKeep(a, b, toOwnerId));
            foreach (var drop in pair.Where(o => o.Id != keep.Id).ToList())
            {
                await OrderOwnershipMerge.MergeIntoAsync(
                    _db,
                    keep,
                    drop,
                    toOwnerId,
                    shopId,
                    preferredFourSellerOrderId: null,
                    _logger,
                    cancellationToken);
                merged++;
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges any remaining multi-owner PlatformOrderId groups linked to this shop.
    /// </summary>
    private async Task<int> MergeAllShopPlatformDuplicatesAsync(
        Guid shopId,
        Guid preferredOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        CancellationToken cancellationToken)
    {
        var linked = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.ShopId == shopId
                        || (o.FourSellerShopId != null
                            && fourSellerShopIds.Contains(o.FourSellerShopId.Value)))
            .Where(o => o.PlatformOrderId != null && o.PlatformOrderId != string.Empty)
            .ToListAsync(cancellationToken);

        var merged = 0;
        foreach (var group in linked.GroupBy(o => o.PlatformOrderId!, StringComparer.Ordinal))
        {
            var rows = group.ToList();
            if (rows.Count < 2)
            {
                continue;
            }

            var keep = rows.Aggregate((a, b) => OrderOwnershipMerge.ChooseKeep(a, b, preferredOwnerId));
            var dropped = new HashSet<Guid>();
            foreach (var drop in rows.Where(o => o.Id != keep.Id).ToList())
            {
                if (dropped.Contains(drop.Id))
                {
                    continue;
                }

                await OrderOwnershipMerge.MergeIntoAsync(
                    _db,
                    keep,
                    drop,
                    preferredOwnerId,
                    shopId,
                    preferredFourSellerOrderId: null,
                    _logger,
                    cancellationToken);
                dropped.Add(drop.Id);
                merged++;
            }
        }

        return merged;
    }

    /// <summary>
    /// A merge earlier in this pass marks the losing row deleted without flushing, so the row is still
    /// in the store and comes back from the next query as the same tracked instance. Counting it as
    /// transferred overstates the result and writes onto a row that is about to disappear.
    /// </summary>
    private bool IsPendingDelete(object entity)
        => _db.Entry(entity).State == EntityState.Deleted;

    private async Task<int> RematchOrdersAsync(
        Guid shopId,
        Guid fromOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (fourSellerShopIds.Count == 0)
        {
            return 0;
        }

        // Load then filter ShopId client-side: EF/InMemory can drop NULL ShopId rows on `!= shopId`.
        var orders = (await _db.Orders
                .Where(o => o.OwnerId == fromOwnerId
                            && o.FourSellerShopId != null
                            && fourSellerShopIds.Contains(o.FourSellerShopId.Value))
                .ToListAsync(cancellationToken))
            .Where(o => (o.ShopId == null || o.ShopId != shopId) && !IsPendingDelete(o))
            .ToList();

        foreach (var order in orders)
        {
            order.ShopId = shopId;
            order.UpdatedAt = now;
        }

        return orders.Count;
    }

    private async Task<int> RematchListingsAsync(
        Guid shopId,
        Guid fromOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (fourSellerShopIds.Count == 0)
        {
            return 0;
        }

        // Load then filter ShopId client-side: EF/InMemory can drop NULL ShopId rows on `!= shopId`.
        var listings = (await _db.Listings
                .Where(l => l.OwnerId == fromOwnerId
                            && l.FourSellerShopId != null
                            && fourSellerShopIds.Contains(l.FourSellerShopId.Value))
                .ToListAsync(cancellationToken))
            .Where(l => (l.ShopId == null || l.ShopId != shopId) && !IsPendingDelete(l))
            .ToList();

        foreach (var listing in listings)
        {
            listing.ShopId = shopId;
            listing.UpdatedAt = now;
        }

        return listings.Count;
    }

    private async Task<int> ReassignOrdersAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var orders = (await _db.Orders
                .Where(o => o.OwnerId == fromOwnerId
                            && (o.ShopId == shopId
                                || (o.FourSellerShopId != null
                                    && fourSellerShopIds.Contains(o.FourSellerShopId.Value))))
                .ToListAsync(cancellationToken))
            .Where(o => !IsPendingDelete(o))
            .ToList();

        foreach (var order in orders)
        {
            order.OwnerId = toOwnerId;
            order.ShopId = shopId;
            order.UpdatedAt = now;
        }

        return orders.Count;
    }

    private async Task<int> ReassignListingsAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var listings = (await _db.Listings
                .Where(l => l.OwnerId == fromOwnerId
                            && (l.ShopId == shopId
                                || (l.FourSellerShopId != null
                                    && fourSellerShopIds.Contains(l.FourSellerShopId.Value))))
                .ToListAsync(cancellationToken))
            .Where(l => !IsPendingDelete(l))
            .ToList();

        foreach (var listing in listings)
        {
            listing.OwnerId = toOwnerId;
            listing.ShopId = shopId;
            listing.UpdatedAt = now;
        }

        return listings.Count;
    }

    private async Task<int> ReassignAnalyticsAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ListingAnalyticsSnapshots
            .Where(r => r.ShopId == shopId && r.OwnerId == fromOwnerId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignDiscountsAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProductDiscountActivities
            .Where(r => r.OwnerUserId == fromOwnerId
                        && (r.ShopId == shopId
                            || fourSellerShopIds.Contains(r.FourSellerShopId)))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.OwnerUserId = toOwnerId;
            if (row.ShopId != shopId)
            {
                row.ShopId = shopId;
            }

            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignSavedKeywordsAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProductOpportunitySavedKeywords
            .Where(r => r.ShopId == shopId && r.OwnerId == fromOwnerId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignListingAddOriginsAsync(
        Guid fromOwnerId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (fourSellerShopIds.Count == 0)
        {
            return 0;
        }

        var rows = await _db.ListingAddOrigins
            .Where(r => r.OwnerId == fromOwnerId
                        && fourSellerShopIds.Contains(r.FourSellerShopId))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignAllMismatchedAnalyticsAsync(
        Guid shopId,
        Guid toOwnerId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.ListingAnalyticsSnapshots
            .Where(r => r.ShopId == shopId && r.OwnerId != toOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignAllMismatchedKeywordsAsync(
        Guid shopId,
        Guid toOwnerId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.ProductOpportunitySavedKeywords
            .Where(r => r.ShopId == shopId && r.OwnerId != toOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignAllMismatchedDiscountsAsync(
        Guid shopId,
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.ProductDiscountActivities
            .Where(r => r.OwnerUserId != toOwnerId
                        && (r.ShopId == shopId
                            || fourSellerShopIds.Contains(r.FourSellerShopId)))
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OwnerUserId = toOwnerId;
            row.ShopId = shopId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    private async Task<int> ReassignAllMismatchedOriginsAsync(
        Guid toOwnerId,
        IReadOnlyList<long> fourSellerShopIds,
        CancellationToken cancellationToken)
    {
        if (fourSellerShopIds.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var rows = await _db.ListingAddOrigins
            .Where(r => fourSellerShopIds.Contains(r.FourSellerShopId) && r.OwnerId != toOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OwnerId = toOwnerId;
            row.UpdatedAt = now;
        }

        return rows.Count;
    }

    /// <summary>
    /// Best-effort: fill empty Listing.SourceUrl from order lines on the same shop
    /// matched by variation SellerSku. Never overwrites an existing URL.
    /// </summary>
    private async Task<int> BackfillListingSourceUrlsFromOrderItemsAsync(
        Guid shopId,
        Guid ownerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Shop-scoped: ownership already reassigned (or backfill after Đồng bộ). Do not require
        // OwnerId in the SQL filter — InMemory/pending tracker edge cases can miss just-reassigned rows.
        var listingIds = await _db.Listings
            .Where(l => l.ShopId == shopId
                        && (l.SourceUrl == null || l.SourceUrl == ""))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        if (listingIds.Count == 0)
        {
            return 0;
        }

        var skuRows = await _db.ListingVariations
            .AsNoTracking()
            .Where(v => listingIds.Contains(v.ListingId)
                        && v.SellerSku != null
                        && v.SellerSku != "")
            .Select(v => new { v.ListingId, v.SellerSku })
            .ToListAsync(cancellationToken);

        if (skuRows.Count == 0)
        {
            return 0;
        }

        var orderIdsOnShop = await _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        if (orderIdsOnShop.Count == 0)
        {
            return 0;
        }

        var orderUrls = await _db.OrderItems
            .AsNoTracking()
            .Where(oi => orderIdsOnShop.Contains(oi.OrderId)
                         && oi.SourceUrl != null
                         && oi.SourceUrl != ""
                         && oi.SellerSku != null
                         && oi.SellerSku != "")
            .Select(oi => new { oi.SellerSku, oi.SourceUrl })
            .ToListAsync(cancellationToken);

        if (orderUrls.Count == 0)
        {
            return 0;
        }

        var urlBySku = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in orderUrls)
        {
            var sku = row.SellerSku!.Trim();
            var url = row.SourceUrl!.Trim();
            if (sku.Length == 0 || url.Length == 0 || urlBySku.ContainsKey(sku))
            {
                continue;
            }

            urlBySku[sku] = url;
        }

        var urlByListingId = new Dictionary<Guid, string>();
        foreach (var row in skuRows)
        {
            if (urlByListingId.ContainsKey(row.ListingId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.SellerSku))
            {
                continue;
            }

            if (!urlBySku.TryGetValue(row.SellerSku.Trim(), out var url))
            {
                continue;
            }

            urlByListingId[row.ListingId] = url;
        }

        if (urlByListingId.Count == 0)
        {
            return 0;
        }

        var idsToFill = urlByListingId.Keys.ToList();
        var listings = await _db.Listings
            .Where(l => idsToFill.Contains(l.Id))
            .ToListAsync(cancellationToken);

        var filled = 0;
        foreach (var listing in listings)
        {
            if (!urlByListingId.TryGetValue(listing.Id, out var url))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(listing.SourceUrl))
            {
                continue;
            }

            listing.SourceUrl = url;
            listing.UpdatedAt = now;
            filled++;
        }

        if (filled > 0)
        {
            _logger.LogInformation(
                "Shop {ShopId} backfilled SourceUrl on {Count} listings for owner {OwnerId}",
                shopId,
                filled,
                ownerId);
        }

        return filled;
    }

    /// <summary>
    /// Every 4Seller shop id this shop has answered to. Reconnecting under another 4Seller account
    /// mints a new internal id while older Orders / Listings keep the old one, so the rematch has to
    /// look for both.
    /// </summary>
    private async Task<List<long>> ResolveFourSellerShopIdsAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new { s.FourSellerShopId, s.PreviousFourSellerShopId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var ids = new List<long>();
        foreach (var raw in new[] { shop.FourSellerShopId, shop.PreviousFourSellerShopId })
        {
            if (!string.IsNullOrWhiteSpace(raw)
                && long.TryParse(raw.Trim(), out var parsed)
                && parsed > 0
                && !ids.Contains(parsed))
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }

    /// <summary>
    /// Denorm owner columns on rows keyed by <c>ShopId</c> alone. They follow the shop, so a handover
    /// leaves them pointing at the previous seller unless they are rewritten here.
    /// </summary>
    private async Task ReassignShopScopedOwnerDenormAsync(
        Guid shopId,
        Guid fromOwnerId,
        Guid toOwnerId,
        CancellationToken cancellationToken)
    {
        var autoPublishRuns = await _db.ListingAutoPublishRuns
            .Where(r => r.ShopId == shopId && r.OwnerId == fromOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in autoPublishRuns)
        {
            row.OwnerId = toOwnerId;
        }

        var taskCompletions = await _db.ShopTaskCompletions
            .Where(r => r.ShopId == shopId && r.OwnerId == fromOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in taskCompletions)
        {
            row.OwnerId = toOwnerId;
        }

        var syncLogs = await _db.TikTokShopSyncLogs
            .Where(r => r.ShopId == shopId && r.OwnerId == fromOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var row in syncLogs)
        {
            row.OwnerId = toOwnerId;
        }

        // The receiving seller's delta cursor says "seen everything up to T", but the shop it just
        // gained is full of rows older than T. Dropping the cursor forces one full scan so those
        // rows are read instead of skipped forever.
        var watermarks = await _db.ListingSyncWatermarks
            .Where(w => w.OwnerId == toOwnerId)
            .ToListAsync(cancellationToken);
        if (watermarks.Count > 0)
        {
            _db.ListingSyncWatermarks.RemoveRange(watermarks);
            _logger.LogInformation(
                "Cleared {Count} listing sync watermarks for seller {OwnerId} after gaining shop {ShopId}",
                watermarks.Count,
                toOwnerId,
                shopId);
        }
    }

    /// <summary>
    /// Shop rows are unique per <c>(OwnerId, ExternalShopId)</c>, so one TikTok shop can exist twice
    /// when it was handed over by creating a second row instead of reassigning the first. Duplicate
    /// listings then sit on sibling shops and never meet. Returns every shop id covering this
    /// TikTok shop, current shop first.
    /// </summary>
    private async Task<List<Guid>> ResolveSiblingShopIdsAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var externalShopId = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => s.ExternalShopId)
            .FirstOrDefaultAsync(cancellationToken);

        var ids = new List<Guid> { shopId };
        if (string.IsNullOrWhiteSpace(externalShopId))
        {
            return ids;
        }

        var normalized = externalShopId.Trim();
        var siblings = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id != shopId && s.ExternalShopId == normalized)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        ids.AddRange(siblings);
        return ids;
    }

    /// <summary>
    /// Copies facts the thin row holds but the kept row is missing. A shell seeded by a
    /// status-only sync has no SourceUrl / supplier link / payload, and the reverse happens
    /// when the shell is the one that got enriched, so the merge runs field by field.
    /// </summary>
    private static bool MergeListingFacts(Listing keep, Listing drop)
    {
        var changed = false;

        bool Take<T>(Func<Listing, T?> read, Action<T> write) where T : struct
        {
            if (read(keep).HasValue || read(drop) is not T value)
            {
                return false;
            }

            write(value);
            return true;
        }

        bool TakeText(Func<Listing, string?> read, Action<string> write)
        {
            if (!string.IsNullOrWhiteSpace(read(keep)) || string.IsNullOrWhiteSpace(read(drop)))
            {
                return false;
            }

            write(read(drop)!);
            return true;
        }

        changed |= TakeText(l => l.SourceUrl, v => keep.SourceUrl = v);
        changed |= TakeText(l => l.RawPayload, v => keep.RawPayload = v);
        changed |= TakeText(l => l.SupplierDescriptionHtml, v => keep.SupplierDescriptionHtml = v);
        changed |= TakeText(l => l.CategoryId, v => keep.CategoryId = v);
        changed |= TakeText(l => l.CategoryName, v => keep.CategoryName = v);
        changed |= TakeText(l => l.MainImage, v => keep.MainImage = v);
        changed |= TakeText(l => l.AddedFrom, v => keep.AddedFrom = v);
        changed |= TakeText(l => l.SearchTermsJson, v => keep.SearchTermsJson = v);
        changed |= TakeText(l => l.ProductHighlightsJson, v => keep.ProductHighlightsJson = v);
        changed |= TakeText(l => l.AppliedPricingJson, v => keep.AppliedPricingJson = v);
        changed |= TakeText(l => l.PublishSource, v => keep.PublishSource = v);
        changed |= TakeText(l => l.WarehouseId, v => keep.WarehouseId = v);
        changed |= Take(l => l.SupplierProductInfoId, v => keep.SupplierProductInfoId = v);
        changed |= Take(l => l.ReviewCount, v => keep.ReviewCount = v);
        changed |= Take(l => l.AverageRating, v => keep.AverageRating = v);

        // The rewrite flags describe DescriptionHtml, so they only travel with it.
        if (string.IsNullOrWhiteSpace(keep.DescriptionHtml)
            && !string.IsNullOrWhiteSpace(drop.DescriptionHtml))
        {
            keep.DescriptionHtml = drop.DescriptionHtml;
            keep.DescriptionRewritten = drop.DescriptionRewritten;
            keep.DescriptionImagesInserted = drop.DescriptionImagesInserted;
            changed = true;
        }

        if (drop.LastPushAt is DateTime pushedAt && (keep.LastPushAt is null || keep.LastPushAt < pushedAt))
        {
            keep.LastPushAt = pushedAt;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Keep the richest listing per TikTok product across the shop and its siblings; delete thinner
    /// shells after copying their facts onto the kept row. Repoints analytics FKs before delete.
    /// </summary>
    private async Task<int> CollapseDuplicateListingsByTikTokProductAsync(
        Guid shopId,
        Guid shopOwnerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var shopIds = await ResolveSiblingShopIdsAsync(shopId, cancellationToken);

        // Narrow to the duplicated products first: this runs after every sync, and a busy shop
        // holds far more listings than twins.
        var duplicateProductIds = await _db.Listings
            .Where(l => l.ShopId != null
                        && shopIds.Contains(l.ShopId.Value)
                        && l.TikTokProductId != null
                        && l.TikTokProductId != "")
            .GroupBy(l => l.TikTokProductId!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        if (duplicateProductIds.Count == 0)
        {
            return 0;
        }

        var listings = await _db.Listings
            .Where(l => l.ShopId != null
                        && shopIds.Contains(l.ShopId.Value)
                        && l.TikTokProductId != null
                        && duplicateProductIds.Contains(l.TikTokProductId))
            .ToListAsync(cancellationToken);

        var groups = listings
            .GroupBy(l => l.TikTokProductId!.Trim(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0)
        {
            return 0;
        }

        var dropIds = new List<Guid>();
        var keepByDrop = new Dictionary<Guid, Guid>();

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(l => !string.IsNullOrWhiteSpace(l.RawPayload))
                .ThenByDescending(l => !string.IsNullOrWhiteSpace(l.SourceUrl))
                .ThenByDescending(l => l.VariationCount)
                .ThenByDescending(l => l.UpdatedAt ?? l.CreatedAt)
                .ThenByDescending(l => l.FourSellerListingId)
                .ToList();

            var keep = ordered[0];
            var keepChanged = false;
            if (keep.OwnerId != shopOwnerId)
            {
                keep.OwnerId = shopOwnerId;
                keepChanged = true;
            }

            if (keep.ShopId != shopId)
            {
                keep.ShopId = shopId;
                keepChanged = true;
            }

            foreach (var drop in ordered.Skip(1))
            {
                keepChanged |= MergeListingFacts(keep, drop);
                dropIds.Add(drop.Id);
                keepByDrop[drop.Id] = keep.Id;
            }

            if (keepChanged)
            {
                keep.UpdatedAt = now;
            }
        }

        if (dropIds.Count == 0)
        {
            return 0;
        }

        var analytics = await _db.ListingAnalyticsSnapshots
            .Where(a => a.ListingId != null && dropIds.Contains(a.ListingId.Value))
            .ToListAsync(cancellationToken);

        foreach (var snap in analytics)
        {
            if (snap.ListingId is Guid dropId && keepByDrop.TryGetValue(dropId, out var keepId))
            {
                snap.ListingId = keepId;
                snap.OwnerId = shopOwnerId;
                snap.UpdatedAt = now;
            }
        }

        await AdoptVariationsFromDroppedListingsAsync(dropIds, keepByDrop, now, cancellationToken);

        var toRemove = listings.Where(l => dropIds.Contains(l.Id)).ToList();
        _db.Listings.RemoveRange(toRemove);

        _logger.LogInformation(
            "Shop {ShopId} collapsed {Count} duplicate TikTok listings onto shop owner {OwnerId}",
            shopId,
            dropIds.Count,
            shopOwnerId);

        return dropIds.Count;
    }

    /// <summary>
    /// Variations cascade-delete with the shell, so a kept row holding none inherits them from its
    /// richest dropped twin. Persisted before the caller removes the shells so the repoint UPDATE
    /// cannot be reordered behind the DELETE.
    /// </summary>
    private async Task AdoptVariationsFromDroppedListingsAsync(
        IReadOnlyList<Guid> dropIds,
        IReadOnlyDictionary<Guid, Guid> keepByDrop,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var keepIds = keepByDrop.Values.Distinct().ToList();
        var keepsWithVariations = await _db.ListingVariations
            .AsNoTracking()
            .Where(v => keepIds.Contains(v.ListingId))
            .Select(v => v.ListingId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var emptyKeeps = keepIds.Except(keepsWithVariations).ToHashSet();
        if (emptyKeeps.Count == 0)
        {
            return;
        }

        var movable = await _db.ListingVariations
            .Where(v => dropIds.Contains(v.ListingId))
            .ToListAsync(cancellationToken);

        var moved = 0;
        var filled = new HashSet<Guid>();
        foreach (var group in movable.GroupBy(v => v.ListingId).OrderByDescending(g => g.Count()))
        {
            if (!keepByDrop.TryGetValue(group.Key, out var keepId)
                || !emptyKeeps.Contains(keepId)
                || !filled.Add(keepId))
            {
                continue;
            }

            foreach (var variation in group)
            {
                variation.ListingId = keepId;
                variation.UpdatedAt = now;
                moved++;
            }
        }

        if (moved > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Moved {Count} variations onto {Keeps} kept listings before collapse",
                moved,
                filled.Count);
        }
    }
}
