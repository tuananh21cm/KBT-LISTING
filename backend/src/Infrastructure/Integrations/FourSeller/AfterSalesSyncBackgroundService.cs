using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Ticks every minute and lets <see cref="IAfterSalesSyncOrchestrator"/> decide whether the
/// TikTok trigger or the 4Seller pull is due. Enabled flags and intervals come from Settings DB,
/// so admins change them without a restart.
/// </summary>
public class AfterSalesSyncBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Longer than the order sync delay so the first tick lands after that sync has taken the
    /// write gate — the pull would only be deferred otherwise.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AfterSalesSyncBackgroundService> _logger;

    /// <summary>Initializes the background after-sales sync.</summary>
    public AfterSalesSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AfterSalesSyncBackgroundService> logger)
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
            "After-sales sync background loop started (tick every {Minutes} min; intervals from Settings)",
            TickInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IAfterSalesSyncOrchestrator>();
                await orchestrator.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "After-sales sync background tick failed unexpectedly");
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
