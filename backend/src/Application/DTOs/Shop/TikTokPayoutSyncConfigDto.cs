namespace Application.DTOs.Shop;

/// <summary>
/// Extension auto-sync config for TikTok Shop payouts (resolved from Settings).
/// </summary>
public class TikTokPayoutSyncConfigDto
{
    /// <summary>Whether auto-sync is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Auto-sync interval in seconds (value+unit resolved).</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// ISO date (yyyy-MM-dd) floor — do not sync payouts older than this UTC midnight.
    /// Null when unset.
    /// </summary>
    public string? MaxDate { get; set; }

    /// <summary>Whether Telegram is enabled for newly inserted payouts.</summary>
    public bool NotifyNewPayment { get; set; }
}
