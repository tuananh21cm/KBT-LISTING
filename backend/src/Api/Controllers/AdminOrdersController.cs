using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Application.Services.Shein;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin order list/detail and 4Seller sync.
/// </summary>
[ApiController]
[Route("api/v1/admin/orders")]
public class AdminOrdersController : ControllerBase
{
    private readonly IOrderSyncService _orderSyncService;
    private readonly IOrderSyncJobRunner _orderSyncJobRunner;
    private readonly IOrderAfterSalesSyncService _afterSalesSync;
    private readonly ISheinRealtimeSyncService _sheinRealtimeSync;
    private readonly ISheinOrderDetailSweepService _sheinDetailSweep;
    private readonly IUspsScanPushService _uspsScanPush;

    /// <summary>
    /// Initializes the admin orders controller.
    /// </summary>
    public AdminOrdersController(
        IOrderSyncService orderSyncService,
        IOrderSyncJobRunner orderSyncJobRunner,
        IOrderAfterSalesSyncService afterSalesSync,
        ISheinRealtimeSyncService sheinRealtimeSync,
        ISheinOrderDetailSweepService sheinDetailSweep,
        IUspsScanPushService uspsScanPush)
    {
        _orderSyncService = orderSyncService;
        _orderSyncJobRunner = orderSyncJobRunner;
        _afterSalesSync = afterSalesSync;
        _sheinRealtimeSync = sheinRealtimeSync;
        _sheinDetailSweep = sheinDetailSweep;
        _uspsScanPush = uspsScanPush;
    }

    /// <summary>
    /// Lists synced orders from local DB.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OrderListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderListPageDto>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        // Tab «Đã mua» (extension): Fulfil chỉ thấy đơn mình stamp; Admin thấy tất cả (hoặc lọc paidByUserId tùy chọn).
        if (query.PurchasedToday == true && !User.IsInRole("Admin"))
        {
            query.PaidByUserId = User.GetUserId();
        }

        var result = await _orderSyncService.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderListPageDto>.Ok(result));
    }

    /// <summary>
    /// Workflow tab counts (same filters as list except the selected tab / paging).
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("status-counts")]
    [ProducesResponseType(typeof(ApiResponse<OrderStatusCountsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderStatusCountsDto>>> StatusCounts(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.GetStatusCountsAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderStatusCountsDto>.Ok(result));
    }

    /// <summary>
    /// Lists 4Seller shipping carriers for Mark Shipped (filtered by platform).
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("shipping-services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>>> ListShippingServices(
        [FromQuery] Guid ownerUserId,
        [FromQuery] string? platform,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == Guid.Empty)
        {
            return BadRequest(ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>.Fail("ownerUserId là bắt buộc."));
        }

        var result = await _orderSyncService.ListShippingServicesAsync(
            ownerUserId,
            platform,
            refresh,
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>.Ok(result));
    }

    /// <summary>
    /// Gets order detail with line items.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result));
    }

    /// <summary>
    /// Lightweight hover preview (items + sell/cost). Prefer over full detail for tooltips.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(typeof(ApiResponse<OrderHoverPreviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderHoverPreviewDto>>> GetPreview(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.GetPreviewAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderHoverPreviewDto>.Ok(result));
    }

    /// <summary>
    /// Marks a local order shipped on 4Seller (shipping service + tracking) and updates local DB.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/mark-ship")]
    [ProducesResponseType(typeof(ApiResponse<MarkShipResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MarkShipResultDto>>> MarkShip(
        Guid id,
        [FromBody] MarkShipRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.MarkShipAsync(id, request, cancellationToken);
        return Ok(ApiResponse<MarkShipResultDto>.Ok(result, result.Message ?? "Mark Shipped thành công."));
    }

    /// <summary>
    /// Updates supplier (NCC) cost breakdown. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/cost")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateCost(
        Guid id,
        [FromBody] UpdateOrderCostRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateCostAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật phí đơn hàng."));
    }

    /// <summary>
    /// Updates local fulfil pipeline milestones (purchase / tracking / in-transit / delivered / refund).
    /// Partial body: only provided (non-null) timestamp fields are applied.
    /// <c>clearFulfilPurchasedAt: true</c> clears the purchase stamp (undo accidental mark).
    /// Does not call 4Seller. First-time <c>fulfilPurchasedAt</c> also posts OrderChat + inbox notify to the seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/milestones")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateMilestones(
        Guid id,
        [FromBody] UpdateOrderMilestonesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateMilestonesAsync(
            id,
            request,
            User.GetUserId(),
            cancellationToken,
            HttpContext.TryGetFulfilSessionId());
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật mốc fulfil."));
    }

    /// <summary>
    /// Claims (or renews) an order for the current fulfil session (<c>X-Fulfil-Session</c>).
    /// Optionally claims siblings sharing phone+zip. Returns 409 when another user holds it.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/fulfil-claim")]
    [ProducesResponseType(typeof(ApiResponse<FulfilClaimResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilClaimResultDto>>> ClaimOrder(
        Guid id,
        [FromBody] ClaimOrderRequest? request,
        CancellationToken cancellationToken)
    {
        var sessionId = HttpContext.GetFulfilSessionId();
        var result = await _orderSyncService.ClaimAsync(
            id,
            sessionId,
            request?.SessionLabel,
            request?.SiblingOrderIds,
            User.GetUserId(),
            cancellationToken);
        return Ok(ApiResponse<FulfilClaimResultDto>.Ok(result, "Đã giữ đơn."));
    }

    /// <summary>
    /// Releases the claim for the current session. Lease clears only when no sessions remain.
    /// Query <c>allSessions=true</c> (holder or Admin) releases every session on the order.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete("{id:guid}/fulfil-claim")]
    [ProducesResponseType(typeof(ApiResponse<FulfilClaimResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilClaimResultDto>>> ReleaseClaim(
        Guid id,
        [FromQuery] bool allSessions = false,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");
        Guid? sessionId = allSessions ? null : HttpContext.GetFulfilSessionId();
        var result = await _orderSyncService.ReleaseClaimAsync(
            id,
            sessionId,
            User.GetUserId(),
            isAdmin,
            allSessions,
            cancellationToken);
        return Ok(ApiResponse<FulfilClaimResultDto>.Ok(
            result,
            result.Claim.SessionCount > 0
                ? $"Đã nhả phiên này — đơn vẫn giữ ở {result.Claim.SessionCount} phiên khác."
                : "Đã nhả đơn."));
    }

    /// <summary>
    /// Releases every open claim of the current Chrome-profile session (<c>X-Fulfil-Session</c>).
    /// Used on extension logout — does not depend on client order cache.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete("fulfil-claims/session")]
    [ProducesResponseType(typeof(ApiResponse<FulfilActiveClaimsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilActiveClaimsDto>>> ReleaseSessionClaims(
        CancellationToken cancellationToken)
    {
        var sessionId = HttpContext.GetFulfilSessionId();
        var result = await _orderSyncService.ReleaseSessionClaimsAsync(
            sessionId,
            User.GetUserId(),
            cancellationToken);
        return Ok(ApiResponse<FulfilActiveClaimsDto>.Ok(
            result,
            result.Items.Count > 0
                ? $"Đã nhả {result.Items.Count} đơn của phiên này."
                : "Không có đơn đang giữ ở phiên này."));
    }

    /// <summary>
    /// Lists orders currently held (lease not expired). Lightweight poll / panel sync.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("fulfil-claims")]
    [ProducesResponseType(typeof(ApiResponse<FulfilActiveClaimsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilActiveClaimsDto>>> ListActiveClaims(
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.ListActiveClaimsAsync(cancellationToken);
        return Ok(ApiResponse<FulfilActiveClaimsDto>.Ok(result));
    }

    /// <summary>
    /// Paged fulfil attempt history (per Chrome-profile session) for purchase-speed KPI.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("fulfil-attempts")]
    [ProducesResponseType(typeof(ApiResponse<FulfilAttemptListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FulfilAttemptListPageDto>>> ListAttempts(
        [FromQuery] FulfilAttemptListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.ListAttemptsAsync(query, cancellationToken);
        return Ok(ApiResponse<FulfilAttemptListPageDto>.Ok(result));
    }

    /// <summary>
    /// Marks shop payout received (or unpaid) on the receiving-bank card. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/settlement")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateSettlement(
        Guid id,
        [FromBody] UpdateOrderSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateSettlementAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsSettlementPaid ? "Đã đánh dấu thanh toán." : "Đã đánh dấu chưa thanh toán."));
    }

    /// <summary>
    /// Marks or clears USPS last-mile extra handling. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/usps-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateUspsProcessed(
        Guid id,
        [FromBody] UpdateOrderUspsProcessedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateUspsProcessedAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsUspsProcessed ? "Đã đánh dấu xử lý USPS." : "Đã bỏ đánh dấu xử lý USPS."));
    }

    /// <summary>
    /// Pushes selected USPS tracking numbers to PrintPackShip (create batch → send).
    /// Send is not idempotent and may charge the remote account.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("usps-scan/push")]
    [ProducesResponseType(typeof(ApiResponse<UspsScanPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UspsScanPushResultDto>>> PushUspsScan(
        [FromBody] PushUspsScanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _uspsScanPush.PushAsync(
            request.OrderIds,
            request.Notes,
            User.GetUserId(),
            ownerUserId: null,
            cancellationToken);
        return Ok(ApiResponse<UspsScanPushResultDto>.Ok(result, SummarizeUspsScanPush(result)));
    }

    /// <summary>
    /// Lists local PrintPackShip USPS scan batches for audit / retrieval.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("usps-scan/batches")]
    [ProducesResponseType(typeof(ApiResponse<UspsScanBatchListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UspsScanBatchListPageDto>>> ListUspsScanBatches(
        [FromQuery] UspsScanBatchListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _uspsScanPush.ListBatchesAsync(query, cancellationToken);
        return Ok(ApiResponse<UspsScanBatchListPageDto>.Ok(result));
    }

    /// <summary>
    /// Marks or clears ThankCard purchase and reconciles ThankCard fee + Manual ledger. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/thank-card-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateThankCardProcessed(
        Guid id,
        [FromBody] UpdateOrderThankCardProcessedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateThankCardProcessedAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsThankCardProcessed ? "Đã đánh dấu mua ThankCard." : "Đã bỏ đánh dấu mua ThankCard."));
    }

    /// <summary>
    /// Marks or clears ops REFUND handling. Does not call 4Seller or touch ledger.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/refund-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateRefundProcessed(
        Guid id,
        [FromBody] UpdateOrderRefundProcessedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateRefundProcessedAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsRefundProcessed ? "Đã đánh dấu xử lý Refund." : "Đã bỏ đánh dấu xử lý Refund."));
    }

    /// <summary>
    /// Updates buyer / seller / internal notes on 4Seller and local DB.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateNotes(
        Guid id,
        [FromBody] UpdateOrderNoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateNotesAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật ghi chú đơn hàng."));
    }

    /// <summary>
    /// Updates local operational notice under the order-card status line. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPut("{id:guid}/message")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateMessage(
        Guid id,
        [FromBody] UpdateOrderMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.UpdateMessageAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật thông báo đơn hàng."));
    }

    /// <summary>
    /// Ingests SHEIN account orders from the fulfil-extension and matches them to
    /// purchased/untracked local orders, filling tracking + cost breakdown.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("shein-sync")]
    [ProducesResponseType(typeof(ApiResponse<SheinOrderSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinOrderSyncResultDto>>> SheinSync(
        [FromBody] SheinOrderSyncRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.SyncFromSheinAsync(
            request ?? new SheinOrderSyncRequest(),
            User.GetUserId(),
            cancellationToken);

        return Ok(ApiResponse<SheinOrderSyncResultDto>.Ok(
            result,
            $"Khớp {result.Matched}/{result.Fetched} đơn SHEIN · track local +{result.UpdatedTracking} · auto ship {result.AutoShipped} · phí +{result.UpdatedCost}."));
    }

    /// <summary>
    /// Returns the extension SHEIN auto-sync config (enabled + interval seconds).
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet("shein-sync/config")]
    [ProducesResponseType(typeof(ApiResponse<SheinSyncConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinSyncConfigDto>>> SheinSyncConfig(
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.GetSheinSyncConfigAsync(cancellationToken);
        return Ok(ApiResponse<SheinSyncConfigDto>.Ok(result));
    }

    /// <summary>
    /// Local mode: sync one fulfil account immediately using stored SHEIN session (cookie + proxy).
    /// Called by the extension Sync Order button after pushing the session.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("shein-sync/run")]
    [ProducesResponseType(typeof(ApiResponse<SheinSyncRunResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinSyncRunResultDto>>> SheinSyncRun(
        [FromBody] SheinSyncRunRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccountId == Guid.Empty)
        {
            return BadRequest(ApiResponse<SheinSyncRunResultDto>.Fail("Thiếu accountId."));
        }

        var summary = await _sheinRealtimeSync.RunAccountAsync(request.AccountId, cancellationToken);
        return Ok(ApiResponse<SheinSyncRunResultDto>.Ok(
            new SheinSyncRunResultDto { Summary = summary },
            summary));
    }

    /// <summary>
    /// Runs one backend SHEIN detail sweep now: <c>get_order_info</c> for orders still missing real
    /// carrier tracking or a published Mark Ship, then Mark Ship via 4Seller. Ignores the enabled
    /// flag and the interval so a change can be verified without waiting out the cycle.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("shein-detail-sync/run")]
    [ProducesResponseType(typeof(ApiResponse<SheinDetailSweepResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinDetailSweepResult>>> SheinDetailSyncRun(
        CancellationToken cancellationToken)
    {
        var result = await _sheinDetailSweep.RunNowAsync(cancellationToken);
        return Ok(ApiResponse<SheinDetailSweepResult>.Ok(result, result.Summary));
    }

    /// <summary>
    /// Sync track for one order: <c>get_order_info</c> via stored fulfil-account session, refresh
    /// tracking + full cost breakdown, then Auto Mark Ship when eligible.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/sync-track")]
    [ProducesResponseType(typeof(ApiResponse<SheinOrderSyncTrackResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinOrderSyncTrackResult>>> SyncTrack(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sheinDetailSweep.RunForOrderAsync(
            id, User.GetUserId(), cancellationToken);

        return Ok(ApiResponse<SheinOrderSyncTrackResult>.Ok(result, result.Summary));
    }

    /// <summary>
    /// Refreshes one order's TikTok/platform commerce status from 4Seller by exact orderNo
    /// (bypasses create-date lookback of bulk sync). Does not set or clear REFUND.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/sync-platform-status")]
    [ProducesResponseType(typeof(ApiResponse<OrderPlatformStatusSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderPlatformStatusSyncResultDto>>> SyncPlatformStatus(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.SyncPlatformStatusAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderPlatformStatusSyncResultDto>.Ok(result, result.Summary));
    }

    /// <summary>
    /// Queues a 4Seller order sync (all sellers with cookie, or one owner) and returns its job id.
    /// The pull runs detached from this request so Cloudflare / browser timeouts cannot 524 mid-upsert;
    /// poll <c>sync/status/{jobId}</c> for the final counts.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<OrderSyncJobStatusDto>), StatusCodes.Status202Accepted)]
    public ActionResult<ApiResponse<OrderSyncJobStatusDto>> Sync([FromBody] OrderSyncRequest? request)
    {
        var job = _orderSyncJobRunner.Start(request ?? new OrderSyncRequest(), User.GetUserId());
        return Accepted(ApiResponse<OrderSyncJobStatusDto>.Ok(job, "Đã bắt đầu đồng bộ đơn hàng."));
    }

    /// <summary>
    /// Progress of a queued order sync.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpGet("sync/status/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderSyncJobStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<OrderSyncJobStatusDto>> SyncStatus(Guid jobId)
    {
        var job = _orderSyncJobRunner.GetStatus(jobId);
        if (job is null)
        {
            return NotFound(ApiResponse<OrderSyncJobStatusDto>.Fail(
                "Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        return Ok(ApiResponse<OrderSyncJobStatusDto>.Ok(job));
    }

    /// <summary>
    /// Runs the after-sales (return / refund) sync now, ignoring the configured intervals:
    /// asks 4Seller to pull from TikTok Shop, then reads the return cases back onto local orders.
    /// This is the only flow that sets or releases <c>REFUND</c>.
    /// </summary>
    /// <param name="request">Optional owner filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("after-sales/sync")]
    [ProducesResponseType(typeof(ApiResponse<OrderAfterSalesSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderAfterSalesSyncResultDto>>> AfterSalesSync(
        [FromBody] OrderAfterSalesSyncRequest? request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = request?.OwnerUserId;

        // 4Seller pulls from TikTok asynchronously, so a manual run reads whatever the previous
        // trigger already landed. Firing the trigger here keeps the next run fresh.
        var trigger = await _afterSalesSync.TriggerPlatformPullAsync(ownerUserId, cancellationToken);
        var result = await _afterSalesSync.PullAsync(ownerUserId, cancellationToken);

        foreach (var failure in trigger.FailedSellers)
        {
            if (result.FailedSellers.All(f => f.OwnerUserId != failure.OwnerUserId))
            {
                result.FailedSellers.Add(failure);
            }
        }

        var message = result.Skipped
            ? "Một lượt đồng bộ 4Seller khác đang chạy, thử lại sau ít phút."
            : result.FailedSellers.Count > 0
                ? $"Đồng bộ sau bán xong: {result.OrdersUpdated} đơn đổi trạng thái; {result.FailedSellers.Count} seller lỗi."
                : $"Đồng bộ sau bán xong: {result.OrdersUpdated} đơn đổi trạng thái (hoàn {result.Refunded}, gỡ hoàn {result.RefundReverted}).";

        return Ok(ApiResponse<OrderAfterSalesSyncResultDto>.Ok(result, message));
    }

    /// <summary>
    /// Deletes local orders matching list filters (ignores paging). Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<OrderDeleteResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDeleteResultDto>>> DeleteByFilter(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.DeleteByFilterAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderDeleteResultDto>.Ok(
            result,
            result.DeletedCount == 0
                ? "Không có đơn hàng nào khớp bộ lọc để xóa."
                : $"Đã xóa {result.DeletedCount} đơn hàng local."));
    }

    /// <summary>
    /// Deletes one local order (cascade items). Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteById(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _orderSyncService.DeleteByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đã xóa đơn hàng local."));
    }

    private static string SummarizeUspsScanPush(UspsScanPushResultDto result)
    {
        if (string.Equals(result.Status, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            return result.PagesFailed > 0
                ? $"Đã đẩy {result.SentCount} mã (batch #{result.BatchId}); {result.PagesFailed} trang lỗi."
                : $"Đã đẩy {result.SentCount} mã (batch #{result.BatchId}).";
        }

        if (string.Equals(result.Status, "SendUnknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Gửi batch không rõ kết quả — kiểm tra PrintPackShip trước khi đẩy lại.";
        }

        if (string.Equals(result.Status, "SendFailed", StringComparison.OrdinalIgnoreCase))
        {
            return result.Error ?? "Gửi batch thất bại.";
        }

        if (result.SentCount == 0 && result.Skipped.Count > 0)
        {
            return $"Không có đơn hợp lệ để đẩy ({result.Skipped.Count} bỏ qua).";
        }

        return "Hoàn tất đẩy track scan.";
    }
}
