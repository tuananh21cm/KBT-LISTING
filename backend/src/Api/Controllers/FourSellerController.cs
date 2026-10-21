using Application.DTOs.FourSeller;
using Application.DTOs.Supplier;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller endpoints for 4Seller integration (scoped to owned shops).
/// </summary>
[ApiController]
[Route("api/v1/fourseller")]
public class FourSellerController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IListingImportService _listingImportService;
    private readonly IPostHtmlDraftSyncScheduler _postHtmlDraftSyncScheduler;
    private readonly ISupplierProductInfoService _supplierProductInfoService;
    private readonly IListingAddOriginService _listingAddOrigins;
    private readonly IValidator<FourSellerPostHtmlRequest> _postHtmlValidator;
    private readonly IValidator<SupplierSizeChartImageRequest> _sizeChartImageValidator;
    private readonly ILogger<FourSellerController> _logger;

    /// <summary>
    /// Initializes the seller 4Seller controller.
    /// </summary>
    public FourSellerController(
        IShopService shopService,
        IFourSellerApiService fourSellerApiService,
        IListingImportService listingImportService,
        IPostHtmlDraftSyncScheduler postHtmlDraftSyncScheduler,
        ISupplierProductInfoService supplierProductInfoService,
        IListingAddOriginService listingAddOrigins,
        IValidator<FourSellerPostHtmlRequest> postHtmlValidator,
        IValidator<SupplierSizeChartImageRequest> sizeChartImageValidator,
        ILogger<FourSellerController> logger)
    {
        _shopService = shopService;
        _fourSellerApiService = fourSellerApiService;
        _listingImportService = listingImportService;
        _postHtmlDraftSyncScheduler = postHtmlDraftSyncScheduler;
        _supplierProductInfoService = supplierProductInfoService;
        _listingAddOrigins = listingAddOrigins;
        _postHtmlValidator = postHtmlValidator;
        _sizeChartImageValidator = sizeChartImageValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates an OAuth authorize URL for connecting an owned shop to 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("shops/{shopId:guid}/auth-redirect-url")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerAuthRedirectResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerAuthRedirectResponse>>> GetAuthRedirectUrl(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shop = await _shopService.GetByIdAsync(User.GetOwnerId(), shopId, cancellationToken);

        var request = new FourSellerAuthRedirectRequest
        {
            PlatformCode = shop.PlatformCode.ToLowerInvariant(),
            ShopName = shop.Name,
            SiteList = [string.IsNullOrWhiteSpace(shop.Region) ? "US" : shop.Region],
            ShopifyShop = shop.ShopUrl ?? string.Empty
        };

        var result = await _fourSellerApiService.GetAuthRedirectUrlAsync(
            User.GetOwnerId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerAuthRedirectResponse>.Ok(result));
    }

    /// <summary>
    /// Checks whether an owned shop is connected on 4Seller and syncs local state when matched.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("shops/{shopId:guid}/check-connection")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerShopCheckResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerShopCheckResponse>>> CheckConnection(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var ownerId = User.GetOwnerId();
        var shop = await _shopService.GetByIdAsync(ownerId, shopId, cancellationToken);

        var request = new FourSellerShopCheckRequest
        {
            ShopName = shop.Name,
            PlatformCode = shop.PlatformCode,
            ExternalShopId = shop.ExternalShopId,
            FourSellerShopId = shop.FourSellerShopId,
            Region = string.IsNullOrWhiteSpace(shop.Region) ? "US" : shop.Region
        };

        var result = await _fourSellerApiService.CheckShopConnectionAsync(
            ownerId,
            request,
            cancellationToken);

        if (result.IsConnected && result.MatchedShop is not null)
        {
            var syncedShop = await _shopService.SyncFourSellerConnectionAsync(
                shopId,
                result.MatchedShop,
                cancellationToken);
            result.ShopSynced = true;
            result.FourSellerConnectedAt = syncedShop.FourSellerConnectedAt;
        }

        return Ok(ApiResponse<FourSellerShopCheckResponse>.Ok(result));
    }

    /// <summary>
    /// Syncs owned (granted) shops from 4Seller tidy-list: update matched locals only — never creates shops.
    /// Validates 4Seller login (<c>is-login</c>) before calling tidy-list.
    /// </summary>
    [Authorize(Policy = "Permission:seller.shops.edit")]
    [HttpPost("shops/sync")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerSyncResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FourSellerSyncResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FourSellerSyncResponse>>> SyncOwnedShops(
        CancellationToken cancellationToken)
    {
        var ownerId = User.GetOwnerId();
        var login = await _fourSellerApiService.CheckLoginAsync(ownerId, null, cancellationToken);
        if (!login.IsLoggedIn)
        {
            return BadRequest(ApiResponse<FourSellerSyncResponse>.Fail(
                string.IsNullOrWhiteSpace(login.Message)
                    ? "Chưa đăng nhập 4Seller. Cập nhật cookie tại Hồ sơ cá nhân."
                    : login.Message));
        }

        var fourSellerShops = await _fourSellerApiService.GetTidyShopListAsync(ownerId, cancellationToken);
        var response = await _shopService.UpsertFromFourSellerListAsync(
            ownerId,
            fourSellerShops,
            scopeToOwnerOnly: true,
            cancellationToken);

        return Ok(ApiResponse<FourSellerSyncResponse>.Ok(response));
    }

    /// <summary>
    /// Lists 4Seller listings for the current user's cookie (platform path, e.g. tiktok).
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.view")]
    [HttpPost("listings/{platform}/page")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerListingPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerListingPageDto>>> GetListingPage(
        string platform,
        [FromBody] FourSellerListingPageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingImportService.GetPageAsync(
            User.GetOwnerId(),
            platform,
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerListingPageDto>.Ok(result));
    }

    /// <summary>
    /// Deletes selected listings on 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost("listings/{platform}/delete")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteListings(
        string platform,
        [FromBody] FourSellerDeleteListingsRequest request,
        CancellationToken cancellationToken)
    {
        await _listingImportService.DeleteAsync(
            User.GetOwnerId(),
            platform,
            request,
            cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, "Đã xóa listing trên 4Seller."));
    }

    /// <summary>
    /// Browser Extension: push product-page HTML into 4Seller store drafts.
    /// Cookie stays on the server — never returned to the client.
    /// On success, schedules a <b>debounced</b> draft pull (does not block the response)
    /// so multi-tab adds coalesce into one <c>IListingSyncService.SyncAsync</c>.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost("crawl/post-html")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<FourSellerPostHtmlResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerPostHtmlResultDto>>> PostHtmlFileShop(
        [FromBody] FourSellerPostHtmlRequest request,
        CancellationToken cancellationToken)
    {
        await _postHtmlValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ownerId = User.GetOwnerId();

        // Persist supplier PDP metrics locally even if 4Seller post-html fails later.
        await _supplierProductInfoService.UpsertFromUrlAsync(
            new SupplierProductInfoUpsertRequest
            {
                SourceUrl = request.Url,
                Platform = request.Platform,
                ReviewCount = request.ReviewCount,
                AverageRating = request.AverageRating,
                Stock = request.Stock,
                ShippingDays = request.ShippingDays,
                ShippingDayPercent = request.ShippingDayPercent,
                ShippingDayPercentDesc = request.ShippingDayPercentDesc,
                StoreCode = request.StoreCode,
                StoreLogo = request.StoreLogo,
                StoreTitle = request.StoreTitle,
                SoldByTips = request.SoldByTips,
                ShipsFromTips = request.ShipsFromTips,
                ShopRank = request.ShopRank,
                StoreAttention = request.StoreAttention,
                StoreUrl = request.StoreUrl,
                IsLocalProduct = request.IsLocalProduct,
                IsSupportQuickShip = request.IsSupportQuickShip,
                SizeGuideJson = request.SizeGuideJson
            },
            cancellationToken);

        var result = await _fourSellerApiService.PostHtmlFileShopAsync(
            ownerId,
            request,
            cancellationToken);

        if (result.Ok)
        {
            // Only after 4Seller accepts: failed post-html must not stamp existing listings.
            // Swallow staging errors so a unique-constraint race cannot 500 after the draft exists
            // (retry would early-return «đã thêm» and never write origin).
            try
            {
                await _listingAddOrigins.UpsertFromPostHtmlAsync(
                    ownerId,
                    request.Url,
                    request.ShopIdList,
                    request.AddedFrom,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Listing add origin staging failed after post-html for owner {OwnerId}",
                    ownerId);
            }

            // Non-blocking: return add-product OK immediately; draft mirror catches up later.
            _postHtmlDraftSyncScheduler.Schedule(ownerId);
        }

        return Ok(ApiResponse<FourSellerPostHtmlResultDto>.Ok(result, result.Message));
    }

    /// <summary>
    /// Links a Bunny CDN size chart image (from extension upload) to the supplier product snapshot.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost("supplier-product/size-chart-image")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> SetSupplierSizeChartImage(
        [FromBody] SupplierSizeChartImageRequest request,
        CancellationToken cancellationToken)
    {
        await _sizeChartImageValidator.ValidateAndThrowAsync(request, cancellationToken);
        await _supplierProductInfoService.SetSizeChartImageUrlAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã lưu ảnh Size Chart."));
    }

    /// <summary>Checks whether a size chart capture exists for the source product URL.</summary>
    [Authorize(Policy = "Permission:seller.products.view")]
    [HttpGet("supplier-product/size-chart-image")]
    [ProducesResponseType(typeof(ApiResponse<SupplierSizeChartImageStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierSizeChartImageStatusDto>>> GetSupplierSizeChartImage(
        [FromQuery] string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return Ok(ApiResponse<SupplierSizeChartImageStatusDto>.Ok(new SupplierSizeChartImageStatusDto()));
        }

        var status = await _supplierProductInfoService.GetSizeChartImageStatusAsync(
            sourceUrl.Trim(),
            cancellationToken);
        return Ok(ApiResponse<SupplierSizeChartImageStatusDto>.Ok(status));
    }
}
