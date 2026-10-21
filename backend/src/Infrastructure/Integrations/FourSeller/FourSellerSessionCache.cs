using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// In-memory process-wide cache of validated 4Seller sessions.
/// TTL is read from Settings (<see cref="FourSellerSessionSettingKeys.ValidateTtlMinutes"/>).
/// </summary>
public sealed class FourSellerSessionCache : IFourSellerSessionCache
{
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FourSellerSessionCache> _logger;

    private int _ttlMinutes = FourSellerSessionSettingKeys.DefaultValidateTtlMinutes;
    private DateTimeOffset _settingsLoadedAt = DateTimeOffset.MinValue;
    private int _refreshInFlight; // 0 = idle, 1 = refreshing

    /// <summary>Initializes the cache.</summary>
    public FourSellerSessionCache(
        IServiceScopeFactory scopeFactory,
        ILogger<FourSellerSessionCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryGetValid(Guid userId, string cookie)
    {
        EnsureSettingsFresh();

        if (!_entries.TryGetValue(userId, out var entry))
        {
            return false;
        }

        var fingerprint = Fingerprint(cookie);
        if (!string.Equals(entry.CookieFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _entries.TryRemove(userId, out _);
            return false;
        }

        var ttl = Volatile.Read(ref _ttlMinutes);
        if (DateTimeOffset.UtcNow - entry.ValidatedAt > TimeSpan.FromMinutes(ttl))
        {
            _entries.TryRemove(userId, out _);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void MarkValid(Guid userId, string cookie)
    {
        EnsureSettingsFresh();
        _entries[userId] = new CacheEntry(Fingerprint(cookie), DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void Invalidate(Guid userId)
    {
        _entries.TryRemove(userId, out _);
    }

    /// <summary>
    /// Test helper: overrides the TTL minutes without hitting Settings.
    /// </summary>
    internal void SetTtlMinutesForTests(int minutes)
    {
        Volatile.Write(ref _ttlMinutes, Math.Max(1, minutes));
        _settingsLoadedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureSettingsFresh()
    {
        if (DateTimeOffset.UtcNow - _settingsLoadedAt < SettingsCacheTtl)
        {
            return;
        }

        // Single-flight async refresh; callers keep using the last known TTL.
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = RefreshSettingsAsync();
    }

    private async Task RefreshSettingsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
            var raw = await provider.GetValueAsync(FourSellerSessionSettingKeys.ValidateTtlMinutes);

            var ttl = FourSellerSessionSettingKeys.DefaultValidateTtlMinutes;
            if (int.TryParse(raw, out var parsed) && parsed >= 1)
            {
                ttl = parsed;
            }

            Volatile.Write(ref _ttlMinutes, ttl);
            _settingsLoadedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not load 4Seller session TTL; keeping {Minutes} min",
                Volatile.Read(ref _ttlMinutes));
            _settingsLoadedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInFlight, 0);
        }
    }

    private static string Fingerprint(string cookie)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cookie.Trim()));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private sealed record CacheEntry(string CookieFingerprint, DateTimeOffset ValidatedAt);
}
