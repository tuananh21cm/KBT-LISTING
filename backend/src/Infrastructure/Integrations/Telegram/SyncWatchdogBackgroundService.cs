using System.Globalization;
using System.Text;
using Application.DTOs.Order;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure.Integrations.Telegram;

/// <summary>
/// Watches the two order sync loops and reports when they stop making progress.
///
/// Before this existed, a stalled sync was invisible: the admin cards only show a clock that
/// quietly stops advancing, so on 2026-09-30 nobody noticed the loops were dead for two and a
/// half hours. Two independent signals:
/// <list type="bullet">
/// <item><description>Stall — a loop is enabled but overdue past the configured threshold. Stateful:
/// one alert when it trips, one recovery message when it clears.</description></item>
/// <item><description>Crash — <see cref="BackgroundServiceHealthRegistry"/> saw a loop throw and
/// restart. Event-based: reported once, batched per tick.</description></item>
/// </list>
///
/// This runs in-process, so it cannot report a host that is fully down — that is what
/// <see cref="ResilientBackgroundService"/> and the IIS keep-alive ping are for.
/// </summary>
public sealed class SyncWatchdogBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    /// <summary>Minimum gap between repeated stall alerts while the stall persists.</summary>
    private static readonly TimeSpan StallRepeatInterval = TimeSpan.FromMinutes(30);

    private const string Separator = "————————————————————";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOrderSyncOrchestrator _newOrderSync;
    private readonly IOrderStatusSyncOrchestrator _statusSync;
    private readonly ILogger<SyncWatchdogBackgroundService> _logger;

    private DateTimeOffset _startedUtc;
    private DateTimeOffset? _lastStallAlertUtc;
    private DateTimeOffset _lastCrashSeenUtc = DateTimeOffset.MinValue;
    private bool _stallActive;

    /// <summary>Initializes the watchdog.</summary>
    public SyncWatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOrderSyncOrchestrator newOrderSync,
        IOrderStatusSyncOrchestrator statusSync,
        ILogger<SyncWatchdogBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _newOrderSync = newOrderSync;
        _statusSync = statusSync;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        _startedUtc = DateTimeOffset.UtcNow;

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
            "Sync watchdog started (tick every {Minutes} min; threshold from Settings)",
            TickInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sync watchdog tick failed");
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

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var config = await ReadSettingsAsync(cancellationToken);
        if (!config.Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromMinutes(config.OverdueMinutes);

        var stalls = new List<string>();
        AppendStall(stalls, "Đơn mới", _newOrderSync.GetStatus(), config.NewOrdersEnabled, threshold, now);
        AppendStall(stalls, "Trạng thái", _statusSync.GetStatus(), config.StatusEnabled, threshold, now);

        await ReportStallsAsync(stalls, config.OverdueMinutes, now, cancellationToken);
        await ReportCrashesAsync(cancellationToken);
    }

    private async Task ReportStallsAsync(
        IReadOnlyList<string> stalls,
        int overdueMinutes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (stalls.Count == 0)
        {
            if (_stallActive)
            {
                _stallActive = false;
                _lastStallAlertUtc = null;
                _logger.LogInformation("Sync watchdog cleared — loops are on schedule again");
                await SendAsync(
                    BuildMessage("✅ Job sync đã chạy lại", ["Các luồng đồng bộ đã trở lại đúng chu kỳ."]),
                    cancellationToken);
            }

            return;
        }

        var due = !_stallActive
            || _lastStallAlertUtc is not { } last
            || now - last >= StallRepeatInterval;
        if (!due)
        {
            return;
        }

        _stallActive = true;
        _lastStallAlertUtc = now;
        _logger.LogWarning(
            "Sync watchdog alert (threshold {Threshold} min): {Details}",
            overdueMinutes,
            string.Join(" | ", stalls));

        await SendAsync(BuildMessage("⚠️ Job sync đang đứng", stalls), cancellationToken);
    }

    private async Task ReportCrashesAsync(CancellationToken cancellationToken)
    {
        var crashes = BackgroundServiceHealthRegistry.SnapshotSince(_lastCrashSeenUtc);
        if (crashes.Count == 0)
        {
            return;
        }

        // Advance past the newest crash rather than to "now": a crash landing in the same instant
        // as the tick would otherwise be reported twice.
        _lastCrashSeenUtc = crashes[0].OccurredUtc.AddTicks(1);

        var lines = crashes
            .Select(c => $"• {EscapeHtml(c.ServiceName)} (lần {c.CrashCount}, {FormatClock(c.OccurredUtc)}): {EscapeHtml(c.Message)}")
            .ToList();

        await SendAsync(BuildMessage("♻️ Vòng lặp nền vừa khởi động lại", lines), cancellationToken);
    }

    private async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
        await notifier.NotifySyncHealthAlertAsync(text, cancellationToken);
    }

    /// <summary>
    /// Adds a line when <paramref name="status"/> is behind schedule. A loop that has never
    /// finished counts as stalled too once the watchdog itself has been up past the threshold —
    /// that is the shape of a loop that died before its first successful pass.
    /// </summary>
    private void AppendStall(
        List<string> stalls,
        string label,
        OrderSyncRuntimeStatusDto status,
        bool enabled,
        TimeSpan threshold,
        DateTimeOffset now)
    {
        if (!enabled || status.IsRunning)
        {
            return;
        }

        if (status.LastFinishedUtc is null)
        {
            if (now - _startedUtc > threshold)
            {
                stalls.Add($"• {label}: chưa hoàn tất lượt nào kể từ khi API khởi động");
            }

            return;
        }

        if (status.SecondsOverdue is not { } overdueSeconds
            || overdueSeconds <= threshold.TotalSeconds)
        {
            return;
        }

        var interval = status.IntervalMinutes is { } minutes ? $", chu kỳ {minutes} phút" : string.Empty;
        stalls.Add(
            $"• {label}: trễ {FormatDuration(TimeSpan.FromSeconds(overdueSeconds))}{interval}, xong lúc {FormatClock(status.LastFinishedUtc)}");
    }

    private async Task<WatchdogConfig> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();

        var values = await settings.GetValuesAsync(
            [
                OrderSyncSettingKeys.WatchdogEnabled,
                OrderSyncSettingKeys.WatchdogOverdueMinutes,
                OrderSyncSettingKeys.NewEnabled,
                OrderSyncSettingKeys.StatusEnabled
            ],
            cancellationToken);

        values.TryGetValue(OrderSyncSettingKeys.WatchdogEnabled, out var enabledRaw);
        values.TryGetValue(OrderSyncSettingKeys.WatchdogOverdueMinutes, out var minutesRaw);
        values.TryGetValue(OrderSyncSettingKeys.NewEnabled, out var newEnabledRaw);
        values.TryGetValue(OrderSyncSettingKeys.StatusEnabled, out var statusEnabledRaw);

        return new WatchdogConfig(
            ParseBool(enabledRaw, defaultValue: true),
            ParseOverdueMinutes(minutesRaw),
            ParseBool(newEnabledRaw, defaultValue: true),
            ParseBool(statusEnabledRaw, defaultValue: true));
    }

    private static int ParseOverdueMinutes(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(
                parsed,
                OrderSyncSettingKeys.MinWatchdogOverdueMinutes,
                OrderSyncSettingKeys.MaxWatchdogOverdueMinutes)
            : OrderSyncSettingKeys.DefaultWatchdogOverdueMinutes;

    private static bool ParseBool(string? raw, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            return false;
        }

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1")
        {
            return true;
        }

        return defaultValue;
    }

    private static string BuildMessage(string title, IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder(title).Append('\n').Append(Separator);
        foreach (var line in lines)
        {
            sb.Append('\n').Append(line);
        }

        return sb.ToString();
    }

    private static string FormatClock(DateTimeOffset? utc) =>
        utc is null
            ? "—"
            : TimeZoneInfo
                .ConvertTime(utc.Value, AppTimeZones.Vietnam)
                .ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
        {
            return $"{value.TotalSeconds:0} giây";
        }

        if (value.TotalHours < 1)
        {
            return $"{value.TotalMinutes:0} phút";
        }

        return $"{value.TotalHours:0.0} giờ";
    }

    private static string EscapeHtml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record WatchdogConfig(
        bool Enabled,
        int OverdueMinutes,
        bool NewOrdersEnabled,
        bool StatusEnabled);
}
