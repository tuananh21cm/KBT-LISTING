using Application.DTOs.Order;
using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Orders;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khớp SHEIN tôn trọng đơn sàn đã hủy/hoàn: ưu tiên đơn còn hiệu lực, không đẩy tiền sang đơn
/// terminal, và phân biệt "đã chi trước lúc hủy" (giữ) với "mua sau khi hủy" (không gắn).
/// </summary>
public class SheinCanceledOrderMatchTests
{
    private const string Phone = "8312295220";
    private const string Zip = "93960";
    private const string Name = "Yellie Silva";
    private const string TikTokSku = "445524101-I8mn2r35hl08p0";

    [Fact]
    public void FindBestOrderMatch_PrefersLiveOrder_OverCanceledSibling()
    {
        var canceled = OrderAt(
            "577585372753728296",
            new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        canceled.CanceledAt = new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc);

        var live = OrderAt(
            "577585366130791208",
            new DateTime(2026, 9, 19, 1, 10, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.AwaitingShipment);

        var shein = Billno("GSU-KEEP", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));

        var (match, ambiguous) = OrderSyncService.FindBestOrderMatch(shein, [canceled, live]);
        Assert.False(ambiguous);
        Assert.Same(live, match);
    }

    [Fact]
    public void FindBestOrderMatch_AllowsCanceled_WhenOnlyCandidate_AndPurchasedBeforeCancel()
    {
        var canceled = OrderAt(
            "ONLY-CANCEL",
            new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        canceled.CanceledAt = new DateTime(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);

        // Mua trước khi hủy — tiền đã chi cho đúng đơn đó.
        var shein = Billno("GSU-BEFORE", new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc));

        var (match, ambiguous) = OrderSyncService.FindBestOrderMatch(shein, [canceled]);
        Assert.False(ambiguous);
        Assert.Same(canceled, match);
    }

    [Fact]
    public void FindBestOrderMatch_SkipsCanceled_WhenPurchasedAfterCancel()
    {
        var canceled = OrderAt(
            "ONLY-CANCEL",
            new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        canceled.CanceledAt = new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc);

        // Mua sau hủy > 12h slack.
        var shein = Billno("GSU-AFTER", new DateTime(2026, 9, 20, 4, 0, 0, DateTimeKind.Utc));

        var (match, ambiguous) = OrderSyncService.FindBestOrderMatch(shein, [canceled]);
        Assert.False(ambiguous);
        Assert.Null(match);
        Assert.True(OrderSyncService.IsPurchasedAfterPlatformCancel(shein, canceled));
        Assert.True(OrderSyncService.IsOnlyPlatformCanceledAfterPurchaseCandidate(
            [canceled], shein, Phone, Zip));
    }

    [Fact]
    public void IsPurchasedAfterPlatformCancel_False_WhenCanceledAtMissing()
    {
        var canceled = OrderAt(
            "NO-CLOCK",
            new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        var shein = Billno("GSU", new DateTime(2026, 9, 20, 4, 0, 0, DateTimeKind.Utc));

        Assert.False(OrderSyncService.IsPurchasedAfterPlatformCancel(shein, canceled));
    }

    [Fact]
    public void IsPurchasedAfterPlatformCancel_Respects12hSlack()
    {
        var canceled = OrderAt(
            "SLACK",
            new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        canceled.CanceledAt = new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc);

        // Trong 12h — không coi là mua sau hủy.
        var within = Billno("GSU-IN", new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc));
        Assert.False(OrderSyncService.IsPurchasedAfterPlatformCancel(within, canceled));

        var beyond = Billno("GSU-OUT", new DateTime(2026, 9, 20, 4, 1, 0, DateTimeKind.Utc));
        Assert.True(OrderSyncService.IsPurchasedAfterPlatformCancel(beyond, canceled));
    }

    [Fact]
    public void FindSiblingWithRoom_SkipsTerminalOrders()
    {
        var full = OrderAt("FULL", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        AttachLive(full, "GSU-A", new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc));

        var canceledEmpty = OrderAt(
            "CANCELED-EMPTY",
            new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        var liveEmpty = OrderAt(
            "LIVE-EMPTY",
            new DateTime(2026, 9, 19, 1, 45, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.AwaitingShipment);

        var shein = Billno("GSU-B", new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc));
        var (redirected, _) = OrderSyncService.FindSiblingWithRoom(
            [full, canceledEmpty, liveEmpty],
            shein,
            Phone,
            Zip,
            shein.Billno,
            exclude: full,
            claimedThisPass: []);

        Assert.Same(liveEmpty, redirected);
    }

    [Fact]
    public void FindRebalanceTarget_SkipsTerminalOrders()
    {
        var full = OrderAt("FULL", new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc));
        var surplus = AttachLive(full, "GSU-SURPLUS", new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc));
        AttachLive(full, "GSU-KEEP", new DateTime(2026, 9, 19, 4, 0, 5, DateTimeKind.Utc));

        var canceledEmpty = OrderAt(
            "CANCELED",
            new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        var liveEmpty = OrderAt(
            "LIVE",
            new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.AwaitingShipment);

        var (target, _) = OrderSyncService.FindRebalanceTarget(
            [full, canceledEmpty, liveEmpty],
            full,
            surplus,
            Phone,
            Zip,
            claimedThisPass: []);

        Assert.Same(liveEmpty, target);
    }

    [Fact]
    public void FindCanceledRelocateTarget_PicksLiveEmptySibling_WithSkuOverlap()
    {
        var canceled = OrderAt(
            "577585372753728296",
            new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled);
        var purchase = AttachLive(
            canceled,
            "GSU-WRONG",
            new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));

        var live = OrderAt(
            "577585366130791208",
            new DateTime(2026, 9, 19, 1, 10, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.AwaitingShipment);

        var target = OrderSyncService.FindCanceledRelocateTarget(
            [canceled, live],
            canceled,
            purchase,
            Phone,
            Zip,
            claimedThisPass: []);

        Assert.Same(live, target);
    }

    [Fact]
    public void FindCanceledRelocateTarget_AllowsSecondBillno_WhenLiveStillHasRoom()
    {
        var canceled = OrderAt(
            "CANCELED",
            new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc),
            PlatformOrderStatuses.Cancelled,
            "sku-1",
            "sku-2");
        var first = AttachLive(
            canceled, "GSU-1", new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc), ["sku-1"]);
        var second = AttachLive(
            canceled, "GSU-2", new DateTime(2026, 9, 19, 4, 0, 5, DateTimeKind.Utc), ["sku-2"]);

        var live = OrderAt(
            "LIVE-MULTI",
            new DateTime(2026, 9, 19, 1, 10, 0, DateTimeKind.Utc),
            PlatformOrderStatuses.AwaitingShipment,
            "sku-1",
            "sku-2");
        AttachLive(live, "GSU-1", first.SupplierPaidAt, ["sku-1"]);

        // Đã nhận 1/2 — vẫn còn chỗ cho billno thứ hai từ đơn hủy.
        var target = OrderSyncService.FindCanceledRelocateTarget(
            [canceled, live],
            canceled,
            second,
            Phone,
            Zip,
            claimedThisPass: []);

        Assert.Same(live, target);
        Assert.True(OrderSyncService.HasRoomForPurchase(live));
    }

    [Fact]
    public void ClearPurchasedWhenLinkMovedAway_ClearsWhenEmptyAndClockMatches()
    {
        var paidAt = new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc);
        var order = new Order
        {
            FulfilPurchasedAt = paidAt,
            FulfilTimelineSources = "{\"fulfilPurchasedAt\":\"shein\"}",
            SupplierPurchases = []
        };
        var moved = new SupplierPurchase
        {
            SupplierPaidAt = paidAt,
            OrderedAt = paidAt,
            SupplierUnpaid = false
        };

        Assert.True(OrderPurchaseEvidence.ClearPurchasedWhenLinkMovedAway(order, moved));
        Assert.Null(order.FulfilPurchasedAt);
    }

    private static Order OrderAt(
        string platformOrderId,
        DateTime placedAt,
        string? platformStatus = null,
        params string[] skus)
    {
        var items = (skus.Length == 0 ? new[] { TikTokSku } : skus)
            .Select(sku => new OrderItem { SellerSku = sku })
            .ToList();

        return new Order
        {
            Id = Guid.NewGuid(),
            PlatformOrderId = platformOrderId,
            BuyerPhone = Phone,
            PostalCode = Zip,
            BuyerName = Name,
            OrderCreateTime = placedAt,
            PlatformOrderStatus = platformStatus ?? PlatformOrderStatuses.AwaitingShipment,
            OrderStatus = "to_ship",
            Items = items,
            SupplierPurchases = []
        };
    }

    private static SheinOrderDto Billno(string billno, DateTime paidAt) => new()
    {
        Billno = billno,
        BuyerPhone = Phone,
        PostalCode = Zip,
        BuyerName = Name,
        SheinPaidAt = paidAt,
        OrderedAt = paidAt,
        ProductIds = [TikTokSku, "445524101"]
    };

    private static SupplierPurchase AttachLive(
        Order order,
        string billno,
        DateTime? paidAt,
        IReadOnlyList<string>? productKeys = null)
    {
        var keys = productKeys ?? [TikTokSku, "445524101"];
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = billno,
            Source = SupplierPurchaseSources.Shein,
            SupplierUnpaid = false,
            SupplierStatus = SupplierStatusGroups.Processing,
            SupplierPaidAt = paidAt,
            OrderedAt = paidAt,
            ProductIdsJson = System.Text.Json.JsonSerializer.Serialize(keys),
            CreatedAt = paidAt ?? DateTime.UtcNow
        };
        SupplierPurchaseIngest.Attach(purchase, order, DateTime.UtcNow);
        return purchase;
    }
}
