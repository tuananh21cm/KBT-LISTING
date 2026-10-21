namespace Shared.Constants;

/// <summary>
/// Settings keys for fulfil order claim / lease (extension multi-operator queue).
/// </summary>
public static class FulfilClaimSettingKeys
{
    /// <summary>Lease TTL in minutes (default 15, clamp 3–60).</summary>
    public const string TtlMinutes = "fulfil.claim.ttl_minutes";

    /// <summary>
    /// Max distinct orders one fulfil user may hold at once (default 10).
    /// Counts orders (leases), not sessions — 3 Chrome profiles on 1 order = 1 slot.
    /// </summary>
    public const string MaxPerUser = "fulfil.claim.max_per_user";

    /// <summary>Default TTL minutes.</summary>
    public const string DefaultTtlMinutes = "15";

    /// <summary>Default max orders per user.</summary>
    public const string DefaultMaxPerUser = "10";

    /// <summary>Minimum TTL minutes.</summary>
    public const int MinTtlMinutes = 3;

    /// <summary>Maximum TTL minutes.</summary>
    public const int MaxTtlMinutes = 60;
}
