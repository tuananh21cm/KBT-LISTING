using Application.DTOs.Promotion;
using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Periodically syncs 4Seller Product Discount and/or Flash Deal activities.
/// Each lane has its own enabled flag and interval (hours). The next interval for a lane
/// starts only after that lane's sync run has fully completed.
/// </summary>
public class ProductDiscountSyncBackgroundService : ResilientBackgroundService
{
    private const int DefaultIntervalHours = 2;
    private const int PollSeconds = 60;
    private const int StartupDelaySeconds = 45;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductDiscountSyncBackgroundService> _logger;

    private DateTime? _lastProductDiscountFinishedUtc;
    private DateTime? _lastFlashDealFinishedUtc;

    /// <summary>Initializes the background sync.</summary>
    public ProductDiscountSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductDiscountSyncBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = await ReadSettingsAsync(stoppingToken);
            var now = DateTime.UtcNow;

            if (settings.ProductDiscountEnabled && IsDue(_lastProductDiscountFinishedUtc, settings.ProductDiscountIntervalHours, now))
            {
                try
                {
                    await RunLaneAsync(
                        discountType: string.Empty,
                        laneName: "product-discount",
                        stoppingToken);
                    _lastProductDiscountFinishedUtc = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Product discount sync background lane failed unexpectedly");
                }
            }

            if (settings.FlashDealEnabled && IsDue(_lastFlashDealFinishedUtc, settings.FlashDealIntervalHours, DateTime.UtcNow))
            {
                try
                {
                    await RunLaneAsync(
                        discountType: PromotionDiscountTypes.FlashSale,
                        laneName: "flash-deal",
                        stoppingToken);
                    _lastFlashDealFinishedUtc = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Flash deal sync background lane failed unexpectedly");
                }
            }

            if (!settings.ProductDiscountEnabled && !settings.FlashDealEnabled)
            {
                _logger.LogDebug("Product discount and Flash Deal background sync are both disabled (Settings)");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static bool IsDue(DateTime? lastFinishedUtc, int intervalHours, DateTime nowUtc)
    {
        if (lastFinishedUtc is null)
        {
            return true;
        }

        return nowUtc - lastFinishedUtc.Value >= TimeSpan.FromHours(intervalHours);
    }

    private async Task<LaneSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();

        var values = await settings.GetValuesAsync(
            [
                ProductDiscountSyncSettingKeys.Enabled,
                ProductDiscountSyncSettingKeys.IntervalHours,
                FlashDealSyncSettingKeys.Enabled,
                FlashDealSyncSettingKeys.IntervalHours
            ],
            cancellationToken);

        values.TryGetValue(ProductDiscountSyncSettingKeys.Enabled, out var pdEnabledRaw);
        values.TryGetValue(ProductDiscountSyncSettingKeys.IntervalHours, out var pdHoursRaw);
        values.TryGetValue(FlashDealSyncSettingKeys.Enabled, out var flashEnabledRaw);
        values.TryGetValue(FlashDealSyncSettingKeys.IntervalHours, out var flashHoursRaw);

        return new LaneSettings(
            ParseBool(pdEnabledRaw, defaultValue: true),
            ParseIntervalHours(pdHoursRaw),
            ParseBool(flashEnabledRaw, defaultValue: true),
            ParseIntervalHours(flashHoursRaw));
    }

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

    private static int ParseIntervalHours(string? intervalRaw)
    {
        if (int.TryParse(intervalRaw, out var parsed) && parsed >= 1)
        {
            return parsed;
        }

        return DefaultIntervalHours;
    }

    private async Task RunLaneAsync(
        string discountType,
        string laneName,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IProductDiscountSyncService>();

        _logger.LogInformation("Starting background 4Seller {Lane} sync", laneName);

        var result = await syncService.SyncAsync(
            new ProductDiscountSyncRequest { DiscountType = string.IsNullOrEmpty(discountType) ? null : discountType },
            progress: null,
            cancellationToken);

        _logger.LogInformation(
            "Background {Lane} sync finished: sellers={Synced}, inserted={Inserted}, updated={Updated}, skipped={Skipped}, failed={Failed}",
            laneName,
            result.SyncedSellers,
            result.Inserted,
            result.Updated,
            result.Skipped,
            result.FailedSellers.Count);

        foreach (var failure in result.FailedSellers)
        {
            _logger.LogWarning(
                "{Lane} sync seller failure {OwnerId} ({OwnerName}): {Message}",
                laneName,
                failure.OwnerUserId,
                failure.OwnerName,
                failure.Message);
        }
    }

    private sealed record LaneSettings(
        bool ProductDiscountEnabled,
        int ProductDiscountIntervalHours,
        bool FlashDealEnabled,
        int FlashDealIntervalHours);
}
