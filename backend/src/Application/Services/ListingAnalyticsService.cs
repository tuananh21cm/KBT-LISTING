using System.Text.Json;
using Application.DTOs.Listing;
using Application.Interfaces;
using Application.Validators.Listing;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// TikTok Compass product metrics: extension ingest (upsert per Vietnam day) plus the paged
/// read model used by both portals.
/// </summary>
public class ListingAnalyticsService : IListingAnalyticsService
{
    /// <summary>Product ids per <c>IN (…)</c> batch.</summary>
    private const int IdChunkSize = 500;

    /// <summary>Days offered in the snapshot date picker.</summary>
    private const int MaxDateOptions = 90;

    /// <summary>Cap on the raw <c>stats_v3.total</c> blob we persist per row.</summary>
    private const int MaxRawStatsLength = 8000;

    private static readonly JsonSerializerOptions RawJsonOptions = new() { WriteIndented = false };

    private readonly IApplicationDbContext _db;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;
    private readonly IListingDelistSuggestionService _delistSuggestions;

    /// <summary>Creates the service.</summary>
    public ListingAnalyticsService(
        IApplicationDbContext db,
        ITikTokShopSyncLogWriter tikTokSyncLog,
        IListingDelistSuggestionService delistSuggestions)
    {
        _db = db;
        _tikTokSyncLog = tikTokSyncLog;
        _delistSuggestions = delistSuggestions;
    }

    /// <inheritdoc />
    public async Task<SyncListingAnalyticsResultDto> SyncAsync(
        Guid ownerId,
        SyncListingAnalyticsDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = SyncListingAnalyticsValidator.ResolveExternalShopId(dto);
        Guid? shopId = null;
        try
        {
        var shop = await ResolveOwnedShopAsync(
            ownerId,
            externalShopId,
            cancellationToken);

        shopId = shop.Id;

        var now = DateTime.UtcNow;
        var snapshotDate = ListingAnalyticsMetrics.SnapshotDateFor(now);
        var (rangeStart, rangeEnd) = ListingAnalyticsMetrics.ResolveRange(
            dto.RangeStart,
            dto.RangeEnd,
            ListingAnalyticsMetrics.PacificToday(now));
        var preset = string.IsNullOrWhiteSpace(dto.Preset) ? "last7days" : dto.Preset.Trim();

        var skipped = 0;
        var byProductId = new Dictionary<string, SyncListingAnalyticsItemDto>(StringComparer.Ordinal);
        foreach (var item in dto.Items ?? [])
        {
            var productId = (item.ProductId ?? string.Empty).Trim();
            if (productId.Length == 0 || !byProductId.TryAdd(productId, item))
            {
                skipped++;
            }
        }

        var result = new SyncListingAnalyticsResultDto
        {
            ShopId = shop.Id,
            SnapshotDate = snapshotDate,
            Skipped = skipped
        };

        if (byProductId.Count == 0)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shop.Id,
                TikTokShopSyncTypes.Analytics,
                TikTokShopSyncStatuses.Completed,
                cancellationToken: cancellationToken);
            return result;
        }

        var productIds = byProductId.Keys.ToList();
        var existing = await LoadExistingAsync(shop.Id, snapshotDate, productIds, cancellationToken);
        var listingLinks = await LoadListingLinksAsync(shop.Id, ownerId, productIds, cancellationToken);

        foreach (var productId in productIds)
        {
            var item = byProductId[productId];
            if (!existing.TryGetValue(productId, out var row))
            {
                row = new ListingAnalyticsSnapshot
                {
                    ShopId = shop.Id,
                    TikTokProductId = productId,
                    SnapshotDate = snapshotDate,
                    CreatedAt = now
                };
                _db.ListingAnalyticsSnapshots.Add(row);
                result.Inserted++;
            }
            else
            {
                row.UpdatedAt = now;
                result.Updated++;
            }

            row.OwnerId = shop.OwnerId;
            row.RangeStart = rangeStart;
            row.RangeEnd = rangeEnd;
            row.Preset = preset;
            row.ProductName = Trim(item.ProductName, 500);
            row.MainImage = Trim(item.MainImage, 1000);
            row.Currency = string.IsNullOrWhiteSpace(item.Currency)
                ? "USD"
                : item.Currency.Trim().ToUpperInvariant();

            row.Gmv = NormalizeMoney(item.Gmv);
            row.Orders = NormalizeCount(item.Orders);
            row.Impressions = NormalizeLong(item.Impressions);
            row.Clicks = NormalizeLong(item.Clicks);
            row.Ctr = NormalizeRatio(item.Ctr);
            row.AddToCartUnits = item.AddToCartUnits.HasValue
                ? NormalizeCount(item.AddToCartUnits)
                : null;
            row.AddToCartBuyers = item.AddToCartBuyers.HasValue
                ? NormalizeCount(item.AddToCartBuyers)
                : null;

            row.GmvRatio = NormalizeRatio(item.GmvRatio);
            row.OrdersRatio = NormalizeRatio(item.OrdersRatio);
            row.ImpressionsRatio = NormalizeRatio(item.ImpressionsRatio);
            row.ClicksRatio = NormalizeRatio(item.ClicksRatio);
            row.CtrRatio = NormalizeRatio(item.CtrRatio);
            row.AddToCartUnitsRatio = NormalizeRatio(item.AddToCartUnitsRatio);
            row.AddToCartBuyersRatio = NormalizeRatio(item.AddToCartBuyersRatio);

            row.RawStatsJson = SerializeRawStats(item.RawStats);
            row.SyncedAt = now;

            // Keep the previous link when this batch cannot resolve one — the listing may be
            // mid-sync and the next run re-links it.
            if (listingLinks.TryGetValue(productId, out var link))
            {
                row.ListingId = link;
            }

            if (row.ListingId.HasValue)
            {
                result.Linked++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Persist delist counters for the optimize board chip (delistCount/scanned).
        await _delistSuggestions.ComputeAndPersistAsync(shop.Id, snapshotDate, cancellationToken);

        await _tikTokSyncLog.WriteAsync(
            ownerId,
            dto.RunId,
            externalShopId,
            shop.Id,
            TikTokShopSyncTypes.Analytics,
            TikTokShopSyncStatuses.Completed,
            cancellationToken: cancellationToken);

        return result;
        }
        catch (Exception ex)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shopId,
                TikTokShopSyncTypes.Analytics,
                TikTokShopSyncStatuses.Failed,
                ex.Message,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ListingAnalyticsPageDto> ListAsync(
        ListingAnalyticsListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize < 1 ? 50 : query.PageSize, 1, 100);
        var pageCurrent = Math.Max(1, query.Page ?? query.PageCurrent);

        var scope = BuildScope(query.OwnerUserId, query.ShopId);
        var page = new ListingAnalyticsPageDto
        {
            PageCurrent = pageCurrent,
            PageSize = pageSize
        };

        var day = query.Date
            ?? await scope.MaxAsync(x => (DateOnly?)x.SnapshotDate, cancellationToken);
        if (!day.HasValue)
        {
            return page;
        }

        page.SnapshotDate = day;

        var rows = scope.Where(x => x.SnapshotDate == day.Value);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var needle = search.ToLowerInvariant();
            rows = rows.Where(x =>
                x.TikTokProductId.Contains(needle)
                || (x.ProductName != null && x.ProductName.ToLower().Contains(needle))
                || (x.Listing != null
                    && x.Listing.ProductName != null
                    && x.Listing.ProductName.ToLower().Contains(needle)));
        }

        page.Total = await rows.CountAsync(cancellationToken);
        if (page.Total == 0)
        {
            return page;
        }

        var totals = await rows
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gmv = g.Sum(x => x.Gmv),
                Orders = g.Sum(x => x.Orders),
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                AddToCartUnits = g.Sum(x => x.AddToCartUnits ?? 0),
                LastSyncedAt = g.Max(x => x.SyncedAt),
                RangeStart = g.Min(x => x.RangeStart),
                RangeEnd = g.Max(x => x.RangeEnd)
            })
            .FirstAsync(cancellationToken);

        page.LastSyncedAt = totals.LastSyncedAt;
        page.RangeStart = totals.RangeStart;
        page.RangeEnd = totals.RangeEnd;
        page.Totals = new ListingAnalyticsTotalsDto
        {
            Products = page.Total,
            Gmv = totals.Gmv,
            Orders = totals.Orders,
            Impressions = totals.Impressions,
            Clicks = totals.Clicks,
            AddToCartUnits = totals.AddToCartUnits,
            Ctr = ListingAnalyticsMetrics.CtrFrom(totals.Clicks, totals.Impressions)
        };

        var sort = (query.Sort ?? "gmv").Trim().ToLowerInvariant();
        var desc = query.SortDir?.Trim().ToLowerInvariant() switch
        {
            "asc" => false,
            "desc" => true,
            _ => sort != "name"
        };

        var pageRows = await ApplySort(rows, sort, desc)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RowProjection(
                x.Id,
                x.ShopId,
                x.Shop.Name,
                x.Shop.ProfileName,
                x.OwnerId,
                x.ListingId,
                x.TikTokProductId,
                x.ProductName,
                x.Listing != null ? x.Listing.ProductName : null,
                x.MainImage,
                x.Listing != null ? x.Listing.MainImage : null,
                x.Listing != null ? x.Listing.AddedFrom : null,
                x.Listing != null ? x.Listing.ReviewCount : null,
                x.Listing != null ? x.Listing.AverageRating : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.StoreTitle
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.StoreUrl
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.ShopRank
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.StoreAttention
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.ShippingDays
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    && x.Listing.SupplierProductInfo.Seller != null
                    ? x.Listing.SupplierProductInfo.Seller.ShippingDayPercent
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    ? x.Listing.SupplierProductInfo.IsLocalProduct
                    : null,
                x.Listing != null && x.Listing.SupplierProductInfo != null
                    ? x.Listing.SupplierProductInfo.IsSupportQuickShip
                    : null,
                x.Currency,
                x.SnapshotDate,
                x.RangeStart,
                x.RangeEnd,
                x.Preset,
                x.Gmv,
                x.Orders,
                x.Impressions,
                x.Clicks,
                x.Ctr,
                x.AddToCartUnits,
                x.AddToCartBuyers,
                x.GmvRatio,
                x.OrdersRatio,
                x.ImpressionsRatio,
                x.ClicksRatio,
                x.CtrRatio,
                x.AddToCartUnitsRatio,
                x.AddToCartBuyersRatio,
                x.SyncedAt))
            .ToListAsync(cancellationToken);

        var items = pageRows.Select(ToItem).ToList();
        await FillOwnerNamesAsync(items, cancellationToken);

        var previousDay = ListingAnalyticsMetrics.ComparisonSnapshotDate(day.Value);
        var hasComparison = await scope.AnyAsync(x => x.SnapshotDate == previousDay, cancellationToken);
        page.PreviousSnapshotDate = hasComparison ? previousDay : null;
        if (hasComparison)
        {
            await FillMissingRatiosAsync(items, previousDay, cancellationToken);
        }

        page.Items = items;
        return page;
    }

    /// <inheritdoc />
    public async Task<ListingAnalyticsDatesDto> GetSnapshotDatesAsync(
        Guid? ownerUserId,
        Guid? shopId,
        CancellationToken cancellationToken = default)
    {
        var dates = await BuildScope(ownerUserId, shopId)
            .Select(x => x.SnapshotDate)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(MaxDateOptions)
            .ToListAsync(cancellationToken);

        return new ListingAnalyticsDatesDto
        {
            Dates = dates,
            Latest = dates.Count > 0 ? dates[0] : null
        };
    }

    private IQueryable<ListingAnalyticsSnapshot> BuildScope(Guid? ownerUserId, Guid? shopId)
    {
        var scope = _db.ListingAnalyticsSnapshots.AsNoTracking().AsQueryable();
        if (ownerUserId.HasValue)
        {
            scope = scope.Where(x => x.OwnerId == ownerUserId.Value);
        }

        if (shopId.HasValue)
        {
            scope = scope.Where(x => x.ShopId == shopId.Value);
        }

        return scope;
    }

    private static IQueryable<ListingAnalyticsSnapshot> ApplySort(
        IQueryable<ListingAnalyticsSnapshot> rows,
        string sort,
        bool desc)
    {
        return sort switch
        {
            "orders" => desc
                ? rows.OrderByDescending(x => x.Orders).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.Orders).ThenByDescending(x => x.Gmv),
            "impressions" => desc
                ? rows.OrderByDescending(x => x.Impressions).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.Impressions).ThenByDescending(x => x.Gmv),
            "clicks" => desc
                ? rows.OrderByDescending(x => x.Clicks).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.Clicks).ThenByDescending(x => x.Gmv),
            "ctr" => desc
                ? rows.OrderByDescending(x => x.Ctr).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.Ctr).ThenByDescending(x => x.Gmv),
            "atc" => desc
                ? rows.OrderByDescending(x => x.AddToCartUnits).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.AddToCartUnits).ThenByDescending(x => x.Gmv),
            "name" => desc
                ? rows.OrderByDescending(x => x.ProductName).ThenByDescending(x => x.Gmv)
                : rows.OrderBy(x => x.ProductName).ThenByDescending(x => x.Gmv),
            _ => desc
                ? rows.OrderByDescending(x => x.Gmv).ThenByDescending(x => x.Orders)
                : rows.OrderBy(x => x.Gmv).ThenByDescending(x => x.Orders)
        };
    }

    private static ListingAnalyticsItemDto ToItem(RowProjection row)
    {
        var hasTikTokRatio = row.GmvRatio.HasValue
            || row.OrdersRatio.HasValue
            || row.ImpressionsRatio.HasValue
            || row.ClicksRatio.HasValue
            || row.CtrRatio.HasValue
            || row.AddToCartUnitsRatio.HasValue
            || row.AddToCartBuyersRatio.HasValue;

        return new ListingAnalyticsItemDto
        {
            Id = row.Id,
            ShopId = row.ShopId,
            ShopName = row.ShopName,
            ShopProfileName = row.ShopProfileName,
            OwnerId = row.OwnerId,
            ListingId = row.ListingId,
            TikTokProductId = row.TikTokProductId,
            ProductName = FirstNonEmpty(row.ProductName, row.ListingProductName),
            MainImage = FirstImage(FirstNonEmpty(row.MainImage, row.ListingMainImage)),
            AddedFrom = row.AddedFrom,
            ReviewCount = row.ReviewCount,
            AverageRating = row.AverageRating,
            StoreTitle = row.StoreTitle,
            StoreUrl = row.StoreUrl,
            ShopRank = row.ShopRank,
            StoreAttention = row.StoreAttention,
            ShippingDays = row.ShippingDays,
            ShippingDayPercent = row.ShippingDayPercent,
            IsLocalProduct = row.IsLocalProduct,
            IsSupportQuickShip = row.IsSupportQuickShip,
            Currency = row.Currency,
            SnapshotDate = row.SnapshotDate,
            RangeStart = row.RangeStart,
            RangeEnd = row.RangeEnd,
            Preset = row.Preset,
            Gmv = row.Gmv,
            Orders = row.Orders,
            Impressions = row.Impressions,
            Clicks = row.Clicks,
            Ctr = row.Ctr ?? ListingAnalyticsMetrics.CtrFrom(row.Clicks, row.Impressions),
            AddToCartUnits = row.AddToCartUnits,
            AddToCartBuyers = row.AddToCartBuyers,
            GmvRatio = row.GmvRatio,
            OrdersRatio = row.OrdersRatio,
            ImpressionsRatio = row.ImpressionsRatio,
            ClicksRatio = row.ClicksRatio,
            CtrRatio = row.CtrRatio,
            AddToCartUnitsRatio = row.AddToCartUnitsRatio,
            AddToCartBuyersRatio = row.AddToCartBuyersRatio,
            RatioSource = hasTikTokRatio ? "tiktok" : null,
            SyncedAt = row.SyncedAt
        };
    }

    private async Task FillOwnerNamesAsync(
        List<ListingAnalyticsItemDto> items,
        CancellationToken cancellationToken)
    {
        var ownerIds = items.Select(i => i.OwnerId).Distinct().ToList();
        if (ownerIds.Count == 0)
        {
            return;
        }

        var owners = await _db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);
        var byId = owners.ToDictionary(o => o.Id);

        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.OwnerId, out var owner))
            {
                continue;
            }

            item.OwnerUserName = owner.UserName;
            item.OwnerFullName = owner.FullName;
        }
    }

    /// <summary>
    /// Fills per-metric deltas TikTok did not send by comparing with the snapshot taken
    /// <see cref="ListingAnalyticsMetrics.WindowDays"/> VN days earlier — the prior
    /// <c>last7days</c> window when sync runs daily. Only the page rows are loaded (≤ page size).
    /// </summary>
    private async Task FillMissingRatiosAsync(
        List<ListingAnalyticsItemDto> items,
        DateOnly previousDay,
        CancellationToken cancellationToken)
    {
        var pending = items.Where(NeedsRatio).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var shopIds = pending.Select(i => i.ShopId).Distinct().ToList();
        var productIds = pending.Select(i => i.TikTokProductId).Distinct().ToList();

        var previous = await _db.ListingAnalyticsSnapshots.AsNoTracking()
            .Where(x => x.SnapshotDate == previousDay
                && shopIds.Contains(x.ShopId)
                && productIds.Contains(x.TikTokProductId))
            .Select(x => new
            {
                x.ShopId,
                x.TikTokProductId,
                x.Gmv,
                x.Orders,
                x.Impressions,
                x.Clicks,
                x.Ctr,
                x.AddToCartUnits,
                x.AddToCartBuyers
            })
            .ToListAsync(cancellationToken);
        if (previous.Count == 0)
        {
            return;
        }

        var byKey = previous.ToDictionary(p => (p.ShopId, p.TikTokProductId));

        foreach (var item in pending)
        {
            if (!byKey.TryGetValue((item.ShopId, item.TikTokProductId), out var prev))
            {
                continue;
            }

            var filled = false;
            filled |= TryFill(() => item.GmvRatio, v => item.GmvRatio = v, item.Gmv, prev.Gmv);
            filled |= TryFill(() => item.OrdersRatio, v => item.OrdersRatio = v, item.Orders, prev.Orders);
            filled |= TryFill(
                () => item.ImpressionsRatio,
                v => item.ImpressionsRatio = v,
                item.Impressions,
                prev.Impressions);
            filled |= TryFill(() => item.ClicksRatio, v => item.ClicksRatio = v, item.Clicks, prev.Clicks);
            filled |= TryFill(() => item.CtrRatio, v => item.CtrRatio = v, item.Ctr, prev.Ctr);
            filled |= TryFill(
                () => item.AddToCartUnitsRatio,
                v => item.AddToCartUnitsRatio = v,
                item.AddToCartUnits,
                prev.AddToCartUnits);
            filled |= TryFill(
                () => item.AddToCartBuyersRatio,
                v => item.AddToCartBuyersRatio = v,
                item.AddToCartBuyers,
                prev.AddToCartBuyers);

            if (filled && item.RatioSource is null)
            {
                item.RatioSource = "computed";
            }
        }
    }

    private static bool NeedsRatio(ListingAnalyticsItemDto item) =>
        !item.GmvRatio.HasValue
        || !item.OrdersRatio.HasValue
        || !item.ImpressionsRatio.HasValue
        || !item.ClicksRatio.HasValue
        || !item.CtrRatio.HasValue
        || !item.AddToCartUnitsRatio.HasValue
        || !item.AddToCartBuyersRatio.HasValue;

    private static bool TryFill(
        Func<decimal?> read,
        Action<decimal?> write,
        decimal? current,
        decimal? previous)
    {
        if (read() is not null)
        {
            return false;
        }

        var delta = ListingAnalyticsMetrics.Delta(current, previous);
        if (delta is null)
        {
            return false;
        }

        write(delta);
        return true;
    }

    private async Task<Dictionary<string, ListingAnalyticsSnapshot>> LoadExistingAsync(
        Guid shopId,
        DateOnly snapshotDate,
        List<string> productIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ListingAnalyticsSnapshot>(StringComparer.Ordinal);
        foreach (var chunk in Chunk(productIds))
        {
            var rows = await _db.ListingAnalyticsSnapshots
                .Where(x => x.ShopId == shopId
                    && x.SnapshotDate == snapshotDate
                    && chunk.Contains(x.TikTokProductId))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                map[row.TikTokProductId] = row;
            }
        }

        return map;
    }

    /// <summary>
    /// Maps TikTok product id → local listing id, preferring a listing already bound to this shop
    /// and then the most recently synced one (a seller can mirror the same product twice).
    /// </summary>
    private async Task<Dictionary<string, Guid>> LoadListingLinksAsync(
        Guid shopId,
        Guid ownerId,
        List<string> productIds,
        CancellationToken cancellationToken)
    {
        var best = new Dictionary<string, (Guid Id, bool ShopMatch, DateTime SyncedAt)>(StringComparer.Ordinal);
        foreach (var chunk in Chunk(productIds))
        {
            var rows = await _db.Listings.AsNoTracking()
                .Where(l => l.TikTokProductId != null
                    && chunk.Contains(l.TikTokProductId)
                    && (l.ShopId == shopId || l.OwnerId == ownerId))
                .Select(l => new { l.Id, l.TikTokProductId, l.ShopId, l.LastSyncedAt })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                var productId = row.TikTokProductId!;
                var shopMatch = row.ShopId == shopId;
                var better = !best.TryGetValue(productId, out var current)
                    || (shopMatch && !current.ShopMatch)
                    || (shopMatch == current.ShopMatch && row.LastSyncedAt > current.SyncedAt);
                if (better)
                {
                    best[productId] = (row.Id, shopMatch, row.LastSyncedAt);
                }
            }
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.Ordinal);
    }

    private static IEnumerable<List<string>> Chunk(List<string> ids)
    {
        for (var i = 0; i < ids.Count; i += IdChunkSize)
        {
            yield return ids.GetRange(i, Math.Min(IdChunkSize, ids.Count - i));
        }
    }

    private static string? SerializeRawStats(JsonElement? raw)
    {
        if (raw is null
            || raw.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(raw.Value, RawJsonOptions);
        return json.Length > MaxRawStatsLength ? null : json;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim()
        : !string.IsNullOrWhiteSpace(second) ? second.Trim()
        : null;

    /// <summary>Listing images can be pipe-separated; the table only needs the first one.</summary>
    private static string? FirstImage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var first = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static decimal NormalizeMoney(decimal? value) =>
        ListingAnalyticsMetrics.NormalizeMoney(value);

    private static int NormalizeCount(int? value) => ListingAnalyticsMetrics.NormalizeCount(value);

    private static long NormalizeLong(long? value) => ListingAnalyticsMetrics.NormalizeLong(value);

    private static decimal? NormalizeRatio(decimal? value) =>
        ListingAnalyticsMetrics.NormalizeRatio(value);

    private async Task<Shop> ResolveOwnedShopAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken)
    {
        var id = (externalShopId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            throw new BusinessException("externalShopId / sellerId không được để trống.");
        }

        var shop = await _db.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == ownerId && s.ExternalShopId == id, cancellationToken);
        if (shop is not null)
        {
            return shop;
        }

        var other = await (
            from s in _db.Shops.AsNoTracking()
            join u in _db.Users.AsNoTracking() on s.OwnerId equals u.Id into owners
            from u in owners.DefaultIfEmpty()
            where s.ExternalShopId == id
            select new
            {
                s.Name,
                OwnerUserName = u != null ? u.UserName : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (other is not null)
        {
            var ownerLabel = string.IsNullOrWhiteSpace(other.OwnerUserName)
                ? "(unknown)"
                : other.OwnerUserName;
            throw new NotFoundException(
                $"Shop «{other.Name}» (externalShopId «{id}») thuộc seller «{ownerLabel}» — " +
                "đăng nhập đúng tài khoản owner trên extension rồi sync lại.");
        }

        throw new NotFoundException(
            $"Chưa có shop với externalShopId «{id}» — tạo/gán shop trên admin trước.");
    }

    private sealed record RowProjection(
        Guid Id,
        Guid ShopId,
        string ShopName,
        string? ShopProfileName,
        Guid OwnerId,
        Guid? ListingId,
        string TikTokProductId,
        string? ProductName,
        string? ListingProductName,
        string? MainImage,
        string? ListingMainImage,
        string? AddedFrom,
        int? ReviewCount,
        decimal? AverageRating,
        string? StoreTitle,
        string? StoreUrl,
        decimal? ShopRank,
        int? StoreAttention,
        int? ShippingDays,
        decimal? ShippingDayPercent,
        bool? IsLocalProduct,
        bool? IsSupportQuickShip,
        string Currency,
        DateOnly SnapshotDate,
        DateOnly RangeStart,
        DateOnly RangeEnd,
        string Preset,
        decimal Gmv,
        int Orders,
        long Impressions,
        long Clicks,
        decimal? Ctr,
        int? AddToCartUnits,
        int? AddToCartBuyers,
        decimal? GmvRatio,
        decimal? OrdersRatio,
        decimal? ImpressionsRatio,
        decimal? ClicksRatio,
        decimal? CtrRatio,
        decimal? AddToCartUnitsRatio,
        decimal? AddToCartBuyersRatio,
        DateTime SyncedAt);
}
