using Application.DTOs.Listing;
using Application.DTOs.ProductOpportunity;
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
/// Shop management endpoints for sellers.
/// </summary>
[ApiController]
[Route("api/v1/shops")]
public class ShopsController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IShopFinanceService _shopFinance;
    private readonly ITikTokPayoutSyncService _tikTokPayoutSync;
    private readonly IProductOpportunityService _productOpportunities;
    private readonly IListingAnalyticsService _listingAnalytics;
    private readonly ITikTokShopHealthRecordService _healthRecords;
    private readonly ITikTokShopScoreService _shopScores;
    private readonly ITikTokShopSyncLogService _tikTokSyncLog;
    private readonly IWhatToSellTodayService _whatToSellToday;
    private readonly ISettingProvider _settings;
    private readonly IValidator<UpdateShopDto> _updateValidator;
    private readonly IValidator<UpdateShopCatalogProfileDto> _catalogValidator;
    private readonly IValidator<UpdateShopAutoPublishDto> _autoPublishValidator;
    private readonly IValidator<SyncTikTokShopProfileDto> _syncTikTokProfileValidator;
    private readonly IValidator<SyncTikTokPayoutsDto> _syncTikTokPayoutsValidator;
    private readonly IValidator<SyncProductOpportunitiesDto> _syncProductOpportunitiesValidator;
    private readonly IValidator<SyncListingAnalyticsDto> _syncListingAnalyticsValidator;
    private readonly IValidator<SyncTikTokShopHealthRecordsDto> _syncHealthRecordsValidator;
    private readonly IValidator<SyncTikTokShopScoresDto> _syncShopScoresValidator;

    /// <summary>
    /// Initializes the shops controller.
    /// </summary>
    public ShopsController(
        IShopService shopService,
        IShopFinanceService shopFinance,
        ITikTokPayoutSyncService tikTokPayoutSync,
        IProductOpportunityService productOpportunities,
        IListingAnalyticsService listingAnalytics,
        ITikTokShopHealthRecordService healthRecords,
        ITikTokShopScoreService shopScores,
        ITikTokShopSyncLogService tikTokSyncLog,
        IWhatToSellTodayService whatToSellToday,
        ISettingProvider settings,
        IValidator<UpdateShopDto> updateValidator,
        IValidator<UpdateShopCatalogProfileDto> catalogValidator,
        IValidator<UpdateShopAutoPublishDto> autoPublishValidator,
        IValidator<SyncTikTokShopProfileDto> syncTikTokProfileValidator,
        IValidator<SyncTikTokPayoutsDto> syncTikTokPayoutsValidator,
        IValidator<SyncProductOpportunitiesDto> syncProductOpportunitiesValidator,
        IValidator<SyncListingAnalyticsDto> syncListingAnalyticsValidator,
        IValidator<SyncTikTokShopHealthRecordsDto> syncHealthRecordsValidator,
        IValidator<SyncTikTokShopScoresDto> syncShopScoresValidator)
    {
        _shopService = shopService;
        _shopFinance = shopFinance;
        _tikTokPayoutSync = tikTokPayoutSync;
        _productOpportunities = productOpportunities;
        _listingAnalytics = listingAnalytics;
        _healthRecords = healthRecords;
        _shopScores = shopScores;
        _tikTokSyncLog = tikTokSyncLog;
        _whatToSellToday = whatToSellToday;
        _settings = settings;
        _updateValidator = updateValidator;
        _catalogValidator = catalogValidator;
        _autoPublishValidator = autoPublishValidator;
        _syncTikTokProfileValidator = syncTikTokProfileValidator;
        _syncTikTokPayoutsValidator = syncTikTokPayoutsValidator;
        _syncProductOpportunitiesValidator = syncProductOpportunitiesValidator;
        _syncListingAnalyticsValidator = syncListingAnalyticsValidator;
        _syncHealthRecordsValidator = syncHealthRecordsValidator;
        _syncShopScoresValidator = syncShopScoresValidator;
    }

    /// <summary>System auto publish toggle for ShopsPage gating.</summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
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
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("auto-publish-scheduler-status")]
    [ProducesResponseType(typeof(ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>>> AutoPublishSchedulerStatus(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var items = await _shopService.GetAutoPublishSchedulerStatusAsync(
            ids,
            User.GetOwnerId(),
            cancellationToken);
        return Ok(ApiResponse<ShopAutoPublishSchedulerStatusBatchDto>.Ok(new ShopAutoPublishSchedulerStatusBatchDto
        {
            Items = items.ToList()
        }));
    }

    /// <summary>
    /// Lists shops for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ShopDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ShopDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _shopService.GetAllAsync(User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<List<ShopDto>>.Ok(result));
    }

    /// <summary>
    /// Active shops for Product Scraper (profile name + published/active listing counts).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("scraper-options")]
    [ProducesResponseType(typeof(ApiResponse<List<ScraperShopOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ScraperShopOptionDto>>>> GetScraperOptions(
        CancellationToken cancellationToken)
    {
        var result = await _shopService.GetScraperOptionsAsync(User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<List<ScraperShopOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Shop finance overview for the current seller (JWT-scoped shops).
    /// On hold is a TTS snapshot; tiền đã về follows optional from/to on Confirmed ledger.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("finance")]
    [ProducesResponseType(typeof(ApiResponse<ShopFinanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopFinanceDto>>> GetFinance(
        [FromQuery] ShopFinanceQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _shopFinance.GetForSellerAsync(User.GetOwnerId(), query, cancellationToken);
        return Ok(ApiResponse<ShopFinanceDto>.Ok(result));
    }

    /// <summary>
    /// Gets a shop by ID.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _shopService.GetByIdAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<ShopDto>.Ok(result));
    }

    /// <summary>
    /// Updates a shop owned by the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopDto>>> Update(
        Guid id,
        [FromBody] UpdateShopDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateAsync(User.GetOwnerId(), id, request, cancellationToken);
        return Ok(ApiResponse<ShopDto>.Ok(result, "Cập nhật shop thành công."));
    }

    /// <summary>
    /// Updates the operational catalog profile (TikTok leaf category + targeting flags).
    /// Does not touch ops fields or <c>ProductCategories</c>.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPut("{id:guid}/catalog-profile")]
    [ProducesResponseType(typeof(ApiResponse<ShopCatalogProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopCatalogProfileDto>>> UpdateCatalogProfile(
        Guid id,
        [FromBody] UpdateShopCatalogProfileDto request,
        CancellationToken cancellationToken)
    {
        await _catalogValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateCatalogProfileAsync(
            User.GetOwnerId(),
            id,
            request,
            cancellationToken);
        return Ok(ApiResponse<ShopCatalogProfileDto>.Ok(result, "Cập nhật danh mục shop thành công."));
    }

    /// <summary>
    /// Updates per-shop auto publish settings only (toggle + interval + min draft age).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPut("{id:guid}/auto-publish")]
    [ProducesResponseType(typeof(ApiResponse<ShopAutoPublishDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopAutoPublishDto>>> UpdateAutoPublish(
        Guid id,
        [FromBody] UpdateShopAutoPublishDto request,
        CancellationToken cancellationToken)
    {
        await _autoPublishValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.UpdateAutoPublishAsync(
            User.GetOwnerId(),
            id,
            request,
            cancellationToken);
        return Ok(ApiResponse<ShopAutoPublishDto>.Ok(result, "Cập nhật auto publish thành công."));
    }

    /// <summary>
    /// Extension auto-sync config for TikTok Shop profile (enabled + intervalSeconds).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-profile/sync-config")]
    [ProducesResponseType(typeof(ApiResponse<TikTokShopSyncConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokShopSyncConfigDto>>> GetTikTokShopSyncConfig(
        CancellationToken cancellationToken)
    {
        var result = await _shopService.GetTikTokShopSyncConfigAsync(cancellationToken);
        return Ok(ApiResponse<TikTokShopSyncConfigDto>.Ok(result));
    }

    /// <summary>
    /// Which Sync All / Sync Fast steps are enabled (admin Settings; missing key → on).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-unified-sync/config")]
    [ProducesResponseType(typeof(ApiResponse<TikTokUnifiedSyncConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokUnifiedSyncConfigDto>>> GetTikTokUnifiedSyncConfig(
        CancellationToken cancellationToken)
    {
        var result = await _shopService.GetTikTokUnifiedSyncConfigAsync(cancellationToken);
        return Ok(ApiResponse<TikTokUnifiedSyncConfigDto>.Ok(result));
    }

    /// <summary>
    /// Sync TikTok seller-us profile onto an existing shop (match externalShopId = sellerId). Never creates shops.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPut("tiktok-profile")]
    [ProducesResponseType(typeof(ApiResponse<ShopDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopDto>>> SyncTikTokProfile(
        [FromBody] SyncTikTokShopProfileDto request,
        CancellationToken cancellationToken)
    {
        await _syncTikTokProfileValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopService.SyncTikTokProfileAsync(
            User.GetOwnerId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<ShopDto>.Ok(result, "Đã đồng bộ profile TikTok Shop."));
    }

    /// <summary>
    /// Extension auto-sync config for TikTok Shop payouts (enabled, interval, maxDate, notify).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-payouts/sync-config")]
    [ProducesResponseType(typeof(ApiResponse<TikTokPayoutSyncConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokPayoutSyncConfigDto>>> GetTikTokPayoutSyncConfig(
        CancellationToken cancellationToken)
    {
        var result = await _tikTokPayoutSync.GetSyncConfigAsync(cancellationToken);
        return Ok(ApiResponse<TikTokPayoutSyncConfigDto>.Ok(result));
    }

    /// <summary>
    /// Latest TikTok payout cursor for incremental scrape (by externalShopId / sellerId).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-payouts/cursor")]
    [ProducesResponseType(typeof(ApiResponse<TikTokPayoutSyncCursorDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokPayoutSyncCursorDto>>> GetTikTokPayoutCursor(
        [FromQuery] string? externalShopId,
        [FromQuery] string? sellerId,
        CancellationToken cancellationToken)
    {
        var id = !string.IsNullOrWhiteSpace(externalShopId)
            ? externalShopId.Trim()
            : (sellerId ?? string.Empty).Trim();
        var result = await _tikTokPayoutSync.GetCursorAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<TikTokPayoutSyncCursorDto>.Ok(result));
    }

    /// <summary>
    /// Upsert TikTok payouts into receiving-bank ledger + mark linked orders settlement-paid.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-payouts/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncTikTokPayoutsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SyncTikTokPayoutsResultDto>>> SyncTikTokPayouts(
        [FromBody] SyncTikTokPayoutsDto request,
        CancellationToken cancellationToken)
    {
        await _syncTikTokPayoutsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _tikTokPayoutSync.SyncAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<SyncTikTokPayoutsResultDto>.Ok(result, "Đã đồng bộ payout TikTok Shop."));
    }

    /// <summary>
    /// Extension auto-sync config for Product opportunities (enabled + intervalSeconds).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-opportunities/sync-config")]
    [ProducesResponseType(typeof(ApiResponse<ProductOpportunitySyncConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductOpportunitySyncConfigDto>>> GetTikTokOpportunitySyncConfig(
        CancellationToken cancellationToken)
    {
        var result = await _productOpportunities.GetSyncConfigAsync(cancellationToken);
        return Ok(ApiResponse<ProductOpportunitySyncConfigDto>.Ok(result));
    }

    /// <summary>
    /// Niche category_infos groups for the owner's shop (abort scrape when hasCatalog is false).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpGet("tiktok-opportunities/sync-context")]
    [ProducesResponseType(typeof(ApiResponse<ProductOpportunitySyncContextDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductOpportunitySyncContextDto>>> GetTikTokOpportunitySyncContext(
        [FromQuery] string? externalShopId,
        [FromQuery] string? sellerId,
        CancellationToken cancellationToken)
    {
        var id = !string.IsNullOrWhiteSpace(externalShopId)
            ? externalShopId.Trim()
            : (sellerId ?? string.Empty).Trim();
        var result = await _productOpportunities.GetSyncContextAsync(
            User.GetOwnerId(),
            id,
            cancellationToken);
        return Ok(ApiResponse<ProductOpportunitySyncContextDto>.Ok(result));
    }

    /// <summary>
    /// Replace the shop Product-opportunity snapshot after a successful scrape.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-opportunities/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncProductOpportunitiesResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SyncProductOpportunitiesResultDto>>> SyncTikTokOpportunities(
        [FromBody] SyncProductOpportunitiesDto request,
        CancellationToken cancellationToken)
    {
        await _syncProductOpportunitiesValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _productOpportunities.SyncAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<SyncProductOpportunitiesResultDto>.Ok(result, "Đã đồng bộ Product opportunities."));
    }

    /// <summary>
    /// Upsert today's TikTok Compass product metrics for the owner's shop.
    /// Idempotent per Vietnam day: bấm Analytics lần 2 ghi đè số liệu của chính ngày đó.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-listing-analytics/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncListingAnalyticsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SyncListingAnalyticsResultDto>>> SyncTikTokListingAnalytics(
        [FromBody] SyncListingAnalyticsDto request,
        CancellationToken cancellationToken)
    {
        await _syncListingAnalyticsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _listingAnalytics.SyncAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<SyncListingAnalyticsResultDto>.Ok(result, "Đã đồng bộ Listing analytics."));
    }

    /// <summary>
    /// Upsert TikTok Shop Growth Center violation/warning records for the owner's shop.
    /// Dedupes by record id — re-sync updates status and enforcement titles.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-health-records/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncTikTokShopHealthRecordsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SyncTikTokShopHealthRecordsResultDto>>> SyncTikTokHealthRecords(
        [FromBody] SyncTikTokShopHealthRecordsDto request,
        CancellationToken cancellationToken)
    {
        await _syncHealthRecordsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _healthRecords.SyncAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<SyncTikTokShopHealthRecordsResultDto>.Ok(result, "Đã đồng bộ Health Center."));
    }

    /// <summary>
    /// Upsert TikTok Health Center scores (AHR + Shop Performance Score) for the owner's shop.
    /// Điểm nào payload không có thì giữ nguyên giá trị đang lưu.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-shop-scores/sync")]
    [ProducesResponseType(typeof(ApiResponse<SyncTikTokShopScoresResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SyncTikTokShopScoresResultDto>>> SyncTikTokShopScores(
        [FromBody] SyncTikTokShopScoresDto request,
        CancellationToken cancellationToken)
    {
        await _syncShopScoresValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _shopScores.SyncAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<SyncTikTokShopScoresResultDto>.Ok(result, "Đã đồng bộ điểm shop."));
    }

    /// <summary>
    /// Record a skipped/failed TikTok Shop sync step from the extension (without calling sync endpoints).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("tiktok-sync/log")]
    [ProducesResponseType(typeof(ApiResponse<TikTokShopSyncLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokShopSyncLogDto>>> RecordTikTokSyncLog(
        [FromBody] RecordTikTokShopSyncLogDto request,
        CancellationToken cancellationToken)
    {
        var result = await _tikTokSyncLog.RecordAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<TikTokShopSyncLogDto>.Ok(result));
    }

    /// <summary>
    /// List TikTok Shop extension sync logs for the current owner.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpGet("tiktok-sync/logs")]
    [ProducesResponseType(typeof(ApiResponse<TikTokShopSyncLogPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TikTokShopSyncLogPageDto>>> GetTikTokSyncLogs(
        [FromQuery] TikTokShopSyncLogQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _tikTokSyncLog.ListAsync(User.GetOwnerId(), query, cancellationToken);
        return Ok(ApiResponse<TikTokShopSyncLogPageDto>.Ok(result));
    }

    /// <summary>
    /// AI «Hôm nay bán gì?» — phân tích ngách/bestsellers + 2 link tìm Shein (organic only).
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.view")]
    [HttpPost("{id:guid}/what-to-sell-today")]
    [ProducesResponseType(typeof(ApiResponse<WhatToSellTodayDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<WhatToSellTodayDto>>> WhatToSellToday(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _whatToSellToday.AnalyzeAsync(
            id,
            User.GetOwnerId(),
            cancellationToken);
        return Ok(ApiResponse<WhatToSellTodayDto>.Ok(result));
    }
}
