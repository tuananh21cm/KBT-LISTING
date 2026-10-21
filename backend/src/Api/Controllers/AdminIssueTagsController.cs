using Application.DTOs.Order;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>Admin issue-tag library + order issue workflow.</summary>
[ApiController]
[Route("api/v1/admin")]
public class AdminIssueTagsController : ControllerBase
{
    private readonly IOrderIssueTagService _issueTags;
    private readonly IOrderSyncService _orders;

    /// <summary>Creates the controller.</summary>
    public AdminIssueTagsController(IOrderIssueTagService issueTags, IOrderSyncService orders)
    {
        _issueTags = issueTags;
        _orders = orders;
    }

    /// <summary>Lists the reusable issue-tag library.</summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("issue-tags")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<IssueTagDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IssueTagDto>>>> ListLibrary(
        CancellationToken cancellationToken)
    {
        var result = await _issueTags.ListLibraryAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<IssueTagDto>>.Ok(result));
    }

    /// <summary>Creates a custom library tag (or returns existing CI match).</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("issue-tags")]
    [ProducesResponseType(typeof(ApiResponse<IssueTagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IssueTagDto>>> CreateLibraryTag(
        [FromBody] CreateIssueTagRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _issueTags.CreateLibraryTagAsync(
            request ?? new CreateIssueTagRequest(),
            User.GetUserId(),
            cancellationToken);
        return Ok(ApiResponse<IssueTagDto>.Ok(result, "Đã thêm tag."));
    }

    /// <summary>Renames a library tag.</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("issue-tags/{tagId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IssueTagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IssueTagDto>>> UpdateLibraryTag(
        Guid tagId,
        [FromBody] UpdateIssueTagRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _issueTags.UpdateLibraryTagAsync(
            tagId,
            request ?? new UpdateIssueTagRequest(),
            cancellationToken);
        return Ok(ApiResponse<IssueTagDto>.Ok(result, "Đã cập nhật nhãn."));
    }

    /// <summary>Deletes a custom library tag (system tags blocked).</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete("issue-tags/{tagId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteLibraryTag(
        Guid tagId,
        CancellationToken cancellationToken)
    {
        await _issueTags.DeleteLibraryTagAsync(tagId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã xóa nhãn."));
    }

    /// <summary>Replaces issue tags on an order.</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("orders/{id:guid}/issue-tags")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> SetOrderTags(
        Guid id,
        [FromBody] SetOrderIssueTagsRequest request,
        CancellationToken cancellationToken)
    {
        await _issueTags.SetOrderTagsAsync(
            id,
            request ?? new SetOrderIssueTagsRequest(),
            User.GetUserId(),
            cancellationToken);
        var detail = await _orders.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(detail, "Đã cập nhật tag lỗi."));
    }

    /// <summary>Sets order issue status (Processing / Done).</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("orders/{id:guid}/issue-status")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> SetOrderStatus(
        Guid id,
        [FromBody] SetOrderIssueStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _issueTags.SetOrderStatusAsync(
            id,
            request ?? new SetOrderIssueStatusRequest(),
            User.GetUserId(),
            cancellationToken);
        var detail = await _orders.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(detail, "Đã cập nhật trạng thái lỗi."));
    }
}
