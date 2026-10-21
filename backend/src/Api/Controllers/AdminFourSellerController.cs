using Application.DTOs.FourSeller;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Constants;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin endpoints for 4Seller integration.
/// </summary>
[ApiController]
[Route("api/v1/admin/fourseller")]
public class AdminFourSellerController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IProductScrapeService _productScrapeService;

    /// <summary>
    /// Initializes the admin 4Seller controller.
    /// </summary>
    public AdminFourSellerController(
        IShopService shopService,
        IFourSellerApiService fourSellerApiService,
        IProductScrapeService productScrapeService)
    {
        _shopService = shopService;
        _fourSellerApiService = fourSellerApiService;
        _productScrapeService = productScrapeService;
    }

    /// <summary>
    /// Creates an OAuth authorize URL for connecting a shop to 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("shops/{shopId:guid}/auth-redirect-url")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerAuthRedirectResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerAuthRedirectResponse>>> GetAuthRedirectUrl(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shop = await _shopService.GetByIdForAdminAsync(shopId, cancellationToken);

        var request = new FourSellerAuthRedirectRequest
        {
            PlatformCode = shop.PlatformCode.ToLowerInvariant(),
            ShopName = shop.Name,
            SiteList = [string.IsNullOrWhiteSpace(shop.Region) ? "US" : shop.Region],
            ShopifyShop = shop.ShopUrl ?? string.Empty
        };

        var result = await _fourSellerApiService.GetAuthRedirectUrlAsync(
            User.GetUserId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerAuthRedirectResponse>.Ok(result));
    }

    /// <summary>
    /// Checks whether a shop is connected on 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("shops/{shopId:guid}/check-connection")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerShopCheckResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerShopCheckResponse>>> CheckConnection(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shop = await _shopService.GetByIdForAdminAsync(shopId, cancellationToken);

        var request = new FourSellerShopCheckRequest
        {
            ShopName = shop.Name,
            PlatformCode = shop.PlatformCode,
            ExternalShopId = shop.ExternalShopId,
            FourSellerShopId = shop.FourSellerShopId,
            Region = string.IsNullOrWhiteSpace(shop.Region) ? "US" : shop.Region
        };

        var result = await _fourSellerApiService.CheckShopConnectionAsync(
            User.GetUserId(),
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
    /// Syncs local shops from 4Seller: mọi user có cookie <c>logged_in</c> → tidy-list → cập nhật shop theo OwnerId.
    /// Không dùng cookie của admin đang đăng nhập.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("shops/sync")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerSyncResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FourSellerSyncResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FourSellerSyncResponse>>> SyncAllShops(
        CancellationToken cancellationToken)
    {
        var response = await _shopService.SyncAllFromLoggedInCookiesAsync(cancellationToken);

        if (response.SyncedAccounts == 0 && response.FailedAccounts.Count == 0)
        {
            return BadRequest(ApiResponse<FourSellerSyncResponse>.Fail(
                "Không có tài khoản nào có cookie 4Seller đang login. Vào Quản lý Người dùng → Check login, rồi Đồng bộ lại."));
        }

        var message =
            $"Đồng bộ xong: {response.SyncedAccounts} tài khoản, cập nhật {response.UpdatedCount}, relink id {response.RelinkedCount}, gắn lại {response.OrdersRelinkedCount} đơn, bỏ qua {response.SkippedCount} (4Seller {response.FourSellerShopCount}).";
        if (response.FailedAccounts.Count > 0)
        {
            message =
                $"{message} {response.FailedAccounts.Count} tài khoản lỗi.";
        }
        if (response.UnmatchedShops.Count > 0)
        {
            var actionable = response.UnmatchedShops.Count(s => FourSellerUnmatchedReasons.IsActionable(s.Reason));
            if (actionable > 0)
            {
                message = $"{message} {actionable} shop 4Seller chưa map local.";
            }
        }

        return Ok(ApiResponse<FourSellerSyncResponse>.Ok(response, message));
    }

    /// <summary>
    /// Lists shops from 4Seller (for crawl save-to-platform shopIdList = record.Id).
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.view")]
    [HttpGet("shops")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FourSellerShopRecordDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FourSellerShopRecordDto>>>> GetFourSellerShops(
        CancellationToken cancellationToken)
    {
        var shops = await _fourSellerApiService.GetShopListAsync(User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FourSellerShopRecordDto>>.Ok(shops));
    }

    /// <summary>
    /// Submits product URLs to 4Seller crawl and creates a local ownership batch.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPost("crawl/urls")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerCrawlUrlsResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerCrawlUrlsResponse>>> SubmitCrawlUrls(
        [FromBody] FourSellerCrawlUrlsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _productScrapeService.SubmitUrlsAsync(
            User.GetUserId(),
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerCrawlUrlsResponse>.Ok(result));
    }

    /// <summary>
    /// Lists owned scrape drafts (claims pending batch URLs against 4Seller first).
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.view")]
    [HttpPost("crawl/drafts")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerMoveDraftPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerMoveDraftPageDto>>> GetMoveDrafts(
        [FromBody] FourSellerMoveDraftPageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _productScrapeService.GetDraftsAsync(
            User.GetUserId(),
            User.IsInRole(RoleNames.Admin),
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerMoveDraftPageDto>.Ok(result));
    }

    /// <summary>
    /// Moves owned scraped drafts onto 4Seller platform shops.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPost("crawl/save-to-platform")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerSaveToPlatformResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerSaveToPlatformResponse>>> SaveToPlatform(
        [FromBody] FourSellerSaveToPlatformRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _productScrapeService.SaveToPlatformAsync(
            User.GetUserId(),
            User.IsInRole(RoleNames.Admin),
            request,
            cancellationToken);
        return Ok(ApiResponse<FourSellerSaveToPlatformResponse>.Ok(result));
    }

    /// <summary>
    /// Polls 4Seller crawl/save schedule progress.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.view")]
    [HttpGet("crawl/schedule")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerScheduleProgressDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerScheduleProgressDto>>> GetSchedule(
        [FromQuery] string scheduleCode,
        CancellationToken cancellationToken)
    {
        var result = await _productScrapeService.GetScheduleAsync(
            User.GetUserId(),
            scheduleCode,
            cancellationToken);
        return Ok(ApiResponse<FourSellerScheduleProgressDto>.Ok(result));
    }

    /// <summary>
    /// Deletes owned 4Seller crawl move-drafts.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPost("crawl/delete-drafts")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteDrafts(
        [FromBody] FourSellerDeleteDraftsRequest request,
        CancellationToken cancellationToken)
    {
        await _productScrapeService.DeleteDraftsAsync(
            User.GetUserId(),
            User.IsInRole(RoleNames.Admin),
            request,
            cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted = request.MoveDraftIds.Count }));
    }
}
