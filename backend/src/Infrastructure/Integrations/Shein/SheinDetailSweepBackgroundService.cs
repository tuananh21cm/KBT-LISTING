using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Shein;

/// <summary>
/// Ticks the backend SHEIN detail sweep: full-cycle snapshot of eligible billnos →
/// <c>get_order_info</c> in <c>max_billnos</c> batches → Mark Ship when carrier tracking appears.
///
/// The tick is short so a finished cycle notices a lowered rest interval quickly; overlapping
/// cycles are blocked by the sweep service's single-flight gate.
/// </summary>
public class SheinDetailSweepBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    /// <summary>Let migrations + seeding finish, and stay clear of the realtime sync's startup.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(40);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SheinDetailSweepBackgroundService> _logger;

    /// <summary>Initializes the SHEIN detail sweep loop.</summary>
    public SheinDetailSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SheinDetailSweepBackgroundService> logger)
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
                var sweep = scope.ServiceProvider.GetRequiredService<ISheinOrderDetailSweepService>();
                await sweep.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SHEIN detail sweep tick failed unexpectedly");
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
