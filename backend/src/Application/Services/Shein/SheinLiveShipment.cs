using Application.DTOs.Order;
using Domain.Entities;

namespace Application.Services.Shein;

/// <summary>
/// Bằng chứng đơn SHEIN còn sống (đang xử lý / đã gửi / đã giao).
///
/// Trang list SHEIN luôn gắn chrome "Return&amp;Refund" lên đơn đã shipped. Classifier cũ nuốt
/// chữ <c>refund</c> đó thành <c>refunded</c>, rồi <c>ApplyStatus</c> không cho hạ rank terminal
/// → billno mua lại (vd. GSU1RR21T00NNAA trên 577529580582638222) kẹt <c>IsCharged=false</c>,
/// sổ cái trống, hover không có chi tiết dù raw status = 5 và đã có mã carrier.
/// </summary>
internal static class SheinLiveShipment
{
    /// <summary>
    /// Payload đang nói đơn còn hiệu lực — không phải hoàn/huỷ thật (status 13/14/16).
    /// </summary>
    public static bool LooksLive(SheinOrderDto shein)
    {
        var raw = NormalizeRaw(shein.SheinStatus);
        if (IsTerminalRaw(raw)) return false;
        if (IsLiveRaw(raw)) return true;

        var group = OrderSyncService.NormalizeSheinStatusGroup(shein.SheinStatusGroup);
        return group is SupplierStatusGroups.Shipped
            or SupplierStatusGroups.Delivered
            or SupplierStatusGroups.Processing;
    }

    /// <summary>Group phục hồi khi stored đang terminal nhưng payload là đơn sống.</summary>
    public static string RecoverGroup(SheinOrderDto shein)
    {
        var raw = NormalizeRaw(shein.SheinStatus);
        if (raw is "6" or "delivered" or "common_delivered" or "received" or "signed")
        {
            return SupplierStatusGroups.Delivered;
        }

        if (raw is "5" or "shipped" or "in_transit" or "in-transit" or "dispatched")
        {
            return SupplierStatusGroups.Shipped;
        }

        if (raw is "3" or "7" or "processing" or "packing" or "preparing")
        {
            return SupplierStatusGroups.Processing;
        }

        var group = OrderSyncService.NormalizeSheinStatusGroup(shein.SheinStatusGroup);
        if (group is SupplierStatusGroups.Shipped
            or SupplierStatusGroups.Delivered
            or SupplierStatusGroups.Processing)
        {
            return group;
        }

        return SupplierStatusGroups.Shipped;
    }

    private static string NormalizeRaw(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsTerminalRaw(string raw) =>
        raw is "13" or "14" or "16" or "refunded" or "canceled" or "cancelled";

    private static bool IsLiveRaw(string raw) =>
        raw is "3" or "5" or "6" or "7"
            or "shipped" or "delivered" or "common_delivered"
            or "in_transit" or "in-transit" or "dispatched"
            or "processing" or "packing" or "preparing"
            or "received" or "signed";
}
