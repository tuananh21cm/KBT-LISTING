using Api.Extensions;
using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Constants;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin listing list/detail, 4Seller sync, and save-draft/publish push.
/// </summary>
[ApiController]
[Route("api/v1/admin/listings")]
public class AdminListingsController : ControllerBase
{
    private readonly IListingSyncService _listingSyncService;
    private readonly IListingSyncJobRunner _listingSyncJobRunner;
    private readonly IListingDescriptionAiService _listingDescriptionAiService;
    private readonly IListingSpecificsMapService _listingSpecificsMapService;
    private readonly IListingAnalyticsService _listingAnalytics;
    private readonly IListingDelistSuggestionService _delistSuggestions;
    private readonly ISizeChartImageService _sizeChartImageService;
    private readonly IListingAutoPublishRunService _autoPublishRuns;

    /// <summary>
    /// Initializes the admin listings controller.
    /// </summary>
    public AdminListingsController(
        IListingSyncService listingSyncService,
        IListingSyncJobRunner listingSyncJobRunner,
        IListingDescriptionAiService listingDescriptionAiService,
        IListingSpecificsMapService listingSpecificsMapService,
        IListingAnalyticsService listingAnalytics,
        IListingDelistSuggestionService delistSuggestions,
        ISizeChartImageService sizeChartImageService,
        IListingAutoPublishRunService autoPublishRuns)
    {
        _listingSyncService = listingSyncService;
        _listingSyncJobRunner = listingSyncJobRunner;
        _listingDescriptionAiService = listingDescriptionAiService;
        _listingSpecificsMapService = listingSpecificsMapService;
        _listingAnalytics = listingAnalytics;
        _delistSuggestions = delistSuggestions;
        _sizeChartImageService = sizeChartImageService;
        _autoPublishRuns = autoPublishRuns;
    }

    /// <summary>
    /// Lists synced listings from local DB.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ListingListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingListPageDto>>> List(
        [FromQuery] ListingListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<ListingListPageDto>.Ok(result));
    }

    /// <summary>
    /// Lightweight auto publish queue badges for draft listing cards (background poll — no full list reload).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("auto-publish-queue-status")]
    [ProducesResponseType(typeof(ApiResponse<ListingAutoPublishQueueStatusBatchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingAutoPublishQueueStatusBatchDto>>> AutoPublishQueueStatus(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var items = await _listingSyncService.GetAutoPublishQueueStatusAsync(ids, cancellationToken: cancellationToken);
        return Ok(ApiResponse<ListingAutoPublishQueueStatusBatchDto>.Ok(new ListingAutoPublishQueueStatusBatchDto
        {
            Items = items.ToList()
        }));
    }

    /// <summary>
    /// TikTok Compass product metrics for one snapshot day (all sellers, filterable).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("analytics")]
    [ProducesResponseType(typeof(ApiResponse<ListingAnalyticsPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingAnalyticsPageDto>>> ListAnalytics(
        [FromQuery] ListingAnalyticsListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _listingAnalytics.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<ListingAnalyticsPageDto>.Ok(result));
    }

    /// <summary>
    /// Snapshot days that have analytics data in the given scope (date picker).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("analytics/dates")]
    [ProducesResponseType(typeof(ApiResponse<ListingAnalyticsDatesDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingAnalyticsDatesDto>>> ListAnalyticsDates(
        [FromQuery] Guid? ownerUserId,
        [FromQuery] Guid? shopId,
        CancellationToken cancellationToken)
    {
        var result = await _listingAnalytics.GetSnapshotDatesAsync(ownerUserId, shopId, cancellationToken);
        return Ok(ApiResponse<ListingAnalyticsDatesDto>.Ok(result));
    }

    /// <summary>
    /// Weakest products of one shop worth delisting to free TikTok publish slots, plus the keywords
    /// «Shop này bán gì» is waiting to list. Read-only.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("analytics/delist-suggestions")]
    [ProducesResponseType(typeof(ApiResponse<ListingDelistSuggestionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDelistSuggestionDto>>> ListDelistSuggestions(
        [FromQuery] ListingDelistSuggestionQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _delistSuggestions.SuggestAsync(query, cancellationToken);
        return Ok(ApiResponse<ListingDelistSuggestionDto>.Ok(result));
    }

    /// <summary>
    /// Status counts from local DB (same filters as list except siteStatus).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("status-counts")]
    [ProducesResponseType(typeof(ApiResponse<ListingStatusCountsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingStatusCountsDto>>> StatusCounts(
        [FromQuery] ListingListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.GetStatusCountsAsync(query, cancellationToken);
        return Ok(ApiResponse<ListingStatusCountsDto>.Ok(result));
    }

    /// <summary>
    /// Gets listing detail with variations.
    /// Default = local DB snapshot (fast). Pass <c>refresh=true</c> to best-effort pull 4Seller per-id detail first.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ListingDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDetailDto>>> GetById(
        Guid id,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _listingSyncService.GetByIdAsync(id, refresh, cancellationToken);
        return Ok(ApiResponse<ListingDetailDto>.Ok(result));
    }

    /// <summary>
    /// Queues a 4Seller listing sync (all sellers with cookie, or one owner) and returns its job id.
    /// The pull runs detached from this request, so a browser timeout cannot cancel it mid-upsert;
    /// poll <c>sync/status/{jobId}</c> for per-seller progress.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<ListingSyncJobStatusDto>), StatusCodes.Status202Accepted)]
    public ActionResult<ApiResponse<ListingSyncJobStatusDto>> Sync([FromBody] ListingSyncRequest? request)
    {
        var body = request ?? new ListingSyncRequest();
        body.SyncSource = ListingSyncSources.Manual;
        body.ApplyDefaults();

        var job = _listingSyncJobRunner.Start(body, User.GetUserId());
        return Accepted(ApiResponse<ListingSyncJobStatusDto>.Ok(job, "Đã bắt đầu đồng bộ listing."));
    }

    /// <summary>
    /// Progress of a queued listing sync, including each seller's own status so finished accounts
    /// are visible while slower ones are still running.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpGet("sync/status/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ListingSyncJobStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<ListingSyncJobStatusDto>> SyncStatus(Guid jobId)
    {
        var job = _listingSyncJobRunner.GetStatus(jobId);
        if (job is null)
        {
            return NotFound(ApiResponse<ListingSyncJobStatusDto>.Fail("Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        return Ok(ApiResponse<ListingSyncJobStatusDto>.Ok(job));
    }

    /// <summary>
    /// Updates local listing fields (patches RawPayload when present).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ListingDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDetailDto>>> Update(
        Guid id,
        [FromBody] UpdateListingMirrorDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.UpdateAsync(id, dto, cancellationToken);
        return Ok(ApiResponse<ListingDetailDto>.Ok(result, "Đã cập nhật listing local."));
    }

    /// <summary>
    /// Local-only: save description HTML + Rewrite/Insert Images checklist (no 4Seller push).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPut("{id:guid}/description-prep")]
    [ProducesResponseType(typeof(ApiResponse<ListingDescriptionPrepResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDescriptionPrepResultDto>>> SaveDescriptionPrep(
        Guid id,
        [FromBody] UpdateListingDescriptionPrepDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.SaveDescriptionPrepAsync(id, dto, cancellationToken);
        return Ok(ApiResponse<ListingDescriptionPrepResultDto>.Ok(result, "Đã lưu mô tả local."));
    }

    /// <summary>
    /// Local-only: set supplier source URL (no 4Seller push). Links existing SupplierProductInfo when the URL key matches.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPut("{id:guid}/source-url")]
    [ProducesResponseType(typeof(ApiResponse<ListingSourceUrlResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingSourceUrlResultDto>>> UpdateSourceUrl(
        Guid id,
        [FromBody] UpdateListingSourceUrlDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.UpdateSourceUrlAsync(id, dto, cancellationToken);
        return Ok(ApiResponse<ListingSourceUrlResultDto>.Ok(result, "Đã lưu URL nguồn."));
    }

    /// <summary>
    /// Patches RawPayload from body (optional), pushes save-draft to 4Seller, then saves local.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/save-draft")]
    [ProducesResponseType(typeof(ApiResponse<ListingPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingPushResultDto>>> SaveDraft(
        Guid id,
        [FromBody] UpdateListingMirrorDto? dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.SaveDraftAsync(id, dto, cancellationToken);
        return Ok(ApiResponse<ListingPushResultDto>.Ok(result, result.Message ?? "Save Draft thành công."));
    }

    /// <summary>
    /// Patches RawPayload from body (optional), pushes publish to 4Seller, then saves local.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(ApiResponse<ListingPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingPushResultDto>>> Publish(
        Guid id,
        [FromBody] UpdateListingMirrorDto? dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.PublishAsync(id, dto, cancellationToken: cancellationToken);
        return Ok(ApiResponse<ListingPushResultDto>.Ok(result, result.Message ?? "Đã gửi publish, đang chờ duyệt."));
    }

    /// <summary>
    /// Patches RawPayload from body (optional), pushes update to 4Seller (already-published listing), then saves local.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/update")]
    [ProducesResponseType(typeof(ApiResponse<ListingPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingPushResultDto>>> PushUpdate(
        Guid id,
        [FromBody] UpdateListingMirrorDto? dto,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.PushUpdateAsync(id, dto, cancellationToken);
        return Ok(ApiResponse<ListingPushResultDto>.Ok(result, result.Message ?? "Update thành công."));
    }

    /// <summary>
    /// Deactivates a published listing on 4Seller (batch-deactivate) and marks it inactive locally.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<ListingPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingPushResultDto>>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.DeactivateAsync(id, cancellationToken);
        return Ok(ApiResponse<ListingPushResultDto>.Ok(result, result.Message ?? "Deactivate thành công."));
    }

    /// <summary>
    /// Activates an inactive listing on 4Seller (batch-activate) and marks it active locally.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(ApiResponse<ListingPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingPushResultDto>>> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.ActivateAsync(id, cancellationToken);
        return Ok(ApiResponse<ListingPushResultDto>.Ok(result, result.Message ?? "Activate thành công."));
    }

    /// <summary>
    /// Duplicates a listing on 4Seller (copy) then upserts the new draft locally and returns its detail.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType(typeof(ApiResponse<ListingDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDetailDto>>> Duplicate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.DuplicateAsync(id, cancellationToken);
        return Ok(ApiResponse<ListingDetailDto>.Ok(result, "Đã tạo bản sao listing."));
    }

    /// <summary>
    /// Syncs one listing on 4Seller (sync-one-local-listing) then refreshes local detail from 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/sync-one")]
    [ProducesResponseType(typeof(ApiResponse<ListingDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDetailDto>>> SyncOne(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.SyncOneAsync(id, cancellationToken);
        return Ok(ApiResponse<ListingDetailDto>.Ok(result, "Đã đồng bộ listing từ shop."));
    }

    /// <summary>
    /// Move-source / category-match info for a draft that still needs a TikTok category
    /// (4Seller <c>batch-get-move-source-listing</c>).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/move-source")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerMoveSourceListingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerMoveSourceListingDto>>> GetMoveSource(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.GetMoveSourceAsync(id, cancellationToken);
        return Ok(ApiResponse<FourSellerMoveSourceListingDto>.Ok(result));
    }

    /// <summary>
    /// Recent TikTok categories for the listing shop (4Seller <c>recent-use-category</c>).
    /// Optional <c>shopId</c> / <c>site</c> from move-source; bind uses each row's <c>id</c>.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/recent-categories")]
    [ProducesResponseType(typeof(ApiResponse<List<FourSellerRecentUseCategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FourSellerRecentUseCategoryDto>>>> GetRecentCategories(
        Guid id,
        [FromQuery] long? shopId = null,
        [FromQuery] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _listingSyncService.GetRecentCategoriesAsync(
            id,
            shopId,
            site,
            cancellationToken);
        return Ok(ApiResponse<List<FourSellerRecentUseCategoryDto>>.Ok(result.ToList()));
    }

    /// <summary>
    /// Binds a recent-use category onto the listing (4Seller <c>batch-binging-category</c>).
    /// Body <c>recentCategoryId</c> is the recent-use row id (not TikTok categoryId).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/bind-category")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> BindCategory(
        Guid id,
        [FromBody] BindListingCategoryRequest request,
        CancellationToken cancellationToken)
    {
        await _listingSyncService.BindCategoryAsync(id, request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã gắn category cho listing."));
    }

    /// <summary>
    /// Rewrites product description via OpenAI from current form title + description (no persist).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("rewrite-description")]
    [ProducesResponseType(typeof(ApiResponse<RewriteListingDescriptionResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RewriteListingDescriptionResultDto>>> RewriteDescription(
        [FromBody] RewriteListingDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingDescriptionAiService.RewriteAsync(request, cancellationToken);
        return Ok(ApiResponse<RewriteListingDescriptionResultDto>.Ok(
            result,
            "Đã viết lại mô tả bằng AI."));
    }

    /// <summary>
    /// Title-mode + whether Rewrite may run again after a successful rewrite.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("rewrite-config")]
    [ProducesResponseType(typeof(ApiResponse<ListingRewriteConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingRewriteConfigDto>>> GetRewriteConfig(
        CancellationToken cancellationToken)
    {
        var result = await _listingDescriptionAiService.GetRewriteConfigAsync(cancellationToken);
        return Ok(ApiResponse<ListingRewriteConfigDto>.Ok(result));
    }

    /// <summary>
    /// Maps Key:Value pairs from description onto the category's 4Seller Specifics schema
    /// (deterministic + AI). Suggestion only — does not persist; FE merges after review.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/map-specifics")]
    [ProducesResponseType(typeof(ApiResponse<MapListingSpecificsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MapListingSpecificsResultDto>>> MapSpecifics(
        Guid id,
        [FromBody] MapListingSpecificsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingSpecificsMapService.MapAsync(id, request, cancellationToken);
        var message =
            $"Đã map {result.Matched.Count}/{result.ParsedPairCount} Specifics" +
            (result.Unmatched.Count > 0 ? $"; {result.Unmatched.Count} chưa khớp." : ".");
        return Ok(ApiResponse<MapListingSpecificsResultDto>.Ok(result, message));
    }

    /// <summary>Lists auto publish run logs for a listing.</summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/auto-publish/runs")]
    [ProducesResponseType(typeof(ApiResponse<List<ListingAutoPublishRunDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ListingAutoPublishRunDto>>>> ListAutoPublishRuns(
        Guid id,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var runs = await _autoPublishRuns.ListRunsAsync(id, take, cancellationToken);
        return Ok(ApiResponse<List<ListingAutoPublishRunDto>>.Ok(runs));
    }

    /// <summary>Gets one auto publish run detail.</summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/auto-publish/runs/{runId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ListingAutoPublishRunDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingAutoPublishRunDto>>> GetAutoPublishRun(
        Guid id,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _autoPublishRuns.GetRunAsync(id, runId, cancellationToken);
        if (run is null)
        {
            return NotFound(ApiResponse<ListingAutoPublishRunDto>.Fail("Không tìm thấy auto publish run."));
        }

        return Ok(ApiResponse<ListingAutoPublishRunDto>.Ok(run));
    }

    /// <summary>Clears auto publish skip flag so scheduler can retry.</summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/auto-publish/retry")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> RetryAutoPublish(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await _autoPublishRuns.RetryAutoPublishAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã cho phép auto publish thử lại listing này."));
    }

    /// <summary>
    /// Loads authorized brands + warehouses for a local shop (via listing owner's 4Seller cookie).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/shop-metadata")]
    [ProducesResponseType(typeof(ApiResponse<ListingShopMetadataDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingShopMetadataDto>>> GetShopMetadata(
        Guid id,
        [FromQuery] Guid shopId,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.GetShopMetadataAsync(id, shopId, cancellationToken);
        return Ok(ApiResponse<ListingShopMetadataDto>.Ok(result));
    }

    /// <summary>
    /// Suggests TikTok brands for a shop (listing-scoped ownership check).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet("{id:guid}/brand-suggest")]
    [ProducesResponseType(typeof(ApiResponse<List<FourSellerBrandDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FourSellerBrandDto>>>> SuggestBrands(
        Guid id,
        [FromQuery] Guid shopId,
        [FromQuery] string brandName = "",
        CancellationToken cancellationToken = default)
    {
        var result = await _listingSyncService.SuggestBrandsAsync(id, shopId, brandName, cancellationToken);
        return Ok(ApiResponse<List<FourSellerBrandDto>>.Ok(result.ToList()));
    }

    /// <summary>
    /// Uploads Size Chart / Product video to 4Seller COS (get-sign + PUT) via listing owner cookie.
    /// Returns CDN URL + localMaterialId for immediate form display.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/cos-upload")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<FourSellerCosUploadResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerCosUploadResultDto>>> UploadCos(
        Guid id,
        IFormFile file,
        [FromForm] string? referenceType = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<FourSellerCosUploadResultDto>.Fail("Chọn file để upload."));
        }

        await using var stream = file.OpenReadStream();
        var result = await _listingSyncService.UploadCosMediaAsync(
            id,
            stream,
            file.FileName,
            file.ContentType,
            referenceType,
            cancellationToken);
        return Ok(ApiResponse<FourSellerCosUploadResultDto>.Ok(result, "Upload 4Seller thành công."));
    }

    /// <summary>
    /// Generates a Size Chart image via Grok Imagine and uploads to 4Seller COS.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("{id:guid}/generate-size-chart")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerCosUploadResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerCosUploadResultDto>>> GenerateSizeChart(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var (result, warning) = await _sizeChartImageService.GenerateAndUploadAsync(id, cancellationToken);
        var message = string.IsNullOrWhiteSpace(warning)
            ? "Tạo và upload Size Chart thành công."
            : $"Tạo và upload Size Chart thành công. {warning}";
        return Ok(ApiResponse<FourSellerCosUploadResultDto>.Ok(result, message));
    }

    /// <summary>
    /// Deletes listings matching list filters (ignores paging): 4Seller delete then local wipe.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<ListingDeleteResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDeleteResultDto>>> DeleteByFilter(
        [FromQuery] ListingListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.DeleteByFilterAsync(query, cancellationToken);
        return Ok(ApiResponse<ListingDeleteResultDto>.Ok(
            result,
            result.DeletedCount == 0
                ? "Không có listing nào khớp bộ lọc để xóa."
                : $"Đã xóa {result.DeletedCount} listing."));
    }

    /// <summary>
    /// Bulk-deactivates selected Active listings (Hủy list): 4Seller batch-deactivate, local Inactive.
    /// Non-Active / unlinked ids are skipped.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("bulk-deactivate")]
    [ProducesResponseType(typeof(ApiResponse<ListingDeactivateResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDeactivateResultDto>>> BulkDeactivate(
        [FromBody] BulkDeleteListingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.DeactivateByIdsAsync(request.Ids, cancellationToken);
        var message = result.DeactivatedCount == 0
            ? "Không có listing Active nào để deactivate."
            : result.SkippedCount > 0
                ? $"Đã deactivate {result.DeactivatedCount} listing (bỏ qua {result.SkippedCount})."
                : $"Đã deactivate {result.DeactivatedCount} listing.";
        return Ok(ApiResponse<ListingDeactivateResultDto>.Ok(result, message));
    }

    /// <summary>
    /// Deletes selected listings by local id: 4Seller delete then local wipe.
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpPost("bulk-delete")]
    [ProducesResponseType(typeof(ApiResponse<ListingDeleteResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ListingDeleteResultDto>>> BulkDelete(
        [FromBody] BulkDeleteListingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingSyncService.DeleteByIdsAsync(request.Ids, cancellationToken);
        return Ok(ApiResponse<ListingDeleteResultDto>.Ok(
            result,
            result.DeletedCount == 0
                ? "Không có listing nào để xóa."
                : $"Đã xóa {result.DeletedCount} listing."));
    }

    /// <summary>
    /// Deletes a listing on 4Seller then removes the local mirror (cascade variations).
    /// </summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteById(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _listingSyncService.DeleteByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã xóa listing."));
    }
}
