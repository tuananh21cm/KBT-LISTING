using Application.DTOs.ReceivingBankAccount;

namespace Application.Interfaces;

/// <summary>Receiving bank account (thẻ nhận tiền) management.</summary>
public interface IReceivingBankAccountService
{
    /// <summary>Lists seeded / configured card sources.</summary>
    Task<List<ReceivingCardSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists provider accounts (optionally filter by source).</summary>
    Task<List<ReceivingSourceAccountDto>> GetSourceAccountsAsync(
        Guid? sourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Active provider accounts for dropdowns.</summary>
    Task<List<ReceivingSourceAccountOptionDto>> GetSourceAccountOptionsAsync(
        Guid? sourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a provider account under a source.</summary>
    Task<ReceivingSourceAccountDto> CreateSourceAccountAsync(
        CreateReceivingSourceAccountDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a provider account.</summary>
    Task<ReceivingSourceAccountDto> UpdateSourceAccountAsync(
        Guid id,
        UpdateReceivingSourceAccountDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a provider account (blocked if cards linked).</summary>
    Task DeleteSourceAccountAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists receiving bank accounts (slim rows for catalog table).</summary>
    Task<List<ReceivingBankAccountListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one by id.</summary>
    Task<ReceivingBankAccountDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Active accounts for shop dropdowns.</summary>
    Task<List<ReceivingBankAccountOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Active accounts linked to at least one shop owned by <paramref name="ownerUserId"/>
    /// (seller filter dropdown).
    /// </summary>
    Task<List<ReceivingBankAccountOptionDto>> GetOptionsForOwnerAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an account.</summary>
    Task<ReceivingBankAccountDto> CreateAsync(CreateReceivingBankAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates an account.</summary>
    Task<ReceivingBankAccountDto> UpdateAsync(Guid id, UpdateReceivingBankAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes an account (blocked if shops are linked).</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Aggregated incoming money (by card / source / source account / shop / day).</summary>
    Task<ReceivingBankIncomingSummaryDto> GetIncomingStatsAsync(
        ReceivingBankIncomingQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Lists receiving-bank ledger rows.</summary>
    Task<List<ReceivingBankTransactionDto>> GetTransactionsAsync(
        ReceivingBankTransactionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one ledger row.</summary>
    Task<ReceivingBankTransactionDto> GetTransactionByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a manual ledger row (card derived from shop).</summary>
    /// <param name="pendingConfirmation">When true (seller), status = Pending; otherwise Confirmed.</param>
    /// <param name="ownerUserId">When set, shop must belong to this seller and direction must be Credit.</param>
    Task<ReceivingBankTransactionDto> CreateTransactionAsync(
        CreateReceivingBankTransactionDto dto,
        Guid createdByUserId,
        CancellationToken cancellationToken = default,
        bool pendingConfirmation = false,
        Guid? ownerUserId = null);

    /// <summary>Updates a ledger row (SettlementPaid: limited fields). Seller: only own Pending Manual.</summary>
    Task<ReceivingBankTransactionDto> UpdateTransactionAsync(
        Guid id,
        UpdateReceivingBankTransactionDto dto,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null);

    /// <summary>Deletes a ledger row; clears order settlement flag when Source=SettlementPaid. Seller: only own Pending Manual.</summary>
    Task DeleteTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null);

    /// <summary>Admin sets confirmation: Pending, Confirmed, or Rejected (Manual / TikTokPayout).</summary>
    Task<ReceivingBankTransactionDto> SetConfirmationStatusAsync(
        Guid id,
        string confirmationStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Admin approves a Pending/Rejected Manual or TikTokPayout row so it counts in incoming stats.</summary>
    Task<ReceivingBankTransactionDto> ConfirmTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Admin rejects a Manual or TikTokPayout row (excluded from incoming totals).</summary>
    Task<ReceivingBankTransactionDto> RejectTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
