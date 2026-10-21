using Application.DTOs.Fulfil;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller fulfil transaction APIs — scoped to orders owned by the current user
/// (plus orphan Manual rows created by self). Catalog CRUD remains admin-only.
/// </summary>
[ApiController]
[Route("api/v1/fulfil")]
public class FulfilController : ControllerBase
{
    private readonly IFulfilService _fulfilService;
    private readonly IValidator<CreateFulfilTransactionDto> _createTxnValidator;
    private readonly IValidator<UpdateFulfilTransactionDto> _updateTxnValidator;

    /// <summary>Initializes the seller fulfil controller.</summary>
    public FulfilController(
        IFulfilService fulfilService,
        IValidator<CreateFulfilTransactionDto> createTxnValidator,
        IValidator<UpdateFulfilTransactionDto> updateTxnValidator)
    {
        _fulfilService = fulfilService;
        _createTxnValidator = createTxnValidator;
        _updateTxnValidator = updateTxnValidator;
    }

    /// <summary>Active account options for filter / Manual form.</summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("accounts/options")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilAccountOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilAccountOptionDto>>>> GetAccountOptions(
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetAccountOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<FulfilAccountOptionDto>>.Ok(result));
    }

    /// <summary>Active card options for filter / Manual form.</summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("cards/options")]
    [ProducesResponseType(typeof(ApiResponse<List<FulfilCardOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FulfilCardOptionDto>>>> GetCardOptions(
        [FromQuery] Guid? accountId,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetCardOptionsAsync(accountId, cancellationToken);
        return Ok(ApiResponse<List<FulfilCardOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Ledger rows for orders owned by the current seller
    /// (plus orphan Manual created by self).
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionListPageDto>>> GetTransactions(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? cardId,
        [FromQuery] Guid? sourceId,
        [FromQuery] Guid? sourceAccountId,
        [FromQuery] Guid? orderId,
        [FromQuery] Guid? shopId,
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
                OwnerUserId = User.GetOwnerId(),
                AccountId = accountId,
                CardId = cardId,
                SourceId = sourceId,
                SourceAccountId = sourceAccountId,
                OrderId = orderId,
                ShopId = shopId,
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

    /// <summary>Gets a transaction by id (owner-scoped).</summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> GetTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _fulfilService.GetTransactionByIdAsync(
            id,
            cancellationToken,
            ownerUserId: User.GetOwnerId());
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result));
    }

    /// <summary>Creates a Manual transaction (order must be owned when linked).</summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("transactions")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> CreateTransaction(
        [FromBody] CreateFulfilTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _createTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = User.GetOwnerId();
        var result = await _fulfilService.CreateTransactionAsync(
            request,
            userId,
            cancellationToken,
            ownerUserId: userId);
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result, "Tạo giao dịch thành công."));
    }

    /// <summary>Updates own Manual transaction (CostUpdate blocked).</summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FulfilTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilTransactionDto>>> UpdateTransaction(
        Guid id,
        [FromBody] UpdateFulfilTransactionDto request,
        CancellationToken cancellationToken)
    {
        await _updateTxnValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = User.GetOwnerId();
        var result = await _fulfilService.UpdateTransactionAsync(
            id,
            request,
            cancellationToken,
            ownerUserId: userId,
            allowCostUpdateMutate: false);
        return Ok(ApiResponse<FulfilTransactionDto>.Ok(result, "Cập nhật giao dịch thành công."));
    }

    /// <summary>Deletes own Manual transaction (CostUpdate blocked).</summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpDelete("transactions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTransaction(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _fulfilService.DeleteTransactionAsync(
            id,
            cancellationToken,
            ownerUserId: User.GetOwnerId(),
            allowCostUpdateMutate: false);
        return Ok(ApiResponse<object>.Ok(null, "Xóa giao dịch thành công."));
    }
}
