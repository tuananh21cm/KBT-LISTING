/**
 * Đọc số liệu Compass (`stats_v3.total`) — phần thuần logic của luồng Sync analytics,
 * tách khỏi content script để test được và để mọi thay đổi schema TikTok chỉ sửa 1 chỗ.
 *
 * Chỉ `src/content/index.ts` được import file này: content.js phải là script classic,
 * nên Rollup cần inline module này vào entry. Nếu background/sidepanel cũng import,
 * Rollup tách thành chunk và content.js sẽ có `import` → Chrome không chạy được.
 */

const STATS_KEYS = ['stats_v3', 'statsV3', 'stats_v2', 'stats', 'metrics'];
const TOTAL_KEYS = ['total', 'totals', 'summary', 'current'];
/** Số liệu kỳ trước Compass gửi kèm — nguồn thật để tính % thay đổi. */
const PREVIOUS_KEYS = [
  'total_prev',
  'totalPrev',
  'total_previous',
  'prev',
  'previous',
  'total_last',
  'last_period',
  'compare',
];

const METRIC_VALUE_KEYS = [
  'value',
  'val',
  'amount',
  'number',
  'num',
  'count',
  'cnt',
  'current',
  'cur_value',
  'curValue',
  'total',
];

const METRIC_RATIO_KEYS = [
  'ratio',
  'chain_ratio',
  'chainRatio',
  'ring_ratio',
  'ringRatio',
  'compare_ratio',
  'compareRatio',
  'diff_ratio',
  'diffRatio',
  'growth_rate',
  'growthRate',
  'rate_of_change',
  'fluctuation',
];

/**
 * Alias metric theo thứ tự ưu tiên; tên **đầu tiên** của mỗi nhóm là tên thật đang thấy trên
 * `stats_v3` (đo bằng payload thực), các tên sau là dự phòng khi TTS đổi schema.
 *
 * Compass đếm hai chiều: `_pv` = số lần (page view) và `_uv` = số người (unique visitor). Bảng của
 * mình dùng **`_pv`** cho impressions / clicks / add-to-cart units và **`_uv`** cho add-to-cart
 * buyers (số người), khớp cách Compass UI gắn nhãn.
 */
export const GMV_KEYS = ['gmv', 'gmv_amount', 'gmvAmount', 'total_gmv', 'totalGmv', 'revenue', 'sales_amount'];
export const ORDER_KEYS = [
  'sku_orders',
  'skuOrders',
  'sku_order_cnt',
  'orders',
  'order_cnt',
  'orderCnt',
  'order_count',
  'orderCount',
  'paid_orders',
  'paidOrders',
];
export const IMPRESSION_KEYS = [
  'product_impression_pv',
  'product_impression_uv',
  'product_impressions',
  'productImpressions',
  'impressions',
  'impression',
  'impression_cnt',
  'impressionCnt',
  'product_show_cnt',
  'show_cnt',
  'exposure',
];
export const CLICK_KEYS = [
  'product_click_pv',
  'product_click_uv',
  'product_page_views',
  'productPageViews',
  'page_views',
  'pageViews',
  'product_clicks',
  'productClicks',
  'clicks',
  'click',
  'click_cnt',
  'clickCnt',
  'product_pv',
  'pv',
];
export const CTR_KEYS = [
  'product_ctr_pv',
  'product_ctr_uv',
  'ctr',
  'click_through_rate',
  'clickThroughRate',
  'click_rate',
  'clickRate',
  'product_ctr',
];
export const ATC_UNIT_KEYS = [
  'add_to_cart_cnt_pv',
  'add_to_cart_cnt_uv',
  'add_to_cart_units',
  'addToCartUnits',
  'add_to_cart_cnt',
  'addToCartCnt',
  'sku_add_to_cart',
  'add_cart_cnt',
  'cart_units',
  'atc_units',
  'add_to_cart',
  'addToCart',
];
export const ATC_BUYER_KEYS = [
  'add_to_cart_users_uv',
  'add_to_cart_users_pv',
  'add_to_cart_buyers',
  'addToCartBuyers',
  'add_to_cart_customers',
  'add_to_cart_users',
  'cart_buyers',
  'atc_buyers',
];
export const CURRENCY_KEYS = ['currency', 'currency_code', 'currencyCode', 'gmv_currency'];

function record(value: unknown): Record<string, any> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, any>)
    : null;
}

/** `product_id` / `productId` / `Product Id` đều về `productid` để so khớp theo tên. */
function normalizeKey(key: string): string {
  return key.replace(/[\s_\-.]/g, '').toLowerCase();
}

const STATS_KEY_SET = new Set(STATS_KEYS.map(normalizeKey));

/**
 * Tên khóa id theo thứ tự ưu tiên (số nhỏ = tin cậy hơn). `id` trần xếp cuối vì nhiều node lồng
 * cũng có `id` của riêng nó (category, shop, dimension…).
 */
const PRODUCT_ID_KEY_RANK: Record<string, number> = {
  productid: 0,
  productidstr: 1,
  productidstring: 1,
  ttproductid: 1,
  productcode: 2,
  itemid: 3,
  spuid: 4,
  goodsid: 5,
  skuid: 6,
  id: 7,
};

const DIMENSION_NAME_KEYS = ['key', 'name', 'field', 'dimension', 'dim_name', 'dimName', 'type'];
const DIMENSION_VALUE_KEYS = ['value', 'val', 'id', 'dim_value', 'dimValue', 'text'];

/** Chuỗi/số thô — không đi vào object (tránh nhận cả blob i18n làm id). */
function plainString(value: unknown): string | undefined {
  if (typeof value === 'string') return value.trim() || undefined;
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  if (typeof value === 'bigint') return String(value);
  return undefined;
}

/** Id có thể bị bọc `{ value: '…' }` như metric — mở một lớp là đủ. */
function idString(value: unknown): string | undefined {
  const direct = plainString(value);
  if (direct) return direct;
  const rec = record(value);
  if (!rec) return undefined;
  for (const key of ['value', 'val', 'id', 'text', 'str']) {
    const inner = plainString(rec[key]);
    if (inner) return inner;
  }
  return undefined;
}

function pickPlainString(rec: Record<string, any>, keys: string[]): string | undefined {
  for (const key of keys) {
    const text = plainString(rec[key]);
    if (text) return text;
  }
  return undefined;
}

/** Id TikTok là chuỗi số dài; vẫn nhận mã alnum để không kén khi TTS đổi định dạng. */
export function looksLikeProductId(text: string): boolean {
  if (/^\d{6,25}$/.test(text)) return true;
  return text.length >= 6 && text.length <= 64 && /^[A-Za-z0-9][A-Za-z0-9_-]*$/.test(text);
}

type IdHit = { value: string; rank: number; depth: number };

function preferHit(current: IdHit | null, next: IdHit): IdHit {
  if (!current) return next;
  if (next.rank !== current.rank) return next.rank < current.rank ? next : current;
  return next.depth < current.depth ? next : current;
}

function walkForProductId(node: unknown, depth: number, best: IdHit | null): IdHit | null {
  if (depth > 3) return best;

  if (Array.isArray(node)) {
    let found = best;
    for (const item of node) found = walkForProductId(item, depth + 1, found);
    return found;
  }

  const rec = record(node);
  if (!rec) return best;

  let found = best;

  // Dạng dimension: `{ key: 'product_id', value: '1729…' }`.
  const dimensionName = pickPlainString(rec, DIMENSION_NAME_KEYS);
  if (dimensionName) {
    const rank = PRODUCT_ID_KEY_RANK[normalizeKey(dimensionName)];
    if (rank != null) {
      const dimensionValue = pickPlainString(rec, DIMENSION_VALUE_KEYS);
      if (dimensionValue && looksLikeProductId(dimensionValue)) {
        found = preferHit(found, { value: dimensionValue, rank, depth });
      }
    }
  }

  for (const [key, raw] of Object.entries(rec)) {
    const norm = normalizeKey(key);
    if (STATS_KEY_SET.has(norm)) continue;

    const rank = PRODUCT_ID_KEY_RANK[norm];
    if (rank != null) {
      const text = idString(raw);
      if (text && looksLikeProductId(text)) {
        found = preferHit(found, { value: text, rank, depth });
      }
    }

    if (raw && typeof raw === 'object') {
      found = walkForProductId(raw, depth + 1, found);
    }
  }

  return found;
}

/**
 * Tìm `product_id` ở bất kỳ độ sâu nào của dòng Compass — TTS gói product info trong nhiều lớp
 * khác nhau tuỳ version endpoint, nên dò theo **tên khóa** thay vì cố định đường dẫn.
 */
export function findProductId(row: unknown): string | undefined {
  return walkForProductId(row, 0, null)?.value;
}

/** Bản ghi lồng bên trong (kèm chính nó) — nguồn để lấy tên / ảnh / currency. */
export function nestedRecords(row: unknown, maxDepth = 2): Record<string, any>[] {
  const root = record(row);
  if (!root) return [];

  const out: Record<string, any>[] = [root];
  const walk = (rec: Record<string, any>, depth: number): void => {
    if (depth >= maxDepth) return;
    for (const [key, raw] of Object.entries(rec)) {
      if (STATS_KEY_SET.has(normalizeKey(key))) continue;
      const inner = record(raw);
      if (!inner) continue;
      out.push(inner);
      walk(inner, depth + 1);
    }
  };
  walk(root, 0);
  return out;
}

/**
 * Đường dẫn khóa của một dòng (`product.title`, `stats_v3`…) để thông báo lỗi nói được TTS trả
 * shape gì — không phải mở devtools mới biết vì sao mapper trượt.
 */
export function describeRowKeys(row: unknown, limit = 24): string[] {
  const rec = record(row) ?? record(Array.isArray(row) ? row[0] : null);
  if (!rec) return [];

  const paths: string[] = [];
  const walk = (node: Record<string, any>, prefix: string, depth: number): void => {
    for (const [key, raw] of Object.entries(node)) {
      if (paths.length >= limit) return;
      const path = prefix ? `${prefix}.${key}` : key;
      const inner = record(raw);
      if (inner && depth < 1) {
        walk(inner, path, depth + 1);
      } else {
        paths.push(path);
      }
    }
  };
  walk(rec, '', 0);
  return paths.slice(0, limit);
}

/** Giá trị đầu tiên khác null/rỗng trong danh sách key. */
export function firstDefined(rec: Record<string, any>, keys: string[]): unknown {
  for (const key of keys) {
    if (rec[key] != null && rec[key] !== '') return rec[key];
  }
  return undefined;
}

/**
 * Số từ Compass: number, hoặc string kiểu `$1,234.56` / `12.3K` / `4.5%`.
 * Trả về đơn vị gốc (percent string → phân số).
 */
export function parseAnalyticsNumber(raw: unknown, depth = 0): number | undefined {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : undefined;
  if (typeof raw !== 'string') {
    if (depth > 2) return undefined;
    const rec = record(raw);
    if (!rec) return undefined;
    for (const key of METRIC_VALUE_KEYS) {
      if (rec[key] == null) continue;
      const parsed = parseAnalyticsNumber(rec[key], depth + 1);
      if (parsed != null) return parsed;
    }
    return undefined;
  }

  let text = raw.trim();
  if (!text || text === '-' || text === '--') return undefined;

  let percent = false;
  if (text.endsWith('%')) {
    percent = true;
    text = text.slice(0, -1).trim();
  }

  let scale = 1;
  const suffix = text.slice(-1).toUpperCase();
  if (suffix === 'K' || suffix === 'M' || suffix === 'B') {
    scale = suffix === 'K' ? 1e3 : suffix === 'M' ? 1e6 : 1e9;
    text = text.slice(0, -1);
  }

  const cleaned = text.replace(/[^0-9.+-]/g, '');
  if (!cleaned || cleaned === '-' || cleaned === '+' || cleaned === '.') return undefined;
  const n = Number(cleaned);
  if (!Number.isFinite(n)) return undefined;
  const scaled = n * scale;
  return percent ? scaled / 100 : scaled;
}

/** GMV: không âm, 2 chữ số thập phân. */
export function analyticsMoney(raw: unknown): number | undefined {
  const n = parseAnalyticsNumber(raw);
  if (n == null) return undefined;
  return Math.round(Math.max(0, n) * 100) / 100;
}

/** Đếm: không âm, làm tròn về số nguyên. */
export function analyticsInt(raw: unknown): number | undefined {
  const n = parseAnalyticsNumber(raw);
  if (n == null) return undefined;
  return Math.max(0, Math.round(n));
}

/** CTR > 1 chỉ có thể là dạng percent (2.35 = 2.35%), nên hạ về phân số. */
export function analyticsRate(raw: unknown): number | undefined {
  const n = parseAnalyticsNumber(raw);
  if (n == null || n < 0) return undefined;
  const fraction = n > 1 ? n / 100 : n;
  return Math.round(fraction * 1e6) / 1e6;
}

/** Ratio giữ nguyên dấu: số thuần = phân số, string `%` đã được chia 100. */
export function analyticsRatio(raw: unknown): number | undefined {
  const n = parseAnalyticsNumber(raw);
  if (n == null) return undefined;
  return Math.round(n * 1e6) / 1e6;
}

/**
 * `stats_v3.total` (số liệu kỳ đang xem) + `stats_v3.total_prev` (kỳ trước). Compass **không** gửi `ratio` cho từng metric mà gửi
 * cả cụm số của kỳ trước, nên % thay đổi phải tự tính từ cặp này.
 *
 * Cẩn thận: `total_contribution` là **tỷ trọng trong shop**, không phải % thay đổi — không được
 * nhặt nó làm ratio.
 */
export function pickAnalyticsStatsPair(row: Record<string, any>): {
  total: Record<string, any> | null;
  previous: Record<string, any> | null;
} {
  for (const key of STATS_KEYS) {
    const node = record(row[key]);
    if (!node) continue;

    const previous = firstRecord(node, PREVIOUS_KEYS);
    const total = firstRecord(node, TOTAL_KEYS);
    return { total: total ?? node, previous };
  }
  return { total: null, previous: null };
}

function firstRecord(node: Record<string, any>, keys: string[]): Record<string, any> | null {
  for (const key of keys) {
    const inner = record(node[key]);
    if (inner) return inner;
  }
  return null;
}

function siblingRatio(stats: Record<string, any>, key: string): unknown {
  const candidates = [
    `${key}_ratio`,
    `${key}Ratio`,
    `${key}_chain_ratio`,
    `${key}ChainRatio`,
    `${key}_ring_ratio`,
    `${key}RingRatio`,
  ];
  for (const candidate of candidates) {
    if (stats[candidate] != null) return stats[candidate];
  }
  return undefined;
}

/**
 * TikTok gửi metric theo 2 dạng: scalar (`orders: "7"`) hoặc object
 * (`gmv: { amount, currency_code }` / `{ value, chain_ratio }`). Đọc cả hai, kèm ratio dạng key anh em.
 *
 * So khớp theo tên đã chuẩn hoá (bỏ `_`, `-`, hạ chữ) nên `product_click_pv` và `productClickPv`
 * dùng chung một alias.
 */
export function readMetric(
  stats: Record<string, any>,
  keys: string[],
): { value?: unknown; ratio?: unknown } {
  const byNorm = new Map<string, string>();
  for (const original of Object.keys(stats)) {
    const norm = normalizeKey(original);
    if (!byNorm.has(norm)) byNorm.set(norm, original);
  }

  for (const alias of keys) {
    const key = byNorm.get(normalizeKey(alias));
    if (!key) continue;
    const node = stats[key];
    if (node == null || node === '') continue;
    const rec = record(node);
    if (!rec) {
      return { value: node, ratio: siblingRatio(stats, key) };
    }
    return {
      value: firstDefined(rec, METRIC_VALUE_KEYS),
      ratio: firstDefined(rec, METRIC_RATIO_KEYS) ?? siblingRatio(stats, key),
    };
  }
  return {};
}

/**
 * % thay đổi so kỳ trước. Quy ước phải khớp backend `ListingAnalyticsMetrics.Delta`:
 * kỳ trước bằng 0 mà kỳ này có số → không xác định (bỏ trống) thay vì hiện vô cực.
 */
export function analyticsDeltaRatio(current?: number, previous?: number): number | undefined {
  if (current == null || previous == null) return undefined;
  if (previous === 0) return current === 0 ? 0 : undefined;
  return Math.round(((current - previous) / previous) * 1e6) / 1e6;
}

/** Danh sách product trong `data` — TikTok đổi tên field tuỳ endpoint/version. */
export function pickAnalyticsList(data: unknown): { found: boolean; list: unknown[] } {
  if (Array.isArray(data)) return { found: true, list: data };
  const rec = record(data);
  if (!rec) return { found: false, list: [] };
  const keys = [
    'list',
    'products',
    'product_list',
    'productList',
    'rows',
    'items',
    'data',
    'list_result',
    'listResult',
  ];
  for (const key of keys) {
    if (Array.isArray(rec[key])) return { found: true, list: rec[key] as unknown[] };
    const inner = record(rec[key]);
    if (!inner) continue;
    for (const nested of keys) {
      if (Array.isArray(inner[nested])) return { found: true, list: inner[nested] as unknown[] };
    }
  }
  return { found: false, list: [] };
}

/**
 * Node phân trang. Payload thật đặt ở `data.list_control.next_pagination`
 * (`{ has_more, next_page, total_page, total }`).
 */
function pickPagination(data: unknown): Record<string, any> | null {
  const dataRec = record(data);
  if (!dataRec) return null;
  const control = record(dataRec.list_control) ?? record(dataRec.listControl);
  const candidates = [
    control ? record(control.next_pagination) ?? record(control.nextPagination) : null,
    control,
    record(dataRec.next_pagination),
    record(dataRec.nextPagination),
    record(dataRec.pagination),
    record(dataRec.page_info),
    record(dataRec.pageInfo),
  ];
  for (const candidate of candidates) {
    if (candidate) return candidate;
  }
  return null;
}

/** Tổng số product Compass báo — dùng để biết khi nào dừng phân trang. */
export function pickAnalyticsTotal(envelope: Record<string, any> | null, data: unknown): number {
  const dataRec = record(data);
  const pagination = pickPagination(data);
  const raw =
    dataRec?.total ??
    dataRec?.total_count ??
    dataRec?.totalCount ??
    dataRec?.total_num ??
    pagination?.total ??
    pagination?.total_count ??
    pagination?.totalCount ??
    envelope?.total;
  const parsed = parseAnalyticsNumber(raw);
  return parsed != null && parsed > 0 ? Math.floor(parsed) : 0;
}

/**
 * Cờ `has_more` của Compass. `undefined` = payload không nói gì, khi đó phân trang phải suy ra từ
 * số dòng nhận được.
 */
export function pickAnalyticsHasMore(data: unknown): boolean | undefined {
  const pagination = pickPagination(data);
  const raw = pagination?.has_more ?? pagination?.hasMore ?? record(data)?.has_more;
  if (typeof raw === 'boolean') return raw;
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  return undefined;
}

/**
 * Nén `stats_v3.total` trước khi gửi backend: mỗi money object (`{ amount_formatted, amount,
 * currency_code, … }`) rút còn `amount`. Giữ đủ tên metric để sau này khai thác thêm mà payload
 * không phình gấp ~5 lần khi shop có vài nghìn product.
 */
export function compactAnalyticsStats(stats: Record<string, any>): Record<string, any> {
  const out: Record<string, any> = {};
  for (const [key, value] of Object.entries(stats)) {
    const rec = record(value);
    if (!rec) {
      out[key] = value;
      continue;
    }
    const amount = firstDefined(rec, METRIC_VALUE_KEYS);
    out[key] = amount ?? value;
  }
  return out;
}
