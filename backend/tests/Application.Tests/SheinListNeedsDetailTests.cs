using Application.DTOs.Order;
using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>List-first smart sync: when phase list should request get_order_info.</summary>
public class SheinListNeedsDetailTests
{
    [Fact]
    public void NewLink_AlwaysNeedsDetail()
    {
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        var shein = BaseShein();

        Assert.Equal(
            SheinNeedsDetailReasons.NewLink,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: true, prevLinkTotal: link.SupplierTotal));
    }

    [Fact]
    public void StatusOnlyDelta_WithCompleteCostAndCarrier_SkipsDetail()
    {
        var order = LinkedOrder();
        order.InTransitAt = DateTime.UtcNow.AddDays(-1);
        var link = CostCompleteLink(order);
        link.TrackingNo = "1Z999AA10123456784";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        link.SupplierStatus = SupplierStatusGroups.Shipped;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Shipped;
        shein.SheinStatus = "Shipped";
        shein.TrackingNo = link.TrackingNo;
        shein.Carrier = "UPS";
        shein.Total = link.SupplierTotal;
        shein.SheinShippedAt = order.InTransitAt;

        Assert.Null(OrderSyncService.EvaluateSheinListNeedsDetail(
            order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));
    }

    [Fact]
    public void CostIncomplete_NeedsDetail()
    {
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        link.SupplierSubtotal = null;
        var shein = BaseShein();
        shein.Total = 12.5m;

        Assert.Equal(
            SheinNeedsDetailReasons.CostIncomplete,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: false, prevLinkTotal: 12.5m));
    }

    [Fact]
    public void TotalChanged_NeedsDetail()
    {
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        var shein = BaseShein();
        shein.Total = 20m;

        Assert.Equal(
            SheinNeedsDetailReasons.TotalDelta,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: false, prevLinkTotal: 12.5m));
    }

    [Fact]
    public void WarehouseTrackingWhenShipped_NeedsDetail()
    {
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        link.TrackingNo = "C2607123456789012";
        link.TrackingKind = SupplierTrackingKinds.Warehouse;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Shipped;
        shein.TrackingNo = link.TrackingNo;
        shein.Total = link.SupplierTotal;
        shein.SheinShippedAt = DateTime.UtcNow.AddHours(-2);
        order.InTransitAt = shein.SheinShippedAt;

        Assert.Equal(
            SheinNeedsDetailReasons.NoCarrierTracking,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));
    }

    [Fact]
    public void DeliveredMissingClockOnList_NeedsDetail()
    {
        var order = LinkedOrder();
        order.DeliveredAt = null;
        order.InTransitAt = DateTime.UtcNow.AddDays(-3);
        var link = CostCompleteLink(order);
        link.TrackingNo = "1Z999AA10123456784";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Delivered;
        shein.TrackingNo = link.TrackingNo;
        shein.Carrier = "UPS";
        shein.Total = link.SupplierTotal;
        shein.SheinDeliveredAt = null;
        shein.SheinShippedAt = order.InTransitAt;

        Assert.Equal(
            SheinNeedsDetailReasons.MissingMilestoneClock,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));
    }

    [Fact]
    public void DeliveredWithClockAlreadyOnOrder_SkipsDetail()
    {
        var deliveredAt = DateTime.UtcNow.AddDays(-1);
        var order = LinkedOrder();
        order.DeliveredAt = deliveredAt;
        order.InTransitAt = deliveredAt.AddDays(-2);
        var link = CostCompleteLink(order);
        link.TrackingNo = "1Z999AA10123456784";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Delivered;
        shein.TrackingNo = link.TrackingNo;
        shein.Carrier = "UPS";
        shein.Total = link.SupplierTotal;
        shein.SheinDeliveredAt = deliveredAt;
        shein.SheinShippedAt = order.InTransitAt;

        Assert.Null(OrderSyncService.EvaluateSheinListNeedsDetail(
            order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));
    }

    /// <summary>
    /// Payload list mang đủ mã carrier, giá và clock — đúng hình dạng của một đơn đã giao đang
    /// nằm trong lookback. Chốt bằng test vì đây là ca chiếm đa số mỗi lượt sync: nếu nó vẫn đòi
    /// detail thì mọi đơn đã khớp đều bị gọi lại get_order_info.
    /// </summary>
    [Fact]
    public void DeliveredWithFullListPayload_SkipsDetail()
    {
        var deliveredAt = DateTime.UtcNow.AddDays(-2);
        var order = LinkedOrder();
        order.DeliveredAt = deliveredAt;
        order.InTransitAt = deliveredAt.AddDays(-5);
        order.TrackingAddedAt = deliveredAt.AddDays(-6);
        var link = CostCompleteLink(order);
        link.TrackingNo = "GFUS01063094456704";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        link.SupplierStatus = SupplierStatusGroups.Delivered;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Delivered;
        shein.TrackingNo = link.TrackingNo;
        shein.Carrier = string.Empty;
        shein.Total = link.SupplierTotal;
        shein.SheinDeliveredAt = deliveredAt;
        shein.SheinShippedAt = order.InTransitAt;

        Assert.Null(OrderSyncService.EvaluateSheinListNeedsDetail(
            order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));
    }

    /// <summary>
    /// Chế độ "Cập nhật tất cả" dựng lại breakdown giá từ dữ liệu SHEIN mới, mà chỉ phase detail
    /// mới full-replace được cost, nên payload list đủ đến đâu cũng phải gọi get_order_info.
    /// </summary>
    [Fact]
    public void ForceDetail_OverridesCompleteListPayload()
    {
        var deliveredAt = DateTime.UtcNow.AddDays(-2);
        var order = LinkedOrder();
        order.DeliveredAt = deliveredAt;
        order.InTransitAt = deliveredAt.AddDays(-5);
        order.TrackingAddedAt = deliveredAt.AddDays(-6);
        var link = CostCompleteLink(order);
        link.TrackingNo = "GFUS01063094456704";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        link.SupplierStatus = SupplierStatusGroups.Delivered;
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Delivered;
        shein.TrackingNo = link.TrackingNo;
        shein.Total = link.SupplierTotal;
        shein.SheinDeliveredAt = deliveredAt;
        shein.SheinShippedAt = order.InTransitAt;

        Assert.Null(OrderSyncService.EvaluateSheinListNeedsDetail(
            order, link, shein, billnoLinked: false, prevLinkTotal: link.SupplierTotal));

        Assert.Equal(
            SheinNeedsDetailReasons.ModeAll,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order,
                link,
                shein,
                billnoLinked: false,
                prevLinkTotal: link.SupplierTotal,
                forceDetail: true));
    }

    [Fact]
    public void FirstSeen_UnmatchedRealGoods_QueuesDetail()
    {
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = "GSO1NEW",
            MatchState = SupplierPurchaseMatchStates.Unmatched,
            IsMembershipOrVirtual = false
        };
        var shein = BaseShein();
        shein.Billno = "GSO1NEW";
        shein.SheinStatusGroup = SupplierStatusGroups.Processing;
        shein.SheinUnpaid = false;

        Assert.True(OrderSyncService.ShouldQueueFirstSeenDetail(
            purchaseExistedBeforeUpsert: false, purchase, shein));
    }

    [Fact]
    public void FirstSeen_AwaitingDetailReason_IsStableContract()
    {
        // Extension + nhật ký dựa vào mã ổn định — đổi chuỗi là phá thông báo / skipCounts.
        Assert.Equal("awaiting_detail", SheinSkipReasons.AwaitingDetail);
        Assert.Equal("first_seen", SheinNeedsDetailReasons.FirstSeen);
    }

    [Fact]
    public void FirstSeen_AlreadyExisted_DoesNotQueue()
    {
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = "GSO1OLD",
            MatchState = SupplierPurchaseMatchStates.Matched
        };
        var shein = BaseShein();

        Assert.False(OrderSyncService.ShouldQueueFirstSeenDetail(
            purchaseExistedBeforeUpsert: true, purchase, shein));
    }

    [Fact]
    public void FirstSeen_UnpaidGhost_DoesNotQueue()
    {
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = "GSO1GHOST",
            MatchState = SupplierPurchaseMatchStates.Unmatched,
            SupplierPaidAt = null
        };
        var shein = BaseShein();
        shein.SheinUnpaid = true;
        shein.SheinPaidAt = null;

        Assert.False(OrderSyncService.ShouldQueueFirstSeenDetail(
            purchaseExistedBeforeUpsert: false, purchase, shein));
    }

    [Fact]
    public void FirstSeen_TerminalRefunded_DoesNotQueue()
    {
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = "GSO1REF",
            MatchState = SupplierPurchaseMatchStates.Unmatched
        };
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Refunded;

        Assert.False(OrderSyncService.ShouldQueueFirstSeenDetail(
            purchaseExistedBeforeUpsert: false, purchase, shein));
    }

    [Fact]
    public void FirstSeen_KnownMembership_DoesNotQueue()
    {
        var purchase = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = "GSO1CLUB",
            MatchState = SupplierPurchaseMatchStates.Unmatched,
            IsMembershipOrVirtual = true
        };
        var shein = BaseShein();
        shein.SheinStatusGroup = SupplierStatusGroups.Processing;

        Assert.False(OrderSyncService.ShouldQueueFirstSeenDetail(
            purchaseExistedBeforeUpsert: false, purchase, shein));
    }

    [Fact]
    public void NewLink_StillNeedsDetail_AlongsideFirstSeenPath()
    {
        // Đơn mới đã khớp: EvaluateSheinListNeedsDetail vẫn trả NewLink (first_seen đã xếp
        // hàng trước vòng khớp; dedupe NeedsDetailBillnos cuối method).
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        var shein = BaseShein();

        Assert.Equal(
            SheinNeedsDetailReasons.NewLink,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: true, prevLinkTotal: null));
    }

    [Fact]
    public void NeedsCarrierTrackingFromDetail_WarehouseTrue_CarrierFalse()
    {
        var link = new SupplierPurchase
        {
            TrackingNo = "C2607123456789012",
            TrackingKind = SupplierTrackingKinds.Warehouse
        };
        var shein = BaseShein();
        shein.TrackingNo = link.TrackingNo;

        Assert.True(OrderSyncService.NeedsCarrierTrackingFromDetail(link, shein));

        link.TrackingNo = "1Z999AA10123456784";
        link.TrackingKind = SupplierTrackingKinds.Carrier;
        shein.TrackingNo = link.TrackingNo;
        shein.Carrier = "UPS";
        Assert.False(OrderSyncService.NeedsCarrierTrackingFromDetail(link, shein));
    }

    [Fact]
    public void WalletMisattributed_TotalZeroCouponInflated_NeedsDetail()
    {
        var order = LinkedOrder();
        var link = CostCompleteLink(order);
        link.SupplierTotal = 0m;
        link.SupplierCouponPrice = 17.55m;
        link.SupplierWalletPrice = 0m;
        link.SupplierMembershipDiscount = 0.92m;
        var shein = BaseShein();
        shein.Total = 0m;

        Assert.Equal(
            SheinNeedsDetailReasons.WalletCostStale,
            OrderSyncService.EvaluateSheinListNeedsDetail(
                order, link, shein, billnoLinked: false, prevLinkTotal: 0m));
    }

    [Fact]
    public void LooksLikeWalletMisattributed_MatchesGsu18jPattern()
    {
        Assert.True(OrderSyncService.LooksLikeWalletMisattributed(0m, 17.55m, 0m));
        Assert.False(OrderSyncService.LooksLikeWalletMisattributed(0m, 0m, 17.55m));
        Assert.False(OrderSyncService.LooksLikeWalletMisattributed(12.5m, 3m, 0m));
    }

    private static Order LinkedOrder() => new()
    {
        Id = Guid.NewGuid(),
        PlatformOrderId = "TT-1",
        FourSellerOrderId = 1,
        SupplierPurchases = []
    };

    private static SupplierPurchase CostCompleteLink(Order order)
    {
        var link = new SupplierPurchase
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SupplierOrderNo = "GSO1TEST",
            SupplierSubtotal = 10m,
            SupplierTotal = 12.5m,
            SupplierStatus = SupplierStatusGroups.Processing,
            TrackingKind = SupplierTrackingKinds.None
        };
        order.SupplierPurchases!.Add(link);
        return link;
    }

    private static SheinOrderDto BaseShein() => new()
    {
        Billno = "GSO1TEST",
        BuyerName = string.Empty,
        BuyerPhone = string.Empty,
        Address = string.Empty,
        City = string.Empty,
        State = string.Empty,
        PostalCode = string.Empty,
        CountryCode = string.Empty,
        TrackingNo = string.Empty,
        Carrier = string.Empty,
        Currency = "USD",
        Total = 12.5m,
        Subtotal = 10m
    };
}
