using Api.Extensions;
using Application.DTOs.Promotion;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller product-discount activities (own shops only).
/// </summary>
[ApiController]
[Route("api/v1/promotions/product-discounts")]
[Authorize]
public class ProductDiscountsController : ControllerBase
{
    private readonly IProductDiscountSyncService _syncService;
    private readonly IProductDiscountSyncJobRunner _jobRunner;

    /// <summary>Initializes the controller.</summary>
    public ProductDiscountsController(
        IProductDiscountSyncService syncService,
        IProductDiscountSyncJobRunner jobRunner)
    {
        _syncService = syncService;
        _jobRunner = jobRunner;
    }

    /// <summary>Lists own product-discount activities from local DB.</summary>
    [Authorize(Policy = "Permission:seller.promotions.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductDiscountListPageDto>>> List(
        [FromQuery] ProductDiscountListQuery query,
        CancellationToken cancellationToken)
    {
        query.OwnerUserId = User.GetOwnerId();
        var result = await _syncService.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<ProductDiscountListPageDto>.Ok(result));
    }

    /// <summary>Queues sync for the current seller only.</summary>
    [Authorize(Policy = "Permission:seller.promotions.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountSyncJobStatusDto>), StatusCodes.Status202Accepted)]
    public ActionResult<ApiResponse<ProductDiscountSyncJobStatusDto>> Sync()
    {
        var body = new ProductDiscountSyncRequest { OwnerUserId = User.GetOwnerId() };
        var job = _jobRunner.Start(body, User.GetOwnerId());
        return Accepted(ApiResponse<ProductDiscountSyncJobStatusDto>.Ok(job, "Đã bắt đầu đồng bộ Product Discount."));
    }

    /// <summary>Progress of own sync job.</summary>
    [Authorize(Policy = "Permission:seller.promotions.manage")]
    [HttpGet("sync/status/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountSyncJobStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<ProductDiscountSyncJobStatusDto>> SyncStatus(Guid jobId)
    {
        var startedBy = _jobRunner.GetStartedByUserId(jobId);
        if (startedBy is null || startedBy.Value != User.GetOwnerId())
        {
            return NotFound(ApiResponse<ProductDiscountSyncJobStatusDto>.Fail(
                "Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        var job = _jobRunner.GetStatus(jobId);
        if (job is null)
        {
            return NotFound(ApiResponse<ProductDiscountSyncJobStatusDto>.Fail(
                "Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        return Ok(ApiResponse<ProductDiscountSyncJobStatusDto>.Ok(job));
    }

    /// <summary>Deactivates an own ONGOING product-discount activity on 4Seller.</summary>
    [Authorize(Policy = "Permission:seller.promotions.manage")]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _syncService.DeactivateAsync(
            id,
            restrictOwnerUserId: User.GetOwnerId(),
            cancellationToken: cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã deactivate Product Discount."));
    }
}
