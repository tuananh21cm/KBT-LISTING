using Application.DTOs.Order;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Heavy platform-status auto-sync runner. Pulls all six 4Seller buckets inside a long
/// create-date lookback (default 30 days) on an hours-scale clock (default 12h). Holds
/// <see cref="FourSellerSyncGate"/> for the whole pass so the light new-orders loop waits.
/// </summary>
public sealed class OrderStatusSyncOrchestrator : IOrderStatusSyncOrchestrator
{
    /// <summary>Max time a real status sync may hold <see cref="_syncing"/> before force-clear.</summary>
    private static readonly TimeSpan SyncStuckTimeout = TimeSpan.FromMinutes(180);

    /// <summary>Max time the gate may be held without an active sync.</summary>
    private static readonly TimeSpan GateStaleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Warn when a pass runs longer than this — light new-orders loop is blocked meanwhile.</summary>
    private static readonly TimeSpan LongPassWarning = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOrderSyncOrchestrator _newOrdersOrchestrator;
    private readonly ILogger<OrderStatusSyncOrchestrator> _logger;

    private int _gate;
    private int _syncing;
    private long _gateAcquiredTicks;
    private bool? _lastEnabled;
    private int? _lastIntervalMinutes;
    private DateTimeOffset? _lastStartedUtc;
    private DateTimeOffset? _lastFinishedUtc;
    private string? _lastSummary;

    /// <summary>Initializes the status sync orchestrator.</summary>
    public OrderStatusSyncOrchestrator(
        IServiceScopeFactory scopeFactory,
        IOrderSyncOrchestrator newOrdersOrchestrator,
        ILogger<OrderStatusSyncOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _newOrdersOrchestrator = newOrdersOrchestrator;
        _logger = logger;
    }

    /// <inheritdoc />
    public OrderSyncRuntimeStatusDto GetStatus()
    {
        TryReleaseStaleLocks();
        return BuildStatus();
    }

    /// <inheritdoc />
    public async Task<OrderSyncRuntimeStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        TryReleaseStaleLocks();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
            var config = await ReadSettingsAsync(settings, cancellationToken);
            _lastEnabled = config.Enabled;
            _lastIntervalMinutes = config.IntervalMinutes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh status sync settings for /health status");
        }

        return BuildStatus();
    }

    /// <inheritdoc />
    public void ScheduleIfDue()
    {
        _ = PulseFireAndForgetAsync();
    }

    /// <inheritdoc />
    public async Task RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        TryReleaseStaleLocks();

        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0)
        {
            _logger.LogDebug("Status sync tick skipped — gate busy");
            return;
        }

        Interlocked.Exchange(ref _gateAcquiredTicks, DateTimeOffset.UtcNow.Ticks);
        try
        {
            await RunCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status sync tick failed");
        }
        finally
        {
            Interlocked.Exchange(ref _gateAcquiredTicks, 0);
            Interlocked.Exchange(ref _gate, 0);
        }
    }

    private async Task PulseFireAndForgetAsync()
    {
        try
        {
            await RunIfDueAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status sync scheduled pulse failed");
        }
    }

    private OrderSyncRuntimeStatusDto BuildStatus()
    {
        DateTimeOffset? nextDueUtc = null;
        int? secondsOverdue = null;
        if (_lastEnabled == true
            && _lastIntervalMinutes is int interval
            && _lastFinishedUtc is { } finished)
        {
            nextDueUtc = finished.AddMinutes(interval);
            if (_syncing != 1)
            {
                var overdue = DateTimeOffset.UtcNow - nextDueUtc.Value;
                if (overdue > TimeSpan.Zero)
                {
                    secondsOverdue = (int)Math.Min(overdue.TotalSeconds, int.MaxValue);
                }
            }
        }

        return new OrderSyncRuntimeStatusDto
        {
            Enabled = _lastEnabled,
            IntervalMinutes = _lastIntervalMinutes,
            LastStartedUtc = _lastStartedUtc,
            LastFinishedUtc = _lastFinishedUtc,
            LastSummary = _lastSummary,
            IsRunning = _syncing == 1,
            NextDueUtc = nextDueUtc,
            SecondsOverdue = secondsOverdue
        };
    }

    private void TryReleaseStaleLocks()
    {
        var now = DateTimeOffset.UtcNow;

        if (_syncing == 1
            && _lastStartedUtc is { } started
            && now - started > SyncStuckTimeout)
        {
            _logger.LogWarning(
                "Status sync flagged running since {Started:o} (> {Timeout}); force-clearing syncing/gate",
                started,
                SyncStuckTimeout);
            Interlocked.Exchange(ref _syncing, 0);
            Interlocked.Exchange(ref _gateAcquiredTicks, 0);
            Interlocked.Exchange(ref _gate, 0);
            return;
        }

        if (_gate != 1 || _syncing == 1)
        {
            return;
        }

        var acquiredTicks = Interlocked.Read(ref _gateAcquiredTicks);
        if (acquiredTicks > 0)
        {
            var acquired = new DateTimeOffset(acquiredTicks, TimeSpan.Zero);
            if (now - acquired > GateStaleTimeout)
            {
                _logger.LogWarning(
                    "Status sync gate held since {Acquired:o} without syncing; force-releasing",
                    acquired);
                Interlocked.Exchange(ref _gateAcquiredTicks, 0);
                Interlocked.Exchange(ref _gate, 0);
            }

            return;
        }

        if (_lastFinishedUtc is { } finished && now - finished > GateStaleTimeout)
        {
            _logger.LogWarning(
                "Status sync gate stuck with no acquire time (lastFinished {Finished:o}); force-releasing",
                finished);
            Interlocked.Exchange(ref _gate, 0);
        }
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
        var config = await ReadSettingsAsync(settings, cancellationToken);
        _lastEnabled = config.Enabled;
        _lastIntervalMinutes = config.IntervalMinutes;

        if (!config.Enabled)
        {
            _logger.LogDebug("Status sync skipped — disabled in Settings");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!OrderSyncScheduleConfig.IsDue(_lastFinishedUtc, config.IntervalMinutes, now))
        {
            return;
        }

        if (_lastFinishedUtc is { } lastFinished)
        {
            var elapsed = now - lastFinished;
            if (elapsed > TimeSpan.FromMinutes(config.IntervalMinutes * 2))
            {
                _logger.LogWarning(
                    "Status sync overdue by {OverdueHours:0.0} h (interval={IntervalHours}h); starting catch-up",
                    (elapsed.TotalMinutes - config.IntervalMinutes) / 60.0,
                    config.IntervalHours);
            }
        }

        // Yield to the light new-orders loop: status holds FourSellerSyncGate for the whole pass.
        // Also yield on cold start (new never finished) so a heavy status pass cannot win the race.
        var newOrders = _newOrdersOrchestrator.GetStatus();
        var newNeedsPriority =
            newOrders.IsRunning
            || newOrders.SecondsOverdue is > 0
            || (newOrders.Enabled == true && newOrders.LastFinishedUtc is null);
        if (newNeedsPriority)
        {
            _logger.LogInformation(
                "Status sync deferred — new-order sync needs priority (running={Running}, overdue={Overdue}s, neverFinished={NeverFinished}); retry next tick",
                newOrders.IsRunning,
                newOrders.SecondsOverdue,
                newOrders.LastFinishedUtc is null);
            return;
        }

        var syncService = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
        var (dateFromUtc, dateToUtc) = FourSellerOrderDateTime.GetVietnamLookbackUtcRange(
            config.LookbackDays,
            OrderSyncSettingKeys.MaxStatusLookbackDays);
        var (vnFrom, vnTo) = FourSellerOrderDateTime.ToVietnamLocalRange(dateFromUtc, dateToUtc);
        var (pacificFrom, pacificTo) = FourSellerOrderDateTime.ToPacificLocalRange(dateFromUtc, dateToUtc);

        _lastStartedUtc = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "Starting status 4Seller sync (lookbackDays={LookbackDays}, batchSize={BatchSize}, business VN {VnFrom:yyyy-MM-dd HH:mm:ss} .. {VnTo:yyyy-MM-dd HH:mm:ss}, 4Seller Pacific {PacificFrom:yyyy-MM-dd HH:mm:ss} .. {PacificTo:yyyy-MM-dd HH:mm:ss})",
            config.LookbackDays,
            config.BatchSize,
            vnFrom,
            vnTo,
            pacificFrom,
            pacificTo);

        Interlocked.Exchange(ref _syncing, 1);
        OrderSyncResultDto result;
        try
        {
            // Một truy vấn "all" có lọc ngày cho mỗi tài khoản 4Seller (mọi bucket trộn chung),
            // thay cho 6 lượt theo từng bucket. SkipStalePricingSweep: this pass only refreshes
            // platformOrderStatus; the new-orders loop already sweeps empty pricing every few minutes.
            // SkipNewOrderTelegram: new-order alerts stay on the light loop (+ pending sweep).
            result = await syncService.SyncAsync(
                new OrderSyncRequest
                {
                    DateFrom = dateFromUtc,
                    DateTo = dateToUtc,
                    OpenStatusesIgnoreDateFilter = false,
                    SkipStalePricingSweep = true,
                    SkipNewOrderTelegram = true,
                    MaxParallelOwners = config.BatchSize
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _lastSummary = $"ERROR: {Truncate(ex.Message, 180)}";
            _logger.LogError(ex, "Status sync failed before completion");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }

        if (result.SyncedSellers == 0
            && result.Inserted == 0
            && result.Updated == 0
            && result.FailedSellers.Count == 1
            && result.FailedSellers[0].Message.Contains("đang chạy", StringComparison.OrdinalIgnoreCase))
        {
            var overdueHint = BuildStatus().SecondsOverdue;
            if (overdueHint is > 0)
            {
                _logger.LogInformation(
                    "Status sync skipped — another sync is already running (overdue={Overdue}s)",
                    overdueHint);
            }
            else
            {
                _logger.LogDebug("Status sync skipped — another sync is already running");
            }

            return;
        }

        _lastFinishedUtc = DateTimeOffset.UtcNow;
        var duration = _lastFinishedUtc.Value - _lastStartedUtc.Value;
        _lastSummary =
            $"sellers={result.SyncedSellers}, inserted={result.Inserted}, updated={result.Updated}, failed={result.FailedSellers.Count}, durationMin={duration.TotalMinutes:0.0}";

        if (duration > LongPassWarning)
        {
            _logger.LogWarning(
                "Status sync took {DurationMin:0.0} min (> {WarnMin} min); new-order sync was blocked for the whole pass",
                duration.TotalMinutes,
                LongPassWarning.TotalMinutes);
        }

        if (result.SyncedSellers == 0
            && result.Inserted == 0
            && result.Updated == 0
            && result.FailedSellers.Count == 0)
        {
            _logger.LogWarning(
                "Status sync finished with 0 LoggedIn 4Seller owners (no cookie or all expired/unverified): {Summary}",
                _lastSummary);
        }
        else
        {
            _logger.LogInformation(
                "Status sync finished: {Summary}",
                _lastSummary);
        }

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "Status sync seller failure {OwnerId} ({OwnerName}): {Message}",
                failure.OwnerUserId,
                failure.OwnerName,
                failure.Message);
        }
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
        {
            return value;
        }

        return value[..maxLen];
    }

    private static async Task<OrderSyncScheduleConfig.StatusConfig> ReadSettingsAsync(
        ISettingProvider settings,
        CancellationToken cancellationToken)
    {
        var values = await settings.GetValuesAsync(
            [
                OrderSyncSettingKeys.StatusEnabled,
                OrderSyncSettingKeys.StatusIntervalHours,
                OrderSyncSettingKeys.StatusLookbackDays,
                OrderSyncSettingKeys.StatusBatchSize
            ],
            cancellationToken);

        return OrderSyncScheduleConfig.ParseStatus(values);
    }
}
