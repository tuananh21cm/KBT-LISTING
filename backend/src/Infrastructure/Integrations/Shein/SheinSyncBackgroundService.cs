using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Shein;

/// <summary>
/// Ticks the server-side SHEIN sync (local mode: cookie + proxy on fulfil accounts).
/// Does nothing while <c>extension.shein_sync.header_source</c> is <c>realtime</c>.
/// </summary>
public class SheinSyncBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SheinSyncBackgroundService> _logger;

    /// <summary>Initializes the SHEIN sync background loop.</summary>
    public SheinSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SheinSyncBackgroundService> logger)
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<ISheinRealtimeSyncService>();
                await sync.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SHEIN realtime sync tick failed unexpectedly");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
