using Application.Interfaces;
using Domain.Exceptions;
using Shared.Constants;

namespace Infrastructure.Storage;

/// <summary>
/// Resolves Bunny storage configuration from database settings.
/// </summary>
public static class BunnyConfigResolver
{
    /// <summary>
    /// Loads Bunny storage configuration from system settings.
    /// </summary>
    public static async Task<BunnyStorageConfig> ResolveAsync(
        ISettingProvider settingProvider,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingProvider.GetValuesAsync(
        [
            BunnySettingKeys.AccessKey,
            BunnySettingKeys.StorageZone,
            BunnySettingKeys.StorageHostname,
            BunnySettingKeys.CdnHostname
        ],
        cancellationToken);

        settings.TryGetValue(BunnySettingKeys.AccessKey, out var accessKey);
        settings.TryGetValue(BunnySettingKeys.StorageZone, out var storageZone);
        settings.TryGetValue(BunnySettingKeys.StorageHostname, out var storageHostname);
        settings.TryGetValue(BunnySettingKeys.CdnHostname, out var cdnHostname);

        var hostname = ResolveStorageHostname(storageHostname, cdnHostname);
        var cdnBaseUrl = ResolveCdnBaseUrl(cdnHostname);

        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new BusinessException(
                "Chưa cấu hình Bunny storage hostname. Vui lòng thiết lập bunny.storage_hostname trong Cấu hình hệ thống.");
        }

        if (string.IsNullOrWhiteSpace(storageZone))
        {
            throw new BusinessException(
                "Chưa cấu hình Bunny storage zone. Vui lòng thiết lập bunny.storage_zone trong Cấu hình hệ thống.");
        }

        if (string.IsNullOrWhiteSpace(accessKey))
        {
            throw new BusinessException(
                "Chưa cấu hình Bunny access key. Vui lòng thiết lập bunny.access_key trong Cấu hình hệ thống.");
        }

        return new BunnyStorageConfig
        {
            Hostname = hostname.Trim(),
            StorageZoneName = storageZone.Trim(),
            AccessKey = accessKey.Trim(),
            CdnBaseUrl = cdnBaseUrl
        };
    }

    private static string? ResolveStorageHostname(string? storageHostname, string? cdnHostname)
    {
        if (!string.IsNullOrWhiteSpace(storageHostname))
        {
            return NormalizeStorageHostname(storageHostname);
        }

        if (!string.IsNullOrWhiteSpace(cdnHostname) &&
            cdnHostname.Contains("storage.bunnycdn.com", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeStorageHostname(cdnHostname);
        }

        return null;
    }

    /// <summary>
    /// Storage API host only (e.g. sg.storage.bunnycdn.com). Strips accidental URL schemes.
    /// </summary>
    internal static string? NormalizeStorageHostname(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');

        // Full URL pasted into settings → keep Host only.
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }
        }

        // Malformed paste like "https//sg.storage.bunnycdn.com" (missing ':').
        if (trimmed.StartsWith("https//", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("http//", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[6..];
        }

        trimmed = trimmed.TrimStart('/');
        if (trimmed.Contains('/'))
        {
            trimmed = trimmed.Split('/', 2)[0];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? ResolveCdnBaseUrl(string? cdnHostname)
    {
        if (string.IsNullOrWhiteSpace(cdnHostname) ||
            cdnHostname.Contains("storage.bunnycdn.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = cdnHostname.Trim().TrimEnd('/');
        // Accept bare hostname, hostname with path, or full URL.
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // Strip accidental scheme-less prefixes like "//dropship-media.b-cdn.net"
        value = value.TrimStart('/');
        return $"https://{value}";
    }
}
