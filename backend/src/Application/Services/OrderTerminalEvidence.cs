using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>
/// Chuẩn hoá mốc Hủy/Refund trên timeline fulfil theo trạng thái thương mại TikTok/4Seller.
///
/// Timeline từng có thể hiện <c>Refund</c> trong khi cột trạng thái vẫn <c>Đã giao</c>/<c>Đang giao</c>
/// vì mốc <c>RefundedAt</c>/<c>CanceledAt</c> được stamp tay hoặc từ SHEIN hoàn NCC — không đụng
/// <see cref="Order.PlatformOrderStatus"/>. Commerce terminal chỉ thuộc Sync 4Seller.
/// </summary>
public static class OrderTerminalEvidence
{
    /// <summary>
    /// True khi đơn còn mốc Hủy/Refund trong khi sàn chưa (hoặc không còn) hủy/hoàn.
    /// </summary>
    public static bool HasConflictingTerminal(Order order)
    {
        var platform = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
        var isRefund = PlatformOrderStatuses.IsRefund(platform);
        var isCancel = PlatformOrderStatuses.IsCanceled(platform);

        if (order.RefundedAt is not null && !isRefund && !IsPlatformRefundClock(order)) return true;
        if (order.CanceledAt is not null && !isCancel && !isRefund) return true;
        return false;
    }

    /// <summary>
    /// Mốc Refund do đồng bộ đơn sau bán stamp (<see cref="OrderFulfilTimeline.Source.Platform"/>)
    /// là bằng chứng hoàn của sàn, không phải stamp tay / SHEIN hoàn NCC. Khi nó lệch cột trạng thái
    /// thì cột mới là chỗ sai — order list vẫn báo đơn đã hoàn dưới status thương mại. Xoá mốc ở đây
    /// là xoá đúng bằng chứng mà luồng sau bán dùng để giữ REFUND.
    /// </summary>
    private static bool IsPlatformRefundClock(Order order) =>
        OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.Refunded)
            == OrderFulfilTimeline.Source.Platform;

    /// <summary>
    /// Xoá mốc Hủy/Refund lệch status sàn. Trả <c>true</c> khi có thay đổi.
    /// Giữ mốc khi platform là <c>REFUND</c> (cả Refunded + Canceled) hoặc <c>CANCELLED</c> (chỉ Canceled).
    /// </summary>
    public static bool ClearConflictingTerminals(Order order)
    {
        if (!HasConflictingTerminal(order)) return false;

        var platform = PlatformOrderStatuses.Normalize(order.PlatformOrderStatus);
        var isRefund = PlatformOrderStatuses.IsRefund(platform);
        var isCancel = PlatformOrderStatuses.IsCanceled(platform);
        var changed = false;

        if (order.RefundedAt is not null && !isRefund && !IsPlatformRefundClock(order))
        {
            OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Refunded);
            changed = true;
        }

        if (order.CanceledAt is not null && !isCancel && !isRefund)
        {
            OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.Canceled);
            changed = true;
        }

        return changed;
    }
}
