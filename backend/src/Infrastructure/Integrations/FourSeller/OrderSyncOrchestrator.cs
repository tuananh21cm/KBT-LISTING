using Application.DTOs.Order;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Light new-orders auto-sync runner for background timer and IIS keep-alive /health pings.
/// Pulls pending / on_hold / to_ship / canceled inside a short create-date lookback.
/// </summary>
public sealed class OrderSyncOrchestrator : IOrderSyncOrchestrator
{
    /// <summary>Max time a real 4Seller sync may hold <see cref="_syncing"/> before force-clear.</summary>
    private static readonly TimeSpan SyncStuckTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Max time the gate may be held without an active sync (settings/interval check).</summary>
    private static readonly TimeSpan GateStaleTimeout = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderSyncOrchestrator> _logger;

    /// <summary>Prevents overlapping RunCore / ScheduleIfDue ticks.</summary>
    private int _gate;

    /// <summary>True only while SyncAsync is in progress (not during settings/interval checks).</summary>
    private int _syncing;

    private long _gateAcquiredTicks; // DateTimeOffset.UtcNow.Ticks when gate acquired; 0 = free
    private bool? _lastEnabled;
    private int? _lastIntervalMinutes;
    private DateTimeOffset? _lastStartedUtc;
    private DateTimeOffset? _lastFinishedUtc;
    private string? _lastSummary;

    /// <summary>
    /// Initializes the orchestrator.
    /// </summary>
    public OrderSyncOrchestrator(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderSyncOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
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
            _logger.LogWarning(ex, "Could not refresh new-order sync settings for /health status");
        }

        return BuildStatus();
    }

    /// <inheritdoc />
    public void ScheduleIfDue()
    {
        // Start on the caller’s execution context (no Task.Run). Under IIS, queued thread-pool
        // work can starve while keep-alive still returns — leaving nextDue stuck in the past.
        _ = PulseFireAndForgetAsync();
    }

    /// <inheritdoc />
    public async Task RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        TryReleaseStaleLocks();

        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0)
        {
            _logger.LogDebug("New-order sync tick skipped — gate busy");
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
            // Keep-alive /health must stay 200 even when 4Seller sync fails.
            _logger.LogError(ex, "New-order sync tick failed");
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
            _logger.LogError(ex, "New-order sync scheduled pulse failed");
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

    /// <summary>
    /// Clears a stuck gate / syncing flag so keep-alive can resume after hang or thread-pool glitch.
    /// </summary>
    private void TryReleaseStaleLocks()
    {
        var now = DateTimeOffset.UtcNow;

        if (_syncing == 1
            && _lastStartedUtc is { } started
            && now - started > SyncStuckTimeout)
        {
            _logger.LogWarning(
                "New-order sync flagged running since {Started:o} (> {Timeout}); force-clearing syncing/gate",
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
                    "New-order sync gate held since {Acquired:o} without syncing; force-releasing",
                    acquired);
                Interlocked.Exchange(ref _gateAcquiredTicks, 0);
                Interlocked.Exchange(ref _gate, 0);
            }

            return;
        }

        // Gate=1 but no acquire timestamp (legacy stuck / crash mid-set) and not syncing.
        if (_lastFinishedUtc is { } finished && now - finished > GateStaleTimeout)
        {
            _logger.LogWarning(
                "New-order sync gate stuck with no acquire time (lastFinished {Finished:o}); force-releasing",
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
            _logger.LogDebug("New-order sync skipped — disabled in Settings");
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
                    "New-order sync overdue by {OverdueMinutes:0.0} min (interval={IntervalMinutes}); starting catch-up",
                    elapsed.TotalMinutes - config.IntervalMinutes,
                    config.IntervalMinutes);
            }
        }

        var syncService = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
        var (dateFromUtc, dateToUtc) = FourSellerOrderDateTime.GetVietnamLookbackUtcRange(
            config.LookbackDays,
            OrderSyncSettingKeys.MaxNewLookbackDays);
        var (vnFrom, vnTo) = FourSellerOrderDateTime.ToVietnamLocalRange(dateFromUtc, dateToUtc);
        var (pacificFrom, pacificTo) = FourSellerOrderDateTime.ToPacificLocalRange(dateFromUtc, dateToUtc);

        _lastStartedUtc = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "Starting new-order 4Seller sync (lookbackDays={LookbackDays}, batchSize={BatchSize}, business VN {VnFrom:yyyy-MM-dd HH:mm:ss} .. {VnTo:yyyy-MM-dd HH:mm:ss}, 4Seller Pacific {PacificFrom:yyyy-MM-dd HH:mm:ss} .. {PacificTo:yyyy-MM-dd HH:mm:ss})",
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
            // Không truyền OrderStatuses: một truy vấn "all" có lọc ngày lấy đủ mọi bucket trong
            // một lượt phân trang, thay vì 4 round-trip riêng cho pending/on_hold/to_ship/canceled.
            result = await syncService.SyncAsync(
                new OrderSyncRequest
                {
                    DateFrom = dateFromUtc,
                    DateTo = dateToUtc,
                    OpenStatusesIgnoreDateFilter = false,
                    MaxParallelOwners = config.BatchSize
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Keep lastFinished unchanged so the next tick retries immediately (nextDue stays overdue).
            _lastSummary = $"ERROR: {Truncate(ex.Message, 180)}";
            _logger.LogError(ex, "New-order sync failed before completion");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }

        // SyncGate skip — do not advance lastFinished so the next tick retries soon.
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
                    "New-order sync skipped — another sync is already running (overdue={Overdue}s)",
                    overdueHint);
            }
            else
            {
                _logger.LogDebug("New-order sync skipped — another sync is already running");
            }

            return;
        }

        _lastFinishedUtc = DateTimeOffset.UtcNow;
        _lastSummary =
            $"sellers={result.SyncedSellers}, inserted={result.Inserted}, updated={result.Updated}, failed={result.FailedSellers.Count}";

        if (result.SyncedSellers == 0
            && result.Inserted == 0
            && result.Updated == 0
            && result.FailedSellers.Count == 0)
        {
            _logger.LogWarning(
                "New-order sync finished with 0 LoggedIn 4Seller owners (no cookie or all expired/unverified): {Summary}",
                _lastSummary);
        }
        else
        {
            _logger.LogInformation(
                "New-order sync finished: {Summary}",
                _lastSummary);
        }

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "New-order sync seller failure {OwnerId} ({OwnerName}): {Message}",
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

    private static async Task<OrderSyncScheduleConfig.NewOrdersConfig> ReadSettingsAsync(
        ISettingProvider settings,
        CancellationToken cancellationToken)
    {
        var values = await settings.GetValuesAsync(
            [
                OrderSyncSettingKeys.NewEnabled,
                OrderSyncSettingKeys.NewIntervalMinutes,
                OrderSyncSettingKeys.NewLookbackDays,
                OrderSyncSettingKeys.NewBatchSize
            ],
            cancellationToken);

        return OrderSyncScheduleConfig.ParseNewOrders(values);
    }
}
