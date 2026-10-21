using Application.DTOs.FourSeller;
using Domain.Entities;
using Domain.Orders;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// 4Seller → local order apply helpers for tracking ownership and platform status timeline bridge.
/// </summary>
public static class OrderFourSellerStatusApply
{
    private static readonly System.Text.RegularExpressions.Regex SheinWarehouseTrackingRegex =
        new(@"^[CN]\d{10,}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Classifies SHEIN / platform tracking as carrier, warehouse, or none.</summary>
    public static string ClassifyTracking(string? trackingNo, string? carrier)
    {
        if (string.IsNullOrWhiteSpace(trackingNo)) return SupplierTrackingKinds.None;
        var t = trackingNo.Trim();
        if (SheinWarehouseTrackingRegex.IsMatch(t)) return SupplierTrackingKinds.Warehouse;
        var c = (carrier ?? string.Empty).Trim();
        if (c.Contains("warehouse", StringComparison.OrdinalIgnoreCase)
            || c.Contains("international warehouse", StringComparison.OrdinalIgnoreCase)
            || c.Contains("local warehouse", StringComparison.OrdinalIgnoreCase))
        {
            return SupplierTrackingKinds.Warehouse;
        }

        return SupplierTrackingKinds.Carrier;
    }

    /// <summary>
    /// Bridges các clock sự kiện thật từ 4Seller: mốc huỷ/hoàn, và mốc mã vận đơn lên sàn.
    ///
    /// 4Seller là hệ thống duy nhất biết thời điểm mã vận đơn được công bố lên sàn: payload SHEIN
    /// chỉ mang <c>shipping_no</c> trần, không kèm bất kỳ mốc thời gian nào cho việc gán mã (đã
    /// đối chiếu 158 payload gốc). Nếu không lấy <c>OrderShippedTime</c> ở đây thì mốc Track sẽ
    /// vĩnh viễn trống với mọi đơn không đi qua Mark Ship thủ công — đúng triệu chứng "Track?".
    ///
    /// Rank <see cref="OrderFulfilTimeline.Source.Platform"/> thấp hơn Shein và Manual nên Mark
    /// Ship và chỉnh tay vẫn ghi đè được. <c>TrackingNo</c>/<c>ShippingService</c> được seed từ
    /// list sync khi payload có mã (xem <see cref="ApplyTrackingFromRecord"/>); Mark Ship vẫn
    /// được đè sau. Không clear mã local khi payload trống.
    /// </summary>
    public static IReadOnlyList<OrderFulfilTimeline.Cleared> ApplyPlatformClocks(
        Order order,
        FourSellerOrderTimeInfoDto? time)
    {
        var cancelAt = time is not null
            ? AppTimeZones.ParseFourSellerNaiveAsUtc(time.CancelTime)
            : null;

        // Mốc rời kho của SHEIN lệch mốc này trung bình 6,6 giờ và khác nhau ở 144/154 đơn đo
        // được, nên Track và Transit là hai sự kiện tách biệt chứ không phải một.
        var shippedAt = time is not null
            ? AppTimeZones.ParseFourSellerNaiveAsUtc(time.OrderShippedTime)
            : null;
        OrderFulfilTimeline.Stamp(
            order, OrderFulfilTimeline.Milestone.TrackingAdded,
            shippedAt, OrderFulfilTimeline.Source.Platform);

        if (PlatformOrderStatuses.IsCanceledCommerce(order.PlatformOrderStatus, order.OrderStatus)
            || PlatformOrderStatuses.IsRefund(order.PlatformOrderStatus))
        {
            // CancelTime is a real event clock; a canceled/refunded order with no clock stays empty.
            OrderFulfilTimeline.Stamp(
                order, OrderFulfilTimeline.Milestone.Canceled,
                cancelAt, OrderFulfilTimeline.Source.Platform);
        }

        // Mốc Refunded thuộc luồng đồng bộ đơn sau bán (returns/tiktok/page) — order list không
        // mang clock hoàn, CancelTime ở đây chỉ là mốc đóng đơn.
        return OrderFulfilTimeline.Normalize(order);
    }

    /// <summary>
    /// Writes 4Seller remote <c>orderStatus</c> bucket onto the local order (stats / sync filters).
    /// Empty remote keeps existing; blank local seeds <c>unknown</c>.
    /// </summary>
    public static void ApplyOrderStatusFromRecord(Order order, string? remoteOrderStatus)
    {
        var normalized = NormalizeFourSellerOrderStatus(remoteOrderStatus);
        if (normalized.Length > 0)
        {
            order.OrderStatus = normalized;
            return;
        }

        if (string.IsNullOrWhiteSpace(order.OrderStatus))
        {
            order.OrderStatus = "unknown";
        }
    }

    /// <summary>Normalizes 4Seller list bucket (<c>cancelled</c> → <c>canceled</c>).</summary>
    public static string NormalizeFourSellerOrderStatus(string? remoteOrderStatus)
    {
        if (string.IsNullOrWhiteSpace(remoteOrderStatus)) return string.Empty;
        var value = remoteOrderStatus.Trim();
        if (string.Equals(value, "cancelled", StringComparison.OrdinalIgnoreCase))
            return "canceled";
        return value;
    }

    /// <summary>
    /// Seeds <see cref="Order.TrackingNo"/> / <see cref="Order.ShippingService"/> from list payload
    /// when present. Never clears local values when remote is empty. Never seeds SHEIN warehouse
    /// codes (<c>C…</c>/<c>N…</c>) onto the platform order — those must stay on
    /// <c>SupplierPurchases</c> only so Auto Mark Ship can still publish real carrier tracking.
    /// Mark Ship may overwrite later.
    /// </summary>
    public static void ApplyTrackingFromRecord(Order order, FourSellerOrderRecordDto record)
    {
        if (!string.IsNullOrWhiteSpace(record.TrackingNo))
        {
            var tracking = record.TrackingNo.Trim();
            var shippingService = record.ShippingServiceInfo?.ShippingService;
            if (ClassifyTracking(tracking, shippingService) != SupplierTrackingKinds.Warehouse)
            {
                order.TrackingNo = tracking;
            }
        }

        var service = record.ShippingServiceInfo?.ShippingService;
        if (!string.IsNullOrWhiteSpace(service))
        {
            order.ShippingService = service.Trim();
        }
    }
}
