namespace Infrastructure.Storage;

/// <summary>
/// Bunny CDN storage runtime configuration loaded from system settings.
/// </summary>
public class BunnyStorageConfig
{
    /// <summary>
    /// Storage API hostname (e.g. sg.storage.bunnycdn.com).
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Storage zone name.
    /// </summary>
    public string StorageZoneName { get; set; } = string.Empty;

    /// <summary>
    /// Storage zone password (AccessKey).
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional CDN pull zone base URL.
    /// </summary>
    public string? CdnBaseUrl { get; set; }
}
