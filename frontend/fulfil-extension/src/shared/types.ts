/** Trạng thái đăng nhập kinhbac lưu trong storage. */
export interface AuthState {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  savedAt: string;
  user: {
    id: string;
    username: string;
    fullName?: string;
    /** Roles từ /auth/me — dùng để phân biệt Admin (thấy mọi đơn đã mua) vs Fulfil. */
    roles?: string[];
  } | null;
}

/** Nguồn sản phẩm (từ /product-sources/options). */
export interface ProductSourceOption {
  id: string;
  code: string;
  name: string;
  logoUrl?: string;
  domains?: string;
}

/** Line item đơn cần mua. */
export interface FulfilOrderItem {
  id: string;
  sellerSku?: string | null;
  title?: string | null;
  imgUrl?: string | null;
  sourceImgUrl?: string | null;
  listingUrl?: string | null;
  sourceUrl?: string | null;
  variantAttr?: string | null;
  quantity: number;
}

export interface FulfilIssueTag {
  id: string;
  label: string;
  isSystem: boolean;
}

/** Đơn trong hàng đợi mua. */
export interface FulfilOrder {
  id: string;
  platformOrderId?: string | null;
  platform?: string | null;
  orderStatus: string;
  shopName?: string | null;
  shopProfileName?: string | null;
  /** Tên seller (owner). */
  ownerName?: string | null;
  /** Số tiền đơn (fallback Rev khi chưa enrich). */
  amount?: number | null;
  /** Số tiền khách đã trả trên sàn. */
  totalPaidAmount?: number | null;
  /** Thực nhận seller (Rev) sau khi enrich breakdown TikTok. */
  sellerNetAmount?: number | null;
  currency?: string | null;
  buyerName?: string | null;
  buyerPhone?: string | null;
  buyerEmail?: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  countryCode?: string | null;
  buyerMemo?: string | null;
  sellerMemo?: string | null;
  internalMemo?: string | null;
  /** Thông báo vận hành local (denormalize từ issue tags). */
  message?: string | null;
  /** Attached issue tags. */
  issueTags?: FulfilIssueTag[];
  /** None | Processing | Done */
  issueStatus?: string | null;
  fulfilPurchasedAt?: string | null;
  /** Tên tài khoản mua hàng (FulfilAccounts.Name) khi đã map. */
  fulfilAccountName?: string | null;
  /** Tên fulfil đã stamp mua (PaidByUser) — Admin xem trên tab Đã mua. */
  paidByUserName?: string | null;
  /**
   * Đã gắn ≥1 billno SHEIN (`SupplierPurchases.SupplierOrderNo`).
   * Khi true: không hiện nút «Chưa mua» (không cho gỡ stamp tay).
   */
  hasSupplierBillno?: boolean;
  /**
   * Lease hiện tại (null / thiếu = đơn tự do hoặc claim đã hết hạn).
   * Server chỉ trả field khi claim còn hiệu lực.
   */
  claim?: FulfilOrderClaim | null;
  lineItems: FulfilOrderItem[];
  orderCreateTime?: string | null;
  orderPaidTime?: string | null;
}

/** Snapshot lease trên đơn cần mua. */
export interface FulfilOrderClaim {
  userId: string;
  userName?: string | null;
  claimedAt?: string | null;
  expiresAt?: string | null;
  sessionCount: number;
  sessionIds: string[];
  /** Phiên Chrome profile hiện tại đang giữ. */
  heldByThisSession: boolean;
  /** Cùng account nhưng profile khác đang giữ. */
  heldByMyOtherSession: boolean;
}

/** Pending auto-select size/sku trên PDP (SHEIN). */
export interface PendingSelect {
  goodsId?: string;
  skuCode?: string;
  sizeLabel?: string | null;
  colorLabel?: string | null;
  variantAttr?: string | null;
  at: number;
}

/** Match giữa tab đang mở và một line item trong queue. */
export interface ActiveMatch {
  orderId: string;
  itemId: string;
  order: FulfilOrder;
  item: FulfilOrderItem;
  tabUrl: string;
  sourceName?: string;
  productId?: string;
  /**
   * User vừa chọn đơn/item thủ công (click line trong list).
   * Trong TTL, GET_ACTIVE_TAB_STATUS không được ghi đè bằng findMatchForUrl
   * (tránh race lúc tab chưa kịp navigate / nhiều đơn trùng goodsId).
   */
  userPinnedAt?: number;
}

/** Kết quả khớp biến thể trên PDP (content ghi storage). */
export interface VariantFitSnapshot {
  fit: 'ok' | 'bad' | 'warn';
  detail: string;
  orderId?: string;
  itemId?: string;
  sizeSel?: string | null;
  colorSel?: string | null;
  at: number;
}

/** Tổng thanh toán đọc từ DOM checkout SHEIN (giá thực trả). */
export interface CheckoutCostSnapshot {
  amount: number;
  currency?: string;
  raw?: string;
  orderId?: string;
  itemId?: string;
  tabUrl?: string;
  at: number;
}
