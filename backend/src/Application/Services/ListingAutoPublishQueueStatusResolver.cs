using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Batch-resolves auto publish queue badges for draft listing list rows.</summary>
public sealed class ListingAutoPublishQueueStatusResolver : IListingAutoPublishQueueStatusResolver
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;

    public ListingAutoPublishQueueStatusResolver(IApplicationDbContext db, ISettingProvider settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<IReadOnlyDictionary<Guid, ListingAutoPublishQueueStatusResult>> ResolveAsync(
        IReadOnlyList<Guid> listingIds,
        CancellationToken cancellationToken = default)
    {
        if (listingIds.Count == 0)
        {
            return new Dictionary<Guid, ListingAutoPublishQueueStatusResult>();
        }

        var enabledRaw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.Enabled, cancellationToken);
        if (!string.Equals(enabledRaw?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<Guid, ListingAutoPublishQueueStatusResult>();
        }

        var defaultSettings = await _settings.GetValuesAsync(
            [
                ListingAutoPublishSettingKeys.DefaultIntervalSeconds,
                ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes
            ],
            cancellationToken);
        var defaultIntervalSeconds = ParseInt(
            defaultSettings.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultIntervalSeconds),
            120,
            1);
        var defaultMinDraftAgeMinutes = ParseInt(
            defaultSettings.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes),
            int.Parse(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutesValue),
            ListingAutoPublishSettingKeys.MinDraftAgeMinutes);

        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var inFlightLower = PublishStatuses.InFlightLowercase;

        var rows = await (
            from l in _db.Listings.AsNoTracking()
            join s in _db.Shops.AsNoTracking() on l.ShopId equals s.Id into shopJoin
            from shop in shopJoin.DefaultIfEmpty()
            join spi in _db.SupplierProductInfos.AsNoTracking() on l.SupplierProductInfoId equals spi.Id into spiJoin
            from supplier in spiJoin.DefaultIfEmpty()
            where listingIds.Contains(l.Id)
                  && l.PageType == ListingPageTypes.Draft
            select new ListingQueueRow(
                l.Id,
                l.ShopId,
                l.AutoPublishSkipped,
                l.PublishStatus,
                l.CreatedAt,
                l.SupplierProductInfoId,
                supplier != null ? supplier.SizeChartImageUrl : null,
                shop != null ? shop.Status : null,
                shop != null && shop.AutoPublishEnabled,
                shop != null ? shop.FourSellerShopId : null,
                shop != null ? shop.PublishLimitPublished : null,
                shop != null ? shop.PublishLimitTotal : null,
                shop != null ? shop.AutoPublishBlockedUntil : null,
                shop != null ? shop.AutoPublishMinDraftAgeMinutes : null,
                shop != null ? shop.AutoPublishIntervalSeconds : null,
                shop != null ? shop.LastAutoPublishAt : null))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new Dictionary<Guid, ListingAutoPublishQueueStatusResult>();
        }

        var pageIds = rows.Select(r => r.Id).ToList();

        var shopIdsOnPage = rows
            .Where(r => r.ShopId is not null)
            .Select(r => r.ShopId!.Value)
            .Distinct()
            .ToList();

        var activeRunsForShops = shopIdsOnPage.Count == 0
            ? []
            : await (
                from r in _db.ListingAutoPublishRuns.AsNoTracking()
                join l in _db.Listings.AsNoTracking() on r.ListingId equals l.Id
                where l.ShopId != null
                      && shopIdsOnPage.Contains(l.ShopId.Value)
                      && r.Status == ListingAutoPublishRunStatuses.Running
                      && r.StartedAt >= staleRunCutoff
                select new { r.ListingId, ShopId = l.ShopId!.Value })
                .ToListAsync(cancellationToken);

        var processingSet = activeRunsForShops.Select(x => x.ListingId).ToHashSet();
        var processingShopSet = activeRunsForShops.Select(x => x.ShopId).ToHashSet();
        var now = DateTime.UtcNow;

        var runRows = await _db.ListingAutoPublishRuns.AsNoTracking()
            .Where(r => pageIds.Contains(r.ListingId))
            .Select(r => new { r.ListingId, r.Status, r.StartedAt })
            .ToListAsync(cancellationToken);

        var latestRunByListing = runRows
            .GroupBy(r => r.ListingId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(x => x.StartedAt).First();
                    return latest.Status;
                });

        var headByShop = await ComputeGlobalFifoHeadPerShopAsync(
            shopIdsOnPage,
            defaultMinDraftAgeMinutes,
            inFlightLower,
            processingSet,
            cancellationToken);

        var result = new Dictionary<Guid, ListingAutoPublishQueueStatusResult>();

        foreach (var row in rows)
        {
            latestRunByListing.TryGetValue(row.Id, out var lastRunStatus);

            if (processingSet.Contains(row.Id))
            {
                result[row.Id] = new ListingAutoPublishQueueStatusResult(
                    ListingAutoPublishQueueStatuses.Processing,
                    lastRunStatus ?? ListingAutoPublishRunStatuses.Running);
                continue;
            }

            if (IsFailed(row))
            {
                result[row.Id] = new ListingAutoPublishQueueStatusResult(
                    ListingAutoPublishQueueStatuses.Failed,
                    lastRunStatus);
                continue;
            }

            if (!IsWaitingEligible(row, defaultMinDraftAgeMinutes, inFlightLower, processingSet, now)
                || row.ShopId is not Guid shopId)
            {
                continue;
            }

            var shopProcessing = processingShopSet.Contains(shopId);
            var isGlobalHead = headByShop.TryGetValue(shopId, out var headId) && headId == row.Id;

            if (isGlobalHead && !shopProcessing)
            {
                result[row.Id] = new ListingAutoPublishQueueStatusResult(
                    ListingAutoPublishQueueStatuses.Waiting,
                    lastRunStatus,
                    ComputeNextPickAt(row, defaultIntervalSeconds, now));
                continue;
            }

            result[row.Id] = new ListingAutoPublishQueueStatusResult(
                ListingAutoPublishQueueStatuses.Queue,
                lastRunStatus);
        }

        return result;
    }

    /// <summary>
    /// Oldest eligible draft per shop (global FIFO) — aligned with <c>PickListingIdAsync</c> in the background service.
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> ComputeGlobalFifoHeadPerShopAsync(
        List<Guid> shopIdsOnPage,
        int defaultMinDraftAgeMinutes,
        string[] inFlightLower,
        HashSet<Guid> processingListingIds,
        CancellationToken cancellationToken)
    {
        if (shopIdsOnPage.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var shopRows = await _db.Shops.AsNoTracking()
            .Where(s => shopIdsOnPage.Contains(s.Id))
            .Select(s => new ShopEligibilityRow(
                s.Id,
                s.Status,
                s.AutoPublishEnabled,
                s.FourSellerShopId,
                s.PublishLimitPublished,
                s.PublishLimitTotal,
                s.AutoPublishBlockedUntil,
                s.AutoPublishMinDraftAgeMinutes))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var eligibleShops = shopRows
            .Where(s => IsShopEligibleForAutoPublish(s, now))
            .ToList();
        if (eligibleShops.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var eligibleShopIds = eligibleShops.Select(s => s.Id).ToHashSet();

        var listingCandidates = await (
            from l in _db.Listings.AsNoTracking()
            join spi in _db.SupplierProductInfos.AsNoTracking() on l.SupplierProductInfoId equals spi.Id into spiJoin
            from supplier in spiJoin.DefaultIfEmpty()
            where l.ShopId != null
                  && eligibleShopIds.Contains(l.ShopId.Value)
                  && l.PageType == ListingPageTypes.Draft
                  && !l.AutoPublishSkipped
            select new GlobalFifoCandidate(
                l.Id,
                l.ShopId!.Value,
                l.PublishStatus,
                l.CreatedAt,
                l.SupplierProductInfoId,
                supplier != null ? supplier.SizeChartImageUrl : null))
            .ToListAsync(cancellationToken);

        var headByShop = new Dictionary<Guid, Guid>();

        foreach (var shop in eligibleShops)
        {
            var minAge = ListingAutoPublishSettingKeys.ResolveMinDraftAgeMinutes(
                shop.MinDraftAgeMinutes,
                defaultMinDraftAgeMinutes);
            var ageCutoff = now - TimeSpan.FromMinutes(minAge);

            var head = listingCandidates
                .Where(c => c.ShopId == shop.Id)
                .Where(c => !processingListingIds.Contains(c.Id))
                .Where(c => !PublishStatuses.BlocksAutoPublishPick(c.PublishStatus))
                .Where(c => c.CreatedAt <= ageCutoff)
                .Where(c => c.SupplierProductInfoId is not null
                            && !string.IsNullOrWhiteSpace(c.SizeChartImageUrl))
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefault();

            if (head is not null)
            {
                headByShop[shop.Id] = head.Id;
            }
        }

        return headByShop;
    }

    private static bool IsShopEligibleForAutoPublish(
        ShopEligibilityRow shop,
        DateTime utcNow)
    {
        if (!string.Equals(shop.Status, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!shop.AutoPublishEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(shop.FourSellerShopId))
        {
            return false;
        }

        if (ShopListingSlotRules.IsBlocked(shop.AutoPublishBlockedUntil, utcNow))
        {
            return false;
        }

        if (ShopListingSlotRules.IsFull(shop.PublishLimitPublished ?? 0, shop.PublishLimitTotal))
        {
            return false;
        }

        return true;
    }

    private static DateTime ComputeNextPickAt(ListingQueueRow row, int defaultIntervalSeconds, DateTime now)
    {
        var interval = row.ShopIntervalSeconds ?? defaultIntervalSeconds;
        var nextPickAt = row.ShopLastAutoPublishAt.HasValue
            ? row.ShopLastAutoPublishAt.Value.AddSeconds(interval)
            : now;
        return nextPickAt <= now ? now : nextPickAt;
    }

    /// <summary>
    /// Badge Failed khớp cổng pick: chỉ khi skip. Run failed/cancelled không skip vẫn vào FIFO.
    /// </summary>
    private static bool IsFailed(ListingQueueRow row) => row.AutoPublishSkipped;

    private static bool IsWaitingEligible(
        ListingQueueRow row,
        int defaultMinDraftAgeMinutes,
        string[] inFlightLower,
        HashSet<Guid> processingSet,
        DateTime utcNow)
    {
        _ = inFlightLower;
        if (row.AutoPublishSkipped)
        {
            return false;
        }

        if (row.ShopId is null)
        {
            return false;
        }

        if (!string.Equals(row.ShopStatus, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!row.ShopAutoPublishEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(row.FourSellerShopId))
        {
            return false;
        }

        if (ShopListingSlotRules.IsBlocked(row.AutoPublishBlockedUntil, utcNow))
        {
            return false;
        }

        if (ShopListingSlotRules.IsFull(row.PublishLimitPublished ?? 0, row.PublishLimitTotal))
        {
            return false;
        }

        if (PublishStatuses.BlocksAutoPublishPick(row.PublishStatus))
        {
            return false;
        }

        var minAge = ListingAutoPublishSettingKeys.ResolveMinDraftAgeMinutes(
            row.ShopMinDraftAgeMinutes,
            defaultMinDraftAgeMinutes);
        var cutoff = utcNow - TimeSpan.FromMinutes(minAge);
        if (row.CreatedAt > cutoff)
        {
            return false;
        }

        if (row.SupplierProductInfoId is null
            || string.IsNullOrWhiteSpace(row.SizeChartImageUrl))
        {
            return false;
        }

        return !processingSet.Contains(row.Id);
    }

    private static int ParseInt(string? raw, int defaultValue, int min)
    {
        if (!int.TryParse(raw?.Trim(), out var n))
        {
            n = defaultValue;
        }

        return Math.Max(min, n);
    }

    private sealed record ListingQueueRow(
        Guid Id,
        Guid? ShopId,
        bool AutoPublishSkipped,
        string? PublishStatus,
        DateTime CreatedAt,
        Guid? SupplierProductInfoId,
        string? SizeChartImageUrl,
        string? ShopStatus,
        bool ShopAutoPublishEnabled,
        string? FourSellerShopId,
        int? PublishLimitPublished,
        int? PublishLimitTotal,
        DateTime? AutoPublishBlockedUntil,
        int? ShopMinDraftAgeMinutes,
        int? ShopIntervalSeconds,
        DateTime? ShopLastAutoPublishAt);

    private sealed record ShopEligibilityRow(
        Guid Id,
        string? Status,
        bool AutoPublishEnabled,
        string? FourSellerShopId,
        int? PublishLimitPublished,
        int? PublishLimitTotal,
        DateTime? AutoPublishBlockedUntil,
        int? MinDraftAgeMinutes);

    private sealed record GlobalFifoCandidate(
        Guid Id,
        Guid ShopId,
        string? PublishStatus,
        DateTime CreatedAt,
        Guid? SupplierProductInfoId,
        string? SizeChartImageUrl);
}
