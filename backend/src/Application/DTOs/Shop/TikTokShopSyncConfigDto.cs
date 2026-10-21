namespace Application.DTOs.Shop;

/// <summary>
/// Extension auto-sync config for TikTok Shop profile (resolved from Settings).
/// </summary>
public class TikTokShopSyncConfigDto
{
    /// <summary>Whether auto-sync is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Auto-sync interval in seconds (value+unit resolved).</summary>
    public int IntervalSeconds { get; set; }
}
