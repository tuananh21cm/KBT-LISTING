using System.Text.Json.Serialization;

namespace Application.DTOs.Order;

/// <summary>
/// Payload from the fulfil-extension "Sync Order" button: the current SHEIN
/// account's orders (already normalized in the content script) to match against
/// local orders and fill tracking + cost breakdown.
/// Two-phase: <c>list</c> (order/list only) then <c>detail</c> (get_order_info for needsDetail).
/// </summary>
public class SheinOrderSyncRequest
{
    /// <summary>
    /// Sync phase: <c>list</c> (default) or <c>detail</c>.
    /// </summary>
    public string Phase { get; set; } = "list";

    /// <summary>
    /// Optional override: <c>new</c> (pool fulfil) or <c>all</c> (pool mọi đơn không huỷ trong Lookback).
    /// Null → dùng Settings <c>extension.shein_sync.update_mode</c>.
    /// </summary>
    public string? UpdateMode { get; set; }

    /// <summary>
    /// Mã do extension sinh một lần cho cả lượt Sync Order và dùng chung cho mọi POST list +
    /// detail của lượt đó. Backend ghi mã này vào <c>SheinSyncRunLogs</c> để sau còn gộp lại
    /// được các lô rời rạc thành một lượt và trả lời "billno nào bị bỏ qua".
    /// </summary>
    public string? RunId { get; set; }

    /// <summary>Thứ tự lô trong lượt (list 1..n rồi detail 1..m), chỉ để đọc log cho dễ.</summary>
    public int? BatchIndex { get; set; }

    /// <summary>
    /// Trần lần gọi 4Seller Mark Ship cho lô này.
    /// <list type="bullet">
    /// <item><c>null</c> (mặc định HTTP extension / realtime) — <b>không</b> chạy Auto Mark Ship
    /// hay reconcile; Mark Ship để job <c>shein.order_detail_sync</c> / Sync Track.</item>
    /// <item><c>0</c> — sweep đã hết ngân sách giữa các lô: vẫn reconcile, không publish mới.</item>
    /// <item><c>&gt;0</c> — detail sweep / Sync Track: trần publish trong lô (clamp
    /// <c>MaxAttemptsOverride</c>).</item>
    /// </list>
    /// </summary>
    [JsonIgnore]
    public int? MaxAutoMarkShipAttempts { get; set; }

    /// <summary>
    /// When true, Auto Mark Ship dùng poll interactive (~60s) và bật pha Mark Ship dù
    /// <see cref="MaxAutoMarkShipAttempts"/> null. Set bởi Sync Track / detail sweep; để
    /// <c>false</c> trên extension <c>shein-sync</c> HTTP (không Mark Ship).
    /// </summary>
    [JsonIgnore]
    public bool AutoMarkShipPollInteractive { get; set; }

    /// <summary>
    /// Tài khoản fulfil đang chạy session (backend realtime). Fallback khi
    /// <c>billaddr_info.email</c> thiếu hoặc chưa map được <c>FulfilAccounts</c> — vẫn phải
    /// ghi <c>SupplierPurchases.FulfilAccountId</c> vì biết chắc billno thuộc session này.
    /// </summary>
    [JsonIgnore]
    public Guid? SyncFulfilAccountId { get; set; }

    /// <summary>SHEIN orders captured from the account's order list (+ detail).</summary>
    public List<SheinOrderDto> Orders { get; set; } = [];
}

/// <summary>One normalized SHEIN order used for matching + cost breakdown.</summary>
public class SheinOrderDto
{
    /// <summary>SHEIN order number (billno).</summary>
    public string Billno { get; set; } = string.Empty;

    /// <summary>
    /// Email billing của tài khoản SHEIN đã mua đơn (<c>billaddr_info.email</c>).
    /// Dùng để map <c>FulfilAccounts</c> và lọc pool khớp.
    /// </summary>
    public string? AccountEmail { get; set; }

    /// <summary>
    /// SHEIN order detail URL (e.g. https://us.shein.com/user/orders/detail/{billno}).
    /// </summary>
    public string? OrderUrl { get; set; }

    /// <summary>Buyer full name (firstname + lastname).</summary>
    public string? BuyerName { get; set; }

    /// <summary>Buyer phone.</summary>
    public string? BuyerPhone { get; set; }

    /// <summary>Ship-to street address.</summary>
    public string? Address { get; set; }

    /// <summary>Ship-to city.</summary>
    public string? City { get; set; }

    /// <summary>Ship-to state / province.</summary>
    public string? State { get; set; }

    /// <summary>Ship-to postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Ship-to country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Tracking / shipping number (from goods_pkg_rel_list). Prefer carrier over warehouse.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>
    /// SHEIN warehouse C…/N… when present alongside or before last-mile carrier.
    /// Used to persist eligibility after <see cref="TrackingNo"/> upgrades.
    /// </summary>
    public string? WarehouseTrackingNo { get; set; }

    /// <summary>Carrier name (from track_info.name, or inferred from method_real / tracking prefix).</summary>
    public string? Carrier { get; set; }

    /// <summary>
    /// SHEIN <c>goods_pkg_rel_list[].shipping_method_real</c> (e.g. <c>ASnGOFOxCNO-DID-PB-BN-Na</c>).
    /// Used to infer carrier when <c>track_info.name</c> is still null.
    /// </summary>
    public string? ShippingMethodReal { get; set; }

    /// <summary>
    /// Optional: SHEIN Joint Shipment / multi-package flag from list/detail payload.
    /// Used as a soft boost when attaching an additional billno to an already-linked local order.
    /// </summary>
    public bool? IsJointShipment { get; set; }

    /// <summary>
    /// SHEIN <c>relation_billno</c> (e.g. USU18K244000000MPKY). Shared by Joint Shipment siblings.
    /// </summary>
    public string? RelationBillno { get; set; }

    /// <summary>
    /// SHEIN <c>order_package_info_list[].confluence_batch_no</c> (warehouse consolidate batch).
    /// </summary>
    public string? ConfluenceBatchNo { get; set; }

    /// <summary>
    /// SHEIN <c>order_type</c> — <c>5</c> = Club/Prime, <c>6</c> = Saver/xtra virtual (not a product purchase).
    /// </summary>
    public int? OrderType { get; set; }

    /// <summary>
    /// True when this billno is Club/Saver/Prime / virtual (not a physical product to fulfil).
    /// Synced from extension; backend also re-detects from <c>order_type</c> / goods ids / SKU prefixes.
    /// </summary>
    public bool? IsMembershipOrVirtual { get; set; }

    /// <summary>Currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>Retail (list) price.</summary>
    public decimal? RetailPrice { get; set; }

    /// <summary>Giá nhập — SHEIN UI Subtotal (ưu tiên hơn API subTotalPrice khi API đã trừ CLUB).</summary>
    public decimal? Subtotal { get; set; }

    /// <summary>Shipping fee — SHEIN shippingPrice.</summary>
    public decimal? ShippingFee { get; set; }

    /// <summary>Retail delivery fee — SHEIN <c>retail_delivery_fee.amount</c>.</summary>
    public decimal? RetailDeliveryFee { get; set; }

    /// <summary>On-time delivery guarantee fee.</summary>
    public decimal? OnTimeGuarantee { get; set; }

    /// <summary>Coupon discount — SHEIN couponPrice.</summary>
    public decimal? CouponPrice { get; set; }

    /// <summary>Legacy alias: discount → CouponPrice (deserialize only).</summary>
    [JsonPropertyName("discount")]
    public decimal? DiscountAlias
    {
        set { if (value.HasValue) CouponPrice ??= value; }
    }

    /// <summary>SHEIN CLUB discount — prime_discount_amount_vo_total.</summary>
    public decimal? ClubDiscount { get; set; }

    /// <summary>Legacy alias: clubSavings → ClubDiscount (deserialize only).</summary>
    [JsonPropertyName("clubSavings")]
    public decimal? ClubSavingsAlias
    {
        set { if (value.HasValue) ClubDiscount ??= value; }
    }

    /// <summary>Loyalty points redeemed — SHEIN Points row (positive = amount reduced).</summary>
    public decimal? PointsDiscount { get; set; }

    /// <summary>
    /// SHEIN wallet / cashback used — <c>usedWalletPrice.amount</c>. Payment from wallet, not a coupon.
    /// </summary>
    public decimal? WalletPrice { get; set; }

    /// <summary>Sales tax — SHEIN taxPrice / tax.</summary>
    public decimal? Tax { get; set; }

    /// <summary>Legacy alias: salesTax → Tax (deserialize only).</summary>
    [JsonPropertyName("salesTax")]
    public decimal? SalesTaxAlias
    {
        set { if (value.HasValue) Tax ??= value; }
    }

    /// <summary>Grand total charged — SHEIN totalPrice.</summary>
    public decimal? Total { get; set; }

    /// <summary>
    /// SHEIN order create / place time (UTC). Used to fill <c>FulfilPurchasedAt</c>
    /// when that milestone is still empty.
    /// </summary>
    public DateTime? OrderedAt { get; set; }

    /// <summary>
    /// Mốc SHEIN trừ tiền thật (UTC) từ <c>pay_time</c> (list, giây) / <c>paymentTime</c> (detail,
    /// millisecond). Đây mới là clock của hành động "mua"; <see cref="OrderedAt"/> chỉ là lúc tạo đơn.
    /// </summary>
    public DateTime? SheinPaidAt { get; set; }

    /// <summary>
    /// <c>true</c> chỉ khi payload nói rõ <c>pay_time</c> / <c>paymentTime</c> = 0 (tạo đơn nhưng
    /// chưa trả tiền). <c>null</c> = payload không có khoá nào để kết luận, backend giữ hành vi cũ.
    /// </summary>
    public bool? SheinUnpaid { get; set; }

    /// <summary>
    /// Product id / sku của các món trong đơn SHEIN (<c>orderGoodsList</c>). Dùng phân giải
    /// khi nhiều đơn local trùng phone + zip: so với <c>OrderItem.SellerSku</c> dạng
    /// <c>86020798-I58rjh8y8izh</c> (86020798 = goods id SHEIN). Sản phẩm nhiều variant có
    /// nhiều sku nên so theo tập hợp — trùng bất kỳ phần tử là cùng sản phẩm.
    /// </summary>
    public List<string> ProductIds { get; set; } = [];

    /// <summary>Raw SHEIN status code/text from list/detail.</summary>
    public string? SheinStatus { get; set; }

    /// <summary>
    /// Normalized group: <c>processing</c> | <c>shipped</c> | <c>delivered</c> |
    /// <c>canceled</c> | <c>refunded</c>.
    /// </summary>
    public string? SheinStatusGroup { get; set; }

    /// <summary>
    /// Optional SHEIN status-change timestamp (UTC). Backward-compatible fallback for the
    /// current status group when the per-milestone clocks below are absent.
    /// </summary>
    public DateTime? SheinStatusAt { get; set; }

    /// <summary>
    /// Real SHEIN ship / dispatch clock (UTC) → <c>InTransitAt</c> and (for carrier) <c>TrackingAddedAt</c>.
    /// Independent of <see cref="SheinDeliveredAt"/> so a shipped-then-delivered order keeps accurate gaps.
    /// </summary>
    public DateTime? SheinShippedAt { get; set; }

    /// <summary>Real SHEIN carrier last-mile tracking clock (UTC) → <c>TrackingAddedAt</c>. Falls back to ship clock.</summary>
    public DateTime? SheinTrackingAt { get; set; }

    /// <summary>Real SHEIN delivered / signed clock (UTC) → <c>DeliveredAt</c>.</summary>
    public DateTime? SheinDeliveredAt { get; set; }

    /// <summary>Real SHEIN cancel clock (UTC) → <c>CanceledAt</c>.</summary>
    public DateTime? SheinCanceledAt { get; set; }

    /// <summary>Real SHEIN refund clock (UTC) → <c>RefundedAt</c> (and <c>CanceledAt</c>).</summary>
    public DateTime? SheinRefundedAt { get; set; }

    /// <summary>
    /// JSON gốc của phần tử trong <c>order/list</c>. Chỉ có khi bật
    /// <c>extension.shein_sync.capture_raw_payload</c>. Có thể bị extension cắt ở 256 KB.
    /// </summary>
    public string? RawListItem { get; set; }

    /// <summary>JSON gốc của response <c>get_order_info</c>, cùng điều kiện với <see cref="RawListItem"/>.</summary>
    public string? RawDetail { get; set; }
}
