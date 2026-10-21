import type { ProductSearchCatalog } from './productSearch';
import { sheinSearchSortQuery } from './sheinSearchSort';
import {
  roundSheinMaxPrice,
  roundSheinMinPrice,
  SHEIN_SESSION_SCOPED_PARAMS,
} from './sheinSearchUrl';

export const SHEIN_SEARCH_API_FALLBACK =
  'https://us.shein.com/bff-api/product/get_products_by_keywords' +
  '?_ver=1.1.8&_lang=en&cate_type=4&channelId=8&poskey=SearchPageSort&scene=all&search_source=1';

/** Bucket Shein BFF tôn trọng (HAR mẫu). 100 bị bỏ qua → fallback ~30. */
export const SHEIN_SEARCH_PAGE_LIMIT = 120;

export type SheinSearchHttpMethod = 'GET' | 'POST';

export interface SheinSearchSession {
  templateUrl: string;
  method?: string;
  headers: Record<string, string>;
  /** Raw body SPA gửi (JSON). Replay POST overlay keyword/page lên bản này. */
  body?: string | null;
  at: number;
}

export interface SheinSearchApiRequest {
  url: string;
  method: SheinSearchHttpMethod;
  body?: string;
}

/** SPA BFF mặc định POST (`method || "POST"`). Chỉ GET khi bắt đúng GET. */
export function normalizeSheinSearchMethod(raw?: string | null): SheinSearchHttpMethod {
  return String(raw || 'POST').trim().toUpperCase() === 'GET' ? 'GET' : 'POST';
}

export function isSheinSearchApiUrl(url?: string | null): boolean {
  return /get_products_by_keywords/i.test(String(url || ''));
}

export function isSheinSearchSessionReady(session: SheinSearchSession | null | undefined): boolean {
  if (!session?.templateUrl || !isSheinSearchApiUrl(session.templateUrl)) return false;
  return Number(session.at) > 0;
}

export function isSheinSearchAuthAbort(message?: string | null, httpStatus?: number | null): boolean {
  if (httpStatus === 401 || httpStatus === 403) return true;
  return /TokenIllegal|401|403|Please login|captcha|blocked/i.test(String(message || ''));
}

/** Thông báo UI khi Shein 401/403 — không để raw `SHEIN HTTP 403` khóa trạng thái searching. */
export function sheinSearchAuthMessage(message?: string | null, httpStatus?: number | null): string {
  const status = httpStatus === 401 || httpStatus === 403 ? httpStatus : /403/.test(String(message || '')) ? 403 : 401;
  if (status === 403) {
    return 'Shein từ chối request (HTTP 403). Mở us.shein.com, hoàn thành xác minh nếu có, rồi tìm lại.';
  }
  return 'Header Shein hết hạn. Mở lại trang search, hoàn thành xác minh nếu có, rồi tìm lại.';
}

export function isSheinSearchRiskPayload(json: unknown): boolean {
  if (!json || typeof json !== 'object') return false;
  const obj = json as Record<string, unknown>;
  const blob = `${obj.code ?? ''} ${obj.msg ?? ''} ${obj.tips ?? ''} ${obj.info ?? ''}`;
  return /\/risk\/|captcha|action\/limit/i.test(blob);
}

/**
 * Gỡ mọi tham số gắn với một lượt xem list cụ thể (`src_tab_page_id`, `src_identifier`).
 * Template lấy từ warmup nên id trong đó luôn là của lượt xem **trước**; replay mang theo
 * là tự khai sai phiên — đúng chữ ký mà Shein dùng để bắn `/risk/action/limit`.
 * Áp cho **cả** chế độ conservative: đây là param thống kê, không đổi nội dung list.
 */
function stripSessionScopedParams(u: URL): void {
  for (const key of SHEIN_SESSION_SCOPED_PARAMS) u.searchParams.delete(key);
}

/** Bỏ query rỗng — không gửi `attr_ids=` khi không có Style. */
function omitEmptySearchParams(u: URL): void {
  const keys = [...new Set([...u.searchParams.keys()])];
  for (const key of keys) {
    const kept = u.searchParams.getAll(key).filter((v) => String(v ?? '').trim().length > 0);
    u.searchParams.delete(key);
    for (const value of kept) u.searchParams.append(key, value);
  }
}

/**
 * Overlay BFF **cùng bộ param với pdsearch**: keyword/page/limit + Style `attr_*`
 * + giá / QuickShip / Sort by. Không gắn `child_cat_id` / `filter` / `cloudTag` / rating /
 * sold / Local / Club — mấy cái đó lọc sau JSON (hoặc không dùng).
 * `conservative`: giữ nguyên mọi param SPA đã ký, chỉ đổi keyword + page — dùng cho
 * forge XHR phương án cuối (khi điều hướng URL không hớt được JSON, hoặc sau khi bị 403).
 */
export function buildSheinSearchApiUrl(opts: {
  templateUrl?: string | null;
  keywords: string;
  page: number;
  catalog: ProductSearchCatalog;
  attrIds?: string[];
  attrValues?: string[];
  conservative?: boolean;
}): string {
  const keywords = String(opts.keywords || '').trim();
  const page = Math.max(1, Math.floor(opts.page || 1));
  const template = String(opts.templateUrl || '').trim();
  const base = isSheinSearchApiUrl(template) ? template : SHEIN_SEARCH_API_FALLBACK;
  let u: URL;
  try {
    u = new URL(base);
  } catch {
    u = new URL(SHEIN_SEARCH_API_FALLBACK);
  }

  if (opts.conservative) {
    u.searchParams.set('keywords', keywords);
    u.searchParams.set('page', String(page));
    stripSessionScopedParams(u);
    omitEmptySearchParams(u);
    return u.href;
  }

  if (!u.searchParams.get('_ver')) u.searchParams.set('_ver', '1.1.8');
  if (!u.searchParams.get('_lang')) u.searchParams.set('_lang', 'en');
  if (!u.searchParams.get('cate_type')) u.searchParams.set('cate_type', '4');
  if (!u.searchParams.get('channelId')) u.searchParams.set('channelId', '8');
  if (!u.searchParams.get('poskey')) u.searchParams.set('poskey', 'SearchPageSort');
  if (!u.searchParams.get('scene')) u.searchParams.set('scene', 'all');
  if (!u.searchParams.get('search_source')) u.searchParams.set('search_source', '1');

  // Xóa param SPA không overlay; `filter` xử lý riêng bên dưới theo Style.
  stripSessionScopedParams(u);
  for (const extra of [
    'tag_ids',
    'ici',
    'src_module',
    'price_input',
    'sourceStatus',
    'sort',
    'cloudTag',
    'child_cat_id',
    'page_size',
    'pageSize',
    'pagesize',
    'count',
    'per_page',
    'size',
  ]) {
    u.searchParams.delete(extra);
  }

  u.searchParams.delete('limit');
  u.searchParams.delete('Limit');
  u.searchParams.set('limit', String(SHEIN_SEARCH_PAGE_LIMIT));
  u.searchParams.set('keywords', keywords);
  u.searchParams.set('page', String(page));
  u.searchParams.set('force_suggest', '1');

  // Style: cùng 3 param như pdsearch (`attr_node_ids` = `attr_ids`), bỏ hẳn khi không tick.
  // `filter` là bản encode attr/cate của SPA — không tick Style thì xóa kẻo giữ Casual lần trước.
  const attrIds = (opts.attrIds ?? []).map((v) => String(v || '').trim()).filter(Boolean);
  const attrValues = (opts.attrValues ?? []).map((v) => String(v || '').trim()).filter(Boolean);
  const n = Math.min(attrIds.length, attrValues.length);
  if (n > 0) {
    const ids = attrIds.slice(0, n);
    const values = attrValues.slice(0, n);
    const joined = ids.join('-');
    u.searchParams.set('attr_ids', joined);
    u.searchParams.set('attr_node_ids', joined);
    u.searchParams.set('attr_values', values.join('-'));
  } else {
    u.searchParams.delete('attr_ids');
    u.searchParams.delete('attr_node_ids');
    u.searchParams.delete('attr_values');
    u.searchParams.delete('filter');
  }

  const sortQuery = sheinSearchSortQuery(opts.catalog.sheinSort);
  u.searchParams.set('source', sortQuery.source);
  u.searchParams.set('sourceStatus', sortQuery.sourceStatus);
  if (sortQuery.sort) u.searchParams.set('sort', sortQuery.sort);
  else u.searchParams.delete('sort');

  const minRaw = Number(opts.catalog.priceMinUsd);
  if (Number.isFinite(minRaw) && minRaw > 0) {
    u.searchParams.set('min_price', String(roundSheinMinPrice(minRaw)));
  } else {
    u.searchParams.delete('min_price');
  }
  const maxRaw = Number(opts.catalog.priceMaxUsd);
  if (Number.isFinite(maxRaw) && maxRaw > 0) {
    u.searchParams.set('max_price', String(roundSheinMaxPrice(maxRaw)));
  } else {
    u.searchParams.delete('max_price');
  }

  if (opts.catalog.preferQuickShip) {
    u.searchParams.set('quickship', '1');
    u.searchParams.set('tag_type', '12');
  } else {
    u.searchParams.delete('quickship');
    u.searchParams.delete('tag_type');
  }

  omitEmptySearchParams(u);
  return u.href;
}

const BODY_OVERLAY_KEYS = [
  'keywords',
  'page',
  'limit',
  'force_suggest',
  'source',
  'min_price',
  'max_price',
  'quickship',
  'tag_type',
  'sort',
  'sourceStatus',
  'attr_ids',
  'attr_node_ids',
  'attr_values',
] as const;

/** Chế độ bảo thủ: chỉ phân trang, không đụng tới param nào SPA đã ký. */
const CONSERVATIVE_OVERLAY_KEYS = ['keywords', 'page'] as const;

/**
 * Xóa khỏi body SPA: `tag_ids` đã có `quickship`/`tag_type` thay thế; `cloudTag` /
 * `child_cat_id` mình không dùng.
 * `filter` chỉ giữ khi URL overlay còn Style (`attr_*`); không tick → xóa để khỏi
 * giữ Casual/attr lần warmup trước.
 */
const BODY_REMOVE_KEYS = ['tag_ids', 'cloudTag', 'child_cat_id'] as const;

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function overlayBodyValue(key: string, raw: string, sample: unknown): string | number {
  if (typeof sample === 'number' || key === 'page' || key === 'limit') {
    const n = Number(raw);
    return Number.isFinite(n) ? n : raw;
  }
  return raw;
}

function applyOverlayToRecord(
  target: Record<string, unknown>,
  params: URLSearchParams,
  conservative?: boolean,
): void {
  // Body SPA cũng mang `src_tab_page_id`/`src_identifier` của lượt xem trước — gỡ cả 2 chế độ.
  for (const key of SHEIN_SESSION_SCOPED_PARAMS) delete target[key];
  if (!conservative) {
    for (const key of BODY_REMOVE_KEYS) delete target[key];
    const hasStyle =
      !!String(params.get('attr_ids') || '').trim() &&
      !!String(params.get('attr_values') || '').trim();
    if (!hasStyle) delete target.filter;
  }
  for (const key of conservative ? CONSERVATIVE_OVERLAY_KEYS : BODY_OVERLAY_KEYS) {
    const raw = params.get(key);
    if (raw == null || raw === '') {
      delete target[key];
      continue;
    }
    target[key] = overlayBodyValue(key, raw, target[key]);
  }
}

/** Overlay bộ param pdsearch vào JSON body SPA (kể cả `queryObj`). Không gắn `filter`/`tag_ids`. */
export function overlaySheinSearchBody(
  templateBody: string | null | undefined,
  params: URLSearchParams,
  conservative?: boolean,
): string | undefined {
  const raw = String(templateBody || '').trim();
  if (!raw.startsWith('{')) return undefined;
  try {
    const root = asRecord(JSON.parse(raw) as unknown);
    if (!root) return undefined;
    applyOverlayToRecord(root, params, conservative);
    const nested = asRecord(root.queryObj);
    if (nested) applyOverlayToRecord(nested, params, conservative);
    return JSON.stringify(root);
  } catch {
    return undefined;
  }
}

/**
 * Replay đúng method SPA. POST + JSON body nếu warmup bắt được body;
 * POST không body = axios `params` trên URL (query). Không hardcode GET.
 */
export function buildSheinSearchApiRequest(opts: {
  templateUrl?: string | null;
  method?: string | null;
  templateBody?: string | null;
  keywords: string;
  page: number;
  catalog: ProductSearchCatalog;
  attrIds?: string[];
  attrValues?: string[];
  conservative?: boolean;
}): SheinSearchApiRequest {
  const method = normalizeSheinSearchMethod(opts.method);
  const url = buildSheinSearchApiUrl(opts);
  if (method === 'GET') return { url, method };
  const params = new URL(url).searchParams;
  const body = overlaySheinSearchBody(opts.templateBody, params, opts.conservative);
  if (!body) return { url, method };
  const stripped = new URL(url);
  for (const key of opts.conservative ? CONSERVATIVE_OVERLAY_KEYS : BODY_OVERLAY_KEYS) {
    stripped.searchParams.delete(key);
  }
  omitEmptySearchParams(stripped);
  return { url: stripped.href, method, body };
}
