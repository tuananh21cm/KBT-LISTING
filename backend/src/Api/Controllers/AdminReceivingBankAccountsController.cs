using Application.DTOs.ReceivingBankAccount;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin receiving bank account (thẻ nhận tiền) endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/receiving-bank-accounts")]
public class AdminReceivingBankAccountsController : ControllerBase
{
    private readonly IReceivingBankAccountService _service;
    private readonly IValidator<CreateReceivingBankAccountDto> _createValidator;
    private readonly IValidator<UpdateReceivingBankAccountDto> _updateValidator;
    private readonly IValidator<CreateReceivingSourceAccountDto> _createSourceAccountValidator;
    private readonly IValidator<UpdateReceivingSourceAccountDto> _updateSourceAccountValidator;
    private readonly IValidator<CreateReceivingBankTransactionDto> _createTxnValidator;
    private readonly IValidator<UpdateReceivingBankTransactionDto> _updateTxnValidator;

    /// <summary>Initializes the controller.</summary>
    public AdminReceivingBankAccountsController(
        IReceivingBankAccountService service,
        IValidator<CreateReceivingBankAccountDto> createValidator,
        IValidator<UpdateReceivingBankAccountDto> updateValidator,
        IValidator<CreateReceivingSourceAccountDto> createSourceAccountValidator,
        IValidator<UpdateReceivingSourceAccountDto> updateSourceAccountValidator,
        IValidator<CreateReceivingBankTransactionDto> createTxnValidator,
        IValidator<UpdateReceivingBankTransactionDto> updateTxnValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _createSourceAccountValidator = createSourceAccountValidator;
        _updateSourceAccountValidator = updateSourceAccountValidator;
        _createTxnValidator = createTxnValidator;
        _updateTxnValidator = updateTxnValidator;
    }

    /// <summary>Lists card sources (PingPongX, WorldFirst, …).</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.view,admin.fulfil.view")]
    [HttpGet("sources")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingCardSourceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingCardSourceDto>>>> GetSources(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSourcesAsync(cancellationToken);
        return Ok(ApiResponse<List<ReceivingCardSourceDto>>.Ok(result));
    }

    /// <summary>Lists provider accounts under sources.</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.view,admin.fulfil.view")]
    [HttpGet("source-accounts")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingSourceAccountDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingSourceAccountDto>>>> GetSourceAccounts(
        [FromQuery] Guid? sourceId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSourceAccountsAsync(sourceId, cancellationToken);
        return Ok(ApiResponse<List<ReceivingSourceAccountDto>>.Ok(result));
    }

    /// <summary>Active provider accounts for dropdowns.</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.view,admin.fulfil.view")]
    [HttpGet("source-accounts/options")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingSourceAccountOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingSourceAccountOptionDto>>>> GetSourceAccountOptions(
        [FromQuery] Guid? sourceId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSourceAccountOptionsAsync(sourceId, cancellationToken);
        return Ok(ApiResponse<List<ReceivingSourceAccountOptionDto>>.Ok(result));
    }

    /// <summary>Creates a provider account.</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.manage,admin.fulfil.manage")]
    [HttpPost("source-accounts")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingSourceAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingSourceAccountDto>>> CreateSourceAccount(
        [FromBody] CreateReceivingSourceAccountDto request,
        CancellationToken cancellationToken)
    {
        await _createSourceAccountValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateSourceAccountAsync(request, cancellationToken);
        return Ok(ApiResponse<ReceivingSourceAccountDto>.Ok(result, "Tạo tài khoản nguồn thành công."));
    }

    /// <summary>Updates a provider account.</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.manage,admin.fulfil.manage")]
    [HttpPut("source-accounts/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingSourceAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingSourceAccountDto>>> UpdateSourceAccount(
        Guid id,
        [FromBody] UpdateReceivingSourceAccountDto request,
        CancellationToken cancellationToken)
    {
        await _updateSourceAccountValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateSourceAccountAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ReceivingSourceAccountDto>.Ok(result, "Cập nhật tài khoản nguồn thành công."));
    }

    /// <summary>Deletes a provider account.</summary>
    [Authorize(Policy = "PermissionAny:admin.shops.manage,admin.fulfil.manage")]
    [HttpDelete("source-accounts/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteSourceAccount(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _service.DeleteSourceAccountAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa tài khoản nguồn thành công."));
    }

    /// <summary>Aggregated incoming money (by card / source / source account / shop / day).</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("transactions/stats")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankIncomingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankIncomingSummaryDto>>> GetIncomingStats(
        [FromQuery] Guid? receivingBankAccountId,
        [FromQuery] Guid? sourceId,
        [FromQuery] Guid? sourceAccountId,
        [FromQuery] Guid? shopId,
        [FromQuery] string? txnSource,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetIncomingStatsAsync(
            new ReceivingBankIncomingQuery
            {
                ReceivingBankAccountId = receivingBankAccountId,
                SourceId = sourceId,
                SourceAccountId = sourceAccountId,
                ShopId = shopId,
                TxnSource = txnSource,
                From = from,
                To = to
            },
            cancellationToken);
        return Ok(ApiResponse<ReceivingBankIncomingSummaryDto>.Ok(result));
    }

    /// <summary>Lists receiving-bank ledger rows.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingBankTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingBankTransactionDto>>>> GetTransactions(
        [FromQuery] Guid? receivingBankAccountId,
        [FromQuery] Guid? shopId,
        [FromQuery] Guid? ownerUserId,
        [FromQuery] Guid? orderId,
        [FromQuery] string? source,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetTransactionsAsync(
            new ReceivingBankTransactionQuery
            {
                ReceivingBankAccountId = receivingBankAccountId,
                ShopId = shopId,
                OwnerUserId = ownerUserId,
                OrderId = orderId,
                Source = source,
                From = from,
                To = to
            },
            cancellationToken);
        return Ok(ApiResponse<List<ReceivingBankTransactionDto>>.Ok(result));
    }

    /// <summary>Gets one ledger row.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> GetTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetTransactionByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result));
    }

    /// <summary>Creates a manual ledger row (Confirmed; card derived from shop).</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("transactions")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> CreateTransaction(
        [FromBody] CreateReceivingBankTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _createTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateTransactionAsync(request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Tạo giao dịch thành công."));
    }

    /// <summary>Updates a ledger row.</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> UpdateTransaction(
        Guid id,
        [FromBody] UpdateReceivingBankTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _updateTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateTransactionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Cập nhật giao dịch thành công."));
    }

    /// <summary>Sets confirmation status (Pending / Confirmed / Rejected).</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("transactions/{id:guid}/confirmation-status")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> SetConfirmationStatus(
        Guid id,
        [FromBody] SetReceivingBankConfirmationStatusDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetConfirmationStatusAsync(
            id,
            request.ConfirmationStatus,
            cancellationToken);
        var message = result.ConfirmationStatus switch
        {
            "Rejected" => "Đã chuyển sang Không duyệt.",
            "Pending" => "Đã chuyển sang Chờ duyệt.",
            _ => "Đã duyệt giao dịch."
        };
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, message));
    }

    /// <summary>Approves a Pending/Rejected Manual or TikTokPayout row (counts in incoming stats).</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("transactions/{id:guid}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> ConfirmTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.ConfirmTransactionAsync(id, cancellationToken);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Đã duyệt giao dịch."));
    }

    /// <summary>Rejects a Manual or TikTokPayout row (excluded from incoming totals).</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("transactions/{id:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> RejectTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.RejectTransactionAsync(id, cancellationToken);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Đã chuyển sang Không duyệt."));
    }

    /// <summary>Deletes a ledger row.</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpDelete("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _service.DeleteTransactionAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa giao dịch thành công."));
    }

    /// <summary>Lists receiving bank accounts (slim catalog rows).</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingBankAccountListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingBankAccountListItemDto>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<List<ReceivingBankAccountListItemDto>>.Ok(result));
    }

    /// <summary>Active accounts for shop dropdowns.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingBankAccountOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingBankAccountOptionDto>>>> GetOptions(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<ReceivingBankAccountOptionDto>>.Ok(result));
    }

    /// <summary>Gets one by id.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankAccountDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<ReceivingBankAccountDto>.Ok(result));
    }

    /// <summary>Creates a receiving bank account.</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankAccountDto>>> Create(
        [FromBody] CreateReceivingBankAccountDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<ReceivingBankAccountDto>.Ok(result, "Tạo thẻ nhận tiền thành công."));
    }

    /// <summary>Updates a receiving bank account.</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankAccountDto>>> Update(
        Guid id,
        [FromBody] UpdateReceivingBankAccountDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ReceivingBankAccountDto>.Ok(result, "Cập nhật thẻ nhận tiền thành công."));
    }

    /// <summary>Deletes a receiving bank account.</summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa thẻ nhận tiền thành công."));
    }
}
