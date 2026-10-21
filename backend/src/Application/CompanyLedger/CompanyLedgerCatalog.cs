using Application.DTOs.CompanyLedger;
using Domain.Entities;

namespace Application.CompanyLedger;

/// <summary>
/// Fixed category catalog for the company ledger. Not a database table.
/// </summary>
public static class CompanyLedgerCatalog
{
    /// <summary>All categories, stable order.</summary>
    public static readonly IReadOnlyList<CompanyLedgerCategoryDto> All =
    [
        new() { Code = CompanyLedgerEntry.CategoryShopAcquisition, Name = "Tiền mua shop", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryProxy, Name = "Mua proxy", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryOpenAiTopup, Name = "Nạp tiền OpenAI", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryCursorBuy, Name = "Mua Cursor", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryCursorTopup, Name = "Nạp tiền Cursor", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryOtherExpense, Name = "Chi khác", DefaultDirection = CompanyLedgerEntry.DirectionDebit },
        new() { Code = CompanyLedgerEntry.CategoryOtherIncome, Name = "Thu khác", DefaultDirection = CompanyLedgerEntry.DirectionCredit }
    ];

    /// <summary>Whether <paramref name="code"/> is a known category.</summary>
    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code) && All.Any(c => c.Code == code);

    /// <summary>USD or VND.</summary>
    public static bool IsCurrency(string? currency) =>
        currency is "USD" or "VND";

    /// <summary>Debit or Credit.</summary>
    public static bool IsDirection(string? direction) =>
        direction is CompanyLedgerEntry.DirectionDebit or CompanyLedgerEntry.DirectionCredit;
}
