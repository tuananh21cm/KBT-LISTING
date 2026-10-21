using Application.DTOs.Order;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Runs the two after-sales loops on their own clocks: asking 4Seller to pull returns from TikTok
/// Shop, and reading those returns back into our orders.
///
/// They are deliberately not chained. 4Seller does its TikTok pull asynchronously, so a pull that
/// waited for the trigger would still read the previous state; and the pull has to keep running on
/// its own when the trigger is switched off (returns also arrive through 4Seller's own schedule).
/// </summary>
public sealed class AfterSalesSyncOrchestrator : IAfterSalesSyncOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AfterSalesSyncOrchestrator> _logger;

    /// <summary>Prevents overlapping ticks (a pull can outlive the one-minute tick).</summary>
    private int _gate;

    private DateTimeOffset? _lastTriggerFinishedUtc;
    private DateTimeOffset? _lastPullFinishedUtc;

    /// <summary>
    /// Last time the one-shot full reconcile actually reached 4Seller. Only throttles the retries of
    /// a pass that did not finish cleanly — a clean pass persists its own marker.
    /// </summary>
    private DateTimeOffset? _lastReconcileAttemptUtc;

    /// <summary>Initializes the orchestrator.</summary>
    public AfterSalesSyncOrchestrator(
        IServiceScopeFactory scopeFactory,
        ILogger<AfterSalesSyncOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0)
        {
            _logger.LogDebug("After-sales tick skipped — previous tick still running");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
            var config = await ReadSettingsAsync(settings, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var triggerDue = config.TriggerEnabled && IsDue(_lastTriggerFinishedUtc, config.TriggerIntervalMinutes, now);
            var pullDue = config.PullEnabled && IsDue(_lastPullFinishedUtc, config.PullIntervalMinutes, now);
            // The one-shot full reconcile rides the pull switch: it reads the same returns pages on
            // behalf of every seller, so a deployment with the pull off must not start calling out.
            // It marks itself done only after a clean sweep, so one seller with a dead cookie would
            // otherwise re-scan every seller's whole return history once a minute — retries wait a
            // pull interval. A tick deferred by the write gate does not count as an attempt.
            var reconcileDue = config.PullEnabled
                && !config.FullReconcileDone
                && IsDue(_lastReconcileAttemptUtc, config.PullIntervalMinutes, now);
            if (!triggerDue && !pullDue && !reconcileDue)
            {
                return;
            }

            var service = scope.ServiceProvider.GetRequiredService<IOrderAfterSalesSyncService>();

            if (reconcileDue)
            {
                await RunFullReconcileAsync(service, cancellationToken);
            }

            if (triggerDue)
            {
                await RunTriggerAsync(service, cancellationToken);
            }

            if (pullDue)
            {
                await RunPullAsync(service, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "After-sales sync tick failed");
        }
        finally
        {
            Interlocked.Exchange(ref _gate, 0);
        }
    }

    /// <summary>
    /// One-shot pass making local <c>REFUND</c> mirror 4Seller's returns list: pins <c>RefundedAt</c>
    /// to each case's create time, and strips <c>REFUND</c> off orders no case mentions. It walks a
    /// seller's whole return history, so it deliberately runs before the ordinary pull on this tick:
    /// both take the same write gate, and the pull would otherwise hold it for the reconcile's first
    /// several attempts.
    /// </summary>
    private async Task RunFullReconcileAsync(
        IOrderAfterSalesSyncService service,
        CancellationToken cancellationToken)
    {
        OrderAfterSalesSyncResultDto result;
        try
        {
            result = await service.ReconcileAfterSalesFullScanAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The marker stays unset, so a later tick retries. Re-reading a seller already
            // reconciled writes nothing, which is what makes the retry safe.
            _lastReconcileAttemptUtc = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "After-sales reconcile failed before completion");
            return;
        }

        if (result.Skipped)
        {
            // Either already marked done (the next tick reads the marker and stops asking) or the
            // write gate is held — retry on the next tick rather than after a whole interval.
            return;
        }

        _lastReconcileAttemptUtc = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "After-sales reconcile finished: sellers={Sellers}, returns={Returns}, orders={Orders}, "
                + "refunded={Refunded}, reverted={Reverted}, withoutCaseReleased={Released}, "
                + "withoutCaseStuck={Stuck}, casesWithoutClock={NoClock}, unmatched={Unmatched}, failed={Failed}",
            result.SyncedSellers,
            result.ReturnsRead,
            result.OrdersUpdated,
            result.Refunded,
            result.RefundReverted,
            result.RefundWithoutCaseReleased,
            result.RefundWithoutCaseStuck,
            result.CasesWithoutClock,
            result.UnmatchedReturns,
            result.FailedSellers.Count);
    }

    private async Task RunTriggerAsync(IOrderAfterSalesSyncService service, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.TriggerPlatformPullAsync(cancellationToken: cancellationToken);
            _lastTriggerFinishedUtc = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "After-sales trigger finished: sellers={Sellers}, failed={Failed}",
                result.SyncedSellers,
                result.FailedSellers.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave lastFinished alone so the next tick retries instead of waiting a full interval.
            _logger.LogError(ex, "After-sales trigger failed before completion");
        }
    }

    private async Task RunPullAsync(IOrderAfterSalesSyncService service, CancellationToken cancellationToken)
    {
        OrderAfterSalesSyncResultDto result;
        try
        {
            result = await service.PullAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "After-sales pull failed before completion");
            return;
        }

        if (result.Skipped)
        {
            // Order sync holds the write gate; retry on the next tick rather than in an interval.
            _logger.LogInformation("After-sales pull deferred — another 4Seller sync is running");
            return;
        }

        _lastPullFinishedUtc = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "After-sales pull finished: sellers={Sellers}, returns={Returns}, orders={Orders}, refunded={Refunded}, reverted={Reverted}, unmatched={Unmatched}, notified={Notified}, failed={Failed}",
            result.SyncedSellers,
            result.ReturnsRead,
            result.OrdersUpdated,
            result.Refunded,
            result.RefundReverted,
            result.UnmatchedReturns,
            result.Notified,
            result.FailedSellers.Count);

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "After-sales seller failure {OwnerId} ({OwnerName}): {Message}",
                failure.OwnerUserId,
                failure.OwnerName,
                failure.Message);
        }
    }

    private static bool IsDue(DateTimeOffset? lastFinishedUtc, int intervalMinutes, DateTimeOffset now) =>
        lastFinishedUtc is not { } last || now - last >= TimeSpan.FromMinutes(intervalMinutes);

    private static async Task<AfterSalesSyncConfig> ReadSettingsAsync(
        ISettingProvider settings,
        CancellationToken cancellationToken)
    {
        var values = await settings.GetValuesAsync(
            [
                AfterSalesSyncSettingKeys.TriggerEnabled,
                AfterSalesSyncSettingKeys.TriggerIntervalMinutes,
                AfterSalesSyncSettingKeys.PullEnabled,
                AfterSalesSyncSettingKeys.PullIntervalMinutes,
                AfterSalesSyncSettingKeys.ReturnStatusBackfill
            ],
            cancellationToken);

        values.TryGetValue(AfterSalesSyncSettingKeys.TriggerEnabled, out var triggerEnabled);
        values.TryGetValue(AfterSalesSyncSettingKeys.TriggerIntervalMinutes, out var triggerInterval);
        values.TryGetValue(AfterSalesSyncSettingKeys.PullEnabled, out var pullEnabled);
        values.TryGetValue(AfterSalesSyncSettingKeys.PullIntervalMinutes, out var pullInterval);
        values.TryGetValue(AfterSalesSyncSettingKeys.ReturnStatusBackfill, out var reconcileMarker);

        return new AfterSalesSyncConfig(
            // Unlike order sync, both loops default to off: they hit 4Seller on behalf of every
            // seller, so an unconfigured deployment must not start calling out on its own.
            IsTruthy(triggerEnabled),
            ParseInterval(triggerInterval, AfterSalesSyncSettingKeys.DefaultTriggerIntervalMinutes),
            IsTruthy(pullEnabled),
            ParseInterval(pullInterval, AfterSalesSyncSettingKeys.DefaultPullIntervalMinutes),
            // Read here rather than inside the service so a settled marker costs no extra query on
            // the ticks that follow: these five keys are already fetched in one round trip.
            string.Equals(reconcileMarker?.Trim(), "done", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || value?.Trim() == "1";

    private static int ParseInterval(string? raw, int fallback) =>
        int.TryParse(raw, out var parsed)
            ? Math.Clamp(
                parsed,
                AfterSalesSyncSettingKeys.MinIntervalMinutes,
                AfterSalesSyncSettingKeys.MaxIntervalMinutes)
            : fallback;

    private readonly record struct AfterSalesSyncConfig(
        bool TriggerEnabled,
        int TriggerIntervalMinutes,
        bool PullEnabled,
        int PullIntervalMinutes,
        bool FullReconcileDone);
}
