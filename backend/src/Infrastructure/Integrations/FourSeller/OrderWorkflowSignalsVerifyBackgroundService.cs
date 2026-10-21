using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Hourly guard for the denormalized workflow columns on <c>Orders</c>.
///
/// <c>WorkflowTab</c> and the signal flags are what the list filter and the badge counts read, so a
/// stale value silently hides orders from an ops queue. The <c>SaveChangesAsync</c> writer covers
/// every path that goes through EF, but not raw SQL or <c>ExecuteUpdate</c>; this loop recomputes a
/// window of recent orders, logs a warning when anything disagrees, and repairs it.
/// </summary>
public class OrderWorkflowSignalsVerifyBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Recent orders only: an old order whose source fields no longer change cannot drift, and a
    /// full-table scan every hour would cost more than it protects.
    /// </summary>
    private const int WindowSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderWorkflowSignalsVerifyBackgroundService> _logger;

    /// <summary>Khởi tạo vòng lặp kiểm tra.</summary>
    public OrderWorkflowSignalsVerifyBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderWorkflowSignalsVerifyBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        // Let migrate + seed (including the signals backfill) finish first.
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
                var verifier = scope.ServiceProvider.GetRequiredService<IOrderWorkflowSignalsVerifier>();
                await verifier.VerifyAsync(WindowSize, repair: true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order workflow signal verify tick failed unexpectedly");
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
