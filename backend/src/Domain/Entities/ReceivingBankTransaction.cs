using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Ledger entry for money received on a receiving bank account (thẻ nhận tiền).
/// </summary>
public class ReceivingBankTransaction : BaseEntity
{
    /// <summary>Auto-created when marking an order as settlement paid.</summary>
    public const string SourceSettlementPaid = "SettlementPaid";

    /// <summary>Manually entered on transaction history pages.</summary>
    public const string SourceManual = "Manual";

    /// <summary>TikTok Shop payout synced from seller-us finance (one row per payment_id).</summary>
    public const string SourceTikTokPayout = "TikTokPayout";

    /// <summary>Adjustment that reduces received balance.</summary>
    public const string DirectionDebit = "Debit";

    /// <summary>Inbound payout / money received.</summary>
    public const string DirectionCredit = "Credit";

    /// <summary>Awaiting admin review (seller Manual, or TikTok payout card-tail mismatch).</summary>
    public const string ConfirmationPending = "Pending";

    /// <summary>Counted in incoming-money stats (admin-created / approved / SettlementPaid / matching payout).</summary>
    public const string ConfirmationConfirmed = "Confirmed";

    /// <summary>Admin rejected — excluded from incoming-money totals.</summary>
    public const string ConfirmationRejected = "Rejected";

    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Receiving bank card (derived from shop for Manual creates).</summary>
    public Guid ReceivingBankAccountId { get; set; }

    /// <summary>Shop that received the payout (required for Manual creates).</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Linked order when from settlement paid (null for manual).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Absolute transaction amount (&gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary><see cref="DirectionDebit"/> or <see cref="DirectionCredit"/>.</summary>
    public string Direction { get; set; } = DirectionCredit;

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary>Payout / transfer id from the platform that sent the money.</summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>
    /// JSON array of TikTok statement order lines for <see cref="SourceTikTokPayout"/>
    /// (tradeOrderId, settlement/earning/fees, settlementTime). Null for other sources.
    /// </summary>
    public string? PayoutDetailsJson { get; set; }

    /// <summary><see cref="SourceSettlementPaid"/>, <see cref="SourceManual"/>, or <see cref="SourceTikTokPayout"/>.</summary>
    public string Source { get; set; } = SourceManual;

    /// <summary><see cref="ConfirmationPending"/>, <see cref="ConfirmationConfirmed"/>, or <see cref="ConfirmationRejected"/>.</summary>
    public string ConfirmationStatus { get; set; } = ConfirmationConfirmed;

    /// <summary>Who created the row.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When the money movement occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Card navigation.</summary>
    public ReceivingBankAccount ReceivingBankAccount { get; set; } = null!;

    /// <summary>Shop navigation.</summary>
    public Shop? Shop { get; set; }

    /// <summary>Order navigation.</summary>
    public Order? Order { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
