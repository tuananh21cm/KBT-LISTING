using Application.Services;
using Domain.Entities;
using Domain.Orders;
using Xunit;

namespace Application.Tests;

/// <summary>
/// The denormalized columns on <c>Orders</c> are only trustworthy while they agree with the helpers
/// they were derived from. These tests walk the full cross product of the source fields and assert
/// that agreement, so a future rule change in <see cref="OrderWorkflowTabs"/> or
/// <see cref="OrderFulfilHandlingSignals"/> cannot silently leave the columns behind.
/// </summary>
public class OrderWorkflowSignalsTests
{
    private static readonly string?[] PlatformStatuses =
    [
        null,
        "",
        PlatformOrderStatuses.OnHold,
        PlatformOrderStatuses.AwaitingShipment,
        PlatformOrderStatuses.AwaitingCollection,
        PlatformOrderStatuses.InTransit,
        PlatformOrderStatuses.Delivered,
        PlatformOrderStatuses.Cancelled,
        "CANCELED",
        PlatformOrderStatuses.Refund,
        "REFUNDED",
        "SOME_UNKNOWN_STATUS"
    ];

    private static readonly DateTime?[] PurchasedStamps =
    [
        null,
        new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc)
    ];

    private static readonly Guid?[] FulfilAccountIds =
    [
        null,
        Guid.Parse("11111111-1111-1111-1111-111111111111")
    ];

    private static readonly string?[] TrackingNumbers =
    [
        null,
        "",
        "   ",
        "9400111899223197428490",  // USPS last-mile
        "C2607123456789",          // SHEIN warehouse code
        "n26071234567",            // warehouse code, lower case
        "C260712345",              // too short to be a warehouse code
        "CX26071234567"            // starts with C but not all digits
    ];

    private static readonly string?[] ShippingServices =
    [
        null,
        "",
        "USPS Ground Advantage",
        "usps first class",
        "SHEIN International Warehouse",
        "warehouse",
        "FedEx Home"
    ];

    /// <summary>Supplier link shapes that matter to the derived flags.</summary>
    private static IEnumerable<List<SupplierPurchase>> LinkSets()
    {
        yield return [];
        yield return [new SupplierPurchase()];
        yield return [new SupplierPurchase { TrackingKind = SupplierTrackingKinds.None }];
        yield return
        [
            new SupplierPurchase { TrackingKind = SupplierTrackingKinds.Carrier, TrackingNo = "9400111899223197428490" }
        ];
        yield return [new SupplierPurchase { TrackingKind = SupplierTrackingKinds.Carrier, TrackingNo = "" }];
        yield return
        [
            new SupplierPurchase { TrackingKind = SupplierTrackingKinds.Warehouse, TrackingNo = "C2607123456789" }
        ];
        yield return [new SupplierPurchase { WarehouseTrackingNo = "C2607123456789" }];
        yield return [new SupplierPurchase { Carrier = "USPS" }];
        yield return [new SupplierPurchase { Carrier = "SHEIN Warehouse" }];
        yield return
        [
            new SupplierPurchase { Carrier = "USPS", TrackingKind = SupplierTrackingKinds.Carrier, TrackingNo = "94001" },
            new SupplierPurchase { WarehouseTrackingNo = "N2607123456789" }
        ];
    }

    private static Order NewOrder(
        string? platformStatus,
        DateTime? purchasedAt,
        Guid? fulfilAccountId,
        string? trackingNo,
        string? shippingService,
        List<SupplierPurchase> links) =>
        new()
        {
            PlatformOrderStatus = platformStatus,
            FulfilPurchasedAt = purchasedAt,
            FulfilAccountId = fulfilAccountId,
            TrackingNo = trackingNo,
            ShippingService = shippingService,
            SupplierPurchases = links
        };

    private static IEnumerable<Order> AllCombinations()
    {
        foreach (var status in PlatformStatuses)
        foreach (var purchasedAt in PurchasedStamps)
        foreach (var accountId in FulfilAccountIds)
        foreach (var trackingNo in TrackingNumbers)
        foreach (var service in ShippingServices)
        foreach (var links in LinkSets())
        {
            yield return NewOrder(status, purchasedAt, accountId, trackingNo, service, links);
        }
    }

    [Fact]
    public void Apply_MatchesResolveAndSignalHelpers_ForEveryCombination()
    {
        var checked_ = 0;
        foreach (var order in AllCombinations())
        {
            var expectedTab = OrderWorkflowTabs.Resolve(order);
            var expectedCarrier = OrderWorkflowTabs.HasCarrierTracking(order);
            var expectedPurchase = order.SupplierPurchases.Count > 0;
            var expectedUsps = OrderFulfilHandlingSignals.HasUspsSignal(order);
            var expectedWarehouse = OrderFulfilHandlingSignals.HasWarehouseTrack(order);

            OrderWorkflowSignals.Apply(order);

            Assert.Equal(expectedTab, order.WorkflowTab);
            Assert.Equal(expectedCarrier, order.HasCarrierTracking);
            Assert.Equal(expectedPurchase, order.HasSupplierPurchase);
            Assert.Equal(expectedUsps, order.HasUspsSignal);
            Assert.Equal(expectedWarehouse, order.HasWarehouseSignal);
            checked_++;
        }

        // Guard the guard: a shrunken matrix would make this test pass for the wrong reason.
        Assert.True(checked_ > 10_000, $"expected a broad matrix, only walked {checked_} combinations");
    }

    [Fact]
    public void Apply_IsIdempotent_AndReportsNoChangeOnSecondPass()
    {
        foreach (var order in AllCombinations())
        {
            Assert.True(OrderWorkflowSignals.Apply(order) || order.WorkflowTab is not null);

            var tab = order.WorkflowTab;
            Assert.False(OrderWorkflowSignals.Apply(order), "second Apply reported a change");
            Assert.Equal(tab, order.WorkflowTab);
        }
    }

    [Fact]
    public void Apply_WithEffectiveLinks_IgnoresTheNavigationAndRestoresIt()
    {
        var deletedLink = new SupplierPurchase
        {
            TrackingKind = SupplierTrackingKinds.Carrier,
            TrackingNo = "9400111899223197428490"
        };
        var navigation = new List<SupplierPurchase> { deletedLink };
        var order = NewOrder(PlatformOrderStatuses.AwaitingShipment, null, null, null, null, navigation);

        // The link is about to be detached, so the order goes back to "chờ xử lý".
        var changed = OrderWorkflowSignals.Apply(order, []);

        Assert.True(changed);
        Assert.Equal(OrderWorkflowTabs.PendingProcess, order.WorkflowTab);
        Assert.False(order.HasCarrierTracking);
        Assert.False(order.HasSupplierPurchase);
        Assert.Same(navigation, order.SupplierPurchases);
    }

    [Fact]
    public void Apply_WithEffectiveLinks_SeesLinksMissingFromTheNavigation()
    {
        var order = NewOrder(PlatformOrderStatuses.AwaitingShipment, null, null, null, null, []);
        var incoming = new SupplierPurchase
        {
            TrackingKind = SupplierTrackingKinds.Carrier,
            TrackingNo = "9400111899223197428490"
        };

        OrderWorkflowSignals.Apply(order, [incoming]);

        Assert.Equal(OrderWorkflowTabs.AwaitingCollectionGroup, order.WorkflowTab);
        Assert.True(order.HasCarrierTracking);
        Assert.True(order.HasSupplierPurchase);
        Assert.Empty(order.SupplierPurchases);
    }

    [Fact]
    public void SourceProperties_CoverEveryFieldTheHelpersRead()
    {
        // If a rule starts reading another Order column, add it here or the SaveChangesAsync writer
        // will not notice that column changing.
        Assert.Equal(
            [
                nameof(Order.PlatformOrderStatus),
                nameof(Order.FulfilPurchasedAt),
                nameof(Order.FulfilAccountId),
                nameof(Order.TrackingNo),
                nameof(Order.ShippingService)
            ],
            OrderWorkflowSignals.SourceProperties);
    }
}
