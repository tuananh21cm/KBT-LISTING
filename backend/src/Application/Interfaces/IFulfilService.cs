using Application.DTOs.Fulfil;

namespace Application.Interfaces;

/// <summary>Admin fulfil accounts / cards / transactions.</summary>
public interface IFulfilService
{
    /// <summary>List all accounts.</summary>
    Task<List<FulfilAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get account by id.</summary>
    Task<FulfilAccountDto> GetAccountByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Active account options for selects.</summary>
    Task<List<FulfilAccountOptionDto>> GetAccountOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Create account.</summary>
    Task<FulfilAccountDto> CreateAccountAsync(CreateFulfilAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Update account.</summary>
    Task<FulfilAccountDto> UpdateAccountAsync(Guid id, UpdateFulfilAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Delete account.</summary>
    Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Set the single active card on an account.</summary>
    Task<FulfilAccountDto> SetActiveCardAsync(Guid accountId, SetActiveFulfilCardDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the SHEIN session captured by the fulfil extension on the matching account,
    /// so the backend can replay it later without a browser.
    /// </summary>
    Task<SheinSessionSavedDto> SaveSheinSessionAsync(SaveSheinSessionDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the full stored SHEIN session (raw cookie + headers) for one account. Separate from
    /// the accounts list so the ~16 KB cookie is only fetched when an operator asks to see it.
    /// </summary>
    Task<SheinSessionDetailDto> GetSheinSessionAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the stored session by hand. Empty cookie and headers clears it, so a known-bad
    /// session can be removed instead of leaving the sweep to keep failing on it.
    /// </summary>
    Task<SheinSessionSavedDto> SaveSheinSessionManualAsync(
        Guid accountId,
        SaveSheinSessionManualDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes the stored session against SHEIN (newest billno of the account, else the list page)
    /// and records the outcome on the account.
    /// </summary>
    Task<TestSheinSessionResultDto> TestSheinSessionAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>List cards.</summary>
    Task<List<FulfilCardDto>> GetCardsAsync(Guid? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Get card by id.</summary>
    Task<FulfilCardDto> GetCardByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Card options for selects.</summary>
    Task<List<FulfilCardOptionDto>> GetCardOptionsAsync(Guid? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Create card.</summary>
    Task<FulfilCardDto> CreateCardAsync(CreateFulfilCardDto dto, CancellationToken cancellationToken = default);

    /// <summary>Update card.</summary>
    Task<FulfilCardDto> UpdateCardAsync(Guid id, UpdateFulfilCardDto dto, CancellationToken cancellationToken = default);

    /// <summary>Delete card.</summary>
    Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>List transactions (paged).</summary>
    Task<FulfilTransactionListPageDto> GetTransactionsAsync(FulfilTransactionQuery query, CancellationToken cancellationToken = default);

    /// <summary>Aggregated card spending for stats (filter by account / card / shop / paidBy / date).</summary>
    Task<FulfilSpendingSummaryDto> GetSpendingStatsAsync(FulfilSpendingQuery query, CancellationToken cancellationToken = default);

    /// <summary>Get transaction by id (optional seller owner scope).</summary>
    Task<FulfilTransactionDto> GetTransactionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null);

    /// <summary>Create manual transaction (optional seller owner scope on linked order).</summary>
    Task<FulfilTransactionDto> CreateTransactionAsync(
        CreateFulfilTransactionDto dto,
        Guid createdByUserId,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null);

    /// <summary>Update transaction (manual fully; CostUpdate amount/note/paidBy only).</summary>
    Task<FulfilTransactionDto> UpdateTransactionAsync(
        Guid id,
        UpdateFulfilTransactionDto dto,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null,
        bool allowCostUpdateMutate = true);

    /// <summary>Delete a transaction (Manual or CostUpdate; CostUpdate clears order fulfil fields).</summary>
    Task DeleteTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null,
        bool allowCostUpdateMutate = true);

    /// <summary>Delete multiple transactions; CostUpdate rows clear order fulfil fields.</summary>
    Task<int> BulkDeleteTransactionsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}
