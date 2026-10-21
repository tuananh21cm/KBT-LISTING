using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Per-order 4Seller commerce-status refresh (exact orderNo search — bypasses create-date lookback).
/// </summary>
public partial class OrderSyncService
{
    /// <summary>
    /// Bucket dùng khi tra một đơn theo platform orderNo. 4Seller vẫn honour <c>searchValue</c>
    /// với <c>orderStatus=all</c> (kiểm chứng bằng
    /// <c>GET /dev/diagnostics/order-page-search-probe/{orderNo}</c>), nên một truy vấn là đủ cho
    /// mọi trạng thái — kể cả <c>on_hold</c> mà danh sách bucket cũ không hề phủ.
    /// </summary>
    private const string PlatformStatusLookupBucket = AllStatusBucket;

    /// <inheritdoc />
    public async Task<OrderPlatformStatusSyncResultDto> SyncPlatformStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.Shop)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        var previousStatus = order.PlatformOrderStatus;
        var orderNo = order.PlatformOrderId?.Trim() ?? string.Empty;
        if (orderNo.Length == 0)
        {
            throw new BusinessException(
                "Đơn chưa có mã sàn (platformOrderId) — không tra được trên 4Seller.");
        }

        var remote = await FetchRemoteOrderForPlatformStatusAsync(order, orderNo, cancellationToken);
        if (remote is null)
        {
            return new OrderPlatformStatusSyncResultDto
            {
                Found = false,
                Changed = false,
                PreviousPlatformOrderStatus = previousStatus,
                PlatformOrderStatus = order.PlatformOrderStatus,
                WorkflowTab = order.WorkflowTab ?? OrderWorkflowTabs.Resolve(order),
                TrackingNo = order.TrackingNo,
                Summary = "4Seller không tìm thấy đơn này."
            };
        }

        if (string.IsNullOrWhiteSpace(remote.PlatformOrderStatus)
            && !PlatformOrderStatuses.IsCanceled(order.PlatformOrderStatus)
            && !PlatformOrderStatuses.IsRefund(order.PlatformOrderStatus)
            && !string.Equals(
                order.PlatformOrderStatus,
                PlatformOrderStatuses.Delivered,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Sync platform status: 4Seller trả platformOrderStatus rỗng cho đơn {OrderNo} "
                + "(local={LocalStatus}, remoteOrderStatus={RemoteOrderStatus}) — giữ trạng thái local",
                orderNo,
                order.PlatformOrderStatus,
                remote.OrderStatus);
        }

        var now = DateTime.UtcNow;
        // Keep owner + shop: pass current OwnerId and null localShopId so ApplyRecord does not remap.
        LogClearedMilestones(
            order,
            ApplyRecord(
                order,
                remote,
                order.OwnerId,
                localShopId: null,
                now,
                assignRawPayload: true));
        var structuralItemChange = SyncItems(order, remote.OrderItemInfoList, now);

        _db.ChangeTracker.DetectChanges();
        var orderChanged = _db.Entry(order).State == EntityState.Modified;
        var itemsChanged = structuralItemChange || order.Items.Any(i =>
        {
            var state = _db.Entry(i).State;
            return state is EntityState.Added or EntityState.Modified;
        });
        var commerceChanged = orderChanged || itemsChanged;

        // Always stamp LastSyncedAt when 4Seller answered — distinguishes "checked, identical"
        // from "never reached by lookback bulk sync".
        StampOrderTimestamps(order, now, stampAllItems: false);
        await _db.SaveChangesAsync(cancellationToken);

        var workflowTab = order.WorkflowTab ?? OrderWorkflowTabs.Resolve(order);
        var summary = BuildPlatformStatusSyncSummary(
            commerceChanged,
            previousStatus,
            order.PlatformOrderStatus);

        return new OrderPlatformStatusSyncResultDto
        {
            Found = true,
            Changed = commerceChanged,
            PreviousPlatformOrderStatus = previousStatus,
            PlatformOrderStatus = order.PlatformOrderStatus,
            WorkflowTab = workflowTab,
            TrackingNo = order.TrackingNo,
            Summary = summary
        };
    }

    /// <summary>
    /// Looks up the remote 4Seller record by exact platform orderNo across commerce buckets.
    /// Returns null when no bucket contains a match (does not invent a miss as cancel).
    /// </summary>
    private async Task<FourSellerOrderRecordDto?> FetchRemoteOrderForPlatformStatusAsync(
        Order order,
        string orderNo,
        CancellationToken cancellationToken)
    {
        var cookieOwnerId = await OrderFourSellerOwner.ResolveAsync(
            order, _db.Shops, cancellationToken);

        var page = await _fourSeller.GetOrderPageAsync(
            cookieOwnerId,
            new FourSellerOrderPageRequest
            {
                OrderStatus = PlatformStatusLookupBucket,
                PageCurrent = 1,
                PageSize = 10,
                SearchType = "orderNo",
                SearchMethod = "exact",
                SearchValue = orderNo,
                Platform = order.Platform?.Trim() ?? string.Empty
            },
            cancellationToken);

        return MatchRemoteOrderRecord(page.Records, order.FourSellerOrderId, orderNo);
    }

    /// <summary>
    /// Picks the record that matches the local order id or platform order number.
    /// </summary>
    internal static FourSellerOrderRecordDto? MatchRemoteOrderRecord(
        IReadOnlyList<FourSellerOrderRecordDto> records,
        long fourSellerOrderId,
        string orderNo)
    {
        return records.FirstOrDefault(r =>
            r.OrderId == fourSellerOrderId
            || string.Equals(r.PlatformOrderId?.Trim(), orderNo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.PlatformOrderNo?.Trim(), orderNo, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildPlatformStatusSyncSummary(
        bool changed,
        string? previousStatus,
        string? currentStatus)
    {
        var prevLabel = FormatPlatformStatusLabel(previousStatus);
        var currLabel = FormatPlatformStatusLabel(currentStatus);
        if (!changed)
        {
            return $"Không đổi (sàn vẫn {currLabel}).";
        }

        if (string.Equals(previousStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return $"Đã cập nhật thông tin đơn (trạng thái vẫn {currLabel}).";
        }

        return $"Đã cập nhật: {prevLabel} → {currLabel}.";
    }

    private static string FormatPlatformStatusLabel(string? status)
    {
        var n = PlatformOrderStatuses.Normalize(status);
        return n switch
        {
            PlatformOrderStatuses.OnHold => "Tạm giữ",
            PlatformOrderStatuses.AwaitingShipment => "Chờ giao",
            PlatformOrderStatuses.AwaitingCollection => "Chờ lấy",
            PlatformOrderStatuses.InTransit => "Đang giao",
            PlatformOrderStatuses.Delivered => "Đã giao",
            PlatformOrderStatuses.Cancelled => "Đã hủy",
            PlatformOrderStatuses.Refund => "Refund",
            null or "" => "(trống)",
            _ => n
        };
    }
}
