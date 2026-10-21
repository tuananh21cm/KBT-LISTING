using System.Collections.Concurrent;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Singleton proxy resolver for 4Seller egress: sticky-per-seller with shared pool failover
/// and per-proxy block cooldowns. Settings (enabled / pool / cooldown) are read from the DB
/// and cached briefly to avoid a query per outbound request.
///
/// Absolute sticky rule: when a seller has a personal <c>FourSellerProxy</c>, that proxy is
/// always returned (even during cooldown). Pool is only for sellers without a personal proxy —
/// jumping IP mid-session can make 4Seller invalidate the cookie.
/// </summary>
public sealed class FourSellerProxyManager : IFourSellerProxyManager
{
    private const int DefaultCooldownMinutes = 10;
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FourSellerProxyManager> _logger;

    /// <summary>Proxy URL → UTC time the cooldown expires.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new();

    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private ProxySettings _cachedSettings = new(false, [], DefaultCooldownMinutes);
    private DateTimeOffset _settingsLoadedAt = DateTimeOffset.MinValue;

    /// <summary>Initializes the manager.</summary>
    public FourSellerProxyManager(
        IServiceScopeFactory scopeFactory,
        ILogger<FourSellerProxyManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        Guid userId,
        string? userProxy,
        CancellationToken cancellationToken = default)
    {
        var ownProxy = Normalize(userProxy);

        // Absolute sticky: personal proxy always wins — never fall through to pool/direct.
        // Jumping IP mid-session can cause 4Seller to kill the cookie.
        if (ownProxy is not null)
        {
            return ownProxy;
        }

        var settings = await GetSettingsAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Shared pool failover only when enabled in Settings (sellers without personal proxy).
        if (settings.Enabled)
        {
            var healthyPool = settings.Pool.Where(p => IsHealthy(p, now)).ToList();
            if (healthyPool.Count > 0)
            {
                var index = (int)((uint)userId.GetHashCode() % (uint)healthyPool.Count);
                return healthyPool[index];
            }

            // Whole pool cooling down — still try a pool entry rather than direct.
            if (settings.Pool.Count > 0)
            {
                return settings.Pool[(int)((uint)userId.GetHashCode() % (uint)settings.Pool.Count)];
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void ReportBlocked(string? proxyUrl, string reason = "block")
    {
        var key = Normalize(proxyUrl);
        if (key is null)
        {
            return;
        }

        var minutes = _cachedSettings.CooldownMinutes;
        var until = DateTimeOffset.UtcNow.AddMinutes(minutes);
        _cooldownUntil[key] = until;
        _logger.LogWarning(
            "4Seller proxy unusable ({Reason}); cooling down until {Until:o}: {Proxy}",
            string.IsNullOrWhiteSpace(reason) ? "block" : reason,
            until,
            key);
    }

    /// <inheritdoc />
    public void ReportSuccess(string? proxyUrl)
    {
        var key = Normalize(proxyUrl);
        if (key is not null)
        {
            _cooldownUntil.TryRemove(key, out _);
        }
    }

    /// <summary>Test helper: whether a proxy is currently in cooldown.</summary>
    internal bool IsInCooldownForTests(string proxy)
    {
        var key = Normalize(proxy);
        return key is not null && !IsHealthy(key, DateTimeOffset.UtcNow);
    }

    private bool IsHealthy(string proxy, DateTimeOffset now) =>
        !_cooldownUntil.TryGetValue(proxy, out var until) || until <= now;

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<ProxySettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _settingsLoadedAt < SettingsCacheTtl)
        {
            return _cachedSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow - _settingsLoadedAt < SettingsCacheTtl)
            {
                return _cachedSettings;
            }

            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
            var values = await provider.GetValuesAsync(
                [
                    FourSellerProxySettingKeys.Enabled,
                    FourSellerProxySettingKeys.Pool,
                    FourSellerProxySettingKeys.CooldownMinutes
                ],
                cancellationToken);

            var enabled = values.TryGetValue(FourSellerProxySettingKeys.Enabled, out var enabledRaw) &&
                          bool.TryParse(enabledRaw, out var e) && e;

            var pool = ParsePool(values.GetValueOrDefault(FourSellerProxySettingKeys.Pool));

            var cooldown = DefaultCooldownMinutes;
            if (values.TryGetValue(FourSellerProxySettingKeys.CooldownMinutes, out var cdRaw) &&
                int.TryParse(cdRaw, out var cd) && cd >= 1)
            {
                cooldown = cd;
            }

            _cachedSettings = new ProxySettings(enabled, pool, cooldown);
            _settingsLoadedAt = DateTimeOffset.UtcNow;
            return _cachedSettings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load 4Seller proxy settings; using last cached value");
            _settingsLoadedAt = DateTimeOffset.UtcNow;
            return _cachedSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private static IReadOnlyList<string> ParsePool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record ProxySettings(bool Enabled, IReadOnlyList<string> Pool, int CooldownMinutes);
}
