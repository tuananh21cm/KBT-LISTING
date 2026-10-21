using Application.Services;
using Domain.Entities;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Quy tắc gỡ mốc "Mua" dương tính giả. Fixture dựng theo ca thật 577494030896631910: billno
/// GSU18J247000TWB tạo 26/07 08:59:53 nhưng <c>pay_time = 0</c> nên SHEIN tự huỷ, trong khi mốc
/// "Mua" lại được stamp đúng bằng <c>addTime</c> của chính nó.
/// </summary>
public class OrderPurchaseEvidenceTests
{
    private static readonly DateTime GhostOrderedAt = new(2026, 7, 26, 8, 59, 53, DateTimeKind.Utc);

    private static Order OrderWithLinks(
        DateTime? purchasedAt,
        string? timelineSources,
        params SupplierPurchase[] links)
        => new()
        {
            FulfilPurchasedAt = purchasedAt,
            FulfilTimelineSources = timelineSources,
            SupplierPurchases = [.. links]
        };

    private static SupplierPurchase Ghost(DateTime? orderedAt) => new()
    {
        Id = Guid.NewGuid(),
        SupplierOrderNo = "GSU18J247000TWB",
        SupplierStatus = "canceled",
        OrderedAt = orderedAt,
        SupplierUnpaid = true
    };

    private static SupplierPurchase Paid(DateTime orderedAt) => new()
    {
        Id = Guid.NewGuid(),
        SupplierOrderNo = "GSU18F24400W3W3",
        SupplierStatus = "delivered",
        OrderedAt = orderedAt,
        SupplierPaidAt = orderedAt.AddMinutes(2)
    };

    [Fact]
    public void Clears_WhenMilestoneCameFromTheGhostBillno()
    {
        var order = OrderWithLinks(
            GhostOrderedAt,
            "{\"fulfilPurchasedAt\":\"shein\"}",
            Ghost(GhostOrderedAt));

        Assert.True(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
        Assert.Null(order.FulfilPurchasedAt);
    }

    [Fact]
    public void Clears_EvenWhenKindDiffers()
    {
        // Giá trị vừa stamp trong bộ nhớ có thể là Unspecified còn cột DB đọc ra là Utc.
        var order = OrderWithLinks(
            DateTime.SpecifyKind(GhostOrderedAt, DateTimeKind.Unspecified),
            "{\"fulfilPurchasedAt\":\"shein\"}",
            Ghost(GhostOrderedAt));

        Assert.True(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
    }

    [Fact]
    public void Keeps_WhenAnotherBillnoWasActuallyPaid()
    {
        var order = OrderWithLinks(
            GhostOrderedAt,
            "{\"fulfilPurchasedAt\":\"shein\"}",
            Ghost(GhostOrderedAt),
            Paid(GhostOrderedAt));

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
        Assert.Equal(GhostOrderedAt, order.FulfilPurchasedAt);
    }

    [Fact]
    public void Keeps_WhenOperatorEnteredTheMilestoneByHand()
    {
        var order = OrderWithLinks(
            GhostOrderedAt,
            "{\"fulfilPurchasedAt\":\"manual\"}",
            Ghost(GhostOrderedAt));

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
        Assert.Equal(GhostOrderedAt, order.FulfilPurchasedAt);
    }

    [Fact]
    public void Keeps_WhenMilestoneDoesNotMatchAnyGhostClock()
    {
        // Mốc đến từ nguồn khác (đợt backfill cũ, đơn NCC đã gỡ…) thì không được suy là dương tính
        // giả của billno này.
        var order = OrderWithLinks(
            GhostOrderedAt.AddDays(-3),
            "{\"fulfilPurchasedAt\":\"shein\"}",
            Ghost(GhostOrderedAt));

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
    }

    [Fact]
    public void Keeps_WhenGhostLinkHasNoClockToCompare()
    {
        var order = OrderWithLinks(
            GhostOrderedAt,
            "{\"fulfilPurchasedAt\":\"shein\"}",
            Ghost(orderedAt: null));

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
    }

    [Fact]
    public void Keeps_WhenOrderHasNoLinksLeftToProveProvenance()
    {
        var order = OrderWithLinks(GhostOrderedAt, "{\"fulfilPurchasedAt\":\"shein\"}");

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
    }

    [Fact]
    public void NoOp_WhenMilestoneIsAlreadyEmpty()
    {
        var order = OrderWithLinks(null, null, Ghost(GhostOrderedAt));

        Assert.False(OrderPurchaseEvidence.ClearPurchasedWhenOnlyGhostBillnosRemain(order));
    }
}
