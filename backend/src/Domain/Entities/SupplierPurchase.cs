using Domain.Common;
using Shared.Constants;

namespace Domain.Entities;

/// <summary>
/// Một dòng = một lần mua hàng ở nhà cung cấp (SHEIN/Temu/…) hoặc một khoản phí nhập tay.
///
/// Bảng này là <b>chỗ duy nhất</b> chứa thông tin mua hàng NCC. Trước đây cùng một con số tồn tại ở
/// hai nơi: bảng con <c>OrderSupplierLinks</c> và mười một cột <c>Supplier*</c> lặp lại trên
/// <c>Orders</c>, giữ đồng bộ bằng tay qua <c>OrderSupplierCost.ApplyAggregates</c> — mỗi lần hai bên
/// lệch là một lần con số hiển thị sai.
///
/// Ba điểm khiến một bảng đủ cho mọi trường hợp:
/// <list type="bullet">
/// <item><see cref="OrderId"/> nullable — đơn NCC chưa khớp được đơn sàn vẫn có chỗ sống, kèm
/// <see cref="MatchState"/> + <see cref="SkipReason"/> + <see cref="SkipEvidenceJson"/>. Đây chính là
/// thứ đã thiếu khi 20/169 billno bị bỏ mà không để lại dấu vết nào ngoài nhật ký tự xoá sau 7
/// ngày.</item>
/// <item><see cref="SupplierOrderNo"/> nullable + unique index có điều kiện — nút "Cập nhật phí" sinh
/// một dòng <see cref="SupplierPurchaseSources.Manual"/> không mã NCC, dùng chung đường ghi với
/// sync.</item>
/// <item><see cref="IsCharged"/> lưu sẵn — luật "billno nào được tính tiền" của
/// <c>OrderSupplierCost.SelectCharged</c> trước đây chỉ chạy được trong bộ nhớ C#; thành một cột
/// boolean thì mọi phép tổng hợp trở lại là SQL thuần.</item>
/// </list>
/// </summary>
public class SupplierPurchase : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Đơn sàn đã gắn, <c>null</c> khi lần mua này chưa khớp được đơn nào (hàng đợi gắn tay).
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Mã đơn NCC (SHEIN billno). Unique toàn DB nhưng <c>null</c> được phép: dòng nhập tay không có
    /// mã nào cả.
    /// </summary>
    public string? SupplierOrderNo { get; set; }

    /// <summary>Nguồn dòng này: <see cref="SupplierPurchaseSources"/>.</summary>
    public string Source { get; set; } = SupplierPurchaseSources.Shein;

    /// <summary>Trạng thái khớp: <see cref="SupplierPurchaseMatchStates"/>.</summary>
    public string MatchState { get; set; } = SupplierPurchaseMatchStates.Unmatched;

    /// <summary>
    /// Mã lý do chưa gắn được (<c>Shared.Constants.SheinSkipReasons</c>), <c>null</c> khi đã gắn.
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// JSON bằng chứng near-miss cho <see cref="SkipReason"/>: pool có bao nhiêu đơn trùng phone,
    /// trùng zip, trùng cả hai nhưng bị loại vì lý do gì. Không có nó thì người xử lý chỉ thấy
    /// "không khớp đơn nào" mà không biết phải sửa gì.
    /// </summary>
    public string? SkipEvidenceJson { get; set; }

    /// <summary>
    /// <c>true</c> khi dòng này được cộng vào giá mua của đơn. Tính lúc ghi theo đúng luật
    /// <c>OrderSupplierCost.SelectCharged</c>: loại billno chưa thanh toán, ưu tiên billno còn hiệu
    /// lực, hết thì chỉ giữ lần checkout gần nhất.
    /// </summary>
    public bool IsCharged { get; set; }

    /// <summary>
    /// SHEIN <c>relation_billno</c> (e.g. USU18K244000000MPKY). Shared across Joint Shipment
    /// sibling checkouts from different shops; primary group key for multi-link attach.
    /// </summary>
    public string? RelationBillno { get; set; }

    /// <summary>
    /// SHEIN warehouse confluence batch (<c>order_package_info_list[].confluence_batch_no</c>),
    /// e.g. AU6072210161700478600946L — backup group key when relation is missing.
    /// </summary>
    public string? ConfluenceBatchNo { get; set; }

    /// <summary>Supplier order detail URL.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary>Tracking / shipping number for this purchase (may be warehouse or carrier).</summary>
    public string? TrackingNo { get; set; }

    /// <summary>
    /// Last SHEIN warehouse tracking seen (C…/N…). Kept when <see cref="TrackingNo"/> upgrades to
    /// last-mile carrier for audit / badge history (Thank Card pending chỉ khi chưa có carrier).
    /// </summary>
    public string? WarehouseTrackingNo { get; set; }

    /// <summary>
    /// UTC when a warehouse tracking code was first recorded for this purchase
    /// (not updated when the warehouse number itself later changes).
    /// </summary>
    public DateTime? WarehouseTrackingSeenAt { get; set; }

    /// <summary>Carrier / track_info name from supplier.</summary>
    public string? Carrier { get; set; }

    /// <summary>
    /// <c>none</c> | <c>warehouse</c> (SHEIN internal C… / International Warehouse) |
    /// <c>carrier</c> (UPS/USPS/… last-mile).
    /// </summary>
    public string TrackingKind { get; set; } = SupplierTrackingKinds.None;

    /// <summary>Supplier retail / list price for this purchase.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Supplier subtotal (giá nhập).</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Supplier shipping fee.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee (<c>retail_delivery_fee.amount</c>), separate from shipping.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>On-time guarantee fee.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon discount.</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership / club discount.</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed (SHEIN Points).</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used (<c>usedWalletPrice.amount</c>) — payment, not a coupon.</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Sales tax.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Grand total charged for this purchase.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>
    /// Tiền tệ NCC thu, mặc định <c>USD</c>. Trước đây normalizer đọc <c>currency_code</c> rồi vứt
    /// đi vì không có cột nào lưu, nên một đơn EUR bị cộng như USD mà không để lại dấu vết.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>SHEIN order place time (UTC).</summary>
    public DateTime? OrderedAt { get; set; }

    /// <summary>
    /// Mốc SHEIN trừ tiền thật cho lần mua này (UTC), từ <c>pay_time</c> / <c>paymentTime</c>.
    /// Khác <see cref="OrderedAt"/>: SHEIN cho tạo đơn trước rồi mới thu tiền.
    /// </summary>
    public DateTime? SupplierPaidAt { get; set; }

    /// <summary>
    /// <c>true</c> chỉ khi payload SHEIN nói rõ <c>pay_time</c> / <c>paymentTime</c> = 0 — đơn được
    /// tạo nhưng chưa bao giờ trả tiền, nên SHEIN tự huỷ khi hết <c>order_expire_time</c> (mặc định
    /// 4 giờ). Billno như vậy không có gói hàng và không có đồng nào rời tài khoản, nên không được
    /// tính vào giá mua lẫn mốc "Mua".
    ///
    /// Mặc định <c>false</c> nghĩa là "chưa quan sát được", không phải "đã thanh toán": extension
    /// bản cũ không gửi trường này nên mọi hàng sẵn có giữ đúng hành vi trước đây.
    /// </summary>
    public bool SupplierUnpaid { get; set; }

    /// <summary>
    /// <c>true</c> khi lần mua này được nhận là gói SHEIN Club / Saver / hàng ảo.
    ///
    /// Cờ dính với **sự vắng mặt** của bằng chứng: payload <c>order/list</c> không có
    /// <c>order_type</c> nên nếu phase list được phép gỡ cờ thì nó gắn billno Club vào một đơn sàn,
    /// rồi phase detail (có <c>order_type</c>) gỡ ra — lặp lại mỗi lượt sync, mỗi vòng ghi rồi xoá
    /// một dòng sổ cái. Ngược lại, payload **có** <c>order_type</c> và giá trị đó không phải 5/6 là
    /// phát biểu tường minh "không phải gói thành viên" và nó gỡ được cờ: một lần nhận diện sai mà
    /// không sửa được là vĩnh viễn loại một lần mua thật khỏi giá mua.
    /// </summary>
    public bool IsMembershipOrVirtual { get; set; }

    /// <summary>SHEIN <c>order_type</c> thô (5 = Club/Prime, 6 = Saver), <c>null</c> khi chưa biết.</summary>
    public int? SupplierOrderType { get; set; }

    /// <summary>
    /// Purchase account that paid for THIS purchase, resolved from the SHEIN billing email.
    /// Sibling billnos of the same local order may be bought from different accounts, so the
    /// order-level field cannot represent it.
    /// </summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Card charged for this purchase (account's active card when not explicit).</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Order handler credited with this purchase.</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>
    /// JSON array of product id / sku keys matched from this SHEIN order
    /// (used to compute uncovered local line items).
    /// </summary>
    public string? ProductIdsJson { get; set; }

    /// <summary>
    /// Normalized SHEIN lifecycle: <c>processing</c> | <c>shipped</c> | <c>delivered</c> |
    /// <c>canceled</c> | <c>refunded</c>.
    /// </summary>
    public string? SupplierStatus { get; set; }

    /// <summary>Raw SHEIN status text/code from list/detail.</summary>
    public string? SupplierStatusRaw { get; set; }

    /// <summary>Clock thật của lần đổi trạng thái NCC gần nhất (UTC), không phải giờ chạy sync.</summary>
    public DateTime? SupplierStatusAt { get; set; }

    /// <summary>Tên người nhận trên đơn NCC — dùng để đối chiếu và hiển thị hàng đợi gắn tay.</summary>
    public string? ReceiverName { get; set; }

    /// <summary>Số điện thoại người nhận trên đơn NCC (Shipping, không phải Billing).</summary>
    public string? ReceiverPhone { get; set; }

    /// <summary>Mã bưu chính người nhận trên đơn NCC.</summary>
    public string? ReceiverPostalCode { get; set; }

    /// <summary>Phone đã chuẩn hoá qua <c>SheinMatchKeys</c> — khoá khớp và khoá tìm kiếm.</summary>
    public string? PhoneKey { get; set; }

    /// <summary>Zip đã chuẩn hoá qua <c>SheinMatchKeys</c>.</summary>
    public string? ZipKey { get; set; }

    /// <summary>Tên người nhận đã chuẩn hoá qua <c>SheinMatchKeys</c>.</summary>
    public string? NameKey { get; set; }

    /// <summary>Lần đầu ghi nhận lần mua này (UTC).</summary>
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Lần gần nhất thấy lại lần mua này trong một lượt sync (UTC).</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mã lượt sync gần nhất đã chạm dòng này (<c>SheinSyncRunLog.RunId</c>).</summary>
    public string? LastRunId { get; set; }

    /// <summary>Thời điểm gắn được vào đơn sàn (UTC), <c>null</c> khi chưa gắn.</summary>
    public DateTime? MatchedAt { get; set; }

    /// <summary>Người gắn tay, <c>null</c> khi do sync tự gắn.</summary>
    public Guid? MatchedByUserId { get; set; }

    /// <summary>
    /// Mốc người xử lý tự tay chọn đơn cho lần mua này (UTC), <c>null</c> khi do máy khớp.
    ///
    /// Là **khoá**, không chỉ là dấu vết: luồng sync không được tự chuyển lần mua đã có mốc này
    /// sang đơn khác (khớp lại chế độ Cập nhật tất cả, cân bằng đơn vượt trần). Máy khớp bằng
    /// phone+zip+tên+SKU nên nó không thể biết điều người xử lý đã tra ra; ghi đè quyết định đó
    /// là cách một lần sửa tay bị mất âm thầm sau lượt sync kế tiếp.
    /// Gỡ khỏi đơn xoá mốc này — dòng quay về hàng đợi thì không còn quyết định nào để giữ.
    /// </summary>
    public DateTime? ManualLinkedAt { get; set; }

    /// <summary>Carrier tracking number for which automatic 4Seller Mark Ship was evaluated.</summary>
    public string? AutoMarkShipTrackingNo { get; set; }

    /// <summary><c>pending</c> | <c>succeeded</c> | <c>failed</c> | <c>blocked</c> | <c>unconfirmed</c>.</summary>
    public string? AutoMarkShipStatus { get; set; }

    /// <summary>Last automatic Mark Ship attempt time (UTC).</summary>
    public DateTime? AutoMarkShipAttemptedAt { get; set; }

    /// <summary>Last automatic Mark Ship error or blocked reason.</summary>
    public string? AutoMarkShipError { get; set; }

    /// <summary>
    /// 4Seller async Mark Ship <c>requestId</c> — lưu ngay khi gửi để reconcile sau poll timeout,
    /// tránh đẩy lại cùng mã khi chưa biết sàn đã nhận hay chưa.
    /// </summary>
    public string? AutoMarkShipRequestId { get; set; }

    /// <summary>
    /// JSON gốc SHEIN trả về cho lần mua này (list item và/hoặc get_order_info), chỉ ghi khi bật
    /// <c>extension.shein_sync.capture_raw_payload</c>. Kiểu <c>text</c> chứ không phải
    /// <c>jsonb</c> vì extension cắt payload quá dài, bản cắt không còn parse được.
    /// </summary>
    public string? RawPayload { get; set; }

    /// <summary>Thời điểm ghi <see cref="RawPayload"/> (UTC), để biết bản gốc cũ tới mức nào.</summary>
    public DateTime? RawPayloadAt { get; set; }

    /// <summary>Đơn sàn đã gắn (nếu có).</summary>
    public Order? Order { get; set; }

    /// <summary>Purchase account navigation.</summary>
    public FulfilAccount? FulfilAccount { get; set; }

    /// <summary>Payment card navigation.</summary>
    public FulfilCard? FulfilCard { get; set; }

    /// <summary>Order handler navigation.</summary>
    public ApplicationUser? PaidByUser { get; set; }
}

/// <summary>Values for <see cref="SupplierPurchase.SupplierStatus"/>.</summary>
public static class SupplierStatusGroups
{
    /// <summary>Paid / preparing / to-ship.</summary>
    public const string Processing = "processing";

    /// <summary>Shipped / in transit.</summary>
    public const string Shipped = "shipped";

    /// <summary>Delivered / received.</summary>
    public const string Delivered = "delivered";

    /// <summary>Canceled / closed on SHEIN.</summary>
    public const string Canceled = "canceled";

    /// <summary>Refunded / returned on SHEIN.</summary>
    public const string Refunded = "refunded";
}

/// <summary>Values for <see cref="SupplierPurchase.TrackingKind"/>.</summary>
public static class SupplierTrackingKinds
{
    /// <summary>No tracking yet.</summary>
    public const string None = "none";

    /// <summary>SHEIN internal / International Warehouse tracking (e.g. C2607…).</summary>
    public const string Warehouse = "warehouse";

    /// <summary>Last-mile carrier tracking (UPS/USPS/…).</summary>
    public const string Carrier = "carrier";
}

/// <summary>Automatic SHEIN → 4Seller Mark Ship states.</summary>
public static class AutoMarkShipStatuses
{
    /// <summary>New carrier tracking is ready to publish.</summary>
    public const string Pending = "pending";

    /// <summary>4Seller accepted the Mark Ship request.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Transient API/cookie failure; retry after cooldown.</summary>
    public const string Failed = "failed";

    /// <summary>Cannot safely publish automatically (for example carrier mapping not found).</summary>
    public const string Blocked = "blocked";

    /// <summary>
    /// Mark Ship request was accepted by 4Seller but the async result was not confirmed in time.
    /// Must reconcile against the platform before any retry (never auto-republish blindly).
    /// </summary>
    public const string Unconfirmed = "unconfirmed";
}
