using Application.DTOs.Order;
using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Orders;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Phase <c>list</c> phải ghi <c>TrackingKind=carrier</c> khi <c>order/list</c> đã có
/// <c>shipping_no</c>, để link đủ điều kiện <see cref="OrderAutoMarkShipPolicy.ShouldAttempt"/>
/// / hàng đợi detail sweep. Auto Mark Ship lên 4Seller <b>không</b> chạy trong POST extension —
/// job <c>shein.order_detail_sync</c> / Sync Track opt-in qua
/// <see cref="OrderAutoMarkShipPolicy.ShouldRunAutoMarkShip"/>.
/// </summary>
public class SheinListPhaseAutoMarkShipTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(0, false, true)]
    [InlineData(10, false, true)]
    public void ShouldRunAutoMarkShip_OnlyWhenCallerOptsIn(
        int? maxAttempts,
        bool interactive,
        bool expected)
    {
        Assert.Equal(
            expected,
            OrderAutoMarkShipPolicy.ShouldRunAutoMarkShip(maxAttempts, interactive));
    }

    [Theory]
    [InlineData("GSU1RJ03500MYMS", "SPXPDX005672608280028846", "SpeedX")]
    [InlineData("GSU1RJ03500MXLM", "GFUS01069875130948", "GOFO")]
    public void ListPhaseCarrierTracking_IsMarkShipEligible(
        string billno,
        string trackingNo,
        string carrier)
    {
        var link = IngestList(ShippedPayload(billno, trackingNo, carrier));

        Assert.Equal(SupplierTrackingKinds.Carrier, link.TrackingKind);
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(AwaitingShipmentOrder(), link, Now));
    }

    [Fact]
    public void ListPhaseWarehouseTracking_StaysLocalOnly()
    {
        // Đơn joint GSU1RJ035000MLD chỉ có mã kho C… — cập nhật tracking NCC nhưng không lên sàn.
        var link = IngestList(
            ShippedPayload("GSU1RJ035000MLD", "C26082801638181", carrier: string.Empty));

        Assert.Equal(SupplierTrackingKinds.Warehouse, link.TrackingKind);
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(AwaitingShipmentOrder(), link, Now));
    }

    [Fact]
    public void ListPhasePackingOrder_HasNothingToPublish()
    {
        // GSU1RP03F00129U đang PACKING: goods_pkg_rel_list[].shipping_no còn rỗng.
        var link = IngestList(
            ShippedPayload("GSU1RP03F00129U", trackingNo: string.Empty, carrier: string.Empty));

        Assert.Equal(SupplierTrackingKinds.None, link.TrackingKind);
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(AwaitingShipmentOrder(), link, Now));
    }

    [Theory]
    [InlineData(AutoMarkShipStatuses.Unconfirmed, null)]
    [InlineData(AutoMarkShipStatuses.Blocked, "req-4seller-1")]
    public void UnresolvedMarkShip_StillCountsAsDelta_SoReconcileCanRun(
        string autoStatus,
        string? requestId)
    {
        // Mã vận đơn đã cấp xong nên payload SHEIN không còn đổi. Chế độ "Chỉ đơn mới" mà không
        // thấy delta sẽ `continue` trước cả bước xếp hàng reconcile, khoá link lại vĩnh viễn.
        var shein = ShippedPayload("GSU1RJ41Q00NJW2", "SPXJFK013642608280018498", "SpeedX");
        var stored = IngestList(shein);
        stored.AutoMarkShipStatus = autoStatus;
        stored.AutoMarkShipRequestId = requestId;
        stored.AutoMarkShipAttemptedAt = Now.AddHours(-3);

        var before = OrderSyncService.SheinPurchaseSnapshot.Of(stored);
        var order = AwaitingShipmentOrder();
        order.FulfilPurchasedAt = shein.OrderedAt;

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, stored, Now));
        Assert.True(OrderSyncService.HasSheinIncrementalDelta(stored, before, shein, order));
    }

    /// <summary>
    /// Hàng đợi `GET shein-sync/config` trả cho extension phải **rộng hơn hoặc bằng**
    /// <c>ShouldAttempt</c>. Hẹp hơn ở đúng một trạng thái là link đó mất hết lượt POST về sau, vì
    /// Auto Sync chỉ POST khi fingerprint `order/list` đổi — mà mã vận đơn cấp xong là lúc nó đứng
    /// yên. Đây chính là cách đơn `577543204150481621` nằm `pending` suốt 5 ngày.
    /// </summary>
    [Theory]
    [InlineData(null, null, 0)]
    [InlineData(AutoMarkShipStatuses.Pending, null, 0)]
    [InlineData(AutoMarkShipStatuses.Failed, null, 30)]
    [InlineData(AutoMarkShipStatuses.Unconfirmed, null, 30)]
    [InlineData(AutoMarkShipStatuses.Blocked, "req-4seller-1", 30)]
    public void PendingQueue_CoversEveryLinkThatStillNeedsAnotherSyncRound(
        string? autoStatus,
        string? requestId,
        int minutesSinceAttempt)
    {
        var order = AwaitingShipmentOrder();
        var link = IngestList(ShippedPayload("GSU1RJ41Q00NJW2", "SPXJFK013642608280018498", "SpeedX"));
        link.Order = order;
        link.AutoMarkShipStatus = autoStatus;
        link.AutoMarkShipRequestId = requestId;
        link.AutoMarkShipAttemptedAt = minutesSinceAttempt == 0
            ? null
            : Now.AddMinutes(-minutesSinceAttempt);

        Assert.True(OrderAutoMarkShipPolicy.PendingQueuePredicate(Now).Compile()(link));
    }

    [Theory]
    // Đã publish lên sàn — không còn gì để đẩy.
    [InlineData(AutoMarkShipStatuses.Succeeded, null, 0, null)]
    // `blocked` sạch (không requestId treo) chờ người xử lý, POST lại chỉ tốn request.
    [InlineData(AutoMarkShipStatuses.Blocked, null, 30, null)]
    // `failed` còn trong cooldown 15 phút.
    [InlineData(AutoMarkShipStatuses.Failed, null, 5, null)]
    // Sàn đã có mã (Mark Ship tay hoặc seed từ sync 4Seller).
    [InlineData(AutoMarkShipStatuses.Pending, null, 0, "9400100000000000000000")]
    public void PendingQueue_SkipsLinksWithNothingLeftToDo(
        string autoStatus,
        string? requestId,
        int minutesSinceAttempt,
        string? orderTrackingNo)
    {
        var order = AwaitingShipmentOrder();
        order.TrackingNo = orderTrackingNo;
        var link = IngestList(ShippedPayload("GSU1RJ41Q00NJW2", "SPXJFK013642608280018498", "SpeedX"));
        link.Order = order;
        link.AutoMarkShipStatus = autoStatus;
        link.AutoMarkShipRequestId = requestId;
        link.AutoMarkShipAttemptedAt = minutesSinceAttempt == 0
            ? null
            : Now.AddMinutes(-minutesSinceAttempt);

        Assert.False(OrderAutoMarkShipPolicy.PendingQueuePredicate(Now).Compile()(link));
    }

    private static SupplierPurchase IngestList(SheinOrderDto shein) =>
        SupplierPurchaseIngest.Upsert(
            new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase),
            shein,
            shein.Billno,
            "list",
            "run-list",
            Now,
            out _);

    private static SheinOrderDto ShippedPayload(string billno, string trackingNo, string carrier) =>
        new()
        {
            Billno = billno,
            BuyerName = "Jucely Garcia",
            BuyerPhone = "5038842242",
            PostalCode = "97071-9027",
            CountryCode = "US",
            Currency = "USD",
            TrackingNo = trackingNo,
            Carrier = carrier,
            Total = 12.83m,
            SheinStatus = "Shipped",
            SheinStatusGroup = SupplierStatusGroups.Shipped,
            OrderedAt = Now.AddDays(-5),
            SheinPaidAt = Now.AddDays(-5),
            SheinShippedAt = Now.AddDays(-3)
        };

    private static Order AwaitingShipmentOrder() => new()
    {
        Id = Guid.NewGuid(),
        OrderStatus = "unknown",
        PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment
    };
}
