using Application.DTOs.CompanyLedger;
using Application.DTOs.Shop;
using Application.Interfaces;
using Application.Services;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Constants;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin shop management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/shops")]
public class AdminShopsController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IShopFinanceService _shopFinance;
    private readonly ICompanyLedgerService _companyLedger;
    private readonly IWhatToSellTodayService _whatToSellToday;
    private readonly IValidator<CreateAdminShopDto> _createValidator;
    private readonly IValidator<UpdateAdminShopDto> _updateValidator;
    private readonly IValidator<UpdateShopCatalogProfileDto> _catalogValidator;
    private readonly IValidator<UpdateShopAutoPublishDto> _autoPublishValidator;
    private readonly IValidator<ReorderShopsDto> _reorderValidator;
    private readonly IValidator<UpsertShopAcquisitionCostDto> _acquisitionValidator;
    private readonly ISettingProvider _settings;

    /// <summary>
    /// Initializes the admin shops controller.
    /// </summary>
    public AdminShopsController(
        IShopService shopService,
        IShopFinanceService shopFinance,
        ICompanyLedgerService companyLedger,
        IWhatToSellTodayService whatToSellToday,
        IValidator<CreateAdminShopDto> createValidator,
        IValidator<UpdateAdminShopDto> updateValidator,
        IValidator<UpdateShopCatalogProfileDto> catalogValidator,
        IValidator<UpdateShopAutoPublishDto> autoPublishValidator,
        IValidator<ReorderShopsDto> reorderValidator,
        IValidator<UpsertShopAcquisitionCostDto> acquisitionValidator,
        ISettingProvider settings)
    {
        _shopService = shopService;
        _shopFinance = shopFinance;
        _companyLedger = companyLedger;
        _whatToSellToday = whatToSellToday;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _catalogValidator = catalogValidator;
        _autoPublishValidator = autoPublishValidator;
        _reorderValidator = reorderValidator;
        _acquisitionValidator = acquisitionValidator;
        _settings = settings;
    }

    /// <summary>System auto publish toggle for ShopsPage gating.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("auto-publish/system-config")]
    [ProducesResponseType(typeof(ApiResponse<AutoPublishSystemConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AutoPublishSystemConfigDto>>> GetAutoPublishSystemConfig(
        CancellationToken cancellationToken)
    {
        var values = await _settings.GetValuesAsync(
            [
                ListingAutoPublishSettingKeys.Enabled,
                ListingAutoPublishSettingKeys.DefaultIntervalSeconds,
                ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes
            ],
            cancellationToken);
        return Ok(ApiResponse<AutoPublishSystemConfigDto>.Ok(
            AutoPublishSystemConfigBuilder.FromSettings(values)));
    }

    /// <summary>
    /// Lightweight auto publish scheduler badges for shop list rows (background poll — no full list reload).
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("auto-publish-scheduler-status")]
    [ProducesResponseType(typeof(ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>>> AutoPublishSchedulerStatus(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var items = await _shopService.GetAutoPublishSchedulerStatusAsync(ids, cancellationToken: cancellationToken);
        return Ok(ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>.Ok(new ShopAutoPublishSchedulerStatusBatchDto
        {
            Items = items.ToList()
        }));
    }

    /// <summary>
    /// Lists all shops in the system ordered by SortOrder ascending (slim list payload).
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AdminShopListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AdminShopListItemDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _shopService.GetAllForAdminAsync(cancellationToken);
        return Ok(ApiResponse<List<AdminShopListItemDto>>.Ok(result));
    }

    /// <summary>
    /// Minimal shop options (id, name, owner, platform, status) for filter dropdowns.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<AdminShopOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AdminShopOptionDto>>>> GetOptions(CancellationToken cancellationToken)
    {
        var result = await _shopService.GetOptionsForAdminAsync(cancellationToken);
        return Ok(ApiResponse<List<AdminShopOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Shop finance overview: TTS On hold snapshot + Confirmed ledger tiền đã về.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("finance")]
    [ProducesResponseType(typeof(ApiResponse<ShopFinanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopFinanceDto>>> GetFinance(
        [FromQuery] ShopFinanceQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _shopFinance.GetForAdminAsync(query, cancellationToken);
        return Ok(ApiResponse<ShopFinanceDto>.Ok(result));
    }

    /// <summary>
    /// Sets the unique shop purchase cost on the company ledger.
    /// Null or 0 deletes the row. A new row copies the shop owner; later edits keep the assigned seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.company-ledger.manage")]
    [HttpPut("{id:guid}/acquisition-cost")]
    [ProducesResponseType(typeof(ApiResponse<CompanyLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CompanyLedgerEntryDto>>> SetAcquisitionCost(
        Guid id,
        [FromBody] UpsertShopAcquisitionCostDto request,
        CancellationToken cancellationToken)
    {
        await _acquisitionValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _companyLedger.UpsertShopAcquisitionAsync(
            id, request, User.GetUserId(), cancellationToken);
        var message = result is null ? "Đã xóa giá mua shop." : "Đã cập nhật giá mua shop.";
        return Ok(ApiResponse<CompanyLedgerEntryDto>.Ok(result, message));
    }

    /// <summary>
    /// Gets a shop by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminShopDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _shopService.GetByIdForAdminAsync(id, cancellationToken);
        return Ok(ApiResponse<AdminShopDto>.Ok(result));
    }

    /// <summary>
    /// Creates a shop and assigns it to a seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminShopDto>>> Create(
        [FromBody] CreateAdminShopDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.CreateForOwnerAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminShopDto>.Ok(result, "Tạo shop thành công."));
    }

    /// <summary>
    /// Updates a shop and optionally reassigns the seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminShopDto>>> Update(
        Guid id,
        [FromBody] UpdateAdminShopDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateForAdminAsync(id, request, cancellationToken);
        return Ok(ApiResponse<AdminShopDto>.Ok(result, "Cập nhật shop thành công."));
    }

    /// <summary>
    /// Orders with null ShopId grouped by 4Seller shop id — reconnect / transfer gaps.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet("mapping-health")]
    [ProducesResponseType(typeof(ApiResponse<ShopMappingHealthDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopMappingHealthDto>>> GetMappingHealth(
        CancellationToken cancellationToken)
    {
        var result = await _shopService.GetMappingHealthForAdminAsync(cancellationToken);
        return Ok(ApiResponse<ShopMappingHealthDto>.Ok(result));
    }

    /// <summary>
    /// Backfill ownership: reassign Orders/Listings still under a previous seller onto the shop's current owner.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("{id:guid}/reassign-owned-data")]
    [ProducesResponseType(typeof(ApiResponse<ShopOwnershipTransferResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopOwnershipTransferResultDto>>> ReassignOwnedData(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _shopService.ReassignOwnedDataForAdminAsync(id, cancellationToken);
        return Ok(ApiResponse<ShopOwnershipTransferResultDto>.Ok(
            result,
            result.HasChanges
                ? "Đã đồng bộ ownership dữ liệu bán hàng sang seller hiện tại."
                : "Không có dữ liệu lệch ownership cần chuyển."));
    }

    /// <summary>
    /// Backfill ownership for all shops with mismatched Order/Listing owners or duplicate TikTok shells.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("reassign-all-owned-data")]
    [ProducesResponseType(typeof(ApiResponse<ShopOwnershipTransferResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopOwnershipTransferResultDto>>> ReassignAllOwnedData(
        CancellationToken cancellationToken)
    {
        var result = await _shopService.ReassignAllOwnedDataForAdminAsync(cancellationToken);
        return Ok(ApiResponse<ShopOwnershipTransferResultDto>.Ok(
            result,
            result.HasChanges
                ? $"Đã đồng bộ ownership trên {result.ShopsProcessed} shop."
                : "Không có shop lệch ownership cần chuyển."));
    }

    /// <summary>
    /// Updates the operational catalog profile (TikTok leaf category + targeting flags).
    /// Does not touch ops fields or <c>ProductCategories</c>.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("{id:guid}/catalog-profile")]
    [ProducesResponseType(typeof(ApiResponse<ShopCatalogProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopCatalogProfileDto>>> UpdateCatalogProfile(
        Guid id,
        [FromBody] UpdateShopCatalogProfileDto request,
        CancellationToken cancellationToken)
    {
        await _catalogValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateCatalogProfileForAdminAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ShopCatalogProfileDto>.Ok(result, "Cập nhật danh mục shop thành công."));
    }

    /// <summary>
    /// Updates per-shop auto publish settings only (toggle + interval + min draft age).
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("{id:guid}/auto-publish")]
    [ProducesResponseType(typeof(ApiResponse<ShopAutoPublishDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopAutoPublishDto>>> UpdateAutoPublish(
        Guid id,
        [FromBody] UpdateShopAutoPublishDto request,
        CancellationToken cancellationToken)
    {
        await _autoPublishValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateAutoPublishForAdminAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ShopAutoPublishDto>.Ok(result, "Cập nhật auto publish thành công."));
    }

    /// <summary>
    /// Reorders all shops. Body must include every shop ID exactly once (first = SortOrder 0).
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Reorder(
        [FromBody] ReorderShopsDto request,
        CancellationToken cancellationToken)
    {
        await _reorderValidator.ValidateAndThrowAsync(request, cancellationToken);
        await _shopService.ReorderAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Sắp xếp shop thành công."));
    }

    /// <summary>
    /// Deletes a shop.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _shopService.DeleteForAdminAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa shop thành công."));
    }

    /// <summary>
    /// AI «Hôm nay bán gì?» for any shop (admin).
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpPost("{id:guid}/what-to-sell-today")]
    [ProducesResponseType(typeof(ApiResponse<WhatToSellTodayDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<WhatToSellTodayDto>>> WhatToSellToday(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _whatToSellToday.AnalyzeAsync(id, ownerId: null, cancellationToken);
        return Ok(ApiResponse<WhatToSellTodayDto>.Ok(result));
    }
}
