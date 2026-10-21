using Application.DTOs.Supplier;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Helpers;

namespace Application.Services;

/// <summary>Read-only supplier shop catalog with listing counts and item-GMV attribution.</summary>
public class SupplierSellerInfoService : ISupplierSellerInfoService
{
    private readonly IApplicationDbContext _db;

    /// <summary>Initializes the service.</summary>
    public SupplierSellerInfoService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<SupplierSellerListPageDto> ListAsync(
        SupplierSellerListQuery query,
        CancellationToken cancellationToken = default)
    {
        var (pageCurrent, pageSize, skip) = SupplierSellerQuery.NormalizePaging(
            query.PageCurrent,
            query.PageSize);
        var (rangeStartUtc, rangeEndUtc, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(
            DateTime.UtcNow,
            query.Period,
            query.FromDate,
            query.ToDate);

        var ownerId = query.OwnerUserId;
        var sellers = await LoadFilteredSellersAsync(query, ownerId, cancellationToken);
        var sellerIds = sellers.Select(s => s.Id).ToList();
        var money = sellerIds.Count == 0
            ? []
            : await LoadMoneyAsync(ownerId, sellerIds, rangeStartUtc, rangeEndUtc, cancellationToken);
        var (productCounts, listingCounts) = sellerIds.Count == 0
            ? (new Dictionary<Guid, int>(), new Dictionary<Guid, int>())
            : await LoadCountsAsync(ownerId, sellerIds, cancellationToken);

        var rows = sellers
            .Select(s => MapListItem(s, productCounts, listingCounts, money))
            .OrderByDescending(r => r.ItemRevenue)
            .ThenByDescending(r => r.ListingCount)
            .ThenBy(r => r.StoreTitle ?? r.StoreCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (skip >= rows.Count && rows.Count > 0)
        {
            pageCurrent = 1;
            skip = 0;
        }

        return new SupplierSellerListPageDto
        {
            Total = rows.Count,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            FromDate = fromLocal.ToString("yyyy-MM-dd"),
            ToDate = toLocal.ToString("yyyy-MM-dd"),
            Items = rows.Skip(skip).Take(pageSize).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<SupplierSellerDetailDto> GetByIdAsync(
        Guid id,
        SupplierSellerListQuery query,
        CancellationToken cancellationToken = default)
    {
        var seller = await _db.SupplierSellerInfos.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SupplierSellerRow(
                s.Id,
                s.Platform,
                s.StoreCode,
                s.StoreUrl,
                s.StoreLogo,
                s.StoreTitle,
                s.SoldByTips,
                s.ShopRank,
                s.StoreAttention,
                s.ShippingDays,
                s.ShippingDayPercent,
                s.ShipsFromTips))
            .FirstOrDefaultAsync(cancellationToken);
        if (seller is null)
        {
            throw new NotFoundException("Không tìm thấy shop NCC.");
        }

        var ownerId = query.OwnerUserId;
        if (ownerId.HasValue)
        {
            var visible = await _db.Listings.AsNoTracking()
                .AnyAsync(
                    l => l.OwnerId == ownerId.Value
                         && l.SupplierProductInfo != null
                         && l.SupplierProductInfo.SupplierSellerInfoId == id,
                    cancellationToken);
            if (!visible)
            {
                throw new NotFoundException("Không tìm thấy shop NCC.");
            }
        }

        var (rangeStartUtc, rangeEndUtc, fromLocal, toLocal) = SupplierSellerQuery.ResolveUtcRange(
            DateTime.UtcNow,
            query.Period,
            query.FromDate,
            query.ToDate);
        var sellerIds = new List<Guid> { id };
        var money = await LoadMoneyAsync(ownerId, sellerIds, rangeStartUtc, rangeEndUtc, cancellationToken);
        var (productCounts, listingCounts) = await LoadCountsAsync(ownerId, sellerIds, cancellationToken);
        var item = MapListItem(seller, productCounts, listingCounts, money);

        var (productPage, productSize, productSkip) = SupplierSellerQuery.NormalizePaging(
            query.ProductPageCurrent,
            query.ProductPageSize,
            SupplierSellerQuery.DefaultProductPageSize,
            SupplierSellerQuery.MaxProductPageSize);

        var productsQ = _db.SupplierProductInfos.AsNoTracking()
            .Where(p => p.SupplierSellerInfoId == id);
        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            productsQ = productsQ.Where(p => p.Listings.Any(l => l.OwnerId == oid));
        }

        var productTotal = await productsQ.CountAsync(cancellationToken);
        if (productSkip >= productTotal && productTotal > 0)
        {
            productPage = 1;
            productSkip = 0;
        }

        var productRows = await productsQ
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(productSkip)
            .Take(productSize)
            .Select(p => new
            {
                p.Id,
                p.SourceUrl,
                p.SourceUrlKey,
                p.Stock,
                p.ReviewCount,
                p.AverageRating,
                p.IsLocalProduct,
                p.IsSupportQuickShip
            })
            .ToListAsync(cancellationToken);

        var productIds = productRows.Select(p => p.Id).ToList();
        var listingRows = productIds.Count == 0
            ? []
            : await (
                    from l in _db.Listings.AsNoTracking()
                    where l.SupplierProductInfoId != null && productIds.Contains(l.SupplierProductInfoId.Value)
                    where !ownerId.HasValue || l.OwnerId == ownerId.Value
                    orderby l.ProductName, l.Id
                    select new
                    {
                        l.Id,
                        ProductId = l.SupplierProductInfoId!.Value,
                        l.ProductName,
                        l.PageType,
                        l.SiteStatus,
                        l.MainImage,
                        ShopName = l.Shop != null ? l.Shop.Name : null,
                        OwnerName = l.Owner.FullName
                    })
                .ToListAsync(cancellationToken);

        var listingsByProduct = listingRows
            .GroupBy(l => l.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => new SupplierSellerListingDto
                {
                    Id = l.Id,
                    ProductName = l.ProductName,
                    PageType = l.PageType,
                    SiteStatus = l.SiteStatus,
                    MainImage = l.MainImage,
                    ShopName = l.ShopName,
                    OwnerName = ownerId.HasValue ? null : l.OwnerName
                }).ToList());

        return new SupplierSellerDetailDto
        {
            Id = item.Id,
            Platform = item.Platform,
            StoreCode = item.StoreCode,
            StoreUrl = item.StoreUrl,
            StoreLogo = item.StoreLogo,
            StoreTitle = item.StoreTitle,
            ShopRank = item.ShopRank,
            StoreAttention = item.StoreAttention,
            ShippingDays = item.ShippingDays,
            ShippingDayPercent = item.ShippingDayPercent,
            ShipsFromTips = item.ShipsFromTips,
            ProductCount = item.ProductCount,
            ListingCount = item.ListingCount,
            OrderCount = item.OrderCount,
            ItemQty = item.ItemQty,
            ItemRevenue = item.ItemRevenue,
            FromDate = fromLocal.ToString("yyyy-MM-dd"),
            ToDate = toLocal.ToString("yyyy-MM-dd"),
            ProductTotal = productTotal,
            ProductPageCurrent = productPage,
            ProductPageSize = productSize,
            Products = productRows.Select(p =>
            {
                var listings = listingsByProduct.GetValueOrDefault(p.Id) ?? [];
                return new SupplierSellerProductDto
                {
                    Id = p.Id,
                    SourceUrl = p.SourceUrl,
                    SourceUrlKey = p.SourceUrlKey,
                    Stock = p.Stock,
                    ReviewCount = p.ReviewCount,
                    AverageRating = p.AverageRating,
                    IsLocalProduct = p.IsLocalProduct,
                    IsSupportQuickShip = p.IsSupportQuickShip,
                    ListingCount = listings.Count,
                    Listings = listings
                };
            }).ToList()
        };
    }

    private async Task<List<SupplierSellerRow>> LoadFilteredSellersAsync(
        SupplierSellerListQuery query,
        Guid? ownerId,
        CancellationToken cancellationToken)
    {
        var q = _db.SupplierSellerInfos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToLower();
            q = q.Where(s => s.Platform.ToLower() == platform);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            q = q.Where(s =>
                (s.StoreTitle != null && s.StoreTitle.ToLower().Contains(search))
                || s.StoreCode.ToLower().Contains(search)
                || (s.SoldByTips != null && s.SoldByTips.ToLower().Contains(search)));
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            q = q.Where(s => s.Products.Any(p => p.Listings.Any(l => l.OwnerId == oid)));
        }

        return await q
            .Select(s => new SupplierSellerRow(
                s.Id,
                s.Platform,
                s.StoreCode,
                s.StoreUrl,
                s.StoreLogo,
                s.StoreTitle,
                s.SoldByTips,
                s.ShopRank,
                s.StoreAttention,
                s.ShippingDays,
                s.ShippingDayPercent,
                s.ShipsFromTips))
            .ToListAsync(cancellationToken);
    }

    private async Task<(Dictionary<Guid, int> Products, Dictionary<Guid, int> Listings)> LoadCountsAsync(
        Guid? ownerId,
        IReadOnlyCollection<Guid> sellerIds,
        CancellationToken cancellationToken)
    {
        var productsQ = _db.SupplierProductInfos.AsNoTracking()
            .Where(p => p.SupplierSellerInfoId != null && sellerIds.Contains(p.SupplierSellerInfoId.Value));
        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            productsQ = productsQ.Where(p => p.Listings.Any(l => l.OwnerId == oid));
        }

        var productCounts = await productsQ
            .GroupBy(p => p.SupplierSellerInfoId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var listingsQ = _db.Listings.AsNoTracking()
            .Where(l => l.SupplierProductInfoId != null
                        && l.SupplierProductInfo!.SupplierSellerInfoId != null
                        && sellerIds.Contains(l.SupplierProductInfo.SupplierSellerInfoId.Value));
        if (ownerId.HasValue)
        {
            listingsQ = listingsQ.Where(l => l.OwnerId == ownerId.Value);
        }

        var listingCounts = await listingsQ
            .GroupBy(l => l.SupplierProductInfo!.SupplierSellerInfoId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return (productCounts, listingCounts);
    }

    private async Task<Dictionary<Guid, SupplierSellerMoneyAgg>> LoadMoneyAsync(
        Guid? ownerId,
        IReadOnlyCollection<Guid> sellerIds,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken cancellationToken)
    {
        var skuLinksQ =
            from l in _db.Listings.AsNoTracking()
            where l.SupplierProductInfoId != null
            join p in _db.SupplierProductInfos.AsNoTracking() on l.SupplierProductInfoId equals p.Id
            where p.SupplierSellerInfoId != null && sellerIds.Contains(p.SupplierSellerInfoId.Value)
            join v in _db.ListingVariations.AsNoTracking() on l.Id equals v.ListingId
            where v.SellerSku != null && v.SellerSku != ""
            select new { l.OwnerId, v.SellerSku, SellerId = p.SupplierSellerInfoId!.Value };

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            skuLinksQ = skuLinksQ.Where(x => x.OwnerId == oid);
        }

        var skuLinks = await skuLinksQ
            .OrderBy(x => x.OwnerId)
            .ThenBy(x => x.SellerSku)
            .ThenBy(x => x.SellerId)
            .ToListAsync(cancellationToken);
        var skuMap = SupplierSellerQuery.BuildSkuSellerMap(
            skuLinks.Select(x => (x.OwnerId, x.SellerSku!, x.SellerId)));
        if (skuMap.Count == 0)
        {
            return [];
        }

        var skuLowers = skuMap.Keys.Select(k => k.SkuLower).Distinct().ToList();
        var ownerIds = ownerId.HasValue
            ? null
            : skuMap.Keys.Select(k => k.OwnerId).Distinct().ToList();

        var ordersQ = _db.Orders.AsNoTracking().AsQueryable();
        if (ownerId.HasValue)
        {
            ordersQ = ordersQ.Where(o => o.OwnerId == ownerId.Value);
        }
        else if (ownerIds is { Count: > 0 })
        {
            ordersQ = ordersQ.Where(o => ownerIds.Contains(o.OwnerId));
        }

        ordersQ = ordersQ.Where(o =>
            o.PlatformOrderStatus != "CANCELLED"
            && o.PlatformOrderStatus != "CANCELED"
            && o.PlatformOrderStatus != "REFUND"
            && o.PlatformOrderStatus != "REFUNDED"
            && !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "")
                 && (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")));
        ordersQ = ordersQ.Where(o =>
            o.Shop != null
            && o.Shop.Status == Shared.Constants.ShopStatuses.Active
            && o.Shop.ReceivingBankAccountId != null);
        ordersQ = ordersQ.Where(o =>
            (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc
            && (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc);

        var lines = await ordersQ
            .SelectMany(o => o.Items
                .Where(i => i.SellerSku != null && i.SellerSku != "" && skuLowers.Contains(i.SellerSku.ToLower()))
                .Select(i => new SupplierSellerOrderLine(
                    o.Id,
                    o.OwnerId,
                    i.SellerSku,
                    i.Quantity,
                    i.ItemPrice)))
            .ToListAsync(cancellationToken);

        return SupplierSellerQuery.AggregateMoney(lines, skuMap);
    }

    private static SupplierSellerListItemDto MapListItem(
        SupplierSellerRow seller,
        IReadOnlyDictionary<Guid, int> productCounts,
        IReadOnlyDictionary<Guid, int> listingCounts,
        IReadOnlyDictionary<Guid, SupplierSellerMoneyAgg> money)
    {
        money.TryGetValue(seller.Id, out var agg);
        return new SupplierSellerListItemDto
        {
            Id = seller.Id,
            Platform = seller.Platform,
            StoreCode = seller.StoreCode,
            StoreUrl = seller.StoreUrl ?? SupplierPdpMetrics.BuildStoreUrl(seller.Platform, seller.StoreCode),
            StoreLogo = seller.StoreLogo,
            StoreTitle = SupplierSellerQuery.DisplayTitle(seller.StoreTitle, seller.SoldByTips),
            ShopRank = seller.ShopRank,
            StoreAttention = seller.StoreAttention,
            ShippingDays = seller.ShippingDays,
            ShippingDayPercent = seller.ShippingDayPercent,
            ShipsFromTips = seller.ShipsFromTips,
            ProductCount = productCounts.GetValueOrDefault(seller.Id),
            ListingCount = listingCounts.GetValueOrDefault(seller.Id),
            OrderCount = agg?.OrderCount ?? 0,
            ItemQty = agg?.ItemQty ?? 0,
            ItemRevenue = agg?.ItemRevenue ?? 0
        };
    }

    private sealed record SupplierSellerRow(
        Guid Id,
        string Platform,
        string StoreCode,
        string? StoreUrl,
        string? StoreLogo,
        string? StoreTitle,
        string? SoldByTips,
        decimal? ShopRank,
        int? StoreAttention,
        int? ShippingDays,
        decimal? ShippingDayPercent,
        string? ShipsFromTips);
}
