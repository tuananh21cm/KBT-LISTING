using Application.DTOs.Shop;
using Application.DTOs.ShopTask;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Nhiệm vụ hệ thống cho shop — bảng nhiệm vụ toàn hệ thống và cấu hình nhiệm vụ truy cập URL.
/// </summary>
[ApiController]
[Route("api/v1/admin/shop-tasks")]
public class AdminShopTasksController : ControllerBase
{
    private readonly IShopTaskService _shopTaskService;
    private readonly ITikTokShopHealthRecordService _healthRecords;
    private readonly IValidator<SaveShopTaskDefinitionDto> _definitionValidator;
    private readonly IValidator<ReorderShopTaskDefinitionsDto> _reorderValidator;

    /// <summary>Initializes the controller.</summary>
    public AdminShopTasksController(
        IShopTaskService shopTaskService,
        ITikTokShopHealthRecordService healthRecords,
        IValidator<SaveShopTaskDefinitionDto> definitionValidator,
        IValidator<ReorderShopTaskDefinitionsDto> reorderValidator)
    {
        _shopTaskService = shopTaskService;
        _healthRecords = healthRecords;
        _definitionValidator = definitionValidator;
        _reorderValidator = reorderValidator;
    }

    /// <summary>Bảng nhiệm vụ của mọi shop Active trong một ngày VN.</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.view")]
    [HttpGet("board")]
    [ProducesResponseType(typeof(ApiResponse<ShopTaskBoardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopTaskBoardDto>>> GetBoard(
        [FromQuery] ShopTaskBoardQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _shopTaskService.GetBoardAsync(null, query, cancellationToken);
        return Ok(ApiResponse<ShopTaskBoardDto>.Ok(result));
    }

    /// <summary>Danh sách nhiệm vụ truy cập URL (gồm cả đang tắt).</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.view")]
    [HttpGet("definitions")]
    [ProducesResponseType(typeof(ApiResponse<List<ShopTaskDefinitionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ShopTaskDefinitionDto>>>> GetDefinitions(
        CancellationToken cancellationToken)
    {
        var result = await _shopTaskService.ListDefinitionsAsync(cancellationToken);
        return Ok(ApiResponse<List<ShopTaskDefinitionDto>>.Ok(result));
    }

    /// <summary>Tạo nhiệm vụ truy cập URL.</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.manage")]
    [HttpPost("definitions")]
    [ProducesResponseType(typeof(ApiResponse<ShopTaskDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopTaskDefinitionDto>>> CreateDefinition(
        [FromBody] SaveShopTaskDefinitionDto request,
        CancellationToken cancellationToken)
    {
        await _definitionValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopTaskService.CreateDefinitionAsync(request, cancellationToken);
        return Ok(ApiResponse<ShopTaskDefinitionDto>.Ok(result, "Đã tạo nhiệm vụ."));
    }

    /// <summary>Cập nhật nhiệm vụ truy cập URL.</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.manage")]
    [HttpPut("definitions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ShopTaskDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopTaskDefinitionDto>>> UpdateDefinition(
        Guid id,
        [FromBody] SaveShopTaskDefinitionDto request,
        CancellationToken cancellationToken)
    {
        await _definitionValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopTaskService.UpdateDefinitionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ShopTaskDefinitionDto>.Ok(result, "Đã cập nhật nhiệm vụ."));
    }

    /// <summary>Sắp xếp lại thứ tự nhiệm vụ truy cập URL (body phải gồm mọi id hiện có).</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.manage")]
    [HttpPut("definitions/reorder")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> ReorderDefinitions(
        [FromBody] ReorderShopTaskDefinitionsDto request,
        CancellationToken cancellationToken)
    {
        await _reorderValidator.ValidateAndThrowAsync(request, cancellationToken);
        await _shopTaskService.ReorderDefinitionsAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã cập nhật thứ tự nhiệm vụ."));
    }

    /// <summary>Xoá nhiệm vụ truy cập URL cùng lịch sử thực hiện.</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.manage")]
    [HttpDelete("definitions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteDefinition(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _shopTaskService.DeleteDefinitionAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã xoá nhiệm vụ."));
    }

    /// <summary>Admin đánh dấu violation/warning Health Center đã xử lý.</summary>
    [Authorize(Policy = "Permission:admin.shop_tasks.view")]
    [HttpPost("health-records/handled")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> MarkHealthRecordHandled(
        [FromBody] MarkShopHealthRecordHandledDto request,
        CancellationToken cancellationToken)
    {
        await _healthRecords.MarkHandledAsync(null, request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã đánh dấu xử lý."));
    }
}
