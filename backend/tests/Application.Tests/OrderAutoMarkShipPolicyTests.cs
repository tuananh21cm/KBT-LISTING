using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Xunit;

namespace Application.Tests;

public class OrderAutoMarkShipPolicyTests
{
    private static readonly DateTime Now =
        new(2026, 7, 26, 6, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("USPS", "usps")]
    [InlineData("U.S.P.S.", "USPS")]
    [InlineData("  Fed Ex  ", "fed-ex")]
    public void CarrierMatch_IsExactAfterSafeNormalization(string shein, string fourSeller)
    {
        Assert.True(OrderAutoMarkShipPolicy.IsExactCarrierMatch(shein, fourSeller));
    }

    [Fact]
    public void CarrierMatch_DoesNotUseFuzzyFallback()
    {
        Assert.False(OrderAutoMarkShipPolicy.IsExactCarrierMatch("USPS Ground", "USPS"));
        Assert.False(OrderAutoMarkShipPolicy.IsExactCarrierMatch("", "USPS"));
    }

    [Fact]
    public void ShouldAttempt_RequiresAwaitingShipmentAndCarrier()
    {
        var order = new Order
        {
            OrderStatus = "unknown",
            PlatformOrderStatus = PlatformOrderStatuses.InTransit
        };
        var link = CarrierLink();

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        order.PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment;
        link.TrackingKind = SupplierTrackingKinds.Warehouse;
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        link.TrackingKind = SupplierTrackingKinds.Carrier;
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void ShouldAttempt_SkipsWhenOrderAlreadyHasPublishedTracking()
    {
        var order = AwaitingShipmentOrder();
        order.TrackingNo = "GFUS01063840476675";
        var link = CarrierLink();

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        // Joint package with a different SHEIN tracking still must not re-mark.
        link.TrackingNo = "GFUS01063840476699";
        link.AutoMarkShipTrackingNo = null;
        link.AutoMarkShipStatus = null;
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void ShouldAttempt_RequiresSheinCarrierName()
    {
        var order = AwaitingShipmentOrder();
        var link = CarrierLink();
        link.Carrier = null;
        // Unknown prefix — cannot infer → not eligible.
        link.TrackingNo = "ZZZZ999999999999";
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        // GFUS prefix alone is enough to attempt (resolver fills Carrier at publish).
        link.TrackingNo = "GFUS01071879965825";
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        link.Carrier = "GOFO";
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void TryReopenMissingCarrierWait_ClearsLegacyBlock()
    {
        var link = CarrierLink();
        link.Carrier = null;
        link.TrackingNo = "UNKNOWN999";
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Blocked;
        link.AutoMarkShipError = OrderAutoMarkShipPolicy.MissingCarrierError;
        link.AutoMarkShipTrackingNo = link.TrackingNo;

        Assert.True(OrderAutoMarkShipPolicy.IsMissingCarrierBlock(link));
        Assert.True(OrderAutoMarkShipPolicy.TryReopenMissingCarrierWait(link));
        Assert.Equal(AutoMarkShipStatuses.Pending, link.AutoMarkShipStatus);
        Assert.Null(link.AutoMarkShipError);

        // Unknown tracking — still not eligible.
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(AwaitingShipmentOrder(), link, Now));

        link.TrackingNo = "GFUS01071879965825";
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(AwaitingShipmentOrder(), link, Now));
    }

    [Theory]
    [InlineData(AutoMarkShipStatuses.Succeeded)]
    [InlineData(AutoMarkShipStatuses.Blocked)]
    [InlineData(AutoMarkShipStatuses.Unconfirmed)]
    public void ShouldAttempt_IsIdempotentForTerminalState(string status)
    {
        var order = AwaitingShipmentOrder();
        var link = CarrierLink();
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        link.AutoMarkShipStatus = status;

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void NeedsReconcile_RequiresRequestId()
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
        Assert.False(OrderAutoMarkShipPolicy.NeedsReconcile(link));

        link.AutoMarkShipRequestId = "req-1";
        Assert.True(OrderAutoMarkShipPolicy.NeedsReconcile(link));

        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
        Assert.True(OrderAutoMarkShipPolicy.NeedsReconcile(link));

        link.AutoMarkShipRequestId = null;
        Assert.False(OrderAutoMarkShipPolicy.NeedsReconcile(link));
    }

    [Fact]
    public void ShouldAttempt_SkipsUnconfirmedEvenWithoutRequestId()
    {
        var order = AwaitingShipmentOrder();
        var link = CarrierLink();
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void ShouldAttempt_RetriesFailedOnlyAfterCooldown()
    {
        var order = AwaitingShipmentOrder();
        var link = CarrierLink();
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
        link.AutoMarkShipAttemptedAt = Now.AddMinutes(-5);

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        link.AutoMarkShipAttemptedAt = Now - OrderAutoMarkShipPolicy.RetryCooldown;
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void UnconfirmedGracePeriod_AllowsSlowPlatformConfirm()
    {
        Assert.True(OrderAutoMarkShipPolicy.UnconfirmedGracePeriod.TotalMinutes >= 30);
        Assert.True(OrderAutoMarkShipPolicy.RetryCooldown.TotalMinutes >= 15);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void AllowManualClearAndRepublish_OnlyWhenRemoteConfirmedOrSafeToRetry(
        bool confirmedAbsent,
        bool safeToRetry,
        bool expected)
    {
        Assert.Equal(
            expected,
            OrderAutoMarkShipPolicy.AllowManualClearAndRepublish(confirmedAbsent, safeToRetry));
    }

    [Fact]
    public void DowngradeUnconfirmedToFailed_ClearsRequestIdWithoutRestampingAttemptedAt()
    {
        var attemptedAt = Now.AddMinutes(-45);
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
        link.AutoMarkShipRequestId = "req-stuck";
        link.AutoMarkShipAttemptedAt = attemptedAt;
        link.AutoMarkShipError = "pending…";

        OrderAutoMarkShipPolicy.DowngradeUnconfirmedToFailed(
            link,
            "Mark Ship trước đó chưa xác nhận và sàn vẫn chưa có mã — sẽ thử lại sau.");

        Assert.Equal(AutoMarkShipStatuses.Failed, link.AutoMarkShipStatus);
        Assert.Null(link.AutoMarkShipRequestId);
        Assert.Equal(attemptedAt, link.AutoMarkShipAttemptedAt);
        Assert.Contains("chưa xác nhận", link.AutoMarkShipError);
        Assert.False(OrderAutoMarkShipPolicy.NeedsReconcile(link));

        var order = AwaitingShipmentOrder();
        link.AutoMarkShipTrackingNo = link.TrackingNo;
        // Grace ≥ cooldown and AttemptedAt preserved → Auto may republish in the same pass.
        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void DowngradeUnconfirmedToFailed_UnlocksManualPathFromUnconfirmedGate()
    {
        var link = CarrierLink();
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
        link.AutoMarkShipRequestId = "req-1";
        Assert.True(OrderAutoMarkShipPolicy.NeedsReconcile(link));

        OrderAutoMarkShipPolicy.DowngradeUnconfirmedToFailed(link);
        Assert.False(OrderAutoMarkShipPolicy.NeedsReconcile(link));
        Assert.Equal(AutoMarkShipStatuses.Failed, link.AutoMarkShipStatus);
    }

    [Fact]
    public void ResolveMarkShipPoll_InteractiveIsLongerThanCloudflare()
    {
        var interactive = OrderAutoMarkShipPolicy.ResolveMarkShipPoll(interactive: true);
        var cloudflare = OrderAutoMarkShipPolicy.ResolveMarkShipPoll(interactive: false);
        Assert.True(interactive.Attempts * interactive.DelayMs > cloudflare.Attempts * cloudflare.DelayMs);
        Assert.Equal(
            OrderAutoMarkShipPolicy.InteractiveMarkShipPollAttempts,
            interactive.Attempts);
        Assert.Equal(
            OrderAutoMarkShipPolicy.CloudflareMarkShipPollAttempts,
            cloudflare.Attempts);
    }

    [Theory]
    [InlineData("Pending", "processing", false)]
    [InlineData("Pending", "success", false)]
    [InlineData("Failed", null, false)]
    [InlineData("Failed", "success", false)]
    [InlineData("Failed", "Hết thời gian chờ kết quả Mark Shipped từ 4Seller.", false)]
    [InlineData("Failed", "Invalid tracking number", true)]
    [InlineData("Success", "ok", false)]
    public void IsExplicitMarkShipReject_OnlyClearRejects(string status, string? message, bool expected)
    {
        Assert.Equal(expected, OrderAutoMarkShipPolicy.IsExplicitMarkShipReject(status, message));
    }

    [Fact]
    public void SyncRequestBudget_KeepsUnderCloudflareProxyTimeout()
    {
        Assert.True(OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest >= 1);
        Assert.True(OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest <= 3);
        Assert.True(OrderAutoMarkShipPolicy.MaxDurationPerSyncRequest.TotalSeconds is >= 20 and <= 60);
    }

    [Fact]
    public void ShouldAttempt_NewTrackingOnUntrackedOrder_AllowsFirstPublish()
    {
        // Order still has no published tracking (e.g. joint link just got carrier from SHEIN).
        var order = AwaitingShipmentOrder();
        var link = CarrierLink();
        link.AutoMarkShipTrackingNo = "OLD123";
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;

        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));

        order.TrackingNo = "OLD123";
        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Theory]
    [InlineData("TikTok Shipping")]
    [InlineData("Platform Shipping")]
    public void ShouldAttempt_SkipsTikTokPlatformShipping(string shippingMethod)
    {
        var order = AwaitingShipmentOrder();
        order.ShippingMethod = shippingMethod;
        var link = CarrierLink();

        Assert.False(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    [Fact]
    public void ShouldAttempt_AllowsSellerShipping()
    {
        var order = AwaitingShipmentOrder();
        order.ShippingMethod = "Seller Shipping";
        var link = CarrierLink();

        Assert.True(OrderAutoMarkShipPolicy.ShouldAttempt(order, link, Now));
    }

    private static Order AwaitingShipmentOrder() => new()
    {
        OrderStatus = "unknown",
        PlatformOrderStatus = PlatformOrderStatuses.AwaitingShipment
    };

    private static SupplierPurchase CarrierLink() => new()
    {
        SupplierOrderNo = "SHEIN-1",
        TrackingNo = "9400111899223344556677",
        Carrier = "USPS",
        TrackingKind = SupplierTrackingKinds.Carrier
    };
}
