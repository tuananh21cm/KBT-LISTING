using Api.Extensions;
using Application.DTOs.Order;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Hàng đợi đơn NCC: mọi lần mua đã ghi nhận, kể cả lần chưa gắn được đơn sàn nào.
///
/// Khớp tự động cần phone + zip trùng tuyệt đối nên luôn có phần trượt. Trước đây trượt là mất
/// luôn dấu vết; các endpoint dưới đây là đường để người xử lý kết thúc nốt phần đó.
/// </summary>
[ApiController]
[Route("api/v1/admin/supplier-purchases")]
public class AdminSupplierPurchasesController : ControllerBase
{
    private readonly IOrderSyncService _orderSyncService;

    /// <summary>Initializes the controller.</summary>
    public AdminSupplierPurchasesController(IOrderSyncService orderSyncService)
    {
        _orderSyncService = orderSyncService;
    }

    /// <summary>Lists supplier purchases (defaults to the unmatched queue when no filter given).</summary>
    [Authorize(Policy = "Permission:admin.orders.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<SupplierPurchaseListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierPurchaseListPageDto>>> List(
        [FromQuery] string? matchState,
        [FromQuery] string? skipReason,
        [FromQuery] Guid? fulfilAccountId,
        [FromQuery] Guid? orderId,
        [FromQuery] Guid? paidByUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? keyword,
        [FromQuery] int pageCurrent = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderSyncService.ListSupplierPurchasesAsync(
            new SupplierPurchaseQuery
            {
                MatchState = matchState,
                SkipReason = skipReason,
                FulfilAccountId = fulfilAccountId,
                OrderId = orderId,
                PaidByUserId = paidByUserId,
                From = from,
                To = to,
                Keyword = keyword,
                PageCurrent = pageCurrent,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(ApiResponse<SupplierPurchaseListPageDto>.Ok(result));
    }

    /// <summary>
    /// Attaches a purchase to a local order and rebuilds cost + ledger for both orders.
    /// Optional <c>fulfilAccountId</c> fills the purchase attribution when the SHEIN billing email
    /// mapped to no fulfil account; it never overwrites an existing one.
    /// </summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/attach")]
    [ProducesResponseType(typeof(ApiResponse<SupplierPurchaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierPurchaseDto>>> Attach(
        Guid id,
        [FromBody] AttachSupplierPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.AttachSupplierPurchaseAsync(
            id, request.OrderId, User.GetUserId(), request.FulfilAccountId, cancellationToken);
        return Ok(ApiResponse<SupplierPurchaseDto>.Ok(result, "Đã gắn đơn NCC vào đơn hàng."));
    }

    /// <summary>Detaches a purchase from its order and returns it to the queue.</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/detach")]
    [ProducesResponseType(typeof(ApiResponse<SupplierPurchaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierPurchaseDto>>> Detach(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderSyncService.DetachSupplierPurchaseAsync(
            id, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<SupplierPurchaseDto>.Ok(result, "Đã gỡ đơn NCC khỏi đơn hàng."));
    }

    /// <summary>Marks a queued purchase as reviewed-and-skipped, or puts it back in the queue.</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("{id:guid}/ignore")]
    [ProducesResponseType(typeof(ApiResponse<SupplierPurchaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierPurchaseDto>>> Ignore(
        Guid id,
        [FromQuery] bool ignored = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderSyncService.IgnoreSupplierPurchaseAsync(
            id, ignored, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<SupplierPurchaseDto>.Ok(
            result, ignored ? "Đã đánh dấu bỏ qua." : "Đã trả về hàng đợi."));
    }

    /// <summary>Xóa hẳn một lần mua NCC (kèm dòng chi CostUpdate nếu còn).</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _orderSyncService.DeleteSupplierPurchaseAsync(
            id, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, "Đã xóa lần mua NCC."));
    }

    /// <summary>Xóa hàng loạt lần mua NCC (cùng ngữ nghĩa với DELETE đơn lẻ; tối đa 200 id).</summary>
    [Authorize(Policy = "Permission:admin.orders.manage")]
    [HttpPost("bulk-delete")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> BulkDelete(
        [FromBody] BulkDeleteSupplierPurchasesRequest request,
        CancellationToken cancellationToken)
    {
        var deleted = await _orderSyncService.BulkDeleteSupplierPurchasesAsync(
            request.Ids, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted }, $"Đã xóa {deleted} lần mua NCC."));
    }
}
