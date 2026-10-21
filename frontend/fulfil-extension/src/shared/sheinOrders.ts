/** Shared SHEIN order capture + replay for Sync Order (content + background). */

export const SHEIN_CAPTURE_MSG = 'kb-fulfil-shein-capture';

/** Delay giữa các lần gọi get_order_info (ms) — chỉ dùng khi concurrency = 1. 0 = không chờ. */
export const SHEIN_DETAIL_THROTTLE_MS = 0;

/** Delay giữa các trang order/list. 0 = gọi trang kế ngay. */
export const SHEIN_LIST_PAGE_DELAY_MS = 0;

/** Số luồng get_order_info mặc định / min / max (khớp backend Settings). */
export const SHEIN_DETAIL_CONCURRENCY_DEFAULT = 10;
export const SHEIN_DETAIL_CONCURRENCY_MIN = 1;
export const SHEIN_DETAIL_CONCURRENCY_MAX = 20;

/** Số lần retry thêm sau lần thử đầu (failed/sparse) trong cùng lượt sync. */
export const SHEIN_DETAIL_MAX_RETRIES = 2;

/** Stagger giữa worker khi concurrency > 1 (ms × workerId). 0 = khởi động cùng lúc. */
export const SHEIN_DETAIL_STAGGER_MS = 0;

/** Backoff cơ bản + jitter giữa các pass retry (ms). 0 = retry ngay. */
export const SHEIN_DETAIL_RETRY_BASE_MS = 0;
export const SHEIN_DETAIL_RETRY_JITTER_MS = 0;

/** Cửa sổ lỗi mạng gần đây để adaptive hạ concurrency (≥40% lỗi → /2). */
export const SHEIN_DETAIL_FAIL_WINDOW = 10;
export const SHEIN_DETAIL_FAIL_RATE_THRESHOLD = 0.4;

/** Số lần gọi thành công liên tiếp để nâng lại 1 worker sau khi bị hạ. */
export const SHEIN_DETAIL_RECOVER_STREAK = 5;

/** Timeout mỗi request SHEIN (ms) — proxy treo không được giữ cả lượt sync. */
export const SHEIN_FETCH_TIMEOUT_MS = 15_000;

/** Trang order/list nặng hơn get_order_info nên nới timeout. */
export const SHEIN_LIST_FETCH_TIMEOUT_MS = 25_000;

/** Outcome sau một lần gọi get_order_info. */
export type SheinDetailOutcome = 'rich' | 'sparse' | 'failed' | 'not_found' | 'auth_abort';

/**
 * SHEIN trả 200 kèm code 300103 khi billno không thuộc phiên đăng nhập hiện tại
 * (đơn của tài khoản SHEIN khác). Đây là câu trả lời dứt khoát, gọi lại vẫn vậy.
 */
export function isSheinOrderNotFoundMessage(message: string): boolean {
  return /OrderNotExist|code 300103\b/i.test(message);
}

/** Kết quả phase detail có phân loại tin cậy theo billno. */
export interface SheinDetailEnrichResult {
  orders: SheinOrderPayload[];
  aborted?: string;
  /** Billnos gọi detail lỗi (mạng/HTTP/timeout) sau hết retry. */
  incompleteBillnos: string[];
  /** Billnos đã có breakdown rich. */
  richBillnos: string[];
  /** Billnos gọi thành công nhưng SHEIN không trả đủ breakdown — không retry. */
  sparseBillnos: string[];
  /** Billnos SHEIN báo không tồn tại trên phiên này — không retry, không xếp hàng lại. */
  notFoundBillnos: string[];
}

/** Clamp số luồng detail fetch về [1, 20]. */
export function clampSheinDetailConcurrency(n?: number | null): number {
  if (n == null || !Number.isFinite(n) || n < SHEIN_DETAIL_CONCURRENCY_MIN) {
    return SHEIN_DETAIL_CONCURRENCY_DEFAULT;
  }
  return Math.min(
    SHEIN_DETAIL_CONCURRENCY_MAX,
    Math.max(SHEIN_DETAIL_CONCURRENCY_MIN, Math.floor(n)),
  );
}

export function isSheinAuthAbortMessage(message: string): boolean {
  return /TokenIllegal|401|captcha|Please login|blocked/i.test(message);
}

/** Có tín hiệu tracking/milestone đáng POST dù cost chưa rich. */
export function hasNonCostDetailSignal(order: SheinOrderPayload): boolean {
  return !!(
    order.sheinStatusGroup ||
    order.trackingNo ||
    order.sheinShippedAt ||
    order.sheinDeliveredAt ||
    order.sheinCanceledAt ||
    order.sheinRefundedAt ||
    order.sheinTrackingAt ||
    order.sheinPaidAt ||
    // Shipping phone+zip đủ để rematch sau detail (không cần rich cost).
    (order.buyerPhone?.trim() && order.postalCode?.trim())
  );
}

/**
 * Áp địa chỉ Shipping từ list/detail SHEIN.
 * List: nested \shippingaddr_info.{shipping_telephone,shipping_postcode,shipping_firstname,shipping_lastname}\.
 * Detail (get_order_info): các key đó nằm PHẲNG trên order root (không có shippingaddr_info).
 * Không dùng Billing: billaddr_info / telephone / postcode / firstname / lastname phẳng.
 * \overwrite: true\ (detail): ghi đè giá trị list.
 */
export function applySheinShippingAddress(
  target: Pick<
    SheinOrderPayload,
    'buyerName' | 'buyerPhone' | 'address' | 'city' | 'state' | 'postalCode' | 'countryCode'
  >,
  addrOrRoot: Record<string, any> | null | undefined,
  opts?: { overwrite?: boolean },
): void {
  if (!addrOrRoot || typeof addrOrRoot !== 'object') return;
  const overwrite = opts?.overwrite === true;
  // Prefer nested shippingaddr_info (list); else treat object as flat detail root.
  const nested = addrOrRoot.shippingaddr_info;
  const addr =
    nested && typeof nested === 'object'
      ? (nested as Record<string, any>)
      : (addrOrRoot as Record<string, any>);

  const read = (key: string): string => {
    const v = addr[key];
    if (typeof v === 'string' || typeof v === 'number') return String(v).trim();
    return '';
  };

  // Exact SHEIN Shipping keys only — never telephone/postcode/firstname/lastname (Billing flat).
  const phone = read('shipping_telephone');
  const postal = read('shipping_postcode');
  const name = [read('shipping_firstname'), read('shipping_lastname')].filter(Boolean).join(' ');
  const address = [read('shipping_address_1'), read('shipping_address_2')].filter(Boolean).join(' ');
  const city = read('shipping_city');
  const state = read('shipping_province');
  const country = read('shipping_country_code');

  if (phone && (overwrite || !target.buyerPhone)) target.buyerPhone = phone;
  if (postal && (overwrite || !target.postalCode)) target.postalCode = postal;
  if (name && (overwrite || !target.buyerName)) target.buyerName = name;
  if (address && (overwrite || !target.address)) target.address = address;
  if (city && (overwrite || !target.city)) target.city = city;
  if (state && (overwrite || !target.state)) target.state = state;
  if (country && (overwrite || !target.countryCode)) target.countryCode = country;
}


/** Worker bị demote ngủ bao lâu trước khi kiểm tra lại hạn mức. */
const RUN_DEMOTE_WAIT_MS = 250;

/**
 * Chạy `fn` trên từng item với tối đa `concurrency` worker song song.
 * `concurrency` có thể là getter để adaptive hạ số worker giữa chừng.
 * `shouldStop` = true → không nhận job mới (in-flight vẫn chạy xong).
 * Worker bị demote khi `workerId >= getLimit()` sẽ thoát sau job hiện tại.
 * `staggerMs` chỉ giãn thời điểm KHỞI ĐỘNG của từng worker (một lần), không
 * chèn delay vào mỗi job.
 */
export async function runWithConcurrency<T>(
  items: T[],
  concurrency: number | (() => number),
  fn: (item: T, index: number) => Promise<void>,
  shouldStop?: () => boolean,
  staggerMs = 0,
): Promise<void> {
  if (items.length === 0) return;
  const getLimit = typeof concurrency === 'function' ? concurrency : () => concurrency;
  const startLimit = Math.max(1, Math.min(getLimit(), items.length));
  let next = 0;

  async function worker(workerId: number): Promise<void> {
    if (staggerMs > 0 && workerId > 0) await sleep(workerId * staggerMs);
    while (true) {
      if (shouldStop?.()) return;
      if (next >= items.length) return;
      if (workerId >= getLimit()) {
        // Bị demote thì ngủ chờ chứ không thoát hẳn — còn đường quay lại khi
        // getLimit() hồi phục.
        await sleep(RUN_DEMOTE_WAIT_MS);
        continue;
      }
      const i = next++;
      if (i >= items.length) return;
      await fn(items[i]!, i);
    }
  }

  await Promise.all(Array.from({ length: startLimit }, (_, id) => worker(id)));
}

/** Trần an toàn số trang list — full lookback (≈ 2000 đơn). */
export const SHEIN_LIST_MAX_PAGES_NEW = 40;
/** Trần an toàn số trang list — mode `all` (legacy). */
export const SHEIN_LIST_MAX_PAGES_ALL = 60;

/** Auto Sync nông: ~20 đơn mới nhất (1 trang). */
export const SHEIN_LIST_SHALLOW = {
  pageSize: 20,
  maxPages: 1,
} as const;

/** Cứ N lần Auto Sync thì chạy full lookback (status/tracking đơn cũ hơn). */
export const SHEIN_AUTO_FULL_EVERY_N = 10;

/**
 * Fingerprint một đơn từ field list — dùng Auto Sync để biết có gì mới/đổi
 * trước khi POST xuống DB.
 */
export function sheinListOrderFingerprint(order: SheinOrderPayload): string {
  const unpaid =
    order.sheinUnpaid === true ? '1' : order.sheinUnpaid === false ? '0' : '';
  return [
    order.billno.trim().toUpperCase(),
    order.sheinStatusGroup ?? '',
    order.sheinStatus ?? '',
    order.trackingNo ?? '',
    order.carrier ?? '',
    order.sheinStatusAt ?? '',
    order.sheinShippedAt ?? '',
    order.sheinTrackingAt ?? '',
    order.sheinDeliveredAt ?? '',
    order.sheinCanceledAt ?? '',
    order.sheinRefundedAt ?? '',
    order.orderedAt ?? '',
    order.sheinPaidAt ?? '',
    unpaid,
    order.total ?? '',
    order.shippingFee ?? '',
    order.relationBillno ?? '',
  ].join('|');
}

/** Map billno → fingerprint từ list SHEIN vừa fetch. */
export function buildSheinListSnapshot(
  orders: SheinOrderPayload[],
): Record<string, string> {
  const next: Record<string, string> = {};
  for (const order of orders) {
    const key = order.billno.trim().toUpperCase();
    if (!key) continue;
    next[key] = sheinListOrderFingerprint(order);
  }
  return next;
}

/**
 * `true` khi có billno mới hoặc fingerprint đổi so với snapshot trình duyệt.
 * Billno biến mất khỏi cửa sổ list không tính là thay đổi (không cần POST).
 */
export function sheinListSnapshotHasChanges(
  prev: Record<string, string> | null | undefined,
  orders: SheinOrderPayload[],
): boolean {
  return filterSheinListOrdersChanged(prev, orders).length > 0;
}

/**
 * Auto Sync: chỉ giữ đơn mới hoặc fingerprint đổi — POST list/detail theo nhóm này,
 * không đẩy cả 20 đơn không đổi xuống DB.
 *
 * `alwaysInclude` (billno viết hoa) vượt qua cửa fingerprint: backend còn việc dở trên billno đó
 * (Auto Mark Ship chờ publish / chờ reconcile) và việc đó chỉ chạy tiếp khi có thêm một lượt POST.
 * Đúng lúc ấy payload SHEIN lại ngừng đổi — mã vận đơn đã cấp xong — nên nếu chỉ nhìn fingerprint
 * thì lượt sync nào cũng bỏ qua và mã không bao giờ lên sàn.
 */
export function filterSheinListOrdersChanged(
  prev: Record<string, string> | null | undefined,
  orders: SheinOrderPayload[],
  alwaysInclude?: ReadonlySet<string>,
): SheinOrderPayload[] {
  if (!prev || typeof prev !== 'object') return orders.slice();
  return orders.filter((order) => {
    const key = order.billno.trim().toUpperCase();
    if (!key) return false;
    if (alwaysInclude?.has(key)) return true;
    return prev[key] !== sheinListOrderFingerprint(order);
  });
}

/** Lookback ngày — khớp backend ExtensionSheinSyncSettingKeys. */
export const SHEIN_LIST_LOOKBACK = {
  min: 1,
  max: 60,
  default: 14,
} as const;

export interface SheinListFetchOptions {
  /** Số đơn / trang (SHEIN thường 50; nông = 20). */
  pageSize?: number;
  /** Số trang tối đa. */
  maxPages?: number;
  /** Nghỉ giữa các trang (ms). */
  pageDelayMs?: number;
  /**
   * Cửa sổ ngày theo `orderedAt`. Dừng phân trang khi trang toàn đơn ngoài cửa sổ;
   * lọc đơn ngoài cửa sổ khỏi kết quả. `undefined` = không cắt theo ngày.
   */
  lookbackDays?: number;
}

/** Clamp lookback days về [1, 60]. */
export function clampSheinLookbackDays(n?: number | null): number {
  if (n == null || !Number.isFinite(n)) return SHEIN_LIST_LOOKBACK.default;
  return Math.min(
    SHEIN_LIST_LOOKBACK.max,
    Math.max(SHEIN_LIST_LOOKBACK.min, Math.floor(n)),
  );
}

/**
 * True khi `orderedAt` còn trong cửa sổ lookback. Thiếu/invalid `orderedAt` → giữ
 * (không loại để tránh miss đơn list sparse).
 */
export function isSheinOrderWithinLookback(
  orderedAt: string | null | undefined,
  lookbackDays: number,
  nowMs: number = Date.now(),
): boolean {
  if (!orderedAt) return true;
  const t = Date.parse(orderedAt);
  if (!Number.isFinite(t)) return true;
  const cutoff = nowMs - lookbackDays * 24 * 60 * 60 * 1000;
  return t >= cutoff;
}

/**
 * True khi mọi phần tử trên trang list đều ngoài lookback (SHEIN list mới → cũ).
 * Trang rỗng → false (để caller tự break vì hết trang).
 */
export function isSheinListPageOutsideLookback(
  rawOrders: Record<string, any>[],
  lookbackDays: number,
  nowMs: number = Date.now(),
): boolean {
  if (rawOrders.length === 0) return false;
  return rawOrders.every(
    (o) => !isSheinOrderWithinLookback(extractSheinOrderedAt(o), lookbackDays, nowMs),
  );
}

/** Auto Sync nông — chỉ ~20 đơn đầu list (bắt đơn mới). */
export function sheinListFetchOptionsShallow(): SheinListFetchOptions {
  return {
    pageSize: SHEIN_LIST_SHALLOW.pageSize,
    maxPages: SHEIN_LIST_SHALLOW.maxPages,
    pageDelayMs: SHEIN_LIST_PAGE_DELAY_MS,
  };
}

/**
 * Sync Order thủ công mode <c>all</c> — lấy hết trang list SHEIN (không cắt lookback).
 * Mode <c>new</c> thủ công dùng {@link sheinListFetchOptionsFull}; Auto nông dùng
 * {@link sheinListFetchOptionsShallow}.
 */
export function sheinListFetchOptionsManualAll(): SheinListFetchOptions {
  return {
    pageSize: 50,
    maxPages: SHEIN_LIST_MAX_PAGES_ALL,
    pageDelayMs: SHEIN_LIST_PAGE_DELAY_MS,
  };
}

/** Full lookback theo Settings (Auto định kỳ deep). */
export function sheinListFetchOptionsFull(
  lookbackDays?: number | null,
): SheinListFetchOptions {
  return {
    pageSize: 50,
    maxPages: SHEIN_LIST_MAX_PAGES_NEW,
    pageDelayMs: SHEIN_LIST_PAGE_DELAY_MS,
    lookbackDays: clampSheinLookbackDays(lookbackDays),
  };
}

/** Preset phân trang theo chế độ sync + lookback (legacy `all` / `new`). */
export function sheinListFetchOptionsForMode(
  updateMode?: string | null,
  lookbackDays?: number | null,
): SheinListFetchOptions {
  const isAll = (updateMode || '').toLowerCase() === 'all';
  return isAll
    ? {
        pageSize: 50,
        maxPages: SHEIN_LIST_MAX_PAGES_ALL,
        pageDelayMs: SHEIN_LIST_PAGE_DELAY_MS,
      }
    : sheinListFetchOptionsFull(lookbackDays);
}

export interface SheinCaptureState {
  headers: Record<string, string>;
  listUrl?: string;
  detailUrl?: string;
  /** HTTP method của order/list đã bắt (thường POST). */
  listMethod?: string;
  /** Request body order/list — tái sử dụng trên mọi trang SHEIN. */
  listBody?: string;
  detailMethod?: string;
  detailBody?: string;
  /** Email tài khoản SHEIN đang đăng nhập (nếu bắt được). */
  accountEmail?: string;
  /** Id thành viên SHEIN (member_id / user_id…) — detector đã login. */
  accountId?: string;
  /**
   * User-Agent thật của trình duyệt đã bắt session. Cloudflare cấp `cf_clearance` theo
   * IP + UA, nên backend replay bằng UA khác sẽ ăn trang xác minh.
   */
  userAgent?: string;
  at: number;
}

export interface SheinCapturePayload {
  source: typeof SHEIN_CAPTURE_MSG;
  url: string;
  kind: 'list' | 'detail' | 'other';
  method?: string;
  headers: Record<string, string>;
  body?: string;
  /** Email SHEIN bắt từ profile API / DOM. */
  accountEmail?: string;
  /** Id thành viên SHEIN bắt từ profile API. */
  accountId?: string;
  /** UA thật của trình duyệt (không đi qua allowlist header). */
  userAgent?: string;
  at: number;
}

export interface SheinOrderPayload {
  billno: string;
  /** Email billing tài khoản SHEIN (billaddr_info.email) — map FulfilAccounts. */
  accountEmail: string | null;
  buyerName: string;
  buyerPhone: string;
  address: string;
  city: string;
  state: string;
  postalCode: string;
  countryCode: string;
  trackingNo: string;
  /** SHEIN warehouse C…/N… when present (kept after carrier preferred into trackingNo). */
  warehouseTrackingNo: string | null;
  carrier: string;
  /**
   * SHEIN Joint Shipment / multi-package hint (gộp kiện nhiều shop).
   * Soft signal for backend when attaching an additional billno to one TikTok order.
   */
  isJointShipment: boolean | null;
  /** SHEIN `relation_billno` — shared by Joint Shipment siblings across shops. */
  relationBillno: string | null;
  /** SHEIN `order_package_info_list[].confluence_batch_no` — warehouse consolidate batch. */
  confluenceBatchNo: string | null;
  /** SHEIN `order_type` — 5 = Club/Prime, 6 = Saver/xtra virtual. */
  orderType: number | null;
  /**
   * Club / Saver / Quarterly / virtual goods — not a physical product to fulfil.
   * Still sent to backend so bad links can be unlinked; must not match TikTok orders.
   */
  isMembershipOrVirtual: boolean;
  currency: string;
  /** ISO UTC — ngày đặt/tạo đơn trên SHEIN (map → FulfilPurchasedAt). */
  orderedAt: string | null;
  /** ISO UTC — mốc SHEIN trừ tiền thật (`pay_time` / `paymentTime`), mới là clock của "mua". */
  sheinPaidAt: string | null;
  /**
   * `true` chỉ khi payload nói rõ `pay_time` / `paymentTime` = 0 (tạo đơn nhưng chưa trả tiền).
   * `null` = payload không có khoá nào để kết luận ⇒ backend giữ hành vi cũ.
   */
  sheinUnpaid: boolean | null;
  /** URL trang chi tiết đơn SHEIN. */
  orderUrl: string | null;
  /** Retail Price — `retailTotallPrice` / `orderGoodsList[].retail_price_vo` (× qty). */
  retailPrice: number | null;
  /**
   * Giá nhập = UI Subtotal (`newSubTotalPrice` / `special_price_vo` / label Subtotal).
   * Fallback: total − ship − onTime − tax + club + coupon (khi thiếu literal).
   */
  subtotal: number | null;
  shippingFee: number | null;
  /**
   * Phí giao hàng retail — SHEIN `retail_delivery_fee.amount` (tách khỏi shippingFee).
   */
  retailDeliveryFee: number | null;
  onTimeGuarantee: number | null;
  /** Giảm coupon — SHEIN `couponPrice` (không dùng `totalDiscountNew` — hay gộp ship FREE). */
  couponPrice: number | null;
  /**
   * Giảm SHEIN CLUB — sau reconcile với coupon:
   * coupon + club + points ≈ subtotal + ship + retailDelivery + onTime + tax − total − wallet
   * (tách ship FREE bị gộp vào cả couponPrice lẫn Total Savings).
   */
  clubDiscount: number | null;
  /** Giảm điểm — hàng “SHEIN Points: (Points:66)”; trừ khỏi ngân sách trước coupon/club. */
  pointsDiscount: number | null;
  /**
   * Tiền ví cashback SHEIN đã dùng — `usedWalletPrice.amount`.
   * Là thanh toán từ ví, không phải coupon / membership.
   */
  walletPrice: number | null;
  /** Thuế — `total_tax` / `tax[].taxPrice` (không tin `taxPrice` khi = 0). */
  tax: number | null;
  /** Tổng thanh toán — SHEIN totalPrice. */
  total: number | null;
  /**
   * Product id / sku của từng món trong đơn (SHEIN `orderGoodsList`). Backend so với
   * `OrderItem.SellerSku` dạng `86020798-I58rjh8y8izh` để phân giải khi nhiều đơn local
   * trùng phone+zip. Sản phẩm nhiều variant có nhiều sku → so theo tập hợp.
   */
  productIds: string[];
  /** Raw SHEIN status code/text (order_status / status_name / …). */
  sheinStatus: string | null;
  /**
   * Normalized lifecycle group for backend timeline:
   * `processing` | `shipped` | `delivered` | `canceled` | `refunded`.
   */
  sheinStatusGroup: SheinStatusGroup | null;
  /** ISO UTC when status changed (if SHEIN provides a timestamp). Fallback for the group below. */
  sheinStatusAt: string | null;
  /** ISO UTC ship/dispatch clock → backend InTransitAt (+ Track for carrier). */
  sheinShippedAt: string | null;
  /** ISO UTC carrier last-mile tracking clock → backend TrackingAddedAt (falls back to ship). */
  sheinTrackingAt: string | null;
  /** ISO UTC delivered/signed clock → backend DeliveredAt. */
  sheinDeliveredAt: string | null;
  /** ISO UTC cancel clock → backend CanceledAt. */
  sheinCanceledAt: string | null;
  /** ISO UTC refund clock → backend RefundedAt (+ CanceledAt). */
  sheinRefundedAt: string | null;
  /**
   * JSON gốc của một phần tử `order/list`, chỉ đính kèm khi bật capture. Mọi field ở trên đều
   * là kết quả suy diễn — không có bản gốc thì không cách nào kiểm chứng tên field hay múi giờ.
   */
  rawListItem?: string | null;
  /** JSON gốc của response `get_order_info`, chỉ đính kèm khi bật capture. */
  rawDetail?: string | null;
  /**
   * `true` sau `extractBreakdownFromDetail` khi có tín hiệu cost từ detail thật
   * (Subtotal literal/UI, priceDetailList, hoặc field giảm giá). List invert không set cờ này.
   */
  detailCostProven?: boolean;
  /**
   * `true` khi detail đã inspect discount (priceDetailList / usedWalletPrice / coupon…).
   * Chỉ khi đó mới zero-fill coupon/wallet — tránh chốt 0 sớm khi sparse.
   */
  detailDiscountInspected?: boolean;
}

/**
 * Trần ký tự cho JSON gốc đính kèm mỗi đơn. Phase detail POST 10 đơn/lô nên trần này giữ body
 * dưới ~2.5 MB cả lô, đủ xa giới hạn của Cloudflare phía trước API.
 */
export const SHEIN_RAW_CAPTURE_MAX_CHARS = 256 * 1024;

/** Hậu tố đánh dấu bản đã cắt — JSON cắt dở không parse được, phải nhìn thấy ngay là bị cắt. */
export const SHEIN_RAW_TRUNCATED_SUFFIX = '…[truncated]';

let sheinRawCaptureEnabled = false;

/**
 * Bật/tắt đính kèm JSON gốc. Mặc định tắt vì raw làm phình message content→background và body
 * POST; chỉ bật khi cần chẩn đoán. Content script và background là hai bundle riêng nên mỗi
 * ngữ cảnh phải tự gọi hàm này.
 */
export function setSheinRawCaptureEnabled(enabled: boolean): void {
  sheinRawCaptureEnabled = enabled === true;
}

/** Trạng thái cờ capture của ngữ cảnh hiện tại. */
export function isSheinRawCaptureEnabled(): boolean {
  return sheinRawCaptureEnabled;
}

/** Serialize JSON gốc theo cờ capture, cắt ở trần và đánh dấu. Trả `null` khi tắt. */
export function captureSheinRaw(value: unknown): string | null {
  if (!sheinRawCaptureEnabled || value == null) return null;
  let json: string;
  try {
    json = JSON.stringify(value);
  } catch {
    return null;
  }
  if (!json) return null;
  return json.length > SHEIN_RAW_CAPTURE_MAX_CHARS
    ? json.slice(0, SHEIN_RAW_CAPTURE_MAX_CHARS) + SHEIN_RAW_TRUNCATED_SUFFIX
    : json;
}

/** All fulfil milestone clocks extracted from one SHEIN payload in a single pass. */
export interface SheinMilestones {
  shippedAt: string | null;
  trackingAt: string | null;
  deliveredAt: string | null;
  canceledAt: string | null;
  refundedAt: string | null;
}

/** Normalized SHEIN order lifecycle used by sync → timeline. */
export type SheinStatusGroup =
  | 'processing'
  | 'shipped'
  | 'delivered'
  | 'canceled'
  | 'refunded';

/**
 * Extract + normalize SHEIN storefront BFF status fields.
 * Real detail payload (get_order_info) often uses:
 * - `subOrderStatus[].packageState` = "refunded" | "shipped" | …
 * - `orderStatus` / `status` numeric codes (11/13 = refunded on US BFF)
 * - `refund_bill_res_list`, goods `refund_scene` / `refund_type`
 * Prefer those over Open-API seller codes (1–7).
 */
export function extractSheinStatus(order: Record<string, any>): {
  sheinStatus: string | null;
  sheinStatusGroup: SheinStatusGroup | null;
  sheinStatusAt: string | null;
} {
  const packageStates = collectSheinPackageStates(order);
  const goodsSignals = collectSheinGoodsStatusSignals(order);
  const hasRefundBill = Array.isArray(order.refund_bill_res_list)
    ? order.refund_bill_res_list.length > 0
    : Array.isArray(order.refundBillResList)
      ? order.refundBillResList.length > 0
      : false;

  const rawCandidates = [
    packageStates[0],
    order.status != null && order.status !== '' ? String(order.status) : null,
    order.order_status_name,
    order.orderStatusName,
    order.orderStatusTitle,
    order.order_status_title,
    order.status_name,
    order.statusName,
    order.status_desc,
    order.statusDesc,
    hasRefundBill ? 'refunded' : null,
    goodsSignals.refundScene,
    order.order_status,
    order.orderStatus,
    order.order_flag,
    order.orderFlag,
  ];
  let sheinStatus: string | null = null;
  for (const c of rawCandidates) {
    if (c == null || c === '') continue;
    const s = String(c).trim();
    if (s) {
      sheinStatus = s;
      break;
    }
  }

  const blob = [
    sheinStatus,
    ...packageStates,
    goodsSignals.blob,
    order.refund_status,
    order.refundStatus,
    order.cancel_status,
    order.cancelStatus,
    order.return_status,
    order.returnStatus,
    order.is_cancel,
    order.isCancel,
    order.is_refund,
    order.isRefund,
    hasRefundBill ? 'refunded' : null,
  ]
    .filter((v) => v != null && v !== '')
    .map((v) => String(v).trim().toLowerCase())
    .join(' ');

  const sheinStatusGroup = classifySheinStatusGroup(blob, order, {
    packageStates,
    hasRefundBill,
    goodsSignals,
  });

  const sheinStatusAt =
    extractSheinStatusAt(order, sheinStatusGroup) ??
    coerceSheinTimeToIso(
      order.status_time ??
        order.statusTime ??
        order.order_status_time ??
        order.orderStatusTime,
      // Không fallback update_time / last_update_time — đó là giờ sync/API, không phải
      // clock sự kiện fulfil (backend không còn dùng sheinStatusAt để stamp timeline,
      // nhưng giữ sạch để SupplierStatusAt không bị lệch).
    );

  return { sheinStatus, sheinStatusGroup, sheinStatusAt };
}

/** First direct time key present on the order → ISO UTC. */
function extractSheinDirectTime(order: Record<string, any>, keys: string[]): string | null {
  for (const k of keys) {
    const iso = coerceSheinTimeToIso(order[k]);
    if (iso) return iso;
  }
  return null;
}

/**
 * Extract every fulfil milestone clock in one pass, independent of the current status group,
 * so a shipped-then-delivered order carries accurate ship AND delivery times (not one shared
 * `sheinStatusAt`). Missing clocks stay null — the backend leaves those milestones empty
 * rather than stamping sync-run time.
 */
export function extractSheinMilestones(order: Record<string, any>): SheinMilestones {
  const shippedAt = extractSheinLogisticsStatusAt(order, 'shipped');
  const deliveredAt = extractSheinLogisticsStatusAt(order, 'delivered');
  // SHEIN không phát ra mốc gán mã vận đơn: payload chỉ có shipping_no trần, không kèm thời gian.
  // Không được lấy shippedAt làm dự phòng — mốc Track sẽ dính chặt vào mốc Transit và hai cột
  // luôn bằng nhau. Để trống thì backend giữ quyền sở hữu mốc này cho 4seller
  // (`orderShippedTime`, tức lúc mã vận đơn được đẩy lên sàn), vốn lệch mốc rời kho trung bình
  // 6,6 giờ và khác nhau ở 144/154 đơn đo được.
  const trackingAt = extractSheinDirectTime(order, [
    'track_add_time',
    'trackAddTime',
    'tracking_time',
    'trackingTime',
    'track_time',
  ]);
  const refundedAt = extractSheinStatusAt(order, 'refunded');
  const canceledAt =
    extractSheinDirectTime(order, [
      'cancel_time',
      'canceled_time',
      'cancelTime',
      'canceledTime',
      'cancel_order_time',
    ]) ?? extractSheinStatusAt(order, 'canceled');
  return { shippedAt, trackingAt, deliveredAt, canceledAt, refundedAt };
}

function collectSheinPackageStates(order: Record<string, any>): string[] {
  const lists = [
    order.subOrderStatus,
    order.sub_order_status,
    order.packageStatusList,
    order.package_status_list,
  ];
  const out: string[] = [];
  for (const list of lists) {
    if (!Array.isArray(list)) continue;
    for (const row of list) {
      if (!row || typeof row !== 'object') continue;
      const state =
        (row as Record<string, unknown>).packageState ??
        (row as Record<string, unknown>).package_state ??
        (row as Record<string, unknown>).state ??
        (row as Record<string, unknown>).status;
      if (state == null || state === '') continue;
      out.push(String(state).trim());
    }
  }
  return out;
}

function collectSheinGoodsStatusSignals(order: Record<string, any>): {
  blob: string;
  refundScene: string | null;
  hasRefund: boolean;
  allRefunded: boolean;
} {
  const goods =
    order.orderGoodsList ??
    order.order_goods_list ??
    order.orderGoods ??
    order.goodsList ??
    [];
  if (!Array.isArray(goods) || goods.length === 0) {
    return { blob: '', refundScene: null, hasRefund: false, allRefunded: false };
  }

  const parts: string[] = [];
  let refundScene: string | null = null;
  let refundedRows = 0;
  let goodsCount = 0;
  for (const g of goods) {
    if (!g || typeof g !== 'object') continue;
    goodsCount += 1;
    const row = g as Record<string, any>;
    let rowRefunded = false;
    const scene = row.refund_scene ?? row.refundScene;
    if (scene != null && String(scene).trim()) {
      rowRefunded = true;
      if (!refundScene) refundScene = String(scene).trim();
      parts.push(String(scene));
    }
    const refundType = row.refund_type ?? row.refundType;
    if (refundType != null && refundType !== '' && refundType !== 0 && refundType !== '0') {
      rowRefunded = true;
      parts.push(`refund_type:${refundType}`);
    }
    const gs = row.goods_status ?? row.goodsStatus ?? row.status ?? row.orderGoodsStauts;
    if (gs != null && gs !== '') parts.push(String(gs));
    if (rowRefunded) refundedRows += 1;
  }
  return {
    blob: parts.join(' ').toLowerCase(),
    refundScene,
    hasRefund: refundedRows > 0,
    allRefunded: goodsCount > 0 && refundedRows === goodsCount,
  };
}

function extractSheinStatusAt(
  order: Record<string, any>,
  group: SheinStatusGroup | null,
): string | null {
  if (group === 'delivered' || group === 'shipped') {
    return extractSheinLogisticsStatusAt(order, group);
  }

  if (group !== 'refunded' && group !== 'canceled') return null;

  const bills = order.refund_bill_res_list ?? order.refundBillResList;
  if (Array.isArray(bills)) {
    let latest: number | null = null;
    for (const bill of bills) {
      if (!bill || typeof bill !== 'object') continue;
      const logs =
        (bill as Record<string, any>).refund_log_list ??
        (bill as Record<string, any>).refundLogList ??
        [];
      if (!Array.isArray(logs)) continue;
      for (const log of logs) {
        if (!log || typeof log !== 'object') continue;
        const t = coerceSheinTimeToIso(
          (log as Record<string, unknown>).add_time ??
            (log as Record<string, unknown>).addTime,
        );
        if (!t) continue;
        const ms = Date.parse(t);
        if (!Number.isFinite(ms)) continue;
        if (latest == null || ms > latest) latest = ms;
      }
    }
    if (latest != null) return new Date(latest).toISOString();
  }

  const goods = order.orderGoodsList ?? order.order_goods_list;
  if (Array.isArray(goods)) {
    let latest: number | null = null;
    for (const g of goods) {
      const ops =
        g && typeof g === 'object'
          ? ((g as Record<string, any>).goods_operation_list ??
            (g as Record<string, any>).goodsOperationList)
          : null;
      if (!Array.isArray(ops)) continue;
      for (const op of ops) {
        if (!op || typeof op !== 'object') continue;
        const t = coerceSheinTimeToIso(
          (op as Record<string, unknown>).add_time ?? (op as Record<string, unknown>).addTime,
        );
        if (!t) continue;
        const ms = Date.parse(t);
        if (!Number.isFinite(ms)) continue;
        if (latest == null || ms > latest) latest = ms;
      }
    }
    if (latest != null) return new Date(latest).toISOString();
  }

  return null;
}

/**
 * Mốc rời kho / ký nhận lấy theo từng gói trong `order_package_info_list`.
 *
 * Đơn nhiều gói cần hai phép gộp khác nhau: đơn bắt đầu vận chuyển khi gói **đầu tiên** rời kho,
 * nhưng chỉ giao xong khi gói **cuối cùng** được ký nhận. Lấy nhầm chiều sẽ báo đã giao trong khi
 * còn gói đang trên đường.
 */
function extractSheinPackageMilestone(
  order: Record<string, any>,
  group: 'delivered' | 'shipped',
): string | null {
  const packages = order.order_package_info_list ?? order.orderPackageInfoList;
  if (!Array.isArray(packages) || packages.length === 0) return null;

  const keys =
    group === 'delivered'
      ? ['signed_time', 'signedTime', 'sign_time']
      : ['deliveryTime', 'delivery_time', 'ship_time', 'shipTime'];

  let best: number | null = null;
  let seen = 0;
  for (const pkg of packages) {
    if (!pkg || typeof pkg !== 'object') continue;
    const row = pkg as Record<string, unknown>;
    let ms: number | null = null;
    for (const key of keys) {
      const iso = coerceSheinTimeToIso(row[key]);
      if (!iso) continue;
      const parsed = Date.parse(iso);
      if (Number.isFinite(parsed)) {
        ms = parsed;
        break;
      }
    }
    if (ms == null) continue;
    seen += 1;
    if (best == null) best = ms;
    else if (group === 'delivered') best = Math.max(best, ms);
    else best = Math.min(best, ms);
  }

  // Còn gói chưa có mốc ký nhận thì đơn chưa giao xong.
  if (group === 'delivered' && seen < packages.length) return null;

  return best != null ? new Date(best).toISOString() : null;
}

/**
 * Mốc giao suy từ `track_info` — chốt tracking mới nhất của SHEIN.
 *
 * Bắt buộc kiểm tra `status_desc` trước khi dùng: với đơn chưa giao, `track_info.time` là thời
 * điểm của sự kiện vận chuyển gần nhất (thường chỉ cách hiện tại vài giờ), lấy vô điều kiện sẽ
 * đóng dấu "đã giao" cho đơn còn đang trên đường.
 */
function extractSheinDeliveredFromTrackInfo(order: Record<string, any>): string | null {
  const info = order.track_info ?? order.trackInfo;
  if (!info || typeof info !== 'object') return null;
  const row = info as Record<string, unknown>;
  const desc = String(row.status_desc ?? row.statusDesc ?? '').toLowerCase();
  if (!/deliver|signed|received/.test(desc)) return null;
  return coerceSheinTimeToIso(row.time ?? row.status_time);
}

/**
 * Thời điểm delivered / shipped từ payload SHEIN (ưu tiên hơn status_time chung).
 * Dùng cho timeline Giao / Transit khi Sync Order.
 *
 * Ngữ nghĩa hai trường của SHEIN rất dễ nhầm, đã kiểm chứng trên 156 đơn có payload gốc:
 * - `delivery_time` (và `order_package_info_list[].deliveryTime`) là lúc gói hàng **rời kho**,
 *   không phải lúc giao — 55/57 đơn còn đang ở trạng thái `shipped` vẫn có trường này.
 * - `signed_time` là lúc **giao thành công** — vắng mặt ở toàn bộ 57 đơn `shipped`, có đủ ở
 *   99/99 đơn `common_delivered`, và luôn muộn hơn `delivery_time` (trung bình 5,9 ngày,
 *   nhiều nhất 9,5 ngày).
 *
 * Vì vậy `delivery_time` thuộc nhóm shipped và tuyệt đối không được dùng cho nhóm delivered:
 * nhầm hai trường này khiến cột "Giao" sớm gần 6 ngày so với thực tế.
 */
function extractSheinLogisticsStatusAt(
  order: Record<string, any>,
  group: 'delivered' | 'shipped',
): string | null {
  const directKeys =
    group === 'delivered'
      ? [
          'signed_time',
          'sign_time',
          'signedTime',
          'signTime',
          'signing_time',
          'signingTime',
          'received_time',
          'receivedTime',
          'receive_time',
          'receiveTime',
          'delivered_time',
          'deliveredTime',
        ]
      : [
          // Mốc rời kho thực tế mà SHEIN phát ra; các khoá ship_* bên dưới chỉ là dự phòng
          // cho biến thể payload khác, chưa gặp trong dữ liệu thật.
          'delivery_time',
          'deliveryTime',
          'ship_time',
          'shipped_time',
          'shipping_time',
          'shipTime',
          'shippedTime',
          'shippingTime',
          'dispatch_time',
          'dispatchTime',
        ];

  // Danh sách gói xét trước trường cấp đơn: với đơn nhiều gói, `delivery_time` ở cấp đơn báo gói
  // rời kho muộn nhất, trong khi đơn đã bắt đầu di chuyển từ gói đầu tiên.
  const fromPackages = extractSheinPackageMilestone(order, group);
  if (fromPackages) return fromPackages;

  for (const key of directKeys) {
    const iso = coerceSheinTimeToIso(order[key]);
    if (iso) return iso;
  }

  if (group === 'delivered') {
    const fromTrackInfo = extractSheinDeliveredFromTrackInfo(order);
    if (fromTrackInfo) return fromTrackInfo;
  }

  const packageLists = [
    order.subOrderStatus,
    order.sub_order_status,
    order.packageStatusList,
    order.package_status_list,
  ];
  let bestMs: number | null = null;
  const stateRe =
    group === 'delivered'
      ? /\b(delivered|received|signed|completed|complete)\b/i
      : /\b(shipped|in_?transit|dispatched|shipping)\b/i;

  for (const list of packageLists) {
    if (!Array.isArray(list)) continue;
    for (const row of list) {
      if (!row || typeof row !== 'object') continue;
      const r = row as Record<string, unknown>;
      const state = String(
        r.packageState ?? r.package_state ?? r.state ?? r.status ?? '',
      ).trim();
      if (state && !stateRe.test(state)) continue;
      // Cùng lý do như directKeys: delivery_time là mốc rời kho nên chỉ thuộc nhóm shipped.
      const t = coerceSheinTimeToIso(
        group === 'delivered'
          ? (r.signed_time ??
            r.sign_time ??
            r.delivered_time ??
            r.receive_time ??
            r.received_time)
          : (r.delivery_time ??
            r.deliveryTime ??
            r.ship_time ??
            r.shipped_time ??
            r.dispatch_time),
      );
      if (!t) continue;
      const ms = Date.parse(t);
      if (!Number.isFinite(ms)) continue;
      // Đơn nhiều gói: bắt đầu vận chuyển theo gói sớm nhất, giao xong theo gói muộn nhất.
      if (bestMs == null) bestMs = ms;
      else bestMs = group === 'delivered' ? Math.max(bestMs, ms) : Math.min(bestMs, ms);
    }
  }

  if (bestMs != null) return new Date(bestMs).toISOString();

  // goods_operation_list — op mô tả delivered/shipped + add_time. Trong payload thật các op chỉ
  // mang mã số (1, 13, 16, 54…) nên nhánh này hầu như không kích hoạt; chỉ nhận op có nhãn chữ
  // khớp rõ ràng, không đoán theo mã số chưa kiểm chứng.
  const goods = order.orderGoodsList ?? order.order_goods_list;
  if (Array.isArray(goods)) {
    const opRe =
      group === 'delivered'
        ? /\b(deliver|receiv|sign|signed|completed)\b/i
        : /\b(ship|dispatch|transit|warehouse\s*out|send)\b/i;
    for (const g of goods) {
      const ops =
        g && typeof g === 'object'
          ? ((g as Record<string, any>).goods_operation_list ??
            (g as Record<string, any>).goodsOperationList)
          : null;
      if (!Array.isArray(ops)) continue;
      for (const op of ops) {
        if (!op || typeof op !== 'object') continue;
        const o = op as Record<string, unknown>;
        const label = [
          o.operation_desc,
          o.operationDesc,
          o.operate_desc,
          o.operateDesc,
          o.status,
          o.type,
          o.operation_type,
          o.operationType,
        ]
          .filter((v) => v != null && v !== '')
          .map((v) => String(v))
          .join(' ');
        if (!opRe.test(label)) continue;
        const t = coerceSheinTimeToIso(o.add_time ?? o.addTime ?? o.time ?? o.operate_time);
        if (!t) continue;
        const ms = Date.parse(t);
        if (!Number.isFinite(ms)) continue;
        if (bestMs == null) bestMs = ms;
        else bestMs = group === 'delivered' ? Math.max(bestMs, ms) : Math.min(bestMs, ms);
      }
    }
  }

  return bestMs != null ? new Date(bestMs).toISOString() : null;
}

function classifySheinStatusGroup(
  blob: string,
  order: Record<string, any>,
  hints?: {
    packageStates: string[];
    hasRefundBill: boolean;
    goodsSignals: {
      blob: string;
      refundScene: string | null;
      hasRefund: boolean;
      allRefunded: boolean;
    };
  },
): SheinStatusGroup | null {
  const packageBlob = (hints?.packageStates ?? []).join(' ').toLowerCase();
  const truthy = (v: unknown) => {
    if (v === true || v === 1 || v === '1') return true;
    if (typeof v === 'string') {
      const t = v.trim().toLowerCase();
      return t === 'true' || t === 'yes' || t === 'y';
    }
    return false;
  };

  // List `status` là mã lifecycle storefront — đọc trước chrome Return&Refund /
  // leftover refund_type trên hàng. Không thì đơn đã gửi bị gắn refunded rồi kẹt.
  const listStatus = String(order.status ?? '').trim();
  switch (listStatus) {
    case '13':
    case '14':
      return 'refunded';
    case '16':
      return 'canceled';
    case '6':
      return 'delivered';
    case '5':
      return 'shipped';
    case '3':
    case '7':
      return 'processing';
    default:
      break;
  }

  if (
    hints?.hasRefundBill ||
    hints?.goodsSignals.allRefunded ||
    /\brefunded\b/.test(packageBlob)
  ) {
    return 'refunded';
  }
  if (/\b(cancel+ed|cancelled|canceled)\b/.test(packageBlob)) {
    return 'canceled';
  }
  // `common_delivered` không khớp `\bdelivered` vì `_` là word char.
  if (/common_delivered|\b(delivered|received|signed)\b/.test(packageBlob)) {
    return 'delivered';
  }
  if (/\b(shipped|in_?transit|dispatched)\b/.test(packageBlob)) {
    return 'shipped';
  }
  if (
    /\b(processing|packing|preparing)\b/.test(packageBlob) ||
    packageBlob.includes('waiting to be shipped')
  ) {
    return 'processing';
  }

  if (
    truthy(order.is_refund) ||
    truthy(order.isRefund) ||
    /\b(refunded|full\s*refund|refund\s*(success|completed?|done))\b/.test(blob) ||
    /\brefund[_-]?scene\b/.test(blob) ||
    /\brefund[_-]?type:\s*[1-9]\b/.test(blob)
  ) {
    return 'refunded';
  }
  if (
    truthy(order.is_cancel) ||
    truthy(order.isCancel) ||
    /\b(cancel+ed|cancelled|canceled|closed|void)\b/.test(blob)
  ) {
    return 'canceled';
  }
  if (
    /\b(delivered|received|signed|completed|complete|đã\s*giao|đã\s*nhận)\b/.test(blob)
  ) {
    return 'delivered';
  }
  if (
    /\b(shipped|dispatched|in\s*transit|on\s*the\s*way|out\s*for\s*delivery|đã\s*gửi)\b/.test(
      blob,
    )
  ) {
    return 'shipped';
  }

  // Storefront BFF numeric codes (US get_order_info) — distinct from Open API 1–7.
  // Observed: orderStatus 11 + status "13" = refunded (with packageState=refunded).
  // orderStatus 10 + title "Shipped" chiếm đa số payload đã lưu.
  const orderStatusCode = String(order.orderStatus ?? order.order_status ?? '').trim();
  const statusCode = String(order.status ?? '').trim();
  const refundCodes = new Set(['11', '12', '13', '14']);
  if (refundCodes.has(orderStatusCode) || refundCodes.has(statusCode)) {
    return 'refunded';
  }
  // orderStatus=10 vẫn hiện trên đơn đã common_delivered (list không có packageState).
  if (orderStatusCode === '10' && (hints?.packageStates?.length ?? 0) === 0) {
    return 'shipped';
  }
  if (orderStatusCode === '3' || orderStatusCode === '16') return 'processing';

  // Open-platform-style numeric codes (fallback when no packageState).
  const codeRaw = order.order_status ?? order.orderStatus ?? order.status;
  const code = String(codeRaw ?? '').trim();
  if (/^\d+$/.test(code) && (hints?.packageStates?.length ?? 0) === 0) {
    switch (code) {
      case '6':
        return 'refunded';
      case '5':
        return 'delivered';
      case '4':
      case '10':
        return 'shipped';
      case '1':
      case '2':
      case '3':
      case '7':
      case '16':
        return 'processing';
      default:
        break;
    }
  }

  if (
    /\b(pending|unpaid|paid|processing|preparing|to\s*be\s*shipped|awaiting|confirmed|placed)\b/.test(
      blob,
    )
  ) {
    return 'processing';
  }

  if (!blob && order.order_status == null && order.orderStatus == null && order.status == null) {
    return null;
  }

  return sheinStatusGroupFromLooseText(blob);
}

function sheinStatusGroupFromLooseText(blob: string): SheinStatusGroup | null {
  if (!blob) return null;
  if (/\b(refunded|full\s*refund)\b/.test(blob)) return 'refunded';
  if (blob.includes('cancel') || blob.includes('closed')) return 'canceled';
  if (blob.includes('deliver') || blob.includes('receiv')) return 'delivered';
  if (blob.includes('ship')) return 'shipped';
  // Pure numeric leftover without known mapping — unknown, not "processing".
  if (/^\d+(\s+\d+)*$/.test(blob.trim())) return null;
  return 'processing';
}

/** Minimal payload so get_order_info can run for billnos not present in the list page window. */
export function stubSheinOrderPayload(billno: string): SheinOrderPayload {
  return {
    billno: billno.trim(),
    accountEmail: null,
    buyerName: '',
    buyerPhone: '',
    address: '',
    city: '',
    state: '',
    postalCode: '',
    countryCode: '',
    trackingNo: '',
    warehouseTrackingNo: null,
    carrier: '',
    isJointShipment: null,
    relationBillno: null,
    confluenceBatchNo: null,
    orderType: null,
    isMembershipOrVirtual: false,
    currency: 'USD',
    orderedAt: null,
    sheinPaidAt: null,
    sheinUnpaid: null,
    orderUrl: null,
    retailPrice: null,
    subtotal: null,
    shippingFee: null,
    retailDeliveryFee: null,
    onTimeGuarantee: null,
    couponPrice: null,
    clubDiscount: null,
    pointsDiscount: null,
    walletPrice: null,
    tax: null,
    total: null,
    productIds: [],
    sheinStatus: null,
    sheinStatusGroup: null,
    sheinStatusAt: null,
    sheinShippedAt: null,
    sheinTrackingAt: null,
    sheinDeliveredAt: null,
    sheinCanceledAt: null,
    sheinRefundedAt: null,
  };
}

/**
 * List SHEIN hay có coupon / invert subtotal — không tin cậy để full-replace cost.
 * Trước POST phase `list`: xóa breakdown tiền, giữ `total` + `shippingFee` (match / total_delta).
 */
export function stripSheinListCostBreakdown(
  order: SheinOrderPayload,
): SheinOrderPayload {
  return {
    ...order,
    retailPrice: null,
    subtotal: null,
    retailDeliveryFee: null,
    onTimeGuarantee: null,
    couponPrice: null,
    clubDiscount: null,
    pointsDiscount: null,
    walletPrice: null,
    tax: null,
    detailCostProven: undefined,
    detailDiscountInspected: undefined,
  };
}

/** Clone list rồi strip cost — không mutate mảng/local enrich đang dùng cho detail. */
export function stripSheinListOrdersForPost(
  orders: SheinOrderPayload[],
): SheinOrderPayload[] {
  return orders.map(stripSheinListCostBreakdown);
}

/**
 * Điền `accountEmail` từ session capture khi billaddr thiếu email.
 * Backend map `FulfilAccounts` theo email — thiếu thì hàng đợi mất badge TK mua.
 */
export function withSheinCaptureAccountEmail(
  orders: SheinOrderPayload[],
  captureEmail: string | null | undefined,
): SheinOrderPayload[] {
  const fallback = normalizeSheinAccountEmail(captureEmail);
  if (!fallback) return orders;
  return orders.map((o) =>
    o.accountEmail ? o : { ...o, accountEmail: fallback },
  );
}

/**
 * Ensure every billno has a payload row (stub if missing) before detail enrich.
 * Mutates and returns the same array reference.
 */
export function ensureSheinDetailTargets(
  orders: SheinOrderPayload[],
  billnos: string[],
): SheinOrderPayload[] {
  const byBillno = new Map(orders.map((o) => [o.billno.trim().toUpperCase(), o]));
  for (const raw of billnos) {
    const billno = (raw || '').trim();
    if (!billno) continue;
    const key = billno.toUpperCase();
    if (!byBillno.has(key)) {
      const stub = stubSheinOrderPayload(billno);
      orders.push(stub);
      byBillno.set(key, stub);
    }
  }
  return orders;
}

export function isSheinCapturePayload(data: unknown): data is SheinCapturePayload {
  return (
    !!data &&
    typeof data === 'object' &&
    (data as { source?: unknown }).source === SHEIN_CAPTURE_MSG
  );
}

/** Chuẩn hóa body về string (tránh object lỡ lưu → UI thấy field nhưng ready = false). */
export function normalizeSheinBody(body: unknown): string | undefined {
  if (body == null) return undefined;
  if (typeof body === 'string') {
    const trimmed = body.trim();
    return trimmed.length > 0 ? body : undefined;
  }
  if (typeof body === 'object') {
    try {
      const s = JSON.stringify(body);
      return s && s !== '{}' && s !== '[]' ? s : undefined;
    } catch {
      return undefined;
    }
  }
  const s = String(body).trim();
  return s.length > 0 ? s : undefined;
}

/** Body JSON giống request order/list (kể cả khi URL path lạ). */
export function looksLikeSheinListBody(body: unknown): boolean {
  const raw = normalizeSheinBody(body);
  if (!raw) return false;
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>;
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return false;
    if ('typeStatus' in obj || 'type_status' in obj) return true;
    const hasPage = 'page' in obj || 'pageNum' in obj || 'current' in obj;
    const hasLimit = 'limit' in obj || 'pageSize' in obj || 'size' in obj;
    if (hasPage && hasLimit) return true;
    if ('orderStatusList' in obj || 'order_status_list' in obj) return true;
    return false;
  } catch {
    return false;
  }
}

/** Body JSON giống get_order_info (có billno). */
export function looksLikeSheinDetailBody(body: unknown): boolean {
  const raw = normalizeSheinBody(body);
  if (!raw) return false;
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>;
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return false;
    return ['billno', 'billNo', 'order_no', 'orderNo'].some((k) => {
      const v = obj[k];
      return typeof v === 'string' ? v.trim().length > 0 : v != null && String(v).trim().length > 0;
    });
  } catch {
    return false;
  }
}

/**
 * URL API order/list thật (không phải trang HTML `/user/orders/list`).
 * Trang My Orders từng bị classify nhầm → Sync replay HTML → không lấy được đơn.
 */
export function isSheinOrderListApiUrl(url: string): boolean {
  let path: string;
  try {
    path = new URL(url, 'https://us.shein.com').pathname;
  } catch {
    return false;
  }
  // Storefront page — không bao giờ dùng để replay.
  if (/\/user\/orders?\b/i.test(path)) return false;
  return (
    /\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(path) ||
    /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
      path,
    )
  );
}

/**
 * Phân loại URL order API SHEIN.
 * List: bff-api/order/list, api/orders/.../list — không gồm trang /user/orders/list.
 */
export function classifySheinOrderUrl(url: string): SheinCapturePayload['kind'] | null {
  if (isSheinOrderListApiUrl(url)) return 'list';
  if (/\/(?:bff-api\/)?api\/orders\/base\/get_order_info/i.test(url)) return 'detail';
  if (/\/bff-api\/order\/detail/i.test(url)) return 'detail';
  if (/\/bff-api\/order\//i.test(url)) return 'other';
  if (/\/(?:bff-api\/)?api\/orders?\//i.test(url)) return 'other';
  return null;
}

/** Ưu tiên kind từ URL; nếu `other` mà body giống list/detail thì nâng cấp. */
export function resolveSheinCaptureKind(
  url: string,
  body?: unknown,
  hinted?: SheinCapturePayload['kind'] | null,
): SheinCapturePayload['kind'] | null {
  const fromUrl = hinted ?? classifySheinOrderUrl(url);
  if (fromUrl === 'list' || fromUrl === 'detail') return fromUrl;
  if (looksLikeSheinListBody(body)) return 'list';
  if (looksLikeSheinDetailBody(body)) return 'detail';
  return fromUrl;
}

/** Đủ header bảo mật để GET `/bff-api/order/list` (URL cố định, không cần bắt listUrl/listBody). */
export function hasSheinListCapture(capture: SheinCaptureState | null | undefined): boolean {
  return hasSheinSecurityHeaders(capture);
}

/**
 * @deprecated Storefront dùng GET — body không còn dùng cho Sync. Giữ để tương thích storage cũ.
 */
export function hasSheinListBodyCapture(capture: SheinCaptureState | null | undefined): boolean {
  if (!capture) return false;
  return !!normalizeSheinBody(capture.listBody);
}

/**
 * Header tối thiểu để GET `/bff-api/order/list` (cookie phiên qua credentials:include).
 */
export const SHEIN_SYNC_CORE_HEADERS = [
  'armortoken',
  'smdeviceid',
  'x-csrf-token',
  'x-gw-auth',
] as const;

/** Alias checklist = chỉ header lõi (bắt buộc). */
export const SHEIN_SYNC_REQUIRED_HEADERS = SHEIN_SYNC_CORE_HEADERS;

export function hasSheinSecurityHeaders(capture: SheinCaptureState | null | undefined): boolean {
  if (!capture?.headers) return false;
  const present = new Set(
    Object.entries(capture.headers)
      .filter(([, v]) => v != null && String(v).trim() !== '')
      .map(([k]) => k.toLowerCase()),
  );
  return SHEIN_SYNC_CORE_HEADERS.every((h) => present.has(h));
}

/** Đủ dữ liệu để Sync Order: 4 header bảo mật + email + account id SHEIN. */
export function isSheinSyncCaptureReady(capture: SheinCaptureState | null | undefined): boolean {
  if (!hasSheinSecurityHeaders(capture)) return false;
  return !!(
    normalizeSheinAccountEmail(capture?.accountEmail) &&
    normalizeSheinAccountId(capture?.accountId)
  );
}

export interface SheinCaptureChecklistItem {
  key: string;
  label: string;
  present: boolean;
  /** Preview ngắn trên UI. */
  detail?: string;
  /** Full header value — dùng để copy. */
  value?: string;
}

/** Checklist header bắt buộc: có / thiếu (+ preview giá trị). */
export function getSheinCaptureHeaderChecklist(
  capture: SheinCaptureState | null | undefined,
): SheinCaptureChecklistItem[] {
  const headers = capture?.headers || {};
  const lowerMap = new Map<string, string>();
  for (const [k, v] of Object.entries(headers)) {
    if (v == null || v === '') continue;
    lowerMap.set(k.toLowerCase(), v);
  }
  return SHEIN_SYNC_CORE_HEADERS.map((key) => {
    const value = lowerMap.get(key);
    const present = !!value;
    return {
      key,
      label: key,
      present,
      value: present ? value : undefined,
      detail: present
        ? value!.length > 48
          ? `${value!.slice(0, 48)}…`
          : value
        : 'thiếu',
    };
  });
}

/** Thiếu gì để Sync xanh — dùng trên panel inspect. */
export function sheinCaptureReadyGaps(
  capture: SheinCaptureState | null | undefined,
): string[] {
  if (!capture) return ['Chưa có dữ liệu capture'];
  if (isSheinSyncCaptureReady(capture)) return [];
  const gaps: string[] = [];
  const missingCore = SHEIN_SYNC_CORE_HEADERS.filter((h) => {
    const headers = capture.headers || {};
    return !Object.entries(headers).some(
      ([k, v]) => k.toLowerCase() === h && v != null && String(v).trim() !== '',
    );
  });
  if (missingCore.length > 0) gaps.push(`thiếu header: ${missingCore.join(', ')}`);
  if (!normalizeSheinAccountEmail(capture.accountEmail)) gaps.push('thiếu email tài khoản SHEIN');
  if (!normalizeSheinAccountId(capture.accountId)) gaps.push('thiếu account id SHEIN');
  return gaps.length > 0 ? gaps : ['chưa đủ điều kiện sync'];
}

/** URL trang đơn hàng SHEIN — trang này gọi order/list để capture bắt body. */
export function sheinOrdersListUrl(fromUrl?: string | null): string {
  let origin = 'https://us.shein.com';
  if (fromUrl) {
    try {
      const u = new URL(fromUrl);
      if (/(^|\.)shein\.com$/i.test(u.hostname)) {
        origin = u.origin;
      }
    } catch {
      // giữ mặc định us.shein.com
    }
  }
  return `${origin.replace(/\/+$/, '')}/user/orders/list?from=navTop`;
}

function parseMoney(value: unknown): number | null {
  if (value == null) return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  const cleaned = String(value).replace(/[^0-9.-]/g, '');
  if (!cleaned || cleaned === '-' || cleaned === '.') return null;
  const num = Number.parseFloat(cleaned);
  return Number.isFinite(num) ? num : null;
}

function readMoney(value: unknown): number | null {
  if (value == null) return null;
  if (typeof value === 'number' || typeof value === 'string') return parseMoney(value);
  if (typeof value === 'object') {
    const obj = value as Record<string, unknown>;
    for (const key of ['usdAmount', 'amount', 'value', 'price', 'amountWithSymbol', 'priceWithSymbol']) {
      if (key in obj) {
        const parsed = parseMoney(obj[key]);
        if (parsed != null) return parsed;
      }
    }
  }
  return null;
}

function firstTrackingNo(order: Record<string, any>): string {
  const numbers = collectShippingNos(order);
  // Prefer last-mile carrier over SHEIN warehouse C…/N… references.
  const carrier = numbers.find((no) => !/^[CN]\d{10,}$/i.test(no));
  if (carrier) return carrier;
  if (numbers.length > 0) return numbers[0];
  // Warehouse / Joint: reference_number often holds C… before carrier shipping_no appears.
  const ref = (order.reference_number || order.referenceNumber || '').toString().trim();
  if (ref) return ref;
  return '';
}

/** First SHEIN warehouse C…/N… (shipping_no or reference_number). */
function firstWarehouseTrackingNo(order: Record<string, any>): string {
  const numbers = collectShippingNos(order);
  const warehouse = numbers.find((no) => /^[CN]\d{10,}$/i.test(no));
  if (warehouse) return warehouse;
  const ref = (order.reference_number || order.referenceNumber || '').toString().trim();
  if (ref && /^[CN]\d{10,}$/i.test(ref)) return ref;
  return '';
}

function collectShippingNos(order: Record<string, any>): string[] {
  const goods = Array.isArray(order.orderGoodsList) ? order.orderGoodsList : [];
  const numbers: string[] = [];
  for (const g of goods) {
    const rels = Array.isArray(g?.goods_pkg_rel_list) ? g.goods_pkg_rel_list : [];
    for (const rel of rels) {
      const no = (rel?.shipping_no || '').toString().trim();
      if (no) numbers.push(no);
    }
  }
  return numbers;
}

/** SHEIN `relation_billno` — primary Joint Shipment group key (e.g. USU18K244000000MPKY). */
export function extractSheinRelationBillno(order: Record<string, any>): string | null {
  const raw =
    order.relation_billno ??
    order.relationBillno ??
    order.relation_bill_no ??
    order.relationBillNo;
  const v = (raw ?? '').toString().trim();
  return v || null;
}

/** SHEIN warehouse `confluence_batch_no` from `order_package_info_list`. */
export function extractSheinConfluenceBatchNo(order: Record<string, any>): string | null {
  const pkgs = Array.isArray(order.order_package_info_list)
    ? order.order_package_info_list
    : Array.isArray(order.orderPackageInfoList)
      ? order.orderPackageInfoList
      : [];
  for (const pkg of pkgs) {
    const raw = pkg?.confluence_batch_no ?? pkg?.confluenceBatchNo;
    const v = (raw ?? '').toString().trim();
    if (v) return v;
  }
  const top =
    order.confluence_batch_no ?? order.confluenceBatchNo ?? order.confluence_batch ?? null;
  const topV = (top ?? '').toString().trim();
  return topV || null;
}

/**
 * Detect SHEIN Club / Saver / Quarterly / virtual (non-product) orders.
 * Diff vs physical: order_type 5|6, is_virtual_order, xtra_order_scene,
 * is_xtra_goods, goods_id 500000…, SKU VPP…/SQN…, name Saver/Club.
 *
 * Phải trùng từng dấu hiệu với `SheinOrderNormalizer.DetectMembershipOrVirtual` phía backend:
 * backend tin cờ này khi extension gửi lên (`shein.IsMembershipOrVirtual == true`), nên bản ở đây
 * lỏng hơn là backend kế thừa luôn cái lỏng đó — đúng đường mà GSU18V24X001CSQ và
 * GSU18G24X001XC3 bị loại khỏi giá mua dù là hàng vật lý đã giao.
 */
export function detectSheinMembershipOrVirtual(order: Record<string, any>): boolean {
  const orderType = Number(order.order_type ?? order.orderType ?? NaN);
  // 5 = Club/Prime membership; 6 = SHEIN Saver / xtra virtual service.
  if (orderType === 5 || orderType === 6) return true;

  const isVirtualOrder =
    order.is_virtual_order === true ||
    order.is_virtual_order === 1 ||
    order.is_virtual_order === '1' ||
    order.isVirtualOrder === true;
  if (isVirtualOrder) return true;

  const xtraScene = order.xtra_order_scene ?? order.xtraOrderScene;
  if (xtraScene != null && String(xtraScene).trim() !== '' && String(xtraScene) !== '0') {
    return true;
  }

  const goods = Array.isArray(order.orderGoodsList) ? order.orderGoodsList : [];
  if (goods.length === 0) return false;

  let allXtra = true;
  for (const g of goods) {
    const name = String(
      g?.product?.goods_name ?? g?.goods_name ?? g?.product?.goods_url_name ?? '',
    ).toLowerCase();
    if (
      /quarterly|free gifts\+extra|shein\s*club|shein\s*saver|prime\s*membership|vip\s*membership/i.test(
        name,
      )
    ) {
      return true;
    }

    const goodsId = String(g?.goods_id ?? g?.product?.goods_id ?? '').trim();
    // Id catalog ảo (Club/Saver/membership) đúng 9 chữ số: 500000433, 500000983, 500001073.
    // Không để mở đuôi `\d+`: id hàng thật cũng bắt đầu bằng 50000 (502358797) và khớp nhầm ở đây
    // là loại một lần mua thật khỏi giá mua.
    if (/^50000\d{4}$/.test(goodsId)) return true;

    const sku = String(
      g?.sku_code ?? g?.goods_sn ?? g?.product?.goods_sn ?? g?.product?.sku_code ?? '',
    ).trim();
    if (/^VPP/i.test(sku) || /^SQN/i.test(sku)) return true; // VPP=Club, SQN=Saver

    // Cố tình không dùng is_prime_goods và virtual_inventory_flag: chúng nói về cách định giá /
    // cách trữ hàng của món, không nói "món này là gói thành viên". Hàng vật lý đã giao vẫn mang
    // `virtual_inventory_flag=1`, mà nhánh membership phía backend là nhánh phá huỷ.
    const isXtra =
      g?.is_xtra_goods === true ||
      g?.is_xtra_goods === 1 ||
      g?.is_xtra_goods === '1' ||
      g?.product?.is_xtra_goods === true;
    if (!isXtra) allXtra = false;
  }

  // Entire order is xtra (Saver) SKUs — no physical merchandise.
  return goods.length > 0 && allXtra;
}

export function extractSheinOrderType(order: Record<string, any>): number | null {
  const raw = order.order_type ?? order.orderType;
  if (raw == null || raw === '') return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

/** Detect Joint Shipment / multi-package flags from SHEIN list/detail payload. */
export function detectSheinJointShipment(order: Record<string, any>): boolean | null {
  // Official SHEIN joint / combined flags (get_order_info).
  const combinedFlag = order.combined_flag ?? order.combinedFlag;
  if (combinedFlag === 3 || combinedFlag === '3') return true;
  if (typeof combinedFlag === 'number' && combinedFlag > 0) return true;

  const combinedShipped =
    order.combined_shipped_flag ?? order.combinedShippedFlag ?? order.combined_shipped;
  if (combinedShipped === true || combinedShipped === 1 || combinedShipped === '1') return true;

  const cross =
    order.cross_order_flag ?? order.crossOrderFlag ?? order.cross_order;
  if (cross === true || cross === 1 || cross === '1') return true;

  if (extractSheinRelationBillno(order)) return true;

  const keys = [
    'is_joint_shipment',
    'isJointShipment',
    'joint_shipment',
    'jointShipment',
    'is_combine_package',
    'isCombinePackage',
    'combine_package',
    'combinePackage',
  ];
  for (const key of keys) {
    const v = order[key];
    if (v === true || v === 1 || v === '1' || v === 'true') return true;
    if (v === false || v === 0 || v === '0' || v === 'false') return false;
  }
  const goods = Array.isArray(order.orderGoodsList) ? order.orderGoodsList : [];
  const pkgNos = new Set<string>();
  for (const g of goods) {
    const rels = Array.isArray(g?.goods_pkg_rel_list) ? g.goods_pkg_rel_list : [];
    for (const rel of rels) {
      const no = (rel?.package_no || rel?.shipping_no || '').toString().trim();
      if (no) pkgNos.add(no.toUpperCase());
    }
  }
  if (pkgNos.size > 1) return true;
  return null;
}

/** Field chứa product id / sku trên `orderGoodsList` (SHEIN đổi tên tùy version API). */
const SHEIN_PRODUCT_ID_KEYS = [
  'goods_id',
  'goodsId',
  'product_id',
  'productId',
  'productRelationID',
  'product_relation_id',
  'goods_sn',
  'goodsSn',
  'sku_code',
  'skuCode',
] as const;

/**
 * Gom product id / sku của các món trong đơn SHEIN. Bắt cả id nhúng trong URL PDP
 * (`/a-p-86020798.html`) vì tên field thay đổi giữa các version API.
 */
export function collectSheinProductIds(order: Record<string, any>): string[] {
  const out = new Set<string>();
  const push = (value: unknown): void => {
    if (value == null) return;
    const s = String(value).trim();
    if (!s || s.length > 40) return;
    out.add(s);
  };

  const walk = (node: unknown, depth: number): void => {
    if (depth > 4 || node == null || typeof node !== 'object') return;
    if (Array.isArray(node)) {
      for (const item of node) walk(item, depth + 1);
      return;
    }
    const obj = node as Record<string, unknown>;
    for (const key of SHEIN_PRODUCT_ID_KEYS) {
      if (key in obj) push(obj[key]);
    }
    for (const value of Object.values(obj)) {
      if (typeof value === 'string' && value.includes('-p-')) {
        const m = value.match(/-p-(\d+)/i);
        if (m?.[1]) push(m[1]);
      }
    }
    for (const value of Object.values(obj)) walk(value, depth + 1);
  };

  walk(Array.isArray(order.orderGoodsList) ? order.orderGoodsList : [], 0);
  return [...out];
}

function sumInsurance(order: Record<string, any>): number | null {
  const list = Array.isArray(order.order_insurance_res_list)
    ? order.order_insurance_res_list
    : [];
  if (list.length === 0) return null;
  let sum = 0;
  let any = false;
  for (const ins of list) {
    const fee = readMoney(ins?.insurance_fee_usd ?? ins?.insurance_fee);
    if (fee != null) {
      sum += fee;
      any = true;
    }
  }
  return any ? sum : null;
}

/** Qty trên 1 dòng `orderGoodsList`. */
function goodsQty(g: Record<string, any>): number {
  const q = Number(g.quantity ?? g.goods_number ?? g.goodsNumber ?? g.qty ?? 1);
  return Number.isFinite(q) && q > 0 ? q : 1;
}

/**
 * Retail / giá gạch trên dòng hàng — get_order_info dùng `retail_price_vo`
 * (và `product.retailPrice`). Order-level: `retailTotallPrice` (typo SHEIN).
 */
const SHEIN_GOODS_RETAIL_KEYS = [
  'retail_price_vo',
  'retailPriceVo',
  'retailPrice',
  'retail_price',
  'retailPriceWithSymbol',
  'marketPrice',
  'market_price',
  'originPrice',
  'origin_price',
  'originalPrice',
  'original_price',
] as const;

/**
 * Giá bán món = UI Subtotal (trước club/coupon) — get_order_info dùng `special_price_vo`
 * (và `unitPrice` / `product.salePrice`). Order-level: `newSubTotalPrice` / `normal_goods_total_price`.
 */
const SHEIN_GOODS_SALE_KEYS = [
  'special_price_vo',
  'specialPriceVo',
  'special_price',
  'specialPrice',
  'unitPrice',
  'unit_price',
  'unitDiscountPrice',
  'unit_discount_price',
  'salePrice',
  'sale_price',
  'goodsPrice',
  'goods_price',
] as const;

/** Đọc unit price từ goods hoặc goods.product (SHEIN lồng price trong product). */
function readGoodsUnit(g: Record<string, any>, keys: readonly string[]): number | null {
  for (const k of keys) {
    if (k in g) {
      const v = readMoney(g[k]);
      if (v != null) return v;
    }
  }
  const product = g.product;
  if (product && typeof product === 'object') {
    const p = product as Record<string, any>;
    for (const k of keys) {
      if (k in p) {
        const v = readMoney(p[k]);
        if (v != null) return v;
      }
    }
  }
  return null;
}

/**
 * Tổng literal từ `orderGoodsList` (unit × qty). Pass-through giá dòng hàng,
 * không cộng/trừ club/coupon.
 */
function sumGoodsField(order: Record<string, any>, keys: readonly string[]): number | null {
  const goods = Array.isArray(order.orderGoodsList) ? order.orderGoodsList : [];
  if (goods.length === 0) return null;
  let sum = 0;
  let any = false;
  for (const raw of goods) {
    if (!raw || typeof raw !== 'object') continue;
    const g = raw as Record<string, any>;
    const unit = readGoodsUnit(g, keys);
    if (unit == null) continue;
    sum += unit * goodsQty(g);
    any = true;
  }
  return any ? Math.round(sum * 100) / 100 : null;
}

/** Retail order-level: retailTotallPrice (typo SHEIN) / retailTotalPrice / retailPrice. */
function readOrderRetail(root: Record<string, any>): number | null {
  return (
    readMoney(root.retailTotallPrice) ??
    readMoney(root.retailTotalPrice) ??
    readMoney(root.retail_totall_price) ??
    readMoney(root.retail_total_price) ??
    readMoney(root.retailPrice ?? root.retail_price) ??
    sumGoodsField(root, SHEIN_GOODS_RETAIL_KEYS)
  );
}

/**
 * Subtotal (Giá nhập) — ưu tiên giá literal trước club:
 * tổng `special_price_vo` → `newSubTotalPrice` / `normal_goods_total_price`.
 * Không ưu tiên raw `subTotalPrice` (đôi khi đã trừ CLUB).
 */
function readOrderSubtotal(root: Record<string, any>): number | null {
  return (
    readMoney(root.newSubTotalPrice) ??
    readMoney(root.new_sub_total_price) ??
    readMoney(root.normal_goods_total_price) ??
    readMoney(root.normalGoodsTotalPrice) ??
    sumGoodsField(root, SHEIN_GOODS_SALE_KEYS)
  );
}

/**
 * Fallback Giá nhập khi thiếu Subtotal literal:
 * total + wallet − ship − retailDelivery − onTime − tax + club + coupon + points.
 */
function computeSubtotalFromTotal(
  total: number | null,
  shipping: number | null,
  onTime: number | null,
  tax: number | null,
  club: number | null,
  coupon: number | null,
  points: number | null,
  wallet: number | null = null,
  retailDelivery: number | null = null,
): number | null {
  if (total == null) return null;
  const v =
    total +
    (wallet ?? 0) -
    (shipping ?? 0) -
    (retailDelivery ?? 0) -
    (onTime ?? 0) -
    (tax ?? 0) +
    (club ?? 0) +
    (coupon ?? 0) +
    (points ?? 0);
  return Math.round(v * 100) / 100;
}

/** Coupon thuần — không fallback `totalDiscountNew`. `0` = placeholder SHEIN → coi như thiếu. */
function readOrderCoupon(root: Record<string, any>): number | null {
  const v = readMoney(root.couponPrice ?? root.coupon_price);
  if (v == null || v === 0) return null;
  return Math.abs(v);
}

/** Club raw từ field API (có thể phình — reconcile sau). `0` = thiếu. */
function readOrderClub(root: Record<string, any>): number | null {
  const v = readMoney(
    root.prime_discount_amount_vo_total ??
      root.primeDiscountAmountVoTotal ??
      root.clubDiscount,
  );
  if (v == null || v === 0) return null;
  return Math.abs(v);
}

/** Giảm điểm SHEIN (hàng “SHEIN Points”). `0` = placeholder → coi như thiếu. */
function readOrderPoints(root: Record<string, any>): number | null {
  const v = readMoney(
    root.pointPrice ??
      root.point_price ??
      root.pointsPrice ??
      root.points_price ??
      root.usePointPrice ??
      root.use_point_price ??
      root.integralPrice ??
      root.integral_price,
  );
  if (v == null || v === 0) return null;
  return Math.abs(v);
}

/** Tiền ví cashback đã dùng — `usedWalletPrice.amount`. `0` = thiếu. */
function readOrderWallet(root: Record<string, any>): number | null {
  const v = readMoney(
    root.usedWalletPrice ??
      root.used_wallet_price ??
      root.walletPrice ??
      root.wallet_price,
  );
  if (v == null || v === 0) return null;
  return Math.abs(v);
}

/** Phí retail delivery — `retail_delivery_fee.amount`. `0` = thiếu. */
function readOrderRetailDeliveryFee(root: Record<string, any>): number | null {
  const v = readMoney(
    root.retail_delivery_fee ??
      root.retailDeliveryFee ??
      root.retail_delivery_fee_amount,
  );
  if (v == null || v === 0) return null;
  return Math.abs(v);
}

function moneyRound2(v: number): number {
  return Math.round(v * 100) / 100;
}

/**
 * Ép các khoản giảm khớp identity khi đã có Subtotal literal:
 *   coupon + club + points ≈ subtotal + ship + retailDelivery + onTime + tax − total − wallet
 *
 * `wallet` là thanh toán từ ví (không phải discount). `points` là hàng riêng trên UI SHEIN
 * (không bị gộp ship FREE) nên được trừ khỏi ngân sách trước; phần còn lại chia cho
 * coupon / club. SHEIN FREE ship thường cộng cùng mức phí gạch (~$3.99) vào CẢ
 * `couponPrice` lẫn `prime_discount` → tách excess/2.
 */
function reconcileDiscounts(
  subtotal: number | null,
  shipping: number | null,
  onTime: number | null,
  tax: number | null,
  total: number | null,
  coupon: number | null,
  club: number | null,
  points: number | null,
  wallet: number | null = null,
  retailDelivery: number | null = null,
): { coupon: number | null; club: number | null; points: number | null } {
  if (subtotal == null || total == null) return { coupon, club, points };

  const expected = moneyRound2(
    subtotal +
      (shipping ?? 0) +
      (retailDelivery ?? 0) +
      (onTime ?? 0) +
      (tax ?? 0) -
      total -
      (wallet ?? 0),
  );
  if (expected < -0.02) return { coupon, club, points };
  const expectedClamped = Math.max(0, expected);

  const p = points != null ? Math.min(Math.abs(points), expectedClamped) : null;
  const budget = moneyRound2(expectedClamped - (p ?? 0));

  const c = coupon != null ? Math.abs(coupon) : null;
  const m = club != null ? Math.abs(club) : null;

  const done = (nextCoupon: number | null, nextClub: number | null) => ({
    coupon: nextCoupon,
    club: nextClub,
    points: p,
  });

  if (c == null && m == null) return done(budget, 0);
  if (c == null) return done(moneyRound2(Math.max(0, budget - (m ?? 0))), m);
  if (m == null) {
    const derived = moneyRound2(budget - c);
    if (derived >= -0.02) return done(c, Math.max(0, derived));
  }

  const sum = moneyRound2(c + (m ?? 0));
  if (Math.abs(sum - budget) <= 0.02) return done(c, m ?? 0);

  // Coupon (hoặc club) đã nằm trong ngân sách giảm → giữ, suy field còn lại.
  // (Tránh half-split khi label đã ghi đúng coupon 3.82 nhưng club vẫn phình.)
  if (c <= budget + 0.02) return done(c, moneyRound2(Math.max(0, budget - c)));
  if (m != null && m <= budget + 0.02) {
    return done(moneyRound2(Math.max(0, budget - m)), m);
  }

  const excess = moneyRound2(sum - budget);
  // Cả hai đều vượt ngân sách → cùng mức ship FREE gộp vào mỗi field.
  if (excess > 0.02 && (shipping ?? 0) === 0 && m != null) {
    const half = moneyRound2(excess / 2);
    const c2 = moneyRound2(c - half);
    const m2 = moneyRound2(m - half);
    if (
      c2 >= -0.02 &&
      m2 >= -0.02 &&
      Math.abs(moneyRound2(Math.max(0, c2) + Math.max(0, m2)) - budget) <= 0.02
    ) {
      return done(Math.max(0, c2), Math.max(0, m2));
    }
  }

  return done(budget, 0);
}

/** Label coupon — loại Total Savings / ship / club / points / wallet / retail delivery. */
function isSheinCouponDiscountLabel(label: string): boolean {
  const l = label.toLowerCase().trim();
  if (
    /total\s*savings|shipping|free\s*ship|\bclub\b|membership|\bprime\b|\bpoints?\b|\bwallet\b|cashback|retail\s*delivery|delivery\s*fee/.test(
      l,
    )
  ) {
    return false;
  }
  if (/coupon/.test(l)) return true;
  return /^discount\b/.test(l);
}

/** Label CLUB exclusive — loại “Total Savings with SHEIN CLUB…”. */
function isSheinClubDiscountLabel(label: string): boolean {
  const l = label.toLowerCase().trim();
  if (/total\s*savings|shipping|free\s*ship|coupon|\bpoints?\b|\bwallet\b|cashback/.test(l))
    return false;
  return /shein\s*club|club\s*exclusive|\bmembership\b|\bprime\b/.test(l);
}

/** Label giảm điểm — hàng “SHEIN Points: (Points:66)”. */
function isSheinPointsDiscountLabel(label: string): boolean {
  const l = label.toLowerCase().trim();
  if (/total\s*savings|shipping|free\s*ship|coupon|\bclub\b|\bwallet\b/.test(l)) return false;
  return /\bpoints?\b/.test(l);
}

/** Label tiền ví cashback đã dùng. */
function isSheinWalletLabel(label: string): boolean {
  const l = label.toLowerCase().trim();
  if (/coupon|\bclub\b|\bpoints?\b|shipping|retail\s*delivery/.test(l)) return false;
  return /\bwallet\b|cashback|shein\s*wallet|store\s*credit/.test(l);
}

/** Label phí retail delivery (tách khỏi shipping thường). */
function isSheinRetailDeliveryLabel(label: string): boolean {
  const l = label.toLowerCase().trim();
  return /retail\s*delivery|delivery\s*fee/.test(l);
}

/**
 * Thuế SHEIN — `taxPrice` thường = 0 dù có thuế; thật nằm ở
 * `total_tax` / `person_all_tax` / `tax[].taxPrice.amount`.
 */
function readOrderTax(root: Record<string, any>): number | null {
  const fromTotal =
    readMoney(root.total_tax) ??
    readMoney(root.totalTax) ??
    readMoney(root.person_all_tax) ??
    readMoney(root.personAllTax) ??
    readMoney(root.hybrid_gst_tax_price) ??
    readMoney(root.hybridGstTaxPrice) ??
    readMoney(root.icms_total_tax_price) ??
    readMoney(root.icmsTotalTaxPrice);
  if (fromTotal != null && fromTotal !== 0) return fromTotal;

  const list = Array.isArray(root.tax) ? root.tax : [];
  let sum = 0;
  let any = false;
  for (const item of list) {
    if (!item || typeof item !== 'object') continue;
    const row = item as Record<string, any>;
    const v = readMoney(row.taxPrice ?? row.tax_price ?? row.amount ?? row.usdAmount);
    if (v != null) {
      sum += v;
      any = true;
    }
  }
  if (any) return Math.round(sum * 100) / 100;

  // Flat taxPrice = 0 thường là placeholder (thuế nằm ở total_tax) — không coi là đã biết.
  const flat = readMoney(root.taxPrice ?? root.tax_price);
  if (flat != null && flat !== 0) return flat;
  return fromTotal;
}

const SHEIN_SUBTOTAL_LABEL_RE =
  /^(items?\s+|merchandise\s+|product\s+|order\s+)?sub\s*-?\s*total\s*:?\s*$/;
const SHEIN_RETAIL_LABEL_RE = /\b(retail(\s*price)?|original(\s*price)?|list(\s*price)?)\b/;

const SHEIN_PRICE_LABEL_KEYS = [
  'name',
  'title',
  'label',
  'lable',
  'text',
  'desc',
  'key',
  'price_type_name',
  'priceTypeName',
  'show_name',
  'showName',
  'local_name',
  'localName',
] as const;

/**
 * Breakdown đủ tin để full-replace phía backend (tránh payload chỉ có total
 * ghi đè retail/coupon/tax cũ thành 0).
 *
 * Component giảm giá / retail / tax > 0 → rich.
 * `subtotal > 0 && total > 0` chỉ rich khi `detailCostProven` (từ get_order_info) —
 * list invert subtotal+coupon không được coi là đủ để chốt cost / zero-fill.
 */
export function isRichSheinBreakdown(target: SheinOrderPayload): boolean {
  if ((target.retailPrice ?? 0) > 0) return true;
  if ((target.tax ?? 0) > 0) return true;
  if ((target.couponPrice ?? 0) > 0) return true;
  if ((target.clubDiscount ?? 0) > 0) return true;
  if ((target.pointsDiscount ?? 0) > 0) return true;
  if ((target.walletPrice ?? 0) > 0) return true;
  if ((target.retailDeliveryFee ?? 0) > 0) return true;
  const sub = target.subtotal ?? 0;
  const total = target.total ?? 0;
  return sub > 0 && total > 0 && target.detailCostProven === true;
}

/** Có đủ thành phần để đảo công thức Giá nhập từ total (không chỉ mỗi total). */
function canInvertSubtotalFromTotal(
  tax: number | null,
  club: number | null,
  coupon: number | null,
  onTime: number | null,
  points: number | null,
  wallet: number | null = null,
  retailDelivery: number | null = null,
): boolean {
  return (
    tax != null ||
    coupon != null ||
    club != null ||
    points != null ||
    wallet != null ||
    retailDelivery != null ||
    ((onTime ?? 0) > 0)
  );
}

/**
 * Detail sync: thiếu field tiền → 0 — chỉ khi breakdown đã rich.
 * Coupon/wallet/club/points/tax chỉ zero-fill khi `detailDiscountInspected`
 * (đã walk priceDetailList / đọc field giảm) — tránh chốt 0 khi sparse chỉ có sub+total.
 */
export function defaultMissingBreakdownToZero(target: SheinOrderPayload): void {
  if (!isRichSheinBreakdown(target)) return;
  if (target.retailPrice == null) target.retailPrice = 0;
  if (target.subtotal == null) target.subtotal = 0;
  if (target.shippingFee == null) target.shippingFee = 0;
  if (target.retailDeliveryFee == null) target.retailDeliveryFee = 0;
  if (target.onTimeGuarantee == null) target.onTimeGuarantee = 0;
  if (target.detailDiscountInspected) {
    if (target.couponPrice == null) target.couponPrice = 0;
    if (target.clubDiscount == null) target.clubDiscount = 0;
    if (target.pointsDiscount == null) target.pointsDiscount = 0;
    if (target.walletPrice == null) target.walletPrice = 0;
    if (target.tax == null) target.tax = 0;
  }
  // Không zero-fill total từ null — payload thiếu total ≠ free-order tường minh.
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function normalizeSheinOrder(order: Record<string, any>, origin?: string): SheinOrderPayload {
  const billAddr = (order.billaddr_info ||
    order.billingaddr_info ||
    order.bill_addr_info ||
    {}) as Record<string, any>;
  // List: shippingaddr_info nested; fallback flat shipping_* on order (same as detail).
  const shippingFields = {
    buyerName: '',
    buyerPhone: '',
    address: '',
    city: '',
    state: '',
    postalCode: '',
    countryCode: '',
  };
  applySheinShippingAddress(shippingFields, order, { overwrite: true });
  const billno = (order.billno || order.billNo || '').toString().trim();
  const host = (origin || 'https://us.shein.com').replace(/\/+$/, '');
  const accountEmail =
    normalizeSheinAccountEmail(billAddr.email) ??
    normalizeSheinAccountEmail(billAddr.Email) ??
    null;

  // List: shipping/total/coupon thường có; subtotal/tax/club chỉ tin cậy ở get_order_info.
  // Giá nhập ưu tiên Subtotal literal; coupon/club reconcile identity khi có literal.
  let coupon = readOrderCoupon(order);
  const shipping = readMoney(order.shippingPrice ?? order.shipping_price);
  const retailDelivery = readOrderRetailDeliveryFee(order);
  const onTime = sumInsurance(order);
  const tax = readOrderTax(order);
  const total = readMoney(order.totalPrice ?? order.total_price);
  const literalSubtotal = readOrderSubtotal(order);
  let club = readOrderClub(order);
  let points = readOrderPoints(order);
  const wallet = readOrderWallet(order);
  let subtotal: number | null;
  if (literalSubtotal != null) {
    subtotal = literalSubtotal;
    const reconciled = reconcileDiscounts(
      subtotal,
      shipping,
      onTime,
      tax,
      total,
      coupon,
      club,
      points,
      wallet,
      retailDelivery,
    );
    coupon = reconciled.coupon;
    club = reconciled.club;
    points = reconciled.points;
  } else if (canInvertSubtotalFromTotal(tax, club, coupon, onTime, points, wallet, retailDelivery)) {
    subtotal = computeSubtotalFromTotal(
      total,
      shipping,
      onTime,
      tax,
      club,
      coupon,
      points,
      wallet,
      retailDelivery,
    );
  } else {
    // Chỉ có total (list nông) — không gán Giá nhập = total.
    subtotal = null;
  }
  const status = extractSheinStatus(order);
  const milestones = extractSheinMilestones(order);
  const paid = extractSheinPaidAt(order);

  return {
    billno,
    accountEmail,
    buyerName: shippingFields.buyerName,
    buyerPhone: shippingFields.buyerPhone,
    address: shippingFields.address,
    city: shippingFields.city,
    state: shippingFields.state,
    postalCode: shippingFields.postalCode,
    countryCode: shippingFields.countryCode,
    trackingNo: firstTrackingNo(order),
    warehouseTrackingNo: firstWarehouseTrackingNo(order) || null,
    carrier: (order.track_info?.name || '').toString().trim(),
    isJointShipment: detectSheinJointShipment(order),
    relationBillno: extractSheinRelationBillno(order),
    confluenceBatchNo: extractSheinConfluenceBatchNo(order),
    orderType: extractSheinOrderType(order),
    isMembershipOrVirtual: detectSheinMembershipOrVirtual(order),
    currency: (order.currency_code || 'USD').toString().trim(),
    orderedAt: extractSheinOrderedAt(order),
    sheinPaidAt: paid.paidAt,
    sheinUnpaid: paid.isUnpaid,
    orderUrl: billno ? `${host}/user/orders/detail/${billno}` : null,
    retailPrice: readOrderRetail(order),
    subtotal,
    shippingFee: shipping,
    retailDeliveryFee: retailDelivery,
    onTimeGuarantee: onTime,
    couponPrice: coupon,
    clubDiscount: club,
    pointsDiscount: points,
    walletPrice: wallet,
    tax,
    total,
    productIds: collectSheinProductIds(order),
    sheinStatus: status.sheinStatus,
    sheinStatusGroup: status.sheinStatusGroup,
    sheinStatusAt: status.sheinStatusAt,
    sheinShippedAt: milestones.shippedAt,
    sheinTrackingAt: milestones.trackingAt,
    sheinDeliveredAt: milestones.deliveredAt,
    sheinCanceledAt: milestones.canceledAt,
    sheinRefundedAt: milestones.refundedAt,
    rawListItem: captureSheinRaw(order),
  };
}

/** Lấy ngày đặt/tạo đơn SHEIN → ISO UTC. Ưu tiên `addTime` (unix giây). */
export function extractSheinOrderedAt(order: Record<string, any>): string | null {
  const candidates = [
    order.addTime,
    order.add_time,
    order.order_time,
    order.orderTime,
    order.order_time_stamp,
    order.orderTimestamp,
    order.createTime,
    order.create_time,
    order.orderCreateTime,
    order.payment_time,
    order.pay_time,
    order.payTime,
    order.order_place_time,
    order.placeTime,
  ];
  for (const c of candidates) {
    const iso = coerceSheinTimeToIso(c);
    if (iso) return iso;
  }
  return null;
}

/**
 * Mốc SHEIN trừ tiền thật: `pay_time` (list, giây) / `paymentTime` (detail, millisecond).
 *
 * SHEIN cho tạo đơn trước rồi mới thu tiền, và tự huỷ khi hết `order_expire_time` (mặc định 4
 * giờ) nếu không ai trả. Đơn ghost đó có `addTime` bình thường nhưng hai khoá trên bằng 0, nên
 * đây là cách duy nhất phân biệt nó với một lần mua thật.
 *
 * Trả `isUnpaid = null` khi payload không có khoá nào: payload rút gọn thiếu khoá không được phép
 * biến đơn đã trả thành đơn ghost. Kiểm tra `n <= 0` trước `coerceSheinTimeToIso` để gắn
 * `isUnpaid` (coerce cũng reject 0, nhưng không phân biệt "chưa trả" vs thiếu khoá).
 */
export function extractSheinPaidAt(order: Record<string, any>): {
  paidAt: string | null;
  isUnpaid: boolean | null;
} {
  let sawZero = false;
  for (const key of ['pay_time', 'paymentTime', 'payment_time', 'payTime']) {
    const raw = order[key];
    if (raw == null || raw === '') continue;
    const n = Number(raw);
    if (Number.isFinite(n) && n <= 0) {
      sawZero = true;
      continue;
    }
    const iso = coerceSheinTimeToIso(raw);
    if (iso) return { paidAt: iso, isUnpaid: false };
  }
  return { paidAt: null, isUnpaid: sawZero ? true : null };
}

/**
 * us.shein.com storefront — wall-clock naive strings are US Eastern.
 * Unix seconds/ms are absolute (UTC). Display in admin = Asia/Ho_Chi_Minh from stored UTC.
 * Do NOT take Eastern/Pacific wall of a unix instant and +7 — that yields e.g. 10:05 instead of 14:05 VN.
 */
export const SHEIN_US_WALL_TZ = 'America/New_York';

/** Epoch bounds matching backend `SheinOrderNormalizer.FromUnix` / `SheinRawTimelineExtractor`. */
const MIN_EPOCH_SECONDS = 946_684_800; // 2000-01-01
const MAX_EPOCH_SECONDS = 4_102_444_800; // 2100-01-01

/** Unix seconds/ms → ISO UTC, or null for 0 / out-of-range (SHEIN "not set"). */
function fromSheinUnix(value: number): string | null {
  if (!Number.isFinite(value) || value <= 0) return null;
  // SHEIN addTime ≈ 10 digits (seconds); ms ≥ 1e12.
  const seconds = value < 1e12 ? value : value / 1000;
  if (seconds < MIN_EPOCH_SECONDS || seconds > MAX_EPOCH_SECONDS) return null;
  const d = new Date(seconds * 1000);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Parse SHEIN time → ISO UTC string. */
export function coerceSheinTimeToIso(value: unknown): string | null {
  if (value == null || value === '') return null;
  if (typeof value === 'number' && Number.isFinite(value)) {
    return fromSheinUnix(value);
  }
  if (typeof value === 'string') {
    const t = value.trim();
    if (!t) return null;
    if (/^\d+(\.\d+)?$/.test(t)) {
      const n = Number(t);
      if (!Number.isFinite(n)) return null;
      return fromSheinUnix(n);
    }
    // Already absolute (Z or ±offset).
    if (/[zZ]$|[+-]\d{2}:?\d{2}$/.test(t)) {
      const d = new Date(t);
      return Number.isNaN(d.getTime()) ? null : d.toISOString();
    }
    // Naive wall clock from US storefront → America/New_York → UTC.
    // Do NOT use `new Date('…T…')` (browser local = VN on operator PCs).
    const normalized = t.includes('T') ? t : t.replace(' ', 'T');
    const m = normalized.match(
      /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?(?:\.\d+)?$/,
    );
    if (m) {
      const ms = wallTimeInTimeZoneToUtcMs(
        Number(m[1]),
        Number(m[2]),
        Number(m[3]),
        Number(m[4]),
        Number(m[5]),
        m[6] != null ? Number(m[6]) : 0,
        SHEIN_US_WALL_TZ,
      );
      if (ms != null) return new Date(ms).toISOString();
    }
    const d2 = new Date(t);
    return Number.isNaN(d2.getTime()) ? null : d2.toISOString();
  }
  return null;
}

/** Convert a wall-clock local time in `timeZone` to UTC epoch ms (Intl, DST-safe). */
function wallTimeInTimeZoneToUtcMs(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  timeZone: string,
): number | null {
  const intendedAsUtc = Date.UTC(year, month - 1, day, hour, minute, second);
  let utcMs = intendedAsUtc;
  for (let i = 0; i < 3; i++) {
    const parts = getDateTimePartsInZone(utcMs, timeZone);
    if (!parts) return null;
    const asIfUtc = Date.UTC(
      parts.year,
      parts.month - 1,
      parts.day,
      parts.hour,
      parts.minute,
      parts.second,
    );
    utcMs += intendedAsUtc - asIfUtc;
  }
  return utcMs;
}

function getDateTimePartsInZone(
  utcMs: number,
  timeZone: string,
): { year: number; month: number; day: number; hour: number; minute: number; second: number } | null {
  try {
    const fmt = new Intl.DateTimeFormat('en-US', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    });
    const map: Record<string, string> = {};
    for (const p of fmt.formatToParts(new Date(utcMs))) {
      if (p.type !== 'literal') map[p.type] = p.value;
    }
    return {
      year: Number(map.year),
      month: Number(map.month),
      day: Number(map.day),
      hour: Number(map.hour),
      minute: Number(map.minute),
      second: Number(map.second),
    };
  } catch {
    return null;
  }
}

/** Đọc amount từ hàng price-summary có label khớp (SHEIN UI Subtotal / Shipping / …). */
function readPriceRowByLabel(root: Record<string, any>, labelRe: RegExp): number | null {
  const lists = [
    root.priceDetailList,
    root.price_detail_list,
    root.orderPriceList,
    root.order_price_list,
    root.priceInfoList,
    root.price_info_list,
    root.paymentPriceList,
    root.payment_price_list,
    root.priceList,
    root.price_list,
    root.showPriceDetailList,
    root.show_price_detail_list,
  ];
  for (const list of lists) {
    if (!Array.isArray(list)) continue;
    for (const row of list) {
      if (!row || typeof row !== 'object') continue;
      const r = row as Record<string, unknown>;
      let label = '';
      for (const k of SHEIN_PRICE_LABEL_KEYS) {
        const v = r[k];
        if (typeof v === 'string' && v.trim()) {
          label = v.trim().toLowerCase();
          break;
        }
      }
      if (!label || !labelRe.test(label)) continue;
      const amt = readMoney(
        r.usdAmount ?? r.amount ?? r.price ?? r.value ?? r.amountWithSymbol ?? r.priceWithSymbol,
      );
      if (amt != null) return amt;
    }
  }
  return null;
}

/** Map field trực tiếp từ get_order_info `info.order` (+ label-walk / goods fallback). */
export function extractBreakdownFromDetail(detail: unknown, target: SheinOrderPayload): void {
  // Chụp trước khi parse: đơn nào làm hàm này ném lỗi mới là đơn cần nhìn bản gốc nhất.
  const raw = captureSheinRaw(detail);
  if (raw) target.rawDetail = raw;

  const root =
    detail && typeof detail === 'object'
      ? ((detail as Record<string, unknown>).order as Record<string, any> | undefined) ??
        (detail as Record<string, any>)
      : null;

  // Prefer special_price_vo / newSubTotalPrice (UI Subtotal). raw subTotalPrice đôi khi đã trừ CLUB.
  let apiSubtotal: number | null = null;
  let uiSubtotal: number | null = null;
  let literalSubtotal: number | null = null;
  let hadLiteralSubtotal = false;

  if (root && typeof root === 'object') {
    apiSubtotal = readMoney(root.subTotalPrice ?? root.subtotalPrice ?? root.subtotal);
    uiSubtotal = readPriceRowByLabel(root, SHEIN_SUBTOTAL_LABEL_RE);
    literalSubtotal = readOrderSubtotal(root);

    const shipping = readMoney(root.shippingPrice ?? root.shipping_price);
    if (shipping != null) target.shippingFee = Math.abs(shipping);

    const retailDelivery = readOrderRetailDeliveryFee(root);
    if (retailDelivery != null) target.retailDeliveryFee = retailDelivery;

    const coupon = readOrderCoupon(root);
    if (coupon != null) target.couponPrice = coupon;

    const club = readOrderClub(root);
    if (club != null) target.clubDiscount = club;

    const points = readOrderPoints(root);
    if (points != null) target.pointsDiscount = points;

    const wallet = readOrderWallet(root);
    if (wallet != null) target.walletPrice = wallet;

    const tax = readOrderTax(root);
    if (tax != null) target.tax = tax;

    const total = readMoney(root.totalPrice ?? root.total_price);
    if (total != null) target.total = total;

    const retail =
      readOrderRetail(root) ?? readPriceRowByLabel(root, SHEIN_RETAIL_LABEL_RE);
    if (retail != null) target.retailPrice = retail;

    const insurance = sumInsurance(root);
    if (insurance != null) target.onTimeGuarantee = insurance;

    // get_order_info is authoritative for last-mile tracking; list often still contains
    // an older SHEIN warehouse C…/N… reference.
    const detailTracking = firstTrackingNo(root);
    if (detailTracking) target.trackingNo = detailTracking;
    const warehouseTracking = firstWarehouseTrackingNo(root);
    if (warehouseTracking) target.warehouseTrackingNo = warehouseTracking;
    if (root.track_info?.name) {
      target.carrier = String(root.track_info.name).trim();
    }

    const milestones = extractSheinMilestones(root);
    if (milestones.shippedAt) target.sheinShippedAt = milestones.shippedAt;
    if (milestones.trackingAt) target.sheinTrackingAt = milestones.trackingAt;
    if (milestones.deliveredAt) target.sheinDeliveredAt = milestones.deliveredAt;
    if (milestones.canceledAt) target.sheinCanceledAt = milestones.canceledAt;
    if (milestones.refundedAt) target.sheinRefundedAt = milestones.refundedAt;
    const detailOrderedAt = extractSheinOrderedAt(root);
    if (detailOrderedAt) target.orderedAt = detailOrderedAt;
    if (target.isJointShipment == null) {
      target.isJointShipment = detectSheinJointShipment(root);
    }
    if (!target.relationBillno) {
      const rel = extractSheinRelationBillno(root);
      if (rel) target.relationBillno = rel;
    }
    if (!target.confluenceBatchNo) {
      const batch = extractSheinConfluenceBatchNo(root);
      if (batch) target.confluenceBatchNo = batch;
    }
    if (target.orderType == null) {
      target.orderType = extractSheinOrderType(root);
    }
    if (!target.isMembershipOrVirtual) {
      target.isMembershipOrVirtual = detectSheinMembershipOrVirtual(root);
    }
    if (target.productIds.length === 0) {
      const ids = collectSheinProductIds(root);
      if (ids.length > 0) target.productIds = ids;
    }

    const status = extractSheinStatus(root);
    if (status.sheinStatusGroup) {
      // Không để list/orderStatus=10 hạ delivered đã lấy từ detail packageState.
      const rank = (g: string | null | undefined) => {
        switch ((g || '').toLowerCase()) {
          case 'processing':
            return 1;
          case 'shipped':
            return 2;
          case 'delivered':
            return 3;
          case 'canceled':
          case 'cancelled':
          case 'refunded':
            return 4;
          default:
            return 0;
        }
      };
      if (rank(status.sheinStatusGroup) >= rank(target.sheinStatusGroup)) {
        target.sheinStatusGroup = status.sheinStatusGroup;
      }
    }
    if (status.sheinStatus) {
      const cur = (target.sheinStatus || '').trim();
      const next = status.sheinStatus.trim();
      if (!cur) {
        target.sheinStatus = next;
      } else {
        const curNum = /^\d+$/.test(cur);
        const nextNum = /^\d+$/.test(next);
        if (curNum && !nextNum) target.sheinStatus = next;
        else if (!(!curNum && nextNum)) target.sheinStatus = next;
      }
    }
    if (status.sheinStatusAt) target.sheinStatusAt = status.sheinStatusAt;

    // Detail dùng `paymentTime` (millisecond) còn list dùng `pay_time` (giây); mốc trả tiền tìm
    // thấy ở bất kỳ mảnh nào cũng thắng, và chỉ khi chưa từng thấy mốc nào mới ghi cờ chưa trả.
    const paid = extractSheinPaidAt(root);
    if (paid.paidAt) {
      target.sheinPaidAt = paid.paidAt;
      target.sheinUnpaid = false;
    } else if (paid.isUnpaid != null && !target.sheinPaidAt) {
      target.sheinUnpaid = paid.isUnpaid;
    }

    // Detail: shipping_* nằm phẳng trên root (hoặc nested shippingaddr_info) — overwrite list.
    applySheinShippingAddress(target, root as Record<string, any>, { overwrite: true });
    const billAddr = (root.billaddr_info ||
      root.billingaddr_info ||
      root.bill_addr_info ||
      {}) as Record<string, any>;
    if (!target.accountEmail) {
      target.accountEmail =
        normalizeSheinAccountEmail(billAddr.email) ??
        normalizeSheinAccountEmail(billAddr.Email) ??
        null;
    }
  }

  // Walk toàn cây: UI "Subtotal" / "Retail Price" ghi đè; field khác chỉ khi còn thiếu.
  const PRICE_KEYS = ['amount', 'price', 'value', 'priceWithSymbol', 'amountWithSymbol', 'usdAmount'];

  const map = (label: string, amount: number): void => {
    const l = label.toLowerCase().trim();
    if (SHEIN_RETAIL_LABEL_RE.test(l) && target.retailPrice == null) target.retailPrice = amount;
    else if (SHEIN_SUBTOTAL_LABEL_RE.test(l)) uiSubtotal = amount;
    else if (isSheinRetailDeliveryLabel(l)) target.retailDeliveryFee = Math.abs(amount);
    else if (/shipping/.test(l) && target.shippingFee == null) target.shippingFee = Math.abs(amount);
    else if (/(on-?time|guarantee|insurance)/.test(l) && target.onTimeGuarantee == null)
      target.onTimeGuarantee = Math.abs(amount);
    else if (isSheinWalletLabel(l)) target.walletPrice = Math.abs(amount);
    else if (isSheinPointsDiscountLabel(l)) target.pointsDiscount = Math.abs(amount);
    else if (isSheinClubDiscountLabel(l)) {
      // Label CLUB exclusive tin hơn prime_discount (hay là Total Savings).
      if (target.clubDiscount == null || /exclusive|membership/.test(l))
        target.clubDiscount = Math.abs(amount);
    } else if (isSheinCouponDiscountLabel(l)) {
      // Label có "coupon" tin hơn API couponPrice (hay gộp ship FREE).
      if (target.couponPrice == null || /coupon/.test(l))
        target.couponPrice = Math.abs(amount);
    }
    else if (/(tax)/.test(l) && target.tax == null) target.tax = amount;
    else if (/(^|\b)total(\b|$)/.test(l) && target.total == null) target.total = amount;
  };

  const walk = (node: unknown, depth: number): void => {
    if (depth > 8 || node == null || typeof node !== 'object') return;
    if (Array.isArray(node)) {
      for (const item of node) walk(item, depth + 1);
      return;
    }
    const obj = node as Record<string, unknown>;
    let label: string | null = null;
    for (const k of SHEIN_PRICE_LABEL_KEYS) {
      const v = obj[k];
      if (typeof v === 'string' && v.trim()) {
        label = v.trim();
        break;
      }
    }
    if (label) {
      for (const pk of PRICE_KEYS) {
        if (pk in obj) {
          const amt = readMoney(obj[pk]);
          if (amt != null) {
            map(label, amt);
            break;
          }
        }
      }
    }
    for (const key of Object.keys(obj)) walk(obj[key], depth + 1);
  };

  walk(detail, 0);

  // Giá nhập: ưu tiên Subtotal literal / UI label; formula chỉ khi đủ tax/coupon/club.
  hadLiteralSubtotal = literalSubtotal != null || uiSubtotal != null;
  const canInvert = canInvertSubtotalFromTotal(
    target.tax,
    target.clubDiscount,
    target.couponPrice,
    target.onTimeGuarantee,
    target.pointsDiscount,
    target.walletPrice,
    target.retailDeliveryFee,
  );
  const formulaSubtotal = canInvert
    ? computeSubtotalFromTotal(
        target.total,
        target.shippingFee,
        target.onTimeGuarantee,
        target.tax,
        target.clubDiscount,
        target.couponPrice,
        target.pointsDiscount,
        target.walletPrice,
        target.retailDeliveryFee,
      )
    : null;
  const resolvedSubtotal =
    literalSubtotal ?? uiSubtotal ?? formulaSubtotal ?? apiSubtotal;
  if (resolvedSubtotal != null) target.subtotal = resolvedSubtotal;

  // Coupon/club identity khi Subtotal độc lập (literal/UI) — tách ship FREE bị gộp.
  if (hadLiteralSubtotal) {
    const reconciled = reconcileDiscounts(
      target.subtotal,
      target.shippingFee,
      target.onTimeGuarantee,
      target.tax,
      target.total,
      target.couponPrice,
      target.clubDiscount,
      target.pointsDiscount,
      target.walletPrice,
      target.retailDeliveryFee,
    );
    target.couponPrice = reconciled.coupon;
    target.clubDiscount = reconciled.club;
    target.pointsDiscount = reconciled.points;
  }

  if (target.retailPrice == null && root) {
    const goodsRetail = sumGoodsField(root, SHEIN_GOODS_RETAIL_KEYS);
    if (goodsRetail != null) target.retailPrice = goodsRetail;
  }

  const hasPriceDetailList =
    !!root &&
    Array.isArray((root as Record<string, unknown>).priceDetailList) &&
    ((root as Record<string, unknown>).priceDetailList as unknown[]).length > 0;
  const discountKeysPresent =
    !!root &&
    (root.usedWalletPrice != null ||
      root.couponPrice != null ||
      root.pointPrice != null ||
      root.prime_discount_amount_vo_total != null ||
      root.total_tax != null ||
      root.tax != null ||
      root.retail_delivery_fee != null);
  target.detailDiscountInspected = !!(
    hasPriceDetailList ||
    discountKeysPresent ||
    target.couponPrice != null ||
    target.walletPrice != null ||
    target.clubDiscount != null ||
    target.pointsDiscount != null ||
    target.tax != null ||
    target.retailDeliveryFee != null
  );
  target.detailCostProven = !!(
    hadLiteralSubtotal ||
    target.detailDiscountInspected ||
    (target.retailPrice ?? 0) > 0
  );

  // Sparse detail (chỉ total) giữ null — không zero-fill kẻo backend xóa breakdown cũ.
  defaultMissingBreakdownToZero(target);
}

function defaultListUrl(origin?: string): string {
  const base = origin || 'https://us.shein.com';
  return `${base.replace(/\/+$/, '')}/bff-api/order/list?_ver=1.1.8&_lang=en`;
}

/** Default get_order_info URL (storefront detail). */
export function defaultDetailUrl(origin?: string, billno?: string): string {
  const base = (origin || 'https://us.shein.com').replace(/\/+$/, '');
  const u = new URL(`${base}/api/orders/base/get_order_info/get`);
  u.searchParams.set('_ver', '1.1.8');
  u.searchParams.set('_lang', 'en');
  if (billno) u.searchParams.set('billno', billno);
  else u.searchParams.set('billno', '');
  return u.href;
}

function buildListUrl(template: string | undefined, page: number, limit: number, origin?: string): string {
  // Không replay trang HTML /user/orders/list — chỉ dùng URL API.
  const base =
    template && isSheinOrderListApiUrl(template) ? template : defaultListUrl(origin);
  const u = new URL(base, origin || base);
  if (!u.searchParams.has('_ver')) u.searchParams.set('_ver', '1.1.8');
  if (!u.searchParams.has('_lang')) u.searchParams.set('_lang', 'en');
  u.searchParams.set('page', String(page));
  u.searchParams.set('limit', String(limit));
  return u.href;
}

function buildDetailUrl(template: string | undefined, billno: string, origin?: string): string | null {
  const fallback = defaultDetailUrl(origin, billno);
  if (!template) return fallback;
  try {
    const u = new URL(template, origin || fallback);
    if (u.searchParams.has('billno')) u.searchParams.set('billno', billno);
    else if (u.searchParams.has('order_no')) u.searchParams.set('order_no', billno);
    else u.searchParams.set('billno', billno);
    if (!u.searchParams.has('_ver')) u.searchParams.set('_ver', '1.1.8');
    if (!u.searchParams.has('_lang')) u.searchParams.set('_lang', 'en');
    return u.href;
  } catch {
    return fallback;
  }
}

function withDetailBillnoBody(body: string | undefined, billno: string): string | undefined {
  if (!body) return undefined;
  try {
    const obj = JSON.parse(body) as Record<string, unknown>;
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return body;
    for (const key of ['billno', 'billNo', 'order_no', 'orderNo']) {
      if (key in obj) obj[key] = billno;
    }
    if (!('billno' in obj) && !('order_no' in obj) && !('orderNo' in obj)) {
      obj.billno = billno;
    }
    return JSON.stringify(obj);
  } catch {
    return body;
  }
}

async function sheinFetchJson(opts: {
  url: string;
  method: string;
  headers: Record<string, string>;
  body?: string;
  timeoutMs?: number;
}): Promise<any> {
  const method = (opts.method || 'GET').toUpperCase();
  const headers: Record<string, string> = {
    accept: 'application/json, text/plain, */*',
    'x-requested-with': 'XMLHttpRequest',
  };
  for (const [k, v] of Object.entries(opts.headers || {})) {
    if (v == null || v === '') continue;
    const name = k.toLowerCase();
    // GET list không gửi content-type / content-length (giống request storefront).
    if (method === 'GET' || method === 'HEAD') {
      if (name === 'content-type' || name === 'content-length') continue;
    }
    headers[name] = v;
  }
  const init: RequestInit = {
    method,
    credentials: 'include',
    headers,
  };
  if (method !== 'GET' && method !== 'HEAD' && opts.body != null) {
    init.body = opts.body;
    if (!headers['content-type']) {
      headers['content-type'] = 'application/json;charset=UTF-8';
    }
  }
  // Qua proxy, request treo có thể kéo tới timeout mặc định của trình duyệt và
  // giữ nguyên cả lượt sync — cắt sớm rồi để pass retry lo.
  const timeoutMs = opts.timeoutMs ?? SHEIN_FETCH_TIMEOUT_MS;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  init.signal = controller.signal;

  let res: Response;
  try {
    res = await fetch(opts.url, init);
  } catch (err) {
    if (controller.signal.aborted) {
      throw new Error(
        `SHEIN ${new URL(opts.url).pathname} quá ${Math.round(timeoutMs / 1000)}s không phản hồi`,
      );
    }
    throw err;
  } finally {
    clearTimeout(timer);
  }

  if (!res.ok) {
    throw new Error(`SHEIN ${new URL(opts.url).pathname} lỗi HTTP ${res.status}`);
  }
  const json = await res.json();
  if (json && typeof json === 'object' && 'code' in json && String(json.code) !== '0') {
    const msg = `${json.msg ?? json.tips ?? ''}`.trim();
    throw new Error(`SHEIN trả code ${json.code}: ${msg}`.trim());
  }
  return json;
}

/**
 * Phase 1: chỉ replay order/list (không gọi detail).
 * Phân trang theo `opts` — mode `all` lấy nhiều trang hơn + delay chống rate-limit.
 */
export async function fetchSheinOrderListFromCapture(
  capture: SheinCaptureState,
  origin?: string,
  opts?: SheinListFetchOptions,
): Promise<{ ok: boolean; orders?: SheinOrderPayload[]; message?: string; pagesFetched?: number }> {
  if (!hasSheinListCapture(capture)) {
    return {
      ok: false,
      message:
        'Chưa bắt được header SHEIN (armortoken/x-gw-auth…). Hãy mở My Orders rồi thử lại.',
    };
  }

  const limit = Math.max(1, opts?.pageSize ?? 50);
  const maxPages = Math.max(1, opts?.maxPages ?? 20);
  const pageDelayMs = Math.max(0, opts?.pageDelayMs ?? 0);
  const lookbackDays =
    opts?.lookbackDays != null && opts.lookbackDays > 0
      ? clampSheinLookbackDays(opts.lookbackDays)
      : undefined;
  const nowMs = Date.now();
  const collected: any[] = [];
  // Khử trùng theo billno. SHEIN phân trang theo offset, nên một đơn đặt trong lúc đang phân trang
  // đẩy toàn bộ danh sách lùi một bậc: trang sau trả lại item đã thấy ở trang trước, và item cuối
  // của nó rơi sang trang chưa lấy. Không khử trùng thì backend nhận billno lặp (đếm sai) mà vẫn
  // thiếu đơn ở đuôi.
  const seen = new Set<string>();
  let pagesFetched = 0;

  try {
    for (let page = 1; page <= maxPages; page++) {
      if (page > 1 && pageDelayMs > 0) await sleep(pageDelayMs);

      // Storefront luôn GET URL cố định — không dùng listUrl/listBody đã bắt.
      const listUrl = buildListUrl(undefined, page, limit, origin);
      const json = await sheinFetchJson({
        url: listUrl,
        method: 'GET',
        headers: capture.headers,
        timeoutMs: SHEIN_LIST_FETCH_TIMEOUT_MS,
      });
      const info = json?.info ?? {};
      const list = Array.isArray(info.order_list) ? info.order_list : [];
      pagesFetched = page;
      if (list.length === 0) break;

      let candidates: any[] = list;
      let reachedWindowEdge = false;
      if (lookbackDays != null) {
        if (isSheinListPageOutsideLookback(list, lookbackDays, nowMs)) break;
        candidates = list.filter((o: Record<string, any>) =>
          isSheinOrderWithinLookback(extractSheinOrderedAt(o), lookbackDays, nowMs),
        );
        // Biên cửa sổ (một phần trang ngoài lookback); list SHEIN mới→cũ nên sau đó chỉ còn đơn cũ.
        reachedWindowEdge = candidates.length < list.length;
      }

      let added = 0;
      for (const order of candidates) {
        const billno = `${order?.billno ?? order?.billNo ?? ''}`.trim().toUpperCase();
        if (!billno || seen.has(billno)) continue;
        seen.add(billno);
        collected.push(order);
        added += 1;
      }

      if (reachedWindowEdge) break;
      // Trang ngắn = hết dữ liệu phía sau. Không dùng `collected.length < total` làm điều kiện lặp
      // như trước: `info.sum` đếm mọi đơn của tài khoản còn `collected` đã bị lookback lọc bớt, nên
      // vòng lặp dừng sớm và bỏ hẳn phần đuôi.
      if (list.length < limit) break;
      // Cả trang chỉ có billno đã thấy: đã đọc hết dữ liệu mới, phần còn lại là do offset trượt.
      if (added === 0) break;
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Không lấy được danh sách đơn SHEIN.';
    // eslint-disable-next-line no-console
    console.error('[kb-fulfil] SHEIN list error:', message);
    return { ok: false, message, pagesFetched };
  }

  const orders = collected.map((o) => normalizeSheinOrder(o, origin)).filter((o) => o.billno);
  return { ok: true, orders, pagesFetched };
}

/**
 * Phase 2: get_order_info cho các billno cần cập nhật giá.
 * Worker pool theo `concurrency` (Settings); chỉ retry lỗi mạng trong cùng lượt
 * (sparse là kết quả cuối); stagger lúc khởi động worker; adaptive hạ/nâng
 * concurrency theo tỉ lệ lỗi mạng; abort auth/captcha.
 */
export async function enrichSheinOrdersWithDetails(
  capture: SheinCaptureState,
  orders: SheinOrderPayload[],
  billnos: string[],
  origin?: string,
  throttleMs: number = SHEIN_DETAIL_THROTTLE_MS,
  concurrency: number = SHEIN_DETAIL_CONCURRENCY_DEFAULT,
): Promise<SheinDetailEnrichResult> {
  const empty: SheinDetailEnrichResult = {
    orders,
    incompleteBillnos: [],
    richBillnos: [],
    sparseBillnos: [],
    notFoundBillnos: [],
  };
  if (!billnos.length) return empty;
  if (!capture.headers || Object.keys(capture.headers).length === 0) {
    return {
      ...empty,
      aborted: 'Thiếu header SHEIN để gọi get_order_info.',
      incompleteBillnos: billnos.map((b) => b.trim()).filter(Boolean),
    };
  }

  ensureSheinDetailTargets(orders, billnos);
  const want = new Set(billnos.map((b) => b.trim().toUpperCase()).filter(Boolean));
  const allTargets = orders.filter((o) => want.has(o.billno.toUpperCase()));
  if (allTargets.length === 0) return empty;

  const detailMethod = (capture.detailMethod || (capture.detailBody ? 'POST' : 'GET')).toUpperCase();
  const maxConcurrency = clampSheinDetailConcurrency(concurrency);
  let liveConcurrency = maxConcurrency;
  let aborted: string | undefined;
  const richKeys = new Set<string>();
  const sparseKeys = new Set<string>();
  const notFoundKeys = new Set<string>();
  const pendingKeys = new Set(allTargets.map((o) => o.billno.toUpperCase()));
  const failWindow: boolean[] = [];
  let okStreak = 0;

  /**
   * Chỉ lỗi mạng/HTTP mới được tính. `sparse` là phản hồi 200 hợp lệ và rất phổ
   * biến (đơn free, đơn đã huỷ) — tính nó là lỗi sẽ kéo pool về 1 worker dù mạng
   * hoàn toàn khoẻ.
   */
  const recordTransportOutcome = (failed: boolean) => {
    failWindow.push(failed);
    if (failWindow.length > SHEIN_DETAIL_FAIL_WINDOW) failWindow.shift();
    if (failed) {
      okStreak = 0;
      if (failWindow.length >= 5) {
        const rate = failWindow.filter(Boolean).length / failWindow.length;
        if (rate >= SHEIN_DETAIL_FAIL_RATE_THRESHOLD && liveConcurrency > 1) {
          liveConcurrency = Math.max(1, Math.floor(liveConcurrency / 2));
        }
      }
      return;
    }
    okStreak++;
    if (okStreak >= SHEIN_DETAIL_RECOVER_STREAK && liveConcurrency < maxConcurrency) {
      liveConcurrency++;
      okStreak = 0;
    }
  };

  const fetchOne = async (order: SheinOrderPayload): Promise<SheinDetailOutcome> => {
    // Detail là nguồn cost — xóa breakdown list kẻo coupon/subtotal list làm false-rich.
    order.retailPrice = null;
    order.subtotal = null;
    order.retailDeliveryFee = null;
    order.onTimeGuarantee = null;
    order.couponPrice = null;
    order.clubDiscount = null;
    order.pointsDiscount = null;
    order.walletPrice = null;
    order.tax = null;
    order.detailCostProven = undefined;
    order.detailDiscountInspected = undefined;

    const detailUrl = buildDetailUrl(capture.detailUrl, order.billno, origin);
    if (!detailUrl) return 'failed';
    try {
      const detail = await sheinFetchJson({
        url: detailUrl,
        method: detailMethod,
        headers: capture.headers,
        body: withDetailBillnoBody(capture.detailBody, order.billno),
      });
      extractBreakdownFromDetail(detail?.info ?? detail, order);
      return isRichSheinBreakdown(order) ? 'rich' : 'sparse';
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      // eslint-disable-next-line no-console
      console.warn(`[kb-fulfil] SHEIN detail ${order.billno} lỗi:`, message);
      if (isSheinOrderNotFoundMessage(message)) return 'not_found';
      if (isSheinAuthAbortMessage(message)) return 'auth_abort';
      return 'failed';
    }
  };

  for (let attempt = 0; attempt <= SHEIN_DETAIL_MAX_RETRIES; attempt++) {
    if (aborted || pendingKeys.size === 0) break;

    if (attempt > 0) {
      const wait =
        SHEIN_DETAIL_RETRY_BASE_MS + Math.floor(Math.random() * SHEIN_DETAIL_RETRY_JITTER_MS);
      await sleep(wait);
    }

    const passTargets = allTargets.filter((o) => pendingKeys.has(o.billno.toUpperCase()));
    if (passTargets.length === 0) break;

    const passConcurrency = liveConcurrency;
    await runWithConcurrency(
      passTargets,
      () => liveConcurrency,
      async (order, index) => {
        if (passConcurrency === 1 && index > 0 && throttleMs > 0) {
          await sleep(throttleMs);
        }

        const outcome = await fetchOne(order);
        const key = order.billno.toUpperCase();
        if (outcome === 'auth_abort') {
          aborted = `SHEIN auth/captcha trên ${order.billno}`;
          recordTransportOutcome(true);
          return;
        }
        if (outcome === 'rich') {
          richKeys.add(key);
          pendingKeys.delete(key);
          recordTransportOutcome(false);
          return;
        }
        if (outcome === 'sparse') {
          // SHEIN trả 200 nhưng không có breakdown — gọi lại cũng vậy, đừng retry.
          sparseKeys.add(key);
          pendingKeys.delete(key);
          recordTransportOutcome(false);
          return;
        }
        if (outcome === 'not_found') {
          // Billno của tài khoản SHEIN khác. Không retry, và không tính vào cửa sổ
          // lỗi mạng — nếu tính, một lô toàn billno lạ sẽ kéo pool về 1 worker và
          // làm chậm các billno hợp lệ còn lại.
          notFoundKeys.add(key);
          pendingKeys.delete(key);
          return;
        }
        // failed — giữ trong pending cho pass sau
        recordTransportOutcome(true);
      },
      () => !!aborted,
      passConcurrency > 1 ? SHEIN_DETAIL_STAGGER_MS : 0,
    );
  }

  const incompleteBillnos = allTargets
    .filter((o) => pendingKeys.has(o.billno.toUpperCase()))
    .map((o) => o.billno);
  const richBillnos = allTargets
    .filter((o) => richKeys.has(o.billno.toUpperCase()))
    .map((o) => o.billno);
  const sparseBillnos = allTargets
    .filter((o) => sparseKeys.has(o.billno.toUpperCase()))
    .map((o) => o.billno);
  const notFoundBillnos = allTargets
    .filter((o) => notFoundKeys.has(o.billno.toUpperCase()))
    .map((o) => o.billno);

  return {
    orders,
    aborted,
    incompleteBillnos,
    richBillnos,
    sparseBillnos,
    notFoundBillnos,
  };
}

/**
 * @deprecated Prefer list + enrich two-phase. Kept for callers that only need list.
 * List-only (không gọi detail hàng loạt).
 */
export async function fetchSheinOrdersFromCapture(
  capture: SheinCaptureState,
  origin?: string,
): Promise<{ ok: boolean; orders?: SheinOrderPayload[]; message?: string }> {
  return fetchSheinOrderListFromCapture(capture, origin);
}

/** Merge payload capture (postMessage) vào state storage. */
export function mergeSheinCapture(
  prev: SheinCaptureState | undefined,
  data: SheinCapturePayload,
): SheinCaptureState {
  const base: SheinCaptureState = prev ?? { headers: {}, at: 0 };
  const body = normalizeSheinBody(data.body);
  const kind = resolveSheinCaptureKind(data.url, body, data.kind) ?? data.kind;
  const listApiUrl = kind === 'list' && isSheinOrderListApiUrl(data.url) ? data.url : undefined;
  const next: SheinCaptureState = {
    headers: { ...base.headers, ...data.headers },
    listUrl: listApiUrl || base.listUrl,
    detailUrl: kind === 'detail' ? data.url : base.detailUrl,
    listMethod: kind === 'list' && data.method ? data.method : base.listMethod,
    listBody:
      kind === 'list' && body
        ? body
        : normalizeSheinBody(base.listBody) ?? base.listBody,
    detailMethod: kind === 'detail' && data.method ? data.method : base.detailMethod,
    detailBody:
      kind === 'detail' && body
        ? body
        : normalizeSheinBody(base.detailBody) ?? base.detailBody,
    accountEmail: normalizeSheinAccountEmail(data.accountEmail) || base.accountEmail,
    accountId: normalizeSheinAccountId(data.accountId) || base.accountId,
    userAgent: data.userAgent?.trim() || base.userAgent,
    at: data.at || base.at,
  };
  return next;
}

const EMAIL_RE = /^[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}$/i;

/** Chuẩn hóa email (trim + lowercase) — dùng cho billaddr_info.email. */
export function normalizeSheinAccountEmail(value: unknown): string | undefined {
  if (value == null) return undefined;
  const s = String(value).trim().toLowerCase();
  if (!s || !EMAIL_RE.test(s)) return undefined;
  return s;
}

const ACCOUNT_ID_KEYS = new Set([
  'member_id',
  'memberid',
  'user_id',
  'userid',
  'account_id',
  'accountid',
  'uid',
  'member_uid',
  'memberuid',
]);

/** Chuẩn hóa id thành viên SHEIN (chuỗi số/id, không phải email). */
export function normalizeSheinAccountId(value: unknown): string | undefined {
  if (value == null) return undefined;
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || value <= 0) return undefined;
    return String(Math.trunc(value));
  }
  const s = String(value).trim();
  if (!s || EMAIL_RE.test(s)) return undefined;
  // Id số dài hoặc alphanumeric member token.
  if (/^\d{4,}$/.test(s)) return s;
  if (/^[a-z0-9_-]{6,64}$/i.test(s) && !/^(true|false|null|undefined)$/i.test(s)) return s;
  return undefined;
}

/** Trích account id từ JSON profile/user (ưu tiên key member/user/account/uid). */
export function extractSheinAccountIdFromJson(input: unknown, budget = 300): string | undefined {
  if (input == null || budget <= 0) return undefined;
  if (typeof input === 'string') {
    const t = input.trim();
    if (!t) return undefined;
    try {
      if (t.startsWith('{') || t.startsWith('[')) {
        return extractSheinAccountIdFromJson(JSON.parse(t), budget - 1);
      }
    } catch {
      return normalizeSheinAccountId(t);
    }
    return normalizeSheinAccountId(t);
  }
  if (typeof input !== 'object') return undefined;
  if (Array.isArray(input)) {
    for (const item of input.slice(0, 30)) {
      const hit = extractSheinAccountIdFromJson(item, budget - 1);
      if (hit) return hit;
    }
    return undefined;
  }
  const obj = input as Record<string, unknown>;
  for (const key of Object.keys(obj)) {
    const lower = key.toLowerCase().replace(/[^a-z0-9_]/g, '');
    if (!ACCOUNT_ID_KEYS.has(lower)) continue;
    const hit = normalizeSheinAccountId(obj[key]);
    if (hit) return hit;
  }
  for (const key of Object.keys(obj)) {
    const val = obj[key];
    if (val && typeof val === 'object') {
      const hit = extractSheinAccountIdFromJson(val, budget - 1);
      if (hit) return hit;
    }
  }
  return undefined;
}
