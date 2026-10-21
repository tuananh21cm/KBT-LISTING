using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Company operating income/expense ledger (sổ cái thu/chi hệ thống).
/// Independent of shop payout and fulfil purchase ledgers.
/// </summary>
public class CompanyLedgerEntry : BaseEntity
{
    /// <summary>Money out.</summary>
    public const string DirectionDebit = "Debit";

    /// <summary>Money in.</summary>
    public const string DirectionCredit = "Credit";

    /// <summary>Entered on the company ledger form.</summary>
    public const string SourceManual = "Manual";

    /// <summary>
    /// One acquisition-cost row per shop (unique when <see cref="ShopId"/> is set).
    /// </summary>
    public const string SourceShopAcquisition = "ShopAcquisition";

    /// <summary>Shop purchase cost. Shop is optional; with a shop the row is the unique acquisition line.</summary>
    public const string CategoryShopAcquisition = "SHOP_ACQUISITION";

    /// <summary>Proxy purchase.</summary>
    public const string CategoryProxy = "PROXY";

    /// <summary>OpenAI top-up.</summary>
    public const string CategoryOpenAiTopup = "OPENAI_TOPUP";

    /// <summary>Cursor subscription / license purchase.</summary>
    public const string CategoryCursorBuy = "CURSOR_BUY";

    /// <summary>Cursor credit top-up.</summary>
    public const string CategoryCursorTopup = "CURSOR_TOPUP";

    /// <summary>Other expense.</summary>
    public const string CategoryOtherExpense = "OTHER_EXPENSE";

    /// <summary>Other income.</summary>
    public const string CategoryOtherIncome = "OTHER_INCOME";

    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Absolute amount (&gt;= 0).</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency code (USD or VND).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary><see cref="DirectionDebit"/> or <see cref="DirectionCredit"/>.</summary>
    public string Direction { get; set; } = DirectionDebit;

    /// <summary>Fixed category code.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>Short label shown in the ledger.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary>When the money movement occurred (business date).</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional shop. Independent of <see cref="OwnerUserId"/>.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Optional seller. Independent of <see cref="ShopId"/> and of <c>Shop.OwnerId</c>.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary><see cref="SourceManual"/> or <see cref="SourceShopAcquisition"/>.</summary>
    public string Source { get; set; } = SourceManual;

    /// <summary>Who created the row.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Shop navigation.</summary>
    public Shop? Shop { get; set; }

    /// <summary>Assigned seller navigation.</summary>
    public ApplicationUser? OwnerUser { get; set; }

    /// <summary>Creator navigation.</summary>
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
