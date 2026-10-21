using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Local mirror of a 4Seller order.
/// Unique by <see cref="FourSellerOrderId"/>; also unique per owner on non-empty <see cref="PlatformOrderId"/>.
/// </summary>
public class Order : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>4Seller internal order id (<c>orderId</c>).</summary>
    public long FourSellerOrderId { get; set; }

    /// <summary>Seller owning the 4Seller cookie used to sync this order.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Local shop when matched via <see cref="Shop.FourSellerShopId"/> (4Seller internal id) or unique shop name.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>4Seller shop id from the order payload.</summary>
    public long? FourSellerShopId { get; set; }

    /// <summary>Platform order id / number (e.g. TikTok).</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Platform code (e.g. TIKTOK).</summary>
    public string? Platform { get; set; }

    /// <summary>
    /// 4Seller remote workflow bucket (<c>to_ship</c>, <c>shipped</c>, …) from list sync —
    /// kept for stats / sync filters. Canonical UI status is <see cref="PlatformOrderStatus"/>.
    /// </summary>
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>Canonical platform-native order status (e.g. AWAITING_SHIPMENT, IN_TRANSIT, REFUND).</summary>
    public string? PlatformOrderStatus { get; set; }

    /// <summary>
    /// TikTok / 4Seller cancel or refund reason text. When present on a commerce status,
    /// sync promotes <see cref="PlatformOrderStatus"/> to <c>REFUND</c> (except raw CANCELLED).
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// Human-readable after-sales case status from 4Seller <c>returns/tiktok/page</c>
    /// (<c>returnStatus</c>: "Awaiting customer return", "Refund complete", …).
    /// Owned by the after-sales sync; cleared when the case closes or the order leaves <c>REFUND</c>.
    /// </summary>
    public string? ReturnStatus { get; set; }

    /// <summary>
    /// Operational notice shown under the status line on the order card (local; not from 4Seller).
    /// Denormalized join of attached <see cref="IssueTags"/> labels (synced from tags only).
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Issue workflow: <see cref="OrderIssueStatuses.None"/> /
    /// <see cref="OrderIssueStatuses.Processing"/> / <see cref="OrderIssueStatuses.Done"/>.
    /// </summary>
    public string IssueStatus { get; set; } = OrderIssueStatuses.None;

    /// <summary>Attached issue tags (library tags).</summary>
    public ICollection<OrderIssueTag> IssueTags { get; set; } = new List<OrderIssueTag>();

    /// <summary>Order amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Total paid amount (customer paid after vouchers; from list sync).</summary>
    public decimal TotalPaidAmount { get; set; }

    // ----- TikTok / platform payout breakdown (from 4Seller order detail, once) -----

    /// <summary>Product amount before discounts (<c>orderAmountVo.productAmountBeforeDiscount</c>).</summary>
    public decimal? ProductAmountBeforeDiscount { get; set; }

    /// <summary>Platform-funded product discount (display only; not in seller net).</summary>
    public decimal? ProductPlatformDiscount { get; set; }

    /// <summary>Seller-funded product discount.</summary>
    public decimal? ProductSellerDiscount { get; set; }

    /// <summary>Shipping fee before discounts.</summary>
    public decimal? ShippingFeeBeforeDiscount { get; set; }

    /// <summary>Seller-funded shipping discount.</summary>
    public decimal? ShippingFeeSellerDiscount { get; set; }

    /// <summary>Platform tax from <c>orderAmountVo.totalTax</c> (not local legacy <see cref="Tax"/>).</summary>
    public decimal? PlatformTotalTax { get; set; }

    /// <summary>Other platform fees from <c>orderAmountVo.otherFee</c>.</summary>
    public decimal? PlatformOtherFee { get; set; }

    /// <summary>
    /// Seller net payout after TikTok fees/discounts (computed once from detail).
    /// System revenue / settlement / stats prefer this over <see cref="TotalPaidAmount"/>.
    /// </summary>
    public decimal? SellerNetAmount { get; set; }

    /// <summary>UTC when pricing detail was successfully enriched (null = still needs detail).</summary>
    public DateTime? PricingDetailSyncedAt { get; set; }

    /// <summary>Currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Product import / supplier cost (local; not from 4Seller).</summary>
    public decimal? Cost { get; set; }

    /// <summary>Supplier shipping fee (local; not from 4Seller).</summary>
    public decimal? ShippingFee { get; set; }

    /// <summary>Tax fee (local; not from 4Seller).</summary>
    public decimal? Tax { get; set; }

    /// <summary>Ads fee if any (local; not from 4Seller).</summary>
    public decimal? Ads { get; set; }

    // ----- Supplier purchase -----
    // Breakdown từng billno nằm ở SupplierPurchases. Các cột Charged* dưới đây là tổng các dòng
    // IsCharged, materialize lúc ApplyAggregates — list/dashboard chỉ đọc cột, không SUM lại.

    /// <summary>Σ SupplierRetailPrice của lần mua IsCharged.</summary>
    public decimal? ChargedSupplierRetailPrice { get; set; }

    /// <summary>Σ SupplierSubtotal (giá nhập) IsCharged.</summary>
    public decimal? ChargedSupplierSubtotal { get; set; }

    /// <summary>Σ SupplierShippingFee IsCharged.</summary>
    public decimal? ChargedSupplierShippingFee { get; set; }

    /// <summary>Σ SupplierRetailDeliveryFee IsCharged.</summary>
    public decimal? ChargedSupplierRetailDeliveryFee { get; set; }

    /// <summary>Σ SupplierOnTimeGuarantee IsCharged.</summary>
    public decimal? ChargedSupplierOnTimeGuarantee { get; set; }

    /// <summary>Σ SupplierCouponPrice IsCharged.</summary>
    public decimal? ChargedSupplierCouponPrice { get; set; }

    /// <summary>Σ SupplierMembershipDiscount IsCharged.</summary>
    public decimal? ChargedSupplierMembershipDiscount { get; set; }

    /// <summary>Σ SupplierPointsDiscount IsCharged.</summary>
    public decimal? ChargedSupplierPointsDiscount { get; set; }

    /// <summary>Σ SupplierWalletPrice IsCharged (ví cashback).</summary>
    public decimal? ChargedSupplierWalletPrice { get; set; }

    /// <summary>Σ SupplierTax IsCharged.</summary>
    public decimal? ChargedSupplierTax { get; set; }

    /// <summary>Σ SupplierTotal IsCharged — phần thẻ / totalPrice hóa đơn.</summary>
    public decimal? ChargedSupplierTotal { get; set; }

    /// <summary>
    /// Chi thật = ChargedSupplierTotal + ChargedSupplierWalletPrice + ChargedSupplierMembershipDiscount.
    /// Club cộng vào vì giảm membership chỉ có nhờ đã mua SHEIN Club.
    /// Dùng cho TrueSpend preview Mua hàng. TotalCost / Profit = gross NCC + BuyTrack + ThankCard.
    /// </summary>
    public decimal? ChargedTrueSpend { get; set; }

    /// <summary>Purchase / fulfil account used to buy this order.</summary>
    public Guid? FulfilAccountId { get; set; }

    /// <summary>Payment card used for this order.</summary>
    public Guid? FulfilCardId { get; set; }

    /// <summary>User who handled the order (for sales attribution); not necessarily the payer.</summary>
    public Guid? PaidByUserId { get; set; }

    /// <summary>Fulfil đang giữ đơn để đi mua (lease). Null = đơn tự do.</summary>
    public Guid? FulfilClaimedByUserId { get; set; }

    /// <summary>Lúc phiên đầu tiên của user claim đơn (UTC). Giữ nguyên khi phiên khác cùng user claim thêm.</summary>
    public DateTime? FulfilClaimedAt { get; set; }

    /// <summary>
    /// Hết hạn lease (UTC) = MAX ExpiresAt của các phiên đang giữ. Claim chỉ còn hiệu lực khi
    /// &gt; now VÀ <see cref="FulfilPurchasedAt"/> null — nên mọi luồng stamp mua tự vô hiệu claim.
    /// </summary>
    public DateTime? FulfilClaimExpiresAt { get; set; }

    /// <summary>
    /// Số phiên (Chrome profile) của <see cref="FulfilClaimedByUserId"/> đang giữ đơn.
    /// Denormalize từ <c>OrderFulfilAttempts</c> đang mở, ghi trong cùng transaction.
    /// Đơn chỉ trở lại pool khi về 0 — một người mở 3 profile thì nhả 1 profile chưa phải là nhả đơn.
    /// </summary>
    public int FulfilClaimSessionCount { get; set; }

    /// <summary>
    /// Whether shop payout for this order was marked received on the linked receiving bank card.
    /// When true via manual mark: a <c>ReceivingBankTransaction</c> with source SettlementPaid exists.
    /// When true via TikTok payout sync: linked by <see cref="TikTokPaymentId"/> (ledger is TikTokPayout row).
    /// </summary>
    public bool IsSettlementPaid { get; set; }

    /// <summary>
    /// TikTok payout <c>payment_id</c> when this order was included in a synced payout.
    /// </summary>
    public string? TikTokPaymentId { get; set; }

    /// <summary>
    /// Whether the customer paid on the marketplace (synced from presence of <see cref="OrderPaidTime"/>).
    /// Distinct from <see cref="IsSettlementPaid"/> (shop payout to receiving bank).
    /// </summary>
    public bool IsCustomerPaid { get; set; }

    /// <summary>Shipment number on 4Seller.</summary>
    public string? ShipmentNo { get; set; }

    /// <summary>Tracking number.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>Shipping method label.</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>Carrier / shipping service name.</summary>
    public string? ShippingService { get; set; }

    /// <summary>Buyer display name.</summary>
    public string? BuyerName { get; set; }

    /// <summary>Buyer phone.</summary>
    public string? BuyerPhone { get; set; }

    /// <summary>Buyer email.</summary>
    public string? BuyerEmail { get; set; }

    /// <summary>Ship-to address line.</summary>
    public string? Address { get; set; }

    /// <summary>Ship-to city.</summary>
    public string? City { get; set; }

    /// <summary>Ship-to state.</summary>
    public string? State { get; set; }

    /// <summary>Ship-to postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Ship-to country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Buyer memo.</summary>
    public string? BuyerMemo { get; set; }

    /// <summary>Seller memo.</summary>
    public string? SellerMemo { get; set; }

    /// <summary>Internal memo.</summary>
    public string? InternalMemo { get; set; }

    /// <summary>Order paid time (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? OrderPaidTime { get; set; }

    /// <summary>Order create time (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? OrderCreateTime { get; set; }

    /// <summary>Latest ship-by deadline (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? LatestShipDate { get; set; }

    /// <summary>Shipped time (4Seller sync or Mark Ship UTC now).</summary>
    public DateTime? OrderShippedTime { get; set; }

    /// <summary>Latest delivery deadline (UTC; 4Seller returns naive UTC strings).</summary>
    public DateTime? LatestDeliveryDate { get; set; }

    /// <summary>
    /// Local fulfil purchase time (when cost/account was recorded). Not from 4Seller.
    /// Used for purchase-speed KPIs: <c>FulfilPurchasedAt − OrderCreateTime</c>.
    /// </summary>
    public DateTime? FulfilPurchasedAt { get; set; }

    /// <summary>
    /// First time a non-empty tracking number was seen (Mark Ship or sync). Never cleared by sync.
    /// </summary>
    public DateTime? TrackingAddedAt { get; set; }

    /// <summary>Manual milestone: package in transit (UTC).</summary>
    public DateTime? InTransitAt { get; set; }

    /// <summary>Manual milestone: delivered to customer (UTC). Distinct from 4Seller <c>shipped</c>.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Manual milestone: refunded (UTC).</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>
    /// When USPS last-mile handling extras were marked done (manual). Null = chưa xử lý USPS.
    /// </summary>
    public DateTime? UspsProcessedAt { get; set; }

    /// <summary>
    /// PrintPackShip external batch id after a successful USPS track-scan push.
    /// Null = chưa đẩy scan qua API. Authoritative for fee source (batch price_per_label).
    /// </summary>
    public long? UspsScanBatchId { get; set; }

    /// <summary>When the USPS track-scan batch was successfully sent (UTC).</summary>
    public DateTime? UspsScanSentAt { get; set; }

    /// <summary>
    /// When Telegram was first notified for a newly inserted order (settings-gated new-order chat).
    /// Dedupes immediate notify + pending sweep; null until shop is resolved and send succeeds
    /// (or the pending window expires without a resolvable shop).
    /// </summary>
    public DateTime? TelegramNewOrderNotifiedAt { get; set; }

    /// <summary>
    /// When Telegram was first notified that this order needs USPS BuyTrack handling.
    /// Dedupes realtime fulfil-handling messages.
    /// </summary>
    public DateTime? TelegramUspsNotifiedAt { get; set; }

    /// <summary>
    /// BuyTrack (USPS scan track) fee currently applied to purchase cost. 0 when not applied.
    /// From PrintPackShip <c>price_per_label</c> when pushed via scan batch; otherwise Settings
    /// <c>order.handling.buy_track</c>. Shown as Track column on ledger CostUpdate rows (not a Manual Debit).
    /// </summary>
    public decimal BuyTrackAmount { get; set; }

    /// <summary>
    /// When ThankCard was marked purchased (manual). Null = chưa mua.
    /// </summary>
    public DateTime? ThankCardProcessedAt { get; set; }

    /// <summary>
    /// When ops marked TikTok REFUND handling done (manual). Null = chờ xử lý refund.
    /// Distinct from <see cref="RefundedAt"/> (4Seller commerce clock).
    /// </summary>
    public DateTime? RefundProcessedAt { get; set; }

    /// <summary>
    /// When Telegram was first notified that Shein warehouse / local tracking appeared.
    /// Dedupes realtime fulfil-handling messages.
    /// </summary>
    public DateTime? TelegramWarehouseNotifiedAt { get; set; }

    /// <summary>
    /// When Telegram was first notified that this purchased order still has no last-mile
    /// (carrier) tracking after the configured N-hour threshold.
    /// </summary>
    public DateTime? TelegramStaleNoTrackingNotifiedAt { get; set; }

    /// <summary>
    /// When the backend detail sweep first saw real carrier tracking for this order but could not
    /// send the Telegram "Add track" message yet (Mark Ship still pending, or notify/chat/bot
    /// not ready). Cleared when <see cref="TelegramAddTrackNotifiedAt"/> is set.
    /// </summary>
    public DateTime? TelegramAddTrackNotifyPendingAt { get; set; }

    /// <summary>
    /// When Telegram was successfully notified for the SHEIN detail "Add track" event.
    /// Dedupes so a deferred Mark Ship still gets one message on a later <c>detail-</c> pass.
    /// </summary>
    public DateTime? TelegramAddTrackNotifiedAt { get; set; }

    /// <summary>
    /// ThankCard fee currently applied to purchase cost. 0 when not applied.
    /// Derived from <see cref="ThankCardProcessedAt"/> + Settings; shown as ThankCard column on ledger CostUpdate rows (not a Manual Debit).
    /// </summary>
    public decimal ThankCardAmount { get; set; }

    /// <summary>
    /// First time local status entered canceled/cancelled (sync or insert). Never cleared by sync.
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// Per-milestone provenance for the fulfil timeline (jsonb), e.g.
    /// <c>{"inTransitAt":"shein","deliveredAt":"shein"}</c>. Drives source-rank arbitration in
    /// <c>OrderFulfilTimeline</c> so a higher-ranked / later re-sync can overwrite legacy values.
    /// </summary>
    public string? FulfilTimelineSources { get; set; }

    /// <summary>Package weight.</summary>
    public decimal? PackageWeight { get; set; }

    /// <summary>Package weight unit (oz, lb, g, kg).</summary>
    public string? PackageWeightUnit { get; set; }

    /// <summary>Package length.</summary>
    public decimal? PackageLength { get; set; }

    /// <summary>Package width.</summary>
    public decimal? PackageWidth { get; set; }

    /// <summary>Package height.</summary>
    public decimal? PackageHeight { get; set; }

    /// <summary>Dimension unit (in, cm).</summary>
    public string? PackageDimensionUnit { get; set; }

    /// <summary>
    /// Materialized <see cref="Domain.Orders.OrderWorkflowTabs.Resolve"/> result so list filters and
    /// badge counts are an index scan instead of a correlated EXISTS over SupplierPurchases.
    ///
    /// Maintained in exactly one place — the <c>SaveChangesAsync</c> override recomputes it from
    /// <see cref="PlatformOrderStatus"/>, <see cref="FulfilPurchasedAt"/>, <see cref="FulfilAccountId"/>,
    /// <see cref="TrackingNo"/>, <see cref="ShippingService"/> and the order's supplier links.
    /// Never set it by hand.
    /// </summary>
    public string? WorkflowTab { get; set; }

    /// <summary>
    /// Denormalized <see cref="Domain.Orders.OrderWorkflowTabs.HasCarrierTracking"/>. See
    /// <see cref="WorkflowTab"/> for the write rule.
    /// </summary>
    public bool HasCarrierTracking { get; set; }

    /// <summary>Denormalized "has at least one supplier link". See <see cref="WorkflowTab"/>.</summary>
    public bool HasSupplierPurchase { get; set; }

    /// <summary>
    /// Denormalized USPS last-mile signal (order shipping service or any link carrier mentions USPS).
    /// Combined with <see cref="UspsProcessedAt"/> for the USPS ops tabs. See <see cref="WorkflowTab"/>.
    /// </summary>
    public bool HasUspsSignal { get; set; }

    /// <summary>
    /// Denormalized SHEIN warehouse / local tracking signal. Combined with
    /// <see cref="ThankCardProcessedAt"/> for the Thank Card tabs. See <see cref="WorkflowTab"/>.
    /// </summary>
    public bool HasWarehouseSignal { get; set; }

    /// <summary>Optional raw 4Seller JSON snapshot.</summary>
    public string? RawPayload { get; set; }

    /// <summary>Last successful sync from 4Seller.</summary>
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owner navigation.</summary>
    public ApplicationUser Owner { get; set; } = null!;

    /// <summary>Shop navigation.</summary>
    public Shop? Shop { get; set; }

    /// <summary>Fulfil purchase account navigation.</summary>
    public FulfilAccount? FulfilAccount { get; set; }

    /// <summary>Fulfil payment card navigation.</summary>
    public FulfilCard? FulfilCard { get; set; }

    /// <summary>Payer navigation.</summary>
    public ApplicationUser? PaidByUser { get; set; }

    /// <summary>Fulfil currently holding this order (lease).</summary>
    public ApplicationUser? FulfilClaimedByUser { get; set; }

    /// <summary>Line items.</summary>
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    /// <summary>
    /// Mọi lần mua NCC đã gắn vào đơn sàn này (1 TikTok → N billno SHEIN), kể cả dòng phí nhập tay.
    /// Giá mua (TrueSpend) đọc từ <c>Orders.ChargedTrueSpend</c> (materialize lúc ApplyAggregates).
    /// </summary>
    public ICollection<SupplierPurchase> SupplierPurchases { get; set; } = new List<SupplierPurchase>();
}
