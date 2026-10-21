using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Batch-resolves auto publish scheduler status for shop list rows.</summary>
public sealed class ShopAutoPublishSchedulerStatusResolver : IShopAutoPublishSchedulerStatusResolver
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;

    public ShopAutoPublishSchedulerStatusResolver(IApplicationDbContext db, ISettingProvider settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<IReadOnlyDictionary<Guid, ShopAutoPublishSchedulerStatusResult>> ResolveAsync(
        IReadOnlyList<Guid> shopIds,
        CancellationToken cancellationToken = default)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, ShopAutoPublishSchedulerStatusResult>();
        }

        var enabledRaw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.Enabled, cancellationToken);
        if (!string.Equals(enabledRaw?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<Guid, ShopAutoPublishSchedulerStatusResult>();
        }

        var settings = await _settings.GetValuesAsync(
            [
                ListingAutoPublishSettingKeys.DefaultIntervalSeconds,
                ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes
            ],
            cancellationToken);

        var defaultIntervalSeconds = ParseInt(
            settings.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultIntervalSeconds),
            120,
            1);
        var defaultMinDraftAgeMinutes = ParseInt(
            settings.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes),
            int.Parse(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutesValue),
            ListingAutoPublishSettingKeys.MinDraftAgeMinutes);

        var shopCandidates = await _db.Shops.AsNoTracking()
            .Where(s => shopIds.Contains(s.Id)
                        && s.AutoPublishEnabled
                        && s.Status == ShopStatuses.Active
                        && s.FourSellerShopId != null
                        && s.FourSellerShopId != "")
            .Where(s => s.AutoPublishBlockedUntil == null || s.AutoPublishBlockedUntil <= DateTime.UtcNow)
            .Select(s => new ShopRow(
                s.Id,
                s.LastAutoPublishAt,
                s.AutoPublishIntervalSeconds,
                s.AutoPublishMinDraftAgeMinutes,
                s.PublishLimitPublished,
                s.PublishLimitTotal))
            .ToListAsync(cancellationToken);

        var shopRows = shopCandidates
            .Where(s => !ShopListingSlotRules.IsFull(
                s.PublishLimitPublished ?? 0,
                s.PublishLimitTotal))
            .ToList();

        if (shopRows.Count == 0)
        {
            return new Dictionary<Guid, ShopAutoPublishSchedulerStatusResult>();
        }

        var activeShopIds = shopRows.Select(s => s.Id).ToList();
        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var inFlightLower = PublishStatuses.InFlightLowercase;
        var now = DateTime.UtcNow;

        var processingShopIds = await (
            from r in _db.ListingAutoPublishRuns.AsNoTracking()
            join l in _db.Listings.AsNoTracking() on r.ListingId equals l.Id
            where l.ShopId != null
                  && activeShopIds.Contains(l.ShopId.Value)
                  && r.Status == ListingAutoPublishRunStatuses.Running
                  && r.StartedAt >= staleRunCutoff
            select l.ShopId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var processingSet = processingShopIds.ToHashSet();

        var processingListingIds = await _db.ListingAutoPublishRuns.AsNoTracking()
            .Where(r => r.Status == ListingAutoPublishRunStatuses.Running
                        && r.StartedAt >= staleRunCutoff)
            .Join(
                _db.Listings.AsNoTracking().Where(l => l.ShopId != null && activeShopIds.Contains(l.ShopId.Value)),
                r => r.ListingId,
                l => l.Id,
                (r, l) => l.Id)
            .ToListAsync(cancellationToken);

        var processingListingSet = processingListingIds.ToHashSet();

        var listingRows = await (
            from l in _db.Listings.AsNoTracking()
            join spi in _db.SupplierProductInfos.AsNoTracking() on l.SupplierProductInfoId equals spi.Id into spiJoin
            from supplier in spiJoin.DefaultIfEmpty()
            where l.ShopId != null
                  && activeShopIds.Contains(l.ShopId.Value)
                  && l.PageType == ListingPageTypes.Draft
                  && !l.AutoPublishSkipped
            select new ListingRow(
                l.Id,
                l.ShopId!.Value,
                l.PublishStatus,
                l.CreatedAt,
                l.SupplierProductInfoId,
                supplier != null ? supplier.SizeChartImageUrl : null))
            .ToListAsync(cancellationToken);

        var eligibleCountByShop = new Dictionary<Guid, int>();
        foreach (var shop in shopRows)
        {
            var minAge = ListingAutoPublishSettingKeys.ResolveMinDraftAgeMinutes(
                shop.MinDraftAgeMinutes,
                defaultMinDraftAgeMinutes);
            var cutoff = now - TimeSpan.FromMinutes(minAge);

            var eligibleCount = listingRows.Count(row =>
                row.ShopId == shop.Id
                && IsEligibleListing(row, cutoff, inFlightLower, processingListingSet));

            if (eligibleCount > 0)
            {
                eligibleCountByShop[shop.Id] = eligibleCount;
            }
        }

        var result = new Dictionary<Guid, ShopAutoPublishSchedulerStatusResult>();

        foreach (var shop in shopRows)
        {
            if (processingSet.Contains(shop.Id))
            {
                result[shop.Id] = new ShopAutoPublishSchedulerStatusResult(
                    ListingAutoPublishQueueStatuses.Processing,
                    null,
                    eligibleCountByShop.GetValueOrDefault(shop.Id));
                continue;
            }

            if (!eligibleCountByShop.TryGetValue(shop.Id, out var eligibleCount) || eligibleCount < 1)
            {
                continue;
            }

            var interval = shop.IntervalSeconds ?? defaultIntervalSeconds;
            var nextPickAt = shop.LastAutoPublishAt.HasValue
                ? shop.LastAutoPublishAt.Value.AddSeconds(interval)
                : now;
            if (nextPickAt <= now)
            {
                nextPickAt = now;
            }

            result[shop.Id] = new ShopAutoPublishSchedulerStatusResult(
                ListingAutoPublishQueueStatuses.Waiting,
                nextPickAt,
                eligibleCount);
        }

        return result;
    }

    private static bool IsEligibleListing(
        ListingRow row,
        DateTime? minAgeCutoff,
        string[] inFlightLower,
        HashSet<Guid> processingListingSet)
    {
        if (processingListingSet.Contains(row.Id))
        {
            return false;
        }

        var publishStatus = row.PublishStatus?.Trim().ToLowerInvariant();
        if (publishStatus is not null
            && (inFlightLower.Contains(publishStatus)
                || publishStatus == PublishStatuses.FailedLowercase))
        {
            return false;
        }

        if (minAgeCutoff.HasValue && row.CreatedAt > minAgeCutoff.Value)
        {
            return false;
        }

        if (row.SupplierProductInfoId is null || string.IsNullOrWhiteSpace(row.SizeChartImageUrl))
        {
            return false;
        }

        return true;
    }

    private static int ParseInt(string? raw, int defaultValue, int min)
    {
        if (!int.TryParse(raw?.Trim(), out var n))
        {
            n = defaultValue;
        }

        return Math.Max(min, n);
    }

    private sealed record ShopRow(
        Guid Id,
        DateTime? LastAutoPublishAt,
        int? IntervalSeconds,
        int? MinDraftAgeMinutes,
        int? PublishLimitPublished,
        int? PublishLimitTotal);

    private sealed record ListingRow(
        Guid Id,
        Guid ShopId,
        string? PublishStatus,
        DateTime CreatedAt,
        Guid? SupplierProductInfoId,
        string? SizeChartImageUrl);
}
