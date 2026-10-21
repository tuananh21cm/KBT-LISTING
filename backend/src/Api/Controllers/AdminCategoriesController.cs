using Application.DTOs.Category;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin TikTok category catalog endpoints (system-wide catalog by site).
/// </summary>
[ApiController]
[Route("api/v1/admin/categories")]
public class AdminCategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    /// <summary>
    /// Initializes the admin categories controller.
    /// </summary>
    public AdminCategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// Syncs the system-wide TikTok category tree from 4Seller for a marketplace site.
    /// Uses any account cookie with status <c>logged_in</c> (not necessarily the current admin JWT).
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<CategorySyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CategorySyncResultDto>>> Sync(
        [FromBody] SyncCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.SyncFromFourSellerAsync(
            User.GetUserId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<CategorySyncResultDto>.Ok(
            result,
            $"Đồng bộ category hệ thống ({result.Site}) thành công: {result.SyncedCount} node (mới {result.CreatedCount}, cập nhật {result.UpdatedCount}, ẩn {result.DeactivatedCount})."));
    }

    /// <summary>
    /// Returns the category tree for Cascader / Tree UI.
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.view")]
    [HttpGet("tree")]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryTreeNodeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<CategoryTreeNodeDto>>>> GetTree(
        [FromQuery] string site = "US",
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.GetTreeAsync(site, activeOnly, cancellationToken);
        return Ok(ApiResponse<List<CategoryTreeNodeDto>>.Ok(result));
    }

    /// <summary>
    /// Returns only the direct children of <paramref name="parentId"/> (roots when empty)
    /// for lazy Cascader loading — avoids shipping the whole category tree per site.
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.view")]
    [HttpGet("children")]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryTreeNodeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<CategoryTreeNodeDto>>>> GetChildren(
        [FromQuery] string site = "US",
        [FromQuery] string? parentId = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.GetChildrenAsync(site, parentId, activeOnly, cancellationToken);
        return Ok(ApiResponse<List<CategoryTreeNodeDto>>.Ok(result));
    }

    /// <summary>
    /// Returns the lazy-cascader seed (option tree pre-expanded to <paramref name="externalId"/>)
    /// so an existing listing's saved category renders without loading the full tree.
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.view")]
    [HttpGet("path")]
    [ProducesResponseType(typeof(ApiResponse<CategoryPathDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CategoryPathDto>>> GetPath(
        [FromQuery] string site = "US",
        [FromQuery] string externalId = "",
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.GetPathAsync(site, externalId, activeOnly, cancellationToken);
        return Ok(ApiResponse<CategoryPathDto>.Ok(result));
    }

    /// <summary>
    /// Lists categories (flat), optionally filtered by search. Query <c>take</c> caps
    /// the picker result (max 200); omit for the admin table.
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<CategoryDto>>>> GetList(
        [FromQuery] string site = "US",
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.GetListAsync(site, search, activeOnly, take, cancellationToken);
        return Ok(ApiResponse<List<CategoryDto>>.Ok(result));
    }

    /// <summary>
    /// Recommends a TikTok category via 4Seller AI from product title.
    /// </summary>
    [Authorize(Policy = "Permission:admin.categories.view")]
    [HttpPost("recommend")]
    [ProducesResponseType(typeof(ApiResponse<RecommendCategoryResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RecommendCategoryResultDto>>> Recommend(
        [FromBody] RecommendCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.RecommendCategoryAsync(
            User.GetUserId(),
            request,
            cancellationToken,
            allowOwnerOverride: true);
        return Ok(ApiResponse<RecommendCategoryResultDto>.Ok(result, "Đã gợi ý category từ AI."));
    }
}
