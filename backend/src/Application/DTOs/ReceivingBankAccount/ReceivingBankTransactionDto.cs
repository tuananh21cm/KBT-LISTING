namespace Application.DTOs.ReceivingBankAccount;

/// <summary>Receiving-bank transaction ledger row.</summary>
public class ReceivingBankTransactionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Receiving bank card id.</summary>
    public Guid ReceivingBankAccountId { get; set; }

    /// <summary>Card display name.</summary>
    public string? ReceivingBankAccountName { get; set; }

    /// <summary>Card account number (full).</summary>
    public string? ReceivingBankAccountNumber { get; set; }

    /// <summary>Masked account number (e.g. 0039****9549).</summary>
    public string? ReceivingBankAccountNumberMasked { get; set; }

    /// <summary>Card currency.</summary>
    public string? ReceivingBankAccountCurrency { get; set; }

    /// <summary>Provider source name (PingPongX, WorldFirst, …).</summary>
    public string? SourceName { get; set; }

    /// <summary>Provider source account name.</summary>
    public string? SourceAccountName { get; set; }

    /// <summary>Account holder name.</summary>
    public string? AccountHolderName { get; set; }

    /// <summary>Shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Browser profile name of the shop.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Shop owner user id.</summary>
    public Guid? ShopOwnerId { get; set; }

    /// <summary>Shop owner display name (seller).</summary>
    public string? ShopOwnerName { get; set; }

    /// <summary>Order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Platform order id for display.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>4Seller order id.</summary>
    public long? FourSellerOrderId { get; set; }

    /// <summary>Amount (absolute, &gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit (điều chỉnh trừ) or Credit (tiền về).</summary>
    public string Direction { get; set; } = "Credit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Payout / transfer id from the platform that sent the money.</summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>SettlementPaid, Manual, or TikTokPayout.</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>
    /// Order lines inside a TikTok payout (only when <see cref="Source"/> is TikTokPayout).
    /// </summary>
    public List<TikTokPayoutOrderDetailDto>? PayoutOrders { get; set; }

    /// <summary>Pending, Confirmed, or Rejected.</summary>
    public string ConfirmationStatus { get; set; } = "Confirmed";

    /// <summary>Creator user id.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creator name.</summary>
    public string? CreatedByUserName { get; set; }

    /// <summary>Occurred at.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create manual receiving-bank transaction (card derived from shop).</summary>
public class CreateReceivingBankTransactionDto
{
    /// <summary>Shop that received the payout (required; card resolved from shop).</summary>
    public Guid ShopId { get; set; }

    /// <summary>Optional order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Amount (absolute, &gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit or Credit. Default Credit. Sellers may only submit Credit.</summary>
    public string Direction { get; set; } = "Credit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Payout / transfer id from the platform that sent the money.</summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>Occurred at (UTC).</summary>
    public DateTime? OccurredAt { get; set; }
}

/// <summary>Admin sets ledger confirmation: Pending, Confirmed, or Rejected.</summary>
public class SetReceivingBankConfirmationStatusDto
{
    /// <summary>Pending (Chờ duyệt), Confirmed (Đã duyệt), or Rejected (Không duyệt).</summary>
    public string ConfirmationStatus { get; set; } = string.Empty;
}

/// <summary>Update receiving-bank transaction (card derived from shop for Manual).</summary>
public class UpdateReceivingBankTransactionDto
{
    /// <summary>Shop id (required for Manual; locked for SettlementPaid).</summary>
    public Guid ShopId { get; set; }

    /// <summary>Optional order id.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Amount (absolute, &gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit or Credit.</summary>
    public string Direction { get; set; } = "Credit";

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Note.</summary>
    public string? Note { get; set; }

    /// <summary>Payout / transfer id from the platform that sent the money.</summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>Occurred at (UTC).</summary>
    public DateTime OccurredAt { get; set; }
}

/// <summary>Filter for receiving-bank transaction list.</summary>
public class ReceivingBankTransactionQuery
{
    /// <summary>Filter by card.</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Filter by shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>
    /// Seller scope: only rows linked to shops owned by this user.
    /// Manual rows with null <c>ShopId</c> are excluded when set.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Filter by order.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Filter by source (SettlementPaid / Manual / TikTokPayout).</summary>
    public string? Source { get; set; }

    /// <summary>From (inclusive, UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>To (inclusive, UTC).</summary>
    public DateTime? To { get; set; }
}

/// <summary>One order line inside a synced TikTok payout.</summary>
public class TikTokPayoutOrderDetailDto
{
    /// <summary>TikTok trade_order_id.</summary>
    public string TradeOrderId { get; set; } = string.Empty;

    /// <summary>Settlement amount for the order.</summary>
    public decimal? SettlementAmount { get; set; }

    /// <summary>Earning / retained amount for the order.</summary>
    public decimal? EarningAmount { get; set; }

    /// <summary>Fees amount for the order.</summary>
    public decimal? Fees { get; set; }

    /// <summary>TikTok statement / bill period (ms) — UI Statement date (Pacific).</summary>
    public string? SettlementTime { get; set; }
}
