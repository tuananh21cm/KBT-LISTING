export const FULFIL_CARD_BRANDS = ['Visa', 'Master'] as const;
export type FulfilCardBrand = (typeof FULFIL_CARD_BRANDS)[number];

export const FULFIL_TXN_SOURCES = ['CostUpdate', 'Manual'] as const;
export type FulfilTxnSource = (typeof FULFIL_TXN_SOURCES)[number];

export interface FulfilAccount {
  id: string;
  name: string;
  username?: string | null;
  password?: string | null;
  email?: string | null;
  passMail?: string | null;
  twoFactorAuth?: string | null;
  assigneeUserIds: string[];
  assigneeUserNames: string[];
  isActive: boolean;
  activeCardId?: string | null;
  activeCardName?: string | null;
  activeCardNumberMasked?: string | null;
  activeCardSourceName?: string | null;
  activeCardSourceAccountName?: string | null;
  activeCardCurrency?: string | null;
  cardCount: number;
  /** Proxy egress backend dùng khi tự gọi SHEIN cho tài khoản này. */
  sheinProxy?: string | null;
  sheinOrigin?: string | null;
  sheinHeaderCount: number;
  sheinHasCookie: boolean;
  /** Đủ header bắt buộc + cookie để backend gọi SHEIN thay trình duyệt. */
  sheinSessionReady: boolean;
  sheinMissingHeaders: string[];
  sheinSessionCapturedAt?: string | null;
  sheinSessionLastCheckedAt?: string | null;
  sheinSessionLastError?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateFulfilAccountRequest {
  name: string;
  username?: string | null;
  password?: string | null;
  email?: string | null;
  passMail?: string | null;
  twoFactorAuth?: string | null;
  assigneeUserIds: string[];
  isActive: boolean;
  sheinProxy?: string | null;
}

export type UpdateFulfilAccountRequest = CreateFulfilAccountRequest;

/**
 * Session SHEIN đầy đủ (cookie + header thô) của một tài khoản.
 * Endpoint riêng, không nằm trong `GET /accounts`: cookie ~16 KB/tài khoản mà list load mỗi lần vào trang.
 */
export interface FulfilSheinSession {
  accountId: string;
  accountName: string;
  origin?: string | null;
  cookie?: string | null;
  headers: Record<string, string>;
  /** UA phải khớp trình duyệt đã bắt session — Cloudflare khoá `cf_clearance` theo IP + UA. */
  userAgent?: string | null;
  proxy?: string | null;
  capturedAt?: string | null;
  lastCheckedAt?: string | null;
  lastError?: string | null;
  missingHeaders: string[];
  ready: boolean;
}

/** Dán tay session. Cookie + headers rỗng = xoá session. */
export interface SaveFulfilSheinSessionRequest {
  origin?: string | null;
  cookie?: string | null;
  headers?: Record<string, string> | null;
  /** Khối `name: value` từng dòng (DevTools "Copy request headers"). */
  headersText?: string | null;
  userAgent?: string | null;
}

export interface SaveFulfilSheinSessionResult {
  accountId: string;
  accountName: string;
  accountEmail?: string | null;
  headerCount: number;
  hasCookie: boolean;
  hasProxy: boolean;
  ready: boolean;
  missingHeaders: string[];
  capturedAt: string;
}

export interface TestFulfilSheinSessionResult {
  ok: boolean;
  error?: string | null;
  /** Session chết thật (cần bắt lại), khác với lỗi mạng thoáng qua. */
  sessionInvalid: boolean;
  proxyFailed: boolean;
  /** Billno đã dùng để test; null = test bằng trang list vì tài khoản chưa có đơn nào. */
  billno?: string | null;
  elapsedMs: number;
}

export interface FulfilAccountOption {
  id: string;
  name: string;
  email?: string | null;
  activeCardId?: string | null;
}

export interface FulfilCard {
  id: string;
  name: string;
  cardNumber: string;
  cardNumberMasked: string;
  cardBrand: string;
  expiryMonth?: number | null;
  expiryYear?: number | null;
  holderName?: string | null;
  cvv?: string | null;
  currency: string;
  sourceId: string;
  sourceName: string;
  sourceCode: string;
  sourceAccountId?: string | null;
  sourceAccountName?: string | null;
  accountIds: string[];
  accountNames: string[];
  assigneeUserIds: string[];
  assigneeUserNames: string[];
  isActive: boolean;
  orderCount: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateFulfilCardRequest {
  name: string;
  cardNumber: string;
  cardBrand: string;
  expiryMonth?: number | null;
  expiryYear?: number | null;
  holderName?: string | null;
  cvv?: string | null;
  currency: string;
  sourceId: string;
  sourceAccountId?: string | null;
  accountIds: string[];
  assigneeUserIds: string[];
  isActive: boolean;
}

export type UpdateFulfilCardRequest = CreateFulfilCardRequest;

export interface FulfilCardOption {
  id: string;
  name: string;
  cardNumberMasked: string;
  cardBrand: string;
  currency: string;
  sourceId?: string | null;
  sourceName?: string | null;
  sourceAccountId?: string | null;
  sourceAccountName?: string | null;
  isActive: boolean;
  isPreferredForAccount: boolean;
}

/** Dropdown label: WorldFirst · Huy Nguyễn · 4568****5673 · USD */
export const formatFulfilCardOptionLabel = (
  card: Pick<
    FulfilCardOption,
    'name' | 'cardNumberMasked' | 'currency' | 'sourceName' | 'sourceAccountName'
  > & { isPreferredForAccount?: boolean },
  opts?: { preferredSuffix?: boolean; compact?: boolean },
) => {
  if (opts?.compact) {
    const source = card.sourceAccountName || card.sourceName || card.name;
    const masked = card.cardNumberMasked?.trim();
    const last4 = masked ? masked.replace(/\D/g, '').slice(-4) || masked : null;
    const parts = [source, last4, card.currency || null].filter(Boolean);
    const base = parts.join(' · ');
    if (opts.preferredSuffix !== false && card.isPreferredForAccount) {
      return `${base} · Đang dùng`;
    }
    return base;
  }

  const source =
    card.sourceAccountName && card.sourceName
      ? `${card.sourceName} · ${card.sourceAccountName}`
      : card.sourceAccountName || card.sourceName || card.name;
  const parts = [source, card.cardNumberMasked, card.currency || null].filter(Boolean);
  const base = parts.join(' · ');
  if (opts?.preferredSuffix !== false && card.isPreferredForAccount) {
    return `${base} · Đang dùng`;
  }
  return base;
};

export interface FulfilTransaction {
  id: string;
  fulfilAccountId?: string | null;
  fulfilAccountName?: string | null;
  fulfilCardId?: string | null;
  fulfilCardName?: string | null;
  fulfilCardNumberMasked?: string | null;
  fulfilCardSourceName?: string | null;
  fulfilCardSourceAccountName?: string | null;
  fulfilCardCurrency?: string | null;
  orderId?: string | null;
  platformOrderId?: string | null;
  fourSellerOrderId?: number | null;
  /** Lần mua NCC mà dòng tiền này trả cho; null với giao dịch thủ công / cấp đơn */
  orderSupplierLinkId?: string | null;
  /** Mốc người xử lý tự chọn đơn cho lần mua này — có mốc thì sync không tự chuyển sang đơn khác */
  manualLinkedAt?: string | null;
  /** Billno thực sự bị trừ tiền (từ link), fallback billno primary của đơn */
  supplierOrderNo?: string | null;
  supplierOrderUrl?: string | null;
  /** Nhóm trạng thái SHEIN đã chuẩn hóa */
  supplierStatus?: string | null;
  /** packageState / mã thô từ SHEIN */
  supplierStatusRaw?: string | null;
  /** Mã vận đơn lần mua gắn dòng tiền (warehouse hoặc last-mile) */
  trackingNo?: string | null;
  /** Carrier từ SHEIN track_info.name (USPS / FedEx / UPS / …) */
  carrier?: string | null;
  /** none | warehouse | carrier */
  trackingKind?: string | null;
  sellerId?: string | null;
  sellerName?: string | null;
  /** Tổng coupon charged của đơn. */
  couponAmount?: number | null;
  /** Tổng ví Shein (cashback) đã dùng — tách phần thanh toán, không phải giảm giá. */
  walletAmount?: number | null;
  /** Giảm membership/club (CostUpdate snapshot). */
  membershipAmount?: number | null;
  /** Giảm điểm SHEIN Points (CostUpdate snapshot). */
  pointsAmount?: number | null;
  /** Doanh thu đơn (SellerNet → TotalPaid → Amount). */
  revenueAmount?: number | null;
  /** Lợi nhuận đơn = Revenue − Cost (gross NCC + BuyTrack + ThankCard). */
  profitAmount?: number | null;
  /**
   * Giá mua gộp + phí xử lý dòng CostUpdate = gross NCC (Subtotal+Ship+Tax…) + BuyTrack + ThankCard.
   * Không trừ coupon/ví/club/points. Manual = null.
   */
  purchaseCostAmount?: number | null;
  /**
   * Chi thật (TrueSpend) dòng CostUpdate = amount (thẻ) + walletAmount + membershipAmount (club).
   * Dùng cho Profit / preview Mua hàng — không phải cột Cost.
   */
  trueSpendAmount?: number | null;
  /** Phí BuyTrack (USPS) cấp đơn — từ Orders.buyTrackAmount. Không phải quẹt thẻ. */
  buyTrackAmount?: number;
  /** Phí ThankCard cấp đơn — từ Orders.thankCardAmount. Không phải quẹt thẻ. */
  thankCardAmount?: number;
  /** Phí ship lần mua NCC (CostUpdate read-model). Manual = null. */
  supplierShippingFee?: number | null;
  /** Thuế lần mua NCC (CostUpdate read-model). Manual = null. */
  supplierTax?: number | null;
  /** CostUpdate = SupplierTotal (phần thẻ / totalPrice). */
  amount: number;
  /** Debit = trừ tiền, Credit = nạp tiền */
  direction: 'Debit' | 'Credit' | string;
  currency: string;
  note?: string | null;
  source: string;
  paidByUserId?: string | null;
  paidByUserName?: string | null;
  createdByUserId: string;
  createdByUserName?: string | null;
  occurredAt: string;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateFulfilTransactionRequest {
  fulfilAccountId?: string | null;
  fulfilCardId?: string | null;
  orderId?: string | null;
  amount: number;
  direction?: 'Debit' | 'Credit' | string;
  currency?: string;
  note?: string | null;
  paidByUserId?: string | null;
  occurredAt?: string | null;
}

export interface UpdateFulfilTransactionRequest {
  fulfilAccountId?: string | null;
  fulfilCardId?: string | null;
  orderId?: string | null;
  amount: number;
  direction?: 'Debit' | 'Credit' | string;
  currency?: string;
  note?: string | null;
  paidByUserId?: string | null;
  occurredAt: string;
}

export interface FulfilTransactionQuery {
  accountId?: string;
  cardId?: string;
  sourceId?: string;
  sourceAccountId?: string;
  orderId?: string;
  shopId?: string;
  /** Order seller / owner (Order.OwnerId). */
  sellerId?: string;
  paidByUserId?: string;
  /** Free-text: mã đơn, NCC, seller, tài khoản, thẻ, ghi chú, người xử lý/tạo… */
  keyword?: string;
  from?: string;
  to?: string;
  /** 1-based page (default 1). */
  pageCurrent?: number;
  /** Page size (default 100, max 200). */
  pageSize?: number;
}

/** Header KPI for the same filter as the paged list (not the current page). */
export interface FulfilTransactionListSummary {
  orderCount: number;
  /** Σ cột Thẻ = Debit − Credit của amount (phần thẻ / totalPrice hóa đơn). */
  cardAmount: number;
  /** Σ Cost column = CostUpdate gross NCC + unique-order BuyTrack/ThankCard. */
  costAmount: number;
  /** Σ BuyTrack 1 lần/đơn — phí USPS scan cấp đơn, không phải quẹt thẻ. */
  buyTrackAmount: number;
  /** Σ ThankCard 1 lần/đơn — phí ThankCard cấp đơn, không phải quẹt thẻ. */
  thankCardAmount: number;
  couponAmount: number;
  walletAmount: number;
  membershipAmount: number;
  pointsAmount: number;
  /** Σ unique-order profit — joint shipment is not counted twice. */
  profitAmount: number;
}

export const EMPTY_FULFIL_TX_SUMMARY: FulfilTransactionListSummary = {
  orderCount: 0,
  cardAmount: 0,
  costAmount: 0,
  buyTrackAmount: 0,
  thankCardAmount: 0,
  couponAmount: 0,
  walletAmount: 0,
  membershipAmount: 0,
  pointsAmount: 0,
  profitAmount: 0,
};

export interface FulfilTransactionListPage {
  items: FulfilTransaction[];
  total: number;
  pageCurrent: number;
  pageSize: number;
  summary?: FulfilTransactionListSummary;
}

export interface FulfilSpendingQuery {
  accountId?: string;
  cardId?: string;
  sourceId?: string;
  sourceAccountId?: string;
  shopId?: string;
  /** Order seller / owner (Order.OwnerId). */
  sellerId?: string;
  paidByUserId?: string;
  from?: string;
  to?: string;
  /** Which breakdown to return: card | account | shop | paidBy (default card). */
  breakdown?: 'card' | 'account' | 'shop' | 'paidBy';
}

export interface FulfilSpendingBreakdownItem {
  id?: string | null;
  name: string;
  extra?: string | null;
  /** Net amount (Debit − Credit). */
  amount: number;
  debitAmount?: number;
  creditAmount?: number;
  transactionCount: number;
  orderCount?: number;
}

export interface FulfilSpendingDay {
  date: string;
  amount: number;
  debitAmount?: number;
  creditAmount?: number;
  transactionCount: number;
}

export interface FulfilSpendingSummary {
  /** Net spend (Debit − Credit). */
  totalAmount: number;
  debitAmount?: number;
  creditAmount?: number;
  transactionCount: number;
  orderCount?: number;
  cardCount: number;
  accountCount: number;
  costUpdateAmount: number;
  manualAmount: number;
  byCard: FulfilSpendingBreakdownItem[];
  byAccount: FulfilSpendingBreakdownItem[];
  byShop: FulfilSpendingBreakdownItem[];
  byPaidBy: FulfilSpendingBreakdownItem[];
  days: FulfilSpendingDay[];
}
