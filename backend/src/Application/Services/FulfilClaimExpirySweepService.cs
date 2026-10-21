using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Periodically: (1) auto-release claims of SignalR sessions disconnected longer than grace,
/// (2) close TTL-expired fulfil session attempts.
/// </summary>
public sealed class FulfilClaimExpirySweepService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);
    private const int ExpiryTicksPerRun = 4; // ~60s between CloseExpiredAttemptsAsync

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFulfilSessionTracker _sessionTracker;
    private readonly ILogger<FulfilClaimExpirySweepService> _logger;

    /// <summary>Initializes the sweep.</summary>
    public FulfilClaimExpirySweepService(
        IServiceScopeFactory scopeFactory,
        IFulfilSessionTracker sessionTracker,
        ILogger<FulfilClaimExpirySweepService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _sessionTracker = sessionTracker;
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

        var tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            tick++;
            try
            {
                await ReleaseOrphanedSessionsAsync(stoppingToken);

                if (tick % ExpiryTicksPerRun == 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var orders = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
                    var closed = await orders.CloseExpiredAttemptsAsync(stoppingToken);
                    if (closed > 0)
                    {
                        _logger.LogInformation(
                            "Fulfil claim sweep closed {Count} expired session attempt(s)",
                            closed);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fulfil claim expiry sweep failed");
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

    private async Task ReleaseOrphanedSessionsAsync(CancellationToken stoppingToken)
    {
        var expired = _sessionTracker.TakeExpired(
            FulfilSessionTracker.DefaultGrace,
            DateTime.UtcNow);
        if (expired.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
        foreach (var item in expired)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                var result = await orders.ReleaseSessionClaimsAsync(
                    item.SessionId,
                    item.UserId,
                    stoppingToken);
                if (result.Items.Count > 0)
                {
                    _logger.LogInformation(
                        "Auto-released {Count} claim(s) for orphaned fulfil session {SessionId}",
                        result.Items.Count,
                        item.SessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to auto-release claims for fulfil session {SessionId}",
                    item.SessionId);
            }
        }
    }
}
