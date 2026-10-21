namespace Application.DTOs.Shop;

/// <summary>
/// Latest TikTok payout already stored for a shop (cursor for incremental scrape).
/// </summary>
public class TikTokPayoutSyncCursorDto
{
    /// <summary>Newest <c>OccurredAt</c> among TikTokPayout ledger rows for the shop (UTC).</summary>
    public DateTime? LatestOccurredAt { get; set; }

    /// <summary>External payment_id of that newest row (when present).</summary>
    public string? LatestExternalTransactionId { get; set; }
}
