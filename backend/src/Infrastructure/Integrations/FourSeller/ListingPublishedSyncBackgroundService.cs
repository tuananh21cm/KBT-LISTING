using Application.DTOs.Listing;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Published status loop: refreshes site status across all published buckets inside a create-date
/// lookback window. Enabled / interval / lookback are read from Settings each loop; the wait
/// starts after the run finishes. Holding <see cref="ListingSyncLoopGate"/> for the whole pass
/// keeps the draft loop from colliding on seller leases.
/// </summary>
public sealed class ListingPublishedSyncBackgroundService : ResilientBackgroundService
{
    private const int DisabledPollMinutes = 1;

    /// <summary>
    /// Cap per published status bucket. Large enough to cover a 10-day create window for typical
    /// sellers; page size still clamps to 100 so paging works normally. Explicit (not null) so
    /// SyncCoreAsync does not fall back to its status-only default of 200.
    /// </summary>
    private const int PublishedLookbackMaxRecordsPerStatus = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ListingSyncLoopGate _loopGate;
    private readonly ILogger<ListingPublishedSyncBackgroundService> _logger;

    /// <summary>Initializes the published listing sync background service.</summary>
    public ListingPublishedSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ListingSyncLoopGate loopGate,
        ILogger<ListingPublishedSyncBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _loopGate = loopGate;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        // Stagger slightly after the draft loop's 30s startup delay.
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = await ReadSettingsAsync(stoppingToken);
            var delayMinutes = DisabledPollMinutes;

            if (config.Enabled)
            {
                try
                {
                    if (_loopGate.IsHeld)
                    {
                        _logger.LogInformation(
                            "Published listing sync waiting — draft loop currently holds the gate");
                    }

                    await using (await _loopGate.AcquireAsync(stoppingToken))
                    {
                        await RunSyncAsync(config.LookbackDays, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Published listing sync background job failed unexpectedly");
                }

                delayMinutes = config.IntervalMinutes;
            }
            else
            {
                _logger.LogDebug("Published listing sync background job is disabled (Settings)");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunSyncAsync(int lookbackDays, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IListingSyncService>();

        var (dateFromUtc, dateToUtc) = FourSellerOrderDateTime.GetVietnamLookbackUtcRange(
            lookbackDays,
            ListingSyncSettingKeys.MaxPublishedLookbackDays);
        var (vnFrom, vnTo) = FourSellerOrderDateTime.ToVietnamLocalRange(dateFromUtc, dateToUtc);
        var (pacificFrom, pacificTo) = FourSellerOrderDateTime.ToPacificLocalRange(dateFromUtc, dateToUtc);

        var publishedStatuses = new List<string>(ListingSiteStatuses.PublishedStatuses);

        _logger.LogInformation(
            "Starting background 4Seller listing sync (published status, lookbackDays={LookbackDays}, maxPerStatus={Max}, statuses={Statuses}, business VN {VnFrom:yyyy-MM-dd HH:mm:ss} .. {VnTo:yyyy-MM-dd HH:mm:ss}, 4Seller Pacific {PacificFrom:yyyy-MM-dd HH:mm:ss} .. {PacificTo:yyyy-MM-dd HH:mm:ss})",
            lookbackDays,
            PublishedLookbackMaxRecordsPerStatus,
            string.Join(',', publishedStatuses),
            vnFrom,
            vnTo,
            pacificFrom,
            pacificTo);

        var result = await syncService.SyncAsync(
            new ListingSyncRequest
            {
                Statuses = publishedStatuses,
                DateFrom = dateFromUtc,
                DateTo = dateToUtc,
                MaxRecordsPerSeller = PublishedLookbackMaxRecordsPerStatus,
                SyncScope = ListingSyncScopes.Status,
                SyncSource = ListingSyncSources.Background
            },
            progress: null,
            cancellationToken);

        _logger.LogInformation(
            "Background published listing sync finished: sellers={Synced}, inserted={Inserted}, updated={Updated}, failed={Failed}",
            result.SyncedSellers,
            result.Inserted,
            result.Updated,
            result.FailedSellers.Count);

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "Published listing sync seller failure {OwnerId} ({OwnerName}): {Message}",
                failure.OwnerUserId,
                failure.OwnerName,
                failure.Message);
        }
    }

    private async Task<ListingSyncScheduleConfig.PublishedConfig> ReadSettingsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();

        var values = await settings.GetValuesAsync(
            [
                ListingSyncSettingKeys.PublishedEnabled,
                ListingSyncSettingKeys.PublishedIntervalMinutes,
                ListingSyncSettingKeys.PublishedLookbackDays
            ],
            cancellationToken);

        return ListingSyncScheduleConfig.ParsePublished(values);
    }
}
