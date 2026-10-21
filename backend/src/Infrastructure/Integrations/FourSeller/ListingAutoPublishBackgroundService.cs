using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>Schedules auto publish for eligible draft listings per shop.</summary>
public sealed class ListingAutoPublishBackgroundService : ResilientBackgroundService
{
    private const int DisabledPollMinutes = 1;
    private const int StartupDelaySeconds = 45;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListingAutoPublishBackgroundService> _logger;
    private readonly IListingAutoPublishConcurrencyGate _concurrencyGate;
    private readonly IListingShopPublishLease _shopLease;
    private readonly IListingAutoPublishDispatchTrigger _dispatchTrigger;

    public ListingAutoPublishBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ListingAutoPublishBackgroundService> logger,
        IListingAutoPublishConcurrencyGate concurrencyGate,
        IListingShopPublishLease shopLease,
        IListingAutoPublishDispatchTrigger dispatchTrigger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _concurrencyGate = concurrencyGate;
        _shopLease = shopLease;
        _dispatchTrigger = dispatchTrigger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = await ReadSettingsAsync(stoppingToken);
            _concurrencyGate.Configure(settings.MaxConcurrentPublishes);

            if (settings.Enabled)
            {
                try
                {
                    await RecoverStaleRunsAsync(stoppingToken);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var dispatched = await DispatchReadyShopsAsync(settings, stoppingToken);
                        if (dispatched <= 0 || _concurrencyGate.AvailableSlots <= 0)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto publish background round failed");
                }

                var delay = await ComputeNextDelayAsync(settings, stoppingToken);
                try
                {
                    await WaitForNextRoundAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            else
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(DisabledPollMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task WaitForNextRoundAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var delayTask = Task.Delay(delay, cts.Token);
        var completed = await Task.WhenAny(delayTask, _dispatchTrigger.WaitAsync(stoppingToken));
        if (completed != delayTask)
        {
            cts.Cancel();
            try
            {
                await delayTask;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // expected when a pipeline signals early wake
            }
        }
    }

    private async Task RecoverStaleRunsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<IListingAutoPublishStaleRunRecovery>();
        var count = await recovery.RecoverStaleRunsAsync(stoppingToken);
        if (count > 0)
        {
            _logger.LogWarning("Recovered {Count} stale auto publish runs", count);
        }

        var retention = scope.ServiceProvider.GetRequiredService<IListingAutoPublishLogRetentionService>();
        var purged = await retention.PurgeOldRunsAsync(stoppingToken);
        if (purged > 0)
        {
            _logger.LogInformation("Purged {Count} old auto publish run logs", purged);
        }
    }

    private async Task<TimeSpan> ComputeNextDelayAsync(AutoPublishJobSettings settings, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var shops = await LoadEligibleShopsAsync(db, ct);

        if (shops.Count == 0)
        {
            return TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.IdlePollSeconds);
        }

        var shopIds = shops.Select(s => s.Id).ToList();
        var runningShopIds = await LoadShopIdsWithActiveRunningRunAsync(db, shopIds, ct);
        var candidates = shops.Where(s => !runningShopIds.Contains(s.Id)).ToList();
        var eligibleShopIds = await LoadShopIdsWithEligibleDraftAsync(
            db,
            candidates,
            settings.DefaultMinDraftAgeMinutes,
            ct);

        var scheduleRows = shops
            .Select(shop => new ListingAutoPublishScheduleCalculator.ShopScheduleRow(
                shop.Id,
                shop.IntervalSeconds,
                shop.LastAutoPublishAt,
                !runningShopIds.Contains(shop.Id) && eligibleShopIds.Contains(shop.Id)))
            .ToList();

        return ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            DateTime.UtcNow,
            scheduleRows,
            settings.DefaultIntervalSeconds);
    }

    private static async Task<bool> HasActiveRunningRunForShopAsync(
        IApplicationDbContext db,
        Guid shopId,
        CancellationToken ct)
    {
        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        return await db.ListingAutoPublishRuns.AsNoTracking()
            .AnyAsync(
                r => r.ShopId == shopId
                     && r.Status == ListingAutoPublishRunStatuses.Running
                     && r.StartedAt >= staleRunCutoff,
                ct);
    }

    /// <summary>One query: shops that currently have a non-stale Running auto-publish run.</summary>
    private static async Task<HashSet<Guid>> LoadShopIdsWithActiveRunningRunAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> shopIds,
        CancellationToken ct)
    {
        if (shopIds.Count == 0)
        {
            return [];
        }

        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var rows = await db.ListingAutoPublishRuns.AsNoTracking()
            .Where(r => shopIds.Contains(r.ShopId)
                        && r.Status == ListingAutoPublishRunStatuses.Running
                        && r.StartedAt >= staleRunCutoff)
            .Select(r => r.ShopId)
            .Distinct()
            .ToListAsync(ct);
        return rows.ToHashSet();
    }

    /// <summary>
    /// Batched eligibility: shops that have at least one draft matching <see cref="PickListingIdAsync"/>
    /// filters (except shop-level running — caller excludes those). Grouped by min-draft-age.
    /// </summary>
    private static async Task<HashSet<Guid>> LoadShopIdsWithEligibleDraftAsync(
        IApplicationDbContext db,
        IReadOnlyList<ShopRow> shops,
        int defaultMinDraftAgeMinutes,
        CancellationToken ct)
    {
        if (shops.Count == 0)
        {
            return [];
        }

        var result = new HashSet<Guid>();
        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var inFlightLower = PublishStatuses.InFlightLowercase;
        var failedLower = PublishStatuses.FailedLowercase;

        foreach (var ageGroup in shops.GroupBy(s =>
                     ListingAutoPublishSettingKeys.ResolveMinDraftAgeMinutes(
                         s.MinDraftAgeMinutes,
                         defaultMinDraftAgeMinutes)))
        {
            var ids = ageGroup.Select(s => s.Id).ToList();
            var minDraftAgeMinutes = ageGroup.Key;
            var cutoff = minDraftAgeMinutes > 0
                ? DateTime.UtcNow - TimeSpan.FromMinutes(minDraftAgeMinutes)
                : (DateTime?)null;

            var found = await db.Listings.AsNoTracking()
                .Where(l => l.ShopId != null && ids.Contains(l.ShopId.Value)
                            && l.PageType == ListingPageTypes.Draft
                            && !l.AutoPublishSkipped
                            && (l.PublishStatus == null
                                || (!inFlightLower.Contains(l.PublishStatus.ToLower())
                                    && l.PublishStatus.ToLower() != failedLower)))
                .Where(l => cutoff == null || l.CreatedAt <= cutoff)
                .Where(l => !db.ListingAutoPublishRuns.Any(r =>
                    r.ListingId == l.Id
                    && r.Status == ListingAutoPublishRunStatuses.Running
                    && r.StartedAt >= staleRunCutoff))
                .Where(l => l.SupplierProductInfoId != null
                            && db.SupplierProductInfos.Any(spi =>
                                spi.Id == l.SupplierProductInfoId
                                && spi.SizeChartImageUrl != null
                                && spi.SizeChartImageUrl != ""))
                .Select(l => l.ShopId!.Value)
                .Distinct()
                .ToListAsync(ct);

            foreach (var id in found)
            {
                result.Add(id);
            }
        }

        return result;
    }

    private async Task<int> DispatchReadyShopsAsync(AutoPublishJobSettings settings, CancellationToken stoppingToken)
    {
        if (_concurrencyGate.AvailableSlots <= 0)
        {
            return 0;
        }

        List<ShopRow> shops;
        using (var listScope = _scopeFactory.CreateScope())
        {
            var listDb = listScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            shops = await LoadEligibleShopsAsync(listDb, stoppingToken);
        }

        if (shops.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var dueShops = shops.Where(s => IsIntervalElapsed(s, settings.DefaultIntervalSeconds, now)).ToList();
        var ready = new List<ShopRow>();

        if (dueShops.Count > 0)
        {
            using var scanScope = _scopeFactory.CreateScope();
            var db = scanScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var dueIds = dueShops.Select(s => s.Id).ToList();
            var runningShopIds = await LoadShopIdsWithActiveRunningRunAsync(db, dueIds, stoppingToken);
            var candidates = dueShops.Where(s => !runningShopIds.Contains(s.Id)).ToList();
            var eligibleShopIds = await LoadShopIdsWithEligibleDraftAsync(
                db,
                candidates,
                settings.DefaultMinDraftAgeMinutes,
                stoppingToken);

            ready.AddRange(dueShops.Where(s =>
                !runningShopIds.Contains(s.Id) && eligibleShopIds.Contains(s.Id)));
        }

        if (ready.Count == 0)
        {
            return 0;
        }

        ready.Sort(CompareReadyShopOrder);

        var dispatched = 0;
        foreach (var shop in ready)
        {
            if (_concurrencyGate.AvailableSlots <= 0)
            {
                break;
            }

            if (await TryDispatchShopAsync(shop, settings, stoppingToken))
            {
                dispatched++;
            }
        }

        if (dispatched > 0)
        {
            _logger.LogInformation(
                "Auto publish dispatched {Dispatched} shop(s) (ready={Ready}, inFlight={InFlight}, capacity={Capacity})",
                dispatched,
                ready.Count,
                _concurrencyGate.InFlight,
                _concurrencyGate.Capacity);
        }

        return dispatched;
    }

    private async ValueTask<bool> TryDispatchShopAsync(
        ShopRow shop,
        AutoPublishJobSettings settings,
        CancellationToken ct)
    {
        if (!_shopLease.TryBegin(shop.Id, out var handle))
        {
            return false;
        }

        var gateAcquired = false;
        var handleTransferred = false;
        try
        {
            if (!_concurrencyGate.TryAcquire(ct))
            {
                return false;
            }

            gateAcquired = true;

            using var workerScope = _scopeFactory.CreateScope();
            var db = workerScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var interval = shop.IntervalSeconds ?? settings.DefaultIntervalSeconds;
            var shopSnapshot = await db.Shops.AsNoTracking()
                .Where(s => s.Id == shop.Id)
                .Select(s => new { s.LastAutoPublishAt })
                .FirstAsync(ct);

            if (shopSnapshot.LastAutoPublishAt.HasValue
                && DateTime.UtcNow - shopSnapshot.LastAutoPublishAt.Value < TimeSpan.FromSeconds(interval))
            {
                return false;
            }

            if (await HasActiveRunningRunForShopAsync(db, shop.Id, ct))
            {
                return false;
            }

            var listingId = await PickListingIdAsync(
                db,
                shop.Id,
                ListingAutoPublishSettingKeys.ResolveMinDraftAgeMinutes(
                    shop.MinDraftAgeMinutes,
                    settings.DefaultMinDraftAgeMinutes),
                ct);
            if (listingId is null)
            {
                return false;
            }

            var previousLastAutoPublishAt = shopSnapshot.LastAutoPublishAt;
            var shopEntity = await db.Shops.FirstAsync(s => s.Id == shop.Id, ct);
            shopEntity.LastAutoPublishAt = DateTime.UtcNow;
            shopEntity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            handleTransferred = true;
            gateAcquired = false;
            _ = ExecutePipelineAsync(
                shop.Id,
                listingId.Value,
                previousLastAutoPublishAt,
                handle!,
                ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto publish dispatch failed for {ShopId}", shop.Id);
            return false;
        }
        finally
        {
            if (gateAcquired)
            {
                _concurrencyGate.Release();
            }

            if (!handleTransferred)
            {
                handle?.Dispose();
            }
        }
    }

    private async Task ExecutePipelineAsync(
        Guid shopId,
        Guid listingId,
        DateTime? previousLastAutoPublishAt,
        IDisposable shopLeaseHandle,
        CancellationToken stoppingToken)
    {
        var pipelineStartedAt = DateTime.UtcNow;
        var runCreated = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IListingAutoPublishOrchestrator>();
            await orchestrator.RunAsync(listingId, stoppingToken);
            runCreated = await HasRunSinceAsync(
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                listingId,
                pipelineStartedAt,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown or round cancelled
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Auto publish pipeline failed for shop {ShopId} listing {ListingId}",
                shopId,
                listingId);

            if (!runCreated)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                runCreated = await HasRunSinceAsync(db, listingId, pipelineStartedAt, stoppingToken);
            }

            if (!runCreated)
            {
                await RollbackLastAutoPublishAsync(shopId, previousLastAutoPublishAt, stoppingToken);
            }
        }
        finally
        {
            _concurrencyGate.Release();
            shopLeaseHandle.Dispose();
            _dispatchTrigger.Signal();
        }
    }

    private static async Task<bool> HasRunSinceAsync(
        IApplicationDbContext db,
        Guid listingId,
        DateTime pipelineStartedAt,
        CancellationToken ct)
    {
        var cutoff = pipelineStartedAt.AddSeconds(-1);
        return await db.ListingAutoPublishRuns.AsNoTracking()
            .AnyAsync(
                r => r.ListingId == listingId && r.StartedAt >= cutoff,
                ct);
    }

    private async Task RollbackLastAutoPublishAsync(
        Guid shopId,
        DateTime? previousLastAutoPublishAt,
        CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
            if (shop is null)
            {
                return;
            }

            shop.LastAutoPublishAt = previousLastAutoPublishAt;
            shop.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to rollback LastAutoPublishAt for shop {ShopId} after early pipeline failure",
                shopId);
        }
    }

    private static async Task<List<ShopRow>> LoadEligibleShopsAsync(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var utcNow = DateTime.UtcNow;
        var candidates = await db.Shops.AsNoTracking()
            .Where(s => s.Status == ShopStatuses.Active
                        && s.AutoPublishEnabled
                        && s.FourSellerShopId != null
                        && s.FourSellerShopId != "")
            .Where(s => s.AutoPublishBlockedUntil == null || s.AutoPublishBlockedUntil <= utcNow)
            .Select(s => new
            {
                s.Id,
                s.AutoPublishIntervalSeconds,
                s.AutoPublishMinDraftAgeMinutes,
                s.LastAutoPublishAt,
                s.PublishLimitPublished,
                s.PublishLimitTotal
            })
            .ToListAsync(ct);

        return candidates
            .Where(s => !ShopListingSlotRules.IsFull(
                s.PublishLimitPublished ?? 0,
                s.PublishLimitTotal))
            .Select(s => new ShopRow(
                s.Id,
                s.AutoPublishIntervalSeconds,
                s.AutoPublishMinDraftAgeMinutes,
                s.LastAutoPublishAt))
            .ToList();
    }

    private static bool IsIntervalElapsed(ShopRow shop, int defaultIntervalSeconds, DateTime utcNow)
    {
        var interval = shop.IntervalSeconds ?? defaultIntervalSeconds;
        if (!shop.LastAutoPublishAt.HasValue)
        {
            return true;
        }

        return shop.LastAutoPublishAt.Value.AddSeconds(interval) <= utcNow;
    }

    private static int CompareReadyShopOrder(ShopRow a, ShopRow b)
    {
        if (!a.LastAutoPublishAt.HasValue && b.LastAutoPublishAt.HasValue)
        {
            return -1;
        }

        if (a.LastAutoPublishAt.HasValue && !b.LastAutoPublishAt.HasValue)
        {
            return 1;
        }

        if (!a.LastAutoPublishAt.HasValue && !b.LastAutoPublishAt.HasValue)
        {
            return a.Id.CompareTo(b.Id);
        }

        var byLast = a.LastAutoPublishAt!.Value.CompareTo(b.LastAutoPublishAt!.Value);
        return byLast != 0 ? byLast : a.Id.CompareTo(b.Id);
    }

    private static async Task<Guid?> PickListingIdAsync(
        IApplicationDbContext db,
        Guid shopId,
        int minDraftAgeMinutes,
        CancellationToken ct)
    {
        var cutoff = minDraftAgeMinutes > 0
            ? DateTime.UtcNow - TimeSpan.FromMinutes(minDraftAgeMinutes)
            : (DateTime?)null;

        var staleRunCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var inFlightLower = PublishStatuses.InFlightLowercase;
        var failedLower = PublishStatuses.FailedLowercase;

        var id = await db.Listings.AsNoTracking()
            .Where(l => l.ShopId == shopId
                        && l.PageType == ListingPageTypes.Draft
                        && !l.AutoPublishSkipped
                        && (l.PublishStatus == null
                            || (!inFlightLower.Contains(l.PublishStatus.ToLower())
                                && l.PublishStatus.ToLower() != failedLower)))
            .Where(l => cutoff == null || l.CreatedAt <= cutoff)
            .Where(l => !db.ListingAutoPublishRuns.Any(r =>
                r.ShopId == shopId
                && r.Status == ListingAutoPublishRunStatuses.Running
                && r.StartedAt >= staleRunCutoff))
            .Where(l => !db.ListingAutoPublishRuns.Any(r =>
                r.ListingId == l.Id
                && r.Status == ListingAutoPublishRunStatuses.Running
                && r.StartedAt >= staleRunCutoff))
            .Where(l => l.SupplierProductInfoId != null
                        && db.SupplierProductInfos.Any(spi =>
                            spi.Id == l.SupplierProductInfoId
                            && spi.SizeChartImageUrl != null
                            && spi.SizeChartImageUrl != ""))
            .OrderBy(l => l.CreatedAt)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(ct);

        return id == Guid.Empty ? null : id;
    }

    private async Task<AutoPublishJobSettings> ReadSettingsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
        var values = await settings.GetValuesAsync(
            [
                ListingAutoPublishSettingKeys.Enabled,
                ListingAutoPublishSettingKeys.MaxConcurrentPublishes,
                ListingAutoPublishSettingKeys.DefaultIntervalSeconds,
                ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes
            ],
            ct);

        return new AutoPublishJobSettings(
            Enabled: ParseBool(values.GetValueOrDefault(ListingAutoPublishSettingKeys.Enabled)),
            MaxConcurrentPublishes: ParseInt(values.GetValueOrDefault(ListingAutoPublishSettingKeys.MaxConcurrentPublishes), 10, 1),
            DefaultIntervalSeconds: ParseInt(values.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultIntervalSeconds), 120, 1),
            DefaultMinDraftAgeMinutes: ParseInt(
                values.GetValueOrDefault(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes),
                int.Parse(ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutesValue),
                ListingAutoPublishSettingKeys.MinDraftAgeMinutes));
    }

    private static bool ParseBool(string? raw) =>
        string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

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
        int? IntervalSeconds,
        int? MinDraftAgeMinutes,
        DateTime? LastAutoPublishAt);

    private sealed record AutoPublishJobSettings(
        bool Enabled,
        int MaxConcurrentPublishes,
        int DefaultIntervalSeconds,
        int DefaultMinDraftAgeMinutes);
}
