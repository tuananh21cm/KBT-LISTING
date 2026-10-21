using System.Globalization;
using Application.DTOs.Dashboard;
using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Domain.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Lightweight admin dashboard aggregates (VN timezone, same formulas as Telegram summaries).
/// </summary>
public class DashboardService : IDashboardService
{
    private const int SellerRankLimit = 20;
    private const int ShopFinanceLimit = 12;
    private const int TopProductsLimit = 10;
    private const string TimezoneId = "Asia/Ho_Chi_Minh";

    private readonly IApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>
    /// Initializes the dashboard service.
    /// </summary>
    public DashboardService(IApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<DashboardSummaryDto> GetSummaryAsync(
        DashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        var tz = ResolveVietnamTz();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var period = NormalizePeriod(query.Period);
        var include = ParseIncludeSections(query.Include);
        bool Want(string section) => include is null || include.Contains(section);
        var ordersOnly = IsOrdersOnlyMetrics(query.Metrics);

        var (fromLocal, toLocalInclusive) = ResolveSummaryRange(localNow.Date, query);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified),
            tz);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(toLocalInclusive.AddDays(1), DateTimeKind.Unspecified),
            tz);
        var fromDate = fromLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toLocalInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query);

        // Period-scoped orders (same clock as KPIs / trends).
        var periodOrdersQ = ordersQ.Where(o =>
            (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
            (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc);

        var orderStatusCounts = new OrderStatusCountsDto();
        if (Want("orderstatus"))
        {
            orderStatusCounts = await GetOrderStatusCountsAsync(periodOrdersQ, cancellationToken);
        }

        var periodOrderCount = 0;
        decimal periodRevenue = 0;
        decimal periodCost = 0;
        decimal periodCoupon = 0;
        decimal periodProfit = 0;
        decimal? periodProfitMargin = null;
        decimal periodIncome = 0;
        decimal periodCardSpend = 0;
        var costFromFulfil = false;

        if (Want("kpis"))
        {
            // KPI Orders + Revenue: Active shop + đã gắn thẻ nhận tiền, exclude CANCELLED / REFUND.
            var periodOkOrdersQ = WhereMoneyEligible(periodOrdersQ);

            periodOrderCount = await periodOkOrdersQ.CountAsync(cancellationToken);

            periodRevenue = await periodOkOrdersQ
                .SumAsync(
                    o => o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                    cancellationToken);

            // Cost + Coupon: one ledger scan (Debit − Credit; Σ CouponAmount).
            // Cost = Lịch sử mua hàng / former cardSpend KPI; Coupon = cột Coupon (không trừ Cost).
            (periodCost, periodCoupon) = await SumFulfilSpendAndCouponAsync(
                query, rangeStartUtc, rangeEndUtc, cancellationToken);
            costFromFulfil = true;
            periodProfit = periodRevenue - periodCost;
            // Markup on actual purchase cost (fulfil ledger), not profit/revenue.
            periodProfitMargin = periodCost > 0
                ? Math.Round(periodProfit / periodCost * 100m, 2, MidpointRounding.AwayFromZero)
                : null;
            periodIncome = await SumReceivingIncomeAsync(query, rangeStartUtc, rangeEndUtc, cancellationToken);
        }

        // cardSpend is deprecated (alias of Cost). Only scan when kpis did not already compute it.
        if (Want("cardspend") && !costFromFulfil)
        {
            periodCardSpend = (await SumFulfilSpendAndCouponAsync(
                query, rangeStartUtc, rangeEndUtc, cancellationToken)).Cost;
        }
        else if (costFromFulfil)
        {
            periodCardSpend = periodCost;
        }

        var listings = new DashboardListingsDto();
        if (Want("listings"))
        {
            // Same listed clock as trends: PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt.
            var listingsQ = ApplyListingFilters(_db.Listings.AsNoTracking(), query)
                .Where(l =>
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) >= rangeStartUtc &&
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) < rangeEndUtc);

            var listingPageCounts = await listingsQ
                .GroupBy(l => l.PageType)
                .Select(g => new { PageType = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var listingSiteCounts = await listingsQ
                .GroupBy(l => l.SiteStatus ?? string.Empty)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            int ListingPageCount(string pageType) =>
                listingPageCounts.FirstOrDefault(x =>
                    string.Equals(x.PageType, pageType, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

            int ListingSiteCount(string status) =>
                listingSiteCounts.FirstOrDefault(x =>
                    string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

            listings = new DashboardListingsDto
            {
                Draft = ListingPageCount(ListingPageTypes.Draft),
                Published = ListingPageCount(ListingPageTypes.Published),
                SyncErrorCount = 0,
                Active = ListingSiteCount(ListingSiteStatuses.Active),
                Inactive = ListingSiteCount(ListingSiteStatuses.Inactive),
                Pending = 0
            };
        }

        var shops = new DashboardShopsDto();
        if (Want("shops"))
        {
            var shopsQ = ApplyShopFilters(_db.Shops.AsNoTracking(), query);
            var shopStatusCounts = await shopsQ
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            int ShopStatusCount(string status) =>
                shopStatusCounts.FirstOrDefault(x =>
                    string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

            shops = new DashboardShopsDto
            {
                Total = shopStatusCounts.Sum(x => x.Count),
                Active = ShopStatusCount(ShopStatuses.Active),
                Inactive = ShopStatusCount(ShopStatuses.Deactivated),
                Suspended = ShopStatusCount(ShopStatuses.Suspended),
                Connected = 0
            };
        }

        List<DashboardSellerRankDto> sellersToday = [];
        List<DashboardShopFinanceDto> shopsFinance = [];
        List<DashboardTopProductDto> topProducts = [];

        var needSellersIdentity = Want("sellers") || Want("shopsfinance") || Want("topproducts");
        if (needSellersIdentity)
        {
            // One Identity round-trip shared by ranking / shop finance / top products.
            var sellers = await GetActiveSellersAsync(cancellationToken);

            if (Want("sellers"))
            {
                // Charts share pies only need orders/revenue — skip listed scan unless full summary.
                var includeListed = include is null;
                sellersToday = await BuildSellerRankingAsync(
                    query,
                    rangeStartUtc,
                    rangeEndUtc,
                    sellers,
                    includeListed,
                    ordersOnly,
                    cancellationToken);
            }

            if (Want("shopsfinance"))
            {
                shopsFinance = await BuildShopFinanceAsync(
                    query,
                    rangeStartUtc,
                    rangeEndUtc,
                    sellers,
                    ordersOnly,
                    cancellationToken);
            }

            if (Want("topproducts"))
            {
                topProducts = await BuildTopProductsAsync(
                    query,
                    rangeStartUtc,
                    rangeEndUtc,
                    sellers,
                    cancellationToken);
            }
        }

        return new DashboardSummaryDto
        {
            Timezone = TimezoneId,
            Period = period,
            FromDate = fromDate,
            ToDate = toDate,
            Today = new DashboardTodayDto
            {
                Date = toDate,
                OrderCount = periodOrderCount,
                Revenue = periodRevenue,
                Cost = periodCost,
                Coupon = periodCoupon,
                Profit = periodProfit,
                ProfitMargin = periodProfitMargin,
                Income = periodIncome,
                CardSpend = periodCardSpend,
                // Listed trend comes from /trends; skip extra listing-date scan here.
                ListedCount = 0
            },
            OrderStatusCounts = orderStatusCounts,
            Listings = listings,
            Shops = shops,
            // Product catalog counts are not shown on Dashboard / stats pages.
            Products = new DashboardProductsDto(),
            SellersToday = sellersToday,
            ShopsFinance = shopsFinance,
            TopProducts = topProducts
        };
    }

    /// <inheritdoc />
    public async Task<DashboardTrendsDto> GetTrendsAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default)
    {
        const int maxSeriesEntities = 8;
        const string othersId = "__others__";
        const string unassignedId = "__unassigned__";

        var tz = ResolveVietnamTz();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var grain = NormalizeGrain(query.Grain);
        var groupBy = NormalizeGroupBy(query.GroupBy);
        var (startLocalDate, endLocalDate) = ResolveTrendsRange(localNow.Date, query);

        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocalDate, DateTimeKind.Unspecified),
            tz);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(endLocalDate.AddDays(1), DateTimeKind.Unspecified),
            tz);

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query);
        var includeListed = query.IncludeListed != false;
        var ordersOnly = IsOrdersOnlyMetrics(query.Metrics);
        var revenueOnly = IsRevenueOnlyMetrics(query.Metrics);

        // Aggregate-only path (Dashboard + most stats charts): skip shop/owner joins.
        // metrics=orders: skip Items/cost/revenue joins (Orders stats column chart).
        // metrics=revenue: keep revenue, skip Items/cost joins (Revenue stats trend/heatmap).
        List<(
            DateTime At,
            bool IsOk,
            bool IsCanceled,
            bool IsRefund,
            decimal Revenue,
            decimal? SupplierTotal,
            Guid OwnerId,
            Guid? ShopId,
            string? ShopName,
            Guid? ShopOwnerId)> orderRows;

        if (ordersOnly && groupBy is not ("seller" or "shop"))
        {
            var atRows = await ordersQ
                .Where(o =>
                    (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                    (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                .Select(o => new
                {
                    At = o.OrderCreateTime ?? o.CreatedAt,
                    o.PlatformOrderStatus,
                    o.OrderStatus
                })
                .ToListAsync(cancellationToken);

            orderRows = atRows
                .Select(r =>
                {
                    var (isOk, isCanceled, isRefund) =
                        ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                    return (
                        r.At,
                        isOk,
                        isCanceled,
                        isRefund,
                        0m,
                        (decimal?)null,
                        Guid.Empty,
                        (Guid?)null,
                        (string?)null,
                        (Guid?)null);
                })
                .ToList();
        }
        else if (revenueOnly && groupBy is not ("seller" or "shop"))
        {
            var revenueRows = await ordersQ
                .Where(o =>
                    (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                    (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                .Select(o => new
                {
                    At = o.OrderCreateTime ?? o.CreatedAt,
                    o.PlatformOrderStatus,
                    o.OrderStatus,
                    IncludeMoney = o.Shop != null &&
                        o.Shop.Status == ShopStatuses.Active &&
                        o.PlatformOrderStatus != "CANCELLED" &&
                        o.PlatformOrderStatus != "CANCELED" &&
                        o.PlatformOrderStatus != "REFUND" &&
                        o.PlatformOrderStatus != "REFUNDED" &&
                        !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                          (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                    Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount)
                })
                .ToListAsync(cancellationToken);

            orderRows = revenueRows
                .Select(r =>
                {
                    var (isOk, isCanceled, isRefund) =
                        ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                    return (
                        r.At,
                        isOk,
                        isCanceled,
                        isRefund,
                        r.IncludeMoney ? r.Revenue : 0m,
                        (decimal?)null,
                        Guid.Empty,
                        (Guid?)null,
                        (string?)null,
                        (Guid?)null);
                })
                .ToList();
        }
        else if (groupBy is "seller" or "shop")
        {
            if (ordersOnly)
            {
                var entityRows = await ordersQ
                    .Where(o =>
                        (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                        (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                    .Select(o => new
                    {
                        At = o.OrderCreateTime ?? o.CreatedAt,
                        o.PlatformOrderStatus,
                        o.OrderStatus,
                        o.OwnerId,
                        o.ShopId,
                        ShopName = o.Shop != null
                            ? (o.Shop.ProfileName != null && o.Shop.ProfileName != string.Empty
                                ? o.Shop.ProfileName
                                : o.Shop.Name)
                            : null,
                        ShopOwnerId = o.Shop != null ? (Guid?)o.Shop.OwnerId : null
                    })
                    .ToListAsync(cancellationToken);

                orderRows = entityRows
                    .Select(r =>
                    {
                        var (isOk, isCanceled, isRefund) =
                            ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                        return (
                            r.At,
                            isOk,
                            isCanceled,
                            isRefund,
                            0m,
                            (decimal?)null,
                            r.OwnerId,
                            r.ShopId,
                            r.ShopName,
                            r.ShopOwnerId);
                    })
                    .ToList();
            }
            else if (revenueOnly)
            {
                var entityRows = await ordersQ
                    .Where(o =>
                        (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                        (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                    .Select(o => new
                    {
                        At = o.OrderCreateTime ?? o.CreatedAt,
                        o.PlatformOrderStatus,
                        o.OrderStatus,
                        IncludeMoney = o.Shop != null &&
                            o.Shop.Status == ShopStatuses.Active &&
                            o.PlatformOrderStatus != "CANCELLED" &&
                            o.PlatformOrderStatus != "CANCELED" &&
                            o.PlatformOrderStatus != "REFUND" &&
                            o.PlatformOrderStatus != "REFUNDED" &&
                            !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                              (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                        Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                        o.OwnerId,
                        o.ShopId,
                        ShopName = o.Shop != null
                            ? (o.Shop.ProfileName != null && o.Shop.ProfileName != string.Empty
                                ? o.Shop.ProfileName
                                : o.Shop.Name)
                            : null,
                        ShopOwnerId = o.Shop != null ? (Guid?)o.Shop.OwnerId : null
                    })
                    .ToListAsync(cancellationToken);

                orderRows = entityRows
                    .Select(r =>
                    {
                        var (isOk, isCanceled, isRefund) =
                            ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                        return (
                            r.At,
                            isOk,
                            isCanceled,
                            isRefund,
                            r.IncludeMoney ? r.Revenue : 0m,
                            (decimal?)null,
                            r.OwnerId,
                            r.ShopId,
                            r.ShopName,
                            r.ShopOwnerId);
                    })
                    .ToList();
            }
            else
            {
                var entityRows = await ordersQ
                    .Where(o =>
                        (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                        (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                    .Select(o => new
                    {
                        At = o.OrderCreateTime ?? o.CreatedAt,
                        o.PlatformOrderStatus,
                        o.OrderStatus,
                        IncludeMoney = o.Shop != null &&
                            o.Shop.Status == ShopStatuses.Active &&
                            o.PlatformOrderStatus != "CANCELLED" &&
                            o.PlatformOrderStatus != "CANCELED" &&
                            o.PlatformOrderStatus != "REFUND" &&
                            o.PlatformOrderStatus != "REFUNDED" &&
                            !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                              (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                        Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                        SupplierTotal = (o.ChargedTrueSpend
                        ?? (decimal?)o.SupplierPurchases.Where(p => p.IsCharged)
                            .Sum(p => (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m)
                                + (p.SupplierMembershipDiscount ?? 0m)))
                            + o.BuyTrackAmount + o.ThankCardAmount,
                        o.OwnerId,
                        o.ShopId,
                        ShopName = o.Shop != null
                            ? (o.Shop.ProfileName != null && o.Shop.ProfileName != string.Empty
                                ? o.Shop.ProfileName
                                : o.Shop.Name)
                            : null,
                        ShopOwnerId = o.Shop != null ? (Guid?)o.Shop.OwnerId : null
                    })
                    .ToListAsync(cancellationToken);

                orderRows = entityRows
                    .Select(r =>
                    {
                        var (isOk, isCanceled, isRefund) =
                            ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                        return (
                            r.At,
                            isOk,
                            isCanceled,
                            isRefund,
                            r.IncludeMoney ? r.Revenue : 0m,
                            r.IncludeMoney ? r.SupplierTotal : null,
                            r.OwnerId,
                            r.ShopId,
                            r.ShopName,
                            r.ShopOwnerId);
                    })
                    .ToList();
            }
        }
        else
        {
            var aggRows = await ordersQ
                .Where(o =>
                    (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                    (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                .Select(o => new
                {
                    At = o.OrderCreateTime ?? o.CreatedAt,
                    o.PlatformOrderStatus,
                    o.OrderStatus,
                    IncludeMoney = o.Shop != null &&
                        o.Shop.Status == ShopStatuses.Active &&
                        o.PlatformOrderStatus != "CANCELLED" &&
                        o.PlatformOrderStatus != "CANCELED" &&
                        o.PlatformOrderStatus != "REFUND" &&
                        o.PlatformOrderStatus != "REFUNDED" &&
                        !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                          (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                    Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                    SupplierTotal = (o.ChargedTrueSpend
                        ?? (decimal?)o.SupplierPurchases.Where(p => p.IsCharged)
                            .Sum(p => (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m)
                                + (p.SupplierMembershipDiscount ?? 0m)))
                            + o.BuyTrackAmount + o.ThankCardAmount
                })
                .ToListAsync(cancellationToken);

            orderRows = aggRows
                .Select(r =>
                {
                    var (isOk, isCanceled, isRefund) =
                        ClassifyTrendOrderStatus(r.PlatformOrderStatus, r.OrderStatus);
                    return (
                        r.At,
                        isOk,
                        isCanceled,
                        isRefund,
                        r.IncludeMoney ? r.Revenue : 0m,
                        r.IncludeMoney ? r.SupplierTotal : null,
                        Guid.Empty,
                        (Guid?)null,
                        (string?)null,
                        (Guid?)null);
                })
                .ToList();
        }

        var buckets = BuildEmptyTrendBuckets(startLocalDate, endLocalDate, grain);

        foreach (var row in orderRows)
        {
            var dayKey = ToLocalDateKey(row.At, tz);
            if (!DateTime.TryParseExact(
                    dayKey,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var localDay))
            {
                continue;
            }

            var bucketKey = ToGrainBucketKey(localDay, grain);
            if (!buckets.TryGetValue(bucketKey, out var bucket))
            {
                continue;
            }

            bucket.Orders++;
            if (row.IsOk)
            {
                bucket.OrdersOk++;
            }
            else if (row.IsCanceled)
            {
                bucket.OrdersCanceled++;
            }
            else if (row.IsRefund)
            {
                bucket.OrdersRefund++;
            }

            if (ordersOnly)
            {
                continue;
            }

            bucket.Revenue += row.Revenue;
            if (revenueOnly)
            {
                continue;
            }

            var cost = ComputeOrderTotalCost(row.SupplierTotal);
            bucket.Cost += cost;
            bucket.Profit = bucket.Revenue - bucket.Cost;
        }

        if (includeListed)
        {
            var listingsQ = ApplyListingFilters(_db.Listings.AsNoTracking(), query);
            var listedRows = await listingsQ
                .Where(l =>
                    l.PageType == ListingPageTypes.Published &&
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) >= rangeStartUtc &&
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) < rangeEndUtc)
                .Select(l => l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt)
                .ToListAsync(cancellationToken);

            foreach (var at in listedRows)
            {
                var dayKey = ToLocalDateKey(at, tz);
                if (!DateTime.TryParseExact(
                        dayKey,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var localDay))
                {
                    continue;
                }

                var bucketKey = ToGrainBucketKey(localDay, grain);
                if (!buckets.TryGetValue(bucketKey, out var bucket))
                {
                    continue;
                }

                bucket.Listed++;
            }
        }

        List<DashboardTrendSeriesPointDto> series = [];
        if (groupBy is "seller" or "shop")
        {
            var sellers = await GetActiveSellersAsync(cancellationToken);
            var sellerNames = sellers.ToDictionary(s => s.Id, s => s.DisplayName);

            series = BuildTrendEntitySeries(
                orderRows.Select(r =>
                {
                    string entityId;
                    string entityName;
                    if (groupBy == "seller")
                    {
                        entityId = r.OwnerId.ToString();
                        entityName = sellerNames.TryGetValue(r.OwnerId, out var name) && !string.IsNullOrWhiteSpace(name)
                            ? name
                            : entityId;
                    }
                    else
                    {
                        if (r.ShopId is null)
                        {
                            entityId = unassignedId;
                            entityName = "Unassigned";
                        }
                        else
                        {
                            entityId = r.ShopId.Value.ToString();
                            var baseName = string.IsNullOrWhiteSpace(r.ShopName) ? entityId : r.ShopName!;
                            if (r.ShopOwnerId is Guid oid &&
                                sellerNames.TryGetValue(oid, out var sellerName) &&
                                !string.IsNullOrWhiteSpace(sellerName))
                            {
                                entityName = $"{baseName} · {sellerName}";
                            }
                            else
                            {
                                entityName = baseName;
                            }
                        }
                    }

                    return (
                        At: r.At,
                        EntityId: entityId,
                        EntityName: entityName,
                        Revenue: r.Revenue,
                        Cost: ComputeOrderTotalCost(r.SupplierTotal));
                }),
                tz,
                grain,
                buckets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                maxSeriesEntities,
                othersId);
        }

        return new DashboardTrendsDto
        {
            Timezone = TimezoneId,
            Grain = grain,
            FromDate = startLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate = endLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            GroupBy = groupBy,
            Days = buckets.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList(),
            Series = series
        };
    }

    /// <inheritdoc />
    public async Task<DashboardOrderHeatmapDto> GetOrderHeatmapAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default)
    {
        const int maxEntities = 12;
        const string othersId = "__others__";
        const string othersGroupId = "__others_group__";

        var tz = ResolveVietnamTz();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var period = NormalizePeriod(query.Period);
        var dimension = NormalizeHeatmapDimension(query.Dimension);
        var includeSellers = dimension is null or "seller";
        var includeShops = dimension is null or "shop";
        // Prefer custom fromDate/toDate (same as trends); else legacy period/days.
        var (startLocalDate, endLocalDate) = ResolveTrendsRange(localNow.Date, query);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocalDate, DateTimeKind.Unspecified),
            tz);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(endLocalDate.AddDays(1), DateTimeKind.Unspecified),
            tz);
        var fromDate = startLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = endLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var dateKeys = new List<string>();
        for (var d = startLocalDate; d <= endLocalDate; d = d.AddDays(1))
        {
            dateKeys.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var dateKeySet = dateKeys.ToHashSet(StringComparer.Ordinal);

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query);

        List<(DateTime At, Guid OwnerId, Guid? ShopId, Guid? ShopOwnerId, string? ShopName)> orderRows;
        if (includeShops)
        {
            var shopRows = await ordersQ
                .Where(o =>
                    (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                    (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                .Select(o => new
                {
                    At = o.OrderCreateTime ?? o.CreatedAt,
                    o.OwnerId,
                    o.ShopId,
                    ShopOwnerId = o.Shop != null ? (Guid?)o.Shop.OwnerId : null,
                    ShopName = o.Shop != null
                        ? (o.Shop.ProfileName != null && o.Shop.ProfileName != string.Empty
                            ? o.Shop.ProfileName
                            : o.Shop.Name)
                        : null
                })
                .ToListAsync(cancellationToken);

            orderRows = shopRows
                .Select(r => (r.At, r.OwnerId, r.ShopId, r.ShopOwnerId, r.ShopName))
                .ToList();
        }
        else
        {
            // Seller heatmap only — no shop join.
            var sellerOnlyRows = await ordersQ
                .Where(o =>
                    (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                    (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
                .Select(o => new
                {
                    At = o.OrderCreateTime ?? o.CreatedAt,
                    o.OwnerId
                })
                .ToListAsync(cancellationToken);

            orderRows = sellerOnlyRows
                .Select(r => (r.At, r.OwnerId, (Guid?)null, (Guid?)null, (string?)null))
                .ToList();
        }

        var sellers = await GetActiveSellersAsync(cancellationToken);
        if (query.OwnerUserId.HasValue)
        {
            sellers = sellers.Where(s => s.Id == query.OwnerUserId.Value).ToList();
        }

        var sellerNameById = sellers.ToDictionary(s => s.Id, s => s.DisplayName);

        List<DashboardHeatmapCellDto> sellerCells = [];
        if (includeSellers)
        {
            var sellerDayCounts = new Dictionary<(string EntityId, string Date), int>();
            var sellerTotals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in orderRows)
            {
                var dateKey = ToLocalDateKey(row.At, tz);
                if (!dateKeySet.Contains(dateKey))
                {
                    continue;
                }

                var entityId = row.OwnerId.ToString("D");
                var key = (entityId, dateKey);
                sellerDayCounts[key] = sellerDayCounts.GetValueOrDefault(key) + 1;
                sellerTotals[entityId] = sellerTotals.GetValueOrDefault(entityId) + 1;
            }

            sellerCells = BuildHeatmapCells(
                dateKeys,
                sellerDayCounts,
                sellerTotals,
                id => sellerNameById.TryGetValue(Guid.Parse(id), out var name) ? name : id,
                maxEntities,
                othersId);
        }

        List<DashboardHeatmapCellDto> shopCells = [];
        if (includeShops)
        {
            // Shop heatmap = Active shops only (include 0-order rows; skip Unassigned / non-Active).
            var sellerIdSet = sellers.Select(s => s.Id).ToHashSet();
            var activeShops = await ApplyShopFilters(_db.Shops.AsNoTracking(), query)
                .Where(s =>
                    sellerIdSet.Contains(s.OwnerId) &&
                    s.Status == ShopStatuses.Active)
                .Select(s => new
                {
                    s.Id,
                    s.OwnerId,
                    s.Name,
                    s.ProfileName,
                    s.PublishLimitPublished,
                    s.PublishLimitTotal
                })
                .ToListAsync(cancellationToken);

            var shopDayCounts = new Dictionary<(string EntityId, string Date), int>();
            var shopTotals = new Dictionary<string, int>(StringComparer.Ordinal);
            var shopMeta = new Dictionary<string, (string Name, string GroupId, int? Published, int? Total)>(StringComparer.Ordinal);
            var activeShopIds = new HashSet<Guid>();

            foreach (var shop in activeShops)
            {
                activeShopIds.Add(shop.Id);
                var entityId = shop.Id.ToString("D");
                var entityName = !string.IsNullOrWhiteSpace(shop.ProfileName)
                    ? shop.ProfileName.Trim()
                    : (string.IsNullOrWhiteSpace(shop.Name) ? entityId : shop.Name.Trim());
                shopMeta[entityId] = (
                    entityName,
                    shop.OwnerId.ToString("D"),
                    shop.PublishLimitPublished,
                    shop.PublishLimitTotal);
                shopTotals[entityId] = 0;
            }

            foreach (var row in orderRows)
            {
                if (row.ShopId is not Guid shopId || !activeShopIds.Contains(shopId))
                {
                    continue;
                }

                var dateKey = ToLocalDateKey(row.At, tz);
                if (!dateKeySet.Contains(dateKey))
                {
                    continue;
                }

                var entityId = shopId.ToString("D");
                var key = (entityId, dateKey);
                shopDayCounts[key] = shopDayCounts.GetValueOrDefault(key) + 1;
                shopTotals[entityId] = shopTotals.GetValueOrDefault(entityId) + 1;
            }

            var shopEntityLimit = Math.Max(shopMeta.Count, 1);
            shopCells = BuildShopHeatmapCellsGrouped(
                dateKeys,
                shopDayCounts,
                shopTotals,
                shopMeta,
                id =>
                {
                    if (Guid.TryParse(id, out var guid) && sellerNameById.TryGetValue(guid, out var name))
                    {
                        return name;
                    }

                    return id;
                },
                shopEntityLimit,
                othersId,
                othersGroupId);
        }

        return new DashboardOrderHeatmapDto
        {
            Timezone = TimezoneId,
            Period = period,
            FromDate = fromDate,
            ToDate = toDate,
            Sellers = sellerCells,
            Shops = shopCells
        };
    }

    /// <inheritdoc />
    public async Task<DashboardOrderHourlyHeatmapDto> GetOrderHourlyHeatmapAsync(
        DashboardTrendsQuery query,
        CancellationToken cancellationToken = default)
    {
        const int maxEntities = 12;
        const string othersId = "__others__";
        const string allId = "__all__";

        // Period window stays VN business calendar; hour buckets: US Pacific (default) or VN.
        var vnTz = ResolveVietnamTz();
        var (hourTz, hourTzId) = ResolveHourlyHeatmapTimezone(query.Timezone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTz);
        var period = NormalizePeriod(query.Period);
        var dimension = NormalizeHourlyHeatmapDimension(query.Dimension);
        var includeTotals = dimension is null or "total";
        var includeSellers = dimension is null or "seller";
        var (startLocalDate, endLocalDate) = ResolveTrendsRange(localNow.Date, query);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocalDate, DateTimeKind.Unspecified),
            vnTz);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(endLocalDate.AddDays(1), DateTimeKind.Unspecified),
            vnTz);
        var fromDate = startLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = endLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query);
        var orderRows = await ordersQ
            .Where(o =>
                (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc)
            .Select(o => new
            {
                At = o.OrderCreateTime ?? o.CreatedAt,
                o.OwnerId
            })
            .ToListAsync(cancellationToken);

        var hourTotals = new int[24];
        var sellerHourCounts = new Dictionary<(string EntityId, int Hour), int>();
        var sellerTotals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in orderRows)
        {
            var hour = ToLocalHour(row.At, hourTz);
            if (hour is < 0 or > 23)
            {
                continue;
            }

            hourTotals[hour]++;

            if (!includeSellers)
            {
                continue;
            }

            var entityId = row.OwnerId.ToString("D");
            var key = (entityId, hour);
            sellerHourCounts[key] = sellerHourCounts.GetValueOrDefault(key) + 1;
            sellerTotals[entityId] = sellerTotals.GetValueOrDefault(entityId) + 1;
        }

        List<DashboardHourlyHeatmapCellDto> totals = [];
        if (includeTotals)
        {
            totals = Enumerable.Range(0, 24)
                .Select(h => new DashboardHourlyHeatmapCellDto
                {
                    EntityId = allId,
                    EntityName = "Tổng",
                    Hour = h,
                    Orders = hourTotals[h]
                })
                .ToList();
        }

        List<DashboardHourlyHeatmapCellDto> sellerCells = [];
        if (includeSellers)
        {
            var sellers = await GetActiveSellersAsync(cancellationToken);
            if (query.OwnerUserId.HasValue)
            {
                sellers = sellers.Where(s => s.Id == query.OwnerUserId.Value).ToList();
            }

            var sellerNameById = sellers.ToDictionary(s => s.Id, s => s.DisplayName);
            sellerCells = BuildHourlyHeatmapCells(
                sellerHourCounts,
                sellerTotals,
                id => sellerNameById.TryGetValue(Guid.Parse(id), out var name) ? name : id,
                maxEntities,
                othersId);
        }

        return new DashboardOrderHourlyHeatmapDto
        {
            Timezone = hourTzId,
            Period = period,
            FromDate = fromDate,
            ToDate = toDate,
            Totals = totals,
            Sellers = sellerCells
        };
    }

    /// <inheritdoc />
    public async Task<SellerPerformanceDto> GetSellerPerformanceAsync(
        SellerPerformanceQuery query,
        CancellationToken cancellationToken = default)
    {
        var tz = ResolveVietnamTz();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var period = NormalizePeriod(query.Period);

        var (fromLocal, toLocalInclusive) = ResolveSummaryRange(localNow.Date, query);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified),
            tz);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(toLocalInclusive.AddDays(1), DateTimeKind.Unspecified),
            tz);
        var fromDate = fromLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toLocalInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayCount = (toLocalInclusive.Date - fromLocal.Date).Days + 1;

        var sellers = await GetActiveSellersAsync(cancellationToken);
        if (query.OwnerUserId.HasValue)
        {
            sellers = sellers.Where(s => s.Id == query.OwnerUserId.Value).ToList();
        }

        if (sellers.Count == 0)
        {
            return new SellerPerformanceDto
            {
                Timezone = TimezoneId,
                Period = period,
                FromDate = fromDate,
                ToDate = toDate,
                Totals = new SellerPerformanceTotalsDto { DayCount = dayCount },
                Sellers = []
            };
        }

        var sellerIds = sellers.Select(s => s.Id).ToList();
        var fromDateOnly = DateOnly.FromDateTime(fromLocal);
        var toDateOnly = DateOnly.FromDateTime(toLocalInclusive);

        // Informational snapshot only — not used as a rate denominator.
        // One GroupBy (owner × status) → Active / Deactivated / Suspended / total per seller.
        var shopStatusRows = await ApplyShopFilters(_db.Shops.AsNoTracking(), query)
            .Where(s => sellerIds.Contains(s.OwnerId))
            .GroupBy(s => new { s.OwnerId, s.Status })
            .Select(g => new { g.Key.OwnerId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var shopCountsByOwner = shopStatusRows
            .GroupBy(x => x.OwnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int CountOf(string status) =>
                        g.Where(x =>
                                string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
                            .Sum(x => x.Count);
                    var active = CountOf(ShopStatuses.Active);
                    var deactivated = CountOf(ShopStatuses.Deactivated);
                    var suspended = CountOf(ShopStatuses.Suspended);
                    return (
                        Active: active,
                        Deactivated: deactivated,
                        Suspended: suspended,
                        Total: g.Sum(x => x.Count));
                });

        // Operational periods overlapping the report window (owner at time of operation).
        var periodRows = await _db.ShopOperationalPeriods.AsNoTracking()
            .Where(p =>
                sellerIds.Contains(p.OwnerId) &&
                p.StartedOn <= toDateOnly &&
                (p.EndedOn == null || p.EndedOn >= fromDateOnly))
            .Select(p => new
            {
                p.OwnerId,
                p.ShopId,
                p.StartedOn,
                p.EndedOn
            })
            .ToListAsync(cancellationToken);

        if (query.ShopId.HasValue)
        {
            periodRows = periodRows.Where(p => p.ShopId == query.ShopId.Value).ToList();
        }

        // Platform filter applies to shops via current Platform navigation when requested.
        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToUpperInvariant();
            var shopIdsForPlatform = await ApplyShopFilters(_db.Shops.AsNoTracking(), query)
                .Where(s => sellerIds.Contains(s.OwnerId))
                .Where(s => s.Platform != null && s.Platform.Code.ToUpper() == platform)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            var allowed = shopIdsForPlatform.ToHashSet();
            periodRows = periodRows.Where(p => allowed.Contains(p.ShopId)).ToList();
        }

        var exposureByOwner = periodRows
            .GroupBy(p => p.OwnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var periods = g
                        .Select(x => (x.ShopId, x.StartedOn, x.EndedOn))
                        .ToList();
                    var shopDays = periods
                        .GroupBy(p => p.ShopId)
                        .Sum(shopGroup =>
                            SellerPerformanceExposureEngine.CountShopDays(
                                fromDateOnly,
                                toDateOnly,
                                shopGroup.Select(x => (x.StartedOn, x.EndedOn))));
                    var sellerDays = SellerPerformanceExposureEngine.CountSellerOperationalDays(
                        fromDateOnly,
                        toDateOnly,
                        periods.Select(x => (x.StartedOn, x.EndedOn)));
                    var operationalShops = SellerPerformanceExposureEngine.CountOperationalShops(
                        fromDateOnly,
                        toDateOnly,
                        periods);
                    return (
                        ShopDays: shopDays,
                        SellerOperationalDays: sellerDays,
                        OperationalShopCount: operationalShops);
                });

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query)
            .Where(o =>
                sellerIds.Contains(o.OwnerId) &&
                (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc);

        var dayOrders = await ordersQ
            .Select(o => new
            {
                o.OwnerId,
                o.ShopId,
                OrderAt = o.OrderCreateTime ?? o.CreatedAt,
                IncludeMoney = o.Shop != null &&
                    o.Shop.Status == ShopStatuses.Active &&
                    o.PlatformOrderStatus != "CANCELLED" &&
                    o.PlatformOrderStatus != "CANCELED" &&
                    o.PlatformOrderStatus != "REFUND" &&
                    o.PlatformOrderStatus != "REFUNDED" &&
                    !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                      (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                SupplierTotal = (o.ChargedTrueSpend
                        ?? (decimal?)o.SupplierPurchases.Where(p => p.IsCharged)
                            .Sum(p => (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m)
                                + (p.SupplierMembershipDiscount ?? 0m)))
                            + o.BuyTrackAmount + o.ThankCardAmount
            })
            .ToListAsync(cancellationToken);

        var orderStats = dayOrders
            .GroupBy(o => o.OwnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var revenue = g.Where(x => x.IncludeMoney).Sum(x => x.Revenue);
                    var cost = g.Where(x => x.IncludeMoney).Sum(x => ComputeOrderTotalCost(x.SupplierTotal));
                    var profit = revenue - cost;
                    var orders = g.Count();
                    var eligibleShopOrders = g.Count(x => x.ShopId != null);
                    var unassigned = orders - eligibleShopOrders;
                    decimal? margin = revenue > 0
                        ? Math.Round(profit / revenue * 100m, 2, MidpointRounding.AwayFromZero)
                        : null;
                    decimal? avgRevenue = orders > 0
                        ? Math.Round(revenue / orders, 2, MidpointRounding.AwayFromZero)
                        : null;
                    decimal? avgProfit = orders > 0
                        ? Math.Round(profit / orders, 2, MidpointRounding.AwayFromZero)
                        : null;
                    decimal? unassignedRate = orders > 0
                        ? Math.Round((decimal)unassigned / orders * 100m, 2, MidpointRounding.AwayFromZero)
                        : null;
                    return (
                        OrderCount: orders,
                        EligibleShopOrderCount: eligibleShopOrders,
                        UnassignedOrderCount: unassigned,
                        UnassignedOrderRate: unassignedRate,
                        Revenue: revenue,
                        Cost: cost,
                        Profit: profit,
                        ProfitMargin: margin,
                        AverageRevenuePerOrder: avgRevenue,
                        AverageProfitPerOrder: avgProfit);
                });

        var rows = sellers
            .Select(seller =>
            {
                orderStats.TryGetValue(seller.Id, out var stats);
                shopCountsByOwner.TryGetValue(seller.Id, out var shopCounts);
                exposureByOwner.TryGetValue(seller.Id, out var exposure);
                return new SellerPerformanceRowDto
                {
                    UserId = seller.Id,
                    DisplayName = seller.DisplayName,
                    Orders = stats.OrderCount,
                    ActiveShopCount = shopCounts.Active,
                    DeactivatedShopCount = shopCounts.Deactivated,
                    SuspendedShopCount = shopCounts.Suspended,
                    ShopCount = shopCounts.Total,
                    SellerOperationalDays = exposure.SellerOperationalDays,
                    OperationalShopCount = exposure.OperationalShopCount,
                    ShopDays = exposure.ShopDays,
                    EligibleShopOrderCount = stats.EligibleShopOrderCount,
                    UnassignedOrderCount = stats.UnassignedOrderCount,
                    UnassignedOrderRate = stats.UnassignedOrderRate,
                    Revenue = stats.Revenue,
                    Cost = stats.Cost,
                    Profit = stats.Profit,
                    ProfitMargin = stats.ProfitMargin,
                    AverageRevenuePerOrder = stats.AverageRevenuePerOrder,
                    AverageProfitPerOrder = stats.AverageProfitPerOrder,
                    AverageOrdersPerCalendarDay = Math.Round(
                        (decimal)stats.OrderCount / dayCount,
                        2,
                        MidpointRounding.AwayFromZero),
                    AverageOrdersPerOperationalDay = SellerPerformanceExposureEngine.AveragePerDay(
                        stats.OrderCount,
                        exposure.SellerOperationalDays),
                    AverageOrdersPerOperationalShop = SellerPerformanceExposureEngine.AveragePerShop(
                        stats.EligibleShopOrderCount,
                        exposure.OperationalShopCount),
                    AverageOrdersPerShopDay = SellerPerformanceExposureEngine.AveragePerDay(
                        stats.EligibleShopOrderCount,
                        exposure.ShopDays)
                };
            })
            .OrderByDescending(s => s.Profit)
            .ThenByDescending(s => s.Revenue)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalOrders = rows.Sum(r => r.Orders);
        var totalRevenue = rows.Sum(r => r.Revenue);
        var totalCost = rows.Sum(r => r.Cost);
        var totalProfit = totalRevenue - totalCost;
        var totalActiveShops = rows.Sum(r => r.ActiveShopCount);
        var totalDeactivatedShops = rows.Sum(r => r.DeactivatedShopCount);
        var totalSuspendedShops = rows.Sum(r => r.SuspendedShopCount);
        var totalShops = rows.Sum(r => r.ShopCount);
        var totalOperationalShops = rows.Sum(r => r.OperationalShopCount);
        var totalShopDays = rows.Sum(r => r.ShopDays);
        var totalSellerOperationalDays = rows.Sum(r => r.SellerOperationalDays);
        var totalEligibleShopOrders = rows.Sum(r => r.EligibleShopOrderCount);
        var totalUnassigned = rows.Sum(r => r.UnassignedOrderCount);

        var growthDays = new List<SellerPerformanceDayDto>();
        var ordersByDate = dayOrders
            .GroupBy(o => ToLocalDateKey(o.OrderAt, tz))
            .ToDictionary(
                g => g.Key,
                g => (
                    Orders: g.Count(),
                    Eligible: g.Count(o => o.ShopId != null)),
                StringComparer.Ordinal);

        for (var day = fromDateOnly; day <= toDateOnly; day = day.AddDays(1))
        {
            var dateKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ordersByDate.TryGetValue(dateKey, out var daily);
            var operationalShops = periodRows
                .Where(p =>
                    (!query.OwnerUserId.HasValue || p.OwnerId == query.OwnerUserId.Value) &&
                    p.StartedOn <= day &&
                    (p.EndedOn == null || p.EndedOn >= day))
                .Select(p => p.ShopId)
                .Distinct()
                .Count();

            growthDays.Add(new SellerPerformanceDayDto
            {
                Date = dateKey,
                Orders = daily.Orders,
                EligibleShopOrders = daily.Eligible,
                OperationalShopCount = operationalShops,
                OrdersPerDay = daily.Orders,
                OrdersPerShop = SellerPerformanceExposureEngine.AveragePerShop(
                    daily.Eligible,
                    operationalShops)
            });
        }

        return new SellerPerformanceDto
        {
            Timezone = TimezoneId,
            Period = period,
            FromDate = fromDate,
            ToDate = toDate,
            Totals = new SellerPerformanceTotalsDto
            {
                DayCount = dayCount,
                SellerOperationalDays = totalSellerOperationalDays,
                ActiveSellerCount = sellers.Count,
                SellersWithOrders = rows.Count(r => r.Orders > 0),
                ActiveShopCount = totalActiveShops,
                DeactivatedShopCount = totalDeactivatedShops,
                SuspendedShopCount = totalSuspendedShops,
                ShopCount = totalShops,
                OperationalShopCount = totalOperationalShops,
                ShopDays = totalShopDays,
                EligibleShopOrderCount = totalEligibleShopOrders,
                UnassignedOrderCount = totalUnassigned,
                UnassignedOrderRate = totalOrders > 0
                    ? Math.Round((decimal)totalUnassigned / totalOrders * 100m, 2, MidpointRounding.AwayFromZero)
                    : null,
                OrderCount = totalOrders,
                Revenue = totalRevenue,
                Cost = totalCost,
                Profit = totalProfit,
                ProfitMargin = totalRevenue > 0
                    ? Math.Round(totalProfit / totalRevenue * 100m, 2, MidpointRounding.AwayFromZero)
                    : null,
                AverageRevenuePerOrder = totalOrders > 0
                    ? Math.Round(totalRevenue / totalOrders, 2, MidpointRounding.AwayFromZero)
                    : null,
                AverageProfitPerOrder = totalOrders > 0
                    ? Math.Round(totalProfit / totalOrders, 2, MidpointRounding.AwayFromZero)
                    : null,
                AverageOrdersPerCalendarDay = Math.Round(
                    (decimal)totalOrders / dayCount,
                    2,
                    MidpointRounding.AwayFromZero),
                AverageOrdersPerOperationalDay = SellerPerformanceExposureEngine.AveragePerDay(
                    totalOrders,
                    totalSellerOperationalDays),
                AverageOrdersPerOperationalShop = SellerPerformanceExposureEngine.AveragePerShop(
                    totalEligibleShopOrders,
                    totalOperationalShops),
                AverageOrdersPerShopDay = SellerPerformanceExposureEngine.AveragePerDay(
                    totalEligibleShopOrders,
                    totalShopDays)
            },
            Days = growthDays,
            Sellers = rows
        };
    }

    private static List<DashboardHeatmapCellDto> BuildShopHeatmapCellsGrouped(
        IReadOnlyList<string> dateKeys,
        Dictionary<(string EntityId, string Date), int> dayCounts,
        Dictionary<string, int> totals,
        Dictionary<string, (string Name, string GroupId, int? Published, int? Total)> shopMeta,
        Func<string, string> resolveGroupName,
        int maxEntities,
        string othersId,
        string othersGroupId)
    {
        if (totals.Count == 0)
        {
            return [];
        }

        var ranked = totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => shopMeta.GetValueOrDefault(kv.Key).Name ?? kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topIds = ranked.Take(maxEntities).Select(kv => kv.Key).ToList();
        var topSet = topIds.ToHashSet(StringComparer.Ordinal);
        var hasOthers = ranked.Count > maxEntities;

        var entityOrder = topIds
            .Select(id =>
            {
                var meta = shopMeta.GetValueOrDefault(id);
                var groupId = string.IsNullOrEmpty(meta.GroupId) ? othersGroupId : meta.GroupId;
                return (
                    Id: id,
                    Name: string.IsNullOrEmpty(meta.Name) ? id : meta.Name,
                    GroupId: groupId,
                    GroupName: resolveGroupName(groupId),
                    OrderTotal: totals.GetValueOrDefault(id),
                    Published: meta.Published,
                    PublishTotal: meta.Total);
            })
            // Seller groups A→Z; shops within each group = same "Theo tên Profile" asc as /shops.
            .OrderBy(x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, ProfileNameComparer.Instance)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        if (hasOthers)
        {
            entityOrder.Add((
                othersId,
                "Others",
                othersGroupId,
                "Others",
                ranked.Skip(maxEntities).Sum(kv => kv.Value),
                null,
                null));
        }

        var collapsed = new Dictionary<(string EntityId, string Date), int>();
        foreach (var (key, count) in dayCounts)
        {
            var entityId = topSet.Contains(key.EntityId) ? key.EntityId : othersId;
            if (!topSet.Contains(key.EntityId) && !hasOthers)
            {
                continue;
            }

            var cellKey = (entityId, key.Date);
            collapsed[cellKey] = collapsed.GetValueOrDefault(cellKey) + count;
        }

        var cells = new List<DashboardHeatmapCellDto>(entityOrder.Count * dateKeys.Count);
        foreach (var entity in entityOrder)
        {
            foreach (var date in dateKeys)
            {
                collapsed.TryGetValue((entity.Id, date), out var orders);
                cells.Add(new DashboardHeatmapCellDto
                {
                    Date = date,
                    EntityId = entity.Id,
                    EntityName = entity.Name,
                    Orders = orders,
                    GroupId = entity.GroupId,
                    GroupName = entity.GroupName,
                    PublishLimitPublished = entity.Published,
                    PublishLimitTotal = entity.PublishTotal
                });
            }
        }

        return cells;
    }

    private static List<DashboardHeatmapCellDto> BuildHeatmapCells(
        IReadOnlyList<string> dateKeys,
        Dictionary<(string EntityId, string Date), int> dayCounts,
        Dictionary<string, int> totals,
        Func<string, string> resolveName,
        int maxEntities,
        string othersId)
    {
        if (totals.Count == 0)
        {
            return [];
        }

        var ranked = totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => resolveName(kv.Key), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topIds = ranked.Take(maxEntities).Select(kv => kv.Key).ToList();
        var topSet = topIds.ToHashSet(StringComparer.Ordinal);
        var hasOthers = ranked.Count > maxEntities;

        var entityOrder = new List<(string Id, string Name)>();
        foreach (var id in topIds)
        {
            entityOrder.Add((id, resolveName(id)));
        }

        if (hasOthers)
        {
            entityOrder.Add((othersId, "Others"));
        }

        // Collapse non-top into Others per day
        var collapsed = new Dictionary<(string EntityId, string Date), int>();
        foreach (var (key, count) in dayCounts)
        {
            var entityId = topSet.Contains(key.EntityId) ? key.EntityId : othersId;
            if (!topSet.Contains(key.EntityId) && !hasOthers)
            {
                continue;
            }

            var cellKey = (entityId, key.Date);
            collapsed[cellKey] = collapsed.GetValueOrDefault(cellKey) + count;
        }

        var cells = new List<DashboardHeatmapCellDto>(entityOrder.Count * dateKeys.Count);
        foreach (var (id, name) in entityOrder)
        {
            foreach (var date in dateKeys)
            {
                collapsed.TryGetValue((id, date), out var orders);
                cells.Add(new DashboardHeatmapCellDto
                {
                    Date = date,
                    EntityId = id,
                    EntityName = name,
                    Orders = orders
                });
            }
        }

        return cells;
    }

    /// <summary>Seller × hour cells (top N + Others), hours always 0–23.</summary>
    private static List<DashboardHourlyHeatmapCellDto> BuildHourlyHeatmapCells(
        Dictionary<(string EntityId, int Hour), int> hourCounts,
        Dictionary<string, int> totals,
        Func<string, string> resolveName,
        int maxEntities,
        string othersId)
    {
        if (totals.Count == 0)
        {
            return [];
        }

        var ranked = totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => resolveName(kv.Key), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topIds = ranked.Take(maxEntities).Select(kv => kv.Key).ToList();
        var topSet = topIds.ToHashSet(StringComparer.Ordinal);
        var hasOthers = ranked.Count > maxEntities;

        var entityOrder = new List<(string Id, string Name)>();
        foreach (var id in topIds)
        {
            entityOrder.Add((id, resolveName(id)));
        }

        if (hasOthers)
        {
            entityOrder.Add((othersId, "Others"));
        }

        var collapsed = new Dictionary<(string EntityId, int Hour), int>();
        foreach (var (key, count) in hourCounts)
        {
            var entityId = topSet.Contains(key.EntityId) ? key.EntityId : othersId;
            if (!topSet.Contains(key.EntityId) && !hasOthers)
            {
                continue;
            }

            var cellKey = (entityId, key.Hour);
            collapsed[cellKey] = collapsed.GetValueOrDefault(cellKey) + count;
        }

        var cells = new List<DashboardHourlyHeatmapCellDto>(entityOrder.Count * 24);
        foreach (var (id, name) in entityOrder)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                collapsed.TryGetValue((id, hour), out var orders);
                cells.Add(new DashboardHourlyHeatmapCellDto
                {
                    EntityId = id,
                    EntityName = name,
                    Hour = hour,
                    Orders = orders
                });
            }
        }

        return cells;
    }

    private static (DateTime StartLocal, DateTime EndLocal) ResolveTrendLocalDates(
        DateTime localToday,
        string period,
        int days)
    {
        if (period is "week" or "month")
        {
            return ResolvePeriodLocalDates(localToday, period);
        }

        var lookback = days < 1 ? 7 : Math.Min(days, 31);
        var end = localToday.Date;
        return (end.AddDays(-(lookback - 1)), end);
    }

    /// <summary>
    /// Custom fromDate/toDate when both parse; otherwise calendar period (day/week/month).
    /// Max span 366 days for summary.
    /// </summary>
    private static (DateTime StartLocal, DateTime EndLocal) ResolveSummaryRange(
        DateTime localToday,
        DashboardQuery query)
    {
        if (TryParseLocalDate(query.FromDate, out var from) &&
            TryParseLocalDate(query.ToDate, out var to))
        {
            var start = from.Date;
            var end = to.Date;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            if (end > localToday)
            {
                end = localToday;
            }

            const int maxDays = 366;
            if ((end - start).Days + 1 > maxDays)
            {
                start = end.AddDays(-(maxDays - 1));
            }

            return (start, end);
        }

        return ResolvePeriodLocalDates(localToday, NormalizePeriod(query.Period));
    }

    /// <summary>
    /// Custom fromDate/toDate when both parse; otherwise legacy period/days.
    /// Clamps span by grain (day≤366, week≤730d, month≤1825d, year≤3650d).
    /// </summary>
    private static (DateTime StartLocal, DateTime EndLocal) ResolveTrendsRange(
        DateTime localToday,
        DashboardTrendsQuery query)
    {
        var grain = NormalizeGrain(query.Grain);
        DateTime start;
        DateTime end;

        if (TryParseLocalDate(query.FromDate, out var from) &&
            TryParseLocalDate(query.ToDate, out var to))
        {
            start = from.Date;
            end = to.Date;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            if (end > localToday)
            {
                end = localToday;
            }
        }
        else
        {
            var period = NormalizePeriod(query.Period);
            (start, end) = ResolveTrendLocalDates(localToday, period, query.Days);
        }

        var maxDays = grain switch
        {
            "week" => 730,
            "month" => 1825,
            "year" => 3650,
            _ => 366
        };

        if ((end - start).Days + 1 > maxDays)
        {
            start = end.AddDays(-(maxDays - 1));
        }

        return (start, end);
    }

    private static bool TryParseLocalDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static string NormalizeGrain(string? grain)
    {
        var value = (grain ?? "day").Trim().ToLowerInvariant();
        return value is "week" or "month" or "year" ? value : "day";
    }

    private static string? NormalizeGroupBy(string? groupBy)
    {
        var value = (groupBy ?? string.Empty).Trim().ToLowerInvariant();
        return value is "seller" or "shop" ? value : null;
    }

    private static string? NormalizeHeatmapDimension(string? dimension)
    {
        var value = (dimension ?? string.Empty).Trim().ToLowerInvariant();
        return value is "seller" or "shop" ? value : null;
    }

    private static string? NormalizeHourlyHeatmapDimension(string? dimension)
    {
        var value = (dimension ?? string.Empty).Trim().ToLowerInvariant();
        return value is "total" or "seller" ? value : null;
    }

    /// <summary>
    /// Hourly heatmap TZ: <c>vn</c> → Asia/Ho_Chi_Minh; otherwise America/Los_Angeles (default).
    /// </summary>
    private static (TimeZoneInfo Tz, string Id) ResolveHourlyHeatmapTimezone(string? timezone)
    {
        var value = (timezone ?? string.Empty).Trim().ToLowerInvariant();
        if (value is "vn" or "vietnam" or "asia/ho_chi_minh")
        {
            return (AppTimeZones.Vietnam, AppTimeZones.VietnamId);
        }

        return (AppTimeZones.Pacific, AppTimeZones.PacificId);
    }

    private static List<DashboardTrendSeriesPointDto> BuildTrendEntitySeries(
        IEnumerable<(DateTime At, string EntityId, string EntityName, decimal Revenue, decimal Cost)> rows,
        TimeZoneInfo tz,
        string grain,
        IReadOnlyList<string> bucketKeys,
        int maxEntities,
        string othersId)
    {
        var bucketKeySet = bucketKeys.ToHashSet(StringComparer.Ordinal);
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var cells = new Dictionary<(string EntityId, string Date), (int Orders, decimal Revenue, decimal Cost)>();

        foreach (var row in rows)
        {
            var dayKey = ToLocalDateKey(row.At, tz);
            if (!DateTime.TryParseExact(
                    dayKey,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var localDay))
            {
                continue;
            }

            var bucketKey = ToGrainBucketKey(localDay, grain);
            if (!bucketKeySet.Contains(bucketKey))
            {
                continue;
            }

            names[row.EntityId] = row.EntityName;
            totals[row.EntityId] = totals.GetValueOrDefault(row.EntityId) + row.Revenue;

            var cellKey = (row.EntityId, bucketKey);
            var prev = cells.GetValueOrDefault(cellKey);
            cells[cellKey] = (
                prev.Orders + 1,
                prev.Revenue + row.Revenue,
                prev.Cost + row.Cost);
        }

        if (totals.Count == 0)
        {
            return [];
        }

        var ranked = totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => names.GetValueOrDefault(kv.Key) ?? kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topIds = ranked.Take(maxEntities).Select(kv => kv.Key).ToList();
        var topSet = topIds.ToHashSet(StringComparer.Ordinal);
        var hasOthers = ranked.Count > maxEntities;
        var entityOrder = new List<(string Id, string Name)>();
        foreach (var id in topIds)
        {
            entityOrder.Add((id, names.GetValueOrDefault(id) ?? id));
        }

        if (hasOthers)
        {
            entityOrder.Add((othersId, "Others"));
        }

        var result = new List<DashboardTrendSeriesPointDto>(entityOrder.Count * bucketKeys.Count);
        foreach (var (entityId, entityName) in entityOrder)
        {
            foreach (var date in bucketKeys)
            {
                int orders = 0;
                decimal revenue = 0;
                decimal cost = 0;

                if (entityId == othersId)
                {
                    foreach (var kv in cells)
                    {
                        if (kv.Key.Date != date || topSet.Contains(kv.Key.EntityId))
                        {
                            continue;
                        }

                        orders += kv.Value.Orders;
                        revenue += kv.Value.Revenue;
                        cost += kv.Value.Cost;
                    }
                }
                else if (cells.TryGetValue((entityId, date), out var cell))
                {
                    orders = cell.Orders;
                    revenue = cell.Revenue;
                    cost = cell.Cost;
                }

                result.Add(new DashboardTrendSeriesPointDto
                {
                    Date = date,
                    EntityId = entityId,
                    EntityName = entityName,
                    Orders = orders,
                    Revenue = revenue,
                    Cost = cost,
                    Profit = revenue - cost
                });
            }
        }

        return result;
    }

    private static Dictionary<string, DashboardTrendDayDto> BuildEmptyTrendBuckets(
        DateTime startLocal,
        DateTime endLocal,
        string grain)
    {
        var buckets = new Dictionary<string, DashboardTrendDayDto>(StringComparer.Ordinal);
        var cursor = ToGrainBucketStart(startLocal, grain);
        var endBucket = ToGrainBucketStart(endLocal, grain);

        while (cursor <= endBucket)
        {
            var key = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            buckets[key] = new DashboardTrendDayDto { Date = key };
            cursor = grain switch
            {
                "week" => cursor.AddDays(7),
                "month" => cursor.AddMonths(1),
                "year" => cursor.AddYears(1),
                _ => cursor.AddDays(1)
            };
        }

        return buckets;
    }

    private static string ToGrainBucketKey(DateTime localDay, string grain) =>
        ToGrainBucketStart(localDay, grain).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime ToGrainBucketStart(DateTime localDay, string grain) =>
        grain switch
        {
            "week" => StartOfIsoWeek(localDay),
            "month" => new DateTime(localDay.Year, localDay.Month, 1),
            "year" => new DateTime(localDay.Year, 1, 1),
            _ => localDay.Date
        };

    private async Task<List<DashboardSellerRankDto>> BuildSellerRankingAsync(
        DashboardQuery query,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        IReadOnlyList<SellerLine> allSellers,
        bool includeListed,
        bool ordersOnly,
        CancellationToken cancellationToken)
    {
        var sellers = allSellers;
        if (query.OwnerUserId.HasValue)
        {
            sellers = sellers.Where(s => s.Id == query.OwnerUserId.Value).ToList();
        }

        if (sellers.Count == 0)
        {
            return [];
        }

        var sellerIds = sellers.Select(s => s.Id).ToList();

        Dictionary<Guid, int> listedCounts = [];
        if (includeListed)
        {
            var listingsQ = ApplyListingFilters(_db.Listings.AsNoTracking(), query)
                .Where(l =>
                    sellerIds.Contains(l.OwnerId) &&
                    l.PageType == ListingPageTypes.Published &&
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) >= dayStartUtc &&
                    (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) < dayEndUtc);

            listedCounts = await listingsQ
                .GroupBy(l => l.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count, cancellationToken);
        }

        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query)
            .Where(o =>
                sellerIds.Contains(o.OwnerId) &&
                (o.OrderCreateTime ?? o.CreatedAt) >= dayStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < dayEndUtc);

        if (ordersOnly)
        {
            var orderCounts = await ordersQ
                .GroupBy(o => o.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count, cancellationToken);

            return sellers
                .Select(seller =>
                {
                    listedCounts.TryGetValue(seller.Id, out var listed);
                    orderCounts.TryGetValue(seller.Id, out var orders);
                    return new DashboardSellerRankDto
                    {
                        UserId = seller.Id,
                        DisplayName = seller.DisplayName,
                        Listed = listed,
                        Orders = orders,
                        Revenue = 0,
                        Cost = 0,
                        Profit = 0,
                        ProfitMargin = null
                    };
                })
                .OrderByDescending(s => s.Orders)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(SellerRankLimit)
                .ToList();
        }

        var dayOrders = await ordersQ
            .Select(o => new
            {
                o.OwnerId,
                IncludeMoney = o.Shop != null &&
                    o.Shop.Status == ShopStatuses.Active &&
                    o.PlatformOrderStatus != "CANCELLED" &&
                    o.PlatformOrderStatus != "CANCELED" &&
                    o.PlatformOrderStatus != "REFUND" &&
                    o.PlatformOrderStatus != "REFUNDED" &&
                    !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                      (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                SupplierTotal = (o.ChargedTrueSpend
                        ?? (decimal?)o.SupplierPurchases.Where(p => p.IsCharged)
                            .Sum(p => (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m)
                                + (p.SupplierMembershipDiscount ?? 0m)))
                            + o.BuyTrackAmount + o.ThankCardAmount
            })
            .ToListAsync(cancellationToken);

        var orderStats = dayOrders
            .GroupBy(o => o.OwnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var revenue = g.Where(x => x.IncludeMoney).Sum(x => x.Revenue);
                    var cost = g.Where(x => x.IncludeMoney).Sum(x => ComputeOrderTotalCost(x.SupplierTotal));
                    var profit = revenue - cost;
                    decimal? margin = revenue > 0
                        ? Math.Round(profit / revenue * 100m, 2, MidpointRounding.AwayFromZero)
                        : null;
                    return (
                        OrderCount: g.Count(),
                        Revenue: revenue,
                        Cost: cost,
                        Profit: profit,
                        ProfitMargin: margin);
                });

        return sellers
            .Select(seller =>
            {
                listedCounts.TryGetValue(seller.Id, out var listed);
                orderStats.TryGetValue(seller.Id, out var stats);
                return new DashboardSellerRankDto
                {
                    UserId = seller.Id,
                    DisplayName = seller.DisplayName,
                    Listed = listed,
                    Orders = stats.OrderCount,
                    Revenue = stats.Revenue,
                    Cost = stats.Cost,
                    Profit = stats.Profit,
                    ProfitMargin = stats.ProfitMargin
                };
            })
            .OrderByDescending(s => s.Revenue)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(SellerRankLimit)
            .ToList();
    }

    private async Task<List<DashboardShopFinanceDto>> BuildShopFinanceAsync(
        DashboardQuery query,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        IReadOnlyList<SellerLine> allSellers,
        bool ordersOnly,
        CancellationToken cancellationToken)
    {
        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query)
            .Where(o =>
                (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc);

        if (ordersOnly)
        {
            var lightRows = await ordersQ
                .Select(o => new
                {
                    o.ShopId,
                    o.OwnerId,
                    ShopName = o.Shop != null ? o.Shop.Name : null,
                    ShopProfileName = o.Shop != null ? o.Shop.ProfileName : null
                })
                .ToListAsync(cancellationToken);

            if (lightRows.Count == 0)
            {
                return [];
            }

            var sellerNameById = allSellers.ToDictionary(s => s.Id, s => s.DisplayName);

            return lightRows
                .GroupBy(o => o.ShopId)
                .Select(g =>
                {
                    var sample = g
                        .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.ShopProfileName))
                        .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.ShopName))
                        .First();
                    var profile = sample.ShopProfileName?.Trim();
                    var name = sample.ShopName?.Trim();
                    var display = !string.IsNullOrEmpty(name)
                        ? name
                        : (!string.IsNullOrEmpty(profile) ? profile : "Unassigned");
                    var ownerId = g
                        .GroupBy(x => x.OwnerId)
                        .OrderByDescending(og => og.Count())
                        .Select(og => og.Key)
                        .First();
                    sellerNameById.TryGetValue(ownerId, out var sellerName);

                    return new DashboardShopFinanceDto
                    {
                        ShopId = g.Key,
                        ShopName = display,
                        ShopProfileName = string.IsNullOrEmpty(profile) ? null : profile,
                        SellerName = sellerName,
                        Orders = g.Count(),
                        Revenue = 0,
                        Cost = 0,
                        Profit = 0
                    };
                })
                .OrderByDescending(s => s.Orders)
                .ThenBy(s => s.ShopName, StringComparer.OrdinalIgnoreCase)
                .Take(ShopFinanceLimit)
                .ToList();
        }

        var rows = await ordersQ
            .Select(o => new
            {
                o.ShopId,
                o.OwnerId,
                ShopName = o.Shop != null ? o.Shop.Name : null,
                ShopProfileName = o.Shop != null ? o.Shop.ProfileName : null,
                IncludeMoney = o.Shop != null &&
                    o.Shop.Status == ShopStatuses.Active &&
                    o.PlatformOrderStatus != "CANCELLED" &&
                    o.PlatformOrderStatus != "CANCELED" &&
                    o.PlatformOrderStatus != "REFUND" &&
                    o.PlatformOrderStatus != "REFUNDED" &&
                    !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
                      (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")),
                Revenue = o.SellerNetAmount ?? (o.TotalPaidAmount != 0 ? o.TotalPaidAmount : o.Amount),
                SupplierTotal = (o.ChargedTrueSpend
                        ?? (decimal?)o.SupplierPurchases.Where(p => p.IsCharged)
                            .Sum(p => (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m)
                                + (p.SupplierMembershipDiscount ?? 0m)))
                            + o.BuyTrackAmount + o.ThankCardAmount
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var sellerNames = allSellers.ToDictionary(s => s.Id, s => s.DisplayName);

        return rows
            .GroupBy(o => o.ShopId)
            .Select(g =>
            {
                var sample = g
                    .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.ShopProfileName))
                    .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.ShopName))
                    .First();
                var profile = sample.ShopProfileName?.Trim();
                var name = sample.ShopName?.Trim();
                var display = !string.IsNullOrEmpty(name)
                    ? name
                    : (!string.IsNullOrEmpty(profile) ? profile : "Unassigned");
                var ownerId = g
                    .GroupBy(x => x.OwnerId)
                    .OrderByDescending(og => og.Count())
                    .Select(og => og.Key)
                    .First();
                sellerNames.TryGetValue(ownerId, out var sellerName);

                var revenue = g.Where(x => x.IncludeMoney).Sum(x => x.Revenue);
                var cost = g.Where(x => x.IncludeMoney).Sum(x => ComputeOrderTotalCost(x.SupplierTotal));

                return new DashboardShopFinanceDto
                {
                    ShopId = g.Key,
                    ShopName = display,
                    ShopProfileName = string.IsNullOrEmpty(profile) ? null : profile,
                    SellerName = sellerName,
                    Orders = g.Count(),
                    Revenue = revenue,
                    Cost = cost,
                    Profit = revenue - cost
                };
            })
            .OrderByDescending(s => s.Revenue)
            .ThenBy(s => s.ShopName, StringComparer.OrdinalIgnoreCase)
            .Take(ShopFinanceLimit)
            .ToList();
    }

    private async Task<List<DashboardTopProductDto>> BuildTopProductsAsync(
        DashboardQuery query,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        IReadOnlyList<SellerLine> allSellers,
        CancellationToken cancellationToken)
    {
        var ordersQ = ApplyOrderFilters(_db.Orders.AsNoTracking(), query)
            .Where(o =>
                (o.OrderCreateTime ?? o.CreatedAt) >= rangeStartUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < rangeEndUtc);
        ordersQ = WhereMoneyEligible(ordersQ);

        var lines = await ordersQ
            .SelectMany(o => o.Items.Select(i => new
            {
                o.Id,
                o.OwnerId,
                o.ShopId,
                ShopName = o.Shop != null ? o.Shop.Name : null,
                ShopProfileName = o.Shop != null ? o.Shop.ProfileName : null,
                i.SellerSku,
                i.Title,
                i.ImgUrl,
                i.Quantity,
                i.ItemPrice
            }))
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            return [];
        }

        var sellerNameById = allSellers.ToDictionary(s => s.Id, s => s.DisplayName);

        // Resolve variant SKUs → parent listing so Color/Size variants count as one product.
        var skuKeys = lines
            .Select(l => l.SellerSku?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var listingIdBySku = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (skuKeys.Count > 0)
        {
            var skuLower = skuKeys.Select(s => s.ToLowerInvariant()).ToList();
            var variationLinks = await _db.ListingVariations.AsNoTracking()
                .Where(v =>
                    v.SellerSku != null &&
                    v.SellerSku != string.Empty &&
                    skuLower.Contains(v.SellerSku.ToLower()))
                .Select(v => new { v.SellerSku, v.ListingId })
                .ToListAsync(cancellationToken);

            foreach (var link in variationLinks)
            {
                var sku = link.SellerSku?.Trim();
                if (!string.IsNullOrEmpty(sku) && !listingIdBySku.ContainsKey(sku))
                {
                    listingIdBySku[sku] = link.ListingId;
                }
            }
        }

        static string? FourSellerProductPrefix(string? sellerSku)
        {
            // Format: `472773277-I2mocj4yng23yl` — shared numeric prefix = same product.
            var sku = sellerSku?.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                return null;
            }

            var dash = sku.IndexOf('-');
            if (dash < 6)
            {
                return null;
            }

            var prefix = sku[..dash];
            return prefix.All(char.IsDigit) ? prefix : null;
        }

        string NormalizeKey(string? sellerSku, string? title)
        {
            var sku = sellerSku?.Trim();
            if (!string.IsNullOrEmpty(sku))
            {
                if (listingIdBySku.TryGetValue(sku, out var listingId))
                {
                    return "listing:" + listingId.ToString("D");
                }

                var prefix = FourSellerProductPrefix(sku);
                if (!string.IsNullOrEmpty(prefix))
                {
                    return "product:" + prefix;
                }

                return "sku:" + sku.ToUpperInvariant();
            }

            var name = title?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                return "title:" + name.ToUpperInvariant();
            }

            return "unknown";
        }

        static int LineQty(int quantity) => quantity < 1 ? 1 : quantity;

        return lines
            .GroupBy(l => NormalizeKey(l.SellerSku, l.Title))
            .Select(g =>
            {
                var title = g
                    .Select(x => x.Title?.Trim())
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t))
                    ?? g.Select(x => x.SellerSku?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                    ?? "Unknown";
                var skus = g
                    .Select(x => x.SellerSku?.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // Prefer shared product prefix when variants were merged; else first SKU.
                var displaySku = skus
                    .Select(FourSellerProductPrefix)
                    .FirstOrDefault(p => !string.IsNullOrEmpty(p))
                    ?? skus.FirstOrDefault();
                var img = g
                    .Select(x => x.ImgUrl?.Trim())
                    .FirstOrDefault(u => !string.IsNullOrEmpty(u));

                // Attribute to the seller/shop with the most units for this product.
                var primary = g
                    .GroupBy(x => new { x.OwnerId, x.ShopId })
                    .Select(sg => new
                    {
                        sg.Key.OwnerId,
                        sg.Key.ShopId,
                        Qty = sg.Sum(x => LineQty(x.Quantity)),
                        ShopName = sg
                            .Select(x => x.ShopName?.Trim())
                            .FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                        ShopProfileName = sg
                            .Select(x => x.ShopProfileName?.Trim())
                            .FirstOrDefault(n => !string.IsNullOrEmpty(n))
                    })
                    .OrderByDescending(x => x.Qty)
                    .ThenBy(x => x.ShopName, StringComparer.OrdinalIgnoreCase)
                    .First();

                sellerNameById.TryGetValue(primary.OwnerId, out var sellerName);
                if (string.IsNullOrEmpty(sellerName))
                {
                    sellerName = primary.OwnerId.ToString("D");
                }

                return new DashboardTopProductDto
                {
                    ProductKey = g.Key,
                    SellerSku = displaySku,
                    Title = title,
                    ImgUrl = img,
                    SellerName = sellerName,
                    ShopName = primary.ShopName,
                    ShopProfileName = primary.ShopProfileName,
                    QuantitySold = g.Sum(x => LineQty(x.Quantity)),
                    OrderCount = g.Select(x => x.Id).Distinct().Count(),
                    Revenue = g.Sum(x => x.ItemPrice * LineQty(x.Quantity))
                };
            })
            .OrderByDescending(p => p.QuantitySold)
            .ThenByDescending(p => p.Revenue)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(TopProductsLimit)
            .ToList();
    }

    private async Task<IReadOnlyList<SellerLine>> GetActiveSellersAsync(CancellationToken cancellationToken)
    {
        var inSellerRole = await _userManager.GetUsersInRoleAsync(RoleNames.Seller);
        var result = new List<SellerLine>();
        foreach (var user in inSellerRole.Where(u => u.IsActive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayName = string.IsNullOrWhiteSpace(user.FullName)
                ? (user.UserName ?? user.Id.ToString())
                : user.FullName.Trim();
            result.Add(new SellerLine(user.Id, displayName));
        }

        return result;
    }

    private static async Task<OrderStatusCountsDto> GetOrderStatusCountsAsync(
        IQueryable<Order> ordersQ,
        CancellationToken cancellationToken)
    {
        // Sequential counts — DbContext is not safe for concurrent queries.
        var all = await ordersQ.CountAsync(cancellationToken);
        var pending = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.PendingProcess)
            .CountAsync(cancellationToken);
        var purchased = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.Purchased)
            .CountAsync(cancellationToken);
        var awaitingCollection = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.AwaitingCollectionGroup)
            .CountAsync(cancellationToken);
        var inTransit = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.InTransit)
            .CountAsync(cancellationToken);
        var delivered = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.Delivered)
            .CountAsync(cancellationToken);
        var canceled = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.Cancelled)
            .CountAsync(cancellationToken);
        var refund = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.Refund)
            .CountAsync(cancellationToken);
        var uspsUnprocessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.UspsUnprocessed)
            .CountAsync(cancellationToken);
        var uspsProcessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.UspsProcessed)
            .CountAsync(cancellationToken);
        var thankCardPending = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.ThankCardPending)
            .CountAsync(cancellationToken);
        var thankCardProcessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.ThankCardProcessed)
            .CountAsync(cancellationToken);
        var refundUnprocessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.RefundUnprocessed)
            .CountAsync(cancellationToken);
        var refundProcessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.RefundProcessed)
            .CountAsync(cancellationToken);
        var uspsThankCardPending = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.UspsThankCardPending)
            .CountAsync(cancellationToken);
        var uspsThankCardProcessed = await OrderWorkflowTabFilter
            .WhereWorkflowTab(ordersQ, OrderWorkflowTabs.UspsThankCardProcessed)
            .CountAsync(cancellationToken);

        // Shop not set up for KPI money: not Active and/or no receiving bank card.
        // Exclude CANCELLED/REFUND so dashboard tiles partition without overlap:
        // all = orderCount (money-eligible) + canceled + refund + suspended.
        var suspended = await WhereOrderStatusOk(ordersQ)
            .Where(o =>
                o.Shop == null ||
                o.Shop.Status != ShopStatuses.Active ||
                o.Shop.ReceivingBankAccountId == null)
            .CountAsync(cancellationToken);

        return new OrderStatusCountsDto
        {
            All = all,
            PendingProcess = pending,
            Purchased = purchased,
            AwaitingCollection = awaitingCollection,
            InTransit = inTransit,
            Delivered = delivered,
            Canceled = canceled,
            Refund = refund,
            UspsUnprocessed = uspsUnprocessed,
            UspsProcessed = uspsProcessed,
            ThankCardPending = thankCardPending,
            ThankCardProcessed = thankCardProcessed,
            RefundUnprocessed = refundUnprocessed,
            RefundProcessed = refundProcessed,
            UspsThankCardPending = uspsThankCardPending,
            UspsThankCardProcessed = uspsThankCardProcessed,
            Suspended = suspended,
            OnHold = pending,
            AwaitingShipment = 0
        };
    }

    /// <summary>
    /// Net fulfil purchase spend (Debit − Credit) + Σ CouponAmount in the UTC window,
    /// optionally scoped by dashboard order filters. Single sequential query (DbContext is not concurrent-safe).
    /// Cost formula matches <c>/admin/fulfil/transactions/stats</c> / Lịch sử mua hàng.
    /// Coupon matches the Coupon column snapshot (CostUpdate; Manual null → 0) — not part of Cost.
    /// </summary>
    private async Task<(decimal Cost, decimal Coupon)> SumFulfilSpendAndCouponAsync(
        DashboardQuery query,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken cancellationToken)
    {
        var q = _db.FulfilTransactions.AsNoTracking()
            .Where(t => t.OccurredAt >= rangeStartUtc && t.OccurredAt < rangeEndUtc);

        if (query.ShopId.HasValue)
        {
            q = q.Where(t => t.Order != null && t.Order.ShopId == query.ShopId.Value);
        }

        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(t => t.Order != null && t.Order.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToUpperInvariant();
            q = q.Where(t =>
                t.Order != null &&
                t.Order.Platform != null &&
                t.Order.Platform.ToUpper() == platform);
        }

        // One round-trip: both aggregates. Empty set → FirstOrDefault null → (0, 0).
        var row = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cost = g.Sum(t =>
                    t.Direction == FulfilTransaction.DirectionCredit ? -t.Amount : t.Amount),
                Coupon = g.Sum(t => t.CouponAmount ?? 0m)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? (0m, 0m) : (row.Cost, row.Coupon);
    }

    /// <summary>
    /// Net receiving-bank income (Credit − Debit) for Confirmed rows in the UTC window.
    /// Same signed formula as <c>/admin/receiving-bank-accounts/transactions/stats</c>.
    /// </summary>
    private async Task<decimal> SumReceivingIncomeAsync(
        DashboardQuery query,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken cancellationToken)
    {
        var q = _db.ReceivingBankTransactions.AsNoTracking()
            .Where(t =>
                t.ConfirmationStatus == ReceivingBankTransaction.ConfirmationConfirmed &&
                t.OccurredAt >= rangeStartUtc &&
                t.OccurredAt < rangeEndUtc);

        if (query.ShopId.HasValue)
        {
            q = q.Where(t => t.ShopId == query.ShopId.Value);
        }

        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(t => t.Shop != null && t.Shop.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToUpperInvariant();
            q = q.Where(t =>
                t.Shop != null &&
                t.Shop.Platform != null &&
                t.Shop.Platform.Code.ToUpper() == platform);
        }

        return await q.SumAsync(
            t => t.Direction == ReceivingBankTransaction.DirectionCredit ? t.Amount : -t.Amount,
            cancellationToken);
    }

    private static IQueryable<Order> ApplyOrderFilters(IQueryable<Order> q, DashboardQuery query)
    {
        // Order counts / backlog: all shops + all statuses (owner/platform/shopId only).
        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(o => o.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToUpperInvariant();
            q = q.Where(o => o.Platform != null && o.Platform.ToUpper() == platform);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(o => o.ShopId == query.ShopId.Value);
        }

        return q;
    }

    /// <summary>
    /// Order count “OK”: exclude CANCELLED / REFUND (mọi shop).
    /// Khớp phần status của <see cref="WhereMoneyEligible"/> (không yêu cầu shop Active).
    /// </summary>
    private static bool IsOrderStatusOk(string? platformOrderStatus, string? orderStatus)
    {
        if (platformOrderStatus is "CANCELLED" or "CANCELED" or "REFUND" or "REFUNDED")
        {
            return false;
        }

        if (string.IsNullOrEmpty(platformOrderStatus) &&
            orderStatus is "canceled" or "cancelled")
        {
            return false;
        }

        return true;
    }

    /// <summary>CANCELLED / CANCELED (+ legacy canceled khi thiếu platform status).</summary>
    private static bool IsOrderCanceled(string? platformOrderStatus, string? orderStatus)
    {
        if (platformOrderStatus is "CANCELLED" or "CANCELED")
        {
            return true;
        }

        return string.IsNullOrEmpty(platformOrderStatus) &&
               orderStatus is "canceled" or "cancelled";
    }

    /// <summary>REFUND / REFUNDED.</summary>
    private static bool IsOrderRefund(string? platformOrderStatus) =>
        platformOrderStatus is "REFUND" or "REFUNDED";

    private static (bool IsOk, bool IsCanceled, bool IsRefund) ClassifyTrendOrderStatus(
        string? platformOrderStatus,
        string? orderStatus) =>
    (
        IsOrderStatusOk(platformOrderStatus, orderStatus),
        IsOrderCanceled(platformOrderStatus, orderStatus),
        IsOrderRefund(platformOrderStatus)
    );

    /// <summary>
    /// EF filter: exclude CANCELLED / REFUND (mọi shop). Used by summary KPI Orders + Revenue.
    /// </summary>
    private static IQueryable<Order> WhereOrderStatusOk(IQueryable<Order> q) =>
        q.Where(o =>
            o.PlatformOrderStatus != "CANCELLED" &&
            o.PlatformOrderStatus != "CANCELED" &&
            o.PlatformOrderStatus != "REFUND" &&
            o.PlatformOrderStatus != "REFUNDED" &&
            !((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "") &&
              (o.OrderStatus == "canceled" || o.OrderStatus == "cancelled")));

    /// <summary>
    /// KPI Orders + Revenue / top-products money: Active shop với thẻ nhận tiền đã gắn,
    /// exclude cancelled/refund (EF-translatable).
    /// </summary>
    private static IQueryable<Order> WhereMoneyEligible(IQueryable<Order> q) =>
        WhereOrderStatusOk(q).Where(o =>
            o.Shop != null &&
            o.Shop.Status == ShopStatuses.Active &&
            o.Shop.ReceivingBankAccountId != null);

    private static IQueryable<Listing> ApplyListingFilters(IQueryable<Listing> q, DashboardQuery query)
    {
        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(l => l.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToLowerInvariant();
            q = q.Where(l => l.Platform.ToLower() == platform);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(l => l.ShopId == query.ShopId.Value);
        }

        return q;
    }

    private static IQueryable<Shop> ApplyShopFilters(IQueryable<Shop> q, DashboardQuery query)
    {
        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(s => s.OwnerId == query.OwnerUserId.Value);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(s => s.Id == query.ShopId.Value);
        }

        // Platform filter for shops uses Platform navigation — skip string platform when only PlatformId exists.
        // Shop list filters by PlatformId on FE; dashboard platform filter applies to orders/listings primarily.
        return q;
    }

    /// <summary>
    /// Order purchase cost already resolved as TrueSpend + BuyTrack + ThankCard in projections.
    /// </summary>
    private static decimal ComputeOrderTotalCost(decimal? purchaseCost) =>
        purchaseCost ?? 0m;

    private static string NormalizePeriod(string? period)
    {
        var value = (period ?? "day").Trim().ToLowerInvariant();
        return value is "week" or "month" ? value : "day";
    }

    /// <summary>
    /// Null = compute all sections (legacy). Otherwise only listed keys (lowercase).
    /// </summary>
    private static HashSet<string>? ParseIncludeSections(string? include)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return null;
        }

        var set = include
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return set.Count == 0 ? null : set;
    }

    /// <summary>
    /// True when the client only needs order counts (skip revenue/cost/Items joins).
    /// </summary>
    private static bool IsOrdersOnlyMetrics(string? metrics) =>
        string.Equals(metrics?.Trim(), "orders", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the client needs orders+revenue but not cost (skip Items/cost joins).
    /// </summary>
    private static bool IsRevenueOnlyMetrics(string? metrics) =>
        string.Equals(metrics?.Trim(), "revenue", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Calendar window in VN local dates (inclusive): today / Mon–today / 1st–today.
    /// </summary>
    private static (DateTime FromLocal, DateTime ToLocalInclusive) ResolvePeriodLocalDates(
        DateTime localToday,
        string period)
    {
        var end = localToday.Date;
        return period switch
        {
            "week" => (StartOfIsoWeek(end), end),
            "month" => (new DateTime(end.Year, end.Month, 1), end),
            _ => (end, end)
        };
    }

    private static DateTime StartOfIsoWeek(DateTime localDate)
    {
        // Monday = start (ISO). DayOfWeek: Sunday=0 … Saturday=6.
        var dow = (int)localDate.DayOfWeek;
        var daysFromMonday = dow == 0 ? 6 : dow - 1;
        return localDate.Date.AddDays(-daysFromMonday);
    }

    private static string ToLocalDateKey(DateTime utcOrUnspecified, TimeZoneInfo tz)
    {
        var utc = utcOrUnspecified.Kind == DateTimeKind.Utc
            ? utcOrUnspecified
            : DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static int ToLocalHour(DateTime utcOrUnspecified, TimeZoneInfo tz)
    {
        var utc = utcOrUnspecified.Kind == DateTimeKind.Utc
            ? utcOrUnspecified
            : DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz).Hour;
    }

    private static TimeZoneInfo ResolveVietnamTz()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    private sealed record SellerLine(Guid Id, string DisplayName);
}
