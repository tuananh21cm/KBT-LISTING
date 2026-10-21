using Application.DTOs.Shein;
using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Infrastructure.Integrations.Telegram;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Telegram "Add track" của vòng quét chi tiết SHEIN chạy trên backend.
///
/// Bất biến: chỉ xếp watchlist khi lượt nền <b>lần đầu</b> đổi mã kho thành mã carrier thật
/// (đường extension im lặng). Mark Ship còn <c>pending</c> → stamp
/// <see cref="Order.TelegramAddTrackNotifyPendingAt"/> và gửi ở lượt <c>detail-</c> sau khi
/// Mark Ship đã reportable (không mất tin vì transition-only).
/// </summary>
public class SheinDetailAddTrackNotifyTests
{
    private static readonly DateTime OrderedAt = new(2026, 8, 30, 2, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime PurchasedAt = new(2026, 8, 31, 2, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime DetectedAt = new(2026, 9, 2, 4, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime PendingAt = new(2026, 9, 2, 4, 0, 0, DateTimeKind.Utc);

    [Theory]
    // Mã kho C… và mã rỗng đều là "chưa có tracking thật" → lần đầu có carrier thì báo.
    [InlineData("detail-abc", "C26082801638181", true)]
    [InlineData("detail-abc", null, true)]
    // Lượt trước đã carrier: lượt này chỉ refresh, không phải tin mới.
    [InlineData("detail-abc", "9400100000000000000000", false)]
    // Đường extension đã có UI theo dõi; bắn cả hai đường thì mỗi mã ra hai tin.
    [InlineData("ext-abc", "C26082801638181", false)]
    [InlineData(null, "C26082801638181", false)]
    public void Watchlist_OnlyBackendSweepFirstCarrierHit(
        string? runId,
        string? beforeTrackingNo,
        bool expected)
    {
        var before = OrderSyncService.SheinPurchaseSnapshot.Of(new SupplierPurchase
        {
            TrackingNo = beforeTrackingNo
        });

        var link = CarrierLink();

        Assert.Equal(expected, OrderSyncService.ShouldNotifyDetailAddTrack(runId, before, link));
    }

    [Fact]
    public void Watchlist_SkipsWarehouseOnlyResult()
    {
        var before = OrderSyncService.SheinPurchaseSnapshot.Of(null);
        var link = CarrierLink();
        link.TrackingKind = SupplierTrackingKinds.Warehouse;
        link.TrackingNo = "C26082801638181";

        Assert.False(OrderSyncService.ShouldNotifyDetailAddTrack("detail-abc", before, link));
    }

    [Fact]
    public void PendingRetry_EnqueuesWhenPendingStampAndCarrierLink()
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
        link.SupplierOrderNo = "GSU1RJ03500MYMS";

        var order = AwaitingShipmentOrder();
        order.TelegramAddTrackNotifyPendingAt = PendingAt;
        order.SupplierPurchases = [link];

        var watchlist = new Dictionary<Guid, SheinDetailAddTrackNotifyItem>();
        OrderSyncService.EnqueuePendingDetailAddTrackRetries([order], watchlist, DetectedAt);

        Assert.True(watchlist.ContainsKey(order.Id));
        Assert.Equal(PendingAt, watchlist[order.Id].DetectedAtUtc);
        Assert.Equal("GSU1RJ03500MYMS", watchlist[order.Id].Billno);
    }

    [Fact]
    public void PendingRetry_SkipsWhenAlreadyNotified()
    {
        var link = CarrierLink();
        var order = AwaitingShipmentOrder();
        order.TelegramAddTrackNotifyPendingAt = PendingAt;
        order.TelegramAddTrackNotifiedAt = DetectedAt;
        order.SupplierPurchases = [link];

        var watchlist = new Dictionary<Guid, SheinDetailAddTrackNotifyItem>();
        OrderSyncService.EnqueuePendingDetailAddTrackRetries([order], watchlist, DetectedAt);

        Assert.Empty(watchlist);
    }

    [Fact]
    public void PendingRetry_SkipsWhenNoPendingStamp()
    {
        var link = CarrierLink();
        var order = AwaitingShipmentOrder();
        order.SupplierPurchases = [link];

        var watchlist = new Dictionary<Guid, SheinDetailAddTrackNotifyItem>();
        OrderSyncService.EnqueuePendingDetailAddTrackRetries([order], watchlist, DetectedAt);

        Assert.Empty(watchlist);
    }

    [Fact]
    public void PendingRetry_DoesNotOverwriteExistingWatchlistEntry()
    {
        var link = CarrierLink();
        var order = AwaitingShipmentOrder();
        order.TelegramAddTrackNotifyPendingAt = PendingAt;
        order.SupplierPurchases = [link];

        var existing = new SheinDetailAddTrackNotifyItem(order, link, "EXISTING", DetectedAt);
        var watchlist = new Dictionary<Guid, SheinDetailAddTrackNotifyItem>
        {
            [order.Id] = existing
        };

        OrderSyncService.EnqueuePendingDetailAddTrackRetries([order], watchlist, DetectedAt);

        Assert.Same(existing, watchlist[order.Id]);
        Assert.Equal("EXISTING", watchlist[order.Id].Billno);
    }

    [Theory]
    [InlineData(AutoMarkShipStatuses.Succeeded, true)]
    [InlineData(AutoMarkShipStatuses.Failed, false)]
    [InlineData(AutoMarkShipStatuses.Blocked, false)]
    [InlineData(AutoMarkShipStatuses.Unconfirmed, false)]
    public void MarkShipOutcome_IsReportable(string status, bool expected)
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = status;

        Assert.Equal(expected, TelegramNotifier.ResolveMarkShipOutcome(link));
    }

    [Theory]
    [InlineData(AutoMarkShipStatuses.Pending)]
    [InlineData(null)]
    public void MarkShipOutcome_DeferredHasNothingToReport(string? status)
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = status;

        Assert.Null(TelegramNotifier.ResolveMarkShipOutcome(link));
    }

    [Fact]
    public void Message_SuccessCarriesTrackingAndLatency()
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;

        var text = TelegramNotifier.BuildSheinDetailAddTrackMessage(
            new SheinDetailAddTrackNotifyItem(AwaitingShipmentOrder(), link, "GSU1RJ03500MYMS", DetectedAt),
            profileName: "KB-07",
            managerFullName: "Nguyễn An");

        Assert.StartsWith("📦 <b>Add track</b> · ✅ Thành công", text);
        Assert.Contains("Nguyễn An • KB-07", text);
        Assert.DoesNotContain("Summer Floral Dress", text);
        Assert.Contains("• Đơn: <code>577543204150481621</code>", text);
        Assert.Contains("• Billno: <code>GSU1RJ03500MYMS</code>", text);
        Assert.Contains("SpeedX SPXPDX005672608280028846", text);
        Assert.DoesNotContain("Mark Ship:", text);
        Assert.Contains("Đặt hàng: 09:15 30/08/2026", text);
        Assert.Contains("Mua hàng: 09:15 31/08/2026", text);
        Assert.Contains("Mua sau đặt: <b>24 giờ 0 phút</b>", text);
        Assert.Contains("Tracking sau mua: <b>50 giờ 15 phút</b>", text);
        Assert.Contains("Tracking sau đặt: <b>74 giờ 15 phút</b>", text);
        Assert.Contains("Time: 11:30:00 02/09/2026", text);
    }

    [Fact]
    public void Message_MissingPurchaseStampShowsDash()
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;

        var order = AwaitingShipmentOrder();
        order.FulfilPurchasedAt = null;

        var text = TelegramNotifier.BuildSheinDetailAddTrackMessage(
            new SheinDetailAddTrackNotifyItem(order, link, "GSU1RJ03500MYMS", DetectedAt),
            profileName: "KB-07",
            managerFullName: "Nguyễn An");

        Assert.Contains("Mua hàng: —", text);
        Assert.Contains("Mua sau đặt: <b>—</b>", text);
        Assert.Contains("Tracking sau mua: <b>—</b>", text);
        Assert.Contains("Tracking sau đặt: <b>74 giờ 15 phút</b>", text);
    }

    [Theory]
    [InlineData(AutoMarkShipStatuses.Failed)]
    [InlineData(AutoMarkShipStatuses.Blocked)]
    [InlineData(AutoMarkShipStatuses.Unconfirmed)]
    public void Message_FailureCarriesReason(string status)
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = status;
        link.AutoMarkShipError = "Shipping Service not matched.";

        var text = TelegramNotifier.BuildSheinDetailAddTrackMessage(
            new SheinDetailAddTrackNotifyItem(AwaitingShipmentOrder(), link, "GSU1RJ03500MYMS", DetectedAt),
            profileName: null,
            managerFullName: null);

        Assert.StartsWith("📦 <b>Add track</b> · ❌ Thất bại", text);
        Assert.Contains("\nShipping Service not matched.\n", text);
        Assert.DoesNotContain("Mark Ship:", text);
        Assert.Contains("Seller • —", text);
    }

    private static SupplierPurchase CarrierLink() => new()
    {
        Id = Guid.NewGuid(),
        SupplierOrderNo = "GSU1RJ03500MYMS",
        TrackingKind = SupplierTrackingKinds.Carrier,
        TrackingNo = "SPXPDX005672608280028846",
        Carrier = "SpeedX"
    };

    private static Order AwaitingShipmentOrder() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        PlatformOrderId = "577543204150481621",
        PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
        OrderCreateTime = OrderedAt,
        FulfilPurchasedAt = PurchasedAt,
        Items = [new OrderItem { Title = "Summer Floral Dress" }]
    };
}
