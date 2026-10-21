namespace Application.DTOs.Shop;

/// <summary>
/// Batch of TikTok payouts scraped by the browser extension.
/// </summary>
public class SyncTikTokPayoutsDto
{
    /// <summary>Run id from extension unified Sync (optional).</summary>
    public string? RunId { get; set; }

    /// <summary>TikTok seller / OEC id — matches <c>Shop.ExternalShopId</c>.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>Alias of <see cref="ExternalShopId"/> (extension may send sellerId).</summary>
    public string? SellerId { get; set; }

    /// <summary>Payout records (newest first preferred).</summary>
    public List<SyncTikTokPayoutItemDto> Payments { get; set; } = [];
}

/// <summary>One TikTok payout (payment_id).</summary>
public class SyncTikTokPayoutItemDto
{
    /// <summary>TikTok <c>payment_id</c>.</summary>
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>Banked amount (<c>payment_amount.amount</c>).</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency code (default USD).</summary>
    public string? Currency { get; set; }

    /// <summary>Unix ms or ISO — payment success time.</summary>
    public string? SuccessTime { get; set; }

    /// <summary>Masked bank card from TikTok.</summary>
    public string? BankCardMask { get; set; }

    /// <summary>Payment note from TikTok.</summary>
    public string? Note { get; set; }

    /// <summary>Orders settled in this payout (optional when statement_id unresolved).</summary>
    public List<SyncTikTokPayoutOrderDto> Orders { get; set; } = [];
}

/// <summary>Order line inside a TikTok payout / statement.</summary>
public class SyncTikTokPayoutOrderDto
{
    /// <summary>TikTok <c>trade_order_id</c> — matches <c>Order.PlatformOrderId</c>.</summary>
    public string TradeOrderId { get; set; } = string.Empty;

    /// <summary>Settlement amount for the order.</summary>
    public decimal? SettlementAmount { get; set; }

    /// <summary>Earning amount for the order.</summary>
    public decimal? EarningAmount { get; set; }

    /// <summary>Fees amount for the order.</summary>
    public decimal? Fees { get; set; }

    /// <summary>TikTok statement / bill period (ms) — UI «Statement date» (Pacific).</summary>
    public string? SettlementTime { get; set; }
}
