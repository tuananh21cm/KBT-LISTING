using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure.Integrations.PrintPackShip;

/// <summary>
/// Hosted loop: after each USPS auto-scan cycle finishes, wait <c>interval_minutes</c> then scan again.
/// Outside the US business-hours window it sleeps until the window reopens instead.
/// </summary>
public sealed class UspsScanAutoPushBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMinutes(1);

    /// <summary>Longest nap while the window is closed, so Settings edits take effect quickly.</summary>
    private static readonly TimeSpan ClosedWindowPollCap = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UspsScanAutoPushBackgroundService> _logger;

    /// <summary>Creates the background service.</summary>
    public UspsScanAutoPushBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<UspsScanAutoPushBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
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

        _logger.LogInformation(
            "USPS auto scan loop started (interval counted after each cycle completes)");

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(UspsScanAutoSettingKeys.DefaultIntervalMinutes);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
                var config = await UspsScanAutoSettingsReader.ReadAsync(settings, stoppingToken);

                interval = TimeSpan.FromMinutes(config.IntervalMinutes);

                if (config.AutoEnabled)
                {
                    var nowUtc = DateTime.UtcNow;
                    if (UspsScanWindow.IsOpen(nowUtc, config.Window))
                    {
                        var orch = scope.ServiceProvider.GetRequiredService<IUspsScanAutoPushOrchestrator>();
                        await orch.RunOnceAsync(config, stoppingToken);
                    }
                    else
                    {
                        interval = ResolveClosedWindowDelay(nowUtc, config);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USPS auto scan tick failed unexpectedly");
                interval = ErrorBackoff;
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Nap length while the US window is closed: until it reopens, capped so Settings edits
    /// (and a misconfigured always-closed window) are re-read within <see cref="ClosedWindowPollCap"/>.
    /// </summary>
    private TimeSpan ResolveClosedWindowDelay(DateTime nowUtc, UspsScanAutoSettings config)
    {
        var nextOpenUtc = UspsScanWindow.NextOpenUtc(nowUtc, config.Window);
        if (nextOpenUtc is null)
        {
            _logger.LogWarning(
                "USPS auto scan: window never opens with current settings (check clocks and cutoff margin)");
            return ClosedWindowPollCap;
        }

        var untilOpen = nextOpenUtc.Value - nowUtc;
        if (untilOpen <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(30);
        }

        _logger.LogDebug(
            "USPS auto scan: window closed, next open {NextOpenUtc:u} (sleeping {Minutes:F0}m)",
            nextOpenUtc.Value,
            Math.Min(untilOpen.TotalMinutes, ClosedWindowPollCap.TotalMinutes));

        return untilOpen < ClosedWindowPollCap ? untilOpen : ClosedWindowPollCap;
    }
}
