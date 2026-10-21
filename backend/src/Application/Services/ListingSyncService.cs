using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Lists local listing mirrors and syncs / pushes via 4Seller.
/// </summary>
public class ListingSyncService : IListingSyncService
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        // Required so JsonNode.ToJsonString can reuse this static options after first serialize.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>Max batch-detail ids per page in light mode (new / empty RawPayload only).</summary>
    private const int LightDetailCapPerPage = 10;

    /// <summary>Manual UI Sync waits briefly when the same seller is finishing an extension pull.</summary>
    private static readonly TimeSpan ManualLeaseRetryDelay = TimeSpan.FromSeconds(2);

    private const int ManualLeaseRetryMaxAttempts = 6;

    /// <summary>
    /// One 4Seller call may stall far past a useful wait, so each gets its own budget and a slow
    /// page cannot consume the seller's whole window.
    /// </summary>
    private static readonly TimeSpan FourSellerCallBudget = TimeSpan.FromSeconds(45);

    /// <summary>Fixed part of a seller's budget: connection setup, is-login, shop lookup.</summary>
    private static readonly TimeSpan SellerBudgetBase = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Allowance per expected row. Upserting a listing means rewriting a large jsonb payload over a
    /// remote DB, which measures around a second per row, so the budget has to track the workload.
    /// </summary>
    private static readonly TimeSpan SellerBudgetPerRecord = TimeSpan.FromSeconds(1.5);

    /// <summary>Hard ceiling so an unbounded full sync still cannot hang forever.</summary>
    private static readonly TimeSpan SellerBudgetMax = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Manual published Sync needs headroom after page pull for multi-round id reconcile + purge.
    /// </summary>
    private static readonly TimeSpan SellerBudgetMaxManual = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Sellers sync in parallel, but not so many that 4Seller egress and the DB pool starve the
    /// rest of the API.
    /// </summary>
    private const int MaxParallelSellers = 4;

    /// <summary>
    /// Sent watermark is pulled back by this much: 4Seller writes updateTime on its own clock, and a
    /// row saved in the same second as the previous cut-off must not fall through the gap.
    /// </summary>
    private static readonly TimeSpan DeltaSafetyMargin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often a bucket is re-read in full despite a valid watermark, so a delta run that silently
    /// missed a row heals by itself.
    /// </summary>
    private static readonly TimeSpan FullScanInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// How long a Publish push may keep a listing on the optimistic Published/Pending state while
    /// 4Seller still lists it under draft. Generous compared to the minutes 4Seller normally needs,
    /// but bounded so a publish that never took cannot pin the row on Reviewing forever.
    /// </summary>
    private static readonly TimeSpan OptimisticPendingWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// When 4Seller omits <c>productStatus</c> on a draft-page row, bridge optimistic Pending only
    /// briefly — long enough for the row to flip to Reviewing, not long enough to mask a silent fail.
    /// </summary>
    private static readonly TimeSpan OptimisticPendingGracePeriod = TimeSpan.FromMinutes(30);

    /// <summary>Re-check local in-flight / stale pending rows after each published status pull.</summary>
    private const int ReconcileStaleInFlightMaxPerSeller = 50;

    /// <summary>
    /// Manual Sync heals a larger Reviewing backlog per seller (listingIds page lookup).
    /// Background stays at <see cref="ReconcileStaleInFlightMaxPerSeller"/> to keep loops light.
    /// </summary>
    private const int ReconcileStaleInFlightMaxPerSellerManual = 200;

    /// <summary>Manual reconcile loops until backlog clears or this many rounds.</summary>
    private const int ReconcileManualMaxRounds = 5;

    /// <summary>4Seller <c>listingIdList</c> is reliable in small batches (Dev reconcile uses 50).</summary>
    private const int ListingIdChunkSize = 50;

    /// <summary>Pending rows not synced recently may have moved bucket on 4Seller.</summary>
    private static readonly TimeSpan StalePendingSyncThreshold = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _db;
    private readonly IFourSellerApiService _fourSeller;
    private readonly ISupplierProductInfoService _supplierProductInfos;
    private readonly IListingAddOriginService _listingAddOrigins;
    private readonly IPostHtmlDraftSyncScheduler _postHtmlDraftSyncScheduler;
    private readonly IListingSellerSyncLease _sellerLease;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IListingAutoPublishQueueStatusResolver _autoPublishQueueStatus;
    private readonly ITikTokAttributeRejectionService _attributeRejections;
    private readonly ITikTokCategorySchemaProvider _categorySchema;
    private readonly IShopOwnershipTransferService _ownershipTransfer;
    private readonly ILogger<ListingSyncService> _logger;

    /// <summary>
    /// Initializes the listing sync service.
    /// </summary>
    public ListingSyncService(
        IApplicationDbContext db,
        IFourSellerApiService fourSeller,
        ISupplierProductInfoService supplierProductInfos,
        IListingAddOriginService listingAddOrigins,
        IPostHtmlDraftSyncScheduler postHtmlDraftSyncScheduler,
        IListingSellerSyncLease sellerLease,
        IServiceScopeFactory scopeFactory,
        IListingAutoPublishQueueStatusResolver autoPublishQueueStatus,
        ITikTokAttributeRejectionService attributeRejections,
        ITikTokCategorySchemaProvider categorySchema,
        IShopOwnershipTransferService ownershipTransfer,
        ILogger<ListingSyncService> logger)
    {
        _db = db;
        _fourSeller = fourSeller;
        _supplierProductInfos = supplierProductInfos;
        _listingAddOrigins = listingAddOrigins;
        _postHtmlDraftSyncScheduler = postHtmlDraftSyncScheduler;
        _sellerLease = sellerLease;
        _scopeFactory = scopeFactory;
        _autoPublishQueueStatus = autoPublishQueueStatus;
        _attributeRejections = attributeRejections;
        _categorySchema = categorySchema;
        _ownershipTransfer = ownershipTransfer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ListingListPageDto> ListAsync(
        ListingListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

        // Slim projection: never materialize RawPayload / DescriptionHtml / Owner cookies.
        var q = BuildListQuery(query, includeNav: false);

        // Sequential on shared DbContext (not thread-safe). Projection + light status counts is the win.
        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(l => l.PlatformUpdateTime ?? l.LastSyncedAt)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ListingListItemDto
            {
                Id = l.Id,
                FourSellerListingId = l.FourSellerListingId,
                Platform = l.Platform,
                PageType = l.PageType,
                SiteStatus = l.SiteStatus,
                ProductName = l.ProductName,
                MainImage = l.MainImage,
                Site = l.Site,
                Currency = l.Currency,
                LowPrice = l.LowPrice,
                HighPrice = l.HighPrice,
                AvailableStock = l.AvailableStock,
                VariationCount = l.VariationCount,
                CategoryName = l.CategoryName,
                ErpStatus = l.ErpStatus,
                PublishStatus = l.PublishStatus,
                TikTokProductId = l.TikTokProductId,
                PlatformUpdateTime = l.PlatformUpdateTime,
                LastSyncedAt = l.LastSyncedAt,
                LastPushAt = l.LastPushAt,
                SyncError = l.SyncError,
                PublishSource = l.PublishSource,
                AutoPublishSkipped = l.AutoPublishSkipped,
                AutoPublishSkippedAt = l.AutoPublishSkippedAt,
                AutoPublishSkipReason = l.AutoPublishSkipReason,
                LastAutoPublishRunId = l.LastAutoPublishRunId,
                OwnerId = l.OwnerId,
                OwnerName = l.Owner == null
                    ? null
                    : (l.Owner.FullName != null && l.Owner.FullName != ""
                        ? l.Owner.FullName
                        : l.Owner.UserName),
                ShopId = l.ShopId,
                ShopName = l.Shop != null ? l.Shop.Name : null,
                ShopProfileName = l.Shop != null ? l.Shop.ProfileName : null,
                SourceUrl = l.SourceUrl,
                AddedFrom = l.AddedFrom,
                ReviewCount = l.ReviewCount,
                AverageRating = l.AverageRating,
                StoreLogo = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.StoreLogo
                    : null,
                StoreTitle = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.StoreTitle
                    : null,
                StoreUrl = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.StoreUrl
                    : null,
                ShopRank = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.ShopRank
                    : null,
                StoreAttention = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.StoreAttention
                    : null,
                SoldByTips = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.SoldByTips
                    : null,
                ShippingDays = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.ShippingDays
                    : null,
                ShippingDayPercent = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.ShippingDayPercent
                    : null,
                SourceStock = l.SupplierProductInfo != null ? l.SupplierProductInfo.Stock : null,
                IsLocalProduct = l.SupplierProductInfo != null ? l.SupplierProductInfo.IsLocalProduct : null,
                IsSupportQuickShip = l.SupplierProductInfo != null ? l.SupplierProductInfo.IsSupportQuickShip : null,
                SellerPlatform = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.Platform
                    : null,
                StoreCode = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                    ? l.SupplierProductInfo.Seller.StoreCode
                    : null,
                HasSizeChart = l.SupplierProductInfo != null
                    && l.SupplierProductInfo.SizeChartImageUrl != null
                    && l.SupplierProductInfo.SizeChartImageUrl != "",
                CreatedAt = l.CreatedAt
            })
            .ToListAsync(cancellationToken);

        await AttachListVariationsAsync(items, cancellationToken);

        foreach (var dto in items)
        {
            ApplySellerDisplayTitle(dto);
            if (string.Equals(dto.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
            {
                ApplyDraftCostPriceForDisplay(dto);
            }
        }

        if (string.Equals(query.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase)
            && items.Count > 0)
        {
            var queueMap = await _autoPublishQueueStatus.ResolveAsync(
                items.Select(i => i.Id).ToList(),
                cancellationToken);
            foreach (var dto in items)
            {
                if (queueMap.TryGetValue(dto.Id, out var queue))
                {
                    dto.AutoPublishQueueStatus = queue.QueueStatus;
                    dto.LastAutoPublishRunStatus = queue.LastRunStatus;
                    dto.AutoPublishNextPickAt = queue.NextPickAt;
                }
            }
        }

        var statusCounts = await GetStatusCountsAsync(query, cancellationToken);

        return new ListingListPageDto
        {
            Items = items,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            StatusCounts = statusCounts
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ListingAutoPublishQueueStatusItemDto>> GetAutoPublishQueueStatusAsync(
        IReadOnlyList<Guid> listingIds,
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (listingIds.Count == 0)
        {
            return [];
        }

        const int maxIds = 200;
        var ids = listingIds.Distinct().Take(maxIds).ToList();

        var rowsQuery = _db.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.Id) && l.PageType == ListingPageTypes.Draft);
        if (ownerUserId is Guid owner)
        {
            rowsQuery = rowsQuery.Where(l => l.OwnerId == owner);
        }

        var rows = await rowsQuery
            .Select(l => new
            {
                l.Id,
                l.AutoPublishSkipped,
                l.AutoPublishSkipReason,
                l.LastAutoPublishRunId
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var queueMap = await _autoPublishQueueStatus.ResolveAsync(
            rows.Select(r => r.Id).ToList(),
            cancellationToken);

        return rows.Select(row =>
        {
            queueMap.TryGetValue(row.Id, out var queue);
            return new ListingAutoPublishQueueStatusItemDto
            {
                Id = row.Id,
                AutoPublishQueueStatus = queue?.QueueStatus,
                AutoPublishNextPickAt = queue?.NextPickAt,
                LastAutoPublishRunStatus = queue?.LastRunStatus,
                AutoPublishSkipped = row.AutoPublishSkipped,
                AutoPublishSkipReason = row.AutoPublishSkipReason,
                LastAutoPublishRunId = row.LastAutoPublishRunId
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<ListingRecentPageDto> ListRecentListingsAsync(
        Guid ownerId,
        string? pageType = null,
        IReadOnlyList<long>? fourSellerShopIds = null,
        int pageCurrent = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedPageType = ListingRecentQuery.NormalizePageType(pageType);
        var (page, size, skip) = ListingRecentQuery.NormalizePaging(pageCurrent, pageSize);

        var filtered = ListingRecentQuery.ApplyFilter(
            _db.Listings.AsNoTracking(),
            ownerId,
            normalizedPageType,
            fourSellerShopIds);

        var total = await filtered.CountAsync(cancellationToken);

        var pageQuery = ListingRecentQuery.ApplyFilter(
            _db.Listings.AsNoTracking()
                .Include(l => l.Shop)
                .Include(l => l.SupplierProductInfo)
                    .ThenInclude(p => p!.Seller),
            ownerId,
            normalizedPageType,
            fourSellerShopIds);

        var items = await ListingRecentQuery.ApplySort(pageQuery, normalizedPageType)
            .Skip(skip)
            .Take(size)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapListItem).ToList();
        await AttachListVariationsAsync(dtos, cancellationToken);
        if (normalizedPageType == ListingPageTypes.Draft)
        {
            foreach (var dto in dtos)
            {
                ApplyDraftCostPriceForDisplay(dto);
            }
        }

        return new ListingRecentPageDto
        {
            Items = dtos,
            Total = total,
            PageCurrent = page,
            PageSize = size
        };
    }

    /// <inheritdoc />
    public async Task<ListingTabCountsDto> GetTabCountsAsync(
        Guid ownerId,
        IReadOnlyList<long>? fourSellerShopIds = null,
        CancellationToken cancellationToken = default)
    {
        var shopIds = (fourSellerShopIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (shopIds.Count == 0)
        {
            return new ListingTabCountsDto();
        }

        var draftTotal = await ListingRecentQuery.ApplyFilter(
            _db.Listings.AsNoTracking(),
            ownerId,
            ListingPageTypes.Draft,
            shopIds).CountAsync(cancellationToken);

        var shopKeys = shopIds.Select(id => id.ToString()).ToList();
        var limits = await _db.Shops.AsNoTracking()
            .Where(s =>
                s.OwnerId == ownerId &&
                s.FourSellerShopId != null &&
                s.FourSellerShopId != "" &&
                shopKeys.Contains(s.FourSellerShopId))
            .Select(s => new { s.PublishLimitPublished, s.PublishLimitTotal })
            .ToListAsync(cancellationToken);

        return new ListingTabCountsDto
        {
            DraftTotal = draftTotal,
            PublishLimitPublished = limits.Sum(s => s.PublishLimitPublished ?? 0),
            PublishLimitTotal = limits.Sum(s => s.PublishLimitTotal ?? 0)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AddedSourceShopDto>> ListOwnedAddedSourceShopsAsync(
        Guid ownerId,
        int take = 5000,
        CancellationToken cancellationToken = default)
    {
        take = take < 1 ? 5000 : Math.Min(take, 5000);
        return await _db.Listings.AsNoTracking()
            .Where(l =>
                l.OwnerId == ownerId &&
                l.FourSellerShopId != null &&
                l.SourceUrl != null &&
                l.SourceUrl != "")
            .OrderByDescending(l => l.PlatformCreateTime ?? l.CreatedAt)
            .Take(take)
            .Select(l => new AddedSourceShopDto
            {
                SourceUrl = l.SourceUrl!,
                FourSellerShopId = l.FourSellerShopId!.Value
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Fills empty store title from Shein soldByTips ("Sold by kcruab-kp").</summary>
    private static void ApplySellerDisplayTitle(ListingListItemDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.StoreTitle))
        {
            return;
        }

        dto.StoreTitle = SupplierPdpMetrics.ParseStoreTitleFromSoldByTips(dto.SoldByTips);
    }

    /// <summary>
    /// For draft cards (admin + browser-extension): always prefer variation
    /// <see cref="ListingVariation.InitialPrice"/> (cost / giá nhập) over listing
    /// LowPrice/HighPrice (often 4Seller page sale range).
    /// </summary>
    private static void ApplyDraftCostPriceForDisplay(ListingListItemDto dto)
    {
        var costs = dto.Variations
            .Where(v => v.InitialPrice is > 0m)
            .Select(v => v.InitialPrice!.Value)
            .ToList();
        if (costs.Count == 0)
        {
            return;
        }

        dto.LowPrice = costs.Min();
        dto.HighPrice = costs.Max();
    }

    /// <inheritdoc />
    public async Task<ListingStatusCountsDto> GetStatusCountsAsync(
        ListingListQuery query,
        CancellationToken cancellationToken = default)
    {
        // Scope: same filters except SiteStatus (and without paging).
        var scopeQuery = new ListingListQuery
        {
            PageType = query.PageType,
            OwnerUserId = query.OwnerUserId,
            Platform = query.Platform,
            ShopId = query.ShopId,
            Search = query.Search,
            DateFrom = query.DateFrom,
            DateTo = query.DateTo
        };

        var baseQ = BuildListQuery(scopeQuery, includeNav: false);

        // One GroupBy replaces separate Count + per-status scans.
        var byStatus = await baseQ
            .GroupBy(l => l.SiteStatus ?? string.Empty)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var allInScope = byStatus.Sum(x => x.Count);

        int CountOf(string status) =>
            byStatus.FirstOrDefault(x =>
                string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

        // When pageType is already scoped, Draft/Published equal All / 0 — skip 2 extra counts.
        var pageType = (query.PageType ?? string.Empty).Trim().ToLowerInvariant();
        int draftCount;
        int publishedCount;
        if (pageType == ListingPageTypes.Draft)
        {
            draftCount = allInScope;
            publishedCount = 0;
        }
        else if (pageType == ListingPageTypes.Published)
        {
            draftCount = 0;
            publishedCount = allInScope;
        }
        else
        {
            var draftQ = BuildListQuery(new ListingListQuery
            {
                PageType = ListingPageTypes.Draft,
                OwnerUserId = query.OwnerUserId,
                Platform = query.Platform,
                ShopId = query.ShopId,
                Search = query.Search,
                DateFrom = query.DateFrom,
                DateTo = query.DateTo
            }, includeNav: false);
            var publishedQ = BuildListQuery(new ListingListQuery
            {
                PageType = ListingPageTypes.Published,
                OwnerUserId = query.OwnerUserId,
                Platform = query.Platform,
                ShopId = query.ShopId,
                Search = query.Search,
                DateFrom = query.DateFrom,
                DateTo = query.DateTo
            }, includeNav: false);

            draftCount = await draftQ.CountAsync(cancellationToken);
            publishedCount = await publishedQ.CountAsync(cancellationToken);
        }

        return new ListingStatusCountsDto
        {
            All = allInScope,
            Draft = draftCount,
            Published = publishedCount,
            Active = CountOf(ListingSiteStatuses.Active),
            Inactive = CountOf(ListingSiteStatuses.Inactive),
            Removed = CountOf(ListingSiteStatuses.Removed),
            Incomplete = CountOf(ListingSiteStatuses.Incomplete),
            Suspended = CountOf(ListingSiteStatuses.Suspended),
            Private = CountOf(ListingSiteStatuses.Private),
            Pending = CountOf(ListingSiteStatuses.Pending),
            Reject = CountOf(ListingSiteStatuses.Reject),
            Unsaleable = CountOf(ListingSiteStatuses.Unsaleable)
        };
    }

    /// <inheritdoc />
    public async Task<ListingDetailDto> GetByIdAsync(
        Guid id,
        bool refreshFromFourSeller = false,
        CancellationToken cancellationToken = default)
    {
        // Fetch the parent (Owner/Shop) and variations in TWO queries — NEVER a single
        // Include(Variations) JOIN, which repeats the large RawPayload/DescriptionHtml columns
        // once per SKU row (cartesian), inflating wire size N× for multi-SKU listings (the main
        // GET cost). Local open uses AsNoTracking (no change-tracker overhead); the refresh path
        // loads variations tracked via LoadAsync so relationship-fixup populates listing.Variations
        // for ApplyRecord / SyncVariations.
        IQueryable<Listing> listingQuery = _db.Listings
            .Include(l => l.Owner)
            .Include(l => l.Shop)
            .Include(l => l.SupplierProductInfo);
        if (!refreshFromFourSeller)
        {
            listingQuery = listingQuery.AsNoTracking();
        }

        var listing = await listingQuery
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (refreshFromFourSeller)
        {
            // Tracked load: EF relationship-fixup wires these onto listing.Variations.
            await _db.ListingVariations
                .Where(v => v.ListingId == listing.Id)
                .LoadAsync(cancellationToken);
        }
        else
        {
            listing.Variations = await _db.ListingVariations
                .AsNoTracking()
                .Where(v => v.ListingId == listing.Id)
                .ToListAsync(cancellationToken);
        }

        // Default: local DB only (fast edit open). Optional refresh pulls full per-id /detail
        // (skuImgs / gallery / description) — used by FE background sync after local hydrate.
        if (refreshFromFourSeller)
        {
            await TryRefreshFullDetailAsync(listing, cancellationToken);
        }

        return await MapDetailWithConditionDefaultsAsync(listing, cancellationToken);
    }

    private async Task<ListingDetailDto> MapDetailWithConditionDefaultsAsync(
        Listing listing,
        CancellationToken cancellationToken)
    {
        var detail = MapDetail(listing);
        if (detail.SizeGuideJson is null && !string.IsNullOrWhiteSpace(listing.SourceUrl))
        {
            var sourceKey = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                var supplierSnapshot = await _db.SupplierProductInfos
                    .AsNoTracking()
                    .Where(s => s.SourceUrlKey == sourceKey)
                    .Select(s => new { s.SizeGuideJson, s.SizeChartImageUrl })
                    .FirstOrDefaultAsync(cancellationToken);
                if (supplierSnapshot is not null)
                {
                    if (!string.IsNullOrWhiteSpace(supplierSnapshot.SizeGuideJson))
                    {
                        detail.SizeGuideJson = DeserializeJsonElementOrNull(supplierSnapshot.SizeGuideJson);
                    }

                    if (string.IsNullOrWhiteSpace(detail.SizeChartCaptureUrl)
                        && !string.IsNullOrWhiteSpace(supplierSnapshot.SizeChartImageUrl))
                    {
                        detail.SizeChartCaptureUrl = supplierSnapshot.SizeChartImageUrl.Trim();
                    }
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(detail.SizeChartCaptureUrl)
                 && !string.IsNullOrWhiteSpace(listing.SourceUrl))
        {
            var sourceKey = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                var captureUrl = await _db.SupplierProductInfos
                    .AsNoTracking()
                    .Where(s => s.SourceUrlKey == sourceKey && s.SizeChartImageUrl != null)
                    .Select(s => s.SizeChartImageUrl)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(captureUrl))
                {
                    detail.SizeChartCaptureUrl = captureUrl.Trim();
                }
            }
        }

        detail.Attributes = await EnrichAttributesWithConditionDefaultsAsync(
            listing,
            detail.Attributes,
            cancellationToken);
        return detail;
    }

    /// <summary>
    /// When the category schema exposes Condition type/description, merge canonical defaults into
    /// the mirror attributes shown on the edit form (ids/valueIds from 4Seller schema).
    /// </summary>
    private async Task<List<ListingAttributeDto>> EnrichAttributesWithConditionDefaultsAsync(
        Listing listing,
        List<ListingAttributeDto> attributes,
        CancellationToken cancellationToken)
    {
        var schema = await TryLoadCategorySchemaAsync(listing, cancellationToken);
        if (schema is null || schema.Count == 0)
        {
            return attributes;
        }

        return ListingSpecificsMappingEngine.MergeConditionDefaultsIntoAttributes(attributes, schema);
    }

    /// <summary>
    /// Loads category attribute schema for Condition defaults / sale-prop resolution; null on failure.
    /// </summary>
    private async Task<IReadOnlyList<FourSellerCategoryAttributeDto>?> TryLoadCategorySchemaAsync(
        Listing listing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(listing.CategoryId) || listing.FourSellerShopId is not > 0)
        {
            return null;
        }

        try
        {
            return await _categorySchema.GetAsync(
                listing.OwnerId,
                listing.CategoryId.Trim(),
                listing.FourSellerShopId.Value,
                string.IsNullOrWhiteSpace(listing.Site) ? "US" : listing.Site.Trim(),
                cancellationToken);
        }
        catch (BusinessException ex)
        {
            _logger.LogDebug(
                ex,
                "Category schema lookup skipped for listing {ListingId}",
                listing.Id);
            return null;
        }
    }

    /// <summary>
    /// Patches <c>productAttributes</c> in RawPayload with Condition defaults when the category requires them.
    /// </summary>
    private async Task TryPatchConditionSpecificsInRawPayloadAsync(
        Listing listing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return;
        }

        JsonObject payloadObj;
        try
        {
            payloadObj = JsonNode.Parse(listing.RawPayload) as JsonObject
                         ?? new JsonObject();
        }
        catch (JsonException)
        {
            return;
        }

        var before = payloadObj.ToJsonString(PatchJsonOptions);
        await EnsureConditionSpecificsInPayloadAsync(listing, payloadObj, cancellationToken);
        var after = payloadObj.ToJsonString(PatchJsonOptions);
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            listing.RawPayload = after;
        }
    }

    /// <summary>
    /// Best-effort refresh of a single listing from 4Seller per-id <c>/detail</c> (full skuImgs /
    /// gallery / description). Silently no-ops when the owner cookie is not logged in or 4Seller fails.
    /// </summary>
    private async Task TryRefreshFullDetailAsync(Listing listing, CancellationToken cancellationToken)
    {
        if (listing.FourSellerListingId <= 0)
        {
            return;
        }

        var owner = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == listing.OwnerId)
            .Select(u => new { u.FourSellerCookie, u.FourSellerCookieLoginStatus })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner is null ||
            string.IsNullOrWhiteSpace(owner.FourSellerCookie) ||
            owner.FourSellerCookieLoginStatus != FourSellerCookieLoginStatuses.LoggedIn)
        {
            return;
        }

        try
        {
            var platform = string.IsNullOrWhiteSpace(listing.Platform) ? "tiktok" : listing.Platform;
            var details = await _fourSeller.GetListingBatchDetailAsync(
                listing.OwnerId,
                platform,
                new[] { listing.FourSellerListingId },
                cancellationToken,
                allowPerIdFallback: true);

            var detailRecord = details.FirstOrDefault(d => d.Id == listing.FourSellerListingId)
                               ?? details.FirstOrDefault();
            if (detailRecord is null || detailRecord.Id <= 0)
            {
                return;
            }

            var pageRecord = await TryFetchPageRecordForRefreshAsync(
                listing.OwnerId,
                platform,
                listing.FourSellerListingId,
                listing.SiteStatus,
                cancellationToken);
            var record = pageRecord is not null
                ? MergePageAndDetail(pageRecord, detailRecord)
                : detailRecord;

            var now = DateTime.UtcNow;
            // Detail endpoint often omits failedMessage — keep page-synced publish errors.
            var previousSyncError = listing.SyncError;
            // Resolve site status from erpStatus / productStatus (detail may omit productStatus).
            var (pageType, siteStatus) = ResolveListingStatuses(
                listing.SiteStatus,
                record.ProductStatus,
                record.ErpStatus,
                record.PublishStatus);
            ApplyRecord(
                listing,
                record,
                listing.OwnerId,
                platform,
                pageType,
                siteStatus,
                listing.ShopId,
                lightMode: false,
                // Explicit user-triggered refresh — the one path allowed to replace RawPayload.
                allowRawPayloadOverwrite: true);
            await _supplierProductInfos.ApplyToListingAsync(listing, cancellationToken);
            ReconcileSyncErrorAfterDetailRefresh(listing, record, previousSyncError);

            SyncVariations(listing, record.VariationList, now);
            await TryPatchConditionSpecificsInRawPayloadAsync(listing, cancellationToken);
            // On-demand refresh always stamps (ApplyRecord no longer sets timestamps).
            StampListingTimestamps(listing, now, stampAllVariations: true);
            await SaveUpsertChangesAsync(cancellationToken);
            await LearnAttributeRejectionsAsync([listing], cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RevertUnsavedListingGraphAsync(listing, cancellationToken);
            _logger.LogWarning(
                ex,
                "On-demand full detail refresh failed for listing {ListingId} ({FourSellerListingId})",
                listing.Id,
                listing.FourSellerListingId);
        }
    }

    /// <summary>
    /// Best-effort page row for one listing id — page list carries publish failures that detail omits.
    /// </summary>
    private async Task<FourSellerListingRecordDto?> TryFetchPageRecordForRefreshAsync(
        Guid ownerId,
        string platform,
        long fourSellerListingId,
        string? hintSiteStatus,
        CancellationToken cancellationToken)
    {
        var idStr = fourSellerListingId.ToString(CultureInfo.InvariantCulture);
        var statusesToTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(hintSiteStatus))
        {
            statusesToTry.Add(hintSiteStatus.Trim());
        }

        if (!statusesToTry.Contains(ListingSiteStatuses.Draft, StringComparer.OrdinalIgnoreCase))
        {
            statusesToTry.Add(ListingSiteStatuses.Draft);
        }

        foreach (var status in new[]
                 {
                     ListingSiteStatuses.Reject,
                     ListingSiteStatuses.Pending,
                     ListingSiteStatuses.Active
                 })
        {
            if (!statusesToTry.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                statusesToTry.Add(status);
            }
        }

        foreach (var status in statusesToTry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await _fourSeller.GetListingPageAsync(
                    ownerId,
                    platform,
                    new FourSellerListingPageRequest
                    {
                        PageCurrent = 1,
                        PageSize = 10,
                        Status = status,
                        ListingIds = [idStr]
                    },
                    cancellationToken);

                var hit = page.Records.FirstOrDefault(r => r.Id == fourSellerListingId);
                if (hit is not null)
                {
                    return hit;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Page fetch for on-demand refresh skipped (owner {OwnerId}, status {Status}, id {ListingId})",
                    ownerId,
                    status,
                    fourSellerListingId);
            }
        }

        return null;
    }

    /// <summary>
    /// Detail refresh often lacks <c>failedMessage</c>. Keep the richer page-synced text while still
    /// blocked; clear when 4Seller reports a healthy publish state.
    /// </summary>
    private static void ReconcileSyncErrorAfterDetailRefresh(
        Listing listing,
        FourSellerListingRecordDto record,
        string? previousSyncError)
    {
        if (!string.IsNullOrWhiteSpace(record.FailedMessage))
        {
            return;
        }

        var stillBlocked =
            listing.PublishStatus == PublishStatuses.Failed || IsRemotePublishBlocked(record);
        if (stillBlocked)
        {
            if (!string.IsNullOrWhiteSpace(previousSyncError)
                && !IsSystemPublishBlockedFallback(previousSyncError)
                && (string.IsNullOrWhiteSpace(listing.SyncError)
                    || IsSystemPublishBlockedFallback(listing.SyncError)))
            {
                listing.SyncError = previousSyncError;
            }

            return;
        }

        listing.SyncError = null;
    }

    /// <summary>
    /// Drops unpersisted listing/variation tracker state and reloads variations from DB
    /// so GetById never returns ghost SKUs after a failed refresh save.
    /// </summary>
    private async Task RevertUnsavedListingGraphAsync(Listing listing, CancellationToken cancellationToken)
    {
        if (_db is not DbContext ctx)
        {
            return;
        }

        foreach (var entry in ctx.ChangeTracker.Entries<ListingVariation>()
                     .Where(e => e.Entity.ListingId == listing.Id)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        var listingEntry = ctx.Entry(listing);
        if (listingEntry.State == EntityState.Modified)
        {
            await listingEntry.ReloadAsync(cancellationToken);
        }

        listing.Variations.Clear();
        await listingEntry.Collection(l => l.Variations).LoadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .Include(l => l.Variations)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var shopId = listing.ShopId;
        var wasActive = string.Equals(
            listing.SiteStatus,
            ListingSiteStatuses.Active,
            StringComparison.OrdinalIgnoreCase);

        if (listing.FourSellerListingId > 0)
        {
            var platform = string.IsNullOrWhiteSpace(listing.Platform) ? "tiktok" : listing.Platform;
            await _fourSeller.DeleteListingsAsync(
                listing.OwnerId,
                platform,
                new[] { listing.FourSellerListingId },
                cancellationToken);
        }

        // Explicit child wipe — do not rely solely on FK cascade / unloaded nav.
        // Mirror store: ListingVariations + columns on Listings (RawPayload jsonb, DescriptionHtml).
        if (listing.Variations.Count > 0)
        {
            _db.ListingVariations.RemoveRange(listing.Variations);
        }

        _db.Listings.Remove(listing);
        if (wasActive)
        {
            await TryDecrementShopPublishPublishedAsync(shopId, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteOwnedDraftAsync(
        Guid ownerId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == ownerId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy draft.");

        if (!string.Equals(listing.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Chỉ được xóa listing ở trạng thái draft.");
        }

        await DeleteByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingDeleteResultDto> DeleteByFilterAsync(
        ListingListQuery query,
        CancellationToken cancellationToken = default)
    {
        var listings = await BuildListQuery(query, tracking: true, includeNav: false)
            .Include(l => l.Variations)
            .ToListAsync(cancellationToken);
        if (listings.Count == 0)
        {
            return new ListingDeleteResultDto { DeletedCount = 0 };
        }

        // Delete on 4Seller first (per owner + platform cookie), then wipe local mirrors.
        foreach (var group in listings
                     .Where(l => l.FourSellerListingId > 0)
                     .GroupBy(l => new
                     {
                         l.OwnerId,
                         Platform = string.IsNullOrWhiteSpace(l.Platform)
                             ? "tiktok"
                             : l.Platform.Trim().ToLowerInvariant()
                     }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = group.Select(l => l.FourSellerListingId).Distinct().ToList();
            await _fourSeller.DeleteListingsAsync(
                group.Key.OwnerId,
                group.Key.Platform,
                ids,
                cancellationToken);
        }

        // Wipe all SKU rows first, then parents (frees RawPayload jsonb with the Listing row).
        var variations = listings.SelectMany(l => l.Variations).ToList();
        if (variations.Count > 0)
        {
            _db.ListingVariations.RemoveRange(variations);
        }

        await TryDecrementShopPublishPublishedForListingsAsync(listings, cancellationToken);

        _db.Listings.RemoveRange(listings);
        await _db.SaveChangesAsync(cancellationToken);
        return new ListingDeleteResultDto { DeletedCount = listings.Count };
    }

    /// <inheritdoc />
    public async Task<ListingDeleteResultDto> DeleteByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (distinctIds.Count == 0)
        {
            return new ListingDeleteResultDto { DeletedCount = 0 };
        }

        var listings = await _db.Listings
            .Include(l => l.Variations)
            .Where(l => distinctIds.Contains(l.Id))
            .ToListAsync(cancellationToken);
        if (listings.Count == 0)
        {
            return new ListingDeleteResultDto { DeletedCount = 0 };
        }

        foreach (var group in listings
                     .Where(l => l.FourSellerListingId > 0)
                     .GroupBy(l => new
                     {
                         l.OwnerId,
                         Platform = string.IsNullOrWhiteSpace(l.Platform)
                             ? "tiktok"
                             : l.Platform.Trim().ToLowerInvariant()
                     }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fourSellerIds = group.Select(l => l.FourSellerListingId).Distinct().ToList();
            await _fourSeller.DeleteListingsAsync(
                group.Key.OwnerId,
                group.Key.Platform,
                fourSellerIds,
                cancellationToken);
        }

        var variations = listings.SelectMany(l => l.Variations).ToList();
        if (variations.Count > 0)
        {
            _db.ListingVariations.RemoveRange(variations);
        }

        await TryDecrementShopPublishPublishedForListingsAsync(listings, cancellationToken);

        _db.Listings.RemoveRange(listings);
        await _db.SaveChangesAsync(cancellationToken);
        return new ListingDeleteResultDto { DeletedCount = listings.Count };
    }

    /// <inheritdoc />
    public async Task<ListingDeactivateResultDto> DeactivateByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (distinctIds.Count == 0)
        {
            return new ListingDeactivateResultDto();
        }

        var listings = await _db.Listings
            .Where(l => distinctIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        var skipped = distinctIds.Count - listings.Count;
        var eligible = listings
            .Where(l =>
                l.FourSellerListingId > 0
                && string.Equals(
                    l.SiteStatus,
                    ListingSiteStatuses.Active,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        skipped += listings.Count - eligible.Count;

        if (eligible.Count == 0)
        {
            return new ListingDeactivateResultDto
            {
                DeactivatedCount = 0,
                SkippedCount = skipped
            };
        }

        foreach (var group in eligible.GroupBy(l => new
                     {
                         l.OwnerId,
                         Platform = string.IsNullOrWhiteSpace(l.Platform)
                             ? "tiktok"
                             : l.Platform.Trim().ToLowerInvariant()
                     }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fourSellerIds = group.Select(l => l.FourSellerListingId).Distinct().ToList();
            await _fourSeller.DeactivateListingsAsync(
                group.Key.OwnerId,
                group.Key.Platform,
                fourSellerIds,
                cancellationToken);
        }

        // Decrement while SiteStatus is still Active (helper filters on Active).
        await TryDecrementShopPublishPublishedForListingsAsync(eligible, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var listing in eligible)
        {
            listing.SiteStatus = ListingSiteStatuses.Inactive;
            listing.PublishStatus = "Inactive";
            listing.LastPushAt = now;
            listing.LastSyncedAt = now;
            listing.SyncError = null;
            listing.UpdatedAt = now;
        }

        await SaveUpsertChangesAsync(cancellationToken);

        foreach (var ownerId in eligible.Select(l => l.OwnerId).Distinct())
        {
            _postHtmlDraftSyncScheduler.Schedule(
                ownerId,
                [ListingSiteStatuses.Active, ListingSiteStatuses.Inactive],
                TimeSpan.Zero,
                ListingSyncScopes.Status);
        }

        return new ListingDeactivateResultDto
        {
            DeactivatedCount = eligible.Count,
            SkippedCount = skipped
        };
    }

    /// <inheritdoc />
    public Task<ListingSyncResultDto> SyncAsync(
        ListingSyncRequest request,
        IProgress<ListingSyncSellerProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Per-seller leases inside SyncCoreAsync — no process-wide gate.
        return SyncCoreAsync(request, progress, cancellationToken);
    }

    private async Task<ListingSyncResultDto> SyncCoreAsync(
        ListingSyncRequest request,
        IProgress<ListingSyncSellerProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var leaseSource = ListingSyncSources.Parse(request.SyncSource);
        var syncScope = ListingSyncScopes.Resolve(request.SyncScope, request.SyncMode);

        // Status-only list sync without an explicit cap: bound work (manual UI default).
        var maxRecords = request.MaxRecordsPerSeller;
        if (ListingSyncScopes.IsStatus(syncScope) && maxRecords is null)
        {
            maxRecords = 200;
        }

        var statuses = (request.Statuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (statuses.Count == 0)
        {
            statuses = ListingSiteStatuses.DefaultSyncStatuses.ToList();
        }

        var platform = string.IsNullOrWhiteSpace(request.Platform)
            ? "tiktok"
            : request.Platform.Trim().ToLowerInvariant();

        var ownersQuery = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn);

        if (request.OwnerUserId.HasValue)
        {
            ownersQuery = ownersQuery.Where(u => u.Id == request.OwnerUserId.Value);
        }

        var owners = await ownersQuery
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);

        var result = new ListingSyncResultDto();

        if (request.OwnerUserId.HasValue && owners.Count == 0)
        {
            var ownerMeta = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == request.OwnerUserId.Value)
                .Select(u => new
                {
                    u.Id,
                    HasCookie = u.FourSellerCookie != null && u.FourSellerCookie.Trim() != string.Empty,
                    u.FourSellerCookieLoginStatus
                })
                .FirstOrDefaultAsync(cancellationToken);

            var message = ownerMeta is null
                ? "Seller không tồn tại hoặc chưa có cookie 4Seller."
                : !ownerMeta.HasCookie
                    ? "Seller không tồn tại hoặc chưa có cookie 4Seller."
                    : ownerMeta.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.Expired
                        ? "Cookie 4Seller đã hết hạn. Chạy Check login hoặc cập nhật cookie."
                        : "Cookie 4Seller chưa được xác nhận đăng nhập. Chạy Check login trước khi đồng bộ.";

            result.FailedSellers.Add(new ListingSyncSellerFailureDto
            {
                OwnerUserId = request.OwnerUserId.Value,
                Message = message
            });
            return result;
        }

        if (owners.Count == 0)
        {
            return result;
        }

        var singleOwner = request.OwnerUserId.HasValue;

        // Publish the roster up front so a poller knows the denominator before any seller finishes.
        if (progress is not null)
        {
            foreach (var owner in owners)
            {
                progress.Report(new ListingSyncSellerProgressDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName,
                    Status = ListingSyncSellerStatuses.Pending
                });
            }
        }

        // Fan-out: each seller gets its own DI scope (DbContext + FourSellerApiService + sticky proxy),
        // capped so a large seller list cannot saturate egress and the DB pool.
        using var sellerGate = new SemaphoreSlim(MaxParallelSellers, MaxParallelSellers);
        var tasks = owners
            .Select(async owner =>
            {
                var ownerName = string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName;
                await sellerGate.WaitAsync(cancellationToken);
                try
                {
                    progress?.Report(new ListingSyncSellerProgressDto
                    {
                        OwnerUserId = owner.Id,
                        OwnerName = ownerName,
                        Status = ListingSyncSellerStatuses.Running
                    });

                    var partial = await SyncOwnerInScopeAsync(
                        owner.Id,
                        ownerName,
                        platform,
                        statuses,
                        request.ShopId,
                        request.DateFrom,
                        request.DateTo,
                        maxRecords,
                        syncScope,
                        leaseSource,
                        singleOwner,
                        request.ListingIds,
                        cancellationToken);

                    progress?.Report(new ListingSyncSellerProgressDto
                    {
                        OwnerUserId = owner.Id,
                        OwnerName = ownerName,
                        Status = partial.Failure is not null
                            ? ListingSyncSellerStatuses.Failed
                            : partial.SkippedBusy
                                ? ListingSyncSellerStatuses.SkippedBusy
                                : ListingSyncSellerStatuses.Succeeded,
                        Inserted = partial.Inserted,
                        Updated = partial.Updated,
                        ElapsedMs = partial.ElapsedMs,
                        Message = partial.Failure?.Message
                    });

                    return partial;
                }
                finally
                {
                    sellerGate.Release();
                }
            })
            .ToArray();

        var partials = await Task.WhenAll(tasks);
        var anySkippedBusy = false;
        foreach (var partial in partials)
        {
            if (partial.SkippedBusy)
            {
                anySkippedBusy = true;
            }

            if (partial.Failure is not null)
            {
                result.FailedSellers.Add(partial.Failure);
                continue;
            }

            if (partial.SkippedBusy)
            {
                continue;
            }

            result.SyncedSellers++;
            result.Inserted += partial.Inserted;
            result.Updated += partial.Updated;
            result.ReconciledUpdated += partial.ReconciledUpdated;
            result.ReconciledRemoved += partial.ReconciledRemoved;
            result.StillInFlight += partial.StillInFlight;
        }

        // Single-seller callers (extension scheduler) retry when the lease was busy.
        if (singleOwner && anySkippedBusy && result.SyncedSellers == 0 && result.FailedSellers.Count == 0)
        {
            result.SkippedBusy = true;
            result.FailedSellers.Add(new ListingSyncSellerFailureDto
            {
                OwnerUserId = request.OwnerUserId ?? Guid.Empty,
                Message = "Đồng bộ listing đang chạy cho tài khoản này. Thử lại sau vài giây."
            });
        }

        return result;
    }

    /// <inheritdoc />
    public Task<ListingOwnerSyncResultDto> SyncOwnerAsync(
        Guid ownerUserId,
        string platform,
        IReadOnlyList<string> statuses,
        string? shopId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? maxRecordsPerSeller,
        string? syncScope = null,
        IReadOnlyList<string>? listingIds = null,
        int? reconcileMaxPerSeller = null,
        bool purgeMissingOnReconcile = false,
        int reconcileMaxRounds = 1,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatuses = (statuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (normalizedStatuses.Count == 0)
        {
            normalizedStatuses = ListingSiteStatuses.DefaultSyncStatuses.ToList();
        }

        var normalizedPlatform = string.IsNullOrWhiteSpace(platform)
            ? "tiktok"
            : platform.Trim().ToLowerInvariant();

        var resolvedScope = ListingSyncScopes.Resolve(syncScope, null);
        var rounds = reconcileMaxRounds > 0 ? reconcileMaxRounds : 1;

        return SyncSellerAsync(
            ownerUserId,
            normalizedPlatform,
            normalizedStatuses,
            shopId,
            dateFrom,
            dateTo,
            maxRecordsPerSeller,
            resolvedScope,
            listingIds,
            runReconcile: true,
            reconcileMaxPerSeller,
            purgeMissingOnReconcile,
            rounds,
            cancellationToken);
    }

    private async Task<OwnerSyncPartial> SyncOwnerInScopeAsync(
        Guid ownerId,
        string? ownerName,
        string platform,
        IReadOnlyList<string> statuses,
        string? shopId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? maxRecordsPerSeller,
        string syncScope,
        ListingSyncLeaseSource leaseSource,
        bool singleOwner,
        IReadOnlyList<string>? listingIds,
        CancellationToken cancellationToken)
    {
        if (leaseSource == ListingSyncLeaseSource.Background
            && _sellerLease.HasActiveExtensionLease(ownerId))
        {
            _logger.LogInformation(
                "Background listing sync skipped seller {OwnerId} — extension lease active",
                ownerId);
            return new OwnerSyncPartial(0, 0, null, SkippedBusy: true);
        }

        IDisposable? handle = null;
        if (leaseSource == ListingSyncLeaseSource.Manual)
        {
            for (var attempt = 1; attempt <= ManualLeaseRetryMaxAttempts; attempt++)
            {
                if (_sellerLease.TryBegin(ownerId, leaseSource, out handle) && handle is not null)
                {
                    break;
                }

                handle = null;
                if (attempt == ManualLeaseRetryMaxAttempts)
                {
                    break;
                }

                _logger.LogDebug(
                    "Manual listing sync waiting for seller {OwnerId} lease (attempt {Attempt}/{Max})",
                    ownerId,
                    attempt,
                    ManualLeaseRetryMaxAttempts);

                try
                {
                    await Task.Delay(ManualLeaseRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
        else if (!_sellerLease.TryBegin(ownerId, leaseSource, out handle) || handle is null)
        {
            handle = null;
        }

        if (handle is null)
        {
            _logger.LogInformation(
                "Listing sync skipped seller {OwnerId} — lease busy (source={Source})",
                ownerId,
                leaseSource);

            if (singleOwner)
            {
                return new OwnerSyncPartial(0, 0, null, SkippedBusy: true);
            }

            // Multi-seller background/manual: skip this account, continue others.
            return new OwnerSyncPartial(0, 0, null, SkippedBusy: true);
        }

        var isManualStatus = leaseSource == ListingSyncLeaseSource.Manual
                             && ListingSyncScopes.IsStatus(syncScope);
        var sellerBudget = ResolveSellerBudget(maxRecordsPerSeller, statuses.Count, isManualStatus);
        var sellerClock = Stopwatch.StartNew();
        try
        {
            using (handle)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(sellerBudget);

                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<IListingSyncService>();
                var reconcileMax = isManualStatus
                    ? ReconcileStaleInFlightMaxPerSellerManual
                    : ReconcileStaleInFlightMaxPerSeller;
                var ownerResult = await worker.SyncOwnerAsync(
                    ownerId,
                    platform,
                    statuses,
                    shopId,
                    dateFrom,
                    dateTo,
                    maxRecordsPerSeller,
                    syncScope,
                    listingIds,
                    reconcileMax,
                    purgeMissingOnReconcile: isManualStatus,
                    reconcileMaxRounds: isManualStatus ? ReconcileManualMaxRounds : 1,
                    budget.Token);

                _logger.LogInformation(
                    "Listing sync seller {OwnerId} finished in {ElapsedMs} ms (inserted={Inserted}, updated={Updated}, reconciledUpd={ReconciledUpdated}, removed={ReconciledRemoved}, stillInFlight={StillInFlight})",
                    ownerId,
                    (long)sellerClock.Elapsed.TotalMilliseconds,
                    ownerResult.Inserted,
                    ownerResult.Updated,
                    ownerResult.ReconciledUpdated,
                    ownerResult.ReconciledRemoved,
                    ownerResult.StillInFlight);
                return new OwnerSyncPartial(
                    ownerResult.Inserted,
                    ownerResult.Updated,
                    null,
                    SkippedBusy: false,
                    ElapsedMs: (long)sellerClock.Elapsed.TotalMilliseconds,
                    ReconciledUpdated: ownerResult.ReconciledUpdated,
                    ReconciledRemoved: ownerResult.ReconciledRemoved,
                    StillInFlight: ownerResult.StillInFlight);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only this seller's budget expired — report it and let the other accounts finish.
            _logger.LogWarning(
                "Listing sync seller {OwnerId} exceeded its {BudgetSeconds}s budget after {ElapsedMs} ms; skipping this account",
                ownerId,
                sellerBudget.TotalSeconds,
                (long)sellerClock.Elapsed.TotalMilliseconds);
            return new OwnerSyncPartial(
                0,
                0,
                new ListingSyncSellerFailureDto
                {
                    OwnerUserId = ownerId,
                    OwnerName = ownerName,
                    Message =
                        $"Đồng bộ quá chậm (quá {sellerBudget.TotalSeconds:0}s) — bỏ qua tài khoản này ở lần đồng bộ này."
                },
                SkippedBusy: false,
                ElapsedMs: (long)sellerClock.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Listing sync failed for seller {OwnerId} after {ElapsedMs} ms",
                ownerId,
                (long)sellerClock.Elapsed.TotalMilliseconds);
            return new OwnerSyncPartial(
                0,
                0,
                new ListingSyncSellerFailureDto
                {
                    OwnerUserId = ownerId,
                    OwnerName = ownerName,
                    Message = ex is BusinessException be ? be.Message : Truncate(ex.Message, 300)
                },
                SkippedBusy: false,
                ElapsedMs: (long)sellerClock.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Time a seller may take before it is abandoned. Scales with the rows it is allowed to pull so
    /// a large manual sync is not cut short, while an uncapped full sync still has a ceiling.
    /// </summary>
    private static TimeSpan ResolveSellerBudget(
        int? maxRecordsPerSeller,
        int statusCount,
        bool manualStatusReconcile = false)
    {
        var ceiling = manualStatusReconcile ? SellerBudgetMaxManual : SellerBudgetMax;
        if (maxRecordsPerSeller is not > 0)
        {
            return ceiling;
        }

        var expectedRecords = (long)maxRecordsPerSeller.Value * Math.Max(statusCount, 1);
        var budget = SellerBudgetBase + SellerBudgetPerRecord * expectedRecords;
        // Manual published: keep ~3 minutes for id reconcile after page pull.
        if (manualStatusReconcile)
        {
            budget += TimeSpan.FromMinutes(3);
        }

        return budget > ceiling ? ceiling : budget;
    }

    /// <summary>
    /// Runs one 4Seller call under its own budget. A blown budget surfaces as
    /// <see cref="TimeoutException"/> rather than a cancellation so callers can degrade gracefully
    /// (page-only merge) instead of mistaking it for the caller giving up.
    /// </summary>
    private static async Task<T> WithCallBudgetAsync<T>(
        Func<CancellationToken, Task<T>> call,
        string description,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(FourSellerCallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"4Seller không phản hồi ({description}) trong {FourSellerCallBudget.TotalSeconds:0}s.");
        }
    }

    private sealed record OwnerSyncPartial(
        int Inserted,
        int Updated,
        ListingSyncSellerFailureDto? Failure,
        bool SkippedBusy = false,
        long ElapsedMs = 0,
        int ReconciledUpdated = 0,
        int ReconciledRemoved = 0,
        int StillInFlight = 0);

    /// <inheritdoc />
    public async Task<ListingDetailDto> UpdateAsync(
        Guid id,
        UpdateListingMirrorDto dto,
        CancellationToken cancellationToken = default)
    {
        // SupplierProductInfo feeds SizeChartCaptureUrl / SizeGuideJson in MapDetail — without it
        // the returned detail silently drops them for callers that chain updates (auto publish).
        var listing = await _db.Listings
            .Include(l => l.Variations)
            .Include(l => l.SupplierProductInfo)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        await ApplyLocalEditsAsync(listing, dto, cancellationToken);
        // Skip RawPayload rewrite when only persisting applied pricing formula (tiny jsonb).
        if (!IsAppliedPricingOnlyUpdate(dto))
        {
            PatchRawPayload(listing, dto);
        }
        listing.UpdatedAt = DateTime.UtcNow;
        listing.SyncError = null;
        await SaveUpsertChangesAsync(cancellationToken);

        // Do not call GetByIdAsync here — it refreshes RawPayload from 4Seller and can
        // overwrite local edits before the subsequent save-draft / publish push.
        return await MapDetailWithConditionDefaultsAsync(listing, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingDescriptionPrepResultDto> SaveDescriptionPrepAsync(
        Guid id,
        UpdateListingDescriptionPrepDto dto,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        listing.DescriptionHtml = dto.DescriptionHtml ?? string.Empty;
        listing.DescriptionRewritten = dto.DescriptionRewritten;
        listing.DescriptionImagesInserted = dto.DescriptionImagesInserted;

        // AI Rewrite sends the SEO title with the description; Insert Images omits it.
        var productName = dto.ProductName?.Trim();
        if (!string.IsNullOrEmpty(productName))
        {
            listing.ProductName = productName;
        }

        if (dto.SearchTerms is not null)
        {
            listing.SearchTermsJson = SerializeStringList(dto.SearchTerms);
        }

        if (dto.ProductHighlights is not null)
        {
            listing.ProductHighlightsJson = SerializeStringList(dto.ProductHighlights);
        }

        listing.UpdatedAt = DateTime.UtcNow;
        PatchDescriptionPrepInRawPayload(listing);

        await SaveUpsertChangesAsync(cancellationToken);

        return new ListingDescriptionPrepResultDto
        {
            ListingId = listing.Id,
            DescriptionHtml = listing.DescriptionHtml,
            DescriptionRewritten = listing.DescriptionRewritten,
            DescriptionImagesInserted = listing.DescriptionImagesInserted,
            ProductName = listing.ProductName,
            SearchTerms = DeserializeStringListOrNull(listing.SearchTermsJson),
            ProductHighlights = DeserializeStringListOrNull(listing.ProductHighlightsJson)
        };
    }

    /// <inheritdoc />
    public async Task<ListingSourceUrlResultDto> UpdateSourceUrlAsync(
        Guid id,
        UpdateListingSourceUrlDto dto,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var canonical = SourceUrlKeyHelper.CanonicalizeForCrawl(dto.SourceUrl);
        if (string.IsNullOrWhiteSpace(canonical) ||
            !Uri.TryCreate(canonical, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new BusinessException("URL nguồn không hợp lệ — dán link http(s) sản phẩm NCC.");
        }

        if (canonical.Length > 2000)
        {
            throw new BusinessException("URL nguồn quá dài.");
        }

        listing.SourceUrl = canonical;
        listing.UpdatedAt = DateTime.UtcNow;
        PatchSourceUrlInRawPayload(listing);

        await _supplierProductInfos.ApplyToListingAsync(listing, cancellationToken);
        await SaveUpsertChangesAsync(cancellationToken);

        var result = new ListingSourceUrlResultDto
        {
            ListingId = listing.Id,
            SourceUrl = listing.SourceUrl,
            ReviewCount = listing.ReviewCount,
            AverageRating = listing.AverageRating
        };

        if (listing.SupplierProductInfoId is not Guid productId)
        {
            return result;
        }

        var overlay = await _db.SupplierProductInfos.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.Stock,
                p.IsLocalProduct,
                p.IsSupportQuickShip,
                StoreLogo = p.Seller != null ? p.Seller.StoreLogo : null,
                StoreTitle = p.Seller != null ? p.Seller.StoreTitle : null,
                StoreUrl = p.Seller != null ? p.Seller.StoreUrl : null,
                ShopRank = p.Seller != null ? p.Seller.ShopRank : null,
                StoreAttention = p.Seller != null ? p.Seller.StoreAttention : null,
                SoldByTips = p.Seller != null ? p.Seller.SoldByTips : null,
                ShippingDays = p.Seller != null ? p.Seller.ShippingDays : null,
                ShippingDayPercent = p.Seller != null ? p.Seller.ShippingDayPercent : null,
                SellerPlatform = p.Seller != null ? p.Seller.Platform : null,
                StoreCode = p.Seller != null ? p.Seller.StoreCode : null,
                HasSizeChart = p.SizeChartImageUrl != null && p.SizeChartImageUrl != ""
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (overlay is null)
        {
            return result;
        }

        result.SourceStock = overlay.Stock;
        result.IsLocalProduct = overlay.IsLocalProduct;
        result.IsSupportQuickShip = overlay.IsSupportQuickShip;
        result.StoreLogo = overlay.StoreLogo;
        result.StoreTitle = overlay.StoreTitle;
        result.StoreUrl = overlay.StoreUrl;
        result.ShopRank = overlay.ShopRank;
        result.StoreAttention = overlay.StoreAttention;
        result.ShippingDays = overlay.ShippingDays;
        result.ShippingDayPercent = overlay.ShippingDayPercent;
        result.SellerPlatform = overlay.SellerPlatform;
        result.StoreCode = overlay.StoreCode;
        result.HasSizeChart = overlay.HasSizeChart;
        if (string.IsNullOrWhiteSpace(result.StoreTitle))
        {
            result.StoreTitle = SupplierPdpMetrics.ParseStoreTitleFromSoldByTips(overlay.SoldByTips);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ListingPushResultDto> SaveDraftAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default)
    {
        return await PushAsync(id, ListingPushMode.Draft, dto, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingPushResultDto> PublishAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        string? publishSource = null,
        CancellationToken cancellationToken = default)
    {
        return await PushAsync(id, ListingPushMode.Publish, dto, publishSource, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingPushResultDto> PushUpdateAsync(
        Guid id,
        UpdateListingMirrorDto? dto = null,
        CancellationToken cancellationToken = default)
    {
        return await PushAsync(id, ListingPushMode.Update, dto, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingPushResultDto> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (listing.FourSellerListingId <= 0)
        {
            throw new BusinessException("Listing chưa có 4Seller id — không thể deactivate.");
        }

        FourSellerListingPushResultDto remote;
        try
        {
            remote = await _fourSeller.DeactivateListingsAsync(
                listing.OwnerId,
                listing.Platform,
                new[] { listing.FourSellerListingId },
                cancellationToken);
        }
        catch (BusinessException ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(
                    ex.Errors.Count > 0
                        ? $"{ex.Message} {string.Join(" | ", ex.Errors)}"
                        : ex.Message,
                    2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(ex.Message, 2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }

        var wasActive = string.Equals(
            listing.SiteStatus,
            ListingSiteStatuses.Active,
            StringComparison.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        listing.SiteStatus = ListingSiteStatuses.Inactive;
        listing.PublishStatus = "Inactive";
        listing.LastPushAt = now;
        listing.LastSyncedAt = now;
        listing.SyncError = null;
        listing.UpdatedAt = now;

        if (wasActive)
        {
            await TryDecrementShopPublishPublishedAsync(listing.ShopId, cancellationToken);
        }

        await SaveUpsertChangesAsync(cancellationToken);

        _postHtmlDraftSyncScheduler.Schedule(
            listing.OwnerId,
            [ListingSiteStatuses.Active, ListingSiteStatuses.Inactive],
            TimeSpan.Zero,
            ListingSyncScopes.Status);

        return new ListingPushResultDto
        {
            Success = true,
            Message = remote.Message ?? "Deactivate thành công.",
            ListingId = listing.Id,
            PageType = listing.PageType,
            SiteStatus = listing.SiteStatus,
            TikTokProductId = listing.TikTokProductId
        };
    }

    /// <summary>
    /// Giảm <see cref="Shop.PublishLimitPublished"/> khi nhả slot local (Hủy list / Xóa Active).
    /// TTS sync sau vẫn ghi đè số thật; đây để board/summary phản ánh ngay.
    /// </summary>
    private async Task TryDecrementShopPublishPublishedAsync(
        Guid? shopId,
        CancellationToken cancellationToken)
    {
        if (shopId is null || shopId == Guid.Empty)
        {
            return;
        }

        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId.Value, cancellationToken);
        if (shop?.PublishLimitPublished is not > 0)
        {
            return;
        }

        shop.PublishLimitPublished -= 1;
        shop.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Giảm publish published theo từng shop khi xóa hàng loạt listing Active.</summary>
    private async Task TryDecrementShopPublishPublishedForListingsAsync(
        IReadOnlyList<Listing> listings,
        CancellationToken cancellationToken)
    {
        var decrements = listings
            .Where(l =>
                l.ShopId is not null
                && string.Equals(l.SiteStatus, ListingSiteStatuses.Active, StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.ShopId!.Value)
            .Select(g => new { ShopId = g.Key, Count = g.Count() })
            .ToList();

        if (decrements.Count == 0)
        {
            return;
        }

        var shopIds = decrements.Select(d => d.ShopId).ToList();
        var shops = await _db.Shops
            .Where(s => shopIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        var byId = shops.ToDictionary(s => s.Id);
        var now = DateTime.UtcNow;

        foreach (var row in decrements)
        {
            if (!byId.TryGetValue(row.ShopId, out var shop) || shop.PublishLimitPublished is not > 0)
            {
                continue;
            }

            shop.PublishLimitPublished = Math.Max(0, shop.PublishLimitPublished.Value - row.Count);
            shop.UpdatedAt = now;
        }
    }

    /// <inheritdoc />
    public async Task<ListingPushResultDto> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (listing.FourSellerListingId <= 0)
        {
            throw new BusinessException("Listing chưa có 4Seller id — không thể activate.");
        }

        FourSellerListingPushResultDto remote;
        try
        {
            remote = await _fourSeller.ActivateListingsAsync(
                listing.OwnerId,
                listing.Platform,
                new[] { listing.FourSellerListingId },
                cancellationToken);
        }
        catch (BusinessException ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(
                    ex.Errors.Count > 0
                        ? $"{ex.Message} {string.Join(" | ", ex.Errors)}"
                        : ex.Message,
                    2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(ex.Message, 2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }

        var now = DateTime.UtcNow;
        listing.SiteStatus = ListingSiteStatuses.Active;
        listing.PublishStatus = "Active";
        listing.LastPushAt = now;
        listing.LastSyncedAt = now;
        listing.SyncError = null;
        listing.UpdatedAt = now;

        await SaveUpsertChangesAsync(cancellationToken);

        _postHtmlDraftSyncScheduler.Schedule(
            listing.OwnerId,
            [ListingSiteStatuses.Active, ListingSiteStatuses.Inactive],
            TimeSpan.Zero,
            ListingSyncScopes.Status);

        return new ListingPushResultDto
        {
            Success = true,
            Message = remote.Message ?? "Activate thành công.",
            ListingId = listing.Id,
            PageType = listing.PageType,
            SiteStatus = listing.SiteStatus,
            TikTokProductId = listing.TikTokProductId
        };
    }

    /// <inheritdoc />
    public async Task<ListingDetailDto> DuplicateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var source = await _db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (source.FourSellerListingId <= 0)
        {
            throw new BusinessException("Listing chưa có 4Seller id — không thể duplicate.");
        }

        var platform = string.IsNullOrWhiteSpace(source.Platform) ? "tiktok" : source.Platform;

        FourSellerListingPushResultDto remote;
        try
        {
            remote = await _fourSeller.CopyListingAsync(
                source.OwnerId,
                platform,
                source.FourSellerListingId,
                cancellationToken);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException($"Duplicate listing thất bại: {Truncate(ex.Message, 400)}");
        }

        var newFourSellerId = remote.ListingId ?? 0;
        if (newFourSellerId <= 0)
        {
            throw new BusinessException("4Seller copy không trả về listing id mới.");
        }

        var details = await _fourSeller.GetListingBatchDetailAsync(
            source.OwnerId,
            platform,
            new[] { newFourSellerId },
            cancellationToken,
            allowPerIdFallback: true);

        var record = details.FirstOrDefault(d => d.Id == newFourSellerId)
                     ?? details.FirstOrDefault();
        if (record is null || record.Id <= 0)
        {
            throw new BusinessException(
                $"Đã copy trên 4Seller (id={newFourSellerId}) nhưng không lấy được detail để lưu local.");
        }

        // Copy always creates a draft on 4Seller.
        if (record.Id != newFourSellerId)
        {
            record.Id = newFourSellerId;
        }

        var shopByFourSellerId = new Dictionary<long, Guid>();
        if (source.ShopId is Guid localShopId && source.FourSellerShopId is > 0)
        {
            shopByFourSellerId[source.FourSellerShopId.Value] = localShopId;
        }
        else if (source.ShopId is Guid fallbackShop && record.ShopId is > 0)
        {
            shopByFourSellerId[record.ShopId.Value] = fallbackShop;
        }

        await UpsertRecordsAsync(
            source.OwnerId,
            platform,
            ListingSiteStatuses.Draft,
            new[] { record },
            shopByFourSellerId,
            ListingSyncScopes.Full,
            cancellationToken);

        var created = await _db.Listings
            .Include(l => l.Owner)
            .Include(l => l.Shop)
            .Include(l => l.Variations)
            .FirstOrDefaultAsync(l => l.FourSellerListingId == newFourSellerId, cancellationToken)
            ?? throw new BusinessException(
                $"Copy thành công trên 4Seller (id={newFourSellerId}) nhưng không tìm thấy listing local.");

        if (created.ShopId is null && source.ShopId is Guid inheritShop)
        {
            created.ShopId = inheritShop;
            created.FourSellerShopId = source.FourSellerShopId ?? created.FourSellerShopId;
            await SaveUpsertChangesAsync(cancellationToken);
        }

        // Copy local-only pricing formula (not present on 4Seller copy payload).
        if (!string.IsNullOrWhiteSpace(source.AppliedPricingJson) &&
            string.IsNullOrWhiteSpace(created.AppliedPricingJson))
        {
            created.AppliedPricingJson = source.AppliedPricingJson;
            created.UpdatedAt = DateTime.UtcNow;
            await SaveUpsertChangesAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(created.SearchTermsJson) &&
            !string.IsNullOrWhiteSpace(source.SearchTermsJson))
        {
            created.SearchTermsJson = source.SearchTermsJson;
            created.UpdatedAt = DateTime.UtcNow;
            await SaveUpsertChangesAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(created.ProductHighlightsJson) &&
            !string.IsNullOrWhiteSpace(source.ProductHighlightsJson))
        {
            created.ProductHighlightsJson = source.ProductHighlightsJson;
            created.UpdatedAt = DateTime.UtcNow;
            await SaveUpsertChangesAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(source.AddedFrom) &&
            !string.Equals(created.AddedFrom, source.AddedFrom, StringComparison.Ordinal))
        {
            created.AddedFrom = source.AddedFrom;
            created.UpdatedAt = DateTime.UtcNow;
            await SaveUpsertChangesAsync(cancellationToken);
        }

        _postHtmlDraftSyncScheduler.Schedule(source.OwnerId, debounce: TimeSpan.Zero, syncScope: ListingSyncScopes.Full);

        return await MapDetailWithConditionDefaultsAsync(created, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingDetailDto> SyncOneAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .Include(l => l.Owner)
            .Include(l => l.Shop)
            .Include(l => l.Variations)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var productId = ResolveFourSellerProductId(listing);
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new BusinessException(
                "Listing chưa có productId từ 4Seller — đồng bộ listing (page/detail) rồi thử lại.");
        }

        var shopId = listing.FourSellerShopId ?? 0;
        if (shopId <= 0 &&
            !string.IsNullOrWhiteSpace(listing.Shop?.FourSellerShopId) &&
            long.TryParse(listing.Shop.FourSellerShopId, out var parsedShopId))
        {
            shopId = parsedShopId;
            listing.FourSellerShopId = parsedShopId;
        }

        if (shopId <= 0)
        {
            throw new BusinessException("Listing chưa có shopId 4Seller — không thể đồng bộ.");
        }

        // Keep local column in sync with the productId we will send.
        if (!string.Equals(listing.TikTokProductId, productId, StringComparison.Ordinal))
        {
            listing.TikTokProductId = productId;
        }

        var platform = string.IsNullOrWhiteSpace(listing.Platform) ? "tiktok" : listing.Platform;

        try
        {
            await _fourSeller.SyncOneLocalListingAsync(
                listing.OwnerId,
                platform,
                productId,
                shopId,
                cancellationToken);
        }
        catch (BusinessException ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(
                    ex.Errors.Count > 0
                        ? $"{ex.Message} {string.Join(" | ", ex.Errors)}"
                        : ex.Message,
                    2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(ex.Message, 2000),
                discardFormEdits: false,
                cancellationToken);
            throw;
        }

        var now = DateTime.UtcNow;
        listing.LastSyncedAt = now;
        listing.UpdatedAt = now;
        await SaveUpsertChangesAsync(cancellationToken);

        // Pull fresh detail after 4Seller has synced from the marketplace.
        await TryRefreshFullDetailAsync(listing, cancellationToken);

        return await MapDetailWithConditionDefaultsAsync(listing, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerMoveSourceListingDto> GetMoveSourceAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Id, l.OwnerId, l.FourSellerListingId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (listing.FourSellerListingId <= 0)
        {
            throw new BusinessException("Listing chưa có id 4Seller — đồng bộ rồi thử lại.");
        }

        var rows = await _fourSeller.BatchGetMoveSourceListingAsync(
            listing.OwnerId,
            [listing.FourSellerListingId],
            cancellationToken);

        var match = rows.FirstOrDefault(r => r.TargetListingId == listing.FourSellerListingId)
            ?? rows.FirstOrDefault();

        if (match is null)
        {
            throw new BusinessException("4Seller không trả về move-source cho listing này.");
        }

        return match;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerRecentUseCategoryDto>> GetRecentCategoriesAsync(
        Guid id,
        long? shopId = null,
        string? site = null,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new
            {
                l.Id,
                l.OwnerId,
                l.FourSellerShopId,
                l.Site,
                ShopFourSellerShopId = l.Shop != null ? l.Shop.FourSellerShopId : null
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var resolvedShopId = shopId ?? 0;
        if (resolvedShopId <= 0)
        {
            resolvedShopId = listing.FourSellerShopId ?? 0;
        }

        if (resolvedShopId <= 0 &&
            !string.IsNullOrWhiteSpace(listing.ShopFourSellerShopId) &&
            long.TryParse(listing.ShopFourSellerShopId, out var parsedShopId))
        {
            resolvedShopId = parsedShopId;
        }

        if (resolvedShopId <= 0)
        {
            throw new BusinessException(
                "Chưa có shopId 4Seller — mở lại bước chọn category hoặc đồng bộ listing.");
        }

        var siteCode = string.IsNullOrWhiteSpace(site)
            ? (string.IsNullOrWhiteSpace(listing.Site) ? "US" : listing.Site.Trim())
            : site.Trim();

        return await _fourSeller.GetRecentUseCategoriesAsync(
            listing.OwnerId,
            resolvedShopId,
            siteCode,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task BindCategoryAsync(
        Guid id,
        BindListingCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.RecentCategoryId <= 0)
        {
            throw new BusinessException("recentCategoryId không hợp lệ.");
        }

        var listing = await _db.Listings
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (listing.FourSellerListingId <= 0)
        {
            throw new BusinessException("Listing chưa có id 4Seller — đồng bộ rồi thử lại.");
        }

        await _fourSeller.BatchBindCategoryAsync(
            listing.OwnerId,
            [
                new FourSellerBindCategoryItemDto
                {
                    ListingId = listing.FourSellerListingId,
                    CategoryId = request.RecentCategoryId
                }
            ],
            cancellationToken);

        var tikTokCategoryId = request.CategoryId?.Trim();
        var categoryName = request.CategoryName?.Trim();
        if (!string.IsNullOrWhiteSpace(tikTokCategoryId))
        {
            listing.CategoryId = tikTokCategoryId;
        }

        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            listing.CategoryName = categoryName;
        }

        listing.UpdatedAt = DateTime.UtcNow;
        listing.SyncError = null;
        await SaveUpsertChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves 4Seller <c>productId</c> for sync-one-local-listing:
    /// local <see cref="Listing.TikTokProductId"/>, else RawPayload <c>productId</c>.
    /// </summary>
    private static string? ResolveFourSellerProductId(Listing listing)
    {
        var fromColumn = listing.TikTokProductId?.Trim();
        if (!string.IsNullOrWhiteSpace(fromColumn))
        {
            return fromColumn;
        }

        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(listing.RawPayload);
            var root = doc.RootElement;
            foreach (var key in new[] { "productId", "product_id", "tiktokProductId", "tikTokProductId" })
            {
                if (!root.TryGetProperty(key, out var el))
                {
                    continue;
                }

                var value = el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString()?.Trim(),
                    JsonValueKind.Number => el.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            // ignore — treat as missing productId
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ListingShopMetadataDto> GetShopMetadataAsync(
        Guid listingId,
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveListingShopForMetadataAsync(listingId, shopId, cancellationToken);

        // Cookie/is-login once, then brands + warehouses in parallel (HTTP only).
        var (brands, warehouses) = await _fourSeller.GetBrandAndWarehouseListsByShopIdAsync(
            resolved.OwnerId,
            resolved.FourSellerShopId,
            fbt: 0,
            allocationMode: string.Empty,
            cancellationToken);

        var defaultWarehouse = warehouses.FirstOrDefault(w => w.IsDefault)
            ?? warehouses.FirstOrDefault(w => w.WarehouseEffectStatus is null or 1)
            ?? warehouses.FirstOrDefault();

        return new ListingShopMetadataDto
        {
            ShopId = resolved.ShopId,
            FourSellerShopId = resolved.FourSellerShopId.ToString(),
            Brands = brands.ToList(),
            Warehouses = warehouses.ToList(),
            DefaultWarehouseId = defaultWarehouse?.WarehouseId
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerBrandDto>> SuggestBrandsAsync(
        Guid listingId,
        Guid shopId,
        string brandName,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveListingShopForMetadataAsync(listingId, shopId, cancellationToken);
        return await _fourSeller.SuggestBrandsByShopIdAsync(
            resolved.OwnerId,
            resolved.FourSellerShopId,
            brandName,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerCosUploadResultDto> UploadCosMediaAsync(
        Guid listingId,
        Stream content,
        string fileName,
        string contentType,
        string? referenceType = null,
        CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new { l.OwnerId, l.Platform })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var platform = string.IsNullOrWhiteSpace(listing.Platform)
            ? "tiktok"
            : listing.Platform.Trim().ToLowerInvariant();
        var refType = string.IsNullOrWhiteSpace(referenceType) ? platform : referenceType.Trim();

        return await _fourSeller.UploadCosAsync(
            listing.OwnerId,
            content,
            fileName,
            contentType,
            refType,
            cancellationToken);
    }

    private async Task<(Guid ShopId, Guid OwnerId, long FourSellerShopId)> ResolveListingShopForMetadataAsync(
        Guid listingId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new { l.Id, l.OwnerId, l.Platform })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new
            {
                s.Id,
                s.OwnerId,
                s.FourSellerShopId,
                PlatformCode = s.Platform != null ? s.Platform.Code : null
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException("Không tìm thấy shop.");

        if (shop.OwnerId != listing.OwnerId)
        {
            throw new BusinessException("Shop không thuộc seller của listing.");
        }

        if (!string.IsNullOrWhiteSpace(listing.Platform) &&
            !string.IsNullOrWhiteSpace(shop.PlatformCode) &&
            !string.Equals(shop.PlatformCode, listing.Platform, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Shop không cùng platform với listing.");
        }

        if (string.IsNullOrWhiteSpace(shop.FourSellerShopId) ||
            !long.TryParse(shop.FourSellerShopId, out var fourSellerShopId) ||
            fourSellerShopId <= 0)
        {
            throw new BusinessException(
                "Shop chưa có 4Seller ID — kết nối shop với 4Seller trước khi tải Brand / Warehouse.");
        }

        return (shop.Id, listing.OwnerId, fourSellerShopId);
    }

    /// <summary>
    /// Flow: refresh RawPayload from 4Seller → apply form edits onto that JSON → push 4Seller →
    /// persist local. Form edits are discarded (only SyncError kept) when push/validation fails.
    /// </summary>
    /// <summary>4Seller push target for <see cref="PushAsync"/>.</summary>
    private enum ListingPushMode
    {
        Draft,
        Publish,
        Update
    }

    private sealed class ListingPushPayloadPrep
    {
        public required JsonObject PayloadObject { get; init; }
        public required List<string> ValidationErrors { get; init; }
    }

    /// <summary>
    /// Shared save-draft / publish / update payload builder. Optionally refreshes RawPayload from 4Seller
    /// before patching form edits (skipped when the caller sends a full mirror DTO from the edit form).
    /// </summary>
    private async Task<ListingPushPayloadPrep> PrepareListingPushPayloadAsync(
        Listing listing,
        UpdateListingMirrorDto? dto,
        ListingPushMode mode,
        bool refreshFromFourSeller,
        CancellationToken cancellationToken)
    {
        if (refreshFromFourSeller)
        {
            await TryRefreshFullDetailAsync(listing, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            throw new BusinessException(
                "Listing chưa có raw detail từ 4Seller. Đồng bộ lại (có batch-detail) trước khi Save Draft / Publish.");
        }

        if (dto is not null)
        {
            await ApplyLocalEditsAsync(listing, dto, cancellationToken);
        }

        if (mode == ListingPushMode.Publish && !listing.DescriptionRewritten)
        {
            throw new BusinessException(
                "Chưa Rewrite mô tả trước khi Publish.",
                new List<string> { "1. Rewrite — AI viết lại mô tả" });
        }

        var valueShapes = CaptureSalePropValueShapes(listing.RawPayload);
        PatchRawPayload(listing, dto, valueShapes);

        JsonObject payloadObj;
        try
        {
            payloadObj = JsonNode.Parse(listing.RawPayload) as JsonObject
                         ?? throw new BusinessException("RawPayload không phải JSON object.");
        }
        catch (JsonException ex)
        {
            throw new BusinessException($"RawPayload không hợp lệ JSON: {ex.Message}");
        }

        var unresolvedAxes = await CompleteSalePropMapsAsync(
            listing, payloadObj, valueShapes, cancellationToken);

        // Runs after the option maps are final (sale-prop ids are read from them) and before the
        // Condition defaults, which come from the live schema and are already canonical.
        var specificsAudit = CanonicalizeProductAttributesAgainstSchema(
            payloadObj,
            await _attributeRejections.GetDenylistAsync(listing.CategoryId, cancellationToken),
            await TryLoadCategorySchemaAsync(listing, cancellationToken));
        if (specificsAudit.HasChanges)
        {
            _logger.LogInformation(
                "Listing {ListingId} Specifics canonicalize: bỏ {DroppedCount} dòng [{Dropped}], " +
                "điền valueId {RepairedCount} dòng [{Repaired}]",
                listing.Id,
                specificsAudit.Dropped.Count,
                string.Join(" | ", specificsAudit.Dropped),
                specificsAudit.Repaired.Count,
                string.Join(" | ", specificsAudit.Repaired));
        }

        await EnsureConditionSpecificsInPayloadAsync(listing, payloadObj, cancellationToken);

        var validationErrors = ValidateListingPayloadForPush(payloadObj, listing);
        foreach (var axis in unresolvedAxes)
        {
            validationErrors.Add(
                $"Không xác định được TikTok attribute id cho thuộc tính `{axis}` — chọn lại category " +
                "hoặc bỏ thuộc tính này trên form.");
        }

        SanitizePayloadForFourSellerPush(payloadObj, listing);

        listing.RawPayload = payloadObj.ToJsonString(PatchJsonOptions);

        return new ListingPushPayloadPrep
        {
            PayloadObject = payloadObj,
            ValidationErrors = validationErrors
        };
    }

    private static bool ShouldRefreshRawPayloadBeforePush(UpdateListingMirrorDto? dto) =>
        dto is null || IsAppliedPricingOnlyUpdate(dto);

    /// <inheritdoc />
    public async Task<ListingPushPreviewDto> BuildPushPreviewAsync(
        Guid id,
        bool includePayload = false,
        CancellationToken cancellationToken = default)
    {
        // No-tracking: PatchRawPayload and the combo re-match mutate the entity graph in place,
        // and a preview must never write any of that back.
        var listing = await _db.Listings
            .AsNoTracking()
            .Include(l => l.Variations)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            throw new BusinessException(
                "Listing chưa có raw detail từ 4Seller. Đồng bộ lại (có batch-detail) trước khi xem preview.");
        }

        var prep = await PrepareListingPushPayloadAsync(
            listing,
            dto: null,
            ListingPushMode.Draft,
            refreshFromFourSeller: false,
            cancellationToken);

        var payloadObj = prep.PayloadObject;
        var errors = prep.ValidationErrors;

        var axisAttrs = BuildVariationAxisAttrs(listing.Variations);
        var preview = new ListingPushPreviewDto
        {
            ListingId = listing.Id,
            FourSellerListingId = listing.FourSellerListingId,
            AxisSlots = ListingSalePropSync.ResolveAxisSlots(payloadObj, axisAttrs),
            ValidationErrors = errors,
            SkuImgs = GetString(payloadObj, "skuImgs"),
            Payload = includePayload ? payloadObj.ToJsonString(PatchJsonOptions) : null
        };

        for (var slot = 1; slot <= ListingSalePropSync.SlotCount; slot++)
        {
            var optionKey = ListingSalePropSync.OptionKeyForSlot(slot);
            preview.SaleProps[optionKey] = GetString(payloadObj, optionKey);
            preview.SaleProps[$"{optionKey}Value"] = GetString(payloadObj, $"{optionKey}Value");
        }

        if (payloadObj[ResolveVariationArrayKey(payloadObj)] is JsonArray variationArr)
        {
            for (var i = 0; i < variationArr.Count; i++)
            {
                if (variationArr[i] is not JsonObject vObj)
                {
                    continue;
                }

                preview.Variations.Add(new ListingPushPreviewVariationDto
                {
                    Index = i,
                    SkuId = TryGetLong(vObj, "id", out var skuId) && skuId > 0 ? skuId : null,
                    SellerSku = GetString(vObj, "sellerSku") ?? GetString(vObj, "seller_sku"),
                    Title = GetString(vObj, "title") ?? GetString(vObj, "name"),
                    Option1 = GetString(vObj, "option1"),
                    Option2 = GetString(vObj, "option2"),
                    Option3 = GetString(vObj, "option3"),
                    Attrs = vObj["attrs"] is JsonArray attrsArr
                        ? attrsArr.Select(a => a?.ToString() ?? string.Empty).ToList()
                        : [],
                    OriginalPrice = GetDecimal(vObj, "originalPrice") ?? GetDecimal(vObj, "salePrice"),
                    AvailableStock = GetDecimal(vObj, "availableStock") ?? GetDecimal(vObj, "quantity"),
                    MainImage = GetString(vObj, "mainImage") ?? GetString(vObj, "skuImg")
                });
            }
        }

        preview.LocalVariations = InPushOrder(listing.Variations)
            .Select(v => new ListingPushPreviewLocalVariationDto
            {
                Id = v.Id,
                FourSellerSkuId = v.FourSellerSkuId,
                SellerSku = v.SellerSku,
                Title = v.Title,
                VariantAttributes = v.VariantAttributes,
                SalePrice = v.SalePrice,
                Quantity = v.Quantity
            })
            .ToList();

        return preview;
    }

    private async Task<ListingPushResultDto> PushAsync(
        Guid id,
        ListingPushMode mode,
        UpdateListingMirrorDto? dto,
        string? publishSource,
        CancellationToken cancellationToken)
    {
        var listing = await _db.Listings
            .Include(l => l.Variations)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var prep = await PrepareListingPushPayloadAsync(
            listing,
            dto,
            mode,
            ShouldRefreshRawPayloadBeforePush(dto),
            cancellationToken);

        if (prep.ValidationErrors.Count > 0)
        {
            var summary = "Dữ liệu listing không đủ / không hợp lệ để gửi 4Seller.";
            await PersistPushFailureAsync(
                listing,
                Truncate($"{summary} {string.Join(" | ", prep.ValidationErrors)}", 2000),
                discardFormEdits: dto is not null,
                cancellationToken);
            throw new BusinessException(summary, prep.ValidationErrors);
        }

        object payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(listing.RawPayload!);
        }
        catch (JsonException ex)
        {
            throw new BusinessException($"RawPayload không hợp lệ JSON: {ex.Message}");
        }

        FourSellerListingPushResultDto remote;
        try
        {
            remote = mode switch
            {
                ListingPushMode.Draft => await _fourSeller.SaveListingDraftAsync(
                    listing.OwnerId, listing.Platform, payload, cancellationToken),
                ListingPushMode.Update => await _fourSeller.UpdateListingAsync(
                    listing.OwnerId,
                    listing.Platform,
                    payload,
                    listing.SiteStatus,
                    cancellationToken),
                _ => await _fourSeller.PublishListingAsync(
                    listing.OwnerId, listing.Platform, payload, cancellationToken)
            };
        }
        catch (BusinessException ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(
                    ex.Errors.Count > 0
                        ? $"{ex.Message} {string.Join(" | ", ex.Errors)}"
                        : ex.Message,
                    2000),
                discardFormEdits: dto is not null,
                cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await PersistPushFailureAsync(
                listing,
                Truncate(ex.Message, 2000),
                discardFormEdits: dto is not null,
                cancellationToken);
            throw;
        }

        var now = DateTime.UtcNow;
        listing.LastPushAt = now;
        listing.LastSyncedAt = now;
        listing.SyncError = null;
        listing.UpdatedAt = now;

        // Update = content only — do not flip Active/Inactive (use Activate/Deactivate).
        switch (mode)
        {
            case ListingPushMode.Draft:
                listing.PageType = ListingPageTypes.Draft;
                listing.SiteStatus = ListingSiteStatuses.Draft;
                listing.PublishStatus = "Draft";
                break;
            case ListingPushMode.Publish:
                // 4Seller accept ≠ live ngay — optimistic Published/Pending/Publishing so the listing
                // leaves Draft immediately (Spin on Published → Pending). Post-push sync
                // confirms active/reject; ApplyRecord guards against draft-page regression.
                listing.PageType = ListingPageTypes.Published;
                listing.SiteStatus = ListingSiteStatuses.Pending;
                listing.PublishStatus = PublishStatuses.Publishing;
                listing.PublishSource = string.IsNullOrWhiteSpace(publishSource)
                    ? ListingPublishSources.Manual
                    : publishSource.Trim();
                listing.AutoPublishSkipped = false;
                listing.AutoPublishSkippedAt = null;
                listing.AutoPublishSkipReason = null;
                if (!string.IsNullOrWhiteSpace(remote.TikTokProductId))
                {
                    listing.TikTokProductId = remote.TikTokProductId;
                }

                break;
            case ListingPushMode.Update:
                listing.PageType = ListingPageTypes.Published;
                listing.PublishStatus = PublishStatuses.Publishing;
                if (!string.IsNullOrWhiteSpace(remote.TikTokProductId))
                {
                    listing.TikTokProductId = remote.TikTokProductId;
                }

                break;
        }

        // Local persist only after 4Seller accepts the new RawPayload version.
        await SaveUpsertChangesAsync(cancellationToken);

        // Best-effort: pull newest listings for this seller off-request (does not block response / FE close).
        switch (mode)
        {
            case ListingPushMode.Draft:
                _postHtmlDraftSyncScheduler.Schedule(
                    listing.OwnerId,
                    debounce: TimeSpan.Zero);
                break;
            case ListingPushMode.Publish:
                // Auto publish skips post-sync so extension lease does not block the next draft
                // on the same seller; background sync reconciles status later.
                if (ShouldSchedulePostPublishSync(publishSource, isPublishMode: true))
                {
                    // Draft reconciles publish that 4Seller accepted but never moved to Reviewing
                    // (productStatus still Draft / publishStatus unpublishable). Pending + reject catch
                    // the happy path once the row leaves the draft bucket.
                    _postHtmlDraftSyncScheduler.Schedule(
                        listing.OwnerId,
                        [ListingSiteStatuses.Pending, ListingSiteStatuses.Active, ListingSiteStatuses.Reject],
                        TimeSpan.Zero,
                        ListingSyncScopes.Status,
                        listing.FourSellerListingId > 0
                            ? [listing.FourSellerListingId.ToString()]
                            : null);
                }

                break;
            case ListingPushMode.Update:
                _postHtmlDraftSyncScheduler.Schedule(
                    listing.OwnerId,
                    [ListingSiteStatuses.Active, ListingSiteStatuses.Inactive],
                    TimeSpan.Zero,
                    ListingSyncScopes.Status);
                break;
        }

        return new ListingPushResultDto
        {
            Success = true,
            Message = remote.Message ?? mode switch
            {
                ListingPushMode.Draft => "Save Draft thành công.",
                ListingPushMode.Update => "Update thành công.",
                _ => "Đã gửi publish, đang chờ duyệt."
            },
            ListingId = listing.Id,
            PageType = listing.PageType,
            SiteStatus = listing.SiteStatus,
            TikTokProductId = listing.TikTokProductId
        };
    }

    /// <summary>
    /// Post-publish sync is skipped for auto publish so extension lease does not block the next draft.
    /// </summary>
    internal static bool ShouldSchedulePostPublishSync(string? publishSource, bool isPublishMode) =>
        !isPublishMode
        || !string.Equals(publishSource, ListingPublishSources.Auto, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies editable form fields onto the listing entity (columns + variations). Does not save.
    /// </summary>
    private async Task ApplyLocalEditsAsync(
        Listing listing,
        UpdateListingMirrorDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.ShopId.HasValue)
        {
            var shop = await _db.Shops
                .AsNoTracking()
                .Where(s => s.Id == dto.ShopId.Value)
                .Select(s => new { s.Id, s.FourSellerShopId, s.Region, s.OwnerId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new BusinessException("Không tìm thấy shop.");

            if (shop.OwnerId != listing.OwnerId)
            {
                throw new BusinessException("Shop không thuộc seller của listing.");
            }

            if (string.IsNullOrWhiteSpace(shop.FourSellerShopId) ||
                !long.TryParse(shop.FourSellerShopId, out var fourSellerShopId) ||
                fourSellerShopId <= 0)
            {
                throw new BusinessException(
                    "Shop chưa có 4Seller ID — kết nối shop với 4Seller trước khi Save Draft / Publish.");
            }

            listing.ShopId = shop.Id;
            listing.FourSellerShopId = fourSellerShopId;
            if (!string.IsNullOrWhiteSpace(shop.Region))
            {
                listing.Site = shop.Region.Trim();
            }
        }

        if (dto.ProductName is not null)
        {
            listing.ProductName = dto.ProductName.Trim();
        }

        if (dto.DescriptionHtml is not null)
        {
            listing.DescriptionHtml = dto.DescriptionHtml;
        }

        if (dto.SearchTerms is not null)
        {
            listing.SearchTermsJson = SerializeStringList(dto.SearchTerms);
        }

        if (dto.ProductHighlights is not null)
        {
            listing.ProductHighlightsJson = SerializeStringList(dto.ProductHighlights);
        }

        if (dto.CategoryId is not null)
        {
            var categoryId = dto.CategoryId.Trim();
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                listing.CategoryId = categoryId;
            }
        }

        if (dto.CategoryName is not null)
        {
            var categoryName = dto.CategoryName.Trim();
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                listing.CategoryName = categoryName;
            }
        }

        if (dto.WarehouseId is not null)
        {
            var warehouseId = dto.WarehouseId.Trim();
            // Do not clear an existing warehouse with empty form hidden field.
            if (!string.IsNullOrWhiteSpace(warehouseId))
            {
                listing.WarehouseId = warehouseId;
            }
        }

        var source = dto.ListingSourceUrl ?? dto.SourceUrl;
        if (!string.IsNullOrWhiteSpace(source))
        {
            listing.SourceUrl = source.Trim();
        }

        if (dto.PackageWeight.HasValue)
        {
            listing.PackageWeight = dto.PackageWeight;
        }

        if (dto.PackageLength.HasValue)
        {
            listing.PackageLength = dto.PackageLength;
        }

        if (dto.PackageWidth.HasValue)
        {
            listing.PackageWidth = dto.PackageWidth;
        }

        if (dto.PackageHeight.HasValue)
        {
            listing.PackageHeight = dto.PackageHeight;
        }

        if (!string.IsNullOrWhiteSpace(dto.WeightUnit))
        {
            listing.WeightUnit = dto.WeightUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.DimensionUnit))
        {
            listing.DimensionUnit = dto.DimensionUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.SiteId))
        {
            listing.Site = dto.SiteId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.MainImage))
        {
            listing.MainImage = UpgradeHttpPipeJoined(dto.MainImage) ?? dto.MainImage.Trim();
        }
        else if (dto.Images is { Count: > 0 })
        {
            var main = dto.Images
                .Where(i => string.Equals(i.MediaType, "Main", StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.SortOrder)
                .Select(i => i.Url)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            if (!string.IsNullOrWhiteSpace(main))
            {
                listing.MainImage = UpgradeHttpToHttps(main.Trim()) ?? main.Trim();
            }
        }

        if (dto.AppliedPricing is not null)
        {
            listing.AppliedPricingJson = SerializeAppliedPricing(dto.AppliedPricing);
        }

        if (dto.DescriptionRewritten.HasValue)
        {
            listing.DescriptionRewritten = dto.DescriptionRewritten.Value;
        }

        if (dto.DescriptionImagesInserted.HasValue)
        {
            listing.DescriptionImagesInserted = dto.DescriptionImagesInserted.Value;
        }

        if (dto.Variations is not null)
        {
            ApplyVariationReplace(listing, dto.Variations);
            RecalcPriceStock(listing);
        }
    }

    /// <summary>
    /// On push/validation failure: drop unpersisted form edits, then store SyncError only.
    /// </summary>
    private async Task PersistPushFailureAsync(
        Listing listing,
        string syncError,
        bool discardFormEdits,
        CancellationToken cancellationToken)
    {
        if (discardFormEdits)
        {
            await RevertUnsavedListingGraphAsync(listing, cancellationToken);
        }

        listing.SyncError = syncError;
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await LearnAttributeRejectionsAsync([listing], cancellationToken);
    }

    /// <summary>
    /// Turns a publish failure into a lasting rule: when TikTok names an attribute value it will not
    /// accept, the value is blocked so the next push picks a different one instead of failing again.
    /// <para>
    /// Must run after the listing itself is saved — it writes through the same context — and never
    /// throws, because learning is an improvement on top of the sync, not part of it.
    /// </para>
    /// </summary>
    private async Task LearnAttributeRejectionsAsync(
        IReadOnlyCollection<Listing> listings,
        CancellationToken cancellationToken)
    {
        if (listings.Count == 0)
        {
            return;
        }

        // The same withdrawn value breaks every listing of a category at once, so one page of sync
        // carries the identical message many times over.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var listing in listings)
        {
            if (string.IsNullOrWhiteSpace(listing.CategoryId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(listing.SyncError))
            {
                await UnlearnAttributeRejectionsAsync(listing, cancellationToken);
                continue;
            }

            if (!seen.Add($"{listing.CategoryId}|{listing.SyncError}"))
            {
                continue;
            }

            try
            {
                await _attributeRejections.RecordFromErrorAsync(
                    listing.Id,
                    listing.CategoryId,
                    listing.SyncError,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not record TikTok attribute rejection for listing {ListingId}",
                    listing.Id);
            }
        }
    }

    /// <summary>
    /// The counter-evidence, taken from the one payload TikTok did accept: a listing that reached the
    /// platform without a failure message proves every attribute value it carries is still in the enum,
    /// so any of those values sitting on the denylist stops blocking.
    /// <para>
    /// This closes the loop that a scan of live listings cannot: category <c>903824</c> had all six
    /// <c>Washing instructions</c> values blocked from failures that were really missing valueIds, and
    /// the newest live payloads of that category never mentioned the value being checked.
    /// </para>
    /// </summary>
    private async Task UnlearnAttributeRejectionsAsync(Listing listing, CancellationToken cancellationToken)
    {
        var accepted = string.Equals(listing.PageType, ListingPageTypes.Published, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(
                           listing.SiteStatus,
                           ListingSiteStatuses.Active,
                           StringComparison.OrdinalIgnoreCase);
        if (!accepted || string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return;
        }

        try
        {
            await _attributeRejections.RecordAcceptedAsync(
                listing.Id,
                listing.CategoryId,
                listing.RawPayload,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not clear TikTok attribute rejections from live listing {ListingId}",
                listing.Id);
        }
    }

    private async Task<ListingOwnerSyncResultDto> SyncSellerAsync(
        Guid ownerId,
        string platform,
        IReadOnlyList<string> statuses,
        string? shopId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? maxRecordsPerSeller,
        string syncScope,
        IReadOnlyList<string>? listingIds,
        bool runReconcile,
        int? reconcileMaxPerSeller,
        bool purgeMissingOnReconcile,
        int reconcileMaxRounds,
        CancellationToken cancellationToken)
    {
        var counts = new ListingOwnerSyncResultDto();
        var targetedIds = (listingIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var recordCap = maxRecordsPerSeller is > 0 ? maxRecordsPerSeller.Value : (int?)null;
        var statusOnly = ListingSyncScopes.IsStatus(syncScope);

        // Manual Sync + Auto Sync only care about Active local shops that already carry a
        // FourSellerShopId — otherwise the cookie pulls every TTS shop on the account and we mint
        // orphan listings (ShopId null) that never show under extension Draft/Publish filters.
        var shops = await _db.Shops
            .AsNoTracking()
            .Where(s =>
                s.OwnerId == ownerId &&
                s.Status == ShopStatuses.Active &&
                s.FourSellerShopId != null &&
                s.FourSellerShopId != "")
            .Select(s => new { s.Id, s.FourSellerShopId })
            .ToListAsync(cancellationToken);

        var fourSellerShopToLocal = new Dictionary<long, Guid>();
        foreach (var shop in shops)
        {
            if (!long.TryParse(shop.FourSellerShopId, out var fsId) || fsId <= 0)
            {
                continue;
            }

            fourSellerShopToLocal.TryAdd(fsId, shop.Id);
        }

        // Resolve local shop Guid → 4Seller shop id when the client sent the filter from the UI dropdown.
        var fourSellerShopFilter = await ResolveFourSellerShopFilterAsync(ownerId, shopId, cancellationToken);

        // Explicit UI shop may be Deactivated / missing from the Active map — still allow this run.
        if (!string.IsNullOrWhiteSpace(fourSellerShopFilter) &&
            long.TryParse(fourSellerShopFilter, out var explicitFsId) &&
            explicitFsId > 0 &&
            !fourSellerShopToLocal.ContainsKey(explicitFsId))
        {
            var explicitLocalId = await _db.Shops
                .AsNoTracking()
                .Where(s =>
                    s.OwnerId == ownerId &&
                    s.FourSellerShopId == fourSellerShopFilter)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (explicitLocalId is Guid localId)
            {
                fourSellerShopToLocal[explicitFsId] = localId;
            }
        }

        // Empty string = unscoped (legacy: every shop on the cookie). Non-empty = one 4Seller shop id.
        List<string> shopIdFilters;
        if (!string.IsNullOrWhiteSpace(fourSellerShopFilter))
        {
            shopIdFilters = [fourSellerShopFilter];
        }
        else if (fourSellerShopToLocal.Count > 0)
        {
            shopIdFilters = fourSellerShopToLocal.Keys
                .OrderBy(id => id)
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToList();
        }
        else
        {
            shopIdFilters = [string.Empty];
        }

        var scopedToKnownShops = shopIdFilters.Exists(f => !string.IsNullOrWhiteSpace(f));
        if (scopedToKnownShops)
        {
            _logger.LogInformation(
                "Listing sync seller {OwnerId} scoped to {ShopCount} Active FourSeller shop id(s): {ShopIds}",
                ownerId,
                shopIdFilters.Count,
                string.Join(',', shopIdFilters));
        }

        // Targeted id pulls: chunk listingIdList (4Seller is unreliable with large batches).
        if (targetedIds.Count > 0)
        {
            foreach (var status in statuses)
            {
                foreach (var chunk in ChunkListingIds(targetedIds, ListingIdChunkSize))
                {
                    var page = await WithCallBudgetAsync(
                        ct => _fourSeller.GetListingPageAsync(
                            ownerId,
                            platform,
                            new FourSellerListingPageRequest
                            {
                                PageCurrent = 1,
                                PageSize = Math.Clamp(chunk.Count, 1, 100),
                                Status = status,
                                ListingIds = chunk
                            },
                            ct),
                        $"listing/page {status} ids×{chunk.Count}",
                        cancellationToken);

                    var pageRecords = page.Records.Where(r => r.Id > 0).ToList();
                    if (pageRecords.Count == 0)
                    {
                        continue;
                    }

                    IReadOnlyList<FourSellerListingRecordDto> merged = statusOnly
                        ? pageRecords
                        : await MergeFullPageAsync(
                            ownerId,
                            platform,
                            pageRecords,
                            status,
                            pageCurrent: 1,
                            cancellationToken);

                    var (pageInserted, pageUpdated, _) = await UpsertRecordsAsync(
                        ownerId,
                        platform,
                        status,
                        merged,
                        fourSellerShopToLocal,
                        syncScope,
                        cancellationToken);
                    counts.Inserted += pageInserted;
                    counts.Updated += pageUpdated;
                }
            }

            return counts;
        }

        // Delta state per (owner, platform, status): read up front, written back only when a bucket
        // finished cleanly. An explicit date / shop / id window means the caller asked for a specific
        // slice, so the watermark plays no part (shop-filtered pages say nothing about bucket progress).
        var skipWatermark = dateFrom.HasValue ||
                            dateTo.HasValue ||
                            scopedToKnownShops;
        var watermarks = skipWatermark
            ? new Dictionary<string, ListingSyncWatermark>(StringComparer.OrdinalIgnoreCase)
            : await _db.ListingSyncWatermarks
                .AsNoTracking()
                .Where(w => w.OwnerId == ownerId && w.Platform == platform)
                .ToDictionaryAsync(w => w.Status, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var status in statuses)
        {
            if (recordCap.HasValue && recordCap.Value <= 0)
            {
                break;
            }

            watermarks.TryGetValue(status, out var watermark);
            var now = DateTime.UtcNow;
            var fullScanDue = watermark?.LastFullScanAtUtc is null ||
                              now - watermark.LastFullScanAtUtc.Value >= FullScanInterval;
            // Delta only for unscoped pulls — shop-filtered pages skip watermark entirely.
            var deltaFrom = !skipWatermark &&
                            watermark?.LastUpdateTimeUtc is not null &&
                            !fullScanDue
                ? watermark.LastUpdateTimeUtc.Value - DeltaSafetyMargin
                : (DateTime?)null;

            // Highest updateTime actually processed this status — becomes the next watermark.
            DateTime? maxUpdateTimeSeen = null;

            // Fixed for the whole bucket: 4Seller derives its offset from pageCurrent * pageSize,
            // so shrinking the size mid-run would re-request rows already seen.
            var pageSize = recordCap.HasValue
                ? Math.Clamp(recordCap.Value, 1, 100)
                : 100;

            foreach (var shopFilter in shopIdFilters)
            {
                // Cap is per Active shop when scoped (so one busy foreign shop cannot starve the rest);
                // unscoped keeps a single shared counter for the status bucket.
                var fetchedForShop = 0;
                var pageCurrent = 1;

                while (true)
                {
                    if (recordCap.HasValue && fetchedForShop >= recordCap.Value)
                    {
                        break;
                    }

                    var requestedPage = pageCurrent;
                    var page = await WithCallBudgetAsync(
                        ct => _fourSeller.GetListingPageAsync(
                            ownerId,
                            platform,
                            new FourSellerListingPageRequest
                            {
                                PageCurrent = requestedPage,
                                PageSize = pageSize,
                                Status = status,
                                ShopId = shopFilter,
                                DateFrom = dateFrom,
                                DateTo = dateTo,
                                UpdatedFrom = deltaFrom
                            },
                            ct),
                        string.IsNullOrWhiteSpace(shopFilter)
                            ? $"listing/page {status} #{requestedPage}"
                            : $"listing/page {status} shop={shopFilter} #{requestedPage}",
                        cancellationToken);

                    var returned = page.Records.Count;
                    var pageRecords = page.Records.Where(r => r.Id > 0).ToList();
                    // Pages arrive newest-first, so a row older than the cut-off means the delta range is
                    // fully covered — a safety net in case 4Seller ignores updateTimeStart for a bucket.
                    var reachedOlderThanWatermark = false;
                    foreach (var seen in pageRecords)
                    {
                        var seenUpdateTime = ParseFourSellerTime(seen.UpdateTime);
                        if (seenUpdateTime is null)
                        {
                            continue;
                        }

                        if (maxUpdateTimeSeen is null || seenUpdateTime > maxUpdateTimeSeen)
                        {
                            maxUpdateTimeSeen = seenUpdateTime;
                        }

                        if (deltaFrom is not null && seenUpdateTime < deltaFrom)
                        {
                            reachedOlderThanWatermark = true;
                        }
                    }

                    if (recordCap.HasValue && fetchedForShop + pageRecords.Count > recordCap.Value)
                    {
                        pageRecords = pageRecords
                            .Take(recordCap.Value - fetchedForShop)
                            .ToList();
                    }

                    if (pageRecords.Count > 0)
                    {
                        var mergeClock = Stopwatch.StartNew();
                        IReadOnlyList<FourSellerListingRecordDto> merged;
                        if (statusOnly)
                        {
                            merged = pageRecords;
                        }
                        else
                        {
                            merged = await MergeFullPageAsync(
                                ownerId,
                                platform,
                                pageRecords,
                                status,
                                pageCurrent,
                                cancellationToken);
                        }

                        mergeClock.Stop();
                        var upsertClock = Stopwatch.StartNew();
                        var (pageInserted, pageUpdated, pageSkipped) = await UpsertRecordsAsync(
                            ownerId,
                            platform,
                            status,
                            merged,
                            fourSellerShopToLocal,
                            syncScope,
                            cancellationToken);
                        upsertClock.Stop();

                        _logger.LogInformation(
                            "Listing page {Status} #{Page} seller {OwnerId}{ShopHint}: {Count} rows, merge {MergeMs} ms, upsert {UpsertMs} ms (+{Inserted}/~{Updated}/={Skipped}) [{Scope}]",
                            status,
                            requestedPage,
                            ownerId,
                            string.IsNullOrWhiteSpace(shopFilter) ? string.Empty : $" shop={shopFilter}",
                            pageRecords.Count,
                            (long)mergeClock.Elapsed.TotalMilliseconds,
                            (long)upsertClock.Elapsed.TotalMilliseconds,
                            pageInserted,
                            pageUpdated,
                            pageSkipped,
                            deltaFrom is null ? "full" : $"delta>={FormatUtc(deltaFrom.Value)}");

                        counts.Inserted += pageInserted;
                        counts.Updated += pageUpdated;
                    }

                    // Advance by what 4Seller returned (not what survived filtering) so the offset stays
                    // aligned with the remote paging even when a row is unusable.
                    fetchedForShop += returned;

                    // A short page means this status/shop slice is exhausted.
                    if (reachedOlderThanWatermark || returned < pageSize || fetchedForShop >= page.Total)
                    {
                        break;
                    }

                    pageCurrent++;
                }
            }

            // Reached only when the page loop ended on its own terms; a failed call throws out of
            // this method, so a half-read bucket never moves the mark — that is the case the plan
            // guards against, since pages come back newest-first and raising the mark past rows that
            // were never read would strand them forever.
            //
            // A run cut short by maxRecordsPerSeller still counts. That cap already stops background
            // sync from ever looking past the newest N rows of a bucket, both before and after this
            // change, so advancing the mark loses no coverage — it only stops re-reading rows 4Seller
            // has told us are unchanged. A manual sync with a larger cap covers the whole bucket.
            // A targeted run only ever saw the rows the caller named, so the newest updateTime among
            // them says nothing about how far the bucket has been scanned — recording it would skip
            // every other row changed since. Same for a shop-filtered pull.
            if (!skipWatermark)
            {
                await SaveSyncWatermarkAsync(
                    ownerId,
                    platform,
                    status,
                    maxUpdateTimeSeen,
                    markFullScan: deltaFrom is null,
                    cancellationToken);
            }
        }

        if (statusOnly && runReconcile)
        {
            var reconcileCap = reconcileMaxPerSeller is > 0
                ? reconcileMaxPerSeller.Value
                : ReconcileStaleInFlightMaxPerSeller;
            var rounds = reconcileMaxRounds > 0 ? reconcileMaxRounds : 1;
            var reconcile = await ReconcileStaleInFlightAsync(
                ownerId,
                platform,
                reconcileCap,
                rounds,
                purgeMissingOnReconcile,
                dateFrom,
                dateTo,
                cancellationToken);
            counts.Inserted += reconcile.Inserted;
            counts.Updated += reconcile.Updated;
            counts.ReconciledUpdated += reconcile.ReconciledUpdated;
            counts.ReconciledRemoved += reconcile.ReconciledRemoved;
            counts.StillInFlight = reconcile.StillInFlight;
        }

        // Only a pull that minted rows can have produced a twin of an existing mirror — either a
        // 4Seller renumbering the adopt pass could not match, or a genuine duplicate inside one
        // account. Collapse before the seller opens the list so the thin shell never shows up.
        if (counts.Inserted > 0 && fourSellerShopToLocal.Count > 0)
        {
            await _ownershipTransfer.CollapseShopDuplicatesAsync(
                fourSellerShopToLocal.Values.Distinct().ToList(),
                cancellationToken);
        }

        return counts;
    }

    /// <summary>
    /// Re-pulls local in-flight or stale pending listings by id across every status bucket.
    /// Manual Sync may purge rows proven missing on 4Seller (same safety as Dev reconcile-review).
    /// </summary>
    private async Task<ListingOwnerSyncResultDto> ReconcileStaleInFlightAsync(
        Guid ownerId,
        string platform,
        int maxPerRound,
        int maxRounds,
        bool purgeMissing,
        DateTime? preferDateFrom,
        DateTime? preferDateTo,
        CancellationToken cancellationToken)
    {
        var counts = new ListingOwnerSyncResultDto();
        var take = maxPerRound > 0 ? maxPerRound : ReconcileStaleInFlightMaxPerSeller;
        var rounds = maxRounds > 0 ? maxRounds : 1;

        for (var round = 0; round < rounds; round++)
        {
            var listingIds = await LoadReconcileListingIdsAsync(
                ownerId,
                platform,
                take,
                preferDateFrom,
                preferDateTo,
                cancellationToken);

            if (listingIds.Count == 0)
            {
                break;
            }

            _logger.LogInformation(
                "Reconciling {Count} stale in-flight listing(s) for seller {OwnerId} (round {Round}/{MaxRounds}, purge={Purge})",
                listingIds.Count,
                ownerId,
                round + 1,
                rounds,
                purgeMissing);

            if (purgeMissing)
            {
                var filterProven = await ProveListingIdFilterAsync(ownerId, platform, cancellationToken);
                var (foundIds, bucketError) = await LocateListingIdsAcrossBucketsAsync(
                    ownerId,
                    platform,
                    listingIds,
                    cancellationToken);

                if (bucketError is not null)
                {
                    _logger.LogWarning(
                        "Reconcile locate incomplete for seller {OwnerId}: {Error} — syncing asked ids without purge",
                        ownerId,
                        bucketError);
                    var fallback = await SyncSellerAsync(
                        ownerId,
                        platform,
                        ListingSiteStatuses.DefaultSyncStatuses.ToList(),
                        shopId: null,
                        dateFrom: null,
                        dateTo: null,
                        maxRecordsPerSeller: listingIds.Count,
                        syncScope: ListingSyncScopes.Status,
                        listingIds,
                        runReconcile: false,
                        reconcileMaxPerSeller: null,
                        purgeMissingOnReconcile: false,
                        reconcileMaxRounds: 1,
                        cancellationToken);
                    counts.Inserted += fallback.Inserted;
                    counts.Updated += fallback.Updated;
                    counts.ReconciledUpdated += fallback.Inserted + fallback.Updated;
                    break;
                }

                var foundList = foundIds.ToList();
                if (foundList.Count > 0)
                {
                    var synced = await SyncSellerAsync(
                        ownerId,
                        platform,
                        ListingSiteStatuses.DefaultSyncStatuses.ToList(),
                        shopId: null,
                        dateFrom: null,
                        dateTo: null,
                        maxRecordsPerSeller: foundList.Count,
                        syncScope: ListingSyncScopes.Status,
                        foundList,
                        runReconcile: false,
                        reconcileMaxPerSeller: null,
                        purgeMissingOnReconcile: false,
                        reconcileMaxRounds: 1,
                        cancellationToken);
                    counts.Inserted += synced.Inserted;
                    counts.Updated += synced.Updated;
                    counts.ReconciledUpdated += synced.Inserted + synced.Updated;
                }

                if (filterProven)
                {
                    var missing = MissingListingIds(listingIds, foundIds);
                    if (missing.Count > 0)
                    {
                        var deleted = await DeleteGoneInFlightListingsAsync(
                            ownerId,
                            missing,
                            cancellationToken);
                        counts.ReconciledRemoved += deleted;
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "listingIdList filter not proven for seller {OwnerId} — skipping purge of {Missing} missing id(s)",
                        ownerId,
                        listingIds.Count - foundIds.Count);
                }
            }
            else
            {
                var synced = await SyncSellerAsync(
                    ownerId,
                    platform,
                    ListingSiteStatuses.DefaultSyncStatuses.ToList(),
                    shopId: null,
                    dateFrom: null,
                    dateTo: null,
                    maxRecordsPerSeller: listingIds.Count,
                    syncScope: ListingSyncScopes.Status,
                    listingIds,
                    runReconcile: false,
                    reconcileMaxPerSeller: null,
                    purgeMissingOnReconcile: false,
                    reconcileMaxRounds: 1,
                    cancellationToken);
                counts.Inserted += synced.Inserted;
                counts.Updated += synced.Updated;
                counts.ReconciledUpdated += synced.Inserted + synced.Updated;
            }

            if (listingIds.Count < take)
            {
                break;
            }
        }

        counts.StillInFlight = await CountInFlightListingsAsync(ownerId, platform, cancellationToken);
        return counts;
    }

    private async Task<List<string>> LoadReconcileListingIdsAsync(
        Guid ownerId,
        string platform,
        int take,
        DateTime? preferDateFrom,
        DateTime? preferDateTo,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleBefore = now - StalePendingSyncThreshold;

        // Over-fetch then rank in memory so date-window preference stays SQL-simple.
        var fetch = Math.Max(take * 3, take);
        var rows = await _db.Listings
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId && l.Platform == platform)
            .Where(l =>
                l.PublishStatus == PublishStatuses.Publishing
                || l.PublishStatus == PublishStatuses.UnderReview
                || l.PublishStatus == "publishing"
                || l.PublishStatus == "underreview"
                || (l.SiteStatus == ListingSiteStatuses.Pending
                    && l.LastSyncedAt < staleBefore))
            .OrderBy(l => l.LastSyncedAt)
            .Take(fetch)
            .Select(l => new
            {
                l.FourSellerListingId,
                l.LastPushAt,
                l.PublishStatus,
                CreateClock = l.PlatformCreateTime ?? l.CreatedAt,
                l.LastSyncedAt
            })
            .ToListAsync(cancellationToken);

        bool InWindow(DateTime clock) =>
            (!preferDateFrom.HasValue || clock >= preferDateFrom.Value)
            && (!preferDateTo.HasValue || clock <= preferDateTo.Value);

        var preferWindow = preferDateFrom.HasValue || preferDateTo.HasValue;

        return rows
            .Where(l => MayReconcileInFlight(l.PublishStatus, l.LastPushAt, now))
            .Where(l => l.FourSellerListingId > 0)
            .OrderBy(l => preferWindow && !InWindow(l.CreateClock) ? 1 : 0)
            .ThenBy(l => l.LastSyncedAt)
            .Select(l => l.FourSellerListingId.ToString())
            .Distinct()
            .Take(take)
            .ToList();
    }

    /// <summary>Whether an in-flight row is past the post-push grace and eligible for reconcile.</summary>
    internal static bool MayReconcileInFlight(string? publishStatus, DateTime? lastPushAt, DateTime utcNow)
    {
        if (!PublishStatuses.IsInFlight(publishStatus))
        {
            return true;
        }

        if (lastPushAt is not { } pushedAt)
        {
            return true;
        }

        return utcNow - pushedAt > OptimisticPendingGracePeriod;
    }

    /// <summary>Whether a missing-on-4Seller row may be deleted locally (past push grace).</summary>
    internal static bool MayPurgeGoneListing(DateTime? lastPushAt, DateTime utcNow) =>
        lastPushAt is not { } pushedAt || utcNow - pushedAt > OptimisticPendingGracePeriod;

    /// <summary>Ids asked for reconcile that were not returned in any bucket.</summary>
    internal static List<string> MissingListingIds(
        IReadOnlyList<string> asked,
        HashSet<string> found)
    {
        var missing = new List<string>();
        foreach (var id in asked)
        {
            if (!found.Contains(id))
            {
                missing.Add(id);
            }
        }

        return missing;
    }

    /// <summary>Splits listing ids into batches safe for 4Seller <c>listingIdList</c>.</summary>
    internal static IEnumerable<List<string>> ChunkListingIds(IReadOnlyList<string> source, int size)
    {
        var chunkSize = size > 0 ? size : ListingIdChunkSize;
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            var len = Math.Min(chunkSize, source.Count - i);
            var chunk = new List<string>(len);
            for (var j = 0; j < len; j++)
            {
                chunk.Add(source[i + j]);
            }

            yield return chunk;
        }
    }

    private async Task<bool> ProveListingIdFilterAsync(
        Guid ownerId,
        string platform,
        CancellationToken cancellationToken)
    {
        try
        {
            var probe = await WithCallBudgetAsync(
                ct => _fourSeller.GetListingPageAsync(
                    ownerId,
                    platform,
                    new FourSellerListingPageRequest
                    {
                        PageCurrent = 1,
                        PageSize = 1,
                        Status = ListingSiteStatuses.Active
                    },
                    ct),
                "listing/page prove probe",
                cancellationToken);

            var knownId = probe.Records.FirstOrDefault()?.Id;
            if (knownId is null or <= 0)
            {
                return false;
            }

            var filtered = await WithCallBudgetAsync(
                ct => _fourSeller.GetListingPageAsync(
                    ownerId,
                    platform,
                    new FourSellerListingPageRequest
                    {
                        PageCurrent = 1,
                        PageSize = 10,
                        Status = ListingSiteStatuses.Active,
                        ListingIds = [knownId.Value.ToString()]
                    },
                    ct),
                "listing/page prove filter",
                cancellationToken);

            return filtered.Records.Count > 0
                   && filtered.Records.All(r => r.Id == knownId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not prove listingIdList filter for seller {OwnerId}", ownerId);
            return false;
        }
    }

    private async Task<(HashSet<string> FoundIds, string? BucketError)> LocateListingIdsAcrossBucketsAsync(
        Guid ownerId,
        string platform,
        IReadOnlyList<string> listingIds,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in ListingSiteStatuses.DefaultSyncStatuses)
        {
            foreach (var chunk in ChunkListingIds(listingIds, ListingIdChunkSize))
            {
                try
                {
                    var page = await WithCallBudgetAsync(
                        ct => _fourSeller.GetListingPageAsync(
                            ownerId,
                            platform,
                            new FourSellerListingPageRequest
                            {
                                PageCurrent = 1,
                                PageSize = Math.Clamp(chunk.Count, 1, 100),
                                Status = status,
                                ListingIds = chunk
                            },
                            ct),
                        $"listing/page locate {status}",
                        cancellationToken);

                    foreach (var record in page.Records.Where(r => r.Id > 0))
                    {
                        found.Add(record.Id.ToString());
                    }
                }
                catch (Exception ex)
                {
                    return (found, $"{status}: {ex.Message}");
                }
            }
        }

        return (found, null);
    }

    private async Task<int> DeleteGoneInFlightListingsAsync(
        Guid ownerId,
        IReadOnlyList<string> missingIds,
        CancellationToken cancellationToken)
    {
        if (missingIds.Count == 0)
        {
            return 0;
        }

        var idSet = new HashSet<long>();
        foreach (var raw in missingIds)
        {
            if (long.TryParse(raw, out var id) && id > 0)
            {
                idSet.Add(id);
            }
        }

        if (idSet.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var rows = await _db.Listings
            .Include(l => l.Variations)
            .Where(l => l.OwnerId == ownerId && idSet.Contains(l.FourSellerListingId))
            .Where(l =>
                l.PublishStatus == PublishStatuses.Publishing
                || l.PublishStatus == PublishStatuses.UnderReview
                || l.PublishStatus == "publishing"
                || l.PublishStatus == "underreview"
                || l.SiteStatus == ListingSiteStatuses.Pending)
            .ToListAsync(cancellationToken);

        var toDelete = rows.Where(l => MayPurgeGoneListing(l.LastPushAt, now)).ToList();
        if (toDelete.Count == 0)
        {
            return 0;
        }

        foreach (var row in toDelete)
        {
            _db.ListingVariations.RemoveRange(row.Variations);
        }

        _db.Listings.RemoveRange(toDelete);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Purged {Count} gone in-flight listing(s) for seller {OwnerId}",
            toDelete.Count,
            ownerId);

        return toDelete.Count;
    }

    private Task<int> CountInFlightListingsAsync(
        Guid ownerId,
        string platform,
        CancellationToken cancellationToken) =>
        _db.Listings
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId && l.Platform == platform)
            .Where(l =>
                l.PublishStatus == PublishStatuses.Publishing
                || l.PublishStatus == PublishStatuses.UnderReview
                || l.PublishStatus == "publishing"
                || l.PublishStatus == "underreview"
                || l.SiteStatus == ListingSiteStatuses.Pending)
            .CountAsync(cancellationToken);

    /// <summary>
    /// Records how far a status bucket got, so the next run can ask 4Seller for changes only.
    /// A delta run that saw nothing keeps the previous mark rather than resetting it.
    /// </summary>
    private async Task SaveSyncWatermarkAsync(
        Guid ownerId,
        string platform,
        string status,
        DateTime? maxUpdateTimeSeen,
        bool markFullScan,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var watermark = await _db.ListingSyncWatermarks
            .FirstOrDefaultAsync(
                w => w.OwnerId == ownerId && w.Platform == platform && w.Status == status,
                cancellationToken);

        if (watermark is null)
        {
            watermark = new ListingSyncWatermark
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Platform = platform,
                Status = status
            };
            _db.ListingSyncWatermarks.Add(watermark);
        }

        if (maxUpdateTimeSeen is not null &&
            (watermark.LastUpdateTimeUtc is null || maxUpdateTimeSeen > watermark.LastUpdateTimeUtc))
        {
            watermark.LastUpdateTimeUtc = maxUpdateTimeSeen;
        }

        if (markFullScan)
        {
            watermark.LastFullScanAtUtc = now;
        }

        watermark.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Light sync: page rows only; batch-detail only for ids missing locally or without RawPayload (cap per page).
    /// </summary>
    private async Task<IReadOnlyList<FourSellerListingRecordDto>> MergeLightPageAsync(
        Guid ownerId,
        string platform,
        IReadOnlyList<FourSellerListingRecordDto> pageRecords,
        CancellationToken cancellationToken)
    {
        var ids = pageRecords.Select(r => r.Id).ToList();
        // Global by FourSellerListingId — after shop transfer the rich mirror may still sit on the
        // previous seller; scoping by OwnerId would miss it and treat the row as needing a thin seed.
        // jsonb: never compare RawPayload to '' — Postgres casts ''→json and throws 22P02.
        var existingPayload = await _db.Listings
            .AsNoTracking()
            .Where(l => ids.Contains(l.FourSellerListingId))
            .Select(l => new
            {
                l.FourSellerListingId,
                HasPayload = l.RawPayload != null
            })
            .ToListAsync(cancellationToken);

        var hasPayload = existingPayload
            .GroupBy(x => x.FourSellerListingId)
            .ToDictionary(g => g.Key, g => g.First().HasPayload);

        var needDetail = ids
            .Where(id => !hasPayload.TryGetValue(id, out var ok) || !ok)
            .Take(LightDetailCapPerPage)
            .ToList();

        var detailById = new Dictionary<long, FourSellerListingRecordDto>();
        if (needDetail.Count > 0)
        {
            try
            {
                var details = await WithCallBudgetAsync(
                    ct => _fourSeller.GetListingBatchDetailAsync(
                        ownerId,
                        platform,
                        needDetail,
                        ct,
                        allowPerIdFallback: false),
                    $"batch-detail {needDetail.Count} ids",
                    cancellationToken);
                foreach (var d in details.Where(d => d.Id > 0))
                {
                    detailById[d.Id] = d;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "light batch-detail failed for seller {OwnerId} ({Count} ids); page-only merge",
                    ownerId,
                    needDetail.Count);
            }
        }

        return pageRecords
            .Select(p => MergePageAndDetail(p, detailById.GetValueOrDefault(p.Id)))
            .ToList();
    }

    private async Task<IReadOnlyList<FourSellerListingRecordDto>> MergeFullPageAsync(
        Guid ownerId,
        string platform,
        IReadOnlyList<FourSellerListingRecordDto> pageRecords,
        string status,
        int pageCurrent,
        CancellationToken cancellationToken)
    {
        var details = new List<FourSellerListingRecordDto>();
        const int detailChunkSize = 30;
        var ids = pageRecords.Select(r => r.Id).ToList();
        for (var offset = 0; offset < ids.Count; offset += detailChunkSize)
        {
            var chunk = ids.Skip(offset).Take(detailChunkSize).ToList();
            IReadOnlyList<FourSellerListingRecordDto> chunkDetails = [];
            Exception? lastChunkError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    chunkDetails = await WithCallBudgetAsync(
                        ct => _fourSeller.GetListingBatchDetailAsync(
                            ownerId,
                            platform,
                            chunk,
                            ct,
                            allowPerIdFallback: false),
                        $"batch-detail {chunk.Count} ids",
                        cancellationToken);
                    lastChunkError = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastChunkError = ex;
                    if (attempt < 2)
                    {
                        _logger.LogWarning(
                            ex,
                            "batch-detail chunk attempt {Attempt} failed for seller {OwnerId} status {Status} page {Page} offset {Offset}; retrying",
                            attempt,
                            ownerId,
                            status,
                            pageCurrent,
                            offset);
                    }
                }
            }

            if (chunkDetails.Count > 0)
            {
                details.AddRange(chunkDetails);
            }
            else if (lastChunkError is not null)
            {
                _logger.LogWarning(
                    lastChunkError,
                    "batch-detail chunk failed after retry for seller {OwnerId} status {Status} page {Page} offset {Offset}; page-only merge (description/skuImgs may be incomplete)",
                    ownerId,
                    status,
                    pageCurrent,
                    offset);
            }
        }

        var detailById = details
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var missingDetail = pageRecords.Count(p => !detailById.ContainsKey(p.Id));
        if (missingDetail > 0)
        {
            _logger.LogWarning(
                "batch-detail missing for {Missing}/{Total} listings (seller {OwnerId} status {Status} page {Page}) — description/Variant Image may be incomplete until resync",
                missingDetail,
                pageRecords.Count,
                ownerId,
                status,
                pageCurrent);
        }

        return pageRecords
            .Select(p => MergePageAndDetail(p, detailById.GetValueOrDefault(p.Id)))
            .ToList();
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertRecordsAsync(
        Guid ownerId,
        string platform,
        string pulledStatus,
        IReadOnlyList<FourSellerListingRecordDto> records,
        IReadOnlyDictionary<long, Guid> shopByFourSellerId,
        string syncScope,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return (0, 0, 0);
        }

        // Bound change-tracking to a single page: disable auto-detect (we DetectChanges manually),
        // and Clear() afterwards so a long run does not accumulate every loaded listing in memory.
        var originalAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return await UpsertPageCoreAsync(
                ownerId,
                platform,
                pulledStatus,
                records,
                shopByFourSellerId,
                syncScope,
                cancellationToken);
        }
        finally
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
        }
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertPageCoreAsync(
        Guid ownerId,
        string platform,
        string pulledStatus,
        IReadOnlyList<FourSellerListingRecordDto> records,
        IReadOnlyDictionary<long, Guid> shopByFourSellerId,
        string syncScope,
        CancellationToken cancellationToken)
    {
        var statusOnly = ListingSyncScopes.IsStatus(syncScope);
        // One record per listing id — duplicates in a page would Clear/delete the same children twice.
        var uniqueRecords = records
            .GroupBy(r => r.Id)
            .Select(g => g.Last())
            .ToList();

        var listingIds = uniqueRecords.Select(r => r.Id).ToList();
        // Split query: a single JOIN repeats each listing's RawPayload jsonb once per variation row,
        // so a page of drafts with many SKUs transfers hundreds of MB and dominates the sync.
        var existing = await _db.Listings
            .Include(l => l.Variations)
            .AsSplitQuery()
            .Where(l => listingIds.Contains(l.FourSellerListingId))
            .ToDictionaryAsync(l => l.FourSellerListingId, cancellationToken);

        var myShopIds = shopByFourSellerId.Values.ToHashSet();
        var now = DateTime.UtcNow;

        var adoptedIds = await AdoptRenumberedListingsAsync(
            ownerId,
            uniqueRecords,
            existing,
            shopByFourSellerId,
            cancellationToken);

        var newListings = new List<Listing>();
        // Existing listings touched this page + whether SyncVariations added/removed rows
        // (structural changes are invisible via ChangeTracker once removed from the collection).
        var existingListings = new List<(Listing Listing, bool StructuralVariationChange)>();
        var skipped = 0;
        var skipBlockReasons = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var record in uniqueRecords)
        {
            Guid? localShopId = null;
            if (record.ShopId is > 0 &&
                shopByFourSellerId.TryGetValue(record.ShopId.Value, out var resolvedShop))
            {
                localShopId = resolvedShop;
            }
            else if (shopByFourSellerId.Count > 0)
            {
                // Active-shop-scoped sync: ignore cookie listings that belong to shops we do not
                // manage locally (avoids orphan Row with FourSellerShopId set but ShopId null).
                skipped++;
                skipBlockReasons["foreign-shop"] =
                    skipBlockReasons.GetValueOrDefault("foreign-shop") + 1;
                continue;
            }

            // Prefer productStatus / erpStatus on the row over the page filter bucket.
            var (pageType, siteStatus) = ResolveListingStatuses(
                pulledStatus,
                record.ProductStatus,
                record.ErpStatus,
                record.PublishStatus);

            if (existing.TryGetValue(record.Id, out var listing))
            {
                // 4Seller only moves updateTime when it actually changed the row, so an unchanged
                // row can skip the whole apply/variation pass. It still goes through enrichment and
                // change detection below, so a pending SupplierProductInfo / AddedFrom link is not lost.
                var blockReason = adoptedIds.Contains(record.Id)
                    ? "adopted-renumbered"
                    : statusOnly
                        ? SkipStatusBlockReason(
                            listing,
                            record,
                            ownerId,
                            pageType,
                            siteStatus,
                            localShopId,
                            myShopIds)
                        : SkipBlockReason(
                            listing,
                            record,
                            ownerId,
                            platform,
                            pageType,
                            siteStatus,
                            localShopId,
                            myShopIds);
                if (blockReason is null)
                {
                    skipped++;
                    existingListings.Add((listing, false));
                    continue;
                }

                skipBlockReasons.TryGetValue(blockReason, out var reasonCount);
                skipBlockReasons[blockReason] = reasonCount + 1;

                if (statusOnly)
                {
                    ApplyStatusRecord(
                        listing,
                        record,
                        ownerId,
                        platform,
                        pageType,
                        siteStatus,
                        localShopId,
                        myShopIds: myShopIds);
                    existingListings.Add((listing, false));
                }
                else
                {
                    ApplyRecord(
                        listing,
                        record,
                        ownerId,
                        platform,
                        pageType,
                        siteStatus,
                        localShopId,
                        lightMode: false,
                        myShopIds: myShopIds);
                    var structuralVariationChange = SyncVariations(listing, record.VariationList, now);
                    existingListings.Add((listing, structuralVariationChange));
                }
            }
            else
            {
                listing = new Listing
                {
                    Id = Guid.NewGuid(),
                    FourSellerListingId = record.Id,
                    OwnerId = ownerId,
                    CreatedAt = now
                };
                if (statusOnly)
                {
                    ApplyStatusRecord(
                        listing,
                        record,
                        ownerId,
                        platform,
                        pageType,
                        siteStatus,
                        localShopId,
                        seedListCardFields: true,
                        myShopIds: myShopIds);
                }
                else
                {
                    ApplyRecord(
                        listing,
                        record,
                        ownerId,
                        platform,
                        pageType,
                        siteStatus,
                        localShopId,
                        lightMode: false,
                        myShopIds: myShopIds);
                    SyncVariations(listing, record.VariationList, now);
                }

                _db.Listings.Add(listing);
                existing[record.Id] = listing;
                newListings.Add(listing);
            }
        }

        var touched = existingListings.Select(x => x.Listing).Concat(newListings).ToList();
        if (!statusOnly)
        {
            await _supplierProductInfos.ApplyToListingsAsync(touched, cancellationToken);
            await _listingAddOrigins.ApplyToListingsAsync(touched, cancellationToken);
        }

        // Detect all business changes made above (auto-detect is disabled for this page).
        _db.ChangeTracker.DetectChanges();

        // New listings always get timestamps. Existing listings only when something actually changed,
        // so unchanged listings produce no UPDATE (the dominant cost on frequent, growing syncs).
        var inserted = newListings.Count;
        foreach (var listing in newListings)
        {
            StampListingTimestamps(listing, now, stampAllVariations: true);
        }

        var updated = 0;
        foreach (var (listing, structuralVariationChange) in existingListings)
        {
            var listingChanged = _db.Entry(listing).State == EntityState.Modified;
            var variationsChanged = structuralVariationChange || listing.Variations.Any(v =>
            {
                var state = _db.Entry(v).State;
                return state == EntityState.Added || state == EntityState.Modified;
            });

            if (listingChanged || variationsChanged)
            {
                StampListingTimestamps(listing, now, stampAllVariations: false);
                updated++;
            }
        }

        // Second pass so the timestamp stamps above are persisted (auto-detect is disabled).
        _db.ChangeTracker.DetectChanges();

        if (skipBlockReasons.Count > 0)
        {
            _logger.LogInformation(
                "Listing skip blocked for seller {OwnerId}: {Reasons}",
                ownerId,
                string.Join(
                    ", ",
                    skipBlockReasons
                        .OrderByDescending(pair => pair.Value)
                        .Select(pair => $"{pair.Key}={pair.Value}")));
        }

        await SaveUpsertChangesAsync(cancellationToken);
        // TikTok reports publish failures asynchronously through 4Seller's failedMessage, so this pass
        // is where a withdrawn attribute value first becomes visible.
        await LearnAttributeRejectionsAsync(touched, cancellationToken);
        return (inserted, updated, skipped);
    }

    /// <summary>
    /// 4Seller listing ids are internal to one 4Seller account, so re-connecting a shop under a
    /// different account mints brand new ids for products this mirror already holds. Without this
    /// pass the upsert would seed a thin shell (no SourceUrl / SupplierProductInfo / RawPayload)
    /// next to the rich row. Repoints the existing row onto the incoming id instead.
    /// </summary>
    /// <returns>The incoming 4Seller listing ids that adopted an existing row.</returns>
    private async Task<HashSet<long>> AdoptRenumberedListingsAsync(
        Guid ownerId,
        IReadOnlyList<FourSellerListingRecordDto> uniqueRecords,
        Dictionary<long, Listing> existing,
        IReadOnlyDictionary<long, Guid> shopByFourSellerId,
        CancellationToken cancellationToken)
    {
        var adopted = new HashSet<long>();
        if (shopByFourSellerId.Count == 0)
        {
            return adopted;
        }

        // Only unknown ids can adopt, and only when the row carries a TikTok product to match on
        // and resolves to a shop of the syncing seller.
        var orphanRecords = new List<(FourSellerListingRecordDto Record, Guid ShopId, string TikTokProductId)>();
        foreach (var record in uniqueRecords)
        {
            if (existing.ContainsKey(record.Id)
                || string.IsNullOrWhiteSpace(record.MarketplaceProductId)
                || record.ShopId is not > 0
                || !shopByFourSellerId.TryGetValue(record.ShopId.Value, out var localShopId))
            {
                continue;
            }

            orphanRecords.Add((record, localShopId, record.MarketplaceProductId.Trim()));
        }

        if (orphanRecords.Count == 0)
        {
            return adopted;
        }

        var shopIds = orphanRecords.Select(x => x.ShopId).Distinct().ToList();
        var tikTokProductIds = orphanRecords.Select(x => x.TikTokProductId).Distinct().ToList();

        // Orphans (no ShopId) are included only for the syncing seller: a rich mirror that lost its
        // shop link during a transfer is still the row this product belongs to.
        var candidates = await _db.Listings
            .Include(l => l.Variations)
            .AsSplitQuery()
            .Where(l => l.TikTokProductId != null
                        && tikTokProductIds.Contains(l.TikTokProductId)
                        && (l.ShopId != null
                            ? shopIds.Contains(l.ShopId.Value)
                            : l.OwnerId == ownerId))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return adopted;
        }

        // Ids the page itself carries: a row still owned by an incoming record must never be stolen.
        var incomingIds = uniqueRecords.Select(r => r.Id).ToHashSet();
        var claimed = new HashSet<Guid>();

        foreach (var (record, localShopId, tikTokProductId) in orphanRecords)
        {
            var match = SelectAdoptionCandidate(
                candidates,
                ownerId,
                localShopId,
                tikTokProductId,
                record.ShopId,
                claimed,
                incomingIds);

            if (match is null)
            {
                continue;
            }

            _logger.LogInformation(
                "Adopting listing {ListingId} (4Seller {OldId} → {NewId}, TikTok {TikTokProductId}) on shop {ShopId}",
                match.Id,
                match.FourSellerListingId,
                record.Id,
                tikTokProductId,
                localShopId);

            match.FourSellerListingId = record.Id;
            claimed.Add(match.Id);
            existing[record.Id] = match;
            adopted.Add(record.Id);
        }

        return adopted;
    }

    /// <summary>
    /// Picks the mirror an incoming record should adopt, or <c>null</c> when it must stay a new row.
    /// </summary>
    /// <param name="candidates">Rows already holding this TikTok product on the seller's shops.</param>
    /// <param name="ownerId">Seller whose 4Seller cookie is driving the sync.</param>
    /// <param name="localShopId">Local shop the incoming record resolved to.</param>
    /// <param name="tikTokProductId">Trimmed TikTok product id from the record.</param>
    /// <param name="recordFourSellerShopId">4Seller shop id on the record.</param>
    /// <param name="claimed">Rows already adopted earlier in this page.</param>
    /// <param name="incomingIds">Every 4Seller listing id the page carries.</param>
    internal static Listing? SelectAdoptionCandidate(
        IReadOnlyCollection<Listing> candidates,
        Guid ownerId,
        Guid localShopId,
        string tikTokProductId,
        long? recordFourSellerShopId,
        IReadOnlyCollection<Guid> claimed,
        IReadOnlyCollection<long> incomingIds) =>
        candidates
            .Where(l => (l.ShopId == localShopId || (l.ShopId is null && l.OwnerId == ownerId))
                        && string.Equals(l.TikTokProductId?.Trim(), tikTokProductId, StringComparison.Ordinal)
                        && !claimed.Contains(l.Id)
                        // A row an incoming record still owns must never be stolen.
                        && !incomingIds.Contains(l.FourSellerListingId)
                        // Same 4Seller shop connection means 4Seller itself holds two listings for
                        // one product — a real duplicate for the collapse pass, not a rename.
                        && l.FourSellerShopId != recordFourSellerShopId)
            .OrderByDescending(l => l.ShopId == localShopId)
            .ThenByDescending(l => !string.IsNullOrWhiteSpace(l.RawPayload))
            .ThenByDescending(l => !string.IsNullOrWhiteSpace(l.SourceUrl))
            .ThenByDescending(l => l.VariationCount)
            .ThenByDescending(l => l.UpdatedAt ?? l.CreatedAt)
            .FirstOrDefault();

    /// <summary>
    /// Stamps <see cref="Listing.UpdatedAt"/> / <see cref="Listing.LastSyncedAt"/> and variation
    /// <see cref="ListingVariation.UpdatedAt"/>. For existing listings only new/modified variations
    /// are stamped (via change-tracker state) so untouched rows stay clean.
    /// </summary>
    private void StampListingTimestamps(Listing listing, DateTime now, bool stampAllVariations)
    {
        listing.UpdatedAt = now;
        listing.LastSyncedAt = now;

        foreach (var variation in listing.Variations)
        {
            if (stampAllVariations)
            {
                variation.UpdatedAt = now;
                continue;
            }

            var state = _db.Entry(variation).State;
            if (state == EntityState.Added || state == EntityState.Modified)
            {
                variation.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Persists upsert changes; on concurrency (e.g. variation already deleted) refresh originals and retry.
    /// </summary>
    private async Task SaveUpsertChangesAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Listing upsert concurrency conflict on attempt {Attempt}/{Max}; resolving and retrying",
                    attempt,
                    maxAttempts);

                foreach (var entry in ex.Entries)
                {
                    var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                    if (databaseValues is null)
                    {
                        // Row missing in DB. Deleted → drop. Modified → re-insert (new SKU /
                        // ghost client id after a prior wipe). Added should not hit this path.
                        if (entry.State == EntityState.Deleted)
                        {
                            entry.State = EntityState.Detached;
                        }
                        else if (entry.State == EntityState.Modified)
                        {
                            entry.State = EntityState.Added;
                        }
                        else
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                    else
                    {
                        // Client wins: keep current values, refresh original tokens / row version.
                        entry.OriginalValues.SetValues(databaseValues);
                    }
                }
            }
        }
    }

    /// <summary>
    /// When TikTok reports Active, do not keep in-flight publish UI states.
    /// </summary>
    internal static string ResolvePublishStatusForActiveListing(
        FourSellerListingRecordDto record,
        string? currentPublishStatus)
    {
        var remote = PublishStatuses.Normalize(record.PublishStatus);
        if (remote is PublishStatuses.Publishing or PublishStatuses.UnderReview)
        {
            return "Active";
        }

        if (!string.IsNullOrWhiteSpace(remote))
        {
            return remote;
        }

        return PublishStatuses.IsInFlight(currentPublishStatus) ? "Active" : currentPublishStatus ?? "Active";
    }

    /// <summary>
    /// Published status-only sync: updates marketplace state without touching listing content.
    /// Ownership is stamped only when the listing belongs to a shop of the syncing seller
    /// (see <see cref="CanStampListingOwner"/>).
    /// </summary>
    internal static void ApplyStatusRecord(
        Listing listing,
        FourSellerListingRecordDto record,
        Guid ownerId,
        string platform,
        string pageType,
        string siteStatus,
        Guid? localShopId,
        bool seedListCardFields = false,
        IReadOnlyCollection<Guid>? myShopIds = null)
    {
        var priorPageType = listing.PageType;
        var priorSiteStatus = listing.SiteStatus;
        var priorPublishStatus = listing.PublishStatus;

        StampListingOwnership(listing, ownerId, localShopId, myShopIds);
        listing.Platform = platform;
        listing.PageType = pageType;
        listing.SiteStatus = siteStatus;
        listing.FourSellerShopId = record.ShopId;

        if (seedListCardFields)
        {
            listing.ProductName = record.ProductName;
            listing.MainImage = FirstImage(record.MainImage);
            listing.Currency = string.IsNullOrWhiteSpace(record.Currency) ? listing.Currency : record.Currency!;
            listing.LowPrice = record.LowPrice;
            listing.HighPrice = record.HighPrice;
            listing.AvailableStock = record.AvailableStock;
            listing.VariationCount = record.VariationCount > 0
                ? record.VariationCount
                : record.VariationList.Count;
            if (!string.IsNullOrWhiteSpace(record.CategoryName))
            {
                listing.CategoryName = record.CategoryName;
            }
        }

        ApplyPublishStatusFields(
            listing,
            record,
            pageType,
            siteStatus,
            priorPageType,
            priorSiteStatus,
            priorPublishStatus);

        if (!string.IsNullOrWhiteSpace(record.MarketplaceProductId))
        {
            listing.TikTokProductId = record.MarketplaceProductId.Trim();
        }

        listing.PlatformUpdateTime = ParseFourSellerTime(record.UpdateTime) ?? listing.PlatformUpdateTime;
        listing.LastSyncedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Shared publish / erp / optimistic-pending branches for full and status-only apply.
    /// </summary>
    private static void ApplyPublishStatusFields(
        Listing listing,
        FourSellerListingRecordDto record,
        string pageType,
        string siteStatus,
        string? priorPageType,
        string? priorSiteStatus,
        string? priorPublishStatus)
    {
        listing.ErpStatus = record.ErpStatus ?? listing.ErpStatus;
        listing.PublishStatus = PublishStatuses.Normalize(record.PublishStatus)
            ?? PublishStatuses.Normalize(listing.PublishStatus)
            ?? listing.PublishStatus;

        if (record.ProductStatus == FourSellerProductStatuses.Reviewing)
        {
            listing.PublishStatus = PublishStatuses.UnderReview;
        }
        else if (record.ProductStatus == FourSellerProductStatuses.Failure &&
                 string.Equals(siteStatus, ListingSiteStatuses.Reject, StringComparison.OrdinalIgnoreCase))
        {
            listing.PublishStatus = PublishStatuses.Failed;
        }
        else if (IsRemotePublishBlocked(record))
        {
            listing.PublishStatus = HasPublishAttemptEvidence(listing, priorPublishStatus, priorSiteStatus)
                ? PublishStatuses.Failed
                : PublishStatuses.Draft;
        }
        else if (record.ProductStatus == FourSellerProductStatuses.Active
                 && string.Equals(siteStatus, ListingSiteStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            listing.PublishStatus = ResolvePublishStatusForActiveListing(record, listing.PublishStatus);
        }
        else if (PublishStatuses.Normalize(record.PublishStatus) == PublishStatuses.Publishing)
        {
            listing.PublishStatus = PublishStatuses.Publishing;
        }
        else if (!HasPublishAttemptEvidence(listing, priorPublishStatus, priorSiteStatus)
                 && PublishStatuses.Normalize(listing.PublishStatus) == PublishStatuses.Failed)
        {
            listing.PublishStatus = PublishStatuses.Draft;
        }

        ApplySyncErrorFromRecord(listing, record);

        if (ShouldHoldOptimisticPending(listing, record, pageType, siteStatus))
        {
            listing.PageType = ListingPageTypes.Published;
            listing.SiteStatus = ListingSiteStatuses.Pending;
            listing.PublishStatus = PublishStatuses.Normalize(listing.PublishStatus) == PublishStatuses.Publishing
                ? PublishStatuses.Publishing
                : PublishStatuses.UnderReview;
        }
        else if (PublishStatuses.Normalize(record.PublishStatus) == PublishStatuses.Publishing
                 && !IsRemotePublishBlocked(record)
                 && record.ProductStatus != FourSellerProductStatuses.Failure
                 && record.ProductStatus != FourSellerProductStatuses.Active)
        {
            listing.PublishStatus = PublishStatuses.Publishing;
            if (string.Equals(pageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase)
                && string.Equals(siteStatus, ListingSiteStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                listing.PageType = ListingPageTypes.Published;
                listing.SiteStatus = ListingSiteStatuses.Pending;
            }
        }

        TrySkipAutoPublishAfterSilentDemote(
            listing,
            record,
            priorPageType,
            priorSiteStatus,
            priorPublishStatus);
    }

    /// <summary>
    /// After an auto publish “success” that never entered review, sync demotes the row back to
    /// draft — skip it so FIFO moves to the next listing (retry API clears the flag).
    /// </summary>
    internal static void TrySkipAutoPublishAfterSilentDemote(
        Listing listing,
        FourSellerListingRecordDto record,
        string? priorPageType,
        string? priorSiteStatus,
        string? priorPublishStatus)
    {
        if (listing.AutoPublishSkipped)
        {
            return;
        }

        if (!string.Equals(listing.PublishSource, ListingPublishSources.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = priorPageType;

        if (!IsDraftPageBucket(listing.PageType, listing.SiteStatus))
        {
            return;
        }

        var wasOptimistic =
            PublishStatuses.IsInFlight(priorPublishStatus)
            || string.Equals(priorSiteStatus, ListingSiteStatuses.Pending, StringComparison.OrdinalIgnoreCase);

        if (!wasOptimistic)
        {
            return;
        }

        // Only treat as silent fail when 4Seller still shows draft/blocked — not when product
        // merely omitted status on a non-draft row (wasOptimistic already requires pending/in-flight).
        var silentFail =
            IsRemotePublishBlocked(record)
            || PublishStatuses.Normalize(listing.PublishStatus) == PublishStatuses.Failed
            || (IsDraftPageBucket(listing.PageType, listing.SiteStatus)
                && record.ProductStatus is FourSellerProductStatuses.Draft or null);

        if (!silentFail)
        {
            return;
        }

        var reason = !string.IsNullOrWhiteSpace(listing.SyncError)
            ? listing.SyncError.Trim()
            : BuildRemotePublishBlockedMessage(record);

        listing.AutoPublishSkipped = true;
        listing.AutoPublishSkippedAt = DateTime.UtcNow;
        listing.AutoPublishSkipReason = Truncate(
            string.IsNullOrWhiteSpace(reason)
                ? "Publish ảo — 4Seller vẫn draft."
                : reason,
            2000);
    }

    private static bool IsDraftPageBucket(string? pageType, string? siteStatus) =>
        string.Equals(pageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase)
        && string.Equals(siteStatus, ListingSiteStatuses.Draft, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when sync may set <see cref="Listing.OwnerId"/> to the cookie seller.
    /// Prevents a previous shop owner from reclaiming rows after admin transfer.
    /// When <paramref name="myShopIds"/> is null (detail refresh / unit tests), keep legacy stamp behaviour.
    /// </summary>
    internal static bool CanStampListingOwner(
        Listing listing,
        Guid ownerId,
        Guid? localShopId,
        IReadOnlyCollection<Guid>? myShopIds)
    {
        if (localShopId.HasValue)
        {
            return true;
        }

        if (myShopIds is null)
        {
            return true;
        }

        if (listing.ShopId is Guid shopId)
        {
            return myShopIds.Contains(shopId);
        }

        // Orphan (no ShopId): allow the syncer to claim.
        return true;
    }

    private static void StampListingOwnership(
        Listing listing,
        Guid ownerId,
        Guid? localShopId,
        IReadOnlyCollection<Guid>? myShopIds)
    {
        if (!CanStampListingOwner(listing, ownerId, localShopId, myShopIds))
        {
            return;
        }

        listing.OwnerId = ownerId;
        if (localShopId.HasValue)
        {
            listing.ShopId = localShopId;
        }
    }

    /// <summary>
    /// Skip gate for published status-only rows — compares only status fields.
    /// Forces apply when ownership can be reclaimed onto the syncer's shop.
    /// </summary>
    internal static string? SkipStatusBlockReason(
        Listing listing,
        FourSellerListingRecordDto record,
        Guid ownerId,
        string pageType,
        string siteStatus,
        Guid? localShopId = null,
        IReadOnlyCollection<Guid>? myShopIds = null)
    {
        if (PublishStatuses.IsInFlight(listing.PublishStatus) && !HoldsOptimisticPending(listing))
        {
            return "stale-in-flight";
        }

        var incomingUpdateTime = ParseFourSellerTime(record.UpdateTime);
        if (incomingUpdateTime is null)
        {
            return "no-update-time";
        }

        if (listing.PlatformUpdateTime != incomingUpdateTime)
        {
            return "update-time";
        }

        if (listing.OwnerId != ownerId
            && CanStampListingOwner(listing, ownerId, localShopId, myShopIds))
        {
            return "scope-owner";
        }

        var (effectivePageType, effectiveSiteStatus, effectivePublishStatus) =
            ResolveEffectiveStatuses(listing, record, pageType, siteStatus);
        if (!SameText(listing.PageType, effectivePageType))
        {
            return "page-type";
        }

        if (!SameText(listing.SiteStatus, effectiveSiteStatus))
        {
            return "site-status";
        }

        if (!SameText(listing.PublishStatus, effectivePublishStatus))
        {
            return "publish-status";
        }

        if (!SameText(listing.ErpStatus, record.ErpStatus ?? listing.ErpStatus))
        {
            return "erp-status";
        }

        var incomingSyncError = string.IsNullOrWhiteSpace(record.FailedMessage)
            ? null
            : Truncate(record.FailedMessage.Trim(), 2000);
        if (!SameText(listing.SyncError, incomingSyncError))
        {
            return "sync-error";
        }

        if (!string.IsNullOrWhiteSpace(record.MarketplaceProductId) &&
            !SameText(listing.TikTokProductId, record.MarketplaceProductId.Trim()))
        {
            return "tiktok-product-id";
        }

        return null;
    }

    /// <summary>
    /// Returns <c>null</c> when <see cref="ApplyRecord"/> and <see cref="SyncVariations"/> would both
    /// be no-ops for this row, so the sync can skip it entirely instead of rewriting every variation.
    /// Otherwise returns a short reason, which the page log surfaces when nothing gets skipped.
    /// <para>
    /// The gate is 4Seller's own <c>updateTime</c>: it only moves when 4Seller actually changed the
    /// row. Everything else here is a completeness guard so a half-filled local mirror (missing
    /// RawPayload / description) or a status transition is never skipped.
    /// </para>
    /// </summary>
    private static string? SkipBlockReason(
        Listing listing,
        FourSellerListingRecordDto record,
        Guid ownerId,
        string platform,
        string pageType,
        string siteStatus,
        Guid? localShopId,
        IReadOnlyCollection<Guid>? myShopIds = null)
    {
        if (PublishStatuses.IsInFlight(listing.PublishStatus) && !HoldsOptimisticPending(listing))
        {
            return "stale-in-flight";
        }

        var incomingUpdateTime = ParseFourSellerTime(record.UpdateTime);
        if (incomingUpdateTime is null)
        {
            return "no-update-time";
        }

        if (listing.PlatformUpdateTime != incomingUpdateTime)
        {
            return "update-time";
        }

        // A mirror without RawPayload cannot push save-draft/publish — let it fill.
        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return "raw-payload";
        }

        if (listing.OwnerId != ownerId
            && CanStampListingOwner(listing, ownerId, localShopId, myShopIds))
        {
            return "scope-owner";
        }

        if (!SameText(listing.Platform, platform))
        {
            return "scope-platform";
        }

        if (listing.FourSellerShopId != record.ShopId ||
            (localShopId.HasValue && listing.ShopId != localShopId))
        {
            return "shop";
        }

        // Status is not a plain copy: reviewing/failure override PublishStatus, and a listing that
        // is already awaiting review stays Published/Pending while 4Seller still lists it as a
        // draft. Reproduce that outcome and compare, otherwise every awaiting-review row (the bulk
        // of the draft page) would look changed forever.
        var (effectivePageType, effectiveSiteStatus, effectivePublishStatus) =
            ResolveEffectiveStatuses(listing, record, pageType, siteStatus);
        if (!SameText(listing.PageType, effectivePageType))
        {
            return "page-type";
        }

        if (!SameText(listing.SiteStatus, effectiveSiteStatus))
        {
            return "site-status";
        }

        if (!SameText(listing.PublishStatus, effectivePublishStatus))
        {
            return "publish-status";
        }

        if (!SameText(listing.ErpStatus, record.ErpStatus ?? listing.ErpStatus))
        {
            return "erp-status";
        }

        if (!KeepsValue(record.SourceUrl, listing.SourceUrl))
        {
            return "source-url";
        }

        if (!KeepsValue(record.CategoryName, listing.CategoryName))
        {
            return "category-name";
        }

        if (!KeepsValue(record.Currency, listing.Currency))
        {
            return "currency";
        }

        var incomingSyncError = string.IsNullOrWhiteSpace(record.FailedMessage)
            ? null
            : Truncate(record.FailedMessage.Trim(), 2000);
        if (!SameText(listing.SyncError, incomingSyncError))
        {
            return "sync-error";
        }

        if (!string.IsNullOrWhiteSpace(record.MarketplaceProductId) &&
            !SameText(listing.TikTokProductId, record.MarketplaceProductId.Trim()))
        {
            return "tiktok-product-id";
        }

        // Light sync fills these once from batch-detail; an empty slot still needs that fill.
        if (!string.IsNullOrWhiteSpace(record.DescriptionHtml) &&
            (string.IsNullOrWhiteSpace(listing.SupplierDescriptionHtml) ||
             string.IsNullOrWhiteSpace(listing.DescriptionHtml)))
        {
            return "description-fill";
        }

        // ApplyRecord keeps a locally rewritten title, but still backfills an empty one.
        var preserveLocalTitle =
            (listing.DescriptionRewritten || listing.DescriptionImagesInserted)
            && !string.IsNullOrWhiteSpace(listing.DescriptionHtml)
            && !string.IsNullOrWhiteSpace(listing.ProductName);
        if (!preserveLocalTitle && !SameText(listing.ProductName, record.ProductName))
        {
            return "product-name";
        }

        if (!SameText(listing.MainImage, FirstImage(record.MainImage)))
        {
            return "main-image";
        }

        if (!SameText(listing.Site, record.Site) ||
            !SameText(listing.CategoryId, record.CategoryId) ||
            !SameText(listing.WarehouseId, record.WarehouseId))
        {
            return "site-category-warehouse";
        }

        if (record.PackageWeight is not null && listing.PackageWeight != record.PackageWeight ||
            record.PackageLength is not null && listing.PackageLength != record.PackageLength ||
            record.PackageWidth is not null && listing.PackageWidth != record.PackageWidth ||
            record.PackageHeight is not null && listing.PackageHeight != record.PackageHeight ||
            !SameText(listing.WeightUnit, record.WeightUnit ?? listing.WeightUnit) ||
            !SameText(listing.DimensionUnit, record.DimensionUnit ?? listing.DimensionUnit))
        {
            return "package";
        }

        var variationReason = VariationsBlockReason(listing, record.VariationList, pageType);
        if (variationReason is not null)
        {
            return variationReason;
        }

        return DerivedTotalsMatch(listing, record) ? null : "derived-totals";
    }

    /// <summary>
    /// Whether the optimistic Published/Pending set by a Publish push may still override what 4Seller
    /// reports.
    ///
    /// The override exists only to bridge the gap between "4Seller accepted the publish" and "4Seller
    /// moved the row out of the draft bucket", which takes minutes. Holding it forever turned a publish
    /// that silently never took into a listing stuck on Reviewing with a spinner for weeks, because
    /// every later sync saw the row back in draft and dutifully restored UnderReview. Past the window
    /// 4Seller is authoritative. A row with no recorded push gets no override at all — there is nothing
    /// to bridge from.
    /// </summary>
    private static bool HoldsOptimisticPending(Listing listing) =>
        listing.LastPushAt is { } pushedAt
        && DateTime.UtcNow - pushedAt <= OptimisticPendingWindow;

    /// <summary>
    /// Whether this listing has already been pushed / was in-flight — only then may remote
    /// <c>unpublishable</c> / <c>failedMessage</c> map to local <see cref="PublishStatuses.Failed"/>.
    /// Raw extension drafts often carry require-fields messages before any publish attempt.
    /// </summary>
    internal static bool HasPublishAttemptEvidence(
        Listing listing,
        string? priorPublishStatus,
        string? priorSiteStatus) =>
        listing.LastPushAt is not null
        || PublishStatuses.IsInFlight(priorPublishStatus)
        || string.Equals(priorSiteStatus, ListingSiteStatuses.Pending, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 4Seller signals that a publish attempt did not enter TikTok review — still a draft row with
    /// blocked publish metadata (<c>unpublishable</c>, <c>update_failed</c>, …).
    /// </summary>
    private static bool IsRemotePublishBlocked(FourSellerListingRecordDto record) =>
        !string.IsNullOrWhiteSpace(record.FailedMessage)
        || IsPublishBlockedStatus(record.PublishStatus)
        || IsPublishBlockedStatus(record.ErpStatus);

    private static bool IsPublishBlockedStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "unpublishable"
            or "update_failed"
            or "publish_failed"
            or "failed";
    }

    /// <summary>
    /// Whether a local UnderReview row may keep optimistic Published/Pending while 4Seller still
    /// lists it on the draft page. Only bridges genuine in-flight review — not rows 4Seller left
    /// as Draft / unpublishable after a publish that never took.
    /// </summary>
    private static bool ShouldHoldOptimisticPending(
        Listing listing,
        FourSellerListingRecordDto record,
        string pageType,
        string siteStatus)
    {
        if (!PublishStatuses.IsInFlight(listing.PublishStatus))
        {
            return false;
        }

        if (!HoldsOptimisticPending(listing))
        {
            return false;
        }

        if (!string.Equals(pageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(siteStatus, ListingSiteStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsRemotePublishBlocked(record))
        {
            return false;
        }

        if (record.ProductStatus == FourSellerProductStatuses.Failure)
        {
            return false;
        }

        var lastPushAt = listing.LastPushAt;
        var withinGrace = lastPushAt is { } pushedAt
            && DateTime.UtcNow - pushedAt <= OptimisticPendingGracePeriod;

        // productStatus=1 = TikTok vẫn coi là draft. Chỉ bridge ngắn khi không có dấu hiệu
        // publish bị chặn; unpublishable / failedMessage → hạ ngay (publish không vào review).
        if (record.ProductStatus == FourSellerProductStatuses.Draft)
        {
            return withinGrace;
        }

        if (record.ProductStatus == FourSellerProductStatuses.Reviewing)
        {
            return true;
        }

        // Page row may omit productStatus briefly after publish — bridge only within grace.
        if (record.ProductStatus is null)
        {
            return withinGrace;
        }

        return false;
    }

    private static string BuildRemotePublishBlockedMessage(FourSellerListingRecordDto record)
    {
        if (!string.IsNullOrWhiteSpace(record.FailedMessage))
        {
            return Truncate(record.FailedMessage.Trim(), 2000);
        }

        var hint = record.PublishStatus ?? record.ErpStatus ?? "blocked";
        return Truncate(
            $"Listing vẫn ở Draft — 4Seller báo publishStatus={hint} nhưng không trả lý do chi tiết. " +
            "Kiểm tra Specifics, ảnh variant, Size Chart trên 4Seller rồi Publish lại.",
            2000);
    }

    /// <summary>
    /// System-generated placeholder when 4Seller reports blocked publish without <c>failedMessage</c>.
    /// </summary>
    internal static bool IsSystemPublishBlockedFallback(string? syncError)
    {
        if (string.IsNullOrWhiteSpace(syncError))
        {
            return false;
        }

        var trimmed = syncError.Trim();
        return trimmed.Contains("4Seller vẫn ở draft (", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("publishStatus=", StringComparison.OrdinalIgnoreCase)
                   && trimmed.Contains("không trả lý do chi tiết", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplySyncErrorFromRecord(Listing listing, FourSellerListingRecordDto record)
    {
        var previousSyncError = listing.SyncError;

        if (!string.IsNullOrWhiteSpace(record.FailedMessage))
        {
            listing.SyncError = Truncate(record.FailedMessage.Trim(), 2000);
            return;
        }

        var publishBlocked = listing.PublishStatus == PublishStatuses.Failed
            || IsRemotePublishBlocked(record);
        if (!publishBlocked)
        {
            listing.SyncError = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousSyncError)
            && !IsSystemPublishBlockedFallback(previousSyncError))
        {
            listing.SyncError = previousSyncError;
            return;
        }

        listing.SyncError = BuildRemotePublishBlockedMessage(record);
    }

    /// <summary>
    /// Mirrors the status branches at the top and bottom of <see cref="ApplyRecord"/> so the skip
    /// check compares against the value that would actually be stored, not the raw record value.
    /// </summary>
    internal static (string PageType, string? SiteStatus, string? PublishStatus) ResolveEffectiveStatuses(
        Listing listing,
        FourSellerListingRecordDto record,
        string pageType,
        string siteStatus)
    {
        var publishStatus = PublishStatuses.Normalize(record.PublishStatus)
            ?? PublishStatuses.Normalize(listing.PublishStatus)
            ?? listing.PublishStatus;
        if (record.ProductStatus == FourSellerProductStatuses.Reviewing)
        {
            publishStatus = PublishStatuses.UnderReview;
        }
        else if (record.ProductStatus == FourSellerProductStatuses.Failure &&
                 string.Equals(siteStatus, ListingSiteStatuses.Reject, StringComparison.OrdinalIgnoreCase))
        {
            publishStatus = PublishStatuses.Failed;
        }
        else if (IsRemotePublishBlocked(record))
        {
            publishStatus = HasPublishAttemptEvidence(listing, listing.PublishStatus, listing.SiteStatus)
                ? PublishStatuses.Failed
                : PublishStatuses.Draft;
        }
        else if (record.ProductStatus == FourSellerProductStatuses.Active
                 && string.Equals(siteStatus, ListingSiteStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            publishStatus = ResolvePublishStatusForActiveListing(record, publishStatus);
        }
        else if (PublishStatuses.Normalize(record.PublishStatus) == PublishStatuses.Publishing)
        {
            publishStatus = PublishStatuses.Publishing;
        }
        else if (!HasPublishAttemptEvidence(listing, listing.PublishStatus, listing.SiteStatus)
                 && PublishStatuses.Normalize(publishStatus) == PublishStatuses.Failed)
        {
            publishStatus = PublishStatuses.Draft;
        }

        if (ShouldHoldOptimisticPending(listing, record, pageType, siteStatus))
        {
            var heldPublish = PublishStatuses.Normalize(listing.PublishStatus) == PublishStatuses.Publishing
                ? PublishStatuses.Publishing
                : PublishStatuses.UnderReview;
            return (ListingPageTypes.Published, ListingSiteStatuses.Pending, heldPublish);
        }

        if (PublishStatuses.Normalize(record.PublishStatus) == PublishStatuses.Publishing
            && !IsRemotePublishBlocked(record)
            && record.ProductStatus != FourSellerProductStatuses.Failure
            && record.ProductStatus != FourSellerProductStatuses.Active)
        {
            var effectivePageType = pageType;
            var effectiveSiteStatus = siteStatus;
            if (string.Equals(pageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase)
                && string.Equals(siteStatus, ListingSiteStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                effectivePageType = ListingPageTypes.Published;
                effectiveSiteStatus = ListingSiteStatuses.Pending;
            }

            return (effectivePageType, effectiveSiteStatus, PublishStatuses.Publishing);
        }

        return (pageType, siteStatus, publishStatus);
    }

    /// <summary>
    /// Price / stock / variation count are not taken from the record as-is: SyncVariations recomputes
    /// them from the local variations via RecalcPriceStock. Compare against that derived result,
    /// otherwise a draft (whose SalePrice is calculated locally) never looks unchanged.
    /// </summary>
    private static bool DerivedTotalsMatch(Listing listing, FourSellerListingRecordDto record)
    {
        if (record.VariationList.Count == 0)
        {
            // RecalcPriceStock never runs — ApplyRecord's raw assignments stand.
            var rawCount = record.VariationCount > 0 ? record.VariationCount : 0;
            return listing.LowPrice == record.LowPrice
                   && listing.HighPrice == record.HighPrice
                   && listing.AvailableStock == record.AvailableStock
                   && listing.VariationCount == rawCount;
        }

        if (listing.VariationCount != listing.Variations.Count)
        {
            return false;
        }

        var salePrices = listing.Variations
            .Where(v => v.SalePrice.HasValue)
            .Select(v => v.SalePrice!.Value)
            .ToList();
        if (salePrices.Count > 0 &&
            (listing.LowPrice != salePrices.Min() || listing.HighPrice != salePrices.Max()))
        {
            return false;
        }

        var quantities = listing.Variations
            .Where(v => v.Quantity.HasValue)
            .Select(v => v.Quantity!.Value)
            .ToList();
        var expectedStock = quantities.Count > 0 ? SumQuantitiesClamped(quantities) : record.AvailableStock;
        return listing.AvailableStock == expectedStock;
    }

    /// <summary>
    /// Cheap structural/price check so a stock or SKU change is never skipped even if 4Seller
    /// failed to move <c>updateTime</c>.
    /// </summary>
    private static string? VariationsBlockReason(
        Listing listing,
        IReadOnlyList<FourSellerListingVariationDto> variations,
        string pageType)
    {
        if (variations.Count == 0)
        {
            // SyncVariations returns early on an empty list — nothing would change.
            return null;
        }

        // 4Seller often returns duplicate Color|Size rows (different skuIds). SyncVariations keeps
        // one per combo, so compare against the unique combo set — not the raw list length.
        var uniqueIncoming = DedupedIncomingVariations(variations).ToList();
        if (listing.Variations.Count != uniqueIncoming.Count)
        {
            return "variation-count";
        }

        var bySkuId = new Dictionary<long, ListingVariation>();
        foreach (var existing in listing.Variations)
        {
            if (existing.FourSellerSkuId is > 0)
            {
                bySkuId[existing.FourSellerSkuId.Value] = existing;
            }
        }

        // Drafts keep a locally calculated SalePrice; published rows take it from 4Seller.
        var isDraft = string.Equals(pageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase);

        foreach (var incoming in uniqueIncoming)
        {
            if (incoming.Id <= 0 || !bySkuId.TryGetValue(incoming.Id, out var match))
            {
                // Combo may already be local under a different skuId after a prior dedupe pass —
                // fall back to combo match before declaring a structural change.
                var incomingCombo = VariationComboKey(
                    ResolveVariationAxisAttrs(incoming.Title, BuildVariantAttributesJson(incoming.Title)));
                match = incomingCombo is null
                    ? null
                    : listing.Variations.FirstOrDefault(v =>
                        VariationComboKey(ResolveVariationAxisAttrs(v.Title, v.VariantAttributes)) ==
                        incomingCombo);
                if (match is null)
                {
                    return "variation-sku";
                }
            }

            if (match.Quantity != incoming.AvailableStock)
            {
                return "variation-stock";
            }

            if (match.GlobalPrice != incoming.GlobalPrice)
            {
                return "variation-price";
            }

            if (!SameText(match.SellerSku, incoming.SellerSku) ||
                !SameText(match.Title, incoming.Title))
            {
                return "variation-text";
            }

            if (!isDraft && match.SalePrice != (incoming.OriginalPrice ?? incoming.GlobalPrice))
            {
                return "variation-sale-price";
            }
        }

        return null;
    }

    /// <summary>True when an incoming blank leaves the stored value untouched, or both are equal.</summary>
    private static bool KeepsValue(string? incoming, string? stored) =>
        string.IsNullOrWhiteSpace(incoming) || SameText(stored, incoming);

    private static bool SameText(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private static string FormatUtc(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static void ApplyRecord(
        Listing listing,
        FourSellerListingRecordDto record,
        Guid ownerId,
        string platform,
        string pageType,
        string siteStatus,
        Guid? localShopId,
        bool lightMode = false,
        bool allowRawPayloadOverwrite = false,
        IReadOnlyCollection<Guid>? myShopIds = null)
    {
        var priorPageType = listing.PageType;
        var priorSiteStatus = listing.SiteStatus;
        var priorPublishStatus = listing.PublishStatus;

        StampListingOwnership(listing, ownerId, localShopId, myShopIds);
        listing.Platform = platform;
        listing.PageType = pageType;
        listing.SiteStatus = siteStatus;
        listing.FourSellerShopId = record.ShopId;

        // Local AI prep (Rewrite / Insert Images) wins over 4Seller until pushed —
        // avoid wiping token-cost work on refresh/bulk sync.
        var preserveLocalDescriptionPrep =
            (listing.DescriptionRewritten || listing.DescriptionImagesInserted)
            && !string.IsNullOrWhiteSpace(listing.DescriptionHtml);

        // The AI SEO title comes out of the same Rewrite as the description, so it has to
        // survive a refresh for exactly as long as that description does.
        if (!preserveLocalDescriptionPrep || string.IsNullOrWhiteSpace(listing.ProductName))
        {
            listing.ProductName = record.ProductName;
        }

        listing.MainImage = FirstImage(record.MainImage);
        listing.Site = record.Site;
        listing.Currency = string.IsNullOrWhiteSpace(record.Currency) ? listing.Currency : record.Currency!;
        listing.LowPrice = record.LowPrice;
        listing.HighPrice = record.HighPrice;
        listing.AvailableStock = record.AvailableStock;
        listing.VariationCount = record.VariationCount > 0
            ? record.VariationCount
            : record.VariationList.Count;
        listing.CategoryId = record.CategoryId;
        // The light listing page omits categoryName while detail returns it — assigning blindly
        // wiped the stored name on every background sync.
        if (!string.IsNullOrWhiteSpace(record.CategoryName))
        {
            listing.CategoryName = record.CategoryName;
        }

        listing.WarehouseId = record.WarehouseId;
        // 4Seller page/detail often omits sourceUrl on fresh drafts — do not wipe the key
        // needed to attach SupplierProductInfo / reviews after post-html.
        if (!string.IsNullOrWhiteSpace(record.SourceUrl))
        {
            listing.SourceUrl = record.SourceUrl;
        }
        // Capture the supplier's own copy before any of the preserve logic below, and only
        // while the slot is still empty. Auto Specifics parses the Key: Value rows out of it,
        // and a rewrite replaces those rows in DescriptionHtml.
        if (string.IsNullOrWhiteSpace(listing.SupplierDescriptionHtml) &&
            !string.IsNullOrWhiteSpace(record.DescriptionHtml))
        {
            listing.SupplierDescriptionHtml = record.DescriptionHtml;
        }

        // Light: never overwrite an existing local description (list sync does not need it).
        // Full: existing preserveLocalDescriptionPrep rules.
        if (!lightMode)
        {
            if (!preserveLocalDescriptionPrep &&
                !string.IsNullOrWhiteSpace(record.DescriptionHtml))
            {
                var incoming = record.DescriptionHtml;
                if (!string.Equals(listing.DescriptionHtml, incoming, StringComparison.Ordinal))
                {
                    // Remote description replaced local copy — invalidate Publish checklist.
                    listing.DescriptionRewritten = false;
                    listing.DescriptionImagesInserted = false;
                }

                listing.DescriptionHtml = incoming;
            }
        }
        else if (string.IsNullOrWhiteSpace(listing.DescriptionHtml) &&
                 !string.IsNullOrWhiteSpace(record.DescriptionHtml))
        {
            listing.DescriptionHtml = record.DescriptionHtml;
        }

        listing.PackageWeight = record.PackageWeight ?? listing.PackageWeight;
        listing.PackageLength = record.PackageLength ?? listing.PackageLength;
        listing.PackageWidth = record.PackageWidth ?? listing.PackageWidth;
        listing.PackageHeight = record.PackageHeight ?? listing.PackageHeight;
        listing.WeightUnit = record.WeightUnit ?? listing.WeightUnit;
        listing.DimensionUnit = record.DimensionUnit ?? listing.DimensionUnit;
        ApplyPublishStatusFields(
            listing,
            record,
            pageType,
            siteStatus,
            priorPageType,
            priorSiteStatus,
            priorPublishStatus);

        if (!string.IsNullOrWhiteSpace(record.MarketplaceProductId))
        {
            // 4Seller field `productId` → local TikTokProductId (used by sync-one-local-listing).
            listing.TikTokProductId = record.MarketplaceProductId.Trim();
        }

        listing.PlatformCreateTime = ParseFourSellerTime(record.CreateTime) ?? listing.PlatformCreateTime;
        listing.PlatformUpdateTime = ParseFourSellerTime(record.UpdateTime) ?? listing.PlatformUpdateTime;

        // 4Seller only holds the original snapshot: listings are created there, pulled down once,
        // then edited locally and pushed back. RawPayload is also where local prep lives (rewritten
        // description, inserted images), so sync fills it once and never overwrites it — otherwise
        // every run rewrites a large jsonb column and can clobber local work. The explicit
        // "refresh from 4Seller" action opts back in.
        var hadRawPayload = !string.IsNullOrWhiteSpace(listing.RawPayload);
        if (!string.IsNullOrWhiteSpace(record.RawDetailJson)
            && (allowRawPayloadOverwrite || !hadRawPayload))
        {
            listing.RawPayload = record.RawDetailJson;
        }

        // Normalize media on explicit refresh, or on the first fill that just got RawPayload.
        if (allowRawPayloadOverwrite || (!hadRawPayload && !string.IsNullOrWhiteSpace(listing.RawPayload)))
        {
            NormalizeListingMediaIntoRawPayload(listing, record);
        }

        FillStringListsFromRawIfEmpty(listing);
    }

    /// <summary>
    /// Writes stable product <c>imageList</c> + <c>skuImgs</c> (<c>color → url[]</c>) into RawPayload.
    /// Merges DetailImageUrls / ColorImageUrls with existing raw maps (never replace with fewer URLs).
    /// </summary>
    private static void NormalizeListingMediaIntoRawPayload(
        Listing listing,
        FourSellerListingRecordDto record)
    {
        JsonObject obj;
        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            obj = new JsonObject();
        }
        else
        {
            try
            {
                obj = JsonNode.Parse(listing.RawPayload) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                obj = new JsonObject();
            }
        }

        var colorMap = BuildNormalizedColorImageMap(record);
        MergeColorUrlsFromPayloadNode(obj, colorMap);

        if (colorMap.Count > 0)
        {
            var skuImgs = new JsonObject();
            foreach (var (color, urls) in colorMap)
            {
                skuImgs[color] = new JsonArray(
                    urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
            }

            obj["skuImgs"] = skuImgs;
            obj["salePropImageMap"] = skuImgs.DeepClone();
        }

        var productUrls = BuildProductGalleryUrls(record, obj);
        if (productUrls.Count > 0)
        {
            obj["mainImage"] = string.Join('|', productUrls);
            // Keep plain-string imageList in sync; never downgrade object-array shapes,
            // and never rewrite `images` (4Seller rows carry ids/metadata required by save-draft).
            if (obj["imageList"] is not JsonArray existingIl ||
                existingIl.Count == 0 ||
                existingIl[0] is JsonValue)
            {
                obj["imageList"] = new JsonArray(
                    productUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
            }
        }

        if (!string.IsNullOrWhiteSpace(listing.DescriptionHtml))
        {
            // After RawPayload replace from 4Seller detail, re-apply local prep HTML when
            // Rewrite/Insert flags are set (or fill empty remote description).
            var preserveLocalDescriptionPrep =
                listing.DescriptionRewritten || listing.DescriptionImagesInserted;
            var existingDesc = GetString(obj, "descriptionHtml") ?? GetString(obj, "description");
            if (preserveLocalDescriptionPrep || string.IsNullOrWhiteSpace(existingDesc))
            {
                obj["descriptionHtml"] = listing.DescriptionHtml;
                obj["description"] = listing.DescriptionHtml;
            }
        }

        ReconcileTypedMediaKeysInRawPayload(obj);
        obj["productType"] = ResolveListingProductType(listing, obj);
        UpgradeHttpUrlsInJsonNode(obj);

        listing.RawPayload = obj.ToJsonString(PatchJsonOptions);
    }

    /// <summary>
    /// Drop blank Video / Size Chart keys from RawPayload so hydrate does not resurrect
    /// typed media 4Seller cleared (empty string still parses as a key).
    /// </summary>
    private static void ReconcileTypedMediaKeysInRawPayload(JsonObject obj)
    {
        var video = FirstNonEmpty(
            GetString(obj, "erpVideoUrl"),
            GetString(obj, "videoUrl"),
            GetString(obj, "productVideo"),
            GetString(obj, "videoPostUrl"));
        if (video is null)
        {
            ClearStringKeys(
                obj,
                "erpVideoUrl",
                "videoUrl",
                "productVideo",
                "videoPostUrl",
                "erpVideoId",
                "videoId");
        }

        var sizeChart = FirstNonEmpty(
            GetString(obj, "sizeChart"),
            GetString(obj, "sizeChartImage"),
            GetString(obj, "sizeChartUrl"));
        if (sizeChart is null)
        {
            ClearStringKeys(obj, "sizeChart", "sizeChartImage", "sizeChartUrl", "sizeChartId");
        }
    }

    private static List<string> BuildProductGalleryUrls(
        FourSellerListingRecordDto record,
        JsonObject obj)
    {
        const int maxShared = 9;
        var urls = new List<string>();

        void AddRaw(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (var part in raw.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (urls.Count >= maxShared)
                {
                    return;
                }

                var upgraded = UpgradeHttpToHttps(part);
                if (string.IsNullOrWhiteSpace(upgraded))
                {
                    continue;
                }

                if (!urls.Contains(upgraded, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(upgraded);
                }
            }
        }

        AddRaw(record.MainImage);
        foreach (var url in record.DetailImageUrls)
        {
            AddRaw(url);
        }

        AddRaw(GetString(obj, "mainImage"));
        foreach (var key in new[] { "imageList", "images", "photoList", "photos", "detailImages" })
        {
            CollectUrlsFromPayloadNode(obj[key], urls, maxShared);
        }

        return urls;
    }

    private static void CollectUrlsFromPayloadNode(JsonNode? node, List<string> urls, int max)
    {
        if (node is null || urls.Count >= max)
        {
            return;
        }

        switch (node)
        {
            case JsonValue jv when jv.TryGetValue(out string? s):
                if (string.IsNullOrWhiteSpace(s))
                {
                    return;
                }

                s = s.Trim();
                if (s.StartsWith('[') || s.StartsWith('{'))
                {
                    try
                    {
                        CollectUrlsFromPayloadNode(JsonNode.Parse(s), urls, max);
                    }
                    catch (JsonException)
                    {
                        foreach (var part in s.Split(
                                     '|',
                                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (urls.Count >= max)
                            {
                                return;
                            }

                            var upgraded = UpgradeHttpToHttps(part);
                            if (!string.IsNullOrWhiteSpace(upgraded) &&
                                upgraded.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                                !urls.Contains(upgraded, StringComparer.OrdinalIgnoreCase))
                            {
                                urls.Add(upgraded);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var part in s.Split(
                                 '|',
                                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (urls.Count >= max)
                        {
                            return;
                        }

                        var upgraded = UpgradeHttpToHttps(part);
                        if (!string.IsNullOrWhiteSpace(upgraded) &&
                            upgraded.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                            !urls.Contains(upgraded, StringComparer.OrdinalIgnoreCase))
                        {
                            urls.Add(upgraded);
                        }
                    }
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    CollectUrlsFromPayloadNode(item, urls, max);
                    if (urls.Count >= max)
                    {
                        return;
                    }
                }

                break;
            case JsonObject jo:
                CollectUrlsFromPayloadNode(
                    GetString(jo, "url") is { } u1 ? JsonValue.Create(u1) : null,
                    urls,
                    max);
                if (urls.Count >= max)
                {
                    return;
                }

                CollectUrlsFromPayloadNode(
                    GetString(jo, "src") is { } u2 ? JsonValue.Create(u2) : null,
                    urls,
                    max);
                if (urls.Count >= max)
                {
                    return;
                }

                CollectUrlsFromPayloadNode(
                    GetString(jo, "imageUrl") is { } u3 ? JsonValue.Create(u3) : null,
                    urls,
                    max);
                break;
        }
    }

    private static Dictionary<string, List<string>> BuildNormalizedColorImageMap(
        FourSellerListingRecordDto record)
    {
        var urlsByColor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (color, list) in record.ColorImageUrls)
        {
            foreach (var url in list)
            {
                AddColorUrl(urlsByColor, color, url);
            }
        }

        foreach (var v in record.VariationList)
        {
            var color = ColorFromVariationTitle(v.Title);
            foreach (var url in v.ImageUrls)
            {
                AddColorUrl(urlsByColor, color, url);
            }

            if (!string.IsNullOrWhiteSpace(v.MainImage))
            {
                foreach (var part in v.MainImage.Split(
                             '|',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    AddColorUrl(urlsByColor, color, part);
                }
            }
        }

        return urlsByColor;
    }

    private static void AddColorUrl(
        Dictionary<string, List<string>> urlsByColor,
        string? color,
        string? url)
    {
        const int maxPerColor = 9;
        if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        color = color.Trim();
        url = UpgradeHttpToHttps(url.Trim());
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!urlsByColor.TryGetValue(color, out var list))
        {
            list = [];
            urlsByColor[color] = list;
        }

        if (list.Count >= maxPerColor)
        {
            return;
        }

        if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(url);
        }
    }

    private static void MergeColorUrlsFromPayloadNode(
        JsonObject obj,
        Dictionary<string, List<string>> urlsByColor)
    {
        foreach (var mapKey in new[]
                 {
                     "skuImgs", "salePropImageMap", "uploadImageInfoMap",
                     "colorImageMap", "variantImageMap", "skuImageMap", "propertyImageMap"
                 })
        {
            MergeColorUrlsFromNode(obj[mapKey], urlsByColor);
        }
    }

    private static void MergeColorUrlsFromNode(
        JsonNode? node,
        Dictionary<string, List<string>> urlsByColor)
    {
        switch (node)
        {
            case JsonObject map:
                foreach (var (color, child) in map)
                {
                    MergeColorUrlValues(color, child, urlsByColor);
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not JsonObject jo)
                    {
                        continue;
                    }

                    var attrName = GetString(jo, "attrName") ?? GetString(jo, "attr_name") ?? GetString(jo, "name");
                    if (!string.IsNullOrWhiteSpace(attrName) &&
                        !attrName.Contains("color", StringComparison.OrdinalIgnoreCase) &&
                        !attrName.Contains("colour", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var color = GetString(jo, "custom_value")
                                ?? GetString(jo, "customValue")
                                ?? GetString(jo, "attrValue")
                                ?? GetString(jo, "attr_value")
                                ?? GetString(jo, "color")
                                ?? GetString(jo, "colorValue")
                                ?? GetString(jo, "colorName")
                                ?? GetString(jo, "value")
                                ?? GetString(jo, "valueName");
                    if (string.IsNullOrWhiteSpace(color))
                    {
                        continue;
                    }

                    // Full extract (img_url pipe + nested imageList), not only first `url`.
                    MergeColorUrlValues(color, jo, urlsByColor);
                }

                break;
            case JsonValue jv when jv.TryGetValue(out string? raw) && !string.IsNullOrWhiteSpace(raw):
                try
                {
                    MergeColorUrlsFromNode(JsonNode.Parse(raw), urlsByColor);
                }
                catch (JsonException)
                {
                    // ignore
                }

                break;
        }
    }

    private static void MergeColorUrlValues(
        string color,
        JsonNode? node,
        Dictionary<string, List<string>> urlsByColor)
    {
        void AddPipe(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (var part in raw.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddColorUrl(urlsByColor, color, part);
            }
        }

        void AddFromObject(JsonObject jo)
        {
            // Prefer multi-url img_url before single thumbnail url.
            AddPipe(GetString(jo, "img_url") ?? GetString(jo, "imgUrl"));
            foreach (var listKey in new[] { "imageList", "images", "urls", "urlList", "url_list", "photos" })
            {
                if (jo[listKey] is not JsonArray listArr)
                {
                    continue;
                }

                foreach (var item in listArr)
                {
                    switch (item)
                    {
                        case JsonValue jv when jv.TryGetValue(out string? s):
                            AddPipe(s);
                            break;
                        case JsonObject nested:
                            AddPipe(
                                GetString(nested, "img_url")
                                ?? GetString(nested, "imgUrl")
                                ?? GetString(nested, "url")
                                ?? GetString(nested, "src")
                                ?? GetString(nested, "imageUrl"));
                            break;
                    }
                }
            }

            AddPipe(
                GetString(jo, "url")
                ?? GetString(jo, "src")
                ?? GetString(jo, "imageUrl")
                ?? GetString(jo, "uri"));
        }

        switch (node)
        {
            case JsonArray arr:
                foreach (var item in arr)
                {
                    switch (item)
                    {
                        case JsonValue jv when jv.TryGetValue(out string? s):
                            AddPipe(s);
                            break;
                        case JsonObject jo:
                            AddFromObject(jo);
                            break;
                    }
                }

                break;
            case JsonObject nestedMap:
                AddFromObject(nestedMap);
                break;
            case JsonValue single when single.TryGetValue(out string? oneUrl):
                AddPipe(oneUrl);
                break;
        }
    }

    private static string? ColorFromVariationTitle(string? title) =>
        ExtractColorFromTitle(title);

    /// <summary>
    /// Upserts variations in place (match by 4Seller SKU / seller SKU). Avoids collection Clear()
    /// mass-deletes that race into DbUpdateConcurrencyException when rows are already gone.
    /// Returns <c>true</c> when variations were added or removed (structural change invisible to
    /// the change tracker once removed from the collection).
    /// </summary>
    private bool SyncVariations(
        Listing listing,
        IReadOnlyList<FourSellerListingVariationDto> variations,
        DateTime now)
    {
        if (variations.Count == 0)
        {
            return false;
        }

        var structuralChanged = false;
        var existing = listing.Variations.ToList();
        var keepIds = new HashSet<Guid>();
        var usedSellerSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptByCombo = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var v in variations)
        {
            ListingVariation? match = null;
            if (v.Id > 0)
            {
                match = existing.FirstOrDefault(x =>
                    !keepIds.Contains(x.Id) && x.FourSellerSkuId == v.Id);
            }

            if (match is null && !string.IsNullOrWhiteSpace(v.SellerSku))
            {
                match = existing.FirstOrDefault(x =>
                    !keepIds.Contains(x.Id) &&
                    string.Equals(x.SellerSku, v.SellerSku, StringComparison.OrdinalIgnoreCase));
            }

            var isNew = false;
            if (match is null)
            {
                isNew = true;
                match = new ListingVariation
                {
                    Id = Guid.NewGuid(),
                    ListingId = listing.Id,
                    CreatedAt = now
                };
                _db.ListingVariations.Add(match);
                if (!listing.Variations.Contains(match))
                {
                    listing.Variations.Add(match);
                }

                existing.Add(match);
                structuralChanged = true;
            }

            var resolvedAttributesJson = ResolveSyncedVariantAttributes(
                isNew ? null : match.VariantAttributes,
                v.Title);
            var comboKey = VariationComboKey(ResolveVariationAxisAttrs(v.Title, resolvedAttributesJson));
            if (comboKey is not null &&
                keptByCombo.TryGetValue(comboKey, out var keptVariationId) &&
                keptVariationId != match.Id)
            {
                _logger.LogInformation(
                    "Listing {ListingId}: bỏ dòng 4Seller trùng combo {Combo} (skuId={SkuId})",
                    listing.Id,
                    comboKey,
                    v.Id);
                if (isNew)
                {
                    listing.Variations.Remove(match);
                    _db.ListingVariations.Remove(match);
                    existing.Remove(match);
                }

                continue;
            }

            keepIds.Add(match.Id);
            match.FourSellerSkuId = v.Id > 0 ? v.Id : match.FourSellerSkuId;

            var incomingSku = v.SellerSku?.Trim();
            if (ListingSellerSkuNormalizer.ShouldClearIncomingSellerSku(incomingSku, usedSellerSkus))
            {
                _logger.LogInformation(
                    "Listing {ListingId}: bỏ sellerSku trùng {SellerSku} khi sync skuId={SkuId}",
                    listing.Id,
                    incomingSku,
                    v.Id);
                incomingSku = null;
            }

            match.SellerSku = incomingSku;
            if (!string.IsNullOrWhiteSpace(match.SellerSku))
            {
                usedSellerSkus.Add(match.SellerSku.Trim());
            }

            match.Title = v.Title;
            // Keep up to 9 URLs (pipe-joined) for Variant Image hydrate fallback — not only the first.
            match.MainImage = JoinVariationImageUrls(v.MainImage, v.ImageUrls);
            var price = v.OriginalPrice ?? v.GlobalPrice;
            match.GlobalPrice = v.GlobalPrice;
            // Cost / first-seen unit price — set once from 4Seller; never overwritten on re-sync.
            if (match.InitialPrice is null && price is not null)
            {
                match.InitialPrice = price;
            }

            var isDraft = string.Equals(
                listing.PageType,
                ListingPageTypes.Draft,
                StringComparison.OrdinalIgnoreCase);
            if (isDraft)
            {
                // Draft: InitialPrice = cost; SalePrice starts at 0 until user calculates on edit.
                // Re-sync must not overwrite a locally calculated SalePrice.
                if (isNew || match.SalePrice is null)
                {
                    match.SalePrice = 0;
                }
            }
            else
            {
                match.SalePrice = price;
            }

            match.Quantity = v.AvailableStock;
            // Assign only on a real change: older rows were stored with indented JSON, so a plain
            // string compare marked every variation of every listing dirty on every sync.
            if (!VariantAttributesEquivalent(match.VariantAttributes, resolvedAttributesJson))
            {
                match.VariantAttributes = resolvedAttributesJson;
            }

            if (comboKey is not null)
            {
                keptByCombo[comboKey] = match.Id;
            }
        }

        foreach (var orphan in existing.Where(x => !keepIds.Contains(x.Id)).ToList())
        {
            listing.Variations.Remove(orphan);
            _db.ListingVariations.Remove(orphan);
            structuralChanged = true;
        }

        var beforeDedupe = listing.Variations.Count;
        if (ListingSellerSkuNormalizer.DedupeListingVariations(
                listing,
                _logger,
                row => _db.ListingVariations.Remove(row))
            || listing.Variations.Count != beforeDedupe)
        {
            structuralChanged = true;
        }

        RecalcPriceStock(listing);
        return structuralChanged;
    }

    private void ApplyVariationReplace(Listing listing, List<UpdateListingVariationDto> patches)
    {
        var now = DateTime.UtcNow;
        var existing = listing.Variations.ToList();
        var keepIds = new HashSet<Guid>();
        var order = 0;

        foreach (var patch in patches.OrderBy(p => p.SortOrder))
        {
            // Skip empty shell rows (form rebuild can emit blanks with no identity).
            if (!patch.Id.HasValue &&
                patch.FourSellerSkuId is not > 0 &&
                string.IsNullOrWhiteSpace(patch.SellerSku) &&
                string.IsNullOrWhiteSpace(patch.Title) &&
                string.IsNullOrWhiteSpace(patch.VariantAttributes))
            {
                continue;
            }

            ListingVariation? match = null;
            if (patch.Id.HasValue)
            {
                match = existing.FirstOrDefault(v =>
                    !keepIds.Contains(v.Id) && v.Id == patch.Id.Value);
            }

            if (match is null && patch.FourSellerSkuId is > 0)
            {
                match = existing.FirstOrDefault(v =>
                    !keepIds.Contains(v.Id) && v.FourSellerSkuId == patch.FourSellerSkuId);
            }

            // Combo before SKU code / title: Batch SKU rewrites every code at once, so matching on
            // the code would cross-assign price and stock between variants.
            var patchCombo = VariationComboKey(
                ResolveVariationAxisAttrs(patch.Title, patch.VariantAttributes));
            if (match is null && patchCombo is not null)
            {
                match = existing.FirstOrDefault(v =>
                    !keepIds.Contains(v.Id) &&
                    VariationComboKey(ResolveVariationAxisAttrs(v.Title, v.VariantAttributes)) ==
                        patchCombo);
            }

            if (match is null && !string.IsNullOrWhiteSpace(patch.SellerSku))
            {
                match = existing.FirstOrDefault(v =>
                    !keepIds.Contains(v.Id) &&
                    string.Equals(v.SellerSku, patch.SellerSku, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && !string.IsNullOrWhiteSpace(patch.Title))
            {
                match = existing.FirstOrDefault(v =>
                    !keepIds.Contains(v.Id) &&
                    string.Equals(v.Title, patch.Title, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null)
            {
                // Prefer client id when present so form/DB stay aligned after restore.
                var newId = patch.Id is { } pid && pid != Guid.Empty && !existing.Any(v => v.Id == pid)
                    ? pid
                    : Guid.NewGuid();
                match = new ListingVariation
                {
                    Id = newId,
                    ListingId = listing.Id,
                    CreatedAt = now
                };
                // Explicit DbSet.Add — collection-only Add can leave ghost client keys as Modified
                // (UPDATE 0 rows) after a prior wipe of the same logical SKUs.
                _db.ListingVariations.Add(match);
                if (!listing.Variations.Contains(match))
                {
                    listing.Variations.Add(match);
                }

                existing.Add(match);
            }

            keepIds.Add(match.Id);
            if (patch.FourSellerSkuId.HasValue)
            {
                match.FourSellerSkuId = patch.FourSellerSkuId;
            }

            if (patch.SellerSku is not null)
            {
                match.SellerSku = patch.SellerSku.Trim();
            }

            if (patch.Title is not null)
            {
                match.Title = patch.Title.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(patch.VariantAttributes))
            {
                match.Title = TitleFromVariantAttributes(patch.VariantAttributes);
            }

            if (patch.MainImage is not null)
            {
                match.MainImage = UpgradeHttpPipeJoined(patch.MainImage) ?? patch.MainImage.Trim();
            }

            if (patch.SalePrice.HasValue)
            {
                match.SalePrice = patch.SalePrice;
            }
            else if (patch.OriginalPrice.HasValue)
            {
                match.SalePrice = patch.OriginalPrice;
            }

            if (patch.InitialPrice.HasValue)
            {
                match.InitialPrice = patch.InitialPrice;
            }
            else if (match.InitialPrice is null && match.SalePrice is > 0)
            {
                // Do not copy SalePrice=0 (draft default) into InitialPrice — cost comes from sync.
                match.InitialPrice = match.SalePrice;
            }

            if (patch.Quantity.HasValue)
            {
                match.Quantity = patch.Quantity;
            }

            if (patch.VariantAttributes is not null)
            {
                match.VariantAttributes = patch.VariantAttributes;
            }
            else if (!string.IsNullOrWhiteSpace(match.Title) && string.IsNullOrWhiteSpace(match.VariantAttributes))
            {
                match.VariantAttributes = BuildVariantAttributesJson(match.Title);
            }

            match.UpdatedAt = now;
            order++;
        }

        // Form sends the full intentional SKU set (collapsed table mounts hidden Form.Items).
        // Drop orphans so Save Draft / Publish / Update actually remove variants on 4Seller.
        // Guard: empty-shell-only payloads must not wipe live SKUs.
        if (keepIds.Count > 0)
        {
            foreach (var orphan in existing.Where(x => !keepIds.Contains(x.Id)).ToList())
            {
                listing.Variations.Remove(orphan);
                _db.ListingVariations.Remove(orphan);
            }
        }

        ListingSellerSkuNormalizer.DedupeListingVariations(
            listing,
            _logger,
            row => _db.ListingVariations.Remove(row));
    }

    private static void RecalcPriceStock(Listing listing)
    {
        var prices = listing.Variations
            .Where(v => v.SalePrice.HasValue)
            .Select(v => v.SalePrice!.Value)
            .ToList();
        if (prices.Count > 0)
        {
            listing.LowPrice = prices.Min();
            listing.HighPrice = prices.Max();
        }

        var stocks = listing.Variations.Where(v => v.Quantity.HasValue).Select(v => v.Quantity!.Value).ToList();
        if (stocks.Count > 0)
        {
            listing.AvailableStock = SumQuantitiesClamped(stocks);
        }

        listing.VariationCount = listing.Variations.Count;
    }

    /// <summary>
    /// Sums variation quantities without throwing when the total exceeds <see cref="int.MaxValue"/>
    /// (common with many SKUs at ~999_999_999 stock from 4Seller).
    /// </summary>
    internal static int SumQuantitiesClamped(IEnumerable<int> quantities)
    {
        long total = 0;
        foreach (var q in quantities)
        {
            total += q;
            if (total >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)total;
    }

    /// <summary>
    /// Writes source URL into RawPayload only (no full form patch) for lightweight list edits.
    /// </summary>
    private static void PatchSourceUrlInRawPayload(Listing listing)
    {
        if (string.IsNullOrWhiteSpace(listing.RawPayload) || string.IsNullOrWhiteSpace(listing.SourceUrl))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(listing.RawPayload) is not JsonObject obj)
            {
                return;
            }

            obj["sourceUrl"] = listing.SourceUrl;
            obj["listingSourceUrl"] = listing.SourceUrl;
            listing.RawPayload = obj.ToJsonString(PatchJsonOptions);
        }
        catch (JsonException)
        {
            // Keep column-level SourceUrl; RawPayload stays unchanged.
        }
    }

    /// <summary>
    /// Writes description into RawPayload only (no full form patch) for lightweight prep saves.
    /// </summary>
    private static void PatchDescriptionPrepInRawPayload(Listing listing)
    {
        if (string.IsNullOrWhiteSpace(listing.RawPayload) || listing.DescriptionHtml is null)
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(listing.RawPayload) is not JsonObject obj)
            {
                return;
            }

            obj["description"] = listing.DescriptionHtml;
            obj["descriptionHtml"] = listing.DescriptionHtml;
            SetStringIfKeyExists(obj, "descriptionHtml", listing.DescriptionHtml);

            if (!string.IsNullOrWhiteSpace(listing.ProductName))
            {
                obj["productName"] = listing.ProductName;
                SetStringIfKeyExists(obj, "title", listing.ProductName);
            }

            PatchStringListOntoPayload(obj, "searchTerms", listing.SearchTermsJson, dto: null);
            PatchStringListOntoPayload(obj, "productHighlights", listing.ProductHighlightsJson, dto: null);

            listing.RawPayload = obj.ToJsonString(PatchJsonOptions);
        }
        catch (JsonException)
        {
            // Keep column-level DescriptionHtml; RawPayload stays unchanged.
        }
    }

    private static void PatchRawPayload(
        Listing listing,
        UpdateListingMirrorDto? dto,
        IReadOnlyDictionary<int, ListingSalePropSync.MapShape>? capturedValueShapes = null)
    {
        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return;
        }

        try
        {
            var node = JsonNode.Parse(listing.RawPayload);
            if (node is not JsonObject obj)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(listing.ProductName))
            {
                obj["productName"] = listing.ProductName;
                SetStringIfKeyExists(obj, "title", listing.ProductName);
            }

            if (listing.DescriptionHtml is not null)
            {
                obj["description"] = listing.DescriptionHtml;
                SetStringIfKeyExists(obj, "descriptionHtml", listing.DescriptionHtml);
            }

            PatchStringListOntoPayload(obj, "searchTerms", listing.SearchTermsJson, dto?.SearchTerms);
            PatchStringListOntoPayload(obj, "productHighlights", listing.ProductHighlightsJson, dto?.ProductHighlights);

            if (!string.IsNullOrWhiteSpace(listing.CategoryId))
            {
                obj["categoryId"] = listing.CategoryId;
            }

            if (!string.IsNullOrWhiteSpace(listing.CategoryName))
            {
                obj["categoryName"] = listing.CategoryName;
            }

            if (!string.IsNullOrWhiteSpace(listing.WarehouseId))
            {
                obj["warehouseId"] = listing.WarehouseId;
            }

            if (!string.IsNullOrWhiteSpace(listing.SourceUrl))
            {
                obj["sourceUrl"] = listing.SourceUrl;
                SetStringIfKeyExists(obj, "listingSourceUrl", listing.SourceUrl);
            }

            if (!string.IsNullOrWhiteSpace(listing.Site))
            {
                obj["site"] = listing.Site;
                SetStringIfKeyExists(obj, "siteId", listing.Site);
            }

            if (listing.PackageWeight.HasValue)
            {
                obj["packageWeight"] = listing.PackageWeight.Value;
            }

            if (listing.PackageLength.HasValue)
            {
                obj["packageLength"] = listing.PackageLength.Value;
            }

            if (listing.PackageWidth.HasValue)
            {
                obj["packageWidth"] = listing.PackageWidth.Value;
            }

            if (listing.PackageHeight.HasValue)
            {
                obj["packageHeight"] = listing.PackageHeight.Value;
            }

            if (!string.IsNullOrWhiteSpace(listing.WeightUnit))
            {
                obj["weightUnit"] = listing.WeightUnit;
            }

            if (!string.IsNullOrWhiteSpace(listing.DimensionUnit))
            {
                obj["dimensionUnit"] = listing.DimensionUnit;
            }

            if (!string.IsNullOrWhiteSpace(listing.MainImage))
            {
                obj["mainImage"] = listing.MainImage;
            }

            // Publish shop — 4Seller expects numeric internal shop id (record.Id).
            if (listing.FourSellerShopId is > 0)
            {
                obj["shopId"] = listing.FourSellerShopId.Value;
                obj["currentShopId"] = listing.FourSellerShopId.Value;
            }

            if (dto is not null)
            {
                SetStringIfPresent(obj, "brandId", dto.BrandId);
                SetStringIfPresent(obj, "brandName", dto.BrandName);
                PatchBrandInfo(obj, dto.BrandId, dto.BrandName);
                SetStringIfPresent(obj, "productType", dto.ProductType);
                SetStringIfPresent(obj, "sizeChartId", dto.SizeChartId);
                SetStringIfPresent(obj, "erpVideoId", dto.ErpVideoId);
                SetStringIfPresent(obj, "safetyWarning", dto.SafetyWarning);
                SetStringIfPresent(obj, "groupId", dto.FourSellerGroupId);
                SetStringIfPresent(obj, "groupName", dto.FourSellerGroupName);
                SetStringIfPresent(obj, "fourSellerGroupId", dto.FourSellerGroupId);
                SetStringIfPresent(obj, "fourSellerGroupName", dto.FourSellerGroupName);
                SetStringIfPresent(obj, "appliedRules", dto.AppliedRules);
                SetStringIfPresent(obj, "refListingId", dto.RefListingId);
                SetStringIfPresent(obj, "refShopId", dto.RefShopId);
                if (!string.IsNullOrWhiteSpace(dto.ListingSourceUrl))
                {
                    obj["listingSourceUrl"] = dto.ListingSourceUrl.Trim();
                    obj["sourceUrl"] = dto.ListingSourceUrl.Trim();
                }

                if (dto.Images is not null)
                {
                    PatchImages(obj, dto.Images, listing.Variations);
                }

                if (dto.Attributes is not null)
                {
                    PatchAttributes(obj, dto.Attributes);
                }

                // Always upsert Product Certification Yes/No into productAttributes (default No).
                // Fashion listings require these on every save-draft / publish / update.
                UpsertProductCertificationAttributes(obj, dto.Certifications ?? []);
                if (dto.Certifications is not null)
                {
                    PatchCertificationArrays(obj, dto.Certifications);
                }
            }

            // Sale-prop slots must be resolved from the pre-rewrite payload (it still declares
            // which option slot is Color vs Size) before any option key is touched.
            var axisAttrs = BuildVariationAxisAttrs(listing.Variations);
            var axisSlots = ListingSalePropSync.ResolveAxisSlots(obj, axisAttrs);
            RewriteVariationArrays(obj, listing.Variations, axisSlots, listing.WarehouseId);
            PruneSkuImgsToVariations(obj, listing.Variations);
            // No schema resolver here: local-only edits must not hit 4Seller. PushAsync re-runs
            // this with the category schema when a brand new axis / label needs an id — until then
            // an unresolvable label leaves `option{N}Value` untouched rather than pruned, so the
            // ids 4Seller gave us survive in RawPayload.
            var valueShapes = capturedValueShapes ?? ListingSalePropSync.CaptureValueShapes(obj);
            ListingSalePropSync.RebuildRootMaps(
                obj, axisAttrs, axisSlots, capturedValueShapes: valueShapes);
            SyncWarehouseAcrossStockInfo(obj, listing.WarehouseId);

            UpgradeHttpUrlsInJsonNode(obj);
            if (!string.IsNullOrWhiteSpace(listing.MainImage))
            {
                listing.MainImage = UpgradeHttpPipeJoined(listing.MainImage);
            }

            foreach (var variation in listing.Variations)
            {
                if (!string.IsNullOrWhiteSpace(variation.MainImage))
                {
                    variation.MainImage = UpgradeHttpPipeJoined(variation.MainImage);
                }
            }

            listing.RawPayload = obj.ToJsonString(PatchJsonOptions);
        }
        catch (JsonException)
        {
            // Keep existing payload if patch fails.
        }
    }

    private static void SetStringIfPresent(JsonObject obj, string key, string? value)
    {
        // Empty string from hidden form fields must not wipe original RawPayload values.
        if (!string.IsNullOrWhiteSpace(value))
        {
            obj[key] = value.Trim();
        }
    }

    /// <summary>
    /// Keeps <c>brandInfo</c> JSON-string in sync with brandId/brandName (golden: {"id","name"}).
    /// </summary>
    private static void PatchBrandInfo(JsonObject obj, string? brandId, string? brandName)
    {
        var id = string.IsNullOrWhiteSpace(brandId) ? null : brandId.Trim();
        var name = string.IsNullOrWhiteSpace(brandName) ? null : brandName.Trim();
        if (id is null && name is null)
        {
            return;
        }

        // Prefer explicit DTO fields; fall back to existing payload pieces.
        id ??= GetString(obj, "brandId") ?? "0";
        name ??= GetString(obj, "brandName")
                 ?? (id == "0" ? "No brand" : id);

        obj["brandId"] = id;
        obj["brandName"] = name;
        obj["brandInfo"] = JsonSerializer.Serialize(new { id, name });
    }

    /// <summary>
    /// Rewrites root warehouseId + every variation stockInfoList to a single row for the
    /// selected warehouse (golden shape: one <c>{warehouse_id, available_stock}</c>).
    /// </summary>
    internal static void SyncWarehouseAcrossStockInfo(JsonObject obj, string? warehouseId)
    {
        if (string.IsNullOrWhiteSpace(warehouseId))
        {
            return;
        }

        var wid = warehouseId.Trim();
        obj["warehouseId"] = wid;

        foreach (var key in VariationArrayKeys)
        {
            if (obj[key] is not JsonArray arr)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject vObj)
                {
                    continue;
                }

                vObj["warehouseId"] = wid;

                var qty = (int?)(GetDecimal(vObj, "availableStock")
                                 ?? GetDecimal(vObj, "quantity")
                                 ?? GetDecimal(vObj, "stock"))
                          ?? ReadPrimaryStockQtyFromLists(vObj)
                          ?? 0;

                CollapseStockInfoList(vObj, qty, wid);
            }
        }
    }

    /// <summary>
    /// Ensures <c>stockInfoList</c> is a single warehouse row with the given quantity.
    /// Avoids writing the same qty into every legacy multi-warehouse / global row — that
    /// used to make sync SUM rows and inflate stock on each auto-publish/retry cycle.
    /// </summary>
    internal static void CollapseStockInfoList(JsonObject vObj, int quantity, string? warehouseId)
    {
        var wid = (warehouseId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(wid))
        {
            wid = GetString(vObj, "warehouseId")
                  ?? GetString(vObj, "warehouse_id")
                  ?? FirstWarehouseIdFromStockLists(vObj)
                  ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(wid))
        {
            // Do not invent stockInfoList without warehouse_id — 4Seller rejects incomplete stock rows.
            return;
        }

        vObj["stockInfoList"] = new JsonArray
        {
            new JsonObject
            {
                ["warehouse_id"] = wid,
                ["available_stock"] = quantity
            }
        };

        // Mirror a single global row when the payload already carries globalStockInfoList;
        // never leave N copies of the same qty (ReadVariationStock used to sum them).
        if (vObj["globalStockInfoList"] is JsonArray)
        {
            vObj["globalStockInfoList"] = new JsonArray
            {
                new JsonObject
                {
                    ["warehouse_id"] = wid,
                    ["available_stock"] = quantity
                }
            };
        }
    }

    private static string? FirstWarehouseIdFromStockLists(JsonObject vObj)
    {
        foreach (var listName in new[] { "stockInfoList", "globalStockInfoList" })
        {
            if (vObj[listName] is not JsonArray arr)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject stockObj)
                {
                    continue;
                }

                var wid = GetString(stockObj, "warehouse_id") ?? GetString(stockObj, "warehouseId");
                if (!string.IsNullOrWhiteSpace(wid))
                {
                    return wid.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Max qty per warehouse across stock lists (does not double-count stockInfo + global).
    /// </summary>
    private static int? ReadPrimaryStockQtyFromLists(JsonObject vObj)
    {
        JsonArray? primary = vObj["stockInfoList"] as JsonArray;
        if (primary is null || primary.Count == 0)
        {
            primary = vObj["globalStockInfoList"] as JsonArray;
        }

        if (primary is null || primary.Count == 0)
        {
            return null;
        }

        var perWarehouse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var found = false;
        foreach (var item in primary)
        {
            if (item is not JsonObject stockObj)
            {
                continue;
            }

            var qty = (int?)(GetDecimal(stockObj, "available_stock")
                             ?? GetDecimal(stockObj, "availableStock")
                             ?? GetDecimal(stockObj, "quantity")
                             ?? GetDecimal(stockObj, "stock"));
            if (qty is null or < 0)
            {
                continue;
            }

            found = true;
            var wid = GetString(stockObj, "warehouse_id")
                      ?? GetString(stockObj, "warehouseId")
                      ?? string.Empty;
            if (!perWarehouse.TryGetValue(wid, out var existing) || qty.Value > existing)
            {
                perWarehouse[wid] = qty.Value;
            }
        }

        if (!found)
        {
            return null;
        }

        long total = 0;
        foreach (var q in perWarehouse.Values)
        {
            total += q;
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static void ClearStringKeys(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            obj[key] = "";
        }
    }

    private static void PatchImages(
        JsonObject obj,
        List<UpdateListingMirrorImageDto> images,
        ICollection<ListingVariation> variations)
    {
        var productImages = images
            .Where(i =>
                string.Equals(i.MediaType, "Main", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.MediaType, "Detail", StringComparison.OrdinalIgnoreCase))
            // Shared gallery only — skip variant-bound rows (by sku id OR colorValue via listingSkuId).
            .Where(i => i.ListingSkuId is null)
            .OrderBy(i => i.SortOrder)
            .Select(i => UpgradeHttpToHttps(i.Url.Trim()) ?? i.Url.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Where(u => !ListingImageUrlFilters.IsGifImageUrl(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productImages.Count > 0)
        {
            // Cap shared gallery like import/create (≤ 9); do not dump variant URLs into mainImage.
            var capped = productImages.Take(9).ToList();
            obj["mainImage"] = string.Join('|', capped);
            // Prefer plain URL imageList when that is already the shape; do not downgrade
            // object-array imageList (rich metadata) into bare strings.
            if (obj["imageList"] is not JsonArray existingIl ||
                existingIl.Count == 0 ||
                existingIl[0] is JsonValue)
            {
                SetArrayFieldIfCompatible(
                    obj,
                    "imageList",
                    new JsonArray(capped.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray()));
            }
            // Do not rewrite `images` object array — original rows often carry ids/metadata
            // that 4Seller save-draft requires; mainImage + imageList are the editable surfaces.
        }

        var video = images.FirstOrDefault(i =>
            string.Equals(i.MediaType, "Video", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(i.Url));
        if (video is not null)
        {
            obj["erpVideoUrl"] = video.Url.Trim();
            obj["videoUrl"] = video.Url.Trim();
            obj["productVideo"] = video.Url.Trim();
        }
        else
        {
            // Form removed Video — must clear stale keys or save-draft re-pushes old MP4.
            ClearStringKeys(obj, "erpVideoUrl", "videoUrl", "productVideo", "videoPostUrl", "erpVideoId", "videoId");
        }

        var sizeChart = images.FirstOrDefault(i =>
            string.Equals(i.MediaType, "SizeChart", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(i.Url) &&
            !ListingImageUrlFilters.IsGifImageUrl(i.Url));
        if (sizeChart is not null)
        {
            obj["sizeChart"] = sizeChart.Url.Trim();
            obj["sizeChartImage"] = sizeChart.Url.Trim();
            obj["sizeChartUrl"] = sizeChart.Url.Trim();
        }
        else
        {
            // Form removed Size Chart — clear URL keys + id so 4Seller drops the chart.
            ClearStringKeys(obj, "sizeChart", "sizeChartImage", "sizeChartUrl", "sizeChartId");
        }

        var colorMap = new JsonObject();
        foreach (var group in images
                     .Where(i => i.ListingSkuId.HasValue && !string.IsNullOrWhiteSpace(i.Url))
                     .GroupBy(i => i.ListingSkuId!.Value))
        {
            // Skip images bound to SKUs the form removed — do not reintroduce dead colors.
            var variation = variations.FirstOrDefault(v => v.Id == group.Key);
            if (variation is null)
            {
                continue;
            }

            var color = ColorFromVariation(variation);
            if (string.IsNullOrWhiteSpace(color) || color.Contains('/'))
            {
                continue;
            }

            var urls = group
                .OrderBy(i => i.SortOrder)
                .Select(i => UpgradeHttpToHttps(i.Url.Trim()) ?? i.Url.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Where(u => !ListingImageUrlFilters.IsGifImageUrl(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (urls.Count == 0)
            {
                variation.MainImage = null;
                continue;
            }

            colorMap[color] = new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
            // Keep full pipe-joined set (≤ 9) — do not collapse to the first URL.
            variation.MainImage = string.Join('|', urls.Take(9));
        }

        if (colorMap.Count > 0)
        {
            // Merge form color galleries; PruneSkuImgsToVariations drops removed colors next.
            if (obj["skuImgs"] is JsonObject existingSkuImgs)
            {
                foreach (var kv in colorMap)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Key.Contains('/'))
                    {
                        continue;
                    }

                    existingSkuImgs[kv.Key] = kv.Value?.DeepClone();
                }

                obj["salePropImageMap"] = existingSkuImgs.DeepClone();
            }
            else
            {
                var cleaned = new JsonObject();
                foreach (var kv in colorMap)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Key.Contains('/'))
                    {
                        continue;
                    }

                    cleaned[kv.Key] = kv.Value?.DeepClone();
                }

                if (cleaned.Count > 0)
                {
                    obj["skuImgs"] = cleaned;
                    obj["salePropImageMap"] = cleaned.DeepClone();
                }
            }
        }

        PruneSkuImgsToVariations(obj, variations);
        ListingImageUrlFilters.CleanupSkuImgsNode(obj["skuImgs"]);
        if (obj["salePropImageMap"] is JsonObject salePropMap)
        {
            ListingImageUrlFilters.CleanupSkuImgsNode(salePropMap);
            obj["salePropImageMap"] = salePropMap.DeepClone();
        }
    }

    /// <summary>
    /// Drops <c>skuImgs</c> / <c>salePropImageMap</c> colors that no longer have a variation.
    /// </summary>
    private static void PruneSkuImgsToVariations(
        JsonObject obj,
        ICollection<ListingVariation> variations)
    {
        var keepColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variations)
        {
            var color = ColorFromVariation(v);
            if (!string.IsNullOrWhiteSpace(color) && !color.Contains('/'))
            {
                keepColors.Add(color.Trim());
            }
        }

        if (obj["skuImgs"] is JsonObject map)
        {
            foreach (var key in map.Select(kv => kv.Key).ToList())
            {
                if (!keepColors.Contains(key))
                {
                    map.Remove(key);
                }
            }

            obj["salePropImageMap"] = map.DeepClone();
            return;
        }

        // Browser push shape: JSON string of [{attrName,img_url,custom_value}, …].
        if (obj["skuImgs"] is JsonValue jv &&
            jv.TryGetValue(out string? raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (JsonNode.Parse(raw) is not JsonArray rows)
                {
                    return;
                }

                var next = new JsonArray();
                foreach (var item in rows)
                {
                    if (item is not JsonObject row)
                    {
                        continue;
                    }

                    var color = GetString(row, "custom_value")
                                ?? GetString(row, "customValue")
                                ?? GetString(row, "attrName");
                    if (!string.IsNullOrWhiteSpace(color) && keepColors.Contains(color.Trim()))
                    {
                        next.Add(row.DeepClone()!);
                    }
                }

                obj["skuImgs"] = next.ToJsonString();
            }
            catch (JsonException)
            {
                // leave skuImgs unchanged
            }
        }
    }

    private static void PatchAttributes(JsonObject obj, List<UpdateListingMirrorAttributeDto> attributes)
    {
        // Preserve certification Yes/No rows already in productAttributes — form Specifics
        // intentionally excludes them (Product Certification card owns those).
        var existing = GetJsonArrayFlexible(obj, "productAttributes")
                       ?? GetJsonArrayFlexible(obj, "attributes")
                       ?? GetJsonArrayFlexible(obj, "attributeList")
                       ?? new JsonArray();
        var preservedCerts = new JsonArray();
        foreach (var item in existing)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var name = GetString(a, "attributeName")
                       ?? GetString(a, "attrName")
                       ?? GetString(a, "name");
            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id");
            if (IsProductCertificationAttribute(id, name))
            {
                preservedCerts.Add(a.DeepClone()!);
            }
        }

        var arr = new JsonArray();
        foreach (var cert in preservedCerts)
        {
            arr.Add(cert!.DeepClone()!);
        }

        foreach (var a in attributes)
        {
            if (string.IsNullOrWhiteSpace(a.AttrId) && string.IsNullOrWhiteSpace(a.AttrName))
            {
                continue;
            }

            if (IsProductCertificationAttribute(a.AttrId, a.AttrName))
            {
                continue;
            }

            // Admin listing flow always publishes as No brand. Ignore any editable/raw Brand row
            // and append the canonical required default exactly once below.
            if (IsBrandAttribute(a.AttrId, a.AttrName))
            {
                continue;
            }

            var attrId = string.IsNullOrWhiteSpace(a.AttrId) ? a.AttrName!.Trim() : a.AttrId.Trim();
            var attrName = string.IsNullOrWhiteSpace(a.AttrName) ? attrId : a.AttrName.Trim();
            var valueEntries = ResolveAttributeValueEntries(a);
            if (valueEntries.Count == 0)
            {
                // Empty valueName → TikTok 【Unpublishable】Product attributes are unavailable
                continue;
            }

            // TikTok requires a numeric (Int64) attributeId for every productAttributes row.
            // Non-numeric ids (custom/free-text keys) fail the whole publish with
            // "Id of ProductAttributes[x] is invalid, the value must be convertible to Int64".
            if (!IsInt64Id(attrId))
            {
                continue;
            }

            var values = new JsonArray();
            foreach (var entry in valueEntries)
            {
                var emitValueId = IsInt64Id(entry.ValueId) ? entry.ValueId! : string.Empty;
                values.Add(new JsonObject
                {
                    ["valueId"] = emitValueId,
                    ["valueName"] = entry.ValueName,
                    ["isShow"] = "true"
                });
            }

            var primaryName = valueEntries[0].ValueName;
            var primaryId = IsInt64Id(valueEntries[0].ValueId) ? valueEntries[0].ValueId : null;

            // Canonical TikTok shape used by save-draft golden payload.
            arr.Add(new JsonObject
            {
                ["attributeId"] = attrId,
                ["attributeName"] = attrName,
                ["values"] = values,
                // Keep simplified keys for hydrate fallback / older array payloads.
                ["attrId"] = attrId,
                ["id"] = attrId,
                ["attrName"] = attrName,
                ["name"] = attrName,
                ["attrValue"] = primaryName,
                ["value"] = primaryName,
                ["valueId"] = primaryId ?? string.Empty,
                ["valueType"] = a.ValueType,
                ["isRequired"] = a.IsRequired
            });
        }

        // Brand=No brand is mandatory for this admin listing flow, even when other Specifics exist.
        // Brand is carried by brandId/brandInfo (golden shape) — NOT as a productAttributes row,
        // whose "Brand" id is not a valid Int64 and would reject the entire publish.
        EnsureDefaultBrandInfo(obj);

        // Always prefer TikTok golden shape (JSON string) so save-draft accepts Specifics edits.
        WriteProductAttributesField(obj, arr, preferJsonString: true);

        // Only touch mutable simplified arrays when they already exist as arrays.
        SetArrayFieldIfCompatible(obj, "attributeList", arr.DeepClone()!.AsArray());
        SetArrayFieldIfCompatible(obj, "attributes", arr.DeepClone()!.AsArray());
    }

    private static bool IsProductCertificationAttribute(string? id, string? name)
    {
        if (!string.IsNullOrWhiteSpace(id)
            && ProductCertificationAttrs.Any(d =>
                string.Equals(d.AttributeId, id.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = name.Trim().ToLowerInvariant();
        return n.Contains("prop 65", StringComparison.Ordinal)
               || n.Contains("prop65", StringComparison.Ordinal)
               || n.Contains("hazardous", StringComparison.Ordinal)
               || n.Contains("dangerous goods", StringComparison.Ordinal)
               || n.Contains("carcinogen", StringComparison.Ordinal)
               || n.Contains("repro. chems", StringComparison.Ordinal)
               || n.Contains("repro chems", StringComparison.Ordinal);
    }

    private static bool IsBrandAttribute(string? id, string? name) =>
        string.Equals(id?.Trim(), "Brand", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name?.Trim(), "Brand", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the id is a valid TikTok Int64 attribute/value id.</summary>
    private static bool IsInt64Id(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && long.TryParse(id.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// Builds canonical value entries for one Specifics attr — prefers <see cref="UpdateListingMirrorAttributeDto.Values"/>
    /// when multi-select; falls back to AttrValue/ValueId.
    /// </summary>
    private static List<(string ValueName, string? ValueId)> ResolveAttributeValueEntries(
        UpdateListingMirrorAttributeDto a)
    {
        var result = new List<(string ValueName, string? ValueId)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (a.Values is { Count: > 0 })
        {
            foreach (var v in a.Values)
            {
                var name = (v.ValueName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var vid = v.ValueId?.Trim();
                var dedupe = (IsInt64Id(vid) ? vid! : string.Empty) + "\0" + name;
                if (!seen.Add(dedupe))
                {
                    continue;
                }

                result.Add((name, vid));
                if (result.Count >= 5)
                {
                    break;
                }
            }

            if (result.Count > 0)
            {
                return result;
            }
        }

        var attrValue = a.AttrValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(attrValue))
        {
            return result;
        }

        result.Add((attrValue, a.ValueId?.Trim()));
        return result;
    }

    /// <summary>
    /// Guarantees the mandatory brand field on the payload (admin defaults to No brand).
    /// Brand lives in <c>brandId</c>/<c>brandInfo</c> (golden shape); a chosen brand is preserved.
    /// </summary>
    private static void EnsureDefaultBrandInfo(JsonObject obj)
    {
        var brandId = GetString(obj, "brandId");
        if (string.IsNullOrWhiteSpace(brandId))
        {
            obj["brandId"] = "0";
            obj["brandName"] = "No brand";
            obj["brandInfo"] = JsonSerializer.Serialize(new { id = "0", name = "No brand" });
            return;
        }

        if (string.IsNullOrWhiteSpace(GetString(obj, "brandInfo")))
        {
            var name = GetString(obj, "brandName")
                       ?? (brandId.Trim() == "0" ? "No brand" : brandId.Trim());
            obj["brandInfo"] = JsonSerializer.Serialize(new { id = brandId.Trim(), name });
        }
    }

    /// <summary>
    /// TikTok US fashion Product Certification Yes/No attrs (canonical store = <c>productAttributes</c>).
    /// <c>productCertifications</c> is usually <c>"[]"</c> and is not used for these flags.
    /// </summary>
    private static readonly (string CertType, string AttributeId, string AttributeName)[] ProductCertificationAttrs =
    [
        ("Compliance", "101619", "Dangerous Goods Or Hazardous Materials"),
        ("ProductCert", "101395", "CA Prop 65: Repro. Chems"),
        ("ResponsiblePerson", "101400", "CA Prop 65: Carcinogens")
    ];

    private const string CertValueIdYes = "1000058";
    private const string CertValueIdNo = "1000059";

    private static void PatchCertifications(JsonObject obj, List<UpdateListingMirrorCertificationDto> certifications)
    {
        UpsertProductCertificationAttributes(obj, certifications);
        PatchCertificationArrays(obj, certifications);
    }

    private static void PatchCertificationArrays(JsonObject obj, List<UpdateListingMirrorCertificationDto> certifications)
    {
        // Keep simplified certification arrays only when they are already mutable arrays
        // (not opaque TikTok JSON strings — those stay "[]" for file/manufacturer certs).
        if (IsJsonStringField(obj, "certifications") || IsJsonStringField(obj, "productCertifications"))
        {
            return;
        }

        var arr = new JsonArray();
        foreach (var c in certifications)
        {
            arr.Add(new JsonObject
            {
                ["certType"] = c.CertType,
                ["type"] = c.CertType,
                ["externalId"] = c.ExternalId,
                ["name"] = NormalizeYesNo(c.Name),
                ["phone"] = c.Phone,
                ["email"] = c.Email,
                ["address"] = c.Address,
                ["fileUrls"] = c.FileUrls
            });
        }

        SetArrayFieldIfCompatible(obj, "certificationList", arr);
        SetArrayFieldIfCompatible(obj, "certifications", arr.DeepClone()!.AsArray());
    }

    /// <summary>
    /// Always writes the three Product Certification Yes/No values into <c>productAttributes</c>
    /// (default <c>No</c>) so fashion listings pass TikTok compliance on every save-draft/publish/update.
    /// </summary>
    private static void UpsertProductCertificationAttributes(
        JsonObject obj,
        List<UpdateListingMirrorCertificationDto> certifications)
    {
        var yesNoByType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in ProductCertificationAttrs)
        {
            yesNoByType[def.CertType] = "No";
        }

        foreach (var c in certifications)
        {
            if (string.IsNullOrWhiteSpace(c.CertType))
            {
                continue;
            }

            yesNoByType[c.CertType.Trim()] = NormalizeYesNo(c.Name);
        }

        var wasString = IsJsonStringField(obj, "productAttributes")
                        || IsJsonStringField(obj, "attributes")
                        || IsJsonStringField(obj, "attributeList");

        var attrArr = GetJsonArrayFlexible(obj, "productAttributes")
                      ?? GetJsonArrayFlexible(obj, "attributes")
                      ?? GetJsonArrayFlexible(obj, "attributeList")
                      ?? new JsonArray();

        foreach (var def in ProductCertificationAttrs)
        {
            var valueName = yesNoByType.TryGetValue(def.CertType, out var v) ? v : "No";
            var valueId = string.Equals(valueName, "Yes", StringComparison.OrdinalIgnoreCase)
                ? CertValueIdYes
                : CertValueIdNo;
            UpsertOneProductCertificationAttribute(attrArr, def.AttributeId, def.AttributeName, valueId, valueName);
        }

        WriteProductAttributesField(obj, attrArr, wasString);
    }

    private static void UpsertOneProductCertificationAttribute(
        JsonArray attrArr,
        string attributeId,
        string attributeName,
        string valueId,
        string valueName)
    {
        JsonObject? match = null;
        foreach (var item in attrArr)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id")
                     ?? GetString(a, "attribute_id");
            var name = GetString(a, "attributeName")
                       ?? GetString(a, "attrName")
                       ?? GetString(a, "name")
                       ?? GetString(a, "attribute_name");

            if (string.Equals(id, attributeId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(name) &&
                 string.Equals(name.Trim(), attributeName, StringComparison.OrdinalIgnoreCase)))
            {
                match = a;
                break;
            }
        }

        if (match is null)
        {
            attrArr.Add(new JsonObject
            {
                ["attributeId"] = attributeId,
                ["attributeName"] = attributeName,
                ["values"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["valueId"] = valueId,
                        ["valueName"] = valueName,
                        ["isShow"] = "true"
                    }
                }
            });
            return;
        }

        // Preserve existing key style (TikTok attributeId/values vs simplified attrId/attrValue).
        if (match.ContainsKey("attributeId") || match.ContainsKey("values") || match.ContainsKey("attributeName"))
        {
            match["attributeId"] = GetString(match, "attributeId") ?? attributeId;
            match["attributeName"] = GetString(match, "attributeName") ?? attributeName;
            match["values"] = new JsonArray
            {
                new JsonObject
                {
                    ["valueId"] = valueId,
                    ["valueName"] = valueName,
                    ["isShow"] = "true"
                }
            };
            return;
        }

        match["attrId"] = GetString(match, "attrId") ?? GetString(match, "id") ?? attributeId;
        match["id"] = GetString(match, "id") ?? GetString(match, "attrId") ?? attributeId;
        match["attrName"] = GetString(match, "attrName") ?? GetString(match, "name") ?? attributeName;
        match["name"] = GetString(match, "name") ?? GetString(match, "attrName") ?? attributeName;
        match["attrValue"] = valueName;
        match["value"] = valueName;
    }

    private static void WriteProductAttributesField(JsonObject obj, JsonArray attrArr, bool preferJsonString)
    {
        if (preferJsonString || obj["productAttributes"] is null || IsJsonStringField(obj, "productAttributes"))
        {
            obj["productAttributes"] = attrArr.ToJsonString();
            return;
        }

        if (obj["productAttributes"] is JsonArray)
        {
            obj["productAttributes"] = attrArr;
            return;
        }

        if (obj["attributes"] is JsonArray)
        {
            obj["attributes"] = attrArr;
            return;
        }

        obj["productAttributes"] = attrArr.ToJsonString();
    }

    private static string NormalizeYesNo(string? value)
    {
        if (string.Equals(value?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase))
        {
            return "Yes";
        }

        return "No";
    }

    private static bool IsJsonStringField(JsonObject obj, string key) =>
        obj[key] is JsonValue jv && jv.TryGetValue<string>(out _);

    private static void SetArrayFieldIfCompatible(JsonObject obj, string key, JsonArray arr)
    {
        if (obj[key] is JsonValue)
        {
            return;
        }

        obj[key] = arr;
    }

    /// <summary>
    /// One category product attribute as 4Seller embeds it in the listing detail
    /// (<c>categoryProductAttributeList</c>) — the same schema the category endpoint returns, so no
    /// extra round trip is needed to validate what the form is about to push.
    /// </summary>
    private sealed class EmbeddedCategoryAttribute
    {
        public required string AttrId { get; init; }
        public required string AttrName { get; init; }
        public bool AllowsCustomValue { get; init; }
        public bool AllowsMultiple { get; init; }

        /// <summary>4Seller <c>isMandatory</c>: the denylist may narrow such an enum but never empty it.</summary>
        public bool IsRequired { get; init; }

        /// <summary>Enum options keyed by normalized value name. Empty for free-text attributes.</summary>
        public Dictionary<string, (string ValueId, string ValueName)> OptionByName { get; } =
            new(StringComparer.Ordinal);

        /// <summary>Enum option names keyed by value id, to keep an id the form already resolved.</summary>
        public Dictionary<string, string> OptionNameById { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attributes whose <c>requirementConditions</c> gate is closed for what the payload actually says:
    /// the Prop 65 chemical lists only exist while the matching Yes/No answer is Yes, so sending them
    /// beside a "No" is a row TikTok has no place for.
    /// </summary>
    private static HashSet<string> CollectClosedGateAttrIds(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        JsonArray rows)
    {
        var closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conditional = schema
            .Where(a => !string.IsNullOrWhiteSpace(a.RequirementConditionsJson))
            .ToList();
        if (conditional.Count == 0)
        {
            return closed;
        }

        // One value id per attribute is all a VALUE_ID_MATCH gate looks at.
        var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.OfType<JsonObject>())
        {
            var id = (GetString(row, "attributeId") ?? GetString(row, "attrId") ?? GetString(row, "id"))?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var valueId = ReadRowValues(row)
                .Select(v => v.ValueId.Trim())
                .FirstOrDefault(v => IsInt64Id(v));
            if (!string.IsNullOrWhiteSpace(valueId))
            {
                answers.TryAdd(id, valueId);
            }
        }

        foreach (var attr in conditional)
        {
            if (!ListingSpecificsMappingEngine.RequirementConditionsSatisfied(attr, answers))
            {
                closed.Add(attr.AttributeId.Trim());
            }
        }

        return closed;
    }

    /// <summary>
    /// The live category schema in the shape the payload pass works with. Sale props (<c>attrType 2</c>:
    /// Color / Size / Number of items) are dropped — they travel in <c>option{N}</c>, and repeating them
    /// in <c>productAttributes</c> is what TikTok answers with 【Unpublishable】Product attributes are
    /// unavailable.
    /// </summary>
    private static Dictionary<string, EmbeddedCategoryAttribute> ToEmbeddedSchema(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        TikTokAttributeValueDenylist? denylist)
    {
        var result = new Dictionary<string, EmbeddedCategoryAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in schema)
        {
            if (string.IsNullOrWhiteSpace(attr.AttributeId)
                || !IsInt64Id(attr.AttributeId)
                || ListingSpecificsMappingEngine.IsSalePropAttribute(attr))
            {
                continue;
            }

            var mapped = new EmbeddedCategoryAttribute
            {
                AttrId = attr.AttributeId.Trim(),
                AttrName = attr.AttributeName,
                AllowsCustomValue = attr.IsCustomizable,
                AllowsMultiple = attr.IsMultiple,
                IsRequired = attr.IsRequired
            };

            // WithoutRejectedValues keeps a mandatory enum intact when every value has been denied, so a
            // row on such an attribute still resolves to a real valueId instead of being dropped.
            foreach (var value in ListingSpecificsMappingEngine.WithoutRejectedValues(attr, denylist).Values)
            {
                if (string.IsNullOrWhiteSpace(value.ValueName))
                {
                    continue;
                }

                var norm = ListingSpecificsMappingEngine.NormalizeKey(value.ValueName);
                if (norm.Length > 0)
                {
                    mapped.OptionByName.TryAdd(norm, (value.ValueId, value.ValueName));
                }

                if (IsInt64Id(value.ValueId))
                {
                    mapped.OptionNameById.TryAdd(value.ValueId, value.ValueName);
                }
            }

            result[mapped.AttrId] = mapped;
        }

        return result;
    }

    /// <summary>
    /// Embedded schema attribute as the mapping engine expects it, so payload and mapping paths share
    /// one set of value-selection rules.
    /// </summary>
    private static FourSellerCategoryAttributeDto ToCategoryAttributeDto(
        EmbeddedCategoryAttribute schemaAttr) =>
        new()
        {
            AttributeId = schemaAttr.AttrId,
            AttributeName = schemaAttr.AttrName,
            IsCustomizable = schemaAttr.AllowsCustomValue,
            IsMultiple = schemaAttr.AllowsMultiple,
            IsRequired = schemaAttr.IsRequired,
            Values = schemaAttr.OptionByName.Values
                .GroupBy(v => v.ValueId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(v => new FourSellerCategoryAttributeValueDto
                {
                    ValueId = v.ValueId,
                    ValueName = v.ValueName
                })
                .ToList()
        };

    /// <summary>What the schema pass changed, for logging and diagnostics.</summary>
    internal sealed class ProductAttributesSchemaAudit
    {
        /// <summary>Rows removed because TikTok would reject them (sale-prop / off-schema / bad enum).</summary>
        public List<string> Dropped { get; } = [];

        /// <summary>Rows rewritten to the schema (missing <c>valueId</c> filled, extra values trimmed).</summary>
        public List<string> Repaired { get; } = [];

        public bool HasChanges => Dropped.Count > 0 || Repaired.Count > 0;
    }

    /// <summary>
    /// Reads the category attribute schema 4Seller embeds in the listing detail. Entries belonging to
    /// another category (the payload keeps the list from the category it was fetched for, and the form
    /// may since have moved the listing) or to a sale prop are ignored, so a stale list degrades to
    /// "no schema" instead of wrongly rejecting every Specific.
    /// <para>
    /// The list is the bulk of RawPayload (~88 KB of 180 KB, mostly the Prop 65 chemical enums), so
    /// <paramref name="parseValuesFor"/> limits the <c>valueList</c> parsing to the attributes the
    /// caller actually holds a row for. Every in-category attribute is still listed, because
    /// membership alone decides whether a row may be pushed.
    /// </para>
    /// </summary>
    private static Dictionary<string, EmbeddedCategoryAttribute> ReadEmbeddedCategoryAttributes(
        JsonObject obj,
        IReadOnlySet<string> parseValuesFor,
        TikTokAttributeValueDenylist? denylist = null)
    {
        var result = new Dictionary<string, EmbeddedCategoryAttribute>(StringComparer.OrdinalIgnoreCase);
        var arr = GetJsonArrayFlexible(obj, "categoryProductAttributeList");
        if (arr is null)
        {
            return result;
        }

        var payloadCategoryId = GetString(obj, "categoryId")?.Trim();
        foreach (var item in arr)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            var attrId = GetString(entry, "attrId") ?? GetString(entry, "attributeId");
            if (string.IsNullOrWhiteSpace(attrId) || !IsInt64Id(attrId))
            {
                continue;
            }

            var entryCategoryId = GetString(entry, "categoryId")?.Trim();
            if (!string.IsNullOrWhiteSpace(payloadCategoryId)
                && !string.IsNullOrWhiteSpace(entryCategoryId)
                && !string.Equals(entryCategoryId, payloadCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // attrType 2 = sale prop (Color/Size/Number of items); it travels in option{N}, never here.
            if (string.Equals(GetString(entry, "attrType")?.Trim(), "2", StringComparison.Ordinal))
            {
                continue;
            }

            var schemaAttr = new EmbeddedCategoryAttribute
            {
                AttrId = attrId.Trim(),
                AttrName = GetString(entry, "attrName") ?? GetString(entry, "attributeName") ?? attrId.Trim(),
                AllowsCustomValue = ReadEmbeddedFlag(entry, "isCustomized"),
                AllowsMultiple = ReadEmbeddedFlag(entry, "isMultipleSelected"),
                IsRequired = ReadEmbeddedFlag(entry, "isMandatory") || ReadEmbeddedFlag(entry, "isRequired")
            };

            if (parseValuesFor.Contains(schemaAttr.AttrId))
            {
                var values = ReadEmbeddedAttributeValues(entry);
                var allowed = values
                    .Where(v => denylist?.IsRejected(schemaAttr.AttrId, v.ValueName) != true)
                    .ToList();

                // Same rule as WithoutRejectedValues: a mandatory enum keeps every value when the denylist
                // leaves nothing that can answer it, because a row that resolves to nothing is dropped and
                // the publish then fails on the missing mandatory attribute instead.
                var usable = schemaAttr.IsRequired
                             && !allowed.Any(v => ListingSpecificsMappingEngine.ValueCanAnswerRequiredAttribute(
                                 schemaAttr.AttrName, v.ValueName))
                    ? values
                    : allowed;
                foreach (var (valueId, valueName) in usable)
                {
                    var norm = ListingSpecificsMappingEngine.NormalizeKey(valueName);
                    if (norm.Length > 0)
                    {
                        schemaAttr.OptionByName.TryAdd(norm, (valueId, valueName));
                    }

                    if (IsInt64Id(valueId))
                    {
                        schemaAttr.OptionNameById.TryAdd(valueId, valueName);
                    }
                }
            }

            result[schemaAttr.AttrId] = schemaAttr;
        }

        return result;
    }

    /// <summary>4Seller sends these flags as <c>0</c>/<c>1</c> numbers, sometimes as strings/bools.</summary>
    private static bool ReadEmbeddedFlag(JsonObject entry, string key)
    {
        var raw = GetString(entry, key)?.Trim();
        return raw is not null
               && (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><c>valueList</c> is a JSON string of <c>[{id,name}]</c> (occasionally a real array).</summary>
    private static List<(string ValueId, string ValueName)> ReadEmbeddedAttributeValues(JsonObject entry)
    {
        var values = new List<(string ValueId, string ValueName)>();
        var arr = GetJsonArrayFlexible(entry, "valueList") ?? GetJsonArrayFlexible(entry, "values");
        if (arr is null)
        {
            return values;
        }

        foreach (var item in arr)
        {
            if (item is not JsonObject v)
            {
                continue;
            }

            var name = GetString(v, "name") ?? GetString(v, "valueName");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = GetString(v, "id") ?? GetString(v, "valueId") ?? string.Empty;
            values.Add((id.Trim(), name.Trim()));
        }

        return values;
    }

    /// <summary>
    /// Attribute ids already spoken for by the sale-prop axes (<c>option{N}</c> = <c>{"100000":"Color"}</c>).
    /// Those values travel on the variations; repeating them in <c>productAttributes</c> is what TikTok
    /// answers with 【Unpublishable】Product attributes are unavailable.
    /// </summary>
    private static HashSet<string> CollectSalePropAttrIds(JsonObject obj)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var slot = 1; slot <= ListingSalePropSync.SlotCount; slot++)
        {
            foreach (var key in ReadJsonMapKeys(obj, ListingSalePropSync.OptionKeyForSlot(slot)))
            {
                if (IsInt64Id(key))
                {
                    ids.Add(key.Trim());
                }
            }
        }

        return ids;
    }

    private static List<string> ReadJsonMapKeys(JsonObject obj, string key)
    {
        var keys = new List<string>();
        JsonNode? node = obj[key];
        if (node is JsonValue jv && jv.TryGetValue<string>(out var raw))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return keys;
            }

            try
            {
                node = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                return keys;
            }
        }

        switch (node)
        {
            case JsonObject map:
                keys.AddRange(map.Select(p => p.Key));
                break;
            case JsonArray arr:
                foreach (var entry in arr.OfType<JsonObject>())
                {
                    keys.AddRange(entry.Select(p => p.Key));
                }

                break;
        }

        return keys;
    }

    /// <summary>
    /// Aligns <c>productAttributes</c> with the category schema the payload already carries, because a
    /// numeric attribute id is not enough for TikTok: the id must belong to <b>this</b> category's
    /// product attributes, and an enum value must carry its <c>valueId</c>.
    /// <para>
    /// Rows arriving from the supplier detail are plain <c>name: value</c> text with no ids, and Auto
    /// Specifics deliberately skips attributes that already hold a value — so nothing used to give them
    /// a <c>valueId</c>, and a sale prop echoed into the list (Color) stayed there. Both faults answer
    /// with 【Unpublishable】Product attributes are unavailable, which names no row.
    /// </para>
    /// </summary>
    internal static ProductAttributesSchemaAudit CanonicalizeProductAttributesAgainstSchema(
        JsonObject obj,
        TikTokAttributeValueDenylist? denylist = null,
        IReadOnlyList<FourSellerCategoryAttributeDto>? liveSchema = null)
    {
        var audit = new ProductAttributesSchemaAudit();
        var wasString = IsJsonStringField(obj, "productAttributes")
                        || IsJsonStringField(obj, "attributes")
                        || IsJsonStringField(obj, "attributeList");

        var arr = GetJsonArrayFlexible(obj, "productAttributes")
                  ?? GetJsonArrayFlexible(obj, "attributes")
                  ?? GetJsonArrayFlexible(obj, "attributeList");
        if (arr is null || arr.Count == 0)
        {
            return audit;
        }

        var rowAttrIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arr.OfType<JsonObject>())
        {
            var id = (GetString(item, "attributeId") ?? GetString(item, "attrId") ?? GetString(item, "id"))?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                rowAttrIds.Add(id);
            }
        }

        // The schema still offers values TikTok has withdrawn, and a stored RawPayload can hold one from
        // an earlier push, so drop them here: every match below then works off valid options only,
        // whatever the attribute.
        // The live schema wins when the caller has it: the copy embedded in the payload describes the
        // category the listing was crawled with, so after a category change (B2 Category AI, or an edit)
        // it either belongs to another category and is skipped entirely, or — worse — validates rows
        // against the wrong mandatory set.
        var schema = liveSchema is { Count: > 0 }
            ? ToEmbeddedSchema(liveSchema, denylist)
            : ReadEmbeddedCategoryAttributes(obj, rowAttrIds, denylist);
        var salePropIds = CollectSalePropAttrIds(obj);
        var gatedAttrIds = liveSchema is { Count: > 0 }
            ? CollectClosedGateAttrIds(liveSchema, arr)
            : [];
        var kept = new JsonArray();

        foreach (var item in arr)
        {
            if (item is not JsonObject row)
            {
                continue;
            }

            var attrId = (GetString(row, "attributeId") ?? GetString(row, "attrId") ?? GetString(row, "id"))
                ?.Trim() ?? string.Empty;
            var attrName = (GetString(row, "attributeName") ?? GetString(row, "attrName") ?? GetString(row, "name"))
                ?.Trim() ?? attrId;
            var label = string.IsNullOrWhiteSpace(attrName) ? attrId : $"{attrName} ({attrId})";

            if (salePropIds.Contains(attrId))
            {
                audit.Dropped.Add($"{label}: sale prop — thuộc option{{N}}/variations, không phải Specifics");
                continue;
            }

            if (gatedAttrIds.Contains(attrId))
            {
                audit.Dropped.Add(
                    $"{label}: requirementConditions chưa mở (attribute chỉ hợp lệ khi thuộc tính điều kiện được chọn)");
                continue;
            }

            // No usable schema (older payload, or the form moved the listing to another category):
            // keep the row and let the existing Int64 guard in PatchAttributes decide.
            if (schema.Count == 0)
            {
                kept.Add(row.DeepClone()!);
                continue;
            }

            if (!schema.TryGetValue(attrId, out var schemaAttr))
            {
                audit.Dropped.Add($"{label}: không thuộc schema thuộc tính của category này");
                continue;
            }

            var resolved = ResolveRowValuesAgainstSchema(row, schemaAttr);
            if (resolved.Count == 0
                && ListingSpecificsMappingEngine.IsWashingInstructionAttribute(schemaAttr.AttrName))
            {
                // Apparel needs this attribute, so dropping the row only trades one publish failure for
                // another. Replace the value TikTok withdrew with one that still describes how to wash.
                var replacement = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
                    ToCategoryAttributeDto(schemaAttr));
                if (replacement is not null)
                {
                    resolved.Add((replacement.ValueId ?? string.Empty, replacement.ValueName));
                    audit.Repaired.Add($"{label}: giá trị cũ TikTok không nhận → {replacement.ValueName}");
                }
            }

            if (resolved.Count == 0)
            {
                audit.Dropped.Add($"{label}: giá trị không có trong enum của thuộc tính");
                continue;
            }

            if (RowNeedsRewrite(row, schemaAttr, resolved))
            {
                audit.Repaired.Add(label);
            }

            WriteCanonicalRowValues(row, schemaAttr, resolved);
            kept.Add(row.DeepClone()!);
        }

        if (!audit.HasChanges)
        {
            return audit;
        }

        WriteProductAttributesField(obj, kept, preferJsonString: wasString);
        SetArrayFieldIfCompatible(obj, "attributeList", kept.DeepClone()!.AsArray());
        SetArrayFieldIfCompatible(obj, "attributes", kept.DeepClone()!.AsArray());
        return audit;
    }

    /// <summary>
    /// Matches the row's values against the schema enum: an id the form already resolved wins, then an
    /// exact-after-normalization name match supplies the missing id. A value that matches no option is
    /// kept only when the attribute accepts custom text.
    /// </summary>
    private static List<(string ValueId, string ValueName)> ResolveRowValuesAgainstSchema(
        JsonObject row,
        EmbeddedCategoryAttribute schemaAttr)
    {
        var resolved = new List<(string ValueId, string ValueName)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawId, rawName) in ReadRowValues(row))
        {
            var name = rawName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var id = rawId.Trim();
            string valueId;
            string valueName;

            // Name before id on purpose: the Specifics card edits the value text while the resolved
            // id stays hidden, so trusting the id would silently restore the value the seller just
            // replaced. The id only decides when the typed name matches no option.
            if (schemaAttr.OptionByName.TryGetValue(
                    ListingSpecificsMappingEngine.NormalizeKey(name),
                    out var hit))
            {
                (valueId, valueName) = (hit.ValueId, hit.ValueName);
            }
            else if (IsInt64Id(id) && schemaAttr.OptionNameById.TryGetValue(id, out var nameForId))
            {
                (valueId, valueName) = (id, nameForId);
            }
            else if (TryResolveEnumByFuzzyName(schemaAttr, name, out var fuzzyId, out var fuzzyName))
            {
                (valueId, valueName) = (fuzzyId, fuzzyName);
            }
            else if (schemaAttr.OptionByName.Count > 0)
            {
                // An enum exists but nothing matched, so the value can only reach TikTok as a custom
                // name — which TikTok refuses for attributes 4Seller still marks isCustomized=1
                // (100401 in category 903824 is the proven case). Dropping an optional row costs a
                // detail; keeping it costs the whole publish. Mandatory rows get a default seeded
                // from this same enum in EnsureConditionSpecificsInPayloadAsync.
                continue;
            }
            else
            {
                (valueId, valueName) = (IsInt64Id(id) ? id : string.Empty, name);
            }

            if (!seen.Add(valueId.Length > 0 ? valueId : valueName))
            {
                continue;
            }

            resolved.Add((valueId, valueName));
            if (!schemaAttr.AllowsMultiple
                || resolved.Count >= ListingSpecificsMappingEngine.MaxMultiValuesPerAttribute)
            {
                break;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Fuzzy-maps a typed Specifics value onto a schema enum — especially care phrases like
    /// <c>Hand wash only</c> → <c>Hand wash</c> for Washing instructions.
    /// </summary>
    private static bool TryResolveEnumByFuzzyName(
        EmbeddedCategoryAttribute schemaAttr,
        string rawName,
        out string valueId,
        out string valueName)
    {
        valueId = string.Empty;
        valueName = string.Empty;
        if (schemaAttr.OptionByName.Count == 0)
        {
            return false;
        }

        if (ListingSpecificsMappingEngine.IsWashingInstructionAttribute(schemaAttr.AttrName))
        {
            var best = ListingSpecificsMappingEngine.ResolveBestWashingEnumValue(
                rawName,
                ToCategoryAttributeDto(schemaAttr).Values);
            if (best is null || string.IsNullOrWhiteSpace(best.ValueName))
            {
                return false;
            }

            valueId = best.ValueId ?? string.Empty;
            valueName = best.ValueName;
            return true;
        }

        var sourceNorm = ListingSpecificsMappingEngine.NormalizeKey(rawName);
        if (sourceNorm.Length == 0)
        {
            return false;
        }

        string? bestId = null;
        string? bestName = null;
        var bestScore = 0.0;
        foreach (var (norm, opt) in schemaAttr.OptionByName)
        {
            double score;
            if (sourceNorm == norm)
            {
                score = 1.0;
            }
            else if (sourceNorm.Contains(norm, StringComparison.Ordinal)
                     || norm.Contains(sourceNorm, StringComparison.Ordinal))
            {
                var shorter = Math.Min(sourceNorm.Length, norm.Length);
                var longer = Math.Max(sourceNorm.Length, norm.Length);
                score = shorter / (double)longer;
            }
            else
            {
                continue;
            }

            if (score >= 0.80 && score > bestScore)
            {
                bestScore = score;
                bestId = opt.ValueId;
                bestName = opt.ValueName;
            }
        }

        if (bestName is null)
        {
            return false;
        }

        valueId = bestId ?? string.Empty;
        valueName = bestName;
        return true;
    }

    /// <summary>
    /// True when the resolved row differs from what the payload holds — a filled <c>valueId</c>, a
    /// canonical spelling, or a value trimmed off a single-select attribute. The whole array is only
    /// rewritten when something actually changed, so an already-canonical payload is left byte-identical.
    /// </summary>
    private static bool RowNeedsRewrite(
        JsonObject row,
        EmbeddedCategoryAttribute schemaAttr,
        List<(string ValueId, string ValueName)> resolved)
    {
        var current = ReadRowValues(row);
        if (current.Count != resolved.Count)
        {
            return true;
        }

        for (var i = 0; i < resolved.Count; i++)
        {
            if (!string.Equals(current[i].ValueId.Trim(), resolved[i].ValueId, StringComparison.Ordinal)
                || !string.Equals(current[i].ValueName.Trim(), resolved[i].ValueName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // A row that carries values[] but no flat attrValue/valueId mirror still needs rewriting: 4Seller
        // reads the id off the flat keys and publishes the value as a custom name without them.
        if (!string.Equals(GetString(row, "valueId")?.Trim(), resolved[0].ValueId, StringComparison.Ordinal)
            || !string.Equals(GetString(row, "attrValue")?.Trim(), resolved[0].ValueName, StringComparison.Ordinal))
        {
            return true;
        }

        // Casing / punctuation differences are cosmetic (4Seller sends "Dangerous Goods Or Hazardous
        // Materials" where the schema says "Dangerous goods or hazardous materials") — not worth
        // rewriting the array and logging a repair on every single push.
        var currentName = GetString(row, "attributeName") ?? GetString(row, "attrName") ?? GetString(row, "name");
        return ListingSpecificsMappingEngine.NormalizeKey(currentName)
               != ListingSpecificsMappingEngine.NormalizeKey(schemaAttr.AttrName);
    }

    private static List<(string ValueId, string ValueName)> ReadRowValues(JsonObject row)
    {
        var values = new List<(string ValueId, string ValueName)>();
        if (row["values"] is JsonArray arr && arr.Count > 0)
        {
            foreach (var v in arr.OfType<JsonObject>())
            {
                var name = GetString(v, "valueName") ?? GetString(v, "value") ?? GetString(v, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                values.Add((GetString(v, "valueId") ?? string.Empty, name));
            }

            if (values.Count > 0)
            {
                return values;
            }
        }

        var single = GetString(row, "attrValue") ?? GetString(row, "value") ?? GetString(row, "attributeValue");
        if (!string.IsNullOrWhiteSpace(single))
        {
            values.Add((GetString(row, "valueId") ?? string.Empty, single));
        }

        return values;
    }

    /// <summary>
    /// Writes the resolved values back in place, keeping whichever keys the row already used so the
    /// golden save-draft shape and the simplified hydrate mirrors both stay consistent.
    /// </summary>
    private static void WriteCanonicalRowValues(
        JsonObject row,
        EmbeddedCategoryAttribute schemaAttr,
        List<(string ValueId, string ValueName)> resolved)
    {
        var values = new JsonArray();
        foreach (var (valueId, valueName) in resolved)
        {
            values.Add(new JsonObject
            {
                ["valueId"] = valueId,
                ["valueName"] = valueName,
                ["isShow"] = "true"
            });
        }

        row["values"] = values;
        row["attributeId"] = schemaAttr.AttrId;
        row["attributeName"] = schemaAttr.AttrName;

        var primaryName = resolved[0].ValueName;
        var primaryId = resolved[0].ValueId;

        // Written whether or not the row already carried them: 4Seller resolves the id off these flat
        // keys, and a row holding only values[] published as a custom value name — TikTok answered
        // "attribute ID 100401 does not support custom value names" for a payload whose nested valueId
        // was correct. Filling only the keys that happen to exist leaves exactly that hole.
        foreach (var key in new[] { "attrId", "id" })
        {
            row[key] = schemaAttr.AttrId;
        }

        foreach (var key in new[] { "attrName", "name" })
        {
            row[key] = schemaAttr.AttrName;
        }

        foreach (var key in new[] { "attrValue", "value" })
        {
            row[key] = primaryName;
        }

        if (row.ContainsKey("attributeValue"))
        {
            row["attributeValue"] = primaryName;
        }

        row["valueId"] = primaryId;
    }

    private static void RewriteVariationArrays(
        JsonObject obj,
        ICollection<ListingVariation> variations,
        IReadOnlyDictionary<string, int> axisSlots,
        string? warehouseId)
    {
        // TikTok detail/save-draft uses `variation` (singular). Multiple variation array keys
        // alongside each other → 4Seller 业务异常参数错误.
        var primaryKey = ResolveVariationArrayKey(obj);
        obj[primaryKey] = BuildPatchedVariationArray(obj, primaryKey, variations, axisSlots, warehouseId);
        RemoveDuplicateVariationArrayKeys(obj, primaryKey);
    }

    /// <summary>
    /// Coerces RawPayload field types to the shapes 4Seller browser <c>save-draft</c> sends.
    /// Internal hydrate may keep <c>skuImgs</c> as <c>color→url[]</c> object; push must send a
    /// JSON <b>string</b> of <c>[{attrName,img_url,custom_value}]</c> or 4Seller returns 4100.
    /// </summary>
    private static void SanitizePayloadForFourSellerPush(JsonObject obj, Listing listing)
    {
        // 4Seller rejects explicit http:// media URLs (Invalid Para). Upgrade every
        // string node (mainImage, skuImgs pipes, variation[].mainImage, HTML img src, …).
        UpgradeHttpUrlsInJsonNode(obj);
        ListingImageUrlFilters.StripGifUrlsFromPayload(obj);
        ListingImageUrlFilters.StripProductVideoFromPayload(obj);

        // LOCAL TO LOCAL golden body uses isGlobal:0 — null → 业务异常参数错误.
        if (obj["isGlobal"] is null || obj["isGlobal"]!.GetValueKind() == JsonValueKind.Null)
        {
            obj["isGlobal"] = 0;
        }

        if (obj["requireMessage"] is null || obj["requireMessage"]!.GetValueKind() == JsonValueKind.Null)
        {
            obj["requireMessage"] = "";
        }

        NormalizeRootShippingUnitsForPush(obj, listing);

        // Listing id must be a string in save-draft (golden: "833495280716").
        if (TryGetLong(obj, "id", out var listingId) && listingId > 0)
        {
            obj["id"] = listingId.ToString(CultureInfo.InvariantCulture);
        }
        else if (listing.FourSellerListingId > 0)
        {
            obj["id"] = listing.FourSellerListingId.ToString(CultureInfo.InvariantCulture);
        }

        EnsureJsonStringField(obj, "productAttributes");
        EnsureJsonStringField(obj, "brandInfo");
        EnsureJsonStringField(obj, "productCertifications");
        EnsureJsonStringField(obj, "certifications");
        // Root option maps are JSON strings with TikTok attr ids — never leave as objects.
        EnsureJsonStringField(obj, "option1");
        EnsureJsonStringField(obj, "option2");
        EnsureJsonStringField(obj, "option1Value");
        EnsureJsonStringField(obj, "option2Value");
        EnsureJsonStringField(obj, "option3");
        EnsureJsonStringField(obj, "option3Value");

        foreach (var dimKey in new[] { "packageLength", "packageWidth", "packageHeight" })
        {
            if (obj[dimKey] is JsonValue jv && jv.GetValueKind() is JsonValueKind.Number)
            {
                obj[dimKey] = jv.ToString();
            }
        }

        obj["skuImgs"] = ToSkuImgsPushString(obj["skuImgs"]);
        // salePropImageMap is not in the golden save-draft body; object form risks 4100.
        if (obj["salePropImageMap"] is JsonObject or JsonArray)
        {
            obj.Remove("salePropImageMap");
        }

        var variationKey = ResolveVariationArrayKey(obj);
        if (obj[variationKey] is JsonArray varArr)
        {
            foreach (var item in varArr)
            {
                if (item is not JsonObject vObj)
                {
                    continue;
                }

                SanitizeVariationForPush(vObj);
            }
        }
    }

    /// <summary>
    /// Browser save-draft/publish sends root <c>POUND</c>/<c>INCH</c> while variant rows keep short
    /// <c>lb</c>/<c>inch</c>. Local form columns use short units — coerce root only at push time.
    /// </summary>
    private static void NormalizeRootShippingUnitsForPush(JsonObject obj, Listing listing)
    {
        var weight = GetString(obj, "weightUnit") ?? listing.WeightUnit;
        if (!string.IsNullOrWhiteSpace(weight))
        {
            obj["weightUnit"] = MapRootWeightUnit(weight);
        }

        var dimension = GetString(obj, "dimensionUnit") ?? listing.DimensionUnit;
        if (!string.IsNullOrWhiteSpace(dimension))
        {
            obj["dimensionUnit"] = MapRootDimensionUnit(dimension);
        }
    }

    private static string MapRootWeightUnit(string unit)
    {
        var normalized = unit.Trim().ToLowerInvariant();
        return normalized switch
        {
            "lb" or "lbs" or "pound" or "pounds" => "POUND",
            "kg" or "kgs" or "kilogram" or "kilograms" => "KILOGRAM",
            _ => unit.Trim()
        };
    }

    private static string MapRootDimensionUnit(string unit)
    {
        var normalized = unit.Trim().ToLowerInvariant();
        return normalized switch
        {
            "inch" or "inches" or "in" => "INCH",
            "cm" or "centimeter" or "centimeters" or "centimetre" or "centimetres" => "CM",
            _ => unit.Trim()
        };
    }

    private static void SanitizeVariationForPush(JsonObject vObj)
    {
        // Single URL only (pipe-joined sets belong in skuImgs Color rows).
        var main = GetString(vObj, "mainImage");
        if (!string.IsNullOrWhiteSpace(main) && main.Contains('|'))
        {
            vObj["mainImage"] = FirstImage(main) ?? main.Split('|')[0].Trim();
        }

        if (TryGetLong(vObj, "id", out var skuId) && skuId > 0)
        {
            if (vObj["keyId"] is null || vObj["keyId"]!.GetValueKind() == JsonValueKind.Null)
            {
                vObj["keyId"] = skuId;
            }
        }

        if (vObj["type"] is null || vObj["type"]!.GetValueKind() == JsonValueKind.Null)
        {
            vObj["type"] = "auto";
        }

        // Always derived from the option labels of this row — a stale attrs array from the cloned
        // detail is exactly what makes 4Seller reject the SKU after a Color/Size edit.
        ListingSalePropSync.RebuildVariationAttrsArray(vObj, hasAxes: false);

        if (vObj["availableStock"] is null || vObj["availableStock"]!.GetValueKind() == JsonValueKind.Null)
        {
            if (GetDecimal(vObj, "quantity") is { } qty)
            {
                vObj["availableStock"] = qty;
            }
            else if (vObj["stockInfoList"] is JsonArray stockArr)
            {
                foreach (var s in stockArr)
                {
                    if (s is JsonObject so &&
                        (GetDecimal(so, "available_stock") ?? GetDecimal(so, "availableStock")) is { } stock)
                    {
                        vObj["availableStock"] = stock;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Browser save-draft sends <c>skuImgs</c> as a JSON <b>string</b> of Color rows, not a map object.
    /// </summary>
    private static JsonNode ToSkuImgsPushString(JsonNode? node)
    {
        if (node is JsonValue jv && jv.TryGetValue(out string? existing) && !string.IsNullOrWhiteSpace(existing))
        {
            // Already a string — keep if it parses as array; otherwise wrap empty.
            try
            {
                if (JsonNode.Parse(existing) is JsonArray)
                {
                    return JsonValue.Create(existing)!;
                }
            }
            catch (JsonException)
            {
                // fall through to rebuild
            }
        }

        var rows = new JsonArray();
        if (node is JsonObject map)
        {
            foreach (var kv in map)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Key.Contains('/'))
                {
                    continue;
                }

                var urls = ExtractUrlList(kv.Value);
                if (urls.Count == 0)
                {
                    continue;
                }

                rows.Add(new JsonObject
                {
                    ["attrName"] = "Color",
                    ["img_url"] = string.Join('|', urls),
                    ["custom_value"] = kv.Key
                });
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject row)
                {
                    rows.Add(row.DeepClone());
                }
            }
        }
        else if (node is JsonValue strVal &&
                 strVal.TryGetValue(out string? raw) &&
                 !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (JsonNode.Parse(raw) is JsonArray parsed)
                {
                    return JsonValue.Create(raw)!;
                }
            }
            catch (JsonException)
            {
                // ignore
            }
        }

        return JsonValue.Create(rows.ToJsonString())!;
    }

    private static List<string> ExtractUrlList(JsonNode? node)
    {
        var urls = new List<string>();
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var u = item?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(u))
                {
                    urls.Add(u);
                }
            }
        }
        else if (node is JsonValue jv && jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
        {
            urls.AddRange(s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
    }

    private static void EnsureJsonStringField(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return;
        }

        if (node is JsonValue jv && jv.TryGetValue(out string? _))
        {
            return;
        }

        if (node is JsonObject or JsonArray)
        {
            obj[key] = node.ToJsonString();
        }
    }

    private static string ResolveVariationArrayKey(JsonObject obj)
    {
        foreach (var key in VariationArrayKeys)
        {
            if (obj[key] is JsonArray)
            {
                return key;
            }
        }

        return "variation";
    }

    private static readonly string[] VariationArrayKeys =
        ["variation", "variationList", "variations", "skuList", "skus"];

    private static void RemoveDuplicateVariationArrayKeys(JsonObject obj, string primaryKey)
    {
        foreach (var key in VariationArrayKeys)
        {
            if (!string.Equals(key, primaryKey, StringComparison.Ordinal) && obj[key] is JsonArray)
            {
                obj.Remove(key);
            }
        }
    }

    /// <summary>
    /// Maps a sync shop filter to the 4Seller shop id string. Accepts either the numeric 4Seller id
    /// or a local <c>Shops.Id</c> Guid (UI dropdown value).
    /// </summary>
    private async Task<string?> ResolveFourSellerShopFilterAsync(
        Guid ownerId,
        string? shopId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shopId))
        {
            return null;
        }

        var trimmed = shopId.Trim();
        if (long.TryParse(trimmed, out var asFourSellerId) && asFourSellerId > 0)
        {
            return asFourSellerId.ToString(CultureInfo.InvariantCulture);
        }

        if (!Guid.TryParse(trimmed, out var localShopId))
        {
            return trimmed;
        }

        var mapped = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == localShopId && s.OwnerId == ownerId)
            .Select(s => s.FourSellerShopId)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(mapped) ? null : mapped.Trim();
    }

    /// <summary>
    /// First occurrence of each Color|Size combo — mirrors <see cref="SyncVariations"/> dedupe.
    /// </summary>
    private static IEnumerable<FourSellerListingVariationDto> DedupedIncomingVariations(
        IReadOnlyList<FourSellerListingVariationDto> variations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in variations)
        {
            var combo = VariationComboKey(
                ResolveVariationAxisAttrs(v.Title, BuildVariantAttributesJson(v.Title)));
            if (combo is null)
            {
                yield return v;
                continue;
            }

            if (!seen.Add(combo))
            {
                continue;
            }

            yield return v;
        }
    }

    /// <summary>
    /// Combo key used to line raw payload rows up with local variations. Matching on the sale-prop
    /// combo first keeps a raw row bound to the same Color/Size cell; SKU code and sku id are only
    /// fallbacks because Batch SKU rewrites codes and dropping an axis re-points ids.
    /// </summary>
    private static string? VariationComboKey(IReadOnlyDictionary<string, string> attrs)
    {
        var parts = ListingSalePropSync.AxisOrder
            .Select(axis => (Axis: axis, Value: attrs.FirstOrDefault(kv =>
                axis.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Axis}={p.Value.Trim().ToLowerInvariant()}")
            .ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    private static string? RawRowComboKey(JsonObject vObj, IReadOnlyDictionary<string, int> axisSlots)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var slot = 1; slot <= ListingSalePropSync.SlotCount; slot++)
        {
            var axis = ListingSalePropSync.AxisForSlot(axisSlots, slot);
            if (axis is null)
            {
                continue;
            }

            var label = GetString(vObj, ListingSalePropSync.OptionKeyForSlot(slot));
            if (!string.IsNullOrWhiteSpace(label))
            {
                attrs[axis] = label;
            }
        }

        if (attrs.Count == 0)
        {
            attrs = ResolveVariationAxisAttrs(
                GetString(vObj, "title") ?? GetString(vObj, "name"),
                GetString(vObj, "variantAttributes"));
        }

        return VariationComboKey(attrs);
    }

    private static JsonArray BuildPatchedVariationArray(
        JsonObject obj,
        string key,
        ICollection<ListingVariation> variations,
        IReadOnlyDictionary<string, int> axisSlots,
        string? warehouseId)
    {
            var existingBySkuId = new Dictionary<long, JsonObject>();
            var existingBySellerSku = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var duplicateRawSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingOrder = new List<(long? SkuId, string? SellerSku, string? Combo, JsonObject Obj)>();

            if (obj.TryGetPropertyValue(key, out var arrNode) && arrNode is JsonArray existingArr)
            {
                foreach (var item in existingArr)
                {
                    if (item is not JsonObject vObj)
                    {
                        continue;
                    }

                    long? skuId = null;
                    if (TryGetLong(vObj, "id", out var id) && id > 0)
                    {
                        skuId = id;
                        existingBySkuId[id] = vObj;
                    }

                    var sellerSku = GetString(vObj, "sellerSku") ?? GetString(vObj, "seller_sku");
                    if (!string.IsNullOrWhiteSpace(sellerSku))
                    {
                        if (!existingBySellerSku.TryAdd(sellerSku, vObj))
                        {
                            duplicateRawSkus.Add(sellerSku);
                        }
                    }

                    existingOrder.Add((skuId, sellerSku, RawRowComboKey(vObj, axisSlots), vObj));
                }
            }

            // Combo key parsing is JSON work — resolve once per variation, not per raw row.
            var localCombo = variations.ToDictionary(
                v => v.Id,
                v => VariationComboKey(ResolveVariationAxisAttrs(v.Title, v.VariantAttributes)));
            var uniqueByCombo = variations
                .Where(v => localCombo[v.Id] is not null)
                .GroupBy(v => localCombo[v.Id]!, StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var uniqueBySku = variations
                .Where(v => !string.IsNullOrWhiteSpace(v.SellerSku))
                .GroupBy(v => v.SellerSku!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var used = new HashSet<Guid>();
            var next = new JsonArray();

            ListingVariation? FindMatch(long? skuId, string? sellerSku, string? combo)
            {
                if (combo is not null &&
                    uniqueByCombo.TryGetValue(combo, out var byCombo) &&
                    !used.Contains(byCombo.Id))
                {
                    return byCombo;
                }

                if (skuId is > 0)
                {
                    var byId = variations.FirstOrDefault(v =>
                        !used.Contains(v.Id) && v.FourSellerSkuId == skuId);
                    if (byId is not null)
                    {
                        return byId;
                    }
                }

                if (!string.IsNullOrWhiteSpace(sellerSku) &&
                    !duplicateRawSkus.Contains(sellerSku) &&
                    uniqueBySku.TryGetValue(sellerSku.Trim(), out var bySku) &&
                    !used.Contains(bySku.Id))
                {
                    return bySku;
                }

                return null;
            }

            // Keep original SKU order when possible (4Seller is sensitive to variation shape).
            // Unmatched raw rows are dropped — user removed those variants on the edit form.
            foreach (var (skuId, sellerSku, combo, existingObj) in existingOrder)
            {
                var match = FindMatch(skuId, sellerSku, combo);
                if (match is null || !used.Add(match.Id))
                {
                    continue;
                }

                var baseObj = existingObj.DeepClone()!.AsObject();
                DetachSkuIdentityOnComboChange(baseObj, match, localCombo[match.Id], combo);
                ApplyVariationToJson(baseObj, match, axisSlots, warehouseId);
                next.Add(baseObj);
            }

            foreach (var variation in InPushOrder(variations))
            {
                if (!used.Add(variation.Id))
                {
                    continue;
                }

                JsonObject? baseObj = null;
                if (variation.FourSellerSkuId is > 0 &&
                    existingBySkuId.TryGetValue(variation.FourSellerSkuId.Value, out var byId))
                {
                    baseObj = byId.DeepClone()!.AsObject();
                }
                else if (!string.IsNullOrWhiteSpace(variation.SellerSku) &&
                         !duplicateRawSkus.Contains(variation.SellerSku) &&
                         existingBySellerSku.TryGetValue(variation.SellerSku, out var bySku))
                {
                    baseObj = bySku.DeepClone()!.AsObject();
                }

                if (baseObj is not null)
                {
                    DetachSkuIdentityOnComboChange(
                        baseObj,
                        variation,
                        localCombo[variation.Id],
                        RawRowComboKey(baseObj, axisSlots));
                }

                baseObj ??= new JsonObject();
                ApplyVariationToJson(baseObj, variation, axisSlots, warehouseId);
                next.Add(baseObj);
            }

        return next;
    }

    /// <summary>
    /// A raw row reused for a different sale-prop combo must not keep the live TikTok sku id —
    /// 4Seller would rename an existing SKU into another combo instead of creating a new one.
    /// </summary>
    private static void DetachSkuIdentityOnComboChange(
        JsonObject baseObj,
        ListingVariation match,
        string? localCombo,
        string? rawCombo)
    {
        if (localCombo is null || rawCombo is null || localCombo == rawCombo)
        {
            return;
        }

        baseObj.Remove("id");
        baseObj.Remove("keyId");
        match.FourSellerSkuId = null;
    }

    private static List<string> ValidateListingPayloadForPush(JsonObject obj, Listing listing)
    {
        var errors = new List<string>();

        var variationKeys = VariationArrayKeys.Where(k => obj[k] is JsonArray).ToList();
        if (variationKeys.Count > 1)
        {
            errors.Add(
                $"Payload có {variationKeys.Count} mảng variant ({string.Join(", ", variationKeys)}) — 4Seller chỉ chấp nhận một key (TikTok thường dùng `variation`).");
        }

        var productName = GetString(obj, "productName") ?? listing.ProductName;
        if (string.IsNullOrWhiteSpace(productName))
        {
            errors.Add("Thiếu tên sản phẩm (productName).");
        }

        var categoryId = GetString(obj, "categoryId") ?? listing.CategoryId;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            errors.Add("Thiếu danh mục TikTok (categoryId) — chọn category trên form hoặc đồng bộ lại từ 4Seller.");
        }

        if (!TryGetLong(obj, "shopId", out var shopId) || shopId <= 0)
        {
            if (listing.FourSellerShopId is null or <= 0)
            {
                errors.Add("Thiếu shopId — gán shop cho listing hoặc đồng bộ lại từ 4Seller.");
            }
            else
            {
                obj["shopId"] = listing.FourSellerShopId.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(GetString(obj, "warehouseId") ?? listing.WarehouseId))
        {
            var hydratedWarehouse = TryHydrateWarehouseId(obj);
            if (!string.IsNullOrWhiteSpace(hydratedWarehouse))
            {
                obj["warehouseId"] = hydratedWarehouse;
                listing.WarehouseId = hydratedWarehouse;
            }
            else
            {
                errors.Add(
                    "Thiếu warehouseId — shop chưa có kho mặc định từ 4Seller (đổi shop hoặc đồng bộ lại).");
            }
        }
        else if (string.IsNullOrWhiteSpace(GetString(obj, "warehouseId")) &&
                 !string.IsNullOrWhiteSpace(listing.WarehouseId))
        {
            obj["warehouseId"] = listing.WarehouseId;
        }

        var variationKey = ResolveVariationArrayKey(obj);
        var variationArr = obj[variationKey] as JsonArray;
        if (variationArr is null || variationArr.Count == 0)
        {
            if (listing.Variations.Count == 0)
            {
                errors.Add("Listing không có variant — thêm ít nhất 1 SKU với giá bán và tồn kho.");
            }
            else
            {
                errors.Add($"Mảng `{variationKey}` trống sau khi patch — kiểm tra lại variant trên form.");
            }
        }
        else
        {
            var axisSlots = ListingSalePropSync.ResolveAxisSlots(
                obj,
                BuildVariationAxisAttrs(listing.Variations));
            var declaredLabels = Enumerable.Range(1, ListingSalePropSync.SlotCount)
                .ToDictionary(slot => slot, slot => ListingSalePropSync.DeclaredLabels(obj, slot));
            var seenCombos = new Dictionary<string, int>(StringComparer.Ordinal);
            var seenSkus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < variationArr.Count; i++)
            {
                if (variationArr[i] is not JsonObject vObj)
                {
                    continue;
                }

                var sellerSku = GetString(vObj, "sellerSku") ?? GetString(vObj, "seller_sku");
                var label = !string.IsNullOrWhiteSpace(sellerSku) ? $"SKU `{sellerSku}`" : $"Variant #{i + 1}";

                if (string.IsNullOrWhiteSpace(sellerSku))
                {
                    errors.Add($"{label}: thiếu sellerSku (MSKU).");
                }
                else if (!seenSkus.TryAdd(sellerSku.Trim(), i))
                {
                    errors.Add(
                        $"{label}: sellerSku trùng với variant #{seenSkus[sellerSku.Trim()] + 1} — " +
                        "dùng Batch SKU để sinh lại mã duy nhất.");
                }

                if (GetDecimal(vObj, "originalPrice") is null &&
                    GetDecimal(vObj, "salePrice") is null &&
                    GetDecimal(vObj, "price") is null)
                {
                    errors.Add($"{label}: thiếu giá bán (originalPrice).");
                }

                if (!VariationHasStock(vObj))
                {
                    errors.Add($"{label}: thiếu tồn kho (availableStock / quantity / stockInfoList).");
                }

                if (axisSlots.Count == 0)
                {
                    continue;
                }

                // Sale-prop parity: every option label must be declared in the matching
                // option{N}Value map, and attrs must mirror exactly those labels.
                var optionLabels = new List<string>();
                var comboParts = new List<string>();
                for (var slot = 1; slot <= ListingSalePropSync.SlotCount; slot++)
                {
                    var axis = ListingSalePropSync.AxisForSlot(axisSlots, slot);
                    var value = GetString(vObj, ListingSalePropSync.OptionKeyForSlot(slot));
                    if (axis is null)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            errors.Add(
                                $"{label}: còn `{ListingSalePropSync.OptionKeyForSlot(slot)}` = \"{value}\" " +
                                "nhưng thuộc tính này đã bị xóa khỏi listing.");
                        }

                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        errors.Add($"{label}: thiếu giá trị {axis}.");
                        continue;
                    }

                    if (!declaredLabels[slot].Contains(value.Trim()))
                    {
                        errors.Add(
                            $"{label}: {axis} = \"{value}\" chưa được khai báo trong " +
                            $"`{ListingSalePropSync.OptionKeyForSlot(slot)}Value` — đồng bộ lại listing từ 4Seller.");
                    }

                    optionLabels.Add(value.Trim());
                    comboParts.Add($"{axis}={value.Trim().ToLowerInvariant()}");
                }

                var attrsCount = vObj["attrs"] is JsonArray attrsArr ? attrsArr.Count : 0;
                if (attrsCount != optionLabels.Count)
                {
                    errors.Add(
                        $"{label}: `attrs` có {attrsCount} phần tử nhưng có {optionLabels.Count} " +
                        "thuộc tính bán — payload lệch, thử Save Draft lại.");
                }

                if (comboParts.Count > 0)
                {
                    var comboKey = string.Join('|', comboParts);
                    if (!seenCombos.TryAdd(comboKey, i))
                    {
                        errors.Add(
                            $"{label}: trùng thuộc tính với variant #{seenCombos[comboKey] + 1} — " +
                            "4Seller từ chối hai SKU cùng tổ hợp Color/Size.");
                    }
                }
            }
        }

        var mainImage = GetString(obj, "mainImage") ?? listing.MainImage;
        if (string.IsNullOrWhiteSpace(mainImage))
        {
            errors.Add("Thiếu ảnh sản phẩm chính (mainImage) — thêm ít nhất 1 ảnh gallery.");
        }

        if (obj["productAttributes"] is JsonArray attrArr && attrArr.Count == 0)
        {
            errors.Add("productAttributes rỗng — điền Specifics hoặc đồng bộ lại raw detail từ 4Seller.");
        }

        return errors;
    }

    private static string? TryHydrateWarehouseId(JsonObject obj)
    {
        var direct = GetString(obj, "warehouseId") ?? GetString(obj, "warehouse_id");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        foreach (var key in VariationArrayKeys)
        {
            if (obj[key] is not JsonArray arr)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject vObj)
                {
                    continue;
                }

                var fromVariant = GetString(vObj, "warehouseId") ?? GetString(vObj, "warehouse_id");
                if (!string.IsNullOrWhiteSpace(fromVariant))
                {
                    return fromVariant.Trim();
                }

                if (vObj["stockInfoList"] is not JsonArray stockArr)
                {
                    continue;
                }

                foreach (var stockItem in stockArr)
                {
                    if (stockItem is not JsonObject stockObj)
                    {
                        continue;
                    }

                    var wid = GetString(stockObj, "warehouse_id") ?? GetString(stockObj, "warehouseId");
                    if (!string.IsNullOrWhiteSpace(wid))
                    {
                        return wid.Trim();
                    }
                }
            }
        }

        return null;
    }

    internal static bool VariationHasStock(JsonObject vObj)
    {
        if (GetDecimal(vObj, "availableStock") is > 0 ||
            GetDecimal(vObj, "quantity") is > 0 ||
            GetDecimal(vObj, "stock") is > 0)
        {
            return true;
        }

        if (vObj["stockInfoList"] is not JsonArray stockArr)
        {
            return false;
        }

        foreach (var item in stockArr)
        {
            if (item is not JsonObject stockObj)
            {
                continue;
            }

            var stock = GetDecimal(stockObj, "available_stock")
                        ?? GetDecimal(stockObj, "availableStock")
                        ?? GetDecimal(stockObj, "quantity");
            if (stock is > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static void ApplyVariationToJson(
        JsonObject vObj,
        ListingVariation match,
        IReadOnlyDictionary<string, int> axisSlots,
        string? warehouseId = null)
    {
        if (match.FourSellerSkuId is > 0)
        {
            vObj["id"] = match.FourSellerSkuId.Value;
        }

        if (!string.IsNullOrWhiteSpace(match.SellerSku))
        {
            vObj["sellerSku"] = match.SellerSku;
        }

        if (!string.IsNullOrWhiteSpace(match.MainImage))
        {
            // 4Seller variation.mainImage is a single URL; pipe-joined sets belong in skuImgs only.
            vObj["mainImage"] = FirstImage(match.MainImage) ?? match.MainImage.Trim();
        }

        if (match.SalePrice.HasValue)
        {
            // TikTok variation uses originalPrice; preserve string vs number shape from raw detail.
            SetNumericPreservingKind(vObj, "originalPrice", match.SalePrice.Value, createIfMissing: true);
            SetNumericPreservingKind(vObj, "salePrice", match.SalePrice.Value, createIfMissing: false);
            SetNumericPreservingKind(vObj, "price", match.SalePrice.Value, createIfMissing: false);
            if (match.GlobalPrice.HasValue)
            {
                SetNumericPreservingKind(vObj, "globalPrice", match.GlobalPrice.Value, createIfMissing: false);
            }
        }

        if (match.Quantity.HasValue)
        {
            // Create availableStock when missing — raw clones / new SKUs often have no stock
            // keys. SyncWarehouseAcrossStockInfo later reads this (fallback 0) to collapse
            // stockInfoList; without the key, auto-publish Quantity never reaches 4Seller.
            if (!string.IsNullOrWhiteSpace(warehouseId))
            {
                vObj["warehouseId"] = warehouseId.Trim();
            }

            SetNumericPreservingKind(vObj, "availableStock", match.Quantity.Value, createIfMissing: true);
            SetNumericIfPresent(vObj, "quantity", match.Quantity.Value);
            SetNumericIfPresent(vObj, "stock", match.Quantity.Value);
            PatchStockInfoList(vObj, match.Quantity.Value);
        }

        var title = match.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            SetStringIfKeyExists(vObj, "title", title);
            SetStringIfKeyExists(vObj, "name", title);
            SetStringIfKeyExists(vObj, "skuName", title);
        }

        // Runs unconditionally: a cloned row must lose the previous combo's option labels even
        // when the new combo has fewer axes (Size deleted on the form).
        ListingSalePropSync.ApplyVariationOptions(
            vObj,
            ResolveVariationAxisAttrs(title, match.VariantAttributes),
            axisSlots);

        if (!string.IsNullOrWhiteSpace(match.VariantAttributes) && vObj.ContainsKey("variantAttributes"))
        {
            vObj["variantAttributes"] = match.VariantAttributes;
        }
    }

    private static void SetNumericIfPresent(JsonObject obj, string key, decimal value)
    {
        if (obj.ContainsKey(key))
        {
            obj[key] = value;
        }
    }

    /// <summary>
    /// Writes a numeric field while preserving whether the original JSON value was a string or number.
    /// </summary>
    private static void SetNumericPreservingKind(
        JsonObject obj,
        string key,
        decimal value,
        bool createIfMissing)
    {
        if (!obj.TryGetPropertyValue(key, out var existing) || existing is null)
        {
            if (createIfMissing)
            {
                obj[key] = value;
            }

            return;
        }

        if (existing is JsonValue jv && jv.TryGetValue(out string? _))
        {
            obj[key] = value.ToString(CultureInfo.InvariantCulture);
            return;
        }

        obj[key] = value;
    }

    private static void SetStringIfKeyExists(JsonObject obj, string key, string value)
    {
        if (obj.ContainsKey(key))
        {
            obj[key] = value;
        }
    }

    private static void PatchStockInfoList(JsonObject vObj, int quantity)
    {
        // Golden shape is a single stockInfoList row. Writing qty into every legacy row
        // (and globalStockInfoList) made inbound sync SUM them → stock inflated each push.
        var wid = GetString(vObj, "warehouseId")
                  ?? GetString(vObj, "warehouse_id")
                  ?? FirstWarehouseIdFromStockLists(vObj);

        if (vObj["stockInfoList"] is not JsonArray stockArr || stockArr.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(wid))
            {
                // Do not invent stockInfoList without warehouse_id — 4Seller rejects incomplete stock rows.
                return;
            }
        }

        CollapseStockInfoList(vObj, quantity, wid);
    }

    /// <summary>
    /// Fills sale-prop ids that the cloned RawPayload cannot supply. Only calls 4Seller when
    /// <see cref="ListingSalePropSync.CollectGaps"/> reports a genuinely new axis or label, so the
    /// common push (same Color/Size set) stays a single request.
    /// </summary>
    /// <returns>Axes that still have no TikTok attribute id — push must be blocked.</returns>
    private async Task<List<string>> CompleteSalePropMapsAsync(
        Listing listing,
        JsonObject payloadObj,
        IReadOnlyDictionary<int, ListingSalePropSync.MapShape> capturedValueShapes,
        CancellationToken cancellationToken)
    {
        var axisAttrs = BuildVariationAxisAttrs(listing.Variations);
        var axisSlots = ListingSalePropSync.ResolveAxisSlots(payloadObj, axisAttrs);
        if (axisSlots.Count == 0)
        {
            return [];
        }

        var gaps = ListingSalePropSync.CollectGaps(payloadObj, axisAttrs);
        if (!gaps.Any)
        {
            return [];
        }

        var schemaByAxis = await TryLoadSalePropSchemaAsync(listing, cancellationToken);
        return ListingSalePropSync.RebuildRootMaps(
            payloadObj,
            axisAttrs,
            axisSlots,
            axis => schemaByAxis?.GetValueOrDefault(axis),
            allowMintedValueIds: true,
            capturedValueShapes: capturedValueShapes);
    }

    private static Dictionary<int, ListingSalePropSync.MapShape> CaptureSalePropValueShapes(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload) ||
            JsonNode.Parse(rawPayload) is not JsonObject obj)
        {
            return [];
        }

        return ListingSalePropSync.CaptureValueShapes(obj);
    }

    /// <summary>
    /// Fills the <c>productAttributes</c> rows the category refuses to publish without: the well-known
    /// ones (Condition type/description, Region of origin, CPSIA, Washing instructions) plus every other
    /// attribute the live schema marks <c>isMandatory=1</c>.
    /// <para>
    /// The mandatory set is per category and cannot be guessed — <c>897040</c> requires nothing while
    /// <c>903824</c> requires Weaving method, Washing instructions, Region of origin and Material — and a
    /// row left empty comes back as 【Publish Failed】Specifics.&lt;attribute name&gt;.
    /// </para>
    /// </summary>
    private async Task EnsureConditionSpecificsInPayloadAsync(
        Listing listing,
        JsonObject payloadObj,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(listing.CategoryId) || listing.FourSellerShopId is not > 0)
        {
            return;
        }

        var denylist = await _attributeRejections.GetDenylistAsync(listing.CategoryId, cancellationToken);

        // 4Seller keeps offering values TikTok has withdrawn, so the live schema alone would seed one
        // again on the very next publish.
        IReadOnlyList<FourSellerCategoryAttributeDto> schema =
            (await TryLoadCategorySchemaAsync(listing, cancellationToken) ?? [])
            .Select(a => ListingSpecificsMappingEngine.WithoutRejectedValues(a, denylist))
            .ToList();

        if (schema.Count == 0)
        {
            return;
        }

        var typeAttr = ListingSpecificsMappingEngine.FindConditionTypeAttribute(schema);
        var descAttr = ListingSpecificsMappingEngine.FindConditionDescriptionAttribute(schema);
        var regionAttr = ListingSpecificsMappingEngine.FindRegionOfOriginAttribute(schema);
        var cpsiaAttr = ListingSpecificsMappingEngine.FindCpsiaTrackingLabelAttribute(schema);
        var washingAttr = ListingSpecificsMappingEngine.FindWashingInstructionAttribute(schema);
        var seedWashing = washingAttr is not null
                          && ListingSpecificsMappingEngine.IsFashionCategory(null, schema);
        var required = ListingSpecificsMappingEngine.RequiredSpecificAttributes(schema);
        if (typeAttr is null
            && descAttr is null
            && regionAttr is null
            && cpsiaAttr is null
            && !seedWashing
            && required.Count == 0)
        {
            return;
        }

        var wasString = IsJsonStringField(payloadObj, "productAttributes")
                        || IsJsonStringField(payloadObj, "attributes")
                        || IsJsonStringField(payloadObj, "attributeList");

        var attrArr = GetJsonArrayFlexible(payloadObj, "productAttributes")
                      ?? GetJsonArrayFlexible(payloadObj, "attributes")
                      ?? GetJsonArrayFlexible(payloadObj, "attributeList")
                      ?? new JsonArray();

        // A value TikTok has withdrawn can still sit in a stored payload from an earlier push. Strip it
        // first so the seeding below treats the attribute as empty and refills it with a valid value.
        var strippedValues = RemoveRejectedProductAttributeValues(attrArr, denylist);
        if (strippedValues.Count > 0)
        {
            _logger.LogWarning(
                "Listing {ListingId}: dropped attribute values TikTok no longer accepts from the publish payload ({Values})",
                listing.Id,
                string.Join(" | ", strippedValues));
        }

        if (typeAttr is not null
            && !HasNonEmptyProductAttribute(attrArr, typeAttr.AttributeId, typeAttr.AttributeName))
        {
            var newValue = ListingSpecificsMappingEngine.ResolveDefaultConditionTypeValue(typeAttr);
            if (newValue is not null)
            {
                UpsertOneProductAttributeRow(
                    attrArr,
                    typeAttr.AttributeId,
                    typeAttr.AttributeName,
                    newValue.ValueId,
                    newValue.ValueName);
            }
        }

        if (descAttr is not null
            && !HasNonEmptyProductAttribute(attrArr, descAttr.AttributeId, descAttr.AttributeName))
        {
            const string defaultDescription = "Brand new";
            UpsertOneProductAttributeRow(
                attrArr,
                descAttr.AttributeId,
                descAttr.AttributeName,
                string.Empty,
                defaultDescription);
        }

        if (regionAttr is not null
            && !HasNonEmptyProductAttribute(attrArr, regionAttr.AttributeId, regionAttr.AttributeName))
        {
            var regionValue = ListingSpecificsMappingEngine.ResolveDefaultRegionOfOriginValue(regionAttr);
            if (regionValue is not null)
            {
                UpsertOneProductAttributeRow(
                    attrArr,
                    regionAttr.AttributeId,
                    regionAttr.AttributeName,
                    regionValue.ValueId,
                    regionValue.ValueName);
            }
        }

        if (cpsiaAttr is not null
            && !HasNonEmptyProductAttribute(attrArr, cpsiaAttr.AttributeId, cpsiaAttr.AttributeName))
        {
            var cpsiaValue = ListingSpecificsMappingEngine.ResolveDefaultCpsiaTrackingLabelValue(cpsiaAttr);
            if (cpsiaValue is not null)
            {
                UpsertOneProductAttributeRow(
                    attrArr,
                    cpsiaAttr.AttributeId,
                    cpsiaAttr.AttributeName,
                    cpsiaValue.ValueId,
                    cpsiaValue.ValueName);
            }
        }

        var seeded = new List<string>();
        var unresolved = new List<string>();

        if (seedWashing && washingAttr is not null)
        {
            var washingFilled = HasNonEmptyProductAttribute(
                attrArr, washingAttr.AttributeId, washingAttr.AttributeName);
            if (!washingFilled)
            {
                var washingValue = ListingSpecificsMappingEngine.ResolveDefaultWashingInstructionValue(
                    washingAttr,
                    denylist);
                if (washingValue is not null)
                {
                    UpsertOneProductAttributeRow(
                        attrArr,
                        washingAttr.AttributeId,
                        washingAttr.AttributeName,
                        washingValue.ValueId,
                        washingValue.ValueName);
                    seeded.Add($"{washingAttr.AttributeName}={washingValue.ValueName}");
                }

                // No else: when the category marks Washing instructions mandatory the loop below reports
                // it, and reporting here too would name the same attribute twice.
            }
        }

        // Everything else the category insists on. Matching is id-strict here: 903824 carries two
        // attributes named "Material" (100157 mandatory, 100701 optional), and a name match would read the
        // optional row as proof the mandatory one is filled.
        foreach (var attr in required)
        {
            if (HasNonEmptyProductAttributeById(attrArr, attr.AttributeId, attr.AttributeName))
            {
                continue;
            }

            var value = ResolveTwinValueFromPayload(attrArr, attr)
                        ?? ListingSpecificsMappingEngine.ResolveRequiredDefaultValue(attr, denylist);
            if (value is null)
            {
                unresolved.Add($"{attr.AttributeName}#{attr.AttributeId}");
                continue;
            }

            UpsertOneProductAttributeRow(
                attrArr,
                attr.AttributeId,
                attr.AttributeName,
                value.ValueId,
                value.ValueName);
            seeded.Add($"{attr.AttributeName}={value.ValueName}");
        }

        if (seeded.Count > 0)
        {
            _logger.LogInformation(
                "Listing {ListingId}: seeded mandatory Specifics of category {CategoryId} ({Seeded})",
                listing.Id,
                listing.CategoryId,
                string.Join(" | ", seeded));
        }

        if (unresolved.Count > 0)
        {
            // Free-text mandatory rows (empty valueList) cannot be invented; publish will report them.
            _logger.LogWarning(
                "Listing {ListingId}: mandatory Specifics of category {CategoryId} left empty — "
                + "category không cung cấp giá trị nào để chọn ({Attributes})",
                listing.Id,
                listing.CategoryId,
                string.Join(" | ", unresolved));
        }

        WriteProductAttributesField(payloadObj, attrArr, wasString);
    }

    /// <summary>
    /// Same as <see cref="HasNonEmptyProductAttribute"/> but only accepts a row of the same attribute id;
    /// the name is a fallback for legacy rows that carry no id at all.
    /// </summary>
    private static bool HasNonEmptyProductAttributeById(
        JsonArray attrArr,
        string attributeId,
        string attributeName)
    {
        foreach (var item in attrArr)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id")
                     ?? GetString(a, "attribute_id");
            var name = GetString(a, "attributeName")
                       ?? GetString(a, "attrName")
                       ?? GetString(a, "name")
                       ?? GetString(a, "attribute_name");

            var sameAttribute = string.IsNullOrWhiteSpace(id)
                ? !string.IsNullOrWhiteSpace(name)
                  && string.Equals(name.Trim(), attributeName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(id.Trim(), attributeId, StringComparison.OrdinalIgnoreCase);
            if (!sameAttribute)
            {
                continue;
            }

            if (ProductAttributeRowValueNames(a).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Value already present on another attribute of the same name, when this attribute's enum offers it
    /// too — keeps the mandatory/optional twins (two "Material" rows) telling the same story instead of
    /// seeding a default beside a value the supplier stated.
    /// </summary>
    private static FourSellerCategoryAttributeValueDto? ResolveTwinValueFromPayload(
        JsonArray attrArr,
        FourSellerCategoryAttributeDto attr)
    {
        if (attr.Values.Count == 0)
        {
            return null;
        }

        foreach (var item in attrArr)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id")
                     ?? GetString(a, "attribute_id");
            if (string.Equals(id?.Trim(), attr.AttributeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(a, "attributeName")
                       ?? GetString(a, "attrName")
                       ?? GetString(a, "name")
                       ?? GetString(a, "attribute_name");
            var nameKey = ListingSpecificsMappingEngine.NormalizeKey(name);
            if (nameKey.Length == 0
                || nameKey != ListingSpecificsMappingEngine.NormalizeKey(attr.AttributeName))
            {
                continue;
            }

            foreach (var valueName in ProductAttributeRowValueNames(a))
            {
                var valueKey = ListingSpecificsMappingEngine.NormalizeKey(valueName);
                var hit = attr.Values.FirstOrDefault(v =>
                    ListingSpecificsMappingEngine.NormalizeKey(v.ValueName) == valueKey);
                if (hit is not null)
                {
                    return hit;
                }
            }
        }

        return null;
    }

    /// <summary>Non-empty value names of one <c>productAttributes</c> row (multi-select or flat).</summary>
    private static List<string> ProductAttributeRowValueNames(JsonObject a)
    {
        var names = new List<string>();
        if (a["values"] is JsonArray values)
        {
            foreach (var v in values)
            {
                if (v is not JsonObject vo)
                {
                    continue;
                }

                var valueName = GetString(vo, "valueName")
                                ?? GetString(vo, "value_name")
                                ?? GetString(vo, "name");
                if (!string.IsNullOrWhiteSpace(valueName))
                {
                    names.Add(valueName.Trim());
                }
            }
        }

        var flat = GetString(a, "attrValue") ?? GetString(a, "valueName");
        if (!string.IsNullOrWhiteSpace(flat))
        {
            names.Add(flat.Trim());
        }

        return names;
    }

    private static bool HasNonEmptyProductAttribute(
        JsonArray attrArr,
        string attributeId,
        string attributeName)
    {
        foreach (var item in attrArr)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id")
                     ?? GetString(a, "attribute_id");
            var name = GetString(a, "attributeName")
                       ?? GetString(a, "attrName")
                       ?? GetString(a, "name")
                       ?? GetString(a, "attribute_name");

            if (!string.Equals(id, attributeId, StringComparison.OrdinalIgnoreCase)
                && !( !string.IsNullOrWhiteSpace(name)
                      && string.Equals(name.Trim(), attributeName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var values = a["values"] as JsonArray;
            if (values is not null)
            {
                foreach (var v in values)
                {
                    if (v is not JsonObject vo)
                    {
                        continue;
                    }

                    var valueName = GetString(vo, "valueName")
                                    ?? GetString(vo, "value_name")
                                    ?? GetString(vo, "name");
                    if (!string.IsNullOrWhiteSpace(valueName))
                    {
                        return true;
                    }
                }
            }

            var flat = GetString(a, "attrValue") ?? GetString(a, "valueName");
            if (!string.IsNullOrWhiteSpace(flat))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every attribute value TikTok has rejected from a publish payload, and the row itself when
    /// nothing valid is left, so the seeding pass can refill it. Returns the removed values for logging.
    /// </summary>
    private static List<string> RemoveRejectedProductAttributeValues(
        JsonArray attrArr,
        TikTokAttributeValueDenylist denylist)
    {
        var removed = new List<string>();
        if (denylist.IsEmpty || attrArr.Count == 0)
        {
            return removed;
        }

        for (var i = attrArr.Count - 1; i >= 0; i--)
        {
            if (attrArr[i] is not JsonObject a)
            {
                continue;
            }

            var attrId = (GetString(a, "attributeId")
                          ?? GetString(a, "attrId")
                          ?? GetString(a, "id")
                          ?? GetString(a, "attribute_id"))?.Trim();
            if (string.IsNullOrWhiteSpace(attrId))
            {
                continue;
            }

            var attrLabel = GetString(a, "attributeName") ?? GetString(a, "attrName") ?? attrId;

            if (a["values"] is JsonArray values)
            {
                for (var j = values.Count - 1; j >= 0; j--)
                {
                    if (values[j] is not JsonObject vo)
                    {
                        continue;
                    }

                    var valueName = GetString(vo, "valueName")
                                    ?? GetString(vo, "value_name")
                                    ?? GetString(vo, "name");
                    if (denylist.IsRejected(attrId, valueName))
                    {
                        removed.Add($"{attrLabel} ({attrId}): {valueName}");
                        values.RemoveAt(j);
                    }
                }
            }

            var flat = GetString(a, "attrValue") ?? GetString(a, "valueName");
            if (denylist.IsRejected(attrId, flat))
            {
                removed.Add($"{attrLabel} ({attrId}): {flat}");
                a.Remove("attrValue");
                a.Remove("valueName");
                a.Remove("valueId");
            }

            var hasValues = a["values"] is JsonArray remaining && remaining.Count > 0;
            var hasFlat = !string.IsNullOrWhiteSpace(GetString(a, "attrValue"))
                          || !string.IsNullOrWhiteSpace(GetString(a, "valueName"));
            if (!hasValues && !hasFlat)
            {
                attrArr.RemoveAt(i);
            }
        }

        return removed;
    }

    private static void UpsertOneProductAttributeRow(
        JsonArray attrArr,
        string attributeId,
        string attributeName,
        string valueId,
        string valueName)
    {
        foreach (var item in attrArr)
        {
            if (item is not JsonObject a)
            {
                continue;
            }

            var id = GetString(a, "attributeId")
                     ?? GetString(a, "attrId")
                     ?? GetString(a, "id");
            if (!string.Equals(id, attributeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WriteBothProductAttributeShapes(a, attributeId, attributeName, valueId, valueName);
            return;
        }

        var row = new JsonObject();
        WriteBothProductAttributeShapes(row, attributeId, attributeName, valueId, valueName);
        attrArr.Add(row);
    }

    /// <summary>
    /// Writes one value into both shapes a productAttributes row carries: the nested
    /// <c>values[]</c> and the flat <c>attrId/attrValue/valueId</c> mirror.
    /// <para>
    /// Both are load-bearing. A row seeded with <c>values[]</c> alone published as a custom value name
    /// — TikTok answered "attribute ID 100401 does not support custom value names" for a payload whose
    /// nested valueId was correct — so 4Seller reads the id off the flat keys. Writing one shape and
    /// leaving the other stale is the same bug in reverse.
    /// </para>
    /// </summary>
    private static void WriteBothProductAttributeShapes(
        JsonObject row,
        string attributeId,
        string attributeName,
        string valueId,
        string valueName)
    {
        row["attributeId"] = attributeId;
        row["attributeName"] = attributeName;
        row["values"] = new JsonArray
        {
            new JsonObject
            {
                ["valueId"] = valueId,
                ["valueName"] = valueName,
                ["isShow"] = "true"
            }
        };
        row["attrId"] = attributeId;
        row["id"] = attributeId;
        row["attrName"] = attributeName;
        row["name"] = attributeName;
        row["attrValue"] = valueName;
        row["value"] = valueName;
        row["valueId"] = valueId;
    }

    /// <summary>
    /// Color / Size / Number of Items schema for the listing category, or null when 4Seller cannot
    /// be reached (minted ids then keep the push going rather than hard-failing the save).
    /// </summary>
    private async Task<Dictionary<string, SalePropAxisSchema>?> TryLoadSalePropSchemaAsync(
        Listing listing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(listing.CategoryId) || listing.FourSellerShopId is not > 0)
        {
            return null;
        }

        try
        {
            var schema = await _categorySchema.GetAsync(
                listing.OwnerId,
                listing.CategoryId.Trim(),
                listing.FourSellerShopId.Value,
                string.IsNullOrWhiteSpace(listing.Site) ? "US" : listing.Site.Trim(),
                cancellationToken);

            var byAxis = new Dictionary<string, SalePropAxisSchema>(StringComparer.OrdinalIgnoreCase);
            foreach (var axis in ListingSalePropSync.AxisOrder)
            {
                var attr = schema.FirstOrDefault(a =>
                    axis.Equals(a.AttributeName?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (attr is null || string.IsNullOrWhiteSpace(attr.AttributeId))
                {
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in attr.Values)
                {
                    if (!string.IsNullOrWhiteSpace(value.ValueName) &&
                        !string.IsNullOrWhiteSpace(value.ValueId))
                    {
                        values.TryAdd(value.ValueName.Trim(), value.ValueId.Trim());
                    }
                }

                byAxis[axis] = new SalePropAxisSchema(attr.AttributeId.Trim(), values);
            }

            return byAxis;
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning(
                ex,
                "sale-prop schema lookup failed for listing {ListingId} category {CategoryId}; falling back to minted value ids",
                listing.Id,
                listing.CategoryId);
            return null;
        }
    }

    /// <summary>
    /// Sale-prop combo of one SKU: prefers the form's structured <c>variantAttributes</c>, falls
    /// back to parsing the label, and repairs a collapsed Color like <c>"L/White"</c>.
    /// </summary>
    private static Dictionary<string, string> ResolveVariationAxisAttrs(
        string? title,
        string? attrsJson)
    {
        var attrs = ParseAttrs(attrsJson);
        if (attrs.Count == 0)
        {
            attrs = ParseAttrs(BuildVariantAttributesJson(title));
        }

        if (attrs.TryGetValue("Color", out var rawColor) && rawColor.Contains('/'))
        {
            var repaired = ParseAttrs(BuildVariantAttributesJson(rawColor));
            if (repaired.Count > 0)
            {
                attrs = repaired;
            }
        }

        // A slash means the label collapsed two axes into one slot — never push that as a label.
        foreach (var key in attrs.Where(kv => kv.Value.Contains('/')).Select(kv => kv.Key).ToList())
        {
            attrs.Remove(key);
        }

        return attrs;
    }

    /// <summary>
    /// Deterministic order for sale-prop label maps. <c>ListingVariation</c> has no sort column, so
    /// SKU code (Batch SKU encodes the combo) then title keeps the maps stable across pushes.
    /// </summary>
    private static IOrderedEnumerable<ListingVariation> InPushOrder(
        ICollection<ListingVariation> variations) =>
        variations
            .OrderBy(v => v.SellerSku ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static List<IReadOnlyDictionary<string, string>> BuildVariationAxisAttrs(
        ICollection<ListingVariation> variations) =>
        InPushOrder(variations)
            .Select(v => (IReadOnlyDictionary<string, string>)
                ResolveVariationAxisAttrs(v.Title, v.VariantAttributes))
            .ToList();

    private static bool TryGetLong(JsonObject obj, string key, out long value)
    {
        value = 0;
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jv)
        {
            return false;
        }

        if (jv.TryGetValue(out long l))
        {
            value = l;
            return true;
        }

        if (jv.TryGetValue(out string? s) && long.TryParse(s, out l))
        {
            value = l;
            return true;
        }

        return false;
    }

    private IQueryable<Listing> BuildListQuery(
        ListingListQuery query,
        bool tracking = false,
        bool includeNav = true)
    {
        IQueryable<Listing> q = tracking
            ? _db.Listings
            : _db.Listings.AsNoTracking();

        // includeNav kept for callers that still materialize entities (e.g. delete-by-filter).
        // ListAsync uses includeNav:false + Select projection instead.
        if (!tracking && includeNav)
        {
            q = q
                .Include(l => l.Owner)
                .Include(l => l.Shop)
                .Include(l => l.Variations);
        }

        if (!string.IsNullOrWhiteSpace(query.PageType))
        {
            var pageType = query.PageType.Trim().ToLowerInvariant();
            q = q.Where(l => l.PageType == pageType);
        }

        if (!string.IsNullOrWhiteSpace(query.SiteStatus))
        {
            // SiteStatus is stored normalized lowercase — equality keeps indexes usable.
            var siteStatus = query.SiteStatus.Trim().ToLowerInvariant();
            q = q.Where(l => l.SiteStatus == siteStatus);
        }

        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(l => l.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            // Platform is stored lowercase (e.g. tiktok).
            var platform = query.Platform.Trim().ToLowerInvariant();
            q = q.Where(l => l.Platform == platform);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(l => l.ShopId == query.ShopId.Value);
        }

        q = ListingSearchQuery.ApplySearch(q, query.Search);

        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value;
            q = q.Where(l => (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) >= from);
        }

        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value;
            q = q.Where(l => (l.PlatformCreateTime ?? l.PlatformUpdateTime ?? l.CreatedAt) <= to);
        }

        return q;
    }

    /// <summary>
    /// Loads up to 5 slim variations per listing on the current page (card preview / draft initial price).
    /// Avoids Include(Variations) cartesian join and skips VariantAttributes jsonb.
    /// </summary>
    private async Task AttachListVariationsAsync(
        List<ListingListItemDto> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var listingIds = items.Select(i => i.Id).ToList();
        var rows = await _db.ListingVariations.AsNoTracking()
            .Where(v => listingIds.Contains(v.ListingId))
            .OrderBy(v => v.SellerSku)
            .Select(v => new
            {
                v.ListingId,
                v.Id,
                v.FourSellerSkuId,
                v.SellerSku,
                v.Title,
                v.MainImage,
                v.SalePrice,
                v.InitialPrice,
                v.Quantity
            })
            .ToListAsync(cancellationToken);

        var byListing = rows
            .GroupBy(v => v.ListingId)
            .ToDictionary(
                g => g.Key,
                g => g.Take(5)
                    .Select((v, i) => new ListingVariationDto
                    {
                        Id = v.Id,
                        FourSellerSkuId = v.FourSellerSkuId,
                        SellerSku = v.SellerSku,
                        Title = v.Title,
                        MainImage = v.MainImage,
                        SalePrice = v.SalePrice,
                        InitialPrice = v.InitialPrice,
                        Quantity = v.Quantity,
                        SortOrder = i,
                        IsDefault = i == 0
                    })
                    .ToList());

        foreach (var item in items)
        {
            if (byListing.TryGetValue(item.Id, out var variations))
            {
                item.Variations = variations;
            }
        }
    }

    /// <summary>
    /// Resolves local pageType / siteStatus from 4Seller row fields.
    /// Priority: Reviewing (<c>productStatus=2</c>) → erpStatus bucket → other productStatus codes → page filter.
    /// </summary>
    internal static (string PageType, string SiteStatus) ResolveListingStatuses(
        string? pulledStatus,
        int? productStatus,
        string? erpStatus,
        string? publishStatus = null)
    {
        // Posted + waiting TikTok review must land on Published / Pending (not Draft/Active).
        if (productStatus == FourSellerProductStatuses.Reviewing)
        {
            return (ListingPageTypes.Published, ListingSiteStatuses.Pending);
        }

        (string PageType, string SiteStatus) mapped;
        var fromErp = TryMapKnownSiteStatus(erpStatus);
        if (fromErp is not null)
        {
            mapped = fromErp.Value;
        }
        else
        {
            var fromProduct = MapProductStatus(productStatus);
            mapped = fromProduct is not null
                ? fromProduct.Value
                : MapPageTypeAndSiteStatus(pulledStatus ?? ListingSiteStatuses.Draft);
        }

        if (PublishStatuses.Normalize(publishStatus) == PublishStatuses.Publishing
            && mapped.PageType == ListingPageTypes.Draft
            && mapped.SiteStatus == ListingSiteStatuses.Draft)
        {
            return (ListingPageTypes.Published, ListingSiteStatuses.Pending);
        }

        return mapped;
    }

    private static (string PageType, string SiteStatus)? MapProductStatus(int? productStatus) =>
        productStatus switch
        {
            FourSellerProductStatuses.Draft => (ListingPageTypes.Draft, ListingSiteStatuses.Draft),
            FourSellerProductStatuses.Failure => (ListingPageTypes.Published, ListingSiteStatuses.Reject),
            FourSellerProductStatuses.Active => (ListingPageTypes.Published, ListingSiteStatuses.Active),
            FourSellerProductStatuses.Inactive => (ListingPageTypes.Published, ListingSiteStatuses.Inactive),
            FourSellerProductStatuses.Remove => (ListingPageTypes.Published, ListingSiteStatuses.Removed),
            _ => null
        };

    private static (string PageType, string SiteStatus)? TryMapKnownSiteStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var status = ListingSiteStatuses.NormalizeSiteStatus(raw);
        if (status == ListingSiteStatuses.Draft)
        {
            return (ListingPageTypes.Draft, ListingSiteStatuses.Draft);
        }

        if (ListingSiteStatuses.PublishedStatuses.Contains(status, StringComparer.Ordinal))
        {
            return (ListingPageTypes.Published, status);
        }

        return null;
    }

    private static (string PageType, string SiteStatus) MapPageTypeAndSiteStatus(string pulledStatus)
    {
        var status = ListingSiteStatuses.NormalizeSiteStatus(pulledStatus);
        if (string.IsNullOrEmpty(status) || status == ListingSiteStatuses.Draft)
        {
            return (ListingPageTypes.Draft, ListingSiteStatuses.Draft);
        }

        return (ListingPageTypes.Published, status);
    }

    private static FourSellerListingRecordDto MergePageAndDetail(
        FourSellerListingRecordDto page,
        FourSellerListingRecordDto? detail)
    {
        if (detail is null)
        {
            return page;
        }

        return new FourSellerListingRecordDto
        {
            Id = page.Id,
            ProductName = detail.ProductName ?? page.ProductName,
            MainImage = detail.MainImage ?? page.MainImage,
            ShopId = detail.ShopId ?? page.ShopId,
            ShopName = detail.ShopName ?? page.ShopName,
            Site = detail.Site ?? page.Site,
            Currency = detail.Currency ?? page.Currency,
            CategoryId = detail.CategoryId ?? page.CategoryId,
            CategoryName = detail.CategoryName ?? page.CategoryName,
            LowPrice = detail.LowPrice ?? page.LowPrice,
            HighPrice = detail.HighPrice ?? page.HighPrice,
            AvailableStock = detail.AvailableStock ?? page.AvailableStock,
            VariationCount = detail.VariationCount > 0 ? detail.VariationCount : page.VariationCount,
            ErpStatus = detail.ErpStatus ?? page.ErpStatus,
            // Page list is the source of truth for TikTok productStatus (detail often omits it).
            ProductStatus = page.ProductStatus ?? detail.ProductStatus,
            PublishStatus = detail.PublishStatus ?? page.PublishStatus,
            // Page list is the source of truth for publish failures (detail often omits it).
            FailedMessage = page.FailedMessage ?? detail.FailedMessage,
            MarketplaceProductId = detail.MarketplaceProductId ?? page.MarketplaceProductId,
            SourceUrl = detail.SourceUrl ?? page.SourceUrl,
            WarehouseId = detail.WarehouseId ?? page.WarehouseId,
            CreateTime = detail.CreateTime ?? page.CreateTime,
            UpdateTime = detail.UpdateTime ?? page.UpdateTime,
            DescriptionHtml = detail.DescriptionHtml ?? page.DescriptionHtml,
            PackageWeight = detail.PackageWeight ?? page.PackageWeight,
            PackageLength = detail.PackageLength ?? page.PackageLength,
            PackageWidth = detail.PackageWidth ?? page.PackageWidth,
            PackageHeight = detail.PackageHeight ?? page.PackageHeight,
            WeightUnit = detail.WeightUnit ?? page.WeightUnit,
            DimensionUnit = detail.DimensionUnit ?? page.DimensionUnit,
            VariationList = detail.VariationList.Count > 0 ? detail.VariationList : page.VariationList,
            DetailImageUrls = detail.DetailImageUrls.Count > 0 ? detail.DetailImageUrls : page.DetailImageUrls,
            ColorImageUrls = detail.ColorImageUrls.Count > 0 ? detail.ColorImageUrls : page.ColorImageUrls,
            RawDetailJson = detail.RawDetailJson ?? page.RawDetailJson
        };
    }

    private static ListingListItemDto MapListItem(Listing listing)
    {
        var dto = new ListingListItemDto
        {
        Id = listing.Id,
        FourSellerListingId = listing.FourSellerListingId,
        Platform = listing.Platform,
        PageType = listing.PageType,
        SiteStatus = listing.SiteStatus,
        ProductName = listing.ProductName,
        MainImage = listing.MainImage,
        Site = listing.Site,
        Currency = listing.Currency,
        LowPrice = listing.LowPrice,
        HighPrice = listing.HighPrice,
        AvailableStock = listing.AvailableStock,
        VariationCount = listing.VariationCount,
        CategoryName = listing.CategoryName,
        ErpStatus = listing.ErpStatus,
        PublishStatus = listing.PublishStatus,
        TikTokProductId = listing.TikTokProductId,
        PlatformUpdateTime = listing.PlatformUpdateTime,
        LastSyncedAt = listing.LastSyncedAt,
        LastPushAt = listing.LastPushAt,
        SyncError = listing.SyncError,
        PublishSource = listing.PublishSource,
        AutoPublishSkipped = listing.AutoPublishSkipped,
        AutoPublishSkippedAt = listing.AutoPublishSkippedAt,
        AutoPublishSkipReason = listing.AutoPublishSkipReason,
        LastAutoPublishRunId = listing.LastAutoPublishRunId,
        OwnerId = listing.OwnerId,
        OwnerName = string.IsNullOrWhiteSpace(listing.Owner?.FullName)
            ? listing.Owner?.UserName
            : listing.Owner.FullName,
        ShopId = listing.ShopId,
        FourSellerShopId = listing.FourSellerShopId,
        ShopName = listing.Shop?.Name,
        ShopProfileName = listing.Shop?.ProfileName,
        SourceUrl = listing.SourceUrl,
        AddedFrom = listing.AddedFrom,
        ReviewCount = listing.ReviewCount,
        AverageRating = listing.AverageRating,
        StoreLogo = listing.SupplierProductInfo?.Seller?.StoreLogo,
        StoreTitle = listing.SupplierProductInfo?.Seller?.StoreTitle,
        StoreUrl = listing.SupplierProductInfo?.Seller?.StoreUrl,
        ShopRank = listing.SupplierProductInfo?.Seller?.ShopRank,
        StoreAttention = listing.SupplierProductInfo?.Seller?.StoreAttention,
        SoldByTips = listing.SupplierProductInfo?.Seller?.SoldByTips,
        ShippingDays = listing.SupplierProductInfo?.Seller?.ShippingDays,
        ShippingDayPercent = listing.SupplierProductInfo?.Seller?.ShippingDayPercent,
        SourceStock = listing.SupplierProductInfo?.Stock,
        IsLocalProduct = listing.SupplierProductInfo?.IsLocalProduct,
        IsSupportQuickShip = listing.SupplierProductInfo?.IsSupportQuickShip,
        SellerPlatform = listing.SupplierProductInfo?.Seller?.Platform,
        StoreCode = listing.SupplierProductInfo?.Seller?.StoreCode,
        HasSizeChart = !string.IsNullOrWhiteSpace(listing.SupplierProductInfo?.SizeChartImageUrl),
        CreatedAt = listing.CreatedAt,
        Variations = listing.Variations
            .OrderBy(v => v.SellerSku)
            .Take(5)
            .Select(MapVariation)
            .ToList()
        };
        ApplySellerDisplayTitle(dto);
        return dto;
    }

    private static ListingDetailDto MapDetail(Listing listing)
    {
        var item = MapListItem(listing);
        var hydrated = HydrateFromRawPayload(listing);
        var variations = listing.Variations
            .OrderBy(v => v.SellerSku)
            .Select((v, i) => MapVariation(v, i))
            .ToList();

        // Enrich variation extras from raw sku rows when present.
        if (hydrated.VariationExtras.Count > 0)
        {
            foreach (var v in variations)
            {
                if (!hydrated.VariationExtras.TryGetValue(v.Id, out var extra) &&
                    !(v.FourSellerSkuId is > 0 &&
                      hydrated.VariationExtrasBySkuId.TryGetValue(v.FourSellerSkuId.Value, out extra)) &&
                    !( !string.IsNullOrWhiteSpace(v.SellerSku) &&
                       hydrated.VariationExtrasBySellerSku.TryGetValue(v.SellerSku!, out extra)))
                {
                    continue;
                }

                v.Barcode ??= extra.Barcode;
                v.OriginalPrice ??= extra.OriginalPrice;
                if (string.IsNullOrWhiteSpace(v.VariantAttributes))
                {
                    v.VariantAttributes = extra.VariantAttributes;
                }
            }
        }

        return new ListingDetailDto
        {
            Id = item.Id,
            FourSellerListingId = item.FourSellerListingId,
            Platform = item.Platform,
            PageType = item.PageType,
            SiteStatus = item.SiteStatus,
            ProductName = item.ProductName,
            MainImage = item.MainImage,
            Site = item.Site,
            Currency = item.Currency,
            LowPrice = item.LowPrice,
            HighPrice = item.HighPrice,
            AvailableStock = item.AvailableStock,
            VariationCount = item.VariationCount,
            CategoryName = item.CategoryName,
            ErpStatus = item.ErpStatus,
            PublishStatus = item.PublishStatus,
            TikTokProductId = item.TikTokProductId,
            PlatformUpdateTime = item.PlatformUpdateTime,
            LastSyncedAt = item.LastSyncedAt,
            LastPushAt = item.LastPushAt,
            SyncError = item.SyncError,
            OwnerId = item.OwnerId,
            OwnerName = item.OwnerName,
            ShopId = item.ShopId,
            // 4Seller shop id must ride along: auto publish B2 sends it as `shopId` to the AI
            // category endpoint, and without it the call falls back to the owner's first Connected
            // shop (wrong shop context → 4Seller answers nothing or a category from another shop).
            FourSellerShopId = item.FourSellerShopId,
            ShopName = item.ShopName,
            ShopProfileName = item.ShopProfileName,
            Variations = variations,
            CategoryId = listing.CategoryId ?? hydrated.CategoryId,
            WarehouseId = listing.WarehouseId ?? hydrated.WarehouseId,
            SourceUrl = listing.SourceUrl ?? hydrated.SourceUrl,
            DescriptionHtml = listing.DescriptionHtml ?? hydrated.DescriptionHtml,
            SearchTerms = DeserializeStringListOrNull(listing.SearchTermsJson)
                          ?? hydrated.SearchTerms
                          ?? [],
            ProductHighlights = DeserializeStringListOrNull(listing.ProductHighlightsJson)
                                ?? hydrated.ProductHighlights
                                ?? [],
            PackageWeight = listing.PackageWeight ?? hydrated.PackageWeight,
            PackageLength = listing.PackageLength ?? hydrated.PackageLength,
            PackageWidth = listing.PackageWidth ?? hydrated.PackageWidth,
            PackageHeight = listing.PackageHeight ?? hydrated.PackageHeight,
            WeightUnit = listing.WeightUnit ?? hydrated.WeightUnit,
            DimensionUnit = listing.DimensionUnit ?? hydrated.DimensionUnit,
            PlatformCreateTime = listing.PlatformCreateTime,
            HasRawPayload = !string.IsNullOrWhiteSpace(listing.RawPayload),
            BrandId = hydrated.BrandId,
            BrandName = hydrated.BrandName,
            ProductType = hydrated.ProductType ?? ResolveListingProductType(listing, null),
            SizeChartId = hydrated.SizeChartId,
            SizeGuideJson = DeserializeJsonElementOrNull(listing.SupplierProductInfo?.SizeGuideJson),
            SizeChartCaptureUrl = listing.SupplierProductInfo?.SizeChartImageUrl?.Trim(),
            ErpVideoId = hydrated.ErpVideoId,
            SafetyWarning = hydrated.SafetyWarning,
            SiteId = hydrated.SiteId ?? listing.Site,
            ListingSourceUrl = hydrated.ListingSourceUrl ?? listing.SourceUrl,
            FourSellerGroupId = hydrated.FourSellerGroupId,
            FourSellerGroupName = hydrated.FourSellerGroupName,
            AppliedRules = hydrated.AppliedRules,
            AppliedPricing = DeserializeAppliedPricing(listing.AppliedPricingJson),
            DescriptionRewritten = listing.DescriptionRewritten,
            DescriptionImagesInserted = listing.DescriptionImagesInserted,
            RefListingId = hydrated.RefListingId,
            RefShopId = hydrated.RefShopId,
            Images = hydrated.Images,
            Attributes = hydrated.Attributes,
            Certifications = hydrated.Certifications
        };
    }

    private static string SerializeAppliedPricing(ListingAppliedPricingDto dto)
    {
        var normalized = NormalizeAppliedPricing(dto);
        return JsonSerializer.Serialize(normalized, PatchJsonOptions);
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();
    }

    private static string SerializeStringList(IEnumerable<string> values) =>
        JsonSerializer.Serialize(NormalizeStringList(values), PatchJsonOptions);

    /// <summary>Null when JSON is missing/invalid; empty list when stored as <c>[]</c>.</summary>
    private static List<string>? DeserializeStringListOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json, PatchJsonOptions);
            return parsed is null ? null : NormalizeStringList(parsed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? DeserializeJsonElementOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonArray ToStringJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var value in NormalizeStringList(values))
        {
            arr.Add(JsonValue.Create(value));
        }

        return arr;
    }

    /// <summary>
    /// Writes a JSON string array onto RawPayload. Column wins over DTO; omit when both missing
    /// so a pricing-only / old-client update does not wipe 4Seller values.
    /// </summary>
    private static void PatchStringListOntoPayload(
        JsonObject obj,
        string key,
        string? columnJson,
        List<string>? dto)
    {
        List<string>? values = null;
        if (columnJson is not null)
        {
            values = DeserializeStringListOrNull(columnJson) ?? [];
        }
        else if (dto is not null)
        {
            values = NormalizeStringList(dto);
        }

        if (values is null)
        {
            return;
        }

        obj[key] = ToStringJsonArray(values);
    }

    /// <summary>Reads a JSON array (or JSON-string of an array) of strings. Null if the key is absent.</summary>
    private static List<string>? ReadStringList(JsonObject obj, string key)
    {
        var arr = GetJsonArrayFlexible(obj, key);
        if (arr is null)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in arr)
        {
            if (item is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(s.Trim());
            }
        }

        return list;
    }

    /// <summary>
    /// Fill-once: persist searchTerms / productHighlights from RawPayload when the typed columns
    /// are still empty (listings pulled before these columns existed, or first detail fill).
    /// </summary>
    private static void FillStringListsFromRawIfEmpty(Listing listing)
    {
        if (listing.SearchTermsJson is not null && listing.ProductHighlightsJson is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return;
        }

        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(listing.RawPayload) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (obj is null)
        {
            return;
        }

        if (listing.SearchTermsJson is null)
        {
            var terms = ReadStringList(obj, "searchTerms");
            if (terms is not null)
            {
                listing.SearchTermsJson = SerializeStringList(terms);
            }
        }

        if (listing.ProductHighlightsJson is null)
        {
            var highlights = ReadStringList(obj, "productHighlights");
            if (highlights is not null)
            {
                listing.ProductHighlightsJson = SerializeStringList(highlights);
            }
        }
    }

    private static ListingAppliedPricingDto? DeserializeAppliedPricing(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ListingAppliedPricingDto>(json, PatchJsonOptions);
            return parsed is null ? null : NormalizeAppliedPricing(parsed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ListingAppliedPricingDto NormalizeAppliedPricing(ListingAppliedPricingDto dto)
    {
        var mode = string.Equals(dto.TargetMode, "profit", StringComparison.OrdinalIgnoreCase)
            ? "profit"
            : "margin";
        return new ListingAppliedPricingDto
        {
            TemplateId = dto.TemplateId is { } tid && tid != Guid.Empty ? tid : null,
            AppliedAt = dto.AppliedAt == default ? DateTime.UtcNow : dto.AppliedAt,
            SupplierShipping = dto.SupplierShipping,
            Tax = dto.Tax,
            DiscountPercent = dto.DiscountPercent,
            FastSalePercent = dto.FastSalePercent,
            AdsFee = dto.AdsFee,
            TikTokShipping = dto.TikTokShipping,
            PlatformFeePercent = dto.PlatformFeePercent,
            TargetMode = mode,
            MarginPercent = dto.MarginPercent,
            ProfitAmount = dto.ProfitAmount
        };
    }

    /// <summary>
    /// True when the mirror update only writes <see cref="Listing.AppliedPricingJson"/> —
    /// skip expensive RawPayload rewrite.
    /// </summary>
    private static bool IsAppliedPricingOnlyUpdate(UpdateListingMirrorDto dto) =>
        dto.AppliedPricing is not null
        && dto.DescriptionRewritten is null
        && dto.DescriptionImagesInserted is null
        && dto.Variations is null
        && dto.Images is null
        && dto.Attributes is null
        && dto.Certifications is null
        && dto.ShopId is null
        && dto.ProductName is null
        && dto.DescriptionHtml is null
        && dto.SearchTerms is null
        && dto.ProductHighlights is null
        && dto.CategoryId is null
        && dto.CategoryName is null
        && dto.WarehouseId is null
        && dto.ListingSourceUrl is null
        && dto.SourceUrl is null
        && dto.PackageWeight is null
        && dto.PackageLength is null
        && dto.PackageWidth is null
        && dto.PackageHeight is null
        && string.IsNullOrWhiteSpace(dto.WeightUnit)
        && string.IsNullOrWhiteSpace(dto.DimensionUnit)
        && string.IsNullOrWhiteSpace(dto.SiteId)
        && string.IsNullOrWhiteSpace(dto.MainImage)
        && string.IsNullOrWhiteSpace(dto.BrandId)
        && string.IsNullOrWhiteSpace(dto.BrandName)
        && string.IsNullOrWhiteSpace(dto.ProductType)
        && string.IsNullOrWhiteSpace(dto.SizeChartId)
        && string.IsNullOrWhiteSpace(dto.ErpVideoId)
        && string.IsNullOrWhiteSpace(dto.SafetyWarning)
        && string.IsNullOrWhiteSpace(dto.FourSellerGroupId)
        && string.IsNullOrWhiteSpace(dto.FourSellerGroupName)
        && string.IsNullOrWhiteSpace(dto.AppliedRules)
        && string.IsNullOrWhiteSpace(dto.RefListingId)
        && string.IsNullOrWhiteSpace(dto.RefShopId);

    private static ListingVariationDto MapVariation(ListingVariation v, int sortOrder = 0) => new()
    {
        Id = v.Id,
        FourSellerSkuId = v.FourSellerSkuId,
        SellerSku = v.SellerSku,
        Title = v.Title,
        MainImage = v.MainImage,
        SalePrice = v.SalePrice,
        InitialPrice = v.InitialPrice,
        OriginalPrice = v.SalePrice,
        GlobalPrice = v.GlobalPrice,
        Quantity = v.Quantity,
        VariantAttributes = v.VariantAttributes ?? BuildVariantAttributesJson(v.Title),
        SortOrder = sortOrder,
        IsDefault = sortOrder == 0
    };

    private sealed class RawHydration
    {
        public string? CategoryId { get; set; }
        public string? WarehouseId { get; set; }
        public string? SourceUrl { get; set; }
        public string? DescriptionHtml { get; set; }
        public List<string>? SearchTerms { get; set; }
        public List<string>? ProductHighlights { get; set; }
        public decimal? PackageWeight { get; set; }
        public decimal? PackageLength { get; set; }
        public decimal? PackageWidth { get; set; }
        public decimal? PackageHeight { get; set; }
        public string? WeightUnit { get; set; }
        public string? DimensionUnit { get; set; }
        public string? BrandId { get; set; }
        public string? BrandName { get; set; }
        public string? ProductType { get; set; }
        public string? SizeChartId { get; set; }
        public string? ErpVideoId { get; set; }
        public string? SafetyWarning { get; set; }
        public string? SiteId { get; set; }
        public string? ListingSourceUrl { get; set; }
        public string? FourSellerGroupId { get; set; }
        public string? FourSellerGroupName { get; set; }
        public string? AppliedRules { get; set; }
        public string? RefListingId { get; set; }
        public string? RefShopId { get; set; }
        public List<ListingImageDto> Images { get; set; } = [];
        public List<ListingAttributeDto> Attributes { get; set; } = [];
        public List<ListingCertificationDto> Certifications { get; set; } = [];
        public Dictionary<Guid, VariationExtra> VariationExtras { get; } = new();
        public Dictionary<long, VariationExtra> VariationExtrasBySkuId { get; } = new();
        public Dictionary<string, VariationExtra> VariationExtrasBySellerSku { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VariationExtra
    {
        public string? Barcode { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? VariantAttributes { get; set; }
    }

    /// <summary>
    /// Infers SingleSku vs MultiSku. 4Seller detail exposes <c>hasVariation</c> (1 = Has Variations);
    /// that flag wins over stale <c>productType</c>. Structural fallbacks cover older payloads.
    /// </summary>
    internal static string ResolveListingProductType(Listing listing, JsonObject? rawObj)
    {
        if (rawObj is not null)
        {
            var hasVariation = TryReadHasVariation(rawObj);
            if (hasVariation is true)
            {
                return ListingProductTypes.MultiSku;
            }

            if (hasVariation is false)
            {
                return ListingProductTypes.SingleSku;
            }
        }

        if (HasMultiSkuStructuralSignals(listing, rawObj))
        {
            return ListingProductTypes.MultiSku;
        }

        var rawType = NormalizeListingProductType(
            rawObj is not null ? GetString(rawObj, "productType") : null);
        if (rawType is not null)
        {
            return rawType;
        }

        return ListingProductTypes.SingleSku;
    }

    /// <summary>4Seller listing detail: <c>hasVariation</c> 1/0 (or bool) = Has Variations toggle.</summary>
    private static bool? TryReadHasVariation(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("hasVariation", out var node) || node is null)
        {
            return null;
        }

        switch (node)
        {
            case JsonValue jv when jv.TryGetValue(out int i):
                return i == 1;
            case JsonValue jv when jv.TryGetValue(out long l):
                return l == 1;
            case JsonValue jv when jv.TryGetValue(out bool b):
                return b;
            case JsonValue jv when jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s):
                return s.Trim() switch
                {
                    "1" or "true" or "True" or "yes" or "Yes" => true,
                    "0" or "false" or "False" or "no" or "No" => false,
                    _ => null
                };
            default:
                return null;
        }
    }

    private static string? NormalizeListingProductType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value.Trim(), ListingProductTypes.MultiSku, StringComparison.OrdinalIgnoreCase))
        {
            return ListingProductTypes.MultiSku;
        }

        if (string.Equals(value.Trim(), ListingProductTypes.SingleSku, StringComparison.OrdinalIgnoreCase))
        {
            return ListingProductTypes.SingleSku;
        }

        return null;
    }

    private static bool HasMultiSkuStructuralSignals(Listing listing, JsonObject? rawObj)
    {
        if (listing.VariationCount > 1 || listing.Variations.Count > 1)
        {
            return true;
        }

        if (rawObj is null)
        {
            return false;
        }

        if (CountRawVariationEntries(rawObj) > 1)
        {
            return true;
        }

        if (HasSalePropConfiguration(rawObj))
        {
            return true;
        }

        return CountSkuImgColorKeys(rawObj) > 1;
    }

    private static int CountRawVariationEntries(JsonObject obj)
    {
        var max = 0;
        foreach (var key in VariationArrayKeys)
        {
            if (obj[key] is JsonArray arr)
            {
                max = Math.Max(max, arr.Count);
            }
        }

        return max;
    }

    private static bool HasSalePropConfiguration(JsonObject obj)
    {
        for (var slot = 1; slot <= ListingSalePropSync.SlotCount; slot++)
        {
            if (CountSalePropValueEntries(obj, $"{ListingSalePropSync.OptionKeyForSlot(slot)}Value") > 0)
            {
                return true;
            }

            if (HasSalePropAxisDefinition(obj, slot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSalePropAxisDefinition(JsonObject obj, int slot)
    {
        var raw = GetString(obj, ListingSalePropSync.OptionKeyForSlot(slot));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(raw) is JsonObject jo && jo.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int CountSalePropValueEntries(JsonObject obj, string key)
    {
        var raw = GetString(obj, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        try
        {
            return JsonNode.Parse(raw) switch
            {
                JsonObject jo => jo.Count,
                JsonArray ja => ja.Count,
                _ => 0
            };
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int CountSkuImgColorKeys(JsonObject obj)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapKey in new[] { "skuImgs", "salePropImageMap" })
        {
            CollectSkuImgColorKeys(obj[mapKey], keys);
        }

        return keys.Count;
    }

    private static void CollectSkuImgColorKeys(JsonNode? node, ISet<string> keys)
    {
        if (node is null)
        {
            return;
        }

        if (node is JsonObject map)
        {
            foreach (var property in map)
            {
                if (!string.IsNullOrWhiteSpace(property.Key))
                {
                    keys.Add(property.Key.Trim());
                }
            }

            return;
        }

        if (node is JsonValue jv && jv.TryGetValue(out string? raw) && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                CollectSkuImgColorKeys(JsonNode.Parse(raw), keys);
            }
            catch (JsonException)
            {
                // Ignore malformed stringified skuImgs.
            }

            return;
        }

        if (node is not JsonArray arr)
        {
            return;
        }

        foreach (var item in arr)
        {
            if (item is not JsonObject row)
            {
                continue;
            }

            var color = FirstNonEmpty(
                GetString(row, "custom_value"),
                GetString(row, "customValue"),
                GetString(row, "attrName"),
                GetString(row, "attr_name"));
            if (!string.IsNullOrWhiteSpace(color))
            {
                keys.Add(color.Trim());
            }
        }
    }

    private static RawHydration HydrateFromRawPayload(Listing listing)
    {
        var result = new RawHydration();
        JsonObject? rawObj = null;
        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            result.Images = BuildImagesFromMain(listing);
            result.ProductType = ResolveListingProductType(listing, null);
            return result;
        }

        try
        {
            var node = JsonNode.Parse(listing.RawPayload);
            if (node is not JsonObject obj)
            {
                result.Images = BuildImagesFromMain(listing);
                result.ProductType = ResolveListingProductType(listing, null);
                return result;
            }

            rawObj = obj;

            result.CategoryId = GetString(obj, "categoryId");
            result.WarehouseId = GetString(obj, "warehouseId");
            result.SourceUrl = GetString(obj, "sourceUrl") ?? GetString(obj, "listingSourceUrl");
            result.ListingSourceUrl = GetString(obj, "listingSourceUrl") ?? result.SourceUrl;
            result.DescriptionHtml = GetString(obj, "descriptionHtml")
                                    ?? GetString(obj, "description")
                                    ?? GetString(obj, "productDescription")
                                    ?? (obj["productInfo"] is JsonObject productInfo
                                        ? GetString(productInfo, "descriptionHtml")
                                          ?? GetString(productInfo, "description")
                                        : null);
            result.SearchTerms = ReadStringList(obj, "searchTerms");
            result.ProductHighlights = ReadStringList(obj, "productHighlights");
            result.PackageWeight = GetDecimal(obj, "packageWeight");
            result.PackageLength = GetDecimal(obj, "packageLength");
            result.PackageWidth = GetDecimal(obj, "packageWidth");
            result.PackageHeight = GetDecimal(obj, "packageHeight");
            result.WeightUnit = GetString(obj, "weightUnit");
            result.DimensionUnit = GetString(obj, "dimensionUnit");
            result.BrandId = GetString(obj, "brandId");
            result.BrandName = GetString(obj, "brandName");
            result.SizeChartId = GetString(obj, "sizeChartId");
            result.ErpVideoId = FirstNonEmpty(
                GetString(obj, "erpVideoId"),
                GetString(obj, "videoId"));
            result.SafetyWarning = GetString(obj, "safetyWarning");
            result.SiteId = GetString(obj, "siteId") ?? GetString(obj, "site");
            result.FourSellerGroupId = GetString(obj, "fourSellerGroupId") ?? GetString(obj, "groupId");
            result.FourSellerGroupName = GetString(obj, "fourSellerGroupName") ?? GetString(obj, "groupName");
            result.AppliedRules = GetString(obj, "appliedRules") ??
                                  (obj["appliedRules"] is JsonObject or JsonArray
                                      ? obj["appliedRules"]!.ToJsonString()
                                      : null);
            result.RefListingId = GetString(obj, "refListingId");
            result.RefShopId = GetString(obj, "refShopId");
            result.Images = ParseImages(obj, listing);
            result.Attributes = ParseAttributes(obj);
            result.Certifications = ParseCertifications(obj);
            IndexVariationExtras(obj, listing, result);
        }
        catch (JsonException)
        {
            result.Images = BuildImagesFromMain(listing);
        }

        if (result.Images.Count == 0)
        {
            result.Images = BuildImagesFromMain(listing);
        }

        AppendVariationMainImages(result.Images, listing);
        result.ProductType = ResolveListingProductType(listing, rawObj);

        return result;
    }

    private static List<ListingImageDto> BuildImagesFromMain(Listing listing)
    {
        var urls = (listing.MainImage ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return urls.Select((u, i) => new ListingImageDto
        {
            MediaType = i == 0 ? "Main" : "Detail",
            Url = u,
            SortOrder = i
        }).ToList();
    }

    private static List<ListingImageDto> ParseImages(JsonObject obj, Listing listing)
    {
        var images = new List<ListingImageDto>();
        var sort = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? url, string mediaType, Guid? skuId = null)
        {
            url = UpgradeHttpToHttps(url);
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            {
                return;
            }

            images.Add(new ListingImageDto
            {
                Url = url,
                MediaType = mediaType,
                ListingSkuId = skuId,
                SortOrder = sort++
            });
        }

        void AddProductUrls(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (var part in raw.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Add(part, images.Count == 0 ? "Main" : "Detail");
            }
        }

        AddProductUrls(GetString(obj, "mainImage") ?? listing.MainImage);

        foreach (var key in new[] { "imageList", "images", "photoList", "photos", "detailImages" })
        {
            var node = obj[key];
            if (node is JsonValue jv && jv.TryGetValue(out string? rawList) &&
                !string.IsNullOrWhiteSpace(rawList))
            {
                try
                {
                    node = JsonNode.Parse(rawList);
                }
                catch (JsonException)
                {
                    AddProductUrls(rawList);
                    continue;
                }
            }

            if (node is not JsonArray arr)
            {
                continue;
            }

            foreach (var item in arr)
            {
                switch (item)
                {
                    case JsonValue itemVal when itemVal.TryGetValue(out string? s):
                        AddProductUrls(s);
                        break;
                    case JsonObject jo:
                        AddProductUrls(
                            GetString(jo, "url")
                            ?? GetString(jo, "src")
                            ?? GetString(jo, "imageUrl")
                            ?? GetString(jo, "img_url")
                            ?? GetString(jo, "imgUrl"));
                        break;
                }
            }
        }

        // 4Seller TikTok drafts often set videoUrl/videoId to "" while erpVideoUrl holds the MP4.
        // Prefer erpVideoUrl; FirstNonEmpty skips blank strings so ?? chains are not blocked.
        Add(
            FirstNonEmpty(
                GetString(obj, "erpVideoUrl"),
                GetString(obj, "videoUrl"),
                GetString(obj, "productVideo"),
                GetString(obj, "videoPostUrl")),
            "Video");
        Add(
            FirstNonEmpty(
                GetString(obj, "sizeChart"),
                GetString(obj, "sizeChartImage"),
                GetString(obj, "sizeChartUrl")),
            "SizeChart");

        // Color-bound images use a separate dedupe key so shared gallery URLs can still bind to SKUs.
        var seenColor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddColorBound(string? url, string? color)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(color))
            {
                return;
            }

            url = UpgradeHttpToHttps(url.Trim());
            if (string.IsNullOrWhiteSpace(url) ||
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            color = color.Trim();
            var key = $"{NormalizeColorKey(color)}|{url}";
            if (!seenColor.Add(key))
            {
                return;
            }

            var skuId = FindVariationIdForColor(listing, color);

            images.Add(new ListingImageDto
            {
                Url = url,
                MediaType = "Main",
                ListingSkuId = skuId,
                ColorValue = color,
                SortOrder = sort++
            });
        }

        void AddColorBoundUrls(string? color, string? rawUrls)
        {
            if (string.IsNullOrWhiteSpace(rawUrls) || string.IsNullOrWhiteSpace(color))
            {
                return;
            }

            foreach (var part in rawUrls.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddColorBound(part, color);
            }
        }

        void AddColorBoundFromImageObject(string color, JsonObject jo)
        {
            // Prefer multi-URL fields (pipe-separated) before single thumbnail `url`.
            AddColorBoundUrls(color, GetString(jo, "img_url") ?? GetString(jo, "imgUrl"));

            foreach (var listKey in new[] { "imageList", "images", "urls", "urlList", "url_list", "photos" })
            {
                if (jo[listKey] is not JsonArray listArr)
                {
                    continue;
                }

                foreach (var item in listArr)
                {
                    switch (item)
                    {
                        case JsonValue jv when jv.TryGetValue(out string? s):
                            AddColorBoundUrls(color, s);
                            break;
                        case JsonObject nested:
                            // url_list items are usually CDN mirrors — take first http URL only.
                            if (nested["url_list"] is JsonArray mirrors || nested["urlList"] is JsonArray)
                            {
                                var mirrorArr = nested["url_list"] as JsonArray ?? nested["urlList"] as JsonArray;
                                foreach (var m in mirrorArr!)
                                {
                                    if (m is JsonValue mv && mv.TryGetValue(out string? ms) &&
                                        !string.IsNullOrWhiteSpace(ms) &&
                                        ms.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    {
                                        AddColorBound(ms, color);
                                        break;
                                    }
                                }
                            }

                            AddColorBoundUrls(
                                color,
                                GetString(nested, "img_url")
                                ?? GetString(nested, "imgUrl")
                                ?? GetString(nested, "url")
                                ?? GetString(nested, "src")
                                ?? GetString(nested, "imageUrl"));
                            break;
                    }
                }
            }

            AddColorBoundUrls(
                color,
                GetString(jo, "url")
                ?? GetString(jo, "src")
                ?? GetString(jo, "imageUrl")
                ?? GetString(jo, "uri"));
        }

        void ConsumeColorMapObject(JsonObject map)
        {
            foreach (var (color, node) in map)
            {
                if (string.IsNullOrWhiteSpace(color))
                {
                    continue;
                }

                switch (node)
                {
                    case JsonArray arr:
                        foreach (var item in arr)
                        {
                            switch (item)
                            {
                                case JsonValue jv when jv.TryGetValue(out string? s):
                                    AddColorBoundUrls(color, s);
                                    break;
                                case JsonObject jo:
                                    AddColorBoundFromImageObject(color, jo);
                                    break;
                            }
                        }

                        break;
                    case JsonObject nestedMap:
                        AddColorBoundFromImageObject(color, nestedMap);
                        break;
                    case JsonValue single when single.TryGetValue(out string? oneUrl):
                        AddColorBoundUrls(color, oneUrl);
                        break;
                }
            }
        }

        void ConsumeColorArray(JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject jo)
                {
                    continue;
                }

                var attrName = GetString(jo, "attrName") ?? GetString(jo, "attr_name") ?? GetString(jo, "name");
                if (!string.IsNullOrWhiteSpace(attrName) &&
                    !attrName.Contains("color", StringComparison.OrdinalIgnoreCase) &&
                    !attrName.Contains("colour", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var color = GetString(jo, "custom_value")
                            ?? GetString(jo, "customValue")
                            ?? GetString(jo, "attrValue")
                            ?? GetString(jo, "attr_value")
                            ?? GetString(jo, "color")
                            ?? GetString(jo, "colorValue")
                            ?? GetString(jo, "colorName")
                            ?? GetString(jo, "value")
                            ?? GetString(jo, "valueName");
                if (string.IsNullOrWhiteSpace(color))
                {
                    continue;
                }

                AddColorBoundFromImageObject(color, jo);
            }
        }

        void ConsumeColorNode(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject mo:
                    ConsumeColorMapObject(mo);
                    break;
                case JsonArray ma:
                    ConsumeColorArray(ma);
                    break;
                case JsonValue jv when jv.TryGetValue(out string? raw) && !string.IsNullOrWhiteSpace(raw):
                    try
                    {
                        var parsed = JsonNode.Parse(raw);
                        ConsumeColorNode(parsed);
                    }
                    catch (JsonException)
                    {
                        // ignore malformed skuImgs string
                    }

                    break;
            }
        }

        foreach (var mapKey in new[]
                 {
                     "skuImgs", "salePropImageMap", "uploadImageInfoMap",
                     "colorImageMap", "variantImageMap", "skuImageMap", "propertyImageMap"
                 })
        {
            ConsumeColorNode(obj[mapKey]);
        }

        return images;
    }

    private static string NormalizeColorKey(string color) =>
        string.Join(
            ' ',
            color.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Guid? FindVariationIdForColor(Listing listing, string color)
    {
        var key = NormalizeColorKey(color);
        return listing.Variations
            .FirstOrDefault(v =>
            {
                var c = ColorFromVariation(v);
                return !string.IsNullOrWhiteSpace(c) &&
                       string.Equals(NormalizeColorKey(c), key, StringComparison.OrdinalIgnoreCase);
            })
            ?.Id;
    }

    /// <summary>
    /// When skuImgs hydrate did not bind a color, fall back to each variation's MainImage URLs.
    /// </summary>
    private static void AppendVariationMainImages(List<ListingImageDto> images, Listing listing)
    {
        var urlsByColor = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Track(string? color, string? url)
        {
            if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var key = NormalizeColorKey(color);
            if (!urlsByColor.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                urlsByColor[key] = set;
            }

            set.Add(url);
        }

        foreach (var img in images.Where(i => !string.IsNullOrWhiteSpace(i.Url)))
        {
            var color = img.ColorValue;
            if (string.IsNullOrWhiteSpace(color) && img.ListingSkuId.HasValue)
            {
                color = ColorFromVariation(listing.Variations.FirstOrDefault(v => v.Id == img.ListingSkuId));
            }

            Track(color, img.Url);
        }

        var sort = images.Count == 0 ? 0 : images.Max(i => i.SortOrder) + 1;
        foreach (var variation in listing.Variations)
        {
            var color = ColorFromVariation(variation);
            if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(variation.MainImage))
            {
                continue;
            }

            var key = NormalizeColorKey(color);
            if (!urlsByColor.TryGetValue(key, out var existing))
            {
                existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                urlsByColor[key] = existing;
            }

            foreach (var url in variation.MainImage.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var upgraded = UpgradeHttpToHttps(url);
                if (string.IsNullOrWhiteSpace(upgraded) ||
                    existing.Count >= 9 ||
                    !existing.Add(upgraded))
                {
                    continue;
                }

                images.Add(new ListingImageDto
                {
                    Url = upgraded,
                    MediaType = "Main",
                    ListingSkuId = variation.Id,
                    ColorValue = color,
                    SortOrder = sort++
                });
            }
        }
    }

    private static string? JoinVariationImageUrls(string? mainImage, IReadOnlyList<string> imageUrls)
    {
        const int max = 9;
        var urls = new List<string>();

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (var part in raw.Split(
                         '|',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (urls.Count >= max)
                {
                    return;
                }

                var upgraded = UpgradeHttpToHttps(part);
                if (!string.IsNullOrWhiteSpace(upgraded) &&
                    upgraded.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !urls.Contains(upgraded, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(upgraded);
                }
            }
        }

        Add(mainImage);
        foreach (var url in imageUrls)
        {
            Add(url);
        }

        return urls.Count == 0 ? null : string.Join('|', urls);
    }

    /// <summary>
    /// Hydrates the Specifics rows the edit form shows, from whichever attribute array the payload uses.
    /// </summary>
    internal static List<ListingAttributeDto> ParseAttributes(JsonObject obj)
    {
        var result = new List<ListingAttributeDto>();

        // The supplier detail echoes the sale-prop axes (Color/Size) into the attribute list. Showing
        // them in the Specifics card invites the seller to edit a value that in fact lives on the
        // variations, and pushing them back is rejected as "Product attributes are unavailable".
        var salePropIds = CollectSalePropAttrIds(obj);

        foreach (var key in new[] { "attributeList", "attributes", "productAttributes", "productAttributeList" })
        {
            var arr = GetJsonArrayFlexible(obj, key);
            if (arr is null)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject a)
                {
                    continue;
                }

                var attrId = GetString(a, "attrId") ?? GetString(a, "id") ?? GetString(a, "attributeId") ?? "";
                if (salePropIds.Contains(attrId.Trim()))
                {
                    continue;
                }

                var name = GetString(a, "attrName") ?? GetString(a, "name") ?? GetString(a, "attributeName");
                var value = GetString(a, "attrValue") ?? GetString(a, "value") ?? GetString(a, "attributeValue");
                string? valueId = GetString(a, "valueId") ?? GetString(a, "value_id");
                List<ListingAttributeValueDto>? multiValues = null;
                if (a["values"] is JsonArray valuesArr && valuesArr.Count > 0)
                {
                    var valueObjs = valuesArr.OfType<JsonObject>().ToList();
                    multiValues = valueObjs
                        .Select(v => new ListingAttributeValueDto
                        {
                            ValueId = GetString(v, "valueId") ?? GetString(v, "value_id") ?? GetString(v, "id"),
                            ValueName = GetString(v, "valueName") ?? GetString(v, "value") ?? GetString(v, "name")
                        })
                        .Where(v => !string.IsNullOrWhiteSpace(v.ValueName) || !string.IsNullOrWhiteSpace(v.ValueId))
                        .ToList();
                    if (multiValues.Count == 0)
                    {
                        multiValues = null;
                    }

                    if (string.IsNullOrWhiteSpace(value) && multiValues is { Count: > 0 })
                    {
                        value = string.Join(", ", multiValues
                            .Select(v => v.ValueName ?? "")
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    valueId ??= multiValues?
                        .Select(v => v.ValueId)
                        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                }

                if (string.IsNullOrWhiteSpace(attrId) && string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new ListingAttributeDto
                {
                    AttrId = string.IsNullOrWhiteSpace(attrId) ? name ?? Guid.NewGuid().ToString("N") : attrId,
                    AttrName = name,
                    AttrValue = value,
                    ValueId = valueId,
                    Values = multiValues,
                    ValueType = GetString(a, "valueType"),
                    IsRequired = a["isRequired"]?.GetValue<bool>()
                                 ?? a["is_required"]?.GetValue<bool>()
                                 ?? false
                });
            }

            if (result.Count > 0)
            {
                break;
            }
        }

        FillMissingValueIdsFromSchema(obj, result);
        return result;
    }

    /// <summary>
    /// Gives the form the enum ids for rows that arrived as plain supplier text — Auto Specifics
    /// deliberately skips attributes that already hold a value, so nothing else fills them, and push
    /// would send a name with no <c>valueId</c>.
    /// <para>
    /// The embedded schema is the biggest field in RawPayload, so it is only read when a row is
    /// actually missing an id: an already-canonical listing hydrates without touching it.
    /// </para>
    /// </summary>
    private static void FillMissingValueIdsFromSchema(JsonObject obj, List<ListingAttributeDto> attrs)
    {
        var needIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in attrs)
        {
            if (string.IsNullOrWhiteSpace(attr.AttrId))
            {
                continue;
            }

            var missing = attr.Values is { Count: > 0 }
                ? attr.Values.Any(v => !IsInt64Id(v.ValueId) && !string.IsNullOrWhiteSpace(v.ValueName))
                : !IsInt64Id(attr.ValueId) && !string.IsNullOrWhiteSpace(attr.AttrValue);
            if (missing)
            {
                needIds.Add(attr.AttrId.Trim());
            }
        }

        if (needIds.Count == 0)
        {
            return;
        }

        var schema = ReadEmbeddedCategoryAttributes(obj, needIds);
        if (schema.Count == 0)
        {
            return;
        }

        foreach (var attr in attrs)
        {
            if (string.IsNullOrWhiteSpace(attr.AttrId)
                || !needIds.Contains(attr.AttrId.Trim())
                || !schema.TryGetValue(attr.AttrId.Trim(), out var schemaAttr)
                || schemaAttr.OptionByName.Count == 0)
            {
                continue;
            }

            attr.AttrName = schemaAttr.AttrName;
            attr.Values = FillValueIds(schemaAttr, attr);
            attr.ValueId = attr.Values?
                .Select(v => v.ValueId)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? attr.ValueId;
        }
    }

    /// <summary>
    /// Matches each value name against the schema enum. Values the schema does not know are left
    /// untouched — the push pass decides whether they survive.
    /// </summary>
    private static List<ListingAttributeValueDto>? FillValueIds(
        EmbeddedCategoryAttribute schemaAttr,
        ListingAttributeDto attr)
    {
        var source = attr.Values;
        if (source is null && !string.IsNullOrWhiteSpace(attr.AttrValue))
        {
            source =
            [
                new ListingAttributeValueDto { ValueId = attr.ValueId, ValueName = attr.AttrValue }
            ];
        }

        if (source is null || source.Count == 0)
        {
            return attr.Values;
        }

        var filled = new List<ListingAttributeValueDto>(source.Count);
        foreach (var v in source)
        {
            var name = v.ValueName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || IsInt64Id(v.ValueId))
            {
                filled.Add(v);
                continue;
            }

            filled.Add(
                schemaAttr.OptionByName.TryGetValue(
                    ListingSpecificsMappingEngine.NormalizeKey(name),
                    out var hit)
                    ? new ListingAttributeValueDto { ValueId = hit.ValueId, ValueName = hit.ValueName }
                    : v);
        }

        return filled;
    }

    private static List<ListingCertificationDto> ParseCertifications(JsonObject obj)
    {
        var result = new List<ListingCertificationDto>();
        foreach (var key in new[] { "certificationList", "certifications", "productCertifications" })
        {
            var arr = GetJsonArrayFlexible(obj, key);
            if (arr is null)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject c)
                {
                    continue;
                }

                var certType = GetString(c, "certType") ?? GetString(c, "type") ?? GetString(c, "name");
                if (string.IsNullOrWhiteSpace(certType))
                {
                    continue;
                }

                result.Add(new ListingCertificationDto
                {
                    CertType = certType,
                    ExternalId = GetString(c, "externalId") ?? GetString(c, "id"),
                    Name = GetString(c, "name"),
                    Phone = GetString(c, "phone"),
                    Email = GetString(c, "email"),
                    Address = GetString(c, "address"),
                    FileUrls = GetString(c, "fileUrls") ??
                               (c["fileUrls"] is JsonArray fa ? fa.ToJsonString() : null)
                });
            }

            if (result.Count > 0)
            {
                break;
            }
        }

        // Product Certification Yes/No live in productAttributes — seed local cert rows when missing.
        MergeProductCertificationAttributesIntoCerts(obj, result);
        return result;
    }

    private static void MergeProductCertificationAttributesIntoCerts(
        JsonObject obj,
        List<ListingCertificationDto> certs)
    {
        var attrs = ParseAttributes(obj);
        foreach (var def in ProductCertificationAttrs)
        {
            var existing = certs.FirstOrDefault(c =>
                string.Equals(c.CertType, def.CertType, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.Name))
            {
                existing.Name = NormalizeYesNo(existing.Name);
                continue;
            }

            var attr = attrs.FirstOrDefault(a =>
                string.Equals(a.AttrId, def.AttributeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.AttrName, def.AttributeName, StringComparison.OrdinalIgnoreCase));

            var yesNo = NormalizeYesNo(attr?.AttrValue);
            if (existing is not null)
            {
                existing.Name = yesNo;
            }
            else
            {
                certs.Add(new ListingCertificationDto
                {
                    CertType = def.CertType,
                    Name = yesNo,
                    FileUrls = "[]"
                });
            }
        }
    }

    private static JsonArray? GetJsonArrayFlexible(JsonObject obj, string key)
    {
        if (obj[key] is JsonArray arr)
        {
            return arr;
        }

        if (obj[key] is JsonValue jv &&
            jv.TryGetValue<string>(out var raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                return JsonNode.Parse(raw) as JsonArray;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private static void IndexVariationExtras(JsonObject obj, Listing listing, RawHydration result)
    {
        foreach (var key in new[] { "variation", "variationList", "variations", "skuList", "skus" })
        {
            if (obj[key] is not JsonArray arr)
            {
                continue;
            }

            foreach (var item in arr)
            {
                if (item is not JsonObject vObj)
                {
                    continue;
                }

                var extra = new VariationExtra
                {
                    Barcode = GetString(vObj, "barcode") ?? GetString(vObj, "upc") ?? GetString(vObj, "ean"),
                    OriginalPrice = GetDecimal(vObj, "originalPrice") ?? GetDecimal(vObj, "salePrice"),
                    VariantAttributes = GetString(vObj, "variantAttributes") ??
                                        BuildVariantAttributesJson(
                                            GetString(vObj, "title") ?? GetString(vObj, "name"),
                                            GetString(vObj, "option1"),
                                            GetString(vObj, "option2"),
                                            GetString(vObj, "option3"))
                };

                if (TryGetLong(vObj, "id", out var skuId) && skuId > 0)
                {
                    result.VariationExtrasBySkuId[skuId] = extra;
                    var local = listing.Variations.FirstOrDefault(v => v.FourSellerSkuId == skuId);
                    if (local is not null)
                    {
                        result.VariationExtras[local.Id] = extra;
                    }
                }

                var sellerSku = GetString(vObj, "sellerSku") ?? GetString(vObj, "seller_sku");
                if (!string.IsNullOrWhiteSpace(sellerSku))
                {
                    result.VariationExtrasBySellerSku[sellerSku] = extra;
                    var local = listing.Variations.FirstOrDefault(v =>
                        string.Equals(v.SellerSku, sellerSku, StringComparison.OrdinalIgnoreCase));
                    if (local is not null)
                    {
                        result.VariationExtras[local.Id] = extra;
                    }
                }
            }
        }
    }

    private static string? GetString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null || node is JsonObject or JsonArray)
        {
            return null;
        }

        var s = node.ToString()?.Trim().Trim('"');
        // Treat blank / JSON null token as missing so ?? and FirstNonEmpty can fall through
        // (4Seller often sends "" for unused sibling keys like videoUrl next to erpVideoUrl).
        return string.IsNullOrWhiteSpace(s) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : s;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jv)
        {
            return null;
        }

        if (jv.TryGetValue(out decimal d))
        {
            return d;
        }

        if (jv.TryGetValue(out int i))
        {
            return i;
        }

        if (jv.TryGetValue(out long l))
        {
            return l;
        }

        if (jv.TryGetValue(out double dbl))
        {
            return (decimal)dbl;
        }

        if (jv.TryGetValue(out string? s) &&
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
        {
            return d;
        }

        return null;
    }

    private static string? BuildVariantAttributesJson(
        string? title,
        string? option1 = null,
        string? option2 = null,
        string? option3 = null)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(option1) || !string.IsNullOrWhiteSpace(option2))
        {
            AssignColorSizeFromOptions(dict, option1, option2);
        }

        if (!string.IsNullOrWhiteSpace(option3))
        {
            dict["Number of Items"] = option3.Trim();
        }

        if (dict.Count == 0 && !string.IsNullOrWhiteSpace(title))
        {
            var parts = title.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                AssignColorSizeFromTitleParts(dict, parts[0], parts[1]);
                if (parts.Length >= 3)
                {
                    dict["Number of Items"] = parts[2];
                }
            }
            else if (parts.Length == 1)
            {
                dict[LooksLikeSizeToken(parts[0]) ? "Size" : "Color"] = parts[0];
            }
        }

        RepairSwappedColorSizeAxes(dict);
        return dict.Count == 0 ? null : JsonSerializer.Serialize(dict);
    }

    /// <summary>
    /// option1/option2 are usually Color/Size, but some 4Seller SKUs store them swapped
    /// (e.g. option1=one-size, option2=White). Detect and assign by token shape.
    /// </summary>
    private static void AssignColorSizeFromOptions(
        Dictionary<string, string> dict,
        string? option1,
        string? option2)
    {
        var o1 = option1?.Trim() ?? string.Empty;
        var o2 = option2?.Trim() ?? string.Empty;
        if (o1.Length == 0 && o2.Length == 0)
        {
            return;
        }

        if (o1.Length > 0 && o2.Length > 0 && LooksLikeSizeToken(o1) && !LooksLikeSizeToken(o2))
        {
            dict["Size"] = o1;
            dict["Color"] = o2;
            return;
        }

        if (o1.Length > 0)
        {
            dict["Color"] = o1;
        }

        if (o2.Length > 0)
        {
            dict["Size"] = o2;
        }
    }

    private static void AssignColorSizeFromTitleParts(
        Dictionary<string, string> dict,
        string first,
        string second)
    {
        var a = first.Trim();
        var b = second.Trim();
        if (LooksLikeSizeToken(a) && !LooksLikeSizeToken(b))
        {
            dict["Size"] = a;
            dict["Color"] = b;
        }
        else if (LooksLikeSizeToken(b) && !LooksLikeSizeToken(a))
        {
            dict["Color"] = a;
            dict["Size"] = b;
        }
        else if (LooksLikeSizeToken(a))
        {
            // Size/Color[/Items] — matches FE formatVariantLabel order.
            dict["Size"] = a;
            dict["Color"] = b;
        }
        else
        {
            // Legacy Color/Size.
            dict["Color"] = a;
            dict["Size"] = b;
        }
    }

    /// <summary>
    /// If Color holds a size token and Size holds a non-size (e.g. White), swap axes.
    /// </summary>
    private static void RepairSwappedColorSizeAxes(Dictionary<string, string> dict)
    {
        if (!dict.TryGetValue("Color", out var color) ||
            !dict.TryGetValue("Size", out var size))
        {
            return;
        }

        if (LooksLikeSizeToken(color) && !LooksLikeSizeToken(size))
        {
            dict["Color"] = size;
            dict["Size"] = color;
        }
    }

    private static string? RepairVariantAttributesJson(string? attrsJson)
    {
        var attrs = ParseAttrs(attrsJson);
        if (attrs.Count == 0)
        {
            return attrsJson;
        }

        RepairSwappedColorSizeAxes(attrs);
        return JsonSerializer.Serialize(attrs);
    }

    /// <summary>
    /// Prefer existing structured Color/Size JSON when title tokens match (order-insensitive),
    /// after repairing Color↔Size swaps (one-size / XS-M wrongly stored as Color).
    /// </summary>
    /// <summary>
    /// Compares two variant-attribute payloads by content rather than by raw JSON text, so a
    /// formatting-only difference (spacing, key order) is not treated as a change.
    /// </summary>
    private static bool VariantAttributesEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        var leftAttrs = ParseAttrs(left);
        var rightAttrs = ParseAttrs(right);
        if (leftAttrs.Count == 0 || leftAttrs.Count != rightAttrs.Count)
        {
            // An unparsable stored value must still be replaced by the repaired one.
            return false;
        }

        foreach (var (key, value) in leftAttrs)
        {
            if (!rightAttrs.TryGetValue(key, out var other) ||
                !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ResolveSyncedVariantAttributes(string? existingAttrsJson, string? title)
    {
        var fromTitle = BuildVariantAttributesJson(title);
        var repairedExisting = RepairVariantAttributesJson(existingAttrsJson);
        if (string.IsNullOrWhiteSpace(repairedExisting))
        {
            return fromTitle;
        }

        var existing = ParseAttrs(repairedExisting);
        var hasStructured =
            existing.ContainsKey("Color") ||
            existing.ContainsKey("Size") ||
            existing.ContainsKey("Number of Items");
        if (!hasStructured)
        {
            return fromTitle ?? repairedExisting;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return repairedExisting;
        }

        var titleTokens = title
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var existingTokens = existing.Values
            .Select(v => v.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (titleTokens.Count > 0 &&
            titleTokens.Count == existingTokens.Count &&
            titleTokens.SetEquals(existingTokens))
        {
            // Keep repaired axis assignment (not the raw corrupt Color=one-size JSON).
            return repairedExisting;
        }

        return fromTitle ?? repairedExisting;
    }

    private static string? TitleFromVariantAttributes(string? attrsJson)
    {
        var attrs = ParseAttrs(attrsJson);
        if (attrs.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (attrs.TryGetValue("Color", out var color))
        {
            parts.Add(color);
        }

        if (attrs.TryGetValue("Size", out var size))
        {
            parts.Add(size);
        }

        if (attrs.TryGetValue("Number of Items", out var items))
        {
            parts.Add(items);
        }

        if (parts.Count == 0)
        {
            parts.AddRange(attrs.Values.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static Dictionary<string, string> ParseAttrs(string? attrsJson)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrsJson))
        {
            return dict;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(attrsJson);
            if (parsed is null)
            {
                return dict;
            }

            foreach (var (k, v) in parsed)
            {
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                {
                    dict[k] = v;
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return dict;
    }

    private static string? ColorFromVariation(ListingVariation? variation)
    {
        if (variation is null)
        {
            return null;
        }

        var attrs = ParseAttrs(variation.VariantAttributes);
        if (attrs.TryGetValue("Color", out var color) &&
            !string.IsNullOrWhiteSpace(color) &&
            !color.Contains('/'))
        {
            return color.Trim();
        }

        return ExtractColorFromTitle(variation.Title) ??
               (color is not null ? ExtractColorFromTitle(color) : null);
    }

    /// <summary>
    /// Title may be Color/Size (legacy) or Size/Color (<c>formatVariantLabel</c>).
    /// </summary>
    private static string? ExtractColorFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (!title.Contains('/'))
        {
            return title.Trim();
        }

        var parts = title.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        // Size-looking first token → Size/Color order.
        if (LooksLikeSizeToken(parts[0]))
        {
            return parts[1];
        }

        return parts[0];
    }

    private static bool LooksLikeSizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var t = token.Trim();
        if (t.Equals("XXS", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("XS", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("S", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("M", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("L", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("XL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("XXL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("2XL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("3XL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("4XL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("5XL", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("OS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // one-size / free-size (common TikTok / Shein single-size SKUs)
        var compact = t.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Equals("onesize", StringComparison.OrdinalIgnoreCase) ||
            compact.Equals("freesize", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // N XL (0XL, 1XL, 6XL, …)
        if (t.Length >= 3 &&
            t.EndsWith("XL", StringComparison.OrdinalIgnoreCase) &&
            t[..^2].All(char.IsDigit))
        {
            return true;
        }

        // Ranges: XS-M, S-L, XL-XXL, 1XL-3XL
        var sizeAtom = @"XXS|XS|S|M|L|XL|XXL|\d+XL";
        return System.Text.RegularExpressions.Regex.IsMatch(
            t,
            $"^({sizeAtom})-({sizeAtom})$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string? FirstImage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var part = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UpgradeHttpToHttps)
            .FirstOrDefault(static p => !string.IsNullOrWhiteSpace(p));
        return string.IsNullOrWhiteSpace(part) ? UpgradeHttpToHttps(raw.Trim()) : part;
    }

    /// <summary>
    /// 4Seller rejects explicit http:// image URLs (Invalid Para). Upgrade scheme only.
    /// </summary>
    private static string? UpgradeHttpToHttps(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + trimmed[7..]
            : trimmed;
    }

    private static string? UpgradeHttpPipeJoined(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UpgradeHttpToHttps)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    /// <summary>
    /// Replace every <c>http://</c> with <c>https://</c> inside a text blob
    /// (pipe-joined URLs, HTML <c>img src</c>, JSON-string fields). Safe vs <c>https://</c>
    /// because that scheme does not contain the <c>http://</c> prefix as a substring start.
    /// </summary>
    private static string UpgradeHttpUrlsInText(string text)
    {
        if (string.IsNullOrEmpty(text) ||
            text.IndexOf("http://", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return text;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            text,
            "http://",
            "https://",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void UpgradeHttpUrlsInJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prop in obj.ToList())
                {
                    if (prop.Value is JsonValue jv && jv.TryGetValue(out string? s) && s is not null)
                    {
                        var upgraded = UpgradeHttpUrlsInText(s);
                        if (!string.Equals(upgraded, s, StringComparison.Ordinal))
                        {
                            obj[prop.Key] = upgraded;
                        }
                    }
                    else
                    {
                        UpgradeHttpUrlsInJsonNode(prop.Value);
                    }
                }

                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue jv && jv.TryGetValue(out string? s) && s is not null)
                    {
                        var upgraded = UpgradeHttpUrlsInText(s);
                        if (!string.Equals(upgraded, s, StringComparison.Ordinal))
                        {
                            arr[i] = upgraded;
                        }
                    }
                    else
                    {
                        UpgradeHttpUrlsInJsonNode(arr[i]);
                    }
                }

                break;
        }
    }

    private static DateTime? ParseFourSellerTime(string? value) =>
        AppTimeZones.ParseFourSellerNaiveAsUtc(value);

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
