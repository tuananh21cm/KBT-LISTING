namespace Application.DTOs.Order;

/// <summary>
/// Paged local orders.
/// </summary>
public class OrderListPageDto
{
    /// <summary>Rows.</summary>
    public List<OrderListItemDto> Items { get; set; } = [];

    /// <summary>Total matching.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Pass back as <c>cursor</c> to fetch the following page without an <c>OFFSET</c> scan.
    /// Null when this is the last page, or when the view sorts on a key the cursor cannot express
    /// (<c>purchasedToday</c>). Page numbers keep working; the cursor is opt-in.
    /// </summary>
    public string? NextCursor { get; set; }

    /// <summary>
    /// Always null — counts live on <c>GET …/orders/status-counts</c> so paging does not recompute them.
    /// Kept on the payload so older clients keep deserializing.
    /// </summary>
    public OrderStatusCountsDto? StatusCounts { get; set; }
}

/// <summary>Aggregate counts for admin order workflow tabs (composite).</summary>
public class OrderStatusCountsDto
{
    /// <summary>All orders in scope (no status tab filter).</summary>
    public int All { get; set; }

    /// <summary>Chờ xử lý (PENDING_PROCESS).</summary>
    public int PendingProcess { get; set; }

    /// <summary>Đã mua (PURCHASED).</summary>
    public int Purchased { get; set; }

    /// <summary>Chờ lấy (AWAITING_COLLECTION_GROUP).</summary>
    public int AwaitingCollection { get; set; }

    /// <summary>Đang giao (IN_TRANSIT).</summary>
    public int InTransit { get; set; }

    /// <summary>Giao thành công (DELIVERED).</summary>
    public int Delivered { get; set; }

    /// <summary>Đã hủy (CANCELLED).</summary>
    public int Canceled { get; set; }

    /// <summary>Refund TikTok (REFUND).</summary>
    public int Refund { get; set; }

    /// <summary>USPS last-mile chưa xử lý thủ công (<c>USPS_UNPROCESSED</c>).</summary>
    public int UspsUnprocessed { get; set; }

    /// <summary>USPS last-mile đã stamp (<c>USPS_PROCESSED</c>).</summary>
    public int UspsProcessed { get; set; }

    /// <summary>Thank Card còn việc (<c>THANK_CARD_PENDING</c>).</summary>
    public int ThankCardPending { get; set; }

    /// <summary>Thank Card đã stamp (<c>THANK_CARD_PROCESSED</c>).</summary>
    public int ThankCardProcessed { get; set; }

    /// <summary>Refund TikTok chưa xử lý ops (<c>REFUND_UNPROCESSED</c>).</summary>
    public int RefundUnprocessed { get; set; }

    /// <summary>Refund TikTok đã stamp ops (<c>REFUND_PROCESSED</c>).</summary>
    public int RefundProcessed { get; set; }

    /// <summary>Đơn hủy chưa xử lý ops (<c>CANCEL_UNPROCESSED</c>).</summary>
    public int CancelUnprocessed { get; set; }

    /// <summary>Đơn hủy đã stamp ops (<c>CANCEL_PROCESSED</c>).</summary>
    public int CancelProcessed { get; set; }

    /// <summary>Queue USPS/Thank Card combined — còn việc (legacy).</summary>
    public int UspsThankCardPending { get; set; }

    /// <summary>Queue USPS/Thank Card combined — đã xử lý đủ (legacy).</summary>
    public int UspsThankCardProcessed { get; set; }

    /// <summary>Chờ mua — đơn tạo trong ngày VN (<c>pendingFilter=new</c>).</summary>
    public int PendingNew { get; set; }

    /// <summary>Chờ mua — IssueStatus=Processing (<c>pendingFilter=out_stock</c>).</summary>
    public int PendingOutStock { get; set; }

    /// <summary>Chờ mua — tuổi 0–&lt;24 giờ (<c>pendingFilter=24h</c>).</summary>
    public int Pending24h { get; set; }

    /// <summary>Chờ mua — tuổi 24–&lt;48 giờ (<c>pendingFilter=48h</c>).</summary>
    public int Pending48h { get; set; }

    /// <summary>Chờ mua — tuổi ≥48 giờ (<c>pendingFilter=over_48h</c>).</summary>
    public int PendingOver48h { get; set; }

    /// <summary>
    /// Orders whose shop is not money-eligible: missing Active status and/or no receiving bank card.
    /// Orthogonal to workflow tabs (Delivered / Cancelled / …).
    /// </summary>
    public int Suspended { get; set; }

    /// <summary>@deprecated Prefer PendingProcess — kept for brief API compat.</summary>
    public int OnHold { get; set; }

    /// <summary>@deprecated Prefer PendingProcess.</summary>
    public int AwaitingShipment { get; set; }
}

/// <summary>Order card row for admin 4Seller-style list.</summary>
public class OrderListItemDto
{
    /// <summary>Local id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller order id.</summary>
    public long FourSellerOrderId { get; set; }

    /// <summary>Platform order id.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Platform.</summary>
    public string? Platform { get; set; }

    /// <summary>Order status.</summary>
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>Platform order status.</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>TikTok / 4Seller cancel or refund reason.</summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// Human-readable after-sales status from 4Seller (<c>returnStatus</c>), e.g.
    /// "Awaiting customer return". Null when the order has no live return case.
    /// </summary>
    public string? ReturnStatus { get; set; }

    /// <summary>Operational notice under the order-card status line (local; denormalized from issue tags).</summary>
    public string? Message { get; set; }

    /// <summary>Attached issue tags.</summary>
    public List<IssueTagDto> IssueTags { get; set; } = [];

    /// <summary><c>None</c> / <c>Processing</c> / <c>Done</c>.</summary>
    public string IssueStatus { get; set; } = "None";

    /// <summary>Composite workflow tab key (PENDING_PROCESS, PURCHASED, …).</summary>
    public string? WorkflowTab { get; set; }

    /// <summary>Amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Paid amount (customer paid after vouchers).</summary>
    public decimal TotalPaidAmount { get; set; }

    /// <summary>Product amount before discounts (TikTok breakdown).</summary>
    public decimal? ProductAmountBeforeDiscount { get; set; }

    /// <summary>Platform-funded product discount.</summary>
    public decimal? ProductPlatformDiscount { get; set; }

    /// <summary>Seller-funded product discount.</summary>
    public decimal? ProductSellerDiscount { get; set; }

    /// <summary>Shipping fee before discounts.</summary>
    public decimal? ShippingFeeBeforeDiscount { get; set; }

    /// <summary>Seller-funded shipping discount.</summary>
    public decimal? ShippingFeeSellerDiscount { get; set; }

    /// <summary>Platform tax from orderAmountVo.</summary>
    public decimal? PlatformTotalTax { get; set; }

    /// <summary>Other platform fees.</summary>
    public decimal? PlatformOtherFee { get; set; }

    /// <summary>Seller net payout (system Rev when present).</summary>
    public decimal? SellerNetAmount { get; set; }

    /// <summary>When pricing detail was enriched from 4Seller.</summary>
    public DateTime? PricingDetailSyncedAt { get; set; }

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Product import / supplier cost (local — deprecated; use Supplier*).</summary>
    public decimal? Cost { get; set; }

    /// <summary>Supplier shipping fee (local — deprecated; use SupplierShippingFee).</summary>
    public decimal? ShippingFee { get; set; }

    /// <summary>Tax fee (local — deprecated; use SupplierTax).</summary>
    public decimal? Tax { get; set; }

    /// <summary>Ads fee (local — deprecated).</summary>
    public decimal? Ads { get; set; }

    /// <summary>Supplier platform order number (SHEIN billno, Temu id, …).</summary>
    public string? SupplierOrderNo { get; set; }

    /// <summary>Supplier order detail page URL.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary>Supplier retail (list) price.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Supplier subtotal (giá nhập NCC).</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Supplier shipping fee.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>Optional on-time / delivery-guarantee fee.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon/promo discount (positive = amount reduced).</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership/club savings (positive = amount saved).</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed (SHEIN Points; positive = amount reduced).</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used (payment, not coupon).</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Supplier sales tax.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Supplier grand total charged.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>
    /// Purchase cost for display/KPI: gross NCC (subtotal+ship+tax+…) + BuyTrack + ThankCard.
    /// Profit = Revenue − TotalCost. TrueSpend (thẻ+ví+club) dùng riêng cho preview Mua hàng.
    /// </summary>
    public decimal? TotalCost { get; set; }

    /// <summary>Sum of line-item quantities (min 1).</summary>
    public int TotalQuantity { get; set; }

    /// <summary>Tracking.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Shipment no.</summary>
    public string? ShipmentNo { get; set; }

    /// <summary>Shipping method.</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>Shipping service.</summary>
    public string? ShippingService { get; set; }

    /// <summary>Create time.</summary>
    public DateTime? OrderCreateTime { get; set; }

    /// <summary>Paid time.</summary>
    public DateTime? OrderPaidTime { get; set; }

    /// <summary>Latest ship (leave) date.</summary>
    public DateTime? LatestShipDate { get; set; }

    /// <summary>Local fulfil purchase time (cost recorded).</summary>
    public DateTime? FulfilPurchasedAt { get; set; }

    /// <summary>First time tracking became non-empty.</summary>
    public DateTime? TrackingAddedAt { get; set; }

    /// <summary>Manual: in transit.</summary>
    public DateTime? InTransitAt { get; set; }

    /// <summary>Manual: delivered to customer.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Manual: refunded.</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>Manual: USPS last-mile extra handling completed (UTC).</summary>
    public DateTime? UspsProcessedAt { get; set; }

    /// <summary>PrintPackShip batch id after successful USPS scan push; null if never pushed.</summary>
    public long? UspsScanBatchId { get; set; }

    /// <summary>When the USPS scan batch was successfully sent (UTC).</summary>
    public DateTime? UspsScanSentAt { get; set; }

    /// <summary>BuyTrack fee currently applied (USD). 0 when not applied.</summary>
    public decimal BuyTrackAmount { get; set; }

    /// <summary>Manual: ThankCard purchased (UTC).</summary>
    public DateTime? ThankCardProcessedAt { get; set; }

    /// <summary>ThankCard fee currently applied (USD). 0 when not applied.</summary>
    public decimal ThankCardAmount { get; set; }

    /// <summary>Manual: ops marked TikTok REFUND handling done (UTC).</summary>
    public DateTime? RefundProcessedAt { get; set; }

    /// <summary>First time status entered canceled.</summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>Last synced.</summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>Owner seller id.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner display.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Local shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Shop browser profile name.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Buyer name.</summary>
    public string? BuyerName { get; set; }

    /// <summary>Buyer phone.</summary>
    public string? BuyerPhone { get; set; }

    /// <summary>Buyer email.</summary>
    public string? BuyerEmail { get; set; }

    /// <summary>Address.</summary>
    public string? Address { get; set; }

    /// <summary>Buyer city.</summary>
    public string? City { get; set; }

    /// <summary>Buyer state.</summary>
    public string? State { get; set; }

    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Buyer memo / note.</summary>
    public string? BuyerMemo { get; set; }

    /// <summary>Seller memo / note.</summary>
    public string? SellerMemo { get; set; }

    /// <summary>Internal memo / note.</summary>
    public string? InternalMemo { get; set; }

    /// <summary>Purchase / fulfil account id.</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Purchase account name.</summary>
    public string? FulfilAccountName { get; set; }

    /// <summary>Payment card id.</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>Payment card name.</summary>
    public string? FulfilCardName { get; set; }

    /// <summary>Masked payment card number.</summary>
    public string? FulfilCardNumberMasked { get; set; }

    /// <summary>Order handler user id (sales attribution).</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Order handler display name.</summary>
    public string? PaidByUserName { get; set; }

    /// <summary>Fulfil currently holding this order (lease). Null when free / expired.</summary>
    public Guid? FulfilClaimedByUserId { get; set; }

    /// <summary>Display name of the fulfil holding the order.</summary>
    public string? FulfilClaimedByName { get; set; }

    /// <summary>When the first session of the holder claimed (UTC).</summary>
    public DateTime? FulfilClaimedAt { get; set; }

    /// <summary>Lease expiry = MAX of open session expires (UTC).</summary>
    public DateTime? FulfilClaimExpiresAt { get; set; }

    /// <summary>Number of Chrome-profile sessions currently holding.</summary>
    public int FulfilClaimSessionCount { get; set; }

    /// <summary>
    /// Whether shop payout was marked received on the linked receiving bank card.
    /// </summary>
    public bool IsSettlementPaid { get; set; }

    /// <summary>
    /// Whether the customer paid on the marketplace (from <c>orderPaidTime</c> sync).
    /// Distinct from <see cref="IsSettlementPaid"/>.
    /// </summary>
    public bool IsCustomerPaid { get; set; }

    /// <summary>Package weight.</summary>
    public decimal? PackageWeight { get; set; }

    /// <summary>Weight unit.</summary>
    public string? PackageWeightUnit { get; set; }

    /// <summary>Length.</summary>
    public decimal? PackageLength { get; set; }

    /// <summary>Width.</summary>
    public decimal? PackageWidth { get; set; }

    /// <summary>Height.</summary>
    public decimal? PackageHeight { get; set; }

    /// <summary>Dimension unit.</summary>
    public string? PackageDimensionUnit { get; set; }

    /// <summary>Item count.</summary>
    public int ItemCount { get; set; }

    /// <summary>Line items for list card.</summary>
    public List<OrderItemDto> LineItems { get; set; } = [];

    /// <summary>
    /// Mọi lần mua NCC đã gắn vào đơn sàn này (1 TikTok → N billno SHEIN). Các trường
    /// <c>Supplier*</c> cấp đơn lấy từ <c>Orders.Charged*</c> (materialize lúc sync);
    /// <c>TotalCost</c> = gross NCC + BuyTrack + ThankCard; Profit = Revenue − TotalCost.
    /// </summary>
    public List<OrderSupplierLinkDto> SupplierLinks { get; set; } = [];
}

/// <summary>One supplier (SHEIN) purchase linked to a local order.</summary>
public class OrderSupplierLinkDto
{
    /// <summary>Row id (<c>SupplierPurchases.Id</c>).</summary>
    public Guid Id { get; set; }

    /// <summary><c>true</c> khi dòng này được cộng vào giá mua của đơn.</summary>
    public bool IsCharged { get; set; }

    /// <summary><c>shein</c> | <c>manual</c>.</summary>
    public string Source { get; set; } = "shein";

    /// <summary>Supplier order number (billno).</summary>
    public string SupplierOrderNo { get; set; } = string.Empty;

    /// <summary>SHEIN relation_billno (Joint Shipment group key).</summary>
    public string? RelationBillno { get; set; }

    /// <summary>SHEIN confluence_batch_no (warehouse consolidate batch).</summary>
    public string? ConfluenceBatchNo { get; set; }

    /// <summary>Detail URL.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary>Tracking number (warehouse C… or carrier).</summary>
    public string? TrackingNo { get; set; }

    /// <summary>
    /// Last SHEIN warehouse C…/N… seen — kept after <see cref="TrackingNo"/> upgrades to carrier.
    /// </summary>
    public string? WarehouseTrackingNo { get; set; }

    /// <summary>UTC when warehouse tracking was first recorded.</summary>
    public DateTime? WarehouseTrackingSeenAt { get; set; }

    /// <summary>Carrier / track_info name.</summary>
    public string? Carrier { get; set; }

    /// <summary><c>none</c> | <c>warehouse</c> | <c>carrier</c>.</summary>
    public string TrackingKind { get; set; } = "none";

    /// <summary>Grand total for this billno.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>Retail (list) price for this billno.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Subtotal (giá nhập) for this billno.</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Shipping fee for this billno.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee for this billno.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>On-time / delivery-guarantee fee for this billno.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon discount for this billno.</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership / club discount for this billno.</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed for this billno.</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used for this billno.</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Sales tax for this billno.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Normalized SHEIN status group on this link.</summary>
    public string? SupplierStatus { get; set; }

    /// <summary>Raw SHEIN status text/code.</summary>
    public string? SupplierStatusRaw { get; set; }

    /// <summary>When SHEIN status was last observed (UTC).</summary>
    public DateTime? SupplierStatusAt { get; set; }

    /// <summary><c>true</c> khi SHEIN xác nhận billno chưa thanh toán.</summary>
    public bool SupplierUnpaid { get; set; }

    /// <summary>Carrier tracking evaluated for automatic 4Seller Mark Ship.</summary>
    public string? AutoMarkShipTrackingNo { get; set; }

    /// <summary><c>pending</c> | <c>succeeded</c> | <c>failed</c> | <c>blocked</c> | <c>unconfirmed</c>.</summary>
    public string? AutoMarkShipStatus { get; set; }

    /// <summary>Last automatic Mark Ship attempt time (UTC).</summary>
    public DateTime? AutoMarkShipAttemptedAt { get; set; }

    /// <summary>Last automatic Mark Ship error or blocked reason (UI when order still lacks published tracking).</summary>
    public string? AutoMarkShipError { get; set; }

    /// <summary>4Seller async Mark Ship request id (for unconfirmed reconcile).</summary>
    public string? AutoMarkShipRequestId { get; set; }
}

/// <summary>Full order detail.</summary>
public class OrderDetailDto : OrderListItemDto
{
    /// <summary>Shipped time.</summary>
    public DateTime? OrderShippedTime { get; set; }

    /// <summary>Latest delivery date.</summary>
    public DateTime? LatestDeliveryDate { get; set; }

    /// <summary>Line items (alias for detail drawer).</summary>
    public List<OrderItemDto> Items { get; set; } = [];
}

/// <summary>Order line item DTO.</summary>
public class OrderItemDto
{
    /// <summary>Local id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller item id.</summary>
    public long FourSellerOrderItemId { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Title.</summary>
    public string? Title { get; set; }

    /// <summary>Image.</summary>
    public string? ImgUrl { get; set; }

    /// <summary>Source catalog image. UI should prefer this, then fall back to <see cref="ImgUrl"/>.</summary>
    public string? SourceImgUrl { get; set; }

    /// <summary>Listing average rating (overlay OwnerId + SellerSku; not persisted on OrderItem).</summary>
    public decimal? AverageRating { get; set; }

    /// <summary>Listing review count (overlay OwnerId + SellerSku; not persisted on OrderItem).</summary>
    public int? ReviewCount { get; set; }

    /// <summary>Supplier shop title (overlay listing → SupplierSellerInfos; not persisted).</summary>
    public string? StoreTitle { get; set; }

    /// <summary>Supplier shop URL (overlay; not persisted).</summary>
    public string? StoreUrl { get; set; }

    /// <summary>Shein shopRank (overlay; not persisted).</summary>
    public decimal? ShopRank { get; set; }

    /// <summary>Shein storeAttention / followers (overlay; not persisted).</summary>
    public int? StoreAttention { get; set; }

    /// <summary>Supplier shipping SLA days (overlay; not persisted).</summary>
    public int? ShippingDays { get; set; }

    /// <summary>Supplier % delivered within SLA (overlay; not persisted).</summary>
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Local US stock (overlay SupplierProductInfos; not persisted).</summary>
    public bool? IsLocalProduct { get; set; }

    /// <summary>Quick-ship support (overlay SupplierProductInfos; not persisted).</summary>
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Listing URL.</summary>
    public string? ListingUrl { get; set; }

    /// <summary>Source URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Variant.</summary>
    public string? VariantAttr { get; set; }

    /// <summary>Qty.</summary>
    public int Quantity { get; set; }

    /// <summary>Price.</summary>
    public decimal ItemPrice { get; set; }

    /// <summary>Currency.</summary>
    public string? Currency { get; set; }
}

/// <summary>
/// Lightweight order card for hover preview (items + cost/revenue breakdown; no buyer/notes/milestones).
/// </summary>
public class OrderHoverPreviewDto
{
    /// <summary>Local order id.</summary>
    public Guid Id { get; set; }

    /// <summary>Platform order id.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Platform order status.</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Shipping method (for revenue section title).</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>Shipping service fallback for title.</summary>
    public string? ShippingService { get; set; }

    /// <summary>Published platform tracking number (after Mark Ship / 4Seller seed).</summary>
    public string? TrackingNo { get; set; }

    /// <summary>When TikTok pricing detail was enriched — revenue breakdown is meaningful.</summary>
    public DateTime? PricingDetailSyncedAt { get; set; }

    /// <summary>System Rev (seller net / paid / amount).</summary>
    public decimal SellAmount { get; set; }

    /// <summary>Customer paid amount.</summary>
    public decimal TotalPaidAmount { get; set; }

    /// <summary>Product amount before discounts.</summary>
    public decimal? ProductAmountBeforeDiscount { get; set; }

    /// <summary>Platform-funded product discount.</summary>
    public decimal? ProductPlatformDiscount { get; set; }

    /// <summary>Seller-funded product discount.</summary>
    public decimal? ProductSellerDiscount { get; set; }

    /// <summary>Shipping fee before discounts.</summary>
    public decimal? ShippingFeeBeforeDiscount { get; set; }

    /// <summary>Seller-funded shipping discount.</summary>
    public decimal? ShippingFeeSellerDiscount { get; set; }

    /// <summary>Platform tax.</summary>
    public decimal? PlatformTotalTax { get; set; }

    /// <summary>Other platform fees.</summary>
    public decimal? PlatformOtherFee { get; set; }

    /// <summary>TrueSpend = thẻ + ví + club (NCC). Khác <c>Orders.TotalCost</c> khi có BuyTrack/ThankCard.</summary>
    public decimal? CostAmount { get; set; }

    /// <summary>Supplier retail (list) price.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Supplier subtotal (giá nhập).</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Supplier shipping fee.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>On-time / delivery-guarantee fee.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon discount.</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership discount.</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed (SHEIN Points).</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used (payment, not coupon).</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Supplier sales tax.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Number of supplier billnos aggregated into the NCC totals (Joint Shipment).</summary>
    public int SupplierOrderCount { get; set; }

    /// <summary>
    /// Per-billno breakdown feeding the aggregate above, oldest first. Each row matches the
    /// supplier's own order page 1-1; the aggregate does not when a local order needed
    /// more than one purchase.
    /// </summary>
    public List<OrderHoverPreviewSupplierLinkDto> SupplierLinks { get; set; } = [];

    /// <summary>Sum of line quantities.</summary>
    public int TotalQuantity { get; set; }

    /// <summary>Total line-item rows (before preview cap).</summary>
    public int ItemCount { get; set; }

    /// <summary>Line items (capped for payload size).</summary>
    public List<OrderHoverPreviewItemDto> Items { get; set; } = [];
}

/// <summary>Cost breakdown of one supplier billno inside <see cref="OrderHoverPreviewDto"/>.</summary>
public class OrderHoverPreviewSupplierLinkDto
{
    /// <summary>Supplier order number (billno).</summary>
    public string SupplierOrderNo { get; set; } = string.Empty;

    /// <summary>Detail URL on the supplier storefront.</summary>
    public string? SupplierOrderUrl { get; set; }

    /// <summary>
    /// SHEIN <c>relation_billno</c> — chung cho các billno tách ra từ một lần checkout.
    /// </summary>
    public string? RelationBillno { get; set; }

    /// <summary>SHEIN <c>confluence_batch_no</c> — chung cho các gói gộp cùng kho.</summary>
    public string? ConfluenceBatchNo { get; set; }

    /// <summary>Normalized supplier lifecycle group.</summary>
    public string? SupplierStatus { get; set; }

    /// <summary>Tracking / shipping number for this purchase (warehouse or last-mile).</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Last SHEIN warehouse C…/N… kept after carrier upgrade.</summary>
    public string? WarehouseTrackingNo { get; set; }

    /// <summary>UTC when warehouse tracking was first recorded.</summary>
    public DateTime? WarehouseTrackingSeenAt { get; set; }

    /// <summary>Carrier name from SHEIN <c>track_info.name</c> (USPS, FedEx, …).</summary>
    public string? Carrier { get; set; }

    /// <summary><c>none</c> | <c>warehouse</c> | <c>carrier</c>.</summary>
    public string? TrackingKind { get; set; }

    /// <summary>
    /// <c>true</c> khi SHEIN xác nhận billno này chưa thanh toán — không được tính vào giá mua.
    /// </summary>
    public bool SupplierUnpaid { get; set; }

    /// <summary><c>true</c> khi dòng này được cộng vào giá mua của đơn.</summary>
    public bool IsCharged { get; set; }

    /// <summary>Retail (list) price.</summary>
    public decimal? SupplierRetailPrice { get; set; }

    /// <summary>Subtotal (giá nhập).</summary>
    public decimal? SupplierSubtotal { get; set; }

    /// <summary>Shipping fee.</summary>
    public decimal? SupplierShippingFee { get; set; }

    /// <summary>SHEIN retail delivery fee.</summary>
    public decimal? SupplierRetailDeliveryFee { get; set; }

    /// <summary>On-time / delivery-guarantee fee.</summary>
    public decimal? SupplierOnTimeGuarantee { get; set; }

    /// <summary>Coupon discount.</summary>
    public decimal? SupplierCouponPrice { get; set; }

    /// <summary>Membership / club discount.</summary>
    public decimal? SupplierMembershipDiscount { get; set; }

    /// <summary>Loyalty points redeemed.</summary>
    public decimal? SupplierPointsDiscount { get; set; }

    /// <summary>SHEIN wallet / cashback used.</summary>
    public decimal? SupplierWalletPrice { get; set; }

    /// <summary>Sales tax.</summary>
    public decimal? SupplierTax { get; set; }

    /// <summary>Grand total charged for this billno.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>
    /// <c>true</c> khi breakdown cost đã đủ tin cậy (subtotal + total đều có giá trị).
    /// Derived — không phải cột DB.
    /// </summary>
    public bool CostDetailComplete { get; set; }
}

/// <summary>One line in <see cref="OrderHoverPreviewDto"/>.</summary>
public class OrderHoverPreviewItemDto
{
    /// <summary>Title.</summary>
    public string? Title { get; set; }

    /// <summary>Image URL.</summary>
    public string? ImgUrl { get; set; }

    /// <summary>Quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Unit sell price.</summary>
    public decimal ItemPrice { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Variant attributes.</summary>
    public string? VariantAttr { get; set; }
}
