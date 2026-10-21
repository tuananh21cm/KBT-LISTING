using Application.DTOs.Listing;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Light draft loop: pulls newest drafts (page + batch-detail) for all sellers with cookies.
/// Enabled / interval are read from Settings each loop; the wait starts after the run finishes.
/// </summary>
public sealed class ListingDraftSyncBackgroundService : ResilientBackgroundService
{
    private const int DisabledPollMinutes = 1;

    /// <summary>Newest drafts per seller — keeps the pull light.</summary>
    private const int DraftMaxRecordsPerSeller = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ListingSyncLoopGate _loopGate;
    private readonly ILogger<ListingDraftSyncBackgroundService> _logger;

    /// <summary>Initializes the draft listing sync background service.</summary>
    public ListingDraftSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ListingSyncLoopGate loopGate,
        ILogger<ListingDraftSyncBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _loopGate = loopGate;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        // Delay first run slightly so app can finish startup / migrate / seed.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
                            "Draft listing sync waiting — published loop currently holds the gate");
                    }

                    await using (await _loopGate.AcquireAsync(stoppingToken))
                    {
                        await RunSyncAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Draft listing sync background job failed unexpectedly");
                }

                delayMinutes = config.IntervalMinutes;
            }
            else
            {
                _logger.LogDebug("Draft listing sync background job is disabled (Settings)");
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

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IListingSyncService>();

        _logger.LogInformation(
            "Starting background 4Seller listing sync (draft full, maxRecords={Max})",
            DraftMaxRecordsPerSeller);

        var result = await syncService.SyncAsync(
            new ListingSyncRequest
            {
                Statuses = [ListingSiteStatuses.Draft],
                MaxRecordsPerSeller = DraftMaxRecordsPerSeller,
                SyncScope = ListingSyncScopes.Full,
                SyncSource = ListingSyncSources.Background
            },
            progress: null,
            cancellationToken);

        _logger.LogInformation(
            "Background draft listing sync finished: sellers={Synced}, inserted={Inserted}, updated={Updated}, failed={Failed}",
            result.SyncedSellers,
            result.Inserted,
            result.Updated,
            result.FailedSellers.Count);

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "Draft listing sync seller failure {OwnerId} ({OwnerName}): {Message}",
                failure.OwnerUserId,
                failure.OwnerName,
                failure.Message);
        }
    }

    private async Task<ListingSyncScheduleConfig.DraftConfig> ReadSettingsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();

        var values = await settings.GetValuesAsync(
            [
                ListingSyncSettingKeys.DraftEnabled,
                ListingSyncSettingKeys.DraftIntervalMinutes
            ],
            cancellationToken);

        return ListingSyncScheduleConfig.ParseDraft(values);
    }
}
