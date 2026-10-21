using Application.DTOs.FourSeller;
using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Xunit;

namespace Application.Tests;

public class PlatformOrderStatusesTests
{
    [Theory]
    [InlineData("awaiting_shipment", "AWAITING_SHIPMENT")]
    [InlineData("CANCELED", "CANCELLED")]
    [InlineData("REFUNDED", "REFUND")]
    [InlineData("COMPLETED", "DELIVERED")]
    [InlineData("completed", "DELIVERED")]
    [InlineData("  in_transit  ", "IN_TRANSIT")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Normalize_Uppercases_AndAliasesCanceled(string? input, string? expected)
    {
        Assert.Equal(expected, PlatformOrderStatuses.Normalize(input));
    }

    [Fact]
    public void IsCanceledCommerce_PrefersPlatform_OverLegacy()
    {
        Assert.True(PlatformOrderStatuses.IsCanceledCommerce("CANCELLED", "to_ship"));
        Assert.False(PlatformOrderStatuses.IsCanceledCommerce("AWAITING_SHIPMENT", "canceled"));
        Assert.True(PlatformOrderStatuses.IsCanceledCommerce(null, "canceled"));
        Assert.True(PlatformOrderStatuses.IsCanceledCommerce("", "cancelled"));
    }

    [Fact]
    public void IsNonRevenue_IncludesCancelAndRefund()
    {
        Assert.True(PlatformOrderStatuses.IsNonRevenue("CANCELLED"));
        Assert.True(PlatformOrderStatuses.IsNonRevenue("REFUND"));
        Assert.True(PlatformOrderStatuses.IsNonRevenue("REFUNDED"));
        Assert.False(PlatformOrderStatuses.IsNonRevenue("DELIVERED"));
        Assert.False(PlatformOrderStatuses.IsNonRevenue("AWAITING_SHIPMENT"));
    }

    /// <summary>
    /// Order list vẫn báo đơn đã hoàn dưới status thương mại (<c>shipped</c> / <c>COMPLETED</c>).
    /// Không latch thì mỗi lượt sync đẩy đơn khỏi REFUND và UI hiện "Đang giao".
    /// </summary>
    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("DELIVERED")]
    [InlineData("IN_TRANSIT")]
    [InlineData(null)]
    public void ResolveCommerceStatus_LatchesRefundSetByAfterSales(string? raw)
    {
        var resolved = PlatformOrderStatuses.ResolveCommerceStatus(
            raw,
            localPlatformStatus: PlatformOrderStatuses.Refund);

        Assert.Equal(PlatformOrderStatuses.Refund, resolved);
    }

    [Fact]
    public void ResolveCommerceStatus_TerminalCancelInRecordWins()
    {
        // Đơn hoàn rồi bị TikTok chốt CANCELLED: bản ghi mới thắng latch.
        var resolved = PlatformOrderStatuses.ResolveCommerceStatus(
            "CANCELLED",
            localPlatformStatus: PlatformOrderStatuses.Refund);

        Assert.Equal(PlatformOrderStatuses.Cancelled, resolved);
    }

    [Fact]
    public void ResolveCommerceStatus_NeverInventsRefund()
    {
        // Sync đơn không còn suy ra REFUND — trạng thái đó chỉ đến từ luồng sau bán.
        Assert.Equal(
            PlatformOrderStatuses.Delivered,
            PlatformOrderStatuses.ResolveCommerceStatus("COMPLETED", PlatformOrderStatuses.InTransit));
        Assert.Equal(
            PlatformOrderStatuses.AwaitingShipment,
            PlatformOrderStatuses.ResolveCommerceStatus("awaiting_shipment", null));
    }

    [Fact]
    public void ResolveCommerceStatus_NullRecord_KeepsNothingToWrite()
    {
        Assert.Null(PlatformOrderStatuses.ResolveCommerceStatus(null, PlatformOrderStatuses.InTransit));
    }
}

public class TikTokReturnStatusesTests
{
    [Theory]
    [InlineData("RETURN_OR_REFUND_REQUEST_COMPLETE", AfterSalesOutcome.Refunded)]
    [InlineData("return_or_refund_request_complete", AfterSalesOutcome.Refunded)]
    [InlineData("RETURN_OR_REFUND_REQUEST_CANCEL", AfterSalesOutcome.Closed)]
    [InlineData("AWAITING_BUYER_SHIP", AfterSalesOutcome.Open)]
    [InlineData("BUYER_SHIPPED_ITEM", AfterSalesOutcome.Open)]
    public void Classify_MapsTerminalStatesOnly(string raw, AfterSalesOutcome expected)
    {
        Assert.Equal(expected, TikTokReturnStatuses.Classify(raw));
    }

    /// <summary>
    /// Trạng thái lạ phải rơi về <c>Open</c>: <c>Open</c> chỉ ghi lý do, còn hai nhánh kia đặt hoặc
    /// gỡ REFUND — đoán sai ở đó làm sai doanh thu.
    /// </summary>
    [Theory]
    [InlineData("SOMETHING_TIKTOK_ADDS_LATER")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_UnknownStaysOpen(string? raw)
    {
        Assert.Equal(AfterSalesOutcome.Open, TikTokReturnStatuses.Classify(raw));
    }
}

public class OrderWorkflowTabsTests
{
    [Fact]
    public void Resolve_Refund_And_Cancelled_Win()
    {
        Assert.Equal(
            OrderWorkflowTabs.Refund,
            OrderWorkflowTabs.Resolve(new Order
            {
                OrderStatus = "unknown",
                PlatformOrderStatus = PlatformOrderStatuses.Refund
            }));
        Assert.Equal(
            OrderWorkflowTabs.Cancelled,
            OrderWorkflowTabs.Resolve(new Order
            {
                OrderStatus = "unknown",
                PlatformOrderStatus = PlatformOrderStatuses.Cancelled,
                FulfilPurchasedAt = DateTime.UtcNow
            }));
    }

    [Fact]
    public void Resolve_Delivered_OnlyFromTikTok()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.InTransit,
            DeliveredAt = DateTime.UtcNow,
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    SupplierOrderNo = "B1",
                    TrackingKind = SupplierTrackingKinds.Carrier,
                    TrackingNo = "9400111899223344556677",
                    SupplierStatus = SupplierStatusGroups.Delivered
                }
            ]
        };
        // Shein delivered + TikTok still IN_TRANSIT → Đang giao (not Giao thành công)
        Assert.Equal(OrderWorkflowTabs.InTransit, OrderWorkflowTabs.Resolve(order));

        order.PlatformOrderStatus = PlatformOrderStatuses.Delivered;
        Assert.Equal(OrderWorkflowTabs.Delivered, OrderWorkflowTabs.Resolve(order));
    }

    /// <summary>
    /// <c>COMPLETED</c> để nguyên thì không khớp nhánh terminal nào, và mã vận đơn còn trên đơn đẩy
    /// nó về <c>AWAITING_COLLECTION_GROUP</c> — đúng triệu chứng đơn đã hoàn/đóng case vẫn hiện
    /// "Chờ lấy" / "Đang giao".
    /// </summary>
    [Fact]
    public void Resolve_Completed_IsDelivered_NotInTransit()
    {
        var order = new Order
        {
            OrderStatus = "shipped",
            PlatformOrderStatus = "COMPLETED",
            TrackingNo = "9400111899223344556677",
            ShippingService = "USPS"
        };

        Assert.Equal(OrderWorkflowTabs.Delivered, OrderWorkflowTabs.Resolve(order));
    }

    [Fact]
    public void Resolve_Purchased_WarehouseTrack_StaysPurchased()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
            FulfilPurchasedAt = DateTime.UtcNow,
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    SupplierOrderNo = "B1",
                    TrackingKind = SupplierTrackingKinds.Warehouse,
                    TrackingNo = "C26072201441374",
                    SupplierStatus = SupplierStatusGroups.Processing
                }
            ]
        };
        Assert.Equal(OrderWorkflowTabs.Purchased, OrderWorkflowTabs.Resolve(order));
    }

    [Fact]
    public void Resolve_Purchased_CarrierTrack_BecomesAwaitingCollection()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
            FulfilPurchasedAt = DateTime.UtcNow,
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    SupplierOrderNo = "B1",
                    TrackingKind = SupplierTrackingKinds.Carrier,
                    TrackingNo = "9400111899223344556677",
                    SupplierStatus = SupplierStatusGroups.Shipped
                }
            ]
        };
        Assert.Equal(OrderWorkflowTabs.AwaitingCollectionGroup, OrderWorkflowTabs.Resolve(order));
    }

    [Fact]
    public void Resolve_AwaitingCollection_IsAwaitingCollectionGroup()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingCollection,
            TrackingNo = "1Z999AA10123456784",
            ShippingService = "UPS"
        };
        Assert.Equal(OrderWorkflowTabs.AwaitingCollectionGroup, OrderWorkflowTabs.Resolve(order));
    }

    [Fact]
    public void Resolve_InTransit_WinsOverCarrierTrack()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.InTransit,
            FulfilPurchasedAt = DateTime.UtcNow,
            SupplierPurchases =
            [
                new SupplierPurchase
                {
                    SupplierOrderNo = "B1",
                    TrackingKind = SupplierTrackingKinds.Carrier,
                    TrackingNo = "9400111899223344556677",
                    SupplierStatus = SupplierStatusGroups.Shipped
                }
            ]
        };
        Assert.Equal(OrderWorkflowTabs.InTransit, OrderWorkflowTabs.Resolve(order));
    }

    [Fact]
    public void Resolve_PendingProcess_WithoutPurchase()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment
        };
        Assert.Equal(OrderWorkflowTabs.PendingProcess, OrderWorkflowTabs.Resolve(order));
    }
}

public class OrderAutoMarkShipPolicyRefundTests
{
    [Fact]
    public void ShouldAttempt_SkipsRefundAndCancelled()
    {
        var link = new SupplierPurchase
        {
            SupplierOrderNo = "B1",
            TrackingKind = SupplierTrackingKinds.Carrier,
            TrackingNo = "9400111899223344556677",
            AutoMarkShipStatus = AutoMarkShipStatuses.Pending
        };
        var now = DateTime.UtcNow;

        var refund = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.Refund,
            TrackingNo = null
        };
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(refund, link, now));

        var cancelled = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.Cancelled,
            TrackingNo = null
        };
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(cancelled, link, now));

        var eligible = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment,
            TrackingNo = null
        };
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(eligible, link, now));
    }
}

public class OrderFourSellerStatusApplyTests
{
    [Fact]
    public void ApplyPlatformClocks_KeepsTrackingNo_AndDoesNotOutrankManual()
    {
        var manualAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            OrderStatus = "unknown",
            TrackingNo = "9400111899223344556677",
            ShippingService = "USPS"
        };
        OrderFulfilTimeline.Stamp(
            order, OrderFulfilTimeline.Milestone.TrackingAdded,
            manualAt, OrderFulfilTimeline.Source.Manual);

        OrderFourSellerStatusApply.ApplyPlatformClocks(
            order,
            new FourSellerOrderTimeInfoDto { OrderShippedTime = "2026-07-20 10:00:00" });

        // Mã vận đơn vẫn do Sync Fulfil / Mark Ship sở hữu, sync 4Seller không đụng.
        Assert.Equal("9400111899223344556677", order.TrackingNo);
        Assert.Equal("USPS", order.ShippingService);
        // Mark Ship thủ công xếp hạng cao hơn Platform nên giữ nguyên.
        Assert.Equal(manualAt, order.TrackingAddedAt);
    }

    /// <summary>
    /// Mốc do đợt backfill cũ để lại không mang provenance nên xếp hạng <c>legacy</c> và phải
    /// nhường cho clock thật từ sàn — đây là cơ chế để dữ liệu cũ tự sửa dần.
    /// </summary>
    [Fact]
    public void ApplyPlatformClocks_ReplacesLegacyTrackingClock()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            TrackingNo = "9400111899223344556677",
            TrackingAddedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        OrderFourSellerStatusApply.ApplyPlatformClocks(
            order,
            new FourSellerOrderTimeInfoDto { OrderShippedTime = "2026-07-20 10:00:00" });

        Assert.Equal(
            new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc),
            order.TrackingAddedAt);
    }

    /// <summary>
    /// SHEIN không phát ra mốc gán mã vận đơn (158 payload gốc chỉ có shipping_no trần), nên nếu
    /// 4Seller cũng bỏ qua OrderShippedTime thì cột Track vĩnh viễn trống và UI hiện "Track?".
    /// </summary>
    [Fact]
    public void ApplyPlatformClocks_StampsTrackingFromOrderShippedTime()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.AwaitingCollection,
            TrackingNo = "1Z999AA10123456784",
            ShippingService = "UPS"
        };

        OrderFourSellerStatusApply.ApplyPlatformClocks(
            order,
            new FourSellerOrderTimeInfoDto { OrderShippedTime = "2026-07-20 10:00:00" });

        Assert.Equal(
            new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc),
            order.TrackingAddedAt);
        // Transit và Delivered vẫn thuộc về SHEIN, 4Seller không được chạm vào.
        Assert.Null(order.InTransitAt);
        Assert.Null(order.DeliveredAt);
    }

    [Fact]
    public void ApplyPlatformClocks_DoesNotStampTransitOrDelivered()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.InTransit,
            TrackingNo = "1Z999AA10123456784",
            ShippingService = "UPS"
        };

        OrderFourSellerStatusApply.ApplyPlatformClocks(order, null);
        Assert.Null(order.InTransitAt);
        Assert.Null(order.TrackingAddedAt);
        Assert.Null(order.DeliveredAt);

        order.PlatformOrderStatus = PlatformOrderStatuses.Delivered;
        OrderFourSellerStatusApply.ApplyPlatformClocks(order, null);
        Assert.Null(order.InTransitAt);
        Assert.Null(order.DeliveredAt);
    }

    [Fact]
    public void ApplyPlatformClocks_Cancelled_UsesCancelTime()
    {
        var cancel = new DateTime(2026, 7, 20, 8, 30, 0, DateTimeKind.Utc);
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.Cancelled
        };

        OrderFourSellerStatusApply.ApplyPlatformClocks(
            order,
            new FourSellerOrderTimeInfoDto { CancelTime = "2026-07-20 08:30:00" });

        Assert.Equal(cancel, order.CanceledAt);
    }

    /// <summary>
    /// Mốc Refunded thuộc luồng đồng bộ đơn sau bán; order list chỉ mang mốc đóng đơn nên stamp
    /// RefundedAt ở đây sẽ dựng bằng chứng hoàn từ dữ liệu không nói gì về hoàn tiền.
    /// </summary>
    [Fact]
    public void ApplyPlatformClocks_Refund_StampsCanceled_NotRefunded()
    {
        var cancel = new DateTime(2026, 7, 20, 8, 30, 0, DateTimeKind.Utc);
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.Refund,
            CancelReason = "buyer return"
        };

        OrderFourSellerStatusApply.ApplyPlatformClocks(
            order,
            new FourSellerOrderTimeInfoDto { CancelTime = "2026-07-20 08:30:00" });

        Assert.Equal(cancel, order.CanceledAt);
        Assert.Null(order.RefundedAt);
    }

    [Fact]
    public void ClassifyTracking_WarehouseVsCarrier()
    {
        Assert.Equal(
            SupplierTrackingKinds.Warehouse,
            OrderFourSellerStatusApply.ClassifyTracking("C26072201441374", null));
        Assert.Equal(
            SupplierTrackingKinds.Carrier,
            OrderFourSellerStatusApply.ClassifyTracking("9400111899223344556677", "USPS"));
    }

    [Fact]
    public void ApplyOrderStatusFromRecord_WritesNormalizedBucket()
    {
        var order = new Order { OrderStatus = "unknown" };
        OrderFourSellerStatusApply.ApplyOrderStatusFromRecord(order, "cancelled");
        Assert.Equal("canceled", order.OrderStatus);

        OrderFourSellerStatusApply.ApplyOrderStatusFromRecord(order, "  to_ship  ");
        Assert.Equal("to_ship", order.OrderStatus);

        OrderFourSellerStatusApply.ApplyOrderStatusFromRecord(order, null);
        Assert.Equal("to_ship", order.OrderStatus);

        var blank = new Order { OrderStatus = "" };
        OrderFourSellerStatusApply.ApplyOrderStatusFromRecord(blank, "   ");
        Assert.Equal("unknown", blank.OrderStatus);
    }

    [Fact]
    public void ApplyTrackingFromRecord_FillsWhenPresent_NeverClears()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            TrackingNo = "KEEP123",
            ShippingService = "USPS"
        };

        OrderFourSellerStatusApply.ApplyTrackingFromRecord(
            order,
            new FourSellerOrderRecordDto
            {
                TrackingNo = null,
                ShippingServiceInfo = new FourSellerOrderShippingServiceDto { ShippingService = null }
            });
        Assert.Equal("KEEP123", order.TrackingNo);
        Assert.Equal("USPS", order.ShippingService);

        OrderFourSellerStatusApply.ApplyTrackingFromRecord(
            order,
            new FourSellerOrderRecordDto
            {
                TrackingNo = " 9400111899223344556677 ",
                ShippingServiceInfo = new FourSellerOrderShippingServiceDto { ShippingService = " UPS " }
            });
        Assert.Equal("9400111899223344556677", order.TrackingNo);
        Assert.Equal("UPS", order.ShippingService);
    }

    [Fact]
    public void ApplyTrackingFromRecord_SkipsWarehouseCodes()
    {
        var order = new Order { OrderStatus = "unknown" };

        OrderFourSellerStatusApply.ApplyTrackingFromRecord(
            order,
            new FourSellerOrderRecordDto
            {
                TrackingNo = "C26072201441374",
                ShippingServiceInfo = new FourSellerOrderShippingServiceDto
                {
                    ShippingService = "International Warehouse"
                }
            });

        Assert.Null(order.TrackingNo);
        Assert.Equal("International Warehouse", order.ShippingService);

        order.TrackingNo = "KEEP-CARRIER";
        OrderFourSellerStatusApply.ApplyTrackingFromRecord(
            order,
            new FourSellerOrderRecordDto
            {
                TrackingNo = "N26072201441374",
                ShippingServiceInfo = new FourSellerOrderShippingServiceDto { ShippingService = "WH" }
            });
        Assert.Equal("KEEP-CARRIER", order.TrackingNo);
    }

    [Fact]
    public void OrderFulfilTimeline_SheinDelivered_OverridesLegacy_AndNormalizeClearsInvertedTransit()
    {
        var track = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var sheinDelivered = new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc);
        var lateSyncTransit = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
        var lateSyncDelivered = new DateTime(2026, 7, 26, 12, 5, 0, DateTimeKind.Utc);

        var order = new Order
        {
            OrderStatus = "unknown",
            TrackingAddedAt = track,
            InTransitAt = lateSyncTransit,
            DeliveredAt = lateSyncDelivered,
            FulfilTimelineSources = """{"trackingAddedAt":"legacy","inTransitAt":"legacy","deliveredAt":"legacy"}"""
        };

        OrderFulfilTimeline.Stamp(
            order, OrderFulfilTimeline.Milestone.Delivered, sheinDelivered,
            OrderFulfilTimeline.Source.Shein);
        OrderFulfilTimeline.Normalize(order);

        Assert.Equal(sheinDelivered, order.DeliveredAt);
        Assert.Null(order.InTransitAt);
        Assert.Equal(track, order.TrackingAddedAt);
    }

    /// <summary>
    /// Track (mã lên sàn, 4Seller) và Transit (rời kho SHEIN) là hai sự kiện độc lập ở hai hệ
    /// thống, thứ tự giữa chúng không cố định: 47/154 đơn đo được có hàng rời kho trước khi mã lên
    /// sàn. Giữ nguyên cả hai thay vì xoá mốc hợp lệ.
    /// </summary>
    [Fact]
    public void OrderFulfilTimeline_Normalize_KeepsTransitEarlierThanTracking()
    {
        var purchased = new DateTime(2026, 7, 22, 7, 0, 0, DateTimeKind.Utc);
        var transit = new DateTime(2026, 7, 22, 17, 42, 57, DateTimeKind.Utc);
        var track = new DateTime(2026, 7, 23, 1, 13, 45, DateTimeKind.Utc);
        var delivered = new DateTime(2026, 7, 29, 16, 26, 45, DateTimeKind.Utc);

        var order = new Order
        {
            OrderStatus = "unknown",
            FulfilPurchasedAt = purchased,
            TrackingAddedAt = track,
            InTransitAt = transit,
            DeliveredAt = delivered
        };

        OrderFulfilTimeline.Normalize(order);

        Assert.Equal(transit, order.InTransitAt);
        Assert.Equal(track, order.TrackingAddedAt);
    }

    [Fact]
    public void OrderFulfilTimeline_Normalize_ClearsTransitBeforePurchase()
    {
        var purchased = new DateTime(2026, 7, 22, 7, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            OrderStatus = "unknown",
            FulfilPurchasedAt = purchased,
            InTransitAt = purchased.AddHours(-3)
        };

        OrderFulfilTimeline.Normalize(order);

        Assert.Null(order.InTransitAt);
    }

    [Fact]
    public void OrderFulfilTimeline_Normalize_ClearsTransitAtOrAfterDelivered()
    {
        var sheinDelivered = new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc);
        var lateSyncTransit = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

        var order = new Order
        {
            OrderStatus = "unknown",
            InTransitAt = lateSyncTransit,
            DeliveredAt = lateSyncTransit,
            FulfilTimelineSources = """{"inTransitAt":"legacy","deliveredAt":"legacy"}"""
        };

        OrderFulfilTimeline.Stamp(
            order, OrderFulfilTimeline.Milestone.Delivered, sheinDelivered,
            OrderFulfilTimeline.Source.Shein);
        OrderFulfilTimeline.Normalize(order);

        Assert.Equal(sheinDelivered, order.DeliveredAt);
        Assert.Null(order.InTransitAt);
    }
}
