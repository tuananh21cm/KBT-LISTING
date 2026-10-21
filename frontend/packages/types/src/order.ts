export interface OrderItem {
  id: string;
  fourSellerOrderItemId: number;
  sellerSku?: string | null;
  title?: string | null;
  imgUrl?: string | null;
  sourceImgUrl?: string | null;
  listingUrl?: string | null;
  sourceUrl?: string | null;
  variantAttr?: string | null;
  quantity: number;
  itemPrice: number;
  currency?: string | null;
  /** Listing overlay (OwnerId + SellerSku) — not stored on OrderItem */
  averageRating?: number | null;
  reviewCount?: number | null;
  /** Shop NCC (overlay listing → SupplierSellerInfos). */
  storeTitle?: string | null;
  storeUrl?: string | null;
  /** Rank shop NCC (Shein shopRank). */
  shopRank?: number | null;
  /** Follow shop NCC (Shein storeAttention). */
  storeAttention?: number | null;
  /** SLA ngày giao shop NCC. */
  shippingDays?: number | null;
  /** % đơn giao trong SLA. */
  shippingDayPercent?: number | null;
  /** Hàng local US. */
  isLocalProduct?: boolean | null;
  /** Ít nhất một SKU mall_stock hỗ trợ ship nhanh. */
  isSupportQuickShip?: boolean | null;
}

/** Reusable / assigned issue tag. */
export interface IssueTag {
  id: string;
  label: string;
  isSystem: boolean;
}

export interface CreateIssueTagRequest {
  label: string;
}

export interface UpdateIssueTagRequest {
  label: string;
}

export interface SetOrderIssueTagsRequest {
  tagIds: string[];
}

export interface SetOrderIssueStatusRequest {
  /** Processing | Done */
  status: string;
}

export type OrderIssueStatus = 'None' | 'Processing' | 'Done';

export interface OrderListItem {
  id: string;
  fourSellerOrderId: number;
  platformOrderId?: string | null;
  platform?: string | null;
  orderStatus: string;
  platformOrderStatus?: string | null;
  /** TikTok / 4Seller cancel or refund reason */
  cancelReason?: string | null;
  /** Human-readable after-sales status from 4Seller returnStatus (e.g. "Awaiting customer return") */
  returnStatus?: string | null;
  /** Operational notice under the status line (local; denormalized from issue tags) */
  message?: string | null;
  /** Attached issue tags */
  issueTags?: IssueTag[];
  /** None | Processing | Done */
  issueStatus?: string | null;
  /** Composite workflow tab: PENDING_PROCESS | PURCHASED | AWAITING_COLLECTION_GROUP | IN_TRANSIT | DELIVERED | CANCELLED | REFUND */
  workflowTab?: string | null;
  amount: number;
  totalPaidAmount: number;
  /** Product amount before discounts (TikTok breakdown) */
  productAmountBeforeDiscount?: number | null;
  /** Platform-funded product discount */
  productPlatformDiscount?: number | null;
  /** Seller-funded product discount */
  productSellerDiscount?: number | null;
  /** Shipping fee before discounts */
  shippingFeeBeforeDiscount?: number | null;
  /** Seller-funded shipping discount */
  shippingFeeSellerDiscount?: number | null;
  /** Platform tax from orderAmountVo */
  platformTotalTax?: number | null;
  /** Other platform fees */
  platformOtherFee?: number | null;
  /** Seller net payout (system Rev when present) */
  sellerNetAmount?: number | null;
  /** When pricing detail was enriched from 4Seller */
  pricingDetailSyncedAt?: string | null;
  currency: string;
  /** @deprecated Local unit cost — use supplierSubtotal / supplierTotal */
  cost?: number | null;
  /** @deprecated Local shipping — use supplierShippingFee */
  shippingFee?: number | null;
  /** @deprecated Local tax — use supplierTax */
  tax?: number | null;
  /** @deprecated Local ads — unused */
  ads?: number | null;
  /** Supplier platform order number (SHEIN billno, Temu id, …) */
  supplierOrderNo?: string | null;
  /** Supplier order detail page URL */
  supplierOrderUrl?: string | null;
  /** Supplier retail (list) price */
  supplierRetailPrice?: number | null;
  /** Supplier subtotal (giá nhập NCC) */
  supplierSubtotal?: number | null;
  /** Supplier shipping fee */
  supplierShippingFee?: number | null;
  /** SHEIN retail delivery fee */
  supplierRetailDeliveryFee?: number | null;
  /** Optional on-time / delivery-guarantee fee */
  supplierOnTimeGuarantee?: number | null;
  /** Coupon/promo discount (positive = amount reduced) */
  supplierCouponPrice?: number | null;
  /** Membership/club savings (positive = amount saved) */
  supplierMembershipDiscount?: number | null;
  /** Loyalty points redeemed (SHEIN Points; positive = amount reduced) */
  supplierPointsDiscount?: number | null;
  /** SHEIN wallet / cashback used (payment, not coupon) */
  supplierWalletPrice?: number | null;
  /** Supplier sales tax */
  supplierTax?: number | null;
  /** Supplier grand total charged */
  supplierTotal?: number | null;
  /** Sum of line-item quantities */
  totalQuantity?: number;
  /**
   * Chi phí mua hàng / Profit card = gross NCC (subtotal+ship+tax+…) + BuyTrack + ThankCard.
   * Profit = Revenue − totalCost. TrueSpend (thẻ+ví+club) dùng riêng cho preview Mua hàng.
   */
  totalCost?: number | null;
  trackingNo?: string | null;
  shipmentNo?: string | null;
  shippingMethod?: string | null;
  shippingService?: string | null;
  orderCreateTime?: string | null;
  orderPaidTime?: string | null;
  latestShipDate?: string | null;
  /** Local: when purchase/cost was recorded */
  fulfilPurchasedAt?: string | null;
  /** First time tracking became non-empty */
  trackingAddedAt?: string | null;
  /** Manual: in transit */
  inTransitAt?: string | null;
  /** Manual: delivered to customer (≠ 4Seller shipped) */
  deliveredAt?: string | null;
  /** Manual: refunded */
  refundedAt?: string | null;
  /** Manual: USPS last-mile extra handling completed */
  uspsProcessedAt?: string | null;
  /** PrintPackShip batch id after successful USPS scan push */
  uspsScanBatchId?: number | null;
  /** When the USPS scan batch was successfully sent (UTC ISO) */
  uspsScanSentAt?: string | null;
  /** BuyTrack fee currently applied (USD). 0 when not applied. */
  buyTrackAmount?: number;
  /** Manual: ThankCard purchased */
  thankCardProcessedAt?: string | null;
  /** ThankCard fee currently applied (USD). 0 when not applied. */
  thankCardAmount?: number;
  /** Manual: ops marked TikTok REFUND handling done */
  refundProcessedAt?: string | null;
  /** First time status entered canceled */
  canceledAt?: string | null;
  lastSyncedAt: string;
  ownerId: string;
  ownerName?: string | null;
  shopId?: string | null;
  shopName?: string | null;
  shopProfileName?: string | null;
  buyerName?: string | null;
  buyerPhone?: string | null;
  buyerEmail?: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  countryCode?: string | null;
  packageWeight?: number | null;
  packageWeightUnit?: string | null;
  packageLength?: number | null;
  packageWidth?: number | null;
  packageHeight?: number | null;
  packageDimensionUnit?: string | null;
  itemCount: number;
  lineItems: OrderItem[];
  buyerMemo?: string | null;
  sellerMemo?: string | null;
  internalMemo?: string | null;
  fulfilAccountId?: string | null;
  fulfilAccountName?: string | null;
  fulfilCardId?: string | null;
  fulfilCardName?: string | null;
  fulfilCardNumberMasked?: string | null;
  paidByUserId?: string | null;
  paidByUserName?: string | null;
  /** Fulfil currently holding this order (lease). */
  fulfilClaimedByUserId?: string | null;
  fulfilClaimedByName?: string | null;
  fulfilClaimedAt?: string | null;
  fulfilClaimExpiresAt?: string | null;
  fulfilClaimSessionCount?: number;
  /** Shop payout marked received on linked receiving bank card */
  isSettlementPaid?: boolean;
  /** Customer paid on marketplace (synced from orderPaidTime) */
  isCustomerPaid?: boolean;
  /** All SHEIN/Temu billnos linked to this platform order (Joint Shipment). */
  supplierLinks?: OrderSupplierLink[];
}

/** One supplier purchase linked to a local order. */
export interface OrderSupplierLink {
  id: string;
  supplierOrderNo: string;
  relationBillno?: string | null;
  confluenceBatchNo?: string | null;
  supplierOrderUrl?: string | null;
  trackingNo?: string | null;
  /** Last SHEIN warehouse C…/N… kept after carrier upgrade. */
  warehouseTrackingNo?: string | null;
  warehouseTrackingSeenAt?: string | null;
  carrier?: string | null;
  /** none | warehouse | carrier */
  trackingKind?: string;
  supplierTotal?: number | null;
  supplierRetailPrice?: number | null;
  supplierSubtotal?: number | null;
  supplierShippingFee?: number | null;
  supplierRetailDeliveryFee?: number | null;
  supplierOnTimeGuarantee?: number | null;
  supplierCouponPrice?: number | null;
  supplierMembershipDiscount?: number | null;
  supplierPointsDiscount?: number | null;
  supplierWalletPrice?: number | null;
  supplierTax?: number | null;
  /** processing | shipped | delivered | canceled | refunded */
  supplierStatus?: string | null;
  supplierStatusRaw?: string | null;
  supplierStatusAt?: string | null;
  /** true khi SHEIN xác nhận chưa thanh toán — không tính vào giá mua. */
  supplierUnpaid?: boolean;
  /** true khi dòng này được cộng vào Charged* / totalCost. */
  isCharged?: boolean;
  /** Carrier tracking last evaluated for auto Mark Ship. */
  autoMarkShipTrackingNo?: string | null;
  /** pending | succeeded | failed | blocked | unconfirmed */
  autoMarkShipStatus?: string | null;
  autoMarkShipAttemptedAt?: string | null;
  /** Auto Mark Ship error/blocked reason — show when order still has no published tracking. */
  autoMarkShipError?: string | null;
  /** 4Seller async request id — used to reconcile unconfirmed Mark Ship. */
  autoMarkShipRequestId?: string | null;
}

/**
 * Cost breakdown of one billno inside the hover preview. Mỗi dòng khớp 1-1 với trang
 * đơn của NCC; số tổng phía trên thì không, khi đơn phải mua bằng nhiều billno.
 */
export interface OrderHoverPreviewSupplierLink {
  supplierOrderNo: string;
  supplierOrderUrl?: string | null;
  supplierStatus?: string | null;
  /** true khi SHEIN xác nhận chưa thanh toán — không tính vào giá mua. */
  supplierUnpaid?: boolean;
  /** true khi dòng này được cộng vào giá mua. */
  isCharged?: boolean;
  /** Mã vận đơn lần mua (warehouse hoặc last-mile) */
  trackingNo?: string | null;
  /** Last SHEIN warehouse C…/N… kept after carrier upgrade */
  warehouseTrackingNo?: string | null;
  warehouseTrackingSeenAt?: string | null;
  /** Carrier từ SHEIN track_info.name (USPS / FedEx / …) */
  carrier?: string | null;
  /** none | warehouse | carrier */
  trackingKind?: string | null;
  supplierRetailPrice?: number | null;
  supplierSubtotal?: number | null;
  supplierShippingFee?: number | null;
  supplierRetailDeliveryFee?: number | null;
  supplierOnTimeGuarantee?: number | null;
  supplierCouponPrice?: number | null;
  supplierMembershipDiscount?: number | null;
  supplierPointsDiscount?: number | null;
  supplierWalletPrice?: number | null;
  supplierTax?: number | null;
  supplierTotal?: number | null;
  /** true khi subtotal + total đều có — breakdown tin cậy. */
  costDetailComplete?: boolean;
}

export interface OrderDetail extends OrderListItem {
  orderShippedTime?: string | null;
  latestDeliveryDate?: string | null;
  items: OrderItem[];
}

/** Lightweight hover preview — GET /admin/orders/{id}/preview */
export interface OrderHoverPreviewItem {
  title?: string | null;
  imgUrl?: string | null;
  quantity: number;
  itemPrice: number;
  sellerSku?: string | null;
  variantAttr?: string | null;
}

export interface OrderHoverPreview {
  id: string;
  platformOrderId?: string | null;
  platformOrderStatus?: string | null;
  currency: string;
  shippingMethod?: string | null;
  shippingService?: string | null;
  /** Mã vận đơn đã publish lên sàn (sau Mark Ship) */
  trackingNo?: string | null;
  pricingDetailSyncedAt?: string | null;
  /** System Rev */
  sellAmount: number;
  totalPaidAmount: number;
  productAmountBeforeDiscount?: number | null;
  productPlatformDiscount?: number | null;
  productSellerDiscount?: number | null;
  shippingFeeBeforeDiscount?: number | null;
  shippingFeeSellerDiscount?: number | null;
  platformTotalTax?: number | null;
  platformOtherFee?: number | null;
  /**
   * TrueSpend = thẻ + ví + club (NCC only — không gồm BuyTrack/ThankCard).
   * Khớp Orders.ChargedTrueSpend; khác Orders.totalCost khi có phí xử lý.
   */
  costAmount?: number | null;
  /** Alias preview cho TrueSpend (= costAmount), không gồm BuyTrack/ThankCard. */
  totalCost?: number | null;
  supplierRetailPrice?: number | null;
  supplierSubtotal?: number | null;
  supplierShippingFee?: number | null;
  supplierRetailDeliveryFee?: number | null;
  supplierOnTimeGuarantee?: number | null;
  supplierCouponPrice?: number | null;
  supplierMembershipDiscount?: number | null;
  supplierPointsDiscount?: number | null;
  supplierWalletPrice?: number | null;
  supplierTax?: number | null;
  /** Số billno NCC được cộng vào các số NCC ở trên (Joint Shipment) */
  supplierOrderCount?: number;
  /** Breakdown từng billno được cộng, cũ → mới */
  supplierLinks?: OrderHoverPreviewSupplierLink[];
  totalQuantity: number;
  itemCount: number;
  items: OrderHoverPreviewItem[];
}

export interface OrderListPage {
  items: OrderListItem[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  /**
   * Pass back as `cursor` to fetch the next page without an OFFSET scan. Null on the last page and
   * on views the cursor cannot express (`purchasedToday`, tab `PURCHASED`). Page numbers keep working — opt-in.
   */
  nextCursor?: string | null;
  /** Always null: counts come from `GET …/orders/status-counts`. */
  statusCounts?: OrderStatusCounts | null;
}

export interface OrderStatusCounts {
  all: number;
  /** Chờ xử lý */
  pendingProcess: number;
  /** Đã mua */
  purchased: number;
  /** Chờ lấy (AWAITING_COLLECTION_GROUP) */
  awaitingCollection: number;
  /** Đang giao (IN_TRANSIT) */
  inTransit: number;
  delivered: number;
  canceled: number;
  refund: number;
  /** USPS last-mile chưa xử lý (uspsProcessedAt null) */
  uspsUnprocessed: number;
  /** USPS last-mile đã stamp */
  uspsProcessed?: number;
  /** Thank Card còn việc */
  thankCardPending?: number;
  /** Thank Card đã stamp */
  thankCardProcessed?: number;
  /** Refund TikTok chưa xử lý ops */
  refundUnprocessed?: number;
  /** Refund TikTok đã stamp ops */
  refundProcessed?: number;
  /** Đơn hủy chưa xử lý ops */
  cancelUnprocessed?: number;
  /** Đơn hủy đã stamp ops */
  cancelProcessed?: number;
  /** Queue USPS/Thank Card — còn việc (legacy combined) */
  uspsThankCardPending?: number;
  /** Queue USPS/Thank Card — đã xử lý đủ (legacy combined) */
  uspsThankCardProcessed?: number;
  /** Chờ mua — đơn trong ngày VN */
  pendingNew?: number;
  /** Chờ mua — có Message / Out Stock (Hết hàng) */
  pendingOutStock?: number;
  /** Chờ mua — tuổi 0–&lt;24 giờ */
  pending24h?: number;
  /** Chờ mua — tuổi 24–&lt;48 giờ */
  pending48h?: number;
  /** Chờ mua — tuổi ≥48 giờ (quá 48h) */
  pendingOver48h?: number;
  /** @deprecated Prefer pendingProcess */
  onHold?: number;
  /** @deprecated Prefer pendingProcess */
  awaitingShipment?: number;
}

export interface OrderListParams {
  pageCurrent?: number;
  pageSize?: number;
  platformOrderStatus?: string;
  ownerUserId?: string;
  platform?: string;
  shopId?: string;
  search?: string;
  /** Inclusive lower bound (ISO); filters OrderPaidTime ?? OrderCreateTime */
  dateFrom?: string;
  /** Inclusive upper bound (ISO) */
  dateTo?: string;
  /** Only orders with FulfilPurchasedAt null */
  needsPurchase?: boolean;
  /**
   * Only orders whose FulfilPurchasedAt falls in today's Vietnam calendar day.
   * Takes precedence over needsPurchase. Used by fulfil-extension tab «Đã mua».
   */
  purchasedToday?: boolean;
  /** Filter by product source domains matching OrderItem.SourceUrl */
  productSourceId?: string;
  /**
   * Sub-filter on `/orders/pending` (with `PENDING_PROCESS`):
   * `new` | `out_stock` | `24h` | `48h` | `over_48h`. Omit = tất cả chờ mua.
   */
  pendingFilter?: string;
  /**
   * Keyset cursor from the previous page's `nextCursor`. Ignores `pageCurrent` when set, and is
   * only valid while the sort and filters stay identical.
   */
  cursor?: string;
}

export interface OrderSyncRequest {
  ownerUserId?: string;
  orderStatuses?: string[];
  /** Inclusive lower bound (ISO); forwarded to 4Seller dateQuery* when set with dateTo */
  dateFrom?: string;
  /** Inclusive upper bound (ISO) */
  dateTo?: string;
  /**
   * When true with dateFrom/dateTo: only apply create-date filter to shipped/canceled.
   * Open buckets sync without date filter (status updates on older open orders).
   */
  openStatusesIgnoreDateFilter?: boolean;
}

export interface OrderSyncSellerFailure {
  ownerUserId: string;
  ownerName?: string | null;
  message: string;
}

export interface OrderSyncResult {
  syncedSellers: number;
  inserted: number;
  updated: number;
  failedSellers: OrderSyncSellerFailure[];
}

/** Lifecycle of a manual order sync job (POST …/sync → poll …/sync/status/{jobId}). */
export type OrderSyncJobState = 'running' | 'completed' | 'failed';

/**
 * Snapshot of a detached manual order sync. Start returns 202 with this shape; poll until
 * state is not `running` so Cloudflare cannot 524 a long-held connection.
 */
export interface OrderSyncJobStatus {
  jobId: string;
  state: OrderSyncJobState | string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  syncedSellers: number;
  inserted: number;
  updated: number;
  failedSellers: OrderSyncSellerFailure[];
  message?: string | null;
  error?: string | null;
}

export interface OrderAfterSalesSyncRequest {
  ownerUserId?: string;
}

/**
 * Result of the after-sales (return / refund) sync — the only flow that sets or releases the
 * REFUND status.
 */
export interface OrderAfterSalesSyncResult {
  syncedSellers: number;
  returnsRead: number;
  ordersUpdated: number;
  refunded: number;
  refundReverted: number;
  unmatchedReturns: number;
  notified: number;
  /** True when another 4Seller sync held the write gate and this run did nothing. */
  skipped: boolean;
  failedSellers: OrderSyncSellerFailure[];
}

export interface ShippingServiceOption {
  id: number;
  carrier: string;
  platform?: string | null;
  region?: string | null;
}

export interface MarkShipRequest {
  shippingService: string;
  trackingNo: string;
}

export interface UpdateOrderCostRequest {
  /** Supplier platform order number */
  supplierOrderNo?: string | null;
  /** Supplier retail (list) price */
  supplierRetailPrice?: number | null;
  /** Supplier subtotal (giá nhập NCC) */
  supplierSubtotal?: number | null;
  /** Supplier shipping fee */
  supplierShippingFee?: number | null;
  /** SHEIN retail delivery fee */
  supplierRetailDeliveryFee?: number | null;
  /** Optional on-time / delivery-guarantee fee */
  supplierOnTimeGuarantee?: number | null;
  /** Coupon/promo discount (positive = amount reduced) */
  supplierCouponPrice?: number | null;
  /** Membership/club savings (positive = amount saved) */
  supplierMembershipDiscount?: number | null;
  /** Loyalty points redeemed (positive = amount reduced) */
  supplierPointsDiscount?: number | null;
  /** SHEIN wallet / cashback used (payment, not coupon) */
  supplierWalletPrice?: number | null;
  /** Supplier sales tax */
  supplierTax?: number | null;
  /** Supplier grand total charged (ledger Amount) */
  supplierTotal?: number | null;
  fulfilAccountId?: string | null;
  fulfilCardId?: string | null;
  paidByUserId?: string | null;
  /** When purchase was made (ISO); omit to keep / default UtcNow on first fulfil */
  fulfilPurchasedAt?: string | null;
}

/** Partial milestone update — only fulfilPurchasedAt / trackingAddedAt are applied.
 * inTransitAt / deliveredAt / refundedAt are rejected by API (sync-owned). */
export interface UpdateOrderMilestonesRequest {
  fulfilPurchasedAt?: string | null;
  trackingAddedAt?: string | null;
  /** Undo accidental purchase stamp. */
  clearFulfilPurchasedAt?: boolean;
  /** Stamp even if another fulfil holds the lease. */
  force?: boolean;
  /** @deprecated Rejected by API */
  inTransitAt?: string | null;
  /** @deprecated Rejected by API */
  deliveredAt?: string | null;
  /** @deprecated Rejected by API */
  refundedAt?: string | null;
}

export interface UpdateOrderSettlementRequest {
  isSettlementPaid: boolean;
}

export interface UpdateOrderUspsProcessedRequest {
  isUspsProcessed: boolean;
  /** Default true. false = stamp only, do not set BuyTrackAmount. */
  applyFee?: boolean;
}

/** Bulk push USPS tracking numbers to PrintPackShip. */
export interface PushUspsScanRequest {
  orderIds: string[];
  notes?: string | null;
}

export interface UspsScanSkippedOrder {
  orderId: string;
  platformOrderId?: string | null;
  trackingNo?: string | null;
  reason: string;
}

export interface UspsScanPushResult {
  batchId?: number | null;
  status: string;
  sentCount: number;
  pricePerLabel?: number | null;
  scanFee?: number | null;
  totalCharged?: number | null;
  balanceCurrent?: number | null;
  pagesFailed: number;
  skipped: UspsScanSkippedOrder[];
  error?: string | null;
}

export interface UpdateOrderThankCardProcessedRequest {
  isThankCardProcessed: boolean;
  /** Default true. false = stamp only, do not set ThankCardAmount. */
  applyFee?: boolean;
}

export interface UpdateOrderRefundProcessedRequest {
  isRefundProcessed: boolean;
}

export interface UpdateOrderNotesRequest {
  buyerMemo?: string | null;
  sellerMemo?: string | null;
  internalMemo?: string | null;
}

export interface UpdateOrderMessageRequest {
  /** Legacy: maps to issue-tag replace / clear */
  message?: string | null;
}

export interface MarkShipResult {
  success: boolean;
  message?: string | null;
  trackingNo?: string | null;
  requestId?: string | null;
  platformOrderStatus?: string | null;
}

/** Outcome of POST …/orders/{id}/sync-platform-status (4Seller commerce status by orderNo). */
export interface OrderPlatformStatusSyncResult {
  found: boolean;
  changed: boolean;
  previousPlatformOrderStatus?: string | null;
  platformOrderStatus?: string | null;
  workflowTab?: string | null;
  trackingNo?: string | null;
  summary: string;
}

/** Outcome of POST …/orders/{id}/sync-track (SHEIN detail + cost + Mark Ship). */
export interface SheinOrderSyncTrackBillnoResult {
  billno: string;
  fulfilAccountName?: string | null;
  fetchOk: boolean;
  skipReason?: string | null;
  error?: string | null;
  updatedTracking: boolean;
  trackingNo?: string | null;
  trackingKind?: string | null;
  carrier?: string | null;
  updatedCost: boolean;
  supplierTotal?: number | null;
  supplierSubtotal?: number | null;
  costIncomplete: boolean;
  autoMarkShipStatus?: string | null;
  autoMarkShipError?: string | null;
}

export interface SheinOrderSyncTrackResult {
  gateBusy?: boolean;
  orderId: string;
  fetched: number;
  updatedTracking: number;
  updatedCost: number;
  autoShipped: number;
  autoShipFailed: number;
  autoShipBlocked: number;
  billnos: SheinOrderSyncTrackBillnoResult[];
  notes: string[];
  summary: string;
}
