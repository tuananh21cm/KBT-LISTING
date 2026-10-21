using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Helpers;

namespace Application.Services;

/// <inheritdoc />
public class ListingAddOriginService : IListingAddOriginService
{
    private readonly IApplicationDbContext _db;

    /// <summary>Creates the service.</summary>
    public ListingAddOriginService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task UpsertFromPostHtmlAsync(
        Guid ownerId,
        string? sourceUrl,
        IReadOnlyList<long> shopIds,
        string? addedFrom,
        CancellationToken cancellationToken = default)
    {
        var origin = ListingAddOriginApply.Normalize(addedFrom);
        if (origin is null)
        {
            return;
        }

        var key = SourceUrlKeyHelper.Normalize(sourceUrl);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var shops = shopIds.Where(id => id > 0).Distinct().ToList();
        if (shops.Count == 0)
        {
            return;
        }

        var existing = await _db.ListingAddOrigins
            .Where(o => o.OwnerId == ownerId && o.SourceUrlKey == key && shops.Contains(o.FourSellerShopId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var shopId in shops)
        {
            var row = existing.Find(o => o.FourSellerShopId == shopId);
            if (row is null)
            {
                _db.ListingAddOrigins.Add(new ListingAddOrigin
                {
                    Id = Guid.NewGuid(),
                    OwnerId = ownerId,
                    SourceUrlKey = key,
                    FourSellerShopId = shopId,
                    AddedFrom = origin,
                    CreatedAt = now
                });
            }
            else
            {
                row.AddedFrom = origin;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ApplyToListingsAsync(
        IReadOnlyCollection<Listing> listings,
        CancellationToken cancellationToken = default)
    {
        if (listings.Count == 0)
        {
            return;
        }

        var candidates = new List<(Listing Listing, string Key, long ShopId)>();
        foreach (var listing in listings)
        {
            if (!string.IsNullOrWhiteSpace(listing.AddedFrom))
            {
                continue;
            }

            if (listing.FourSellerShopId is not > 0)
            {
                continue;
            }

            var key = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            candidates.Add((listing, key, listing.FourSellerShopId.Value));
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var ownerIds = candidates.Select(c => c.Listing.OwnerId).Distinct().ToList();
        var keys = candidates.Select(c => c.Key).Distinct().ToList();
        var shopIds = candidates.Select(c => c.ShopId).Distinct().ToList();

        var origins = await _db.ListingAddOrigins.AsNoTracking()
            .Where(o =>
                ownerIds.Contains(o.OwnerId) &&
                keys.Contains(o.SourceUrlKey) &&
                shopIds.Contains(o.FourSellerShopId))
            .ToListAsync(cancellationToken);

        ListingAddOriginApply.Apply(listings, origins);
    }
}
