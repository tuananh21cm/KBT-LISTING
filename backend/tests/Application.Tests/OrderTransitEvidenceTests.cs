using Application.Services;
using Domain.Entities;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Chốt quy tắc phân biệt mốc Transit thật với mốc chỉ là bản sao clock 4Seller. Đo trên dữ liệu
/// thật: 93/246 đơn có Transit nguồn <c>platform</c> trùng khít mốc Track từng giây, nên khoảng giữa
/// Track và Transit bằng 0 và UI không hiện bộ đếm.
/// </summary>
public class OrderTransitEvidenceTests
{
    private static Order OrderWith(
        DateTime? transit,
        DateTime? track,
        string? transitSource,
        DateTime? shippedTime = null)
    {
        var order = new Order
        {
            OrderStatus = "shipped",
            TrackingAddedAt = track,
            OrderShippedTime = shippedTime
        };

        if (transit is not null)
        {
            OrderFulfilTimeline.Stamp(
                order,
                OrderFulfilTimeline.Milestone.InTransit,
                transit,
                transitSource == "shein"
                    ? OrderFulfilTimeline.Source.Shein
                    : OrderFulfilTimeline.Source.Platform);
        }

        return order;
    }

    [Fact]
    public void Xoa_moc_transit_trung_khit_moc_track_va_mang_nguon_platform()
    {
        var at = new DateTime(2026, 7, 22, 17, 58, 40, DateTimeKind.Utc);
        var order = OrderWith(at, at, "platform");

        Assert.True(OrderTransitEvidence.ClearFabricatedTransit(order));
        Assert.Null(order.InTransitAt);
    }

    [Fact]
    public void Giu_moc_transit_nguon_shein_du_trung_khit_moc_track()
    {
        var at = new DateTime(2026, 7, 22, 17, 58, 40, DateTimeKind.Utc);
        var order = OrderWith(at, at, "shein");

        Assert.False(OrderTransitEvidence.ClearFabricatedTransit(order));
        Assert.Equal(at, order.InTransitAt);
    }

    [Fact]
    public void Giu_moc_transit_nguon_platform_khi_khac_moc_track()
    {
        var transit = new DateTime(2026, 7, 22, 6, 33, 0, DateTimeKind.Utc);
        var track = new DateTime(2026, 7, 22, 17, 58, 40, DateTimeKind.Utc);
        var order = OrderWith(transit, track, "platform");

        Assert.False(OrderTransitEvidence.ClearFabricatedTransit(order));
        Assert.Equal(transit, order.InTransitAt);
    }

    /// <summary>
    /// Mốc Track có thể đã bị xoá vì lý do khác, nhưng bản sao vẫn nhận ra được nhờ trùng khít
    /// <c>OrderShippedTime</c> — chính clock mà phiên bản cũ đã copy sang Transit.
    /// </summary>
    [Fact]
    public void Xoa_moc_transit_trung_khit_OrderShippedTime_khi_moc_track_trong()
    {
        var at = new DateTime(2026, 7, 22, 17, 58, 40, DateTimeKind.Utc);
        var order = OrderWith(at, track: null, transitSource: "platform", shippedTime: at);

        Assert.True(OrderTransitEvidence.ClearFabricatedTransit(order));
        Assert.Null(order.InTransitAt);
    }

    [Fact]
    public void Khong_lam_gi_khi_don_chua_co_moc_transit()
    {
        var order = OrderWith(transit: null, track: null, transitSource: null);

        Assert.False(OrderTransitEvidence.ClearFabricatedTransit(order));
    }
}
