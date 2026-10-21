namespace Application.Services;

/// <summary>
/// In-memory index of local shops for one order-sync run.
/// Resolves 4Seller shop id against <b>all</b> shops (true owner attribution when seller A's
/// page returns an order for seller B's shop). Name fallback stays scoped to the syncing owner
/// to avoid cross-seller name collisions.
/// </summary>
public sealed class OrderSyncShopIndex
{
    private readonly Dictionary<long, ShopLink> _byFourSellerId;
    private readonly Dictionary<Guid, Dictionary<string, List<ShopLink>>> _byOwnerNormalizedName;

    /// <summary>Local shop row used during order upsert attribution.</summary>
    public sealed record ShopLink(Guid ShopId, Guid OwnerId, string? Name);

    /// <summary>
    /// Builds the index from projected shop rows (Id, OwnerId, FourSellerShopId, Name).
    /// Duplicate 4Seller ids keep the first occurrence.
    /// </summary>
    public OrderSyncShopIndex(IEnumerable<(Guid Id, Guid OwnerId, string? FourSellerShopId, string? Name)> shops)
    {
        _byFourSellerId = new Dictionary<long, ShopLink>();
        _byOwnerNormalizedName = new Dictionary<Guid, Dictionary<string, List<ShopLink>>>();

        foreach (var shop in shops)
        {
            var link = new ShopLink(shop.Id, shop.OwnerId, shop.Name);
            if (!string.IsNullOrWhiteSpace(shop.FourSellerShopId)
                && long.TryParse(shop.FourSellerShopId.Trim(), out var fsId)
                && fsId > 0)
            {
                _byFourSellerId.TryAdd(fsId, link);
            }

            var nameKey = NormalizeShopName(shop.Name);
            if (nameKey.Length == 0)
            {
                continue;
            }

            if (!_byOwnerNormalizedName.TryGetValue(shop.OwnerId, out var byName))
            {
                byName = new Dictionary<string, List<ShopLink>>(StringComparer.Ordinal);
                _byOwnerNormalizedName[shop.OwnerId] = byName;
            }

            if (!byName.TryGetValue(nameKey, out var list))
            {
                list = [];
                byName[nameKey] = list;
            }

            list.Add(link);
        }
    }

    /// <summary>
    /// Shop ids owned by <paramref name="ownerId"/> (for reclaim / foreign-order gates).
    /// </summary>
    public HashSet<Guid> GetShopIdsForOwner(Guid ownerId)
    {
        var set = new HashSet<Guid>();
        foreach (var link in _byFourSellerId.Values)
        {
            if (link.OwnerId == ownerId)
            {
                set.Add(link.ShopId);
            }
        }

        if (_byOwnerNormalizedName.TryGetValue(ownerId, out var byName))
        {
            foreach (var list in byName.Values)
            {
                foreach (var link in list)
                {
                    set.Add(link.ShopId);
                }
            }
        }

        return set;
    }

    /// <summary>
    /// 4Seller internal shop ids owned by <paramref name="ownerId"/>.
    /// </summary>
    public HashSet<long> GetFourSellerShopIdsForOwner(Guid ownerId)
    {
        var set = new HashSet<long>();
        foreach (var (fsId, link) in _byFourSellerId)
        {
            if (link.OwnerId == ownerId)
            {
                set.Add(fsId);
            }
        }

        return set;
    }

    /// <summary>
    /// Resolves by 4Seller shop id across the whole system (any owner).
    /// </summary>
    public ShopLink? ResolveByFourSellerShopId(long? fourSellerShopId)
    {
        if (fourSellerShopId is not > 0)
        {
            return null;
        }

        return _byFourSellerId.TryGetValue(fourSellerShopId.Value, out var link) ? link : null;
    }

    /// <summary>
    /// Name fallback scoped to <paramref name="syncOwnerId"/> only when exactly one shop matches.
    /// </summary>
    public ShopLink? ResolveByNameForOwner(string? shopName, Guid syncOwnerId)
    {
        var nameKey = NormalizeShopName(shopName);
        if (nameKey.Length == 0
            || !_byOwnerNormalizedName.TryGetValue(syncOwnerId, out var byName)
            || !byName.TryGetValue(nameKey, out var candidates)
            || candidates.Count != 1)
        {
            return null;
        }

        return candidates[0];
    }

    /// <summary>
    /// Preferred resolve: global 4Seller id, then owner-scoped unique name.
    /// </summary>
    public ShopLink? Resolve(long? fourSellerShopId, string? shopName, Guid syncOwnerId) =>
        ResolveByFourSellerShopId(fourSellerShopId)
        ?? ResolveByNameForOwner(shopName, syncOwnerId);

    private static string NormalizeShopName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
