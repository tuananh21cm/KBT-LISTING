using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Quy tắc chọn billno tính tiền. Fixture dựng theo đúng các tình huống có thật trong DB.
/// Billno refunded/canceled (vd. GSU1RN24R00MBB7) không còn được tính — tiền đã hoàn về tài khoản.
///
/// Từ khi <c>SupplierPurchases</c> thay <c>OrderSupplierLinks</c>, kết quả của luật này là cờ
/// <c>IsCharged</c> chứ không phải mười một cột tổng trên <c>Orders</c> — nên các assert dưới đây
/// đọc tổng qua <see cref="OrderSupplierCost.SumCharged"/>, đúng cách mọi read path đang làm.
/// </summary>
public class OrderSupplierCostTests
{
    private static int _seq;

    private static SupplierPurchase Purchase(
        string billno,
        string? status,
        decimal? total,
        bool unpaid = false,
        string? relationBillno = null,
        string source = SupplierPurchaseSources.Shein,
        string matchState = SupplierPurchaseMatchStates.Matched)
        => new()
        {
            Id = Guid.NewGuid(),
            SupplierOrderNo = billno,
            RelationBillno = relationBillno,
            SupplierStatus = status,
            SupplierTotal = total,
            SupplierSubtotal = total,
            SupplierRetailPrice = total,
            SupplierUnpaid = unpaid,
            Source = source,
            MatchState = matchState,
            // SelectCharged xếp theo CreatedAt để biết lần checkout nào là gần nhất.
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_seq++)
        };

    private static Order OrderWith(params SupplierPurchase[] purchases)
        => new() { SupplierPurchases = purchases.ToList() };

    private static decimal? ChargedTotal(Order order)
        => OrderSupplierCost.SumCharged(order.SupplierPurchases!, p => p.SupplierTotal);

    [Theory]
    [InlineData("canceled", true)]
    [InlineData("cancelled", true)]
    [InlineData("Refunded", true)]
    [InlineData("shipped", false)]
    [InlineData("delivered", false)]
    [InlineData("processing", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsTerminalStatus_CoversBothCancelSpellings(string? status, bool expected)
    {
        Assert.Equal(expected, OrderSupplierCost.IsTerminalStatus(status));
    }

    [Fact]
    public void SelectCharged_ExcludesCanceledBillno_EvenWhenItIsTheOnlyPurchase()
    {
        // Tiền đã hoàn về tài khoản — giữ trong giá mua là trừ sổ cái oan (GSU1RN24R00MBB7).
        var purchases = new[] { Purchase("GSU18J247000TWB", "canceled", 15.80m) };

        var charged = OrderSupplierCost.SelectCharged(purchases);

        Assert.Empty(charged);
    }

    [Fact]
    public void SelectCharged_IgnoresCanceledBillno_WhenOrderWasRepurchased()
    {
        var purchases = new[]
        {
            Purchase("OLD-REFUNDED", "refunded", 12.00m),
            Purchase("NEW-SHIPPED", "shipped", 14.50m)
        };

        var charged = OrderSupplierCost.SelectCharged(purchases);

        Assert.Single(charged);
        Assert.Equal("NEW-SHIPPED", charged[0].SupplierOrderNo);
    }

    [Fact]
    public void ApplyAggregates_ClearsPrice_WhenEveryBillnoIsTerminal()
    {
        var order = OrderWith(Purchase("GSU18J247000TWB", "canceled", 15.80m));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Null(ChargedTotal(order));
        Assert.False(order.SupplierPurchases!.First().IsCharged);
    }

    [Fact]
    public void ApplyAggregates_ExcludesRefundedBillno_FromLedgerCost()
    {
        var order = OrderWith(
            Purchase("GSU1RN24R00MBB7", "refunded", 16.04m),
            Purchase("GSU1RN24R00MBRU", "refunded", 12.00m));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Null(ChargedTotal(order));
        Assert.All(order.SupplierPurchases!, p => Assert.False(p.IsCharged));
    }

    [Fact]
    public void ApplyAggregates_DoesNotDoubleCount_AfterRepurchase()
    {
        var order = OrderWith(
            Purchase("OLD-REFUNDED", "refunded", 12.00m),
            Purchase("NEW-SHIPPED", "shipped", 14.50m));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Equal(14.50m, ChargedTotal(order));
    }

    [Fact]
    public void ApplyAggregates_SumsJointShipmentBillnos()
    {
        var order = OrderWith(
            Purchase("PKG-1", "shipped", 10.00m),
            Purchase("PKG-2", "delivered", 6.25m));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Equal(16.25m, ChargedTotal(order));
    }

    [Fact]
    public void ApplyAggregates_ClearsPrice_WhenTerminalJointShipmentSiblings_AllRefunded()
    {
        // Hai billno Joint đều refunded/canceled: tiền đã về tài khoản — không tính sổ cái.
        var order = OrderWith(
            Purchase("PKG-1", "canceled", 10.00m, relationBillno: "USU18K2440000MPKY"),
            Purchase("PKG-2", "refunded", 6.25m, relationBillno: "USU18K2440000MPKY"));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Null(ChargedTotal(order));
        Assert.All(order.SupplierPurchases!, p => Assert.False(p.IsCharged));
    }

    [Fact]
    public void SelectCharged_ExcludesAllTerminalCheckouts_WhenEveryPurchaseWasTerminal()
    {
        // Mua rồi huỷ, mua lại rồi huỷ tiếp: mỗi lần đều đã hoàn tiền — không giữ lần cuối.
        var purchases = new[]
        {
            Purchase("FIRST-TRY", "canceled", 10.00m),
            Purchase("SECOND-TRY", "refunded", 11.50m)
        };

        Assert.Empty(OrderSupplierCost.SelectCharged(purchases));
    }

    [Fact]
    public void ApplyAggregates_KeepsManualMoney_WhenSyncJustLinkedBillnoWithoutMoney()
    {
        // Người đi đơn nhập phí tay, rồi sync gắn billno ở phase list (chưa gọi detail nên chưa có
        // tiền). Đây là ca từng làm tiền biến mất khỏi sổ cái: bản cũ nhân bản tổng sang
        // Orders.SupplierTotal và lượt sync sau xoá trắng nó. Giờ phí tay là một dòng riêng, sync
        // không chạm tới, nên nó vẫn là dòng duy nhất được tính tiền.
        var manual = Purchase(
            "GSU-MANUAL",
            null,
            16.04m,
            source: SupplierPurchaseSources.Manual);
        manual.SupplierOrderNo = null;
        var order = OrderWith(manual, Purchase("GSU-JUST-LINKED", "processing", null));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Equal(16.04m, ChargedTotal(order));
        Assert.True(manual.IsCharged);
        Assert.False(order.SupplierPurchases!.Single(p => p.SupplierOrderNo == "GSU-JUST-LINKED").IsCharged);
    }

    [Fact]
    public void SelectCharged_IgnoresMoneylessManualRow_SoSyncStillWins()
    {
        // Bấm Lưu chỉ để đổi tài khoản/thẻ tạo ra dòng nhập tay không mang đồng nào. Cho nó thắng
        // sẽ xoá sạch giá SHEIN đã đọc được.
        var manual = Purchase("X", null, null, source: SupplierPurchaseSources.Manual);
        manual.SupplierOrderNo = null;
        manual.SupplierSubtotal = null;
        manual.SupplierRetailPrice = null;
        var synced = Purchase("GSU-SYNCED", "shipped", 14.50m);

        var charged = OrderSupplierCost.SelectCharged(new[] { manual, synced });

        Assert.Single(charged);
        Assert.Equal("GSU-SYNCED", charged[0].SupplierOrderNo);
    }

    [Fact]
    public void SelectCharged_SkipsIgnoredRow()
    {
        // Người xử lý đã xem và kết luận billno này không thuộc đơn — nó phải ra khỏi giá mua dù
        // trạng thái SHEIN vẫn còn hiệu lực.
        var purchases = new[]
        {
            Purchase("GSU-IGNORED", "shipped", 30.00m, matchState: SupplierPurchaseMatchStates.Ignored),
            Purchase("GSU-REAL", "shipped", 14.50m)
        };

        var charged = OrderSupplierCost.SelectCharged(purchases);

        Assert.Single(charged);
        Assert.Equal("GSU-REAL", charged[0].SupplierOrderNo);
    }

    [Fact]
    public void SelectCharged_DropsUnpaidCanceledBillno_EvenWhenItIsTheOnlyOne()
    {
        // GSU18J247000TWB thật: pay_time = 0, SHEIN tự huỷ sau order_expire_time. Giữ lại nó là
        // bịa ra chi phí, vì không đồng nào rời tài khoản.
        var purchases = new[] { Purchase("GSU18J247000TWB", "canceled", 15.80m, unpaid: true) };

        Assert.Empty(OrderSupplierCost.SelectCharged(purchases));
    }

    [Fact]
    public void SelectCharged_IgnoresUnpaidBillno_WhenPaidSiblingExists()
    {
        var purchases = new[]
        {
            Purchase("GHOST-UNPAID", "canceled", 15.80m, unpaid: true),
            Purchase("REAL-SHIPPED", "shipped", 14.50m)
        };

        var charged = OrderSupplierCost.SelectCharged(purchases);

        Assert.Single(charged);
        Assert.Equal("REAL-SHIPPED", charged[0].SupplierOrderNo);
    }

    [Fact]
    public void SelectCharged_KeepsUnpaidBillno_OutOfCost_ButStillNotTerminalFallback()
    {
        // Đơn vừa tạo trên SHEIN, chưa trả tiền và chưa bị huỷ: chưa phải chi phí, nhưng cũng
        // không được biến thành khoản đã trả duy nhất qua nhánh fallback.
        var purchases = new[] { Purchase("GSU-AWAITING-PAY", "processing", 15.80m, unpaid: true) };

        Assert.Empty(OrderSupplierCost.SelectCharged(purchases));
    }

    [Fact]
    public void ApplyAggregates_ClearsPrice_WhenOnlyBillnoWasNeverPaid()
    {
        var ghost = Purchase("GSU18J247000TWB", "canceled", 15.80m, unpaid: true);
        ghost.IsCharged = true;
        var order = OrderWith(ghost);

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Null(ChargedTotal(order));
        Assert.False(ghost.IsCharged);
    }

    [Fact]
    public void ApplyAggregates_LeavesTotalNull_WhenBreakdownNotFetchedYet()
    {
        // Phase "list": billno đã gắn nhưng chưa gọi detail nên chưa có tiền. Không được biến
        // thành 0 vì 0 sẽ bị hiểu là "mua miễn phí" trên dashboard.
        var order = OrderWith(Purchase("GSU-PENDING", "processing", null));

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Null(ChargedTotal(order));
    }

    [Fact]
    public void PrimaryPurchase_PrefersChargedCarrierTracking()
    {
        // Hover preview và tìm kiếm hiện một mã đại diện; chọn dòng đã huỷ trong khi còn dòng có
        // mã carrier sẽ hiện mã không tra được ở đâu.
        var canceled = Purchase("GSU-CANCELED", "canceled", 9.00m);
        var shipped = Purchase("GSU-SHIPPED", "shipped", 14.50m);
        shipped.TrackingKind = SupplierTrackingKinds.Carrier;
        shipped.TrackingNo = "1Z999";
        var order = OrderWith(canceled, shipped);

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Equal("GSU-SHIPPED", OrderSupplierCost.PrimaryPurchase(order.SupplierPurchases!)?.SupplierOrderNo);
    }

    [Fact]
    public void PrimaryPurchase_FallsBackToUnchargedRow_WhenNothingIsCharged()
    {
        // Đơn chỉ còn billno ghost: tập tính tiền rỗng, nhưng người đi đơn vẫn cần mã để mở lại
        // trang SHEIN mà xử lý.
        var ghost = Purchase("GSU-GHOST", "canceled", 15.80m, unpaid: true);
        var order = OrderWith(ghost);

        OrderSupplierCost.ApplyAggregates(order);

        Assert.Equal("GSU-GHOST", OrderSupplierCost.PrimaryPurchase(order.SupplierPurchases!)?.SupplierOrderNo);
    }
}
