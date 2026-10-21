using Application.DTOs.Fulfil;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin fulfil accounts, cards, and transaction history.
/// </summary>
[ApiController]
[Route("api/v1/admin/fulfil")]
public class AdminFulfilController : ControllerBase
{
    private readonly IFulfilService _fulfilService;
    private readonly IValidator<CreateFulfilAccountDto> _createAccountValidator;
    private readonly IValidator<UpdateFulfilAccountDto> _updateAccountValidator;
    private readonly IValidator<CreateFulfilCardDto> _createCardValidator;
    private readonly IValidator<UpdateFulfilCardDto> _updateCardValidator;
    private readonly IValidator<CreateFulfilTransactionDto> _createTxnValidator;
    private readonly IValidator<UpdateFulfilTransactionDto> _updateTxnValidator;
    private readonly IValidator<SaveSheinSessionDto> _sheinSessionValidator;

    /// <summary>Initializes the controller.</summary>
    public AdminFulfilController(
        IFulfilService fulfilService,
        IValidator<CreateFulfilAccountDto> createAccountValidator,
        IValidator<UpdateFulfilAccountDto> updateAccountValidator,
        IValidator<CreateFulfilCardDto> createCardValidator,
        IValidator<UpdateFulfilCardDto> updateCardValidator,
        IValidator<CreateFulfilTransactionDto> createTxnValidator,
        IValidator<UpdateFulfilTransactionDto> updateTxnValidator,
        IValidator<SaveSheinSessionDto> sheinSessionValidator)
    {
        _fulfilService = fulfilService;
        _createAccountValidator = createAccountValidator;
        _updateAccountValidator = updateAccountValidator;
        _createCardValidator = createCardValidator;
        _updateCardValidator = updateCardValidator;
        _createTxnValidator = createTxnValidator;
        _updateTxnValidator = updateTxnValidator;
        _sheinSessionValidator = sheinSessionValidator;
    }

    // ── Accounts ──────────────────────────────────────────────────────────

    /// <summary>Lists fulfil accounts.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("accounts")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilAccountDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilAccountDto>>>> GetAccounts(CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetAccountsAsync(cancellationToken);
        return Ok(ApiResponse<List<FulfilAccountDto>>.Ok(result));
    }

    /// <summary>Account options for dropdowns.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("accounts/options")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilAccountOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilAccountOptionDto>>>> GetAccountOptions(CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetAccountOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<FulfilAccountOptionDto>>.Ok(result));
    }

    /// <summary>Gets an account by id.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("accounts/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilAccountDto>>> GetAccount(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetAccountByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<FulfilAccountDto>.Ok(result));
    }

    /// <summary>Creates an account.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPost("accounts")]
    [ProducesResponseType(typeof(ApiResponse<FulfilAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilAccountDto>>> CreateAccount(
        [FromBody] CreateFulfilAccountDto request,
        CancellationToken cancellationToken)
    {
        await _createAccountValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.CreateAccountAsync(request, cancellationToken);
        return Ok(ApiResponse<FulfilAccountDto>.Ok(result, "Tạo tài khoản fulfil thành công."));
    }

    /// <summary>Updates an account.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPut("accounts/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilAccountDto>>> UpdateAccount(
        Guid id,
        [FromBody] UpdateFulfilAccountDto request,
        CancellationToken cancellationToken)
    {
        await _updateAccountValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.UpdateAccountAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FulfilAccountDto>.Ok(result, "Cập nhật tài khoản fulfil thành công."));
    }

    /// <summary>Deletes an account.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpDelete("accounts/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAccount(Guid id, CancellationToken cancellationToken)
    {
        await _fulfilService.DeleteAccountAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa tài khoản fulfil thành công."));
    }

    /// <summary>Sets the active card on an account.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPut("accounts/{id:guid}/active-card")]
    [ProducesResponseType(typeof(ApiResponse<FulfilAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilAccountDto>>> SetActiveCard(
        Guid id,
        [FromBody] SetActiveFulfilCardDto request,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.SetActiveCardAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FulfilAccountDto>.Ok(result, "Cập nhật thẻ đang dùng thành công."));
    }

    /// <summary>
    /// Stores the SHEIN session (security headers + cookie) captured by the fulfil extension on
    /// the account owning that SHEIN email, so the backend can sync orders without a browser.
    /// Guarded by the same permission as the extension's order sync push.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("accounts/shein-session")]
    [ProducesResponseType(typeof(ApiResponse<SheinSessionSavedDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinSessionSavedDto>>> SaveSheinSession(
        [FromBody] SaveSheinSessionDto request,
        CancellationToken cancellationToken)
    {
        await _sheinSessionValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.SaveSheinSessionAsync(request, cancellationToken);
        return Ok(ApiResponse<SheinSessionSavedDto>.Ok(
            result,
            $"Đã lưu session SHEIN cho tài khoản {result.AccountName}."));
    }

    /// <summary>
    /// Reads the full stored SHEIN session for one account, raw cookie and headers included.
    /// Separate from <c>GET accounts</c> on purpose: that list loads on every page visit and the
    /// cookie alone runs to ~16 KB per account.
    /// </summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpGet("accounts/{id:guid}/shein-session")]
    [ProducesResponseType(typeof(ApiResponse<SheinSessionDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinSessionDetailDto>>> GetSheinSession(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetSheinSessionAsync(id, cancellationToken);
        return Ok(ApiResponse<SheinSessionDetailDto>.Ok(result));
    }

    /// <summary>
    /// Overwrites the stored SHEIN session by hand. Headers accept a dictionary or a pasted
    /// <c>name: value</c> block; sending both headers and cookie empty clears the session.
    /// </summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPut("accounts/{id:guid}/shein-session")]
    [ProducesResponseType(typeof(ApiResponse<SheinSessionSavedDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinSessionSavedDto>>> SaveSheinSessionManual(
        Guid id,
        [FromBody] SaveSheinSessionManualDto request,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.SaveSheinSessionManualAsync(
            id, request ?? new SaveSheinSessionManualDto(), cancellationToken);
        return Ok(ApiResponse<SheinSessionSavedDto>.Ok(
            result,
            result.Ready
                ? $"Đã lưu session SHEIN cho tài khoản {result.AccountName}."
                : $"Đã xoá session SHEIN của tài khoản {result.AccountName}."));
    }

    /// <summary>
    /// Probes the stored session against SHEIN with the account's newest billno (or the list page)
    /// and records the outcome, so a proxy/UA mismatch shows up before the sweep runs.
    /// </summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPost("accounts/{id:guid}/shein-session/test")]
    [ProducesResponseType(typeof(ApiResponse<TestSheinSessionResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TestSheinSessionResultDto>>> TestSheinSession(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.TestSheinSessionAsync(id, cancellationToken);
        return Ok(ApiResponse<TestSheinSessionResultDto>.Ok(
            result,
            result.Ok
                ? $"Session còn dùng được ({result.ElapsedMs} ms)."
                : result.Error ?? "Session không dùng được."));
    }

    // ── Cards ─────────────────────────────────────────────────────────────

    /// <summary>Lists payment cards.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("cards")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilCardDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilCardDto>>>> GetCards(
        [FromQuery] Guid? accountId,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetCardsAsync(accountId, cancellationToken);
        return Ok(ApiResponse<List<FulfilCardDto>>.Ok(result));
    }

    /// <summary>Card options for dropdowns.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("cards/options")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilCardOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilCardOptionDto>>>> GetCardOptions(
        [FromQuery] Guid? accountId,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetCardOptionsAsync(accountId, cancellationToken);
        return Ok(ApiResponse<List<FulfilCardOptionDto>>.Ok(result));
    }

    /// <summary>Gets a card by id.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("cards/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilCardDto>>> GetCard(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetCardByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<FulfilCardDto>.Ok(result));
    }

    /// <summary>Creates a card.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPost("cards")]
    [ProducesResponseType(typeof(ApiResponse<FulfilCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilCardDto>>> CreateCard(
        [FromBody] CreateFulfilCardDto request,
        CancellationToken cancellationToken)
    {
        await _createCardValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.CreateCardAsync(request, cancellationToken);
        return Ok(ApiResponse<FulfilCardDto>.Ok(result, "Tạo thẻ thanh toán thành công."));
    }

    /// <summary>Updates a card.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPut("cards/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilCardDto>>> UpdateCard(
        Guid id,
        [FromBody] UpdateFulfilCardDto request,
        CancellationToken cancellationToken)
    {
        await _updateCardValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.UpdateCardAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FulfilCardDto>.Ok(result, "Cập nhật thẻ thanh toán thành công."));
    }

    /// <summary>Deletes a card.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpDelete("cards/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteCard(Guid id, CancellationToken cancellationToken)
    {
        await _fulfilService.DeleteCardAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa thẻ thanh toán thành công."));
    }

    // ── Transactions ──────────────────────────────────────────────────────

    /// <summary>Aggregated card spending (KPI + days + one breakdown dimension).</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("transactions/stats")]
    [ProducesResponseType(typeof(ApiResponse<FulfilSpendingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilSpendingSummaryDto>>> GetSpendingStats(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? cardId,
        [FromQuery] Guid? sourceId,
        [FromQuery] Guid? sourceAccountId,
        [FromQuery] Guid? shopId,
        [FromQuery] Guid? sellerId,
        [FromQuery] Guid? paidByUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? breakdown,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetSpendingStatsAsync(
            new FulfilSpendingQuery
            {
                AccountId = accountId,
                CardId = cardId,
                SourceId = sourceId,
                SourceAccountId = sourceAccountId,
                ShopId = shopId,
                SellerId = sellerId,
                PaidByUserId = paidByUserId,
                From = from,
                To = to,
                Breakdown = breakdown
            },
            cancellationToken);
        return Ok(ApiResponse<FulfilSpendingSummaryDto>.Ok(result));
    }

    /// <summary>Lists transactions (paged).</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionListPageDto>>> GetTransactions(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? cardId,
        [FromQuery] Guid? sourceId,
        [FromQuery] Guid? sourceAccountId,
        [FromQuery] Guid? orderId,
        [FromQuery] Guid? shopId,
        [FromQuery] Guid? sellerId,
        [FromQuery] Guid? paidByUserId,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int pageCurrent = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _fulfilService.GetTransactionsAsync(
            new FulfilTransactionQuery
            {
                AccountId = accountId,
                CardId = cardId,
                SourceId = sourceId,
                SourceAccountId = sourceAccountId,
                OrderId = orderId,
                ShopId = shopId,
                SellerId = sellerId,
                PaidByUserId = paidByUserId,
                Keyword = keyword,
                From = from,
                To = to,
                PageCurrent = pageCurrent,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(ApiResponse<FulfilTransactionListPageDto>.Ok(result));
    }

    /// <summary>Gets a transaction by id.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.view")]
    [HttpGet("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> GetTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetTransactionByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result));
    }

    /// <summary>Creates a manual transaction.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPost("transactions")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> CreateTransaction(
        [FromBody] CreateFulfilTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _createTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.CreateTransactionAsync(request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result, "Tạo giao dịch thành công."));
    }

    /// <summary>Updates a transaction.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPut("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> UpdateTransaction(
        Guid id,
        [FromBody] UpdateFulfilTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _updateTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fulfilService.UpdateTransactionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result, "Cập nhật giao dịch thành công."));
    }

    /// <summary>Deletes a transaction (Manual or CostUpdate).</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpDelete("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTransaction(Guid id, CancellationToken cancellationToken)
    {
        await _fulfilService.DeleteTransactionAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa giao dịch thành công."));
    }

    /// <summary>Deletes multiple transactions.</summary>
    [Authorize(Policy = "Permission:admin.fulfil.manage")]
    [HttpPost("transactions/bulk-delete")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> BulkDeleteTransactions(
        [FromBody] BulkDeleteFulfilTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var deleted = await _fulfilService.BulkDeleteTransactionsAsync(request.Ids, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted }, $"Đã xóa {deleted} giao dịch."));
    }
}
