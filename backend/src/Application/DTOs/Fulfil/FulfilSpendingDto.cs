namespace Application.DTOs.Fulfil;

/// <summary>Filter for card spending statistics.</summary>
public class FulfilSpendingQuery
{
    /// <summary>Filter by purchase account.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Filter by payment card.</summary>
    public Guid? CardId { get; set; }

    /// <summary>Filter by card provider source.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Filter by provider source account (tài khoản nguồn).</summary>
    public Guid? SourceAccountId { get; set; }

    /// <summary>Filter by order shop (via linked order).</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Filter by order seller / owner (via linked order <c>OwnerId</c>).</summary>
    public Guid? SellerId { get; set; }

    /// <summary>Filter by order handler (người đi đơn / xử lý đơn).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>From (inclusive, UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>To (inclusive, UTC).</summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Which breakdown to compute: <c>card</c> | <c>account</c> | <c>shop</c> | <c>paidBy</c>.
    /// Default <c>card</c>. Other breakdown lists are empty to cut payload / work.
    /// </summary>
    public string? Breakdown { get; set; }
}

/// <summary>Aggregated fulfil card spending for a filtered period.</summary>
public class FulfilSpendingSummaryDto
{
    /// <summary>Net spend (Debit − Credit).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Sum of Debit (trừ tiền) amounts.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>Sum of Credit (nạp tiền) amounts.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>Number of transactions.</summary>
    public int TransactionCount { get; set; }

    /// <summary>Distinct orders linked via CostUpdate / orderId.</summary>
    public int OrderCount { get; set; }

    /// <summary>Distinct cards in the filtered set.</summary>
    public int CardCount { get; set; }

    /// <summary>Distinct purchase accounts (excludes null — card-only txns).</summary>
    public int AccountCount { get; set; }

    /// <summary>Net amount from CostUpdate source.</summary>
    public decimal CostUpdateAmount { get; set; }

    /// <summary>Net amount from Manual source.</summary>
    public decimal ManualAmount { get; set; }

    /// <summary>Breakdown by payment card.</summary>
    public List<FulfilSpendingBreakdownItemDto> ByCard { get; set; } = [];

    /// <summary>Breakdown by purchase account (null bucket = không gắn tài khoản).</summary>
    public List<FulfilSpendingBreakdownItemDto> ByAccount { get; set; } = [];

    /// <summary>Breakdown by shop (from linked order; cards themselves are not shop-bound).</summary>
    public List<FulfilSpendingBreakdownItemDto> ByShop { get; set; } = [];

    /// <summary>Breakdown by order handler (người đi đơn).</summary>
    public List<FulfilSpendingBreakdownItemDto> ByPaidBy { get; set; } = [];

    /// <summary>Daily net spend series (Asia/Ho_Chi_Minh calendar day).</summary>
    public List<FulfilSpendingDayDto> Days { get; set; } = [];
}

/// <summary>One dimension bucket in spending stats.</summary>
public class FulfilSpendingBreakdownItemDto
{
    /// <summary>Entity id (null for unassigned).</summary>
    public Guid? Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional secondary label (e.g. brand · masked · accounts used).</summary>
    public string? Extra { get; set; }

    /// <summary>Net amount (Debit − Credit).</summary>
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

/// <summary>Daily spending point.</summary>
public class FulfilSpendingDayDto
{
    /// <summary>Local VN date as yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Net amount that day (Debit − Credit).</summary>
    public decimal Amount { get; set; }

    /// <summary>Debit sum that day.</summary>
    public decimal DebitAmount { get; set; }

    /// <summary>Credit sum that day.</summary>
    public decimal CreditAmount { get; set; }

    /// <summary>Transaction count that day.</summary>
    public int TransactionCount { get; set; }
}
