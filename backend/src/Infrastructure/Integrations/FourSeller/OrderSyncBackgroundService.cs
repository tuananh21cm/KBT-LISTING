using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Periodically syncs new / open / recently canceled 4Seller orders (light loop).
/// Enabled / interval / lookback / batch size are read from Settings DB each tick.
/// Ticks every minute and delegates to <see cref="IOrderSyncOrchestrator"/> so IIS
/// keep-alive /health can also drive the same sync path.
/// </summary>
public class OrderSyncBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderSyncBackgroundService> _logger;

    /// <summary>
    /// Initializes the background order sync.
    /// </summary>
    public OrderSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderSyncBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        // Delay first run slightly so app can finish startup / migrate / seed.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
            "Order sync background loop started (tick every {Minutes} min; interval from Settings)",
            TickInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrderSyncOrchestrator>();
                await orchestrator.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order sync background tick failed unexpectedly");
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
