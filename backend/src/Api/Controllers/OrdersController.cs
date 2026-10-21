using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Api.Extensions;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller order list/detail and 4Seller sync — always scoped to the authenticated seller.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderSyncService _orderSyncService;
    private readonly IOrderSyncJobRunner _orderSyncJobRunner;
    private readonly ISheinOrderDetailSweepService _sheinDetailSweep;
    private readonly IUspsScanPushService _uspsScanPush;

    /// <summary>
    /// Initializes the seller orders controller.
    /// </summary>
    public OrdersController(
        IOrderSyncService orderSyncService,
        IOrderSyncJobRunner orderSyncJobRunner,
        ISheinOrderDetailSweepService sheinDetailSweep,
        IUspsScanPushService uspsScanPush)
    {
        _orderSyncService = orderSyncService;
        _orderSyncJobRunner = orderSyncJobRunner;
        _sheinDetailSweep = sheinDetailSweep;
        _uspsScanPush = uspsScanPush;
    }

    /// <summary>
    /// Lists synced orders owned by the current seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OrderListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderListPageDto>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        ScopeListToCurrentUser(query);
        var result = await _orderSyncService.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderListPageDto>.Ok(result));
    }

    /// <summary>
    /// Workflow tab counts for the current seller (same filters as list except the selected tab / paging).
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("status-counts")]
    [ProducesResponseType(typeof(ApiResponse<OrderStatusCountsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderStatusCountsDto>>> StatusCounts(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        ScopeListToCurrentUser(query);
        var result = await _orderSyncService.GetStatusCountsAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderStatusCountsDto>.Ok(result));
    }

    /// <summary>
    /// Lists 4Seller shipping carriers for Mark Shipped (current seller's cookie).
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("shipping-services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>>> ListShippingServices(
        [FromQuery] string? platform,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderSyncService.ListShippingServicesAsync(
            User.GetOwnerId(),
            platform,
            refresh,
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FourSellerShippingServiceDto>>.Ok(result));
    }

    /// <summary>
    /// Gets order detail with line items (404 if not owned by current seller).
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await RequireOwnedOrderAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result));
    }

    /// <summary>
    /// Lightweight hover preview (items + sell/cost). Prefer over full detail for tooltips.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.view")]
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(typeof(ApiResponse<OrderHoverPreviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderHoverPreviewDto>>> GetPreview(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.GetPreviewAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderHoverPreviewDto>.Ok(result));
    }

    /// <summary>
    /// Marks a local order shipped on 4Seller and updates local DB.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("{id:guid}/mark-ship")]
    [ProducesResponseType(typeof(ApiResponse<MarkShipResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MarkShipResultDto>>> MarkShip(
        Guid id,
        [FromBody] MarkShipRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.MarkShipAsync(id, request, cancellationToken);
        return Ok(ApiResponse<MarkShipResultDto>.Ok(result, result.Message ?? "Mark Shipped thành công."));
    }

    /// <summary>
    /// Sync track for an owned order: SHEIN <c>get_order_info</c>, refresh tracking + cost, Mark Ship.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("{id:guid}/sync-track")]
    [ProducesResponseType(typeof(ApiResponse<SheinOrderSyncTrackResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SheinOrderSyncTrackResult>>> SyncTrack(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _sheinDetailSweep.RunForOrderAsync(
            id, User.GetOwnerId(), cancellationToken);

        return Ok(ApiResponse<SheinOrderSyncTrackResult>.Ok(result, result.Summary));
    }

    /// <summary>
    /// Refreshes one owned order's TikTok/platform commerce status from 4Seller by exact orderNo.
    /// Does not set or clear REFUND.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("{id:guid}/sync-platform-status")]
    [ProducesResponseType(typeof(ApiResponse<OrderPlatformStatusSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderPlatformStatusSyncResultDto>>> SyncPlatformStatus(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.SyncPlatformStatusAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderPlatformStatusSyncResultDto>.Ok(result, result.Summary));
    }

    /// <summary>
    /// Updates supplier (NCC) cost breakdown. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/cost")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateCost(
        Guid id,
        [FromBody] UpdateOrderCostRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateCostAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật phí đơn hàng."));
    }

    /// <summary>
    /// Updates local fulfil pipeline milestones. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/milestones")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateMilestones(
        Guid id,
        [FromBody] UpdateOrderMilestonesRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateMilestonesAsync(
            id,
            request,
            User.GetOwnerId(),
            cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật mốc fulfil."));
    }

    /// <summary>
    /// Marks shop payout received (or unpaid). Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/settlement")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateSettlement(
        Guid id,
        [FromBody] UpdateOrderSettlementRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateSettlementAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsSettlementPaid ? "Đã đánh dấu thanh toán." : "Đã đánh dấu chưa thanh toán."));
    }

    /// <summary>
    /// Marks or clears USPS last-mile extra handling. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/usps-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateUspsProcessed(
        Guid id,
        [FromBody] UpdateOrderUspsProcessedRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateUspsProcessedAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsUspsProcessed ? "Đã đánh dấu xử lý USPS." : "Đã bỏ đánh dấu xử lý USPS."));
    }

    /// <summary>
    /// Pushes selected USPS tracking numbers to PrintPackShip (create batch → send).
    /// Send is not idempotent and may charge the remote account. Scoped to the current seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("usps-scan/push")]
    [ProducesResponseType(typeof(ApiResponse<UspsScanPushResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UspsScanPushResultDto>>> PushUspsScan(
        [FromBody] PushUspsScanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _uspsScanPush.PushAsync(
            request.OrderIds,
            request.Notes,
            User.GetOwnerId(),
            ownerUserId: User.GetOwnerId(),
            cancellationToken);
        return Ok(ApiResponse<UspsScanPushResultDto>.Ok(result, SummarizeUspsScanPush(result)));
    }

    /// <summary>
    /// Marks or clears ThankCard purchase and reconciles ThankCard fee + Manual ledger. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/thank-card-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateThankCardProcessed(
        Guid id,
        [FromBody] UpdateOrderThankCardProcessedRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateThankCardProcessedAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsThankCardProcessed ? "Đã đánh dấu mua ThankCard." : "Đã bỏ đánh dấu mua ThankCard."));
    }

    /// <summary>
    /// Marks or clears ops REFUND handling. Does not call 4Seller or touch ledger.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/refund-processed")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateRefundProcessed(
        Guid id,
        [FromBody] UpdateOrderRefundProcessedRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateRefundProcessedAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(
            result,
            request.IsRefundProcessed ? "Đã đánh dấu xử lý Refund." : "Đã bỏ đánh dấu xử lý Refund."));
    }

    /// <summary>
    /// Updates buyer / seller / internal notes on 4Seller and local DB.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateNotes(
        Guid id,
        [FromBody] UpdateOrderNoteRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateNotesAsync(id, request, cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật ghi chú đơn hàng."));
    }

    /// <summary>
    /// Updates local operational notice under the order-card status line. Does not call 4Seller.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPut("{id:guid}/message")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateMessage(
        Guid id,
        [FromBody] UpdateOrderMessageRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnedOrderAsync(id, cancellationToken);
        var result = await _orderSyncService.UpdateMessageAsync(id, request, User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<OrderDetailDto>.Ok(result, "Đã cập nhật thông báo đơn hàng."));
    }

    /// <summary>
    /// Queues a 4Seller order sync for the current seller and returns its job id. The pull runs
    /// detached from this request; poll <c>sync/status/{jobId}</c> for the final counts.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<OrderSyncJobStatusDto>), StatusCodes.Status202Accepted)]
    public ActionResult<ApiResponse<OrderSyncJobStatusDto>> Sync([FromBody] OrderSyncRequest? request)
    {
        var body = request ?? new OrderSyncRequest();
        body.OwnerUserId = User.GetOwnerId();
        var job = _orderSyncJobRunner.Start(body, User.GetOwnerId());
        return Accepted(ApiResponse<OrderSyncJobStatusDto>.Ok(job, "Đã bắt đầu đồng bộ đơn hàng."));
    }

    /// <summary>
    /// Progress of a queued order sync. Only the seller who started the job can read it.
    /// </summary>
    [Authorize(Policy = "Permission:seller.orders.manage")]
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

        if (_orderSyncJobRunner.GetStartedByUserId(jobId) != User.GetOwnerId())
        {
            return NotFound(ApiResponse<OrderSyncJobStatusDto>.Fail(
                "Không tìm thấy phiên đồng bộ (có thể đã hết hạn)."));
        }

        return Ok(ApiResponse<OrderSyncJobStatusDto>.Ok(job));
    }

    private void ScopeListToCurrentUser(OrderListQuery query)
    {
        query.OwnerUserId = User.GetOwnerId();
    }

    private async Task<OrderDetailDto> RequireOwnedOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.GetByIdAsync(id, cancellationToken);
        if (result.OwnerId != User.GetOwnerId())
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        return result;
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
