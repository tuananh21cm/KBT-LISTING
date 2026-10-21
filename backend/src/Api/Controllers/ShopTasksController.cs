using Api.Extensions;
using Application.DTOs.Shop;
using Application.DTOs.ShopTask;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Nhiệm vụ hệ thống phía seller: bảng nhiệm vụ theo shop của mình và hai endpoint cho extension.
/// </summary>
[ApiController]
[Route("api/v1/shop-tasks")]
[Authorize]
public class ShopTasksController : ControllerBase
{
    private readonly IShopTaskService _shopTaskService;
    private readonly ITikTokShopHealthRecordService _healthRecords;

    /// <summary>Initializes the controller.</summary>
    public ShopTasksController(
        IShopTaskService shopTaskService,
        ITikTokShopHealthRecordService healthRecords)
    {
        _shopTaskService = shopTaskService;
        _healthRecords = healthRecords;
    }

    /// <summary>Bảng nhiệm vụ của các shop thuộc seller đang đăng nhập.</summary>
    [Authorize(Policy = "Permission:seller.shop_tasks.view")]
    [HttpGet("board")]
    [ProducesResponseType(typeof(ApiResponse<ShopTaskBoardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopTaskBoardDto>>> GetBoard(
        [FromQuery] ShopTaskBoardQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _shopTaskService.GetBoardAsync(User.GetOwnerId(), query, cancellationToken);
        return Ok(ApiResponse<ShopTaskBoardDto>.Ok(result));
    }

    /// <summary>Nhiệm vụ truy cập URL extension cần chạy hôm nay cho một seller id.</summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("extension/daily")]
    [ProducesResponseType(typeof(ApiResponse<ShopTaskExtensionPlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopTaskExtensionPlanDto>>> GetExtensionPlan(
        [FromQuery] string? externalShopId,
        CancellationToken cancellationToken)
    {
        var result = await _shopTaskService.GetExtensionPlanAsync(
            User.GetOwnerId(),
            externalShopId,
            cancellationToken);
        return Ok(ApiResponse<ShopTaskExtensionPlanDto>.Ok(result));
    }

    /// <summary>Extension báo kết quả sau khi mở URL và trang load xong.</summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("extension/visit")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> RecordVisit(
        [FromBody] RecordShopTaskVisitDto request,
        CancellationToken cancellationToken)
    {
        await _shopTaskService.RecordVisitAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã ghi nhận nhiệm vụ."));
    }

    /// <summary>Đánh dấu violation/warning Health Center đã xử lý — ẩn khỏi icon cảnh báo.</summary>
    [Authorize(Policy = "Permission:seller.shop_tasks.view")]
    [HttpPost("health-records/handled")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> MarkHealthRecordHandled(
        [FromBody] MarkShopHealthRecordHandledDto request,
        CancellationToken cancellationToken)
    {
        await _healthRecords.MarkHandledAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã đánh dấu xử lý."));
    }
}
