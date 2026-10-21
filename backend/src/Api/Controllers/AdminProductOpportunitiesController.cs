using Api.Extensions;
using Application.DTOs.ProductOpportunity;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>Admin paged list of TikTok Product opportunities (all shops).</summary>
[ApiController]
[Route("api/v1/admin/product-opportunities")]
public class AdminProductOpportunitiesController : ControllerBase
{
    private readonly IProductOpportunityService _service;
    private readonly IShopKeywordResearchService _research;

    /// <summary>Creates the controller.</summary>
    public AdminProductOpportunitiesController(
        IProductOpportunityService service,
        IShopKeywordResearchService research)
    {
        _service = service;
        _research = research;
    }

    /// <summary>Paged snapshot list (optional shop / owner / keyword / tag filters).</summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ProductOpportunityListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductOpportunityListPageDto>>> List(
        [FromQuery] ProductOpportunityListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<ProductOpportunityListPageDto>.Ok(result));
    }

    /// <summary>Deletes selected keywords across shops.</summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpPost("bulk-delete")]
    [ProducesResponseType(typeof(ApiResponse<BulkDeleteProductOpportunitiesResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<BulkDeleteProductOpportunitiesResultDto>>> BulkDelete(
        [FromBody] BulkDeleteProductOpportunitiesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.BulkDeleteAsync(
            request?.Ids ?? [],
            ownerId: null,
            request?.ShopId,
            cancellationToken);
        return Ok(ApiResponse<BulkDeleteProductOpportunitiesResultDto>.Ok(
            result,
            $"Đã xóa {result.Deleted} keyword."));
    }

    /// <summary>Save / unsave a keyword for one shop (survives the next snapshot sync).</summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpPut("{id:guid}/saved")]
    [ProducesResponseType(typeof(ApiResponse<ProductOpportunitySavedResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductOpportunitySavedResultDto>>> SetSaved(
        Guid id,
        [FromBody] SetProductOpportunitySavedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetSavedAsync(
            id,
            request?.Saved ?? false,
            ownerScope: null,
            User.GetUserId(),
            request?.ShopId,
            cancellationToken);
        return Ok(ApiResponse<ProductOpportunitySavedResultDto>.Ok(
            result,
            result.Saved ? "Đã lưu keyword." : "Đã bỏ lưu."));
    }

    /// <summary>Light saved-keyword list for a shop (bookmark ∪ snapshot).</summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpGet("saved-keywords")]
    [ProducesResponseType(typeof(ApiResponse<SavedKeywordListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SavedKeywordListDto>>> ListSavedKeywords(
        [FromQuery] Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _service.ListSavedKeywordsAsync(shopId, ownerScope: null, cancellationToken);
        return Ok(ApiResponse<SavedKeywordListDto>.Ok(result));
    }

    /// <summary>
    /// Save / unsave a keyword by text for the shop owner (no snapshot row — audience suggest).
    /// </summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpPut("saved-keywords")]
    [ProducesResponseType(typeof(ApiResponse<ProductOpportunitySavedResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductOpportunitySavedResultDto>>> SetSavedKeyword(
        [FromBody] SetSavedKeywordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetSavedByKeywordAsync(
            request?.ShopId ?? Guid.Empty,
            request?.Keyword ?? string.Empty,
            request?.Saved ?? false,
            ownerScope: null,
            User.GetUserId(),
            cancellationToken);
        return Ok(ApiResponse<ProductOpportunitySavedResultDto>.Ok(
            result,
            result.Saved ? "Đã lưu keyword." : "Đã bỏ lưu."));
    }

    /// <summary>Saved «Shop này bán gì» for any shop. Null when never analyzed.</summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpGet("shop-research")]
    [ProducesResponseType(typeof(ApiResponse<ShopKeywordResearchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopKeywordResearchDto>>> GetShopResearch(
        [FromQuery] Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _research.GetCachedAsync(shopId, ownerId: null, cancellationToken);
        return Ok(ApiResponse<ShopKeywordResearchDto>.Ok(result));
    }

    /// <summary>
    /// AI «Shop này bán gì» — snapshot hoặc đề xuất theo tệp khách khi chưa có Trending.
    /// Ghi đè bản lưu DB; lần mở sau dùng GET.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product_opportunities.view")]
    [HttpPost("shop-research")]
    [ProducesResponseType(typeof(ApiResponse<ShopKeywordResearchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopKeywordResearchDto>>> ShopResearch(
        [FromBody] ShopKeywordResearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _research.ResearchAsync(
            request?.ShopId ?? Guid.Empty,
            ownerId: null,
            cancellationToken);
        return Ok(ApiResponse<ShopKeywordResearchDto>.Ok(result));
    }
}
