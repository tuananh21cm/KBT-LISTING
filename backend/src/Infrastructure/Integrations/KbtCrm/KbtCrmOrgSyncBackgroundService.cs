using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.KbtCrm;

/// <summary>
/// Pulls teams/users/shops from KBT CRM on the interval in Settings
/// (<c>kbtcrm.sync.enabled</c> / <c>kbtcrm.sync.interval_minutes</c>). Ticks every minute so a
/// settings change takes effect without restart; the interval counts from the last finished pull.
/// </summary>
public sealed class KbtCrmOrgSyncBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KbtCrmOrgSyncBackgroundService> _logger;
    private DateTime _lastRunUtc = DateTime.MinValue;

    public KbtCrmOrgSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<KbtCrmOrgSyncBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CRM org sync tick failed");
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
        var values = await settings.GetValuesAsync(
            [KbtCrmSyncSettingKeys.Enabled, KbtCrmSyncSettingKeys.IntervalMinutes], cancellationToken);

        var enabled = string.Equals(values.GetValueOrDefault(KbtCrmSyncSettingKeys.Enabled), "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return;
        }

        var interval = int.TryParse(values.GetValueOrDefault(KbtCrmSyncSettingKeys.IntervalMinutes), out var parsed)
            ? Math.Clamp(parsed, KbtCrmSyncSettingKeys.MinIntervalMinutes, KbtCrmSyncSettingKeys.MaxIntervalMinutes)
            : KbtCrmSyncSettingKeys.DefaultIntervalMinutes;
        if (DateTime.UtcNow - _lastRunUtc < TimeSpan.FromMinutes(interval))
        {
            return;
        }

        var sync = scope.ServiceProvider.GetRequiredService<IKbtCrmOrgSyncService>();
        // Stamp before running: a CRM outage must not turn into a retry every minute.
        _lastRunUtc = DateTime.UtcNow;
        await sync.RunAsync("background", cancellationToken);
    }
}
