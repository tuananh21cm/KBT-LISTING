using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Telegram;

/// <summary>
/// Polls once per minute and sends Telegram daily order + seller + fulfil summaries when due
/// (including post-midnight catch-up when a late schedule like 23:59 was missed), plus
/// the purchased-N-hours-without-carrier-tracking digest at the fulfil-summary clock, plus
/// pending new-order retries (shop resolved late / prior send failure).
/// </summary>
public class OrderDailySummaryBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderDailySummaryBackgroundService> _logger;

    /// <summary>
    /// Initializes the daily summary job.
    /// </summary>
    public OrderDailySummaryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderDailySummaryBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
                await notifier.TrySendDailySummaryAsync(stoppingToken);
                await notifier.TrySendSellerSummaryAsync(stoppingToken);
                await notifier.TrySendFulfilSummaryAsync(stoppingToken);
                await notifier.TryNotifyStaleNoTrackingAsync(stoppingToken);
                await notifier.TrySendPendingNewOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram daily summary background job failed unexpectedly");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
