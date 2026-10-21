using Application.DTOs.ReceivingBankAccount;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller receiving-bank APIs — always scoped to shops owned by the current user.
/// Manual creates are Pending until an admin confirms. Catalog CRUD remains admin-only.
/// </summary>
[ApiController]
[Route("api/v1/receiving-bank-accounts")]
public class ReceivingBankAccountsController : ControllerBase
{
    private readonly IReceivingBankAccountService _service;
    private readonly IValidator<CreateReceivingBankTransactionDto> _createTxnValidator;
    private readonly IValidator<UpdateReceivingBankTransactionDto> _updateTxnValidator;

    /// <summary>Initializes the seller receiving-bank controller.</summary>
    public ReceivingBankAccountsController(
        IReceivingBankAccountService service,
        IValidator<CreateReceivingBankTransactionDto> createTxnValidator,
        IValidator<UpdateReceivingBankTransactionDto> updateTxnValidator)
    {
        _service = service;
        _createTxnValidator = createTxnValidator;
        _updateTxnValidator = updateTxnValidator;
    }

    /// <summary>
    /// Active cards linked to at least one of the current seller's shops (filter dropdown).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingBankAccountOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingBankAccountOptionDto>>>> GetOptions(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsForOwnerAsync(User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<List<ReceivingBankAccountOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Ledger rows for shops owned by the current seller
    /// (<c>OwnerUserId</c> forced from JWT; manual null-shop rows excluded).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<List<ReceivingBankTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ReceivingBankTransactionDto>>>> GetTransactions(
        [FromQuery] Guid? receivingBankAccountId,
        [FromQuery] Guid? shopId,
        [FromQuery] Guid? orderId,
        [FromQuery] string? source,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetTransactionsAsync(
            new ReceivingBankTransactionQuery
            {
                OwnerUserId = User.GetUserId(),
                ReceivingBankAccountId = receivingBankAccountId,
                ShopId = shopId,
                OrderId = orderId,
                Source = source,
                From = from,
                To = to
            },
            cancellationToken);
        return Ok(ApiResponse<List<ReceivingBankTransactionDto>>.Ok(result));
    }

    /// <summary>
    /// Creates a Credit (tiền về) manual row for one of the seller's shops — status Pending.
    /// Card is derived from <c>Shop.ReceivingBankAccountId</c>.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpPost("transactions")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> CreateTransaction(
        [FromBody] CreateReceivingBankTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _createTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = User.GetUserId();
        request.Direction = "Credit";
        var result = await _service.CreateTransactionAsync(
            request,
            userId,
            cancellationToken,
            pendingConfirmation: true,
            ownerUserId: userId);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Đã gửi giao dịch — chờ Admin xác nhận."));
    }

    /// <summary>Updates own Pending Manual Credit row (shop-scoped).</summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpPut("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReceivingBankTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReceivingBankTransactionDto>>> UpdateTransaction(
        Guid id,
        [FromBody] UpdateReceivingBankTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _updateTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = User.GetUserId();
        request.Direction = "Credit";
        var result = await _service.UpdateTransactionAsync(id, request, cancellationToken, ownerUserId: userId);
        return Ok(ApiResponse<ReceivingBankTransactionDto>.Ok(result, "Cập nhật giao dịch thành công."));
    }

    /// <summary>Deletes own Pending Manual row (shop-scoped).</summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpDelete("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _service.DeleteTransactionAsync(id, cancellationToken, ownerUserId: User.GetUserId());
        return Ok(ApiResponse<object>.Ok(null, "Xóa giao dịch thành công."));
    }
}
