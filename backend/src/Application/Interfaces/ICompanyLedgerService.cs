using Application.DTOs.CompanyLedger;

namespace Application.Interfaces;

/// <summary>
/// Company operating income/expense ledger.
/// Shop and seller assignments are independent and both optional.
/// </summary>
public interface ICompanyLedgerService
{
    /// <summary>Fixed categories for the form.</summary>
    IReadOnlyList<CompanyLedgerCategoryDto> GetCategories();

    /// <summary>
    /// Filtered rows plus per-currency totals of the same filter.
    /// When <see cref="CompanyLedgerQuery.SellerScopeUserId"/> is set, only rows assigned to that
    /// seller or to one of their current shops are returned.
    /// </summary>
    Task<CompanyLedgerListDto> ListAsync(
        CompanyLedgerQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a manual row. Shop-acquisition with a shop upserts the unique row for that shop
    /// and applies the seller chosen on the form (does not copy the shop owner).
    /// </summary>
    Task<CompanyLedgerEntryDto> CreateAsync(
        SaveCompanyLedgerEntryDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a row. Clearing the shop on a shop-acquisition row turns it into a manual row.
    /// Moving it onto a shop that already has an acquisition row is rejected.
    /// </summary>
    Task<CompanyLedgerEntryDto> UpdateAsync(
        Guid id,
        SaveCompanyLedgerEntryDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a row.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inline shop purchase cost. New rows copy <c>Shop.OwnerId</c>. Later edits keep the seller
    /// already stored on the row (including a cleared seller). Null or &lt;= 0 deletes the row.
    /// </summary>
    Task<CompanyLedgerEntryDto?> UpsertShopAcquisitionAsync(
        Guid shopId,
        UpsertShopAcquisitionCostDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
