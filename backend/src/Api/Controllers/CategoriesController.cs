using Application.DTOs.Category;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller TikTok category catalog read endpoints (system-wide catalog by site).
/// </summary>
[ApiController]
[Route("api/v1/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    /// <summary>
    /// Initializes the categories controller.
    /// </summary>
    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// Returns the category tree for Cascader / Tree UI.
    /// Prefer <see cref="GetChildren"/> / <see cref="GetPath"/> for listing edit (lazy).
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.view")]
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
    [Authorize(Policy = "Permission:seller.products.view")]
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
    /// Lists categories (flat), optionally filtered by search. <paramref name="take"/> caps
    /// the picker result (max 200); omit for an unbounded list.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.view")]
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
    /// Returns the lazy-cascader seed (option tree pre-expanded to <paramref name="externalId"/>)
    /// so an existing listing's saved category renders without loading the full tree.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.view")]
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
    /// Recommends a TikTok category via 4Seller AI from product title.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost("recommend")]
    [ProducesResponseType(typeof(ApiResponse<RecommendCategoryResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RecommendCategoryResultDto>>> Recommend(
        [FromBody] RecommendCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.RecommendCategoryAsync(
            User.GetUserId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<RecommendCategoryResultDto>.Ok(result, "Đã gợi ý category từ AI."));
    }
}
