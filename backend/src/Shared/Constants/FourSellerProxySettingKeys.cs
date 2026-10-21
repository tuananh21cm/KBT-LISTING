namespace Shared.Constants;

/// <summary>
/// Setting keys for the 4Seller egress proxy pool (managed via admin Settings).
/// Per-seller proxy lives on <c>ApplicationUser.FourSellerProxy</c>; these settings
/// control the shared failover pool and the master enable toggle.
/// </summary>
public static class FourSellerProxySettingKeys
{
    /// <summary>
    /// When true, sellers without a personal proxy may use the shared failover pool.
    /// Per-seller <c>ApplicationUser.FourSellerProxy</c> is always used when set (toggle does not gate it).
    /// </summary>
    public const string Enabled = "fourseller.proxy.enabled";

    /// <summary>
    /// Shared failover proxy pool. One proxy per line (or comma-separated).
    /// Formats: <c>ip:port</c>, <c>ip:port:user:pass</c>, or <c>[scheme://][user:pass@]host:port</c>
    /// (scheme optional, defaults to http CONNECT; prefix <c>https://</c> for HTTPS proxy; <c>socks5://</c> for SOCKS5).
    /// </summary>
    public const string Pool = "fourseller.proxy.pool";

    /// <summary>Minutes a proxy stays in cooldown after a 4Seller block (567) before it is retried. Minimum 1.</summary>
    public const string CooldownMinutes = "fourseller.proxy.cooldown_minutes";
}
