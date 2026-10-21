using Api.Extensions;
using Application.DTOs.Promotion;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin product-discount activities (list local DB + sync from 4Seller).
/// </summary>
[ApiController]
[Route("api/v1/admin/promotions/product-discounts")]
public class AdminProductDiscountsController : ControllerBase
{
    private readonly IProductDiscountSyncService _syncService;
    private readonly IProductDiscountSyncJobRunner _jobRunner;

    /// <summary>Initializes the controller.</summary>
    public AdminProductDiscountsController(
        IProductDiscountSyncService syncService,
        IProductDiscountSyncJobRunner jobRunner)
    {
        _syncService = syncService;
        _jobRunner = jobRunner;
    }

    /// <summary>Lists synced product-discount activities from local DB.</summary>
    [Authorize(Policy = "Permission:admin.promotions.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductDiscountListPageDto>>> List(
        [FromQuery] ProductDiscountListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _syncService.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<ProductDiscountListPageDto>.Ok(result));
    }

    /// <summary>
    /// Queues a 4Seller product-discount sync and returns job id immediately.
    /// Poll <c>sync/status/{jobId}</c> for progress.
    /// </summary>
    [Authorize(Policy = "Permission:admin.promotions.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountSyncJobStatusDto>), StatusCodes.Status202Accepted)]
    public ActionResult<ApiResponse<ProductDiscountSyncJobStatusDto>> Sync(
        [FromBody] ProductDiscountSyncRequest? request)
    {
        var body = request ?? new ProductDiscountSyncRequest();
        var job = _jobRunner.Start(body, User.GetUserId());
        return Accepted(ApiResponse<ProductDiscountSyncJobStatusDto>.Ok(job, "Đã bắt đầu đồng bộ Product Discount."));
    }

    /// <summary>Progress of a queued product-discount sync.</summary>
    [Authorize(Policy = "Permission:admin.promotions.manage")]
    [HttpGet("sync/status/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDiscountSyncJobStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<ProductDiscountSyncJobStatusDto>> SyncStatus(Guid jobId)
    {
        var job = _jobRunner.GetStatus(jobId);
        if (job is null)
        {
            return NotFound(ApiResponse<ProductDiscountSyncJobStatusDto>.Fail(
                "Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        return Ok(ApiResponse<ProductDiscountSyncJobStatusDto>.Ok(job));
    }

    /// <summary>Deactivates an ONGOING product-discount activity on 4Seller.</summary>
    [Authorize(Policy = "Permission:admin.promotions.manage")]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _syncService.DeactivateAsync(
            id,
            restrictOwnerUserId: null,
            cancellationToken: cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã deactivate Product Discount."));
    }
}
