namespace Application.DTOs.ReceivingBankAccount;

/// <summary>Filter for receiving-bank incoming (tiền về) statistics.</summary>
public class ReceivingBankIncomingQuery
{
    /// <summary>Filter by receiving bank card.</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Filter by provider source (PingPongX / WorldFirst / …).</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Filter by provider source account (tài khoản nguồn).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Filter by shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Filter by txn origin (Manual / SettlementPaid).</summary>
    public string? TxnSource { get; set; }

    /// <summary>From (inclusive, UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>To (inclusive, UTC).</summary>
    public DateTime? To { get; set; }
}

/// <summary>Aggregated receiving-bank incoming money for a filtered period.</summary>
public class ReceivingBankIncomingSummaryDto
{
    /// <summary>Net incoming (Credit − Debit).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Sum of Credit (tiền về) amounts.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>Sum of Debit (điều chỉnh trừ) amounts.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>Number of transactions.</summary>
    public int TransactionCount { get; set; }

    /// <summary>Distinct orders linked via SettlementPaid / orderId.</summary>
    public int OrderCount { get; set; }

    /// <summary>Distinct receiving bank cards in the filtered set.</summary>
    public int CardCount { get; set; }

    /// <summary>Distinct provider sources in the filtered set.</summary>
    public int SourceCount { get; set; }

    /// <summary>Net amount from SettlementPaid source.</summary>
    public decimal SettlementPaidAmount { get; set; }

    /// <summary>Net amount from Manual source.</summary>
    public decimal ManualAmount { get; set; }

    /// <summary>Breakdown by receiving bank card.</summary>
    public List<ReceivingBankIncomingBreakdownItemDto> ByCard { get; set; } = [];

    /// <summary>Breakdown by provider source.</summary>
    public List<ReceivingBankIncomingBreakdownItemDto> BySource { get; set; } = [];

    /// <summary>Breakdown by provider source account.</summary>
    public List<ReceivingBankIncomingBreakdownItemDto> BySourceAccount { get; set; } = [];

    /// <summary>Breakdown by shop.</summary>
    public List<ReceivingBankIncomingBreakdownItemDto> ByShop { get; set; } = [];

    /// <summary>Daily net incoming series (Asia/Ho_Chi_Minh calendar day).</summary>
    public List<ReceivingBankIncomingDayDto> Days { get; set; } = [];
}

/// <summary>One dimension bucket in incoming stats.</summary>
public class ReceivingBankIncomingBreakdownItemDto
{
    /// <summary>Entity id (null for unassigned).</summary>
    public Guid? Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional secondary label.</summary>
    public string? Extra { get; set; }

    /// <summary>Net amount (Credit − Debit).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit sum in this bucket.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>Credit sum in this bucket.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>Transaction count.</summary>
    public int TransactionCount { get; set; }

    /// <summary>Distinct linked orders in this bucket.</summary>
    public int OrderCount { get; set; }
}

/// <summary>Daily incoming point.</summary>
public class ReceivingBankIncomingDayDto
{
    /// <summary>Local VN date as yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Net amount that day (Credit − Debit).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit sum that day.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>Credit sum that day.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>Transaction count that day.</summary>
    public int TransactionCount { get; set; }
}
