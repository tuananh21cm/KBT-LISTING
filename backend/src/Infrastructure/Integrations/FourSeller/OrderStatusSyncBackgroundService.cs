using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Periodically refreshes platform order status across all six 4Seller buckets.
/// Enabled / interval / lookback / batch size come from Settings DB each tick.
/// Startup delay is longer than the new-orders and after-sales loops so the first
/// tick does not pile onto the same write gate.
/// </summary>
public class OrderStatusSyncBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(90);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderStatusSyncBackgroundService> _logger;

    /// <summary>Initializes the background status sync.</summary>
    public OrderStatusSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderStatusSyncBackgroundService> logger)
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
            "Order status sync background loop started (tick every {Minutes} min; interval from Settings)",
            TickInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrderStatusSyncOrchestrator>();
                await orchestrator.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order status sync background tick failed unexpectedly");
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
