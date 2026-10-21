namespace Shared.Constants;

/// <summary>
/// Setting keys for Bunny CDN configuration (managed via admin Settings).
/// </summary>
public static class BunnySettingKeys
{
    /// <summary>Bunny storage zone access key.</summary>
    public const string AccessKey = "bunny.access_key";

    /// <summary>Storage zone name.</summary>
    public const string StorageZone = "bunny.storage_zone";

    /// <summary>Storage API hostname (e.g. sg.storage.bunnycdn.com).</summary>
    public const string StorageHostname = "bunny.storage_hostname";

    /// <summary>CDN pull zone hostname or base URL (e.g. xxx.b-cdn.net).</summary>
    public const string CdnHostname = "bunny.cdn_hostname";

    /// <summary>Max upload file size in megabytes.</summary>
    public const string MaxFileSizeMb = "bunny.max_file_size_mb";
}
