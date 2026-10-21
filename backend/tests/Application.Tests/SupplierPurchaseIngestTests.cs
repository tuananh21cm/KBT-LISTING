using Application.DTOs.Order;
using Application.Services;
using Application.Services.Shein;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Bước ghi nhận của luồng SHEIN.
///
/// Đây là chỗ khoá lại nguyên nhân "đã mua mà không có trong sổ cái": trước đây chỗ duy nhất trong
/// DB chứa được một billno là bảng con của <c>Orders</c>, nên không khớp đơn là không ghi gì cả —
/// 20/169 billno của lượt <c>ext-2608040342-be285208</c> biến mất theo đúng đường đó. Giờ mọi
/// billno vào <c>SupplierPurchases</c> <b>trước</b> khi chạy khớp, và các test dưới đây giữ hai bất
/// biến làm điều đó có nghĩa: upsert idempotent qua nhiều lượt/hai phase, và dòng chưa gắn được đơn
/// vẫn lưu đủ dữ kiện để người xử lý gắn tay.
/// </summary>
public class SupplierPurchaseIngestTests
{
    private static readonly DateTime Run1 = new(2026, 8, 4, 3, 42, 0, DateTimeKind.Utc);
    private static readonly DateTime Run2 = new(2026, 8, 4, 9, 10, 0, DateTimeKind.Utc);

    /// <summary>Payload phase <c>list</c>: có địa chỉ và tổng, chưa có breakdown.</summary>
    private static SheinOrderDto ListPayload(string billno = "GSU18F24400M9VC") => new()
    {
        Billno = billno,
        BuyerName = "Jane Doe",
        BuyerPhone = "(415) 555-0132",
        Address = "1 Market St",
        City = "San Francisco",
        State = "CA",
        PostalCode = "94105-1804",
        CountryCode = "US",
        TrackingNo = string.Empty,
        Carrier = string.Empty,
        Currency = "usd",
        Total = 16.04m,
        SheinStatus = "Processing",
        SheinStatusGroup = SupplierStatusGroups.Processing,
        OrderedAt = Run1.AddHours(-2),
        SheinPaidAt = Run1.AddHours(-2).AddMinutes(1),
        RawListItem = "{\"billno\":\"x\"}"
    };

    /// <summary>Payload phase <c>detail</c>: breakdown đủ tám thành phần.</summary>
    private static SheinOrderDto DetailPayload(string billno = "GSU18F24400M9VC")
    {
        var dto = ListPayload(billno);
        dto.RetailPrice = 15.59m;
        dto.Subtotal = 13.79m;
        dto.ShippingFee = 3.99m;
        dto.OnTimeGuarantee = 0m;
        dto.CouponPrice = 1.96m;
        dto.ClubDiscount = 0.69m;
        dto.PointsDiscount = 0m;
        dto.Tax = 0.91m;
        dto.Total = 16.04m;
        dto.TrackingNo = "9400111899223344556677";
        dto.Carrier = "USPS";
        dto.RawDetail = "{\"detail\":\"y\"}";
        return dto;
    }

    [Fact]
    public void Detail_WalletPaid_ClearsInflatedCouponFromPriorSync()
    {
        // Sync cũ đổ usedWalletPrice vào coupon; detail đúng: coupon=0, wallet=17.55, total=0.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var stale = DetailPayload("GSU18J2480004PR");
        stale.Subtotal = 18.47m;
        stale.RetailPrice = 25.99m;
        stale.ShippingFee = 0m;
        stale.CouponPrice = 17.55m;
        stale.ClubDiscount = 0.92m;
        stale.WalletPrice = 0m;
        stale.Tax = 0m;
        stale.Total = 0m;
        Ingest(map, stale, "detail", Run1);

        var fixedDto = DetailPayload("GSU18J2480004PR");
        fixedDto.Subtotal = 18.47m;
        fixedDto.RetailPrice = 25.99m;
        fixedDto.ShippingFee = 0m;
        fixedDto.CouponPrice = 0m;
        fixedDto.ClubDiscount = 0.92m;
        fixedDto.WalletPrice = 17.55m;
        fixedDto.Tax = 0m;
        fixedDto.Total = 0m;
        var purchase = Ingest(map, fixedDto, "detail", Run2);

        Assert.Equal(0m, purchase.SupplierCouponPrice);
        Assert.Equal(17.55m, purchase.SupplierWalletPrice);
        Assert.Equal(0m, purchase.SupplierTotal);
        Assert.Equal(0.92m, purchase.SupplierMembershipDiscount);
    }

    private static SupplierPurchase Ingest(
        Dictionary<string, SupplierPurchase> map,
        SheinOrderDto dto,
        string phase,
        DateTime now,
        string runId = "run-1")
        => SupplierPurchaseIngest.Upsert(map, dto, dto.Billno, phase, runId, now, out _);

    [Fact]
    public void Upsert_DoesNotDowngradeDeliveredStatus_WhenListOnlyHasOrderStatus10()
    {
        // List `status=5` (shipped) không được hạ `common_delivered` đã lấy từ detail.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var detail = DetailPayload();
        detail.SheinStatus = "common_delivered";
        detail.SheinStatusGroup = SupplierStatusGroups.Delivered;
        Ingest(map, detail, "detail", Run1);

        var list = ListPayload();
        list.SheinStatus = "5";
        list.SheinStatusGroup = SupplierStatusGroups.Shipped;
        var purchase = Ingest(map, list, "list", Run2);

        Assert.Equal(SupplierStatusGroups.Delivered, purchase.SupplierStatus);
        Assert.Equal("common_delivered", purchase.SupplierStatusRaw);
    }

    [Fact]
    public void Upsert_RecoversFalseRefunded_WhenListStatusIsShipped()
    {
        // GSU1RR21T00NNAA: chrome Return&Refund gắn refunded, rồi status=5 không hạ được rank.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var first = ListPayload("GSU1RR21T00NNAA");
        first.SheinStatus = "refunded";
        first.SheinStatusGroup = SupplierStatusGroups.Refunded;
        Ingest(map, first, "list", Run1);

        var live = ListPayload("GSU1RR21T00NNAA");
        live.SheinStatus = "5";
        live.SheinStatusGroup = SupplierStatusGroups.Refunded;
        live.TrackingNo = "GFUS01068114822657";
        live.Carrier = "GOFO";
        live.Total = 15.74m;
        var purchase = Ingest(map, live, "list", Run2);

        Assert.Equal(SupplierStatusGroups.Shipped, purchase.SupplierStatus);
        Assert.Equal("5", purchase.SupplierStatusRaw);
    }

    [Fact]
    public void Upsert_RecordsBillnoEvenWithoutAnyOrder()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);

        var purchase = Ingest(map, DetailPayload(), "detail", Run1);

        // Ghi nhận tách hẳn khỏi khớp: dòng tồn tại đầy đủ dù chưa thuộc đơn nào.
        Assert.Null(purchase.OrderId);
        Assert.Equal(SupplierPurchaseMatchStates.Unmatched, purchase.MatchState);
        Assert.Equal(SupplierPurchaseSources.Shein, purchase.Source);
        Assert.Equal("GSU18F24400M9VC", purchase.SupplierOrderNo);
        Assert.Equal(16.04m, purchase.SupplierTotal);
        Assert.Equal(13.79m, purchase.SupplierSubtotal);
        Assert.Equal("Jane Doe", purchase.ReceiverName);
        Assert.Equal("(415) 555-0132", purchase.ReceiverPhone);
        Assert.Equal("94105-1804", purchase.ReceiverPostalCode);
        Assert.Equal("USD", purchase.Currency);
        Assert.Equal("9400111899223344556677", purchase.TrackingNo);
        Assert.Equal(SupplierTrackingKinds.Carrier, purchase.TrackingKind);
        Assert.NotNull(purchase.SupplierPaidAt);
        Assert.False(purchase.SupplierUnpaid);
    }

    [Fact]
    public void Upsert_StoresNormalizedMatchKeys_SoManualQueueCanSearchByPhoneAndZip()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);

        var purchase = Ingest(map, ListPayload(), "list", Run1);

        // Khoá chuẩn hoá được ghi ngay lúc nhận: hàng đợi gắn tay lọc theo chúng, và tính lại từ
        // chuỗi thô mỗi lần đọc thì "(415) 555-0132" với "4155550132" là hai đơn khác nhau.
        Assert.Equal(SheinMatchKeys.PhoneKey("(415) 555-0132"), purchase.PhoneKey);
        Assert.Equal(SheinMatchKeys.ZipKey("94105-1804"), purchase.ZipKey);
        Assert.Equal(SheinMatchKeys.NameKey("Jane Doe"), purchase.NameKey);
    }

    [Fact]
    public void Upsert_DetailPhase_FullReplacesWhenSubtotalEqualsTotal()
    {
        // Đơn free ship / không giảm: subtotal ≈ total, không retail/coupon/tax.
        // Trước đây IsRichCostBreakdown đòi |sub−total| > 0.02 → không full-replace → thiếu Giá nhập.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        Ingest(map, ListPayload("GSUFLATSUB"), "list", Run1);

        var detail = ListPayload("GSUFLATSUB");
        detail.Subtotal = 16.04m;
        detail.ShippingFee = 0m;
        detail.Total = 16.04m;
        var purchase = Ingest(map, detail, "detail", Run2);

        Assert.Equal(16.04m, purchase.SupplierSubtotal);
        Assert.Equal(0m, purchase.SupplierShippingFee);
        Assert.Equal(16.04m, purchase.SupplierTotal);
    }

    [Fact]
    public void Upsert_IsIdempotentAcrossRuns()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);

        var first = SupplierPurchaseIngest.Upsert(
            map, DetailPayload(), "GSU18F24400M9VC", "detail", "run-1", Run1, out var createdFirst);
        var second = SupplierPurchaseIngest.Upsert(
            map, DetailPayload(), "GSU18F24400M9VC", "detail", "run-2", Run2, out var createdSecond);

        Assert.True(createdFirst);
        Assert.False(createdSecond);
        Assert.Same(first, second);
        Assert.Single(map);

        // Chỉ dấu vết lượt chạy được đổi; mọi dữ kiện phía NCC giữ nguyên.
        Assert.Equal(Run1, second.FirstSeenAt);
        Assert.Equal(Run2, second.LastSeenAt);
        Assert.Equal("run-2", second.LastRunId);
        Assert.Equal(16.04m, second.SupplierTotal);
        Assert.Equal(3.99m, second.SupplierShippingFee);
        Assert.Equal(0.91m, second.SupplierTax);

        // Payload y nguyên ⇒ mốc "dữ liệu đổi lần cuối" không được đẩy lên, bằng không mọi lượt
        // sync đều báo dòng vừa thay đổi.
        Assert.Equal(Run1, second.UpdatedAt);
    }

    /// <summary>
    /// Nguồn của 46 MB index trên bảng heap 1,6 MB: SHEIN trả lại toàn bộ billno trong khoảng
    /// lookback ở mỗi lượt (30–60 s/lượt), và bước ghi nhận từng stamp <c>LastSeenAt</c> +
    /// <c>LastRunId</c> + <c>UpdatedAt</c> vô điều kiện. Mỗi lượt như vậy là một UPDATE không có
    /// field nghiệp vụ nào đổi, và vì bảng có 13 index thì UPDATE không HOT được ghi lại cả 13.
    ///
    /// Hai test dưới đây chốt lại: trong cửa sổ gộp, lượt sync thấy lại dòng y nguyên **không chạm
    /// field nào**; ngoài cửa sổ thì mốc mới được làm mới.
    /// </summary>
    [Fact]
    public void Upsert_LeavesUnchangedRowUntouched_WithinCoalesceWindow()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var first = SupplierPurchaseIngest.Upsert(
            map, DetailPayload(), "GSU18F24400M9VC", "detail", "run-1", Run1, out _);

        var nextTick = Run1.AddMinutes(1);
        var second = SupplierPurchaseIngest.Upsert(
            map, DetailPayload(), "GSU18F24400M9VC", "detail", "run-2", nextTick, out _);

        Assert.Same(first, second);
        Assert.Equal(Run1, second.LastSeenAt);
        Assert.Equal("run-1", second.LastRunId);
        Assert.Equal(Run1, second.UpdatedAt);
    }

    [Fact]
    public void Upsert_StampsChangedRowImmediately_EvenInsideCoalesceWindow()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        SupplierPurchaseIngest.Upsert(
            map, DetailPayload(), "GSU18F24400M9VC", "detail", "run-1", Run1, out _);

        // Mã vận đơn về là dữ kiện thật: dòng phải được ghi ngay, không chờ hết cửa sổ gộp.
        var withTracking = DetailPayload();
        withTracking.TrackingNo = "1Z999AA10123456784";
        withTracking.Carrier = "UPS";

        var nextTick = Run1.AddMinutes(1);
        var second = SupplierPurchaseIngest.Upsert(
            map, withTracking, "GSU18F24400M9VC", "detail", "run-2", nextTick, out _);

        Assert.Equal("1Z999AA10123456784", second.TrackingNo);
        Assert.Equal(nextTick, second.LastSeenAt);
        Assert.Equal("run-2", second.LastRunId);
        Assert.Equal(nextTick, second.UpdatedAt);
    }

    [Fact]
    public void Upsert_ListPhaseDoesNotWipeBreakdownFetchedByDetail()
    {
        // Ca hồi quy đắt nhất: mỗi lượt Auto Sync chạy phase list trước. Ghi đè thẳng ở đó sẽ xoá
        // sạch breakdown mà detail vừa lấy, và giá mua của đơn tụt về chỉ còn tổng.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        Ingest(map, DetailPayload(), "detail", Run1);

        var purchase = Ingest(map, ListPayload(), "list", Run2);

        Assert.Equal(15.59m, purchase.SupplierRetailPrice);
        Assert.Equal(13.79m, purchase.SupplierSubtotal);
        Assert.Equal(3.99m, purchase.SupplierShippingFee);
        Assert.Equal(1.96m, purchase.SupplierCouponPrice);
        Assert.Equal(0.69m, purchase.SupplierMembershipDiscount);
        Assert.Equal(0.91m, purchase.SupplierTax);
        Assert.Equal(16.04m, purchase.SupplierTotal);
    }

    [Fact]
    public void Upsert_ListPhaseDoesNotWipeTrackingOrRawPayloadFromDetail()
    {
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        Ingest(map, DetailPayload(), "detail", Run1);

        var purchase = Ingest(map, ListPayload(), "list", Run2);

        Assert.Equal("9400111899223344556677", purchase.TrackingNo);
        Assert.Equal("USPS", purchase.Carrier);
        // Hai phase gửi hai mảnh JSON khác nhau nên vỏ RawPayload phải gộp, không ghi đè.
        Assert.Contains("detail", purchase.RawPayload);
        Assert.Contains("billno", purchase.RawPayload);
    }

    [Fact]
    public void Upsert_DoesNotUnsetPaidClock_WhenLaterPayloadReportsUnpaid()
    {
        // Mốc trừ tiền đã lưu là bằng chứng một chiều. Một payload rút gọn báo pay_time = 0 không
        // được phép biến một lần mua thật thành đơn ghost — ghost bị loại khỏi giá mua, nên chiều
        // ngược lại là mất tiền khỏi sổ cái.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        Ingest(map, DetailPayload(), "detail", Run1);

        var stale = ListPayload();
        stale.SheinPaidAt = null;
        stale.SheinUnpaid = true;
        var purchase = Ingest(map, stale, "list", Run2);

        Assert.NotNull(purchase.SupplierPaidAt);
        Assert.False(purchase.SupplierUnpaid);
    }

    [Fact]
    public void Upsert_DoesNotBackdateStatusClockToSyncTime()
    {
        // Bản cũ lùi SupplierStatusAt về giờ chạy sync khi payload không kèm clock, mà mọi lượt sync
        // đều đi qua đây — clock thật bị ghi đè ngay lượt kế tiếp và không lấy lại được.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var withClock = DetailPayload();
        withClock.SheinStatusAt = Run1.AddHours(-1);
        Ingest(map, withClock, "detail", Run1);

        var withoutClock = ListPayload();
        withoutClock.SheinStatusAt = null;
        var purchase = Ingest(map, withoutClock, "list", Run2);

        Assert.Equal(Run1.AddHours(-1), purchase.SupplierStatusAt);
    }

    [Fact]
    public void Upsert_KeepsMembershipFlagWhenLaterPayloadIsSilentAboutOrderType()
    {
        // Payload list không có order_type nên phase list không kết luận được "không phải Club".
        // Gỡ cờ vì thiếu thông tin làm mỗi vòng gắn billno Club vào một đơn TikTok rồi phase detail
        // gỡ ra — ghi rồi xoá một dòng sổ cái ở **mọi** lượt sync.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var club = DetailPayload("GSU18B24Y002XP1");
        club.OrderType = 5;
        Ingest(map, club, "detail", Run1);

        var listAgain = ListPayload("GSU18B24Y002XP1");
        listAgain.OrderType = null;
        var purchase = Ingest(map, listAgain, "list", Run2);

        Assert.True(purchase.IsMembershipOrVirtual);
    }

    [Fact]
    public void Upsert_ClearsMembershipFlag_WhenPayloadStatesANonMembershipOrderType()
    {
        // Mặt còn lại của cờ dính, và là ca đắt tiền: GSU18V24X001CSQ + GSU18G24X001XC3 bị extension
        // gắn cờ ảo vì goods row có `virtual_inventory_flag=1`, trong khi order_type=1 và cả hai đã
        // giao kèm mã vận đơn. Dính vô điều kiện thì một lần nhận diện sai là vĩnh viễn: nhánh hàng
        // ảo gỡ dòng khỏi đơn sàn nên hai lần mua thật rơi khỏi giá mua ở mọi lượt sync.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var mislabeled = ListPayload("GSU18V24X001CSQ");
        mislabeled.IsMembershipOrVirtual = true;
        mislabeled.OrderType = null;
        var purchase = Ingest(map, mislabeled, "list", Run1);
        Assert.True(purchase.IsMembershipOrVirtual);

        var withOrderType = DetailPayload("GSU18V24X001CSQ");
        withOrderType.IsMembershipOrVirtual = null;
        withOrderType.OrderType = 1;
        purchase = Ingest(map, withOrderType, "detail", Run2);

        Assert.False(purchase.IsMembershipOrVirtual);
        Assert.Equal(1, purchase.SupplierOrderType);
    }

    [Fact]
    public void Upsert_KeepsMembershipFlag_WhenOrderTypeContradictsButProductIsVirtual()
    {
        // order_type không phải 5/6 chỉ gỡ được cờ khi **không** còn dấu hiệu nào khác. Gói Saver
        // bán qua goods_id 500000433 vẫn phải bị loại kể cả khi order_type nói khác.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var saver = DetailPayload("GSU18SAVER00001");
        saver.OrderType = 1;
        saver.ProductIds = ["500000433"];

        var purchase = Ingest(map, saver, "detail", Run1);

        Assert.True(purchase.IsMembershipOrVirtual);
    }

    [Theory]
    // Quarterly membership, SHEIN Saver / xtra virtual service.
    [InlineData(5, null, true)]
    [InlineData(6, null, true)]
    // goods_id catalog hàng ảo — đúng 9 chữ số bắt đầu 50000.
    [InlineData(null, "500000433", true)]
    [InlineData(null, "500000983", true)]
    [InlineData(null, "VPP-CLUB-1", true)]
    [InlineData(null, "SQN-SAVER-1", true)]
    // Hàng thật: nới điều kiện thành "bắt đầu 50000" sẽ khớp cả hai mã dưới đây và loại oan một
    // lần mua thật khỏi giá mua.
    [InlineData(null, "500001", false)]
    [InlineData(null, "500009876543", false)]
    [InlineData(null, "1234567890", false)]
    [InlineData(3, null, false)]
    public void IsMembershipOrVirtual_MatchesOnlyRealVirtualSignals(
        int? orderType,
        string? productId,
        bool expected)
    {
        var dto = ListPayload();
        dto.OrderType = orderType;
        dto.ProductIds = productId is null ? [] : [productId];

        Assert.Equal(expected, SupplierPurchaseIngest.IsMembershipOrVirtual(dto));
    }

    [Fact]
    public void MarkUnmatched_KeepsEveryFieldAndAddsEvidence()
    {
        // Đúng nhóm no_local_match của lượt thật: 14 billno không đơn nào trong pool khớp
        // phone + zip. Chúng phải còn nguyên tiền, người nhận và mã vận đơn để gắn tay được.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);

        SupplierPurchaseIngest.MarkUnmatched(
            purchase,
            SheinSkipReasons.NoLocalMatch,
            "{\"poolSize\":473,\"phoneMatches\":0,\"zipMatches\":2}",
            Run1);

        Assert.Equal(SupplierPurchaseMatchStates.Unmatched, purchase.MatchState);
        Assert.Equal(SheinSkipReasons.NoLocalMatch, purchase.SkipReason);
        Assert.Contains("poolSize", purchase.SkipEvidenceJson);
        Assert.False(purchase.IsCharged);
        Assert.Equal(16.04m, purchase.SupplierTotal);
        Assert.Equal("Jane Doe", purchase.ReceiverName);
        Assert.Equal("9400111899223344556677", purchase.TrackingNo);
    }

    [Fact]
    public void MarkUnmatched_DoesNotTouchAlreadyAttachedRow()
    {
        // Bảo hiểm thứ tự: một nhánh bỏ qua chạy sau khi dòng đã được gắn không được gỡ nó ra.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);
        purchase.OrderId = Guid.NewGuid();
        purchase.MatchState = SupplierPurchaseMatchStates.Matched;
        purchase.IsCharged = true;

        SupplierPurchaseIngest.MarkUnmatched(purchase, SheinSkipReasons.NoLocalMatch, null, Run1);

        Assert.Equal(SupplierPurchaseMatchStates.Matched, purchase.MatchState);
        Assert.True(purchase.IsCharged);
        Assert.Null(purchase.SkipReason);
    }

    [Fact]
    public void MarkUnmatched_KeepsIgnoredDecision()
    {
        // Người xử lý đã xem và bỏ qua; một lượt sync sau đó không được kéo dòng về hàng đợi.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);
        purchase.MatchState = SupplierPurchaseMatchStates.Ignored;

        SupplierPurchaseIngest.MarkUnmatched(purchase, SheinSkipReasons.NoLocalMatch, null, Run2);

        Assert.Equal(SupplierPurchaseMatchStates.Ignored, purchase.MatchState);
    }

    [Fact]
    public void Detach_KeepsEvidence_InsteadOfDeletingThePurchase()
    {
        // Một lần mua đã xảy ra thì bằng chứng phải còn lại. Bản cũ xoá hẳn dòng khi gỡ khỏi đơn,
        // nên mọi thứ đã biết về billno đó mất theo.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);
        var order = new Order { Id = Guid.NewGuid(), SupplierPurchases = [] };
        purchase.OrderId = order.Id;
        purchase.MatchState = SupplierPurchaseMatchStates.Matched;
        purchase.IsCharged = true;
        order.SupplierPurchases.Add(purchase);

        SupplierPurchaseIngest.Detach(purchase, order, SheinSkipReasons.ManualDetach, Run2);

        Assert.Empty(order.SupplierPurchases);
        Assert.Null(purchase.OrderId);
        Assert.Equal(SupplierPurchaseMatchStates.Unmatched, purchase.MatchState);
        Assert.Equal(SheinSkipReasons.ManualDetach, purchase.SkipReason);
        Assert.False(purchase.IsCharged);
        Assert.Equal(16.04m, purchase.SupplierTotal);
        Assert.Equal("GSU18F24400M9VC", purchase.SupplierOrderNo);
    }

    [Fact]
    public void Detach_MarkIgnored_MakesTheDecisionSurviveLaterSyncRuns()
    {
        // Quyết định của người xử lý (Bỏ qua) phải bền qua lượt sync: payload vẫn làm tươi tiền, nhưng
        // MatchState ignored không bị kéo về hàng đợi khớp. (Xoá sổ cái giờ xóa hẳn lần mua — không
        // còn đi qua nhánh markIgnored này.)
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);
        var order = new Order { Id = Guid.NewGuid(), SupplierPurchases = [] };
        purchase.OrderId = order.Id;
        purchase.MatchState = SupplierPurchaseMatchStates.Matched;
        purchase.IsCharged = true;
        order.SupplierPurchases.Add(purchase);

        SupplierPurchaseIngest.Detach(
            purchase, order, SheinSkipReasons.OperatorIgnored, Run2, markIgnored: true);

        Assert.Equal(SupplierPurchaseMatchStates.Ignored, purchase.MatchState);
        Assert.Equal(SheinSkipReasons.OperatorIgnored, purchase.SkipReason);
        Assert.False(purchase.IsCharged);
        Assert.Null(purchase.OrderId);

        Ingest(map, DetailPayload(), "detail", Run2);
        Assert.Equal(SupplierPurchaseMatchStates.Ignored, purchase.MatchState);
        Assert.Equal(16.04m, purchase.SupplierTotal);
    }

    [Fact]
    public void Detach_WithoutMarkIgnored_LeavesRowOpenForRematch()
    {
        // Kết luận của **máy** (Club / ghost / ngược nhân quả) phải sửa được bằng payload mới, nên
        // nhánh mặc định vẫn để dòng ở hàng đợi chờ khớp.
        var map = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        var purchase = Ingest(map, DetailPayload(), "detail", Run1);
        var order = new Order { Id = Guid.NewGuid(), SupplierPurchases = [] };
        purchase.OrderId = order.Id;
        purchase.MatchState = SupplierPurchaseMatchStates.Matched;
        order.SupplierPurchases.Add(purchase);

        SupplierPurchaseIngest.Detach(purchase, order, SheinSkipReasons.MembershipVirtual, Run2);

        Assert.Equal(SupplierPurchaseMatchStates.Unmatched, purchase.MatchState);
    }

    [Fact]
    public void SameBatchTwice_ProducesIdenticalState()
    {
        // "Chạy lại Sync tất cả" phải là phép toán luỹ đẳng. Bất kỳ field nào còn dao động giữa hai
        // lượt là một dòng sổ cái bị ghi rồi xoá ở mỗi vòng.
        var payloads = new[]
        {
            DetailPayload("GSU18F24400M9VC"),
            DetailPayload("GSU18F24Q00CU1N"),
            ListPayload("GSU18F24F0025M2")
        };

        var first = new Dictionary<string, SupplierPurchase>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in payloads) Ingest(first, dto, dto.RetailPrice.HasValue ? "detail" : "list", Run1);
        var snapshot = first.ToDictionary(
            kv => kv.Key,
            kv => Fingerprint(kv.Value),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dto in payloads) Ingest(first, dto, dto.RetailPrice.HasValue ? "detail" : "list", Run2);

        Assert.Equal(3, first.Count);
        foreach (var (billno, before) in snapshot)
        {
            Assert.Equal(before, Fingerprint(first[billno]));
        }
    }

    /// <summary>Mọi field quyết định tiền/khớp, trừ dấu vết lượt chạy.</summary>
    private static string Fingerprint(SupplierPurchase p) => string.Join(
        '|',
        p.SupplierOrderNo,
        p.MatchState,
        p.Source,
        p.SupplierTotal,
        p.SupplierSubtotal,
        p.SupplierRetailPrice,
        p.SupplierShippingFee,
        p.SupplierRetailDeliveryFee,
        p.SupplierCouponPrice,
        p.SupplierMembershipDiscount,
        p.SupplierPointsDiscount,
        p.SupplierWalletPrice,
        p.SupplierTax,
        p.SupplierStatus,
        p.SupplierStatusAt?.ToString("O"),
        p.SupplierPaidAt?.ToString("O"),
        p.OrderedAt?.ToString("O"),
        p.SupplierUnpaid,
        p.IsMembershipOrVirtual,
        p.TrackingNo,
        p.TrackingKind,
        p.PhoneKey,
        p.ZipKey,
        p.NameKey,
        p.IsCharged);
}
