using Application.DTOs.Order;
using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Phân phối billno SHEIN khi một khách đặt nhiều đơn sàn riêng (cùng phone+zip, thường cùng SKU).
///
/// Ca gốc: 577577266831396921 ôm hai billno, 577577053923479609 trống — hai đơn 1 item cùng SKU,
/// hai lần mua SHEIN độc lập, khóa SKU hai bên lệch nhau nên guard cũ không tách được.
/// </summary>
public class SheinSiblingDistributionTests
{
    private const string Phone = "8312295220";
    private const string Zip = "93960";
    private const string Name = "Yellie Silva";
    private const string TikTokSku = "445524101-I8mn2r35hl08p0";

    [Fact]
    public void TwoSameCustomerOrders_EachGetsOneBillno_InTimeOrder()
    {
        var earlier = OrderAt("577577053923479609", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        var later = OrderAt("577577266831396921", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var pool = new List<Order> { later, earlier };

        var firstShein = Billno(
            "GSU1S803500232X",
            new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        var secondShein = Billno(
            "GSU1S821500MYUA",
            new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));

        var (firstMatch, firstAmbiguous) = OrderSyncService.FindBestOrderMatch(
            firstShein, pool, resolveAmbiguity: false);
        Assert.False(firstAmbiguous);
        Assert.Same(earlier, firstMatch);

        AttachLive(earlier, "GSU1S803500232X", firstShein.SheinPaidAt);

        var (secondMatch, secondAmbiguous) = OrderSyncService.FindBestOrderMatch(
            secondShein, pool, resolveAmbiguity: false);
        Assert.False(secondAmbiguous);
        Assert.Same(later, secondMatch);
    }

    [Fact]
    public void TwoSameCustomerOrders_WithoutSkuOverlap_StillSplitByRoom()
    {
        var earlier = OrderAt("A", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        var later = OrderAt("B", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var shein = Billno("GSU1", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        shein.ProductIds = ["458805821"];

        var (first, _) = OrderSyncService.FindBestOrderMatch(shein, [later, earlier]);
        Assert.Same(earlier, first);

        AttachLive(earlier, "GSU1", shein.SheinPaidAt);

        var secondShein = Billno("GSU2", new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));
        secondShein.ProductIds = ["458805821"];
        var (second, ambiguous) = OrderSyncService.FindBestOrderMatch(secondShein, [later, earlier]);
        Assert.False(ambiguous);
        Assert.Same(later, second);
    }

    [Fact]
    public void TwoItemOrder_UncoveredSku_AttachesToSameOrder_NotSibling()
    {
        var multi = OrderAt("MULTI", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc), "sku-1", "sku-2");
        AttachLive(multi, "GSU-A", new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc), productKeys: ["sku-1"]);

        var sibling = OrderAt("OTHER", new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc), "sku-other");

        var shein = Billno("GSU-B", new DateTime(2026, 9, 19, 2, 5, 0, DateTimeKind.Utc));
        shein.ProductIds = ["sku-2"];

        Assert.True(OrderSyncService.CanAttachAdditionalPurchase(multi, shein, out var reason));
        Assert.Equal("sku-uncovered", reason);
        Assert.True(OrderSyncService.HasRoomForPurchase(multi));

        var redirected = OrderSyncService.FindSiblingWithRoom(
            [multi, sibling], shein, Phone, Zip, shein.Billno, exclude: multi, claimedThisPass: []);
        // Anh em trống vẫn tồn tại, nhưng CanAttach đã cho phép — luồng khớp không chuyển hướng.
        Assert.Same(sibling, redirected.Order);

        var (best, ambiguous) = OrderSyncService.FindBestOrderMatch(shein, [multi, sibling]);
        Assert.False(ambiguous);
        Assert.Same(multi, best);
    }

    [Fact]
    public void SharedRelationBillno_MayExceedCapacity()
    {
        var order = OrderAt("ONE", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        var existing = AttachLive(order, "GSU-1", new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc));
        existing.RelationBillno = "USU-GROUP";

        var shein = Billno("GSU-2", new DateTime(2026, 9, 19, 2, 0, 4, DateTimeKind.Utc));
        shein.RelationBillno = "USU-GROUP";

        Assert.False(OrderSyncService.HasRoomForPurchase(order));
        Assert.True(OrderSyncService.CanAttachAdditionalPurchase(order, shein, out var reason));
        Assert.Equal("shared-relation-billno", reason);
    }

    [Fact]
    public void RefundedPurchase_DoesNotBlockRepurchase()
    {
        var order = OrderAt("ONE", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        var refunded = AttachLive(order, "GSU-OLD", new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc));
        refunded.SupplierStatus = SupplierStatusGroups.Refunded;

        Assert.Equal(0, OrderSyncService.LivePurchaseCount(order));
        Assert.True(OrderSyncService.HasRoomForPurchase(order));

        var shein = Billno("GSU-NEW", new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc));
        Assert.True(OrderSyncService.CanAttachAdditionalPurchase(order, shein, out var reason));
        Assert.Equal("sku-uncovered", reason);
    }

    [Fact]
    public void Rebalance_MovesNewestDiscreteBillno_ThenIdempotent()
    {
        var full = OrderAt("577577266831396921", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var empty = OrderAt("577577053923479609", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        AttachLive(full, "GSU1S803500232X", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        var surplus = AttachLive(
            full, "GSU1S821500MYUA", new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));

        Assert.Equal(2, OrderSyncService.LivePurchaseCount(full));
        Assert.Equal(1, OrderSyncService.PurchaseCapacity(full));
        Assert.Same(surplus, OrderSyncService.SelectSurplusPurchase(full));

        var target = OrderSyncService.FindRebalanceTarget(
            [full, empty], full, surplus, Phone, Zip, claimedThisPass: []);
        Assert.Same(empty, target.Order);

        SupplierPurchaseIngest.Detach(surplus, full, SheinSkipReasons.RebalancedToSibling, DateTime.UtcNow);
        SupplierPurchaseIngest.Attach(surplus, empty, DateTime.UtcNow);

        Assert.Equal(1, OrderSyncService.LivePurchaseCount(full));
        Assert.Equal(1, OrderSyncService.LivePurchaseCount(empty));
        Assert.True(OrderSyncService.LivePurchaseCount(full) <= OrderSyncService.PurchaseCapacity(full));
        Assert.Null(OrderSyncService.FindRebalanceTarget(
            [full, empty], full, surplus, Phone, Zip, claimedThisPass: []).Order);
    }

    [Fact]
    public void ManuallyLinkedPurchase_IsNeverTheSurplus()
    {
        var full = OrderAt("577577266831396921", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var older = AttachLive(full, "GSU1S803500232X", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        var newer = AttachLive(full, "GSU1S821500MYUA", new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));

        // Người xử lý đã tự chọn đơn cho billno mới nhất: cân bằng phải nhường và chuyển dòng máy
        // tự khớp thay vì lật lại quyết định đã có người tra.
        newer.ManualLinkedAt = DateTime.UtcNow;
        Assert.Same(older, OrderSyncService.SelectSurplusPurchase(full));

        older.ManualLinkedAt = DateTime.UtcNow;
        Assert.Null(OrderSyncService.SelectSurplusPurchase(full));
    }

    [Fact]
    public void SortSheinOrders_PaidEarlyFirst()
    {
        var late = Billno("GSU-B", new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));
        var early = Billno("GSU-A", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        var list = new List<SheinOrderDto> { late, early };

        OrderSyncService.SortSheinOrdersForMatch(list);

        Assert.Equal("GSU-A", list[0].Billno);
        Assert.Equal("GSU-B", list[1].Billno);
    }

    [Fact]
    public void CapacityFull_WithoutJointEvidence_IsRejected()
    {
        var order = OrderAt("ONE", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        AttachLive(order, "GSU-1", new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc));

        var shein = Billno("GSU-2", new DateTime(2026, 9, 19, 2, 0, 4, DateTimeKind.Utc));
        shein.ProductIds = ["458805821"];

        Assert.False(OrderSyncService.CanAttachAdditionalPurchase(order, shein, out var reason));
        Assert.Equal(OrderSyncService.OrderCapacityFullReason, reason);
    }

    [Fact]
    public void PurchaseMark_E1_PrefersMarkedOrder_OverEarlierPlaced()
    {
        var earlier = OrderAt("A", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        var later = OrderAt("B", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var payAt = new DateTime(2026, 9, 19, 4, 41, 0, DateTimeKind.Utc);
        var shein = Billno("GSU-MARK", payAt);

        // ComparePurchaseDemand sẽ chọn earlier (đặt sớm hơn); dấu Đã mua trên later thắng.
        var markT = payAt.AddMinutes(-2);
        var index = IndexWithPurchased(later.Id, markT.AddMinutes(-5), markT);

        var (redirected, byMark) = OrderSyncService.FindSiblingWithRoom(
            [earlier, later], shein, Phone, Zip, shein.Billno, exclude: null, claimedThisPass: [], index);
        Assert.True(byMark);
        Assert.Same(later, redirected);
    }

    [Fact]
    public void PurchaseMark_E1_TwoBillnos_TwoOrders_PairByTime()
    {
        var orderA = OrderAt("A", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        var orderB = OrderAt("B", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc));
        var t1 = new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 9, 19, 5, 0, 0, DateTimeKind.Utc);

        var attemptA = Attempt(orderA.Id, t1.AddMinutes(-10), t1);
        var attemptB = Attempt(orderB.Id, t2.AddMinutes(-10), t2);
        var index = new OrderSyncService.PurchaseMarkIndex([attemptA, attemptB]);

        var shein1 = Billno("GSU-1", t1.AddMinutes(1));
        var shein2 = Billno("GSU-2", t2.AddMinutes(1));

        var (first, by1) = OrderSyncService.FindSiblingWithRoom(
            [orderA, orderB], shein1, Phone, Zip, shein1.Billno, null, [], index);
        Assert.True(by1);
        Assert.Same(orderA, first);

        var (second, by2) = OrderSyncService.FindSiblingWithRoom(
            [orderA, orderB], shein2, Phone, Zip, shein2.Billno, null, [], index);
        Assert.True(by2);
        Assert.Same(orderB, second);
    }

    [Fact]
    public void PurchaseMark_JointShipment_ReusesAttemptForSiblingBillno()
    {
        var order = OrderAt("ONE", new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc), "sku-1", "sku-2");
        var empty = OrderAt("OTHER", new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc), "sku-other");
        var payAt = new DateTime(2026, 9, 19, 4, 0, 0, DateTimeKind.Utc);
        var attempt = Attempt(order.Id, payAt.AddMinutes(-5), payAt);
        var index = new OrderSyncService.PurchaseMarkIndex([attempt]);

        var shein1 = Billno("GSU-1", payAt);
        shein1.RelationBillno = "USU-GROUP";
        shein1.ProductIds = ["sku-1"];

        var (first, by1) = OrderSyncService.FindSiblingWithRoom(
            [order, empty], shein1, Phone, Zip, shein1.Billno, null, [], index);
        Assert.True(by1);
        Assert.Same(order, first);
        Assert.True(index.IsConsumed(attempt.Id));

        AttachLive(order, "GSU-1", payAt).RelationBillno = "USU-GROUP";

        var shein2 = Billno("GSU-2", payAt.AddSeconds(4));
        shein2.RelationBillno = "USU-GROUP";
        shein2.ProductIds = ["sku-2"];

        // Capacity còn 1 chỗ (2 items) — FindSibling vẫn thấy order; attempt đã consume nhưng
        // cùng relation → tái dùng, không tiêu thụ lần hai / không chuyển sang empty.
        var (second, by2) = OrderSyncService.FindSiblingWithRoom(
            [order, empty], shein2, Phone, Zip, shein2.Billno, null, [], index);
        Assert.True(by2);
        Assert.Same(order, second);
    }

    [Fact]
    public void PurchaseMark_E3_NoMarks_KeepsDemandOrder()
    {
        var earlier = OrderAt("A", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        var later = OrderAt("B", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var shein = Billno("GSU1", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));

        var (redirected, byMark) = OrderSyncService.FindSiblingWithRoom(
            [later, earlier], shein, Phone, Zip, shein.Billno, null, [],
            OrderSyncService.PurchaseMarkIndex.Empty);
        Assert.False(byMark);
        Assert.Same(earlier, redirected);
    }

    [Fact]
    public void PurchaseMark_Rebalance_ExemptsE1MatchedBillno()
    {
        var full = OrderAt("FULL", new DateTime(2026, 9, 19, 3, 7, 38, DateTimeKind.Utc));
        var empty = OrderAt("EMPTY", new DateTime(2026, 9, 19, 0, 55, 49, DateTimeKind.Utc));
        var older = AttachLive(full, "GSU-OLD", new DateTime(2026, 9, 19, 4, 39, 6, DateTimeKind.Utc));
        var newer = AttachLive(full, "GSU-NEW", new DateTime(2026, 9, 19, 4, 39, 10, DateTimeKind.Utc));

        // E1 khớp newer với chính đơn full — CompletedAt = pay của newer (±slack); older lệch 4s
        // vẫn ngoài cửa sổ hẹp rebalance nên vẫn bị chọn làm surplus.
        var index = IndexWithPurchased(
            full.Id,
            newer.SupplierPaidAt!.Value.AddMinutes(-5),
            newer.SupplierPaidAt.Value);
        Assert.Same(older, OrderSyncService.SelectSurplusPurchase(full, index));

        // Không có mark → vẫn chọn newer (mới nhất).
        Assert.Same(newer, OrderSyncService.SelectSurplusPurchase(full));

        _ = empty;
    }

    [Fact]
    public void PurchaseMark_Causality_StillBlocksEvenWithMark()
    {
        var order = OrderAt("LATE", new DateTime(2026, 9, 29, 16, 0, 0, DateTimeKind.Utc));
        var payAt = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
        var shein = Billno("GSU-OLD", payAt);
        var index = IndexWithPurchased(order.Id, payAt.AddMinutes(-5), payAt);

        var (redirected, _) = OrderSyncService.FindSiblingWithRoom(
            [order], shein, Phone, Zip, shein.Billno, null, [], index);
        Assert.Null(redirected);
    }

    private static OrderSyncService.PurchaseMarkIndex IndexWithPurchased(
        Guid orderId,
        DateTime startedAt,
        DateTime completedAt) =>
        new([Attempt(orderId, startedAt, completedAt)]);

    private static OrderSyncService.PurchaseMarkAttempt Attempt(
        Guid orderId,
        DateTime startedAt,
        DateTime completedAt) =>
        new(
            Id: Guid.NewGuid(),
            OrderId: orderId,
            UserId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            StartedAt: startedAt,
            CompletedAt: completedAt,
            CustomerKey: $"{Phone}|{Zip}",
            Outcome: FulfilAttemptOutcomes.Purchased,
            IsOpen: false);

    private static Order OrderAt(string platformOrderId, DateTime placedAt, params string[] skus)
    {
        var items = (skus.Length == 0 ? [TikTokSku] : skus)
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
