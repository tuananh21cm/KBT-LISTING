namespace Application.DTOs.CompanyLedger;

/// <summary>Fixed category shown in the ledger form.</summary>
public class CompanyLedgerCategoryDto
{
    /// <summary>Stable code stored on the row.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Vietnamese label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Default direction when the user picks this category.</summary>
    public string DefaultDirection { get; set; } = string.Empty;
}

/// <summary>Query for the company ledger list and summary.</summary>
public class CompanyLedgerQuery
{
    /// <summary>Inclusive UTC start on <c>OccurredAt</c>. Null = no lower bound.</summary>
    public DateTime? From { get; set; }

    /// <summary>Inclusive UTC end on <c>OccurredAt</c>. Null = no upper bound.</summary>
    public DateTime? To { get; set; }

    /// <summary><c>Debit</c> or <c>Credit</c>. Null = both.</summary>
    public string? Direction { get; set; }

    /// <summary>Category code. Null = all.</summary>
    public string? CategoryCode { get; set; }

    /// <summary>Filter rows assigned to this seller. Ignored when <see cref="OwnerUnassigned"/> is true.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Only rows with no seller assigned.</summary>
    public bool OwnerUnassigned { get; set; }

    /// <summary>Filter rows assigned to this shop. Ignored when <see cref="ShopUnassigned"/> is true.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Only rows with no shop assigned.</summary>
    public bool ShopUnassigned { get; set; }

    /// <summary>Case-insensitive match on title or note.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Seller portal scope. When set, rows must be assigned to this user or to one of their current shops.
    /// Unassigned company rows are excluded.
    /// </summary>
    public Guid? SellerScopeUserId { get; set; }
}

/// <summary>One ledger row.</summary>
public class CompanyLedgerEntryDto
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Absolute amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary><c>Debit</c> or <c>Credit</c>.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>Category code.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>Short label.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary>Business date (UTC).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Optional shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Optional seller. Independent of the shop owner.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Seller display name.</summary>
    public string? OwnerFullName { get; set; }

    /// <summary>Seller username.</summary>
    public string? OwnerUserName { get; set; }

    /// <summary><c>Manual</c> or <c>ShopAcquisition</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Creator.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Row created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Totals for one currency inside the current filter.</summary>
public class CompanyLedgerCurrencyTotalDto
{
    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Sum of credits.</summary>
    public decimal CreditTotal { get; set; }

    /// <summary>Sum of debits.</summary>
    public decimal DebitTotal { get; set; }

    /// <summary>Credit − debit.</summary>
    public decimal Net { get; set; }
}

/// <summary>KPI for the filtered set, split by currency so mixed USD/VND is not summed.</summary>
public class CompanyLedgerSummaryDto
{
    /// <summary>Number of rows in the filter.</summary>
    public int Count { get; set; }

    /// <summary>Per-currency totals.</summary>
    public List<CompanyLedgerCurrencyTotalDto> ByCurrency { get; set; } = [];
}

/// <summary>List payload: rows plus summary of the same filter.</summary>
public class CompanyLedgerListDto
{
    /// <summary>Rows, newest business date first.</summary>
    public List<CompanyLedgerEntryDto> Items { get; set; } = [];

    /// <summary>Totals for <see cref="Items"/>.</summary>
    public CompanyLedgerSummaryDto Summary { get; set; } = new();
}

/// <summary>Create or replace a manual ledger row. Shop acquisition with a shop upserts the unique row.</summary>
public class SaveCompanyLedgerEntryDto
{
    /// <summary><c>Debit</c> or <c>Credit</c>. Forced to Debit when category is shop acquisition and a shop is set.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>Category code.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    /// <summary>Amount &gt; 0.</summary>
    public decimal Amount { get; set; }

    /// <summary><c>USD</c> or <c>VND</c>.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional short label. Blank is stored as empty.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    /// <summary>Business date (UTC).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Optional shop. Not required to match <see cref="OwnerUserId"/>.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Optional seller. Not copied from the shop owner.</summary>
    public Guid? OwnerUserId { get; set; }
}

/// <summary>
/// Shop finance inline editor. Null or &lt;= 0 deletes the unique acquisition row.
/// </summary>
public class UpsertShopAcquisitionCostDto
{
    /// <summary>Purchase cost. Null or &lt;= 0 removes the row.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Business date. On update, omitted keeps the existing date.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>Note. On update, omitted keeps the existing note.</summary>
    public string? Note { get; set; }
}
