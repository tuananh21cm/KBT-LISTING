/**
 * Content script trên seller-us.tiktok.com.
 * - GET /api/v3/seller/common/get (shop + seller.status account 2/3 + shop_status)
 * - GET /passport/account/info/v2/ (email/mobile/name/block — có thể mask)
 * - GET /api/v1/seller/onboard/v2/config/get (product_categories)
 * - GET /api/v1/product/product_creation/preload (logistics + publish_limit)
 * - POST /api/v1/pay/onboard_info/get (company + address + owner, email không mask)
 * - GET /api/v1/seller/settlement/account/get (bank_account_number mask)
 * - GET /api/v1/pay/statement/stat/info (On hold + bill_finish_period_in_days)
 * - GET /api/v1/product/local/products/list (tab violation, page_size=1 — fail-soft)
 * - POST /api/v3/insights/seller/ttp/product/list (Compass Product analysis, phân trang)
 * credentials:include — không nhồi Cookie / X-Bogus từ curl.
 *
 * Chrome content_scripts classic: chỉ được import module mà Rollup inline vào content.js
 * (`shared/listingAnalyticsStats`, `shared/opportunityLeadList` — không entry nào khác dùng chúng).
 */
/* eslint-disable @typescript-eslint/no-explicit-any */
/// <reference types="chrome" />

import {
  ATC_BUYER_KEYS,
  ATC_UNIT_KEYS,
  CLICK_KEYS,
  CTR_KEYS,
  CURRENCY_KEYS,
  GMV_KEYS,
  IMPRESSION_KEYS,
  ORDER_KEYS,
  analyticsDeltaRatio,
  analyticsInt,
  analyticsMoney,
  analyticsRate,
  analyticsRatio,
  compactAnalyticsStats,
  describeRowKeys,
  findProductId,
  firstDefined,
  nestedRecords,
  pickAnalyticsHasMore,
  pickAnalyticsList,
  pickAnalyticsStatsPair,
  pickAnalyticsTotal,
  readMetric,
} from '../shared/listingAnalyticsStats';
import {
  buildLeadListRequestBodies,
  describeLeadListShape,
  isEmptyLeadPayload,
  pickLeadList,
  pickLeadTotal,
} from '../shared/opportunityLeadList';
import { mapHealthRecord, type SyncHealthRecordItem } from '../shared/healthRecords';
import {
  emptyShopScoreSnapshot,
  hasAnyShopScore,
  mapAhrScore,
  mapSesScoreOverview,
  type ShopScoreSnapshot,
} from '../shared/shopScores';

const SELLER_COMMON_GET_PATH = '/api/v3/seller/common/get';
const SELLER_COMMON_GET_QUERY =
  'need_verify_account=true&default_region=US&version=3';
const PASSPORT_ACCOUNT_INFO_PATH = '/passport/account/info/v2/';
const PASSPORT_ACCOUNT_INFO_QUERY =
  'get_info_type=2&aid=4068&account_sdk_source=web&sdk_version=2.1.3-tiktokbeta.1&language=en';
const ONBOARD_CONFIG_PATH = '/api/v1/seller/onboard/v2/config/get';
const PRODUCT_PRELOAD_PATH = '/api/v1/product/product_creation/preload';
const PAY_ONBOARD_INFO_PATH = '/api/v1/pay/onboard_info/get';
const PAY_ONBOARD_INFO_BODY = JSON.stringify({ user_type: 1, biz_type: 1 });
const SETTLEMENT_ACCOUNT_PATH = '/api/v1/seller/settlement/account/get';
const STATEMENT_STAT_INFO_PATH = '/api/v1/pay/statement/stat/info';
const PRODUCTS_LIST_PATH = '/api/v1/product/local/products/list';
const VIOLATION_RECORDS_PATH =
  '/api/v1/seller/growth_center/shop/violation/records/list';
const WARNING_RECORDS_PATH =
  '/api/v1/seller/growth_center/shop/warning/records/list';
const HEALTH_RECORDS_PAGE_SIZE = 20;
const MAX_HEALTH_RECORDS_PER_TYPE = 100;
const AHR_SCORE_PATH = '/api/v1/account/ahr/score';
const SES_SCORE_OVERVIEW_PATH = '/api/v1/seller/experience-score/score-overview';
/** Khớp `detectIdentityVideoVerificationLock` trong shared/shop.ts (content standalone). */
const IDENTITY_VIDEO_VERIFICATION_REASON_KEY =
  'post_onboarding_identity_video_verification_reason';
const SELLER_API_BASE_QUERY =
  'locale=en&language=en&aid=4068&app_name=i18n_ecom_shop&device_platform=web&cookie_enabled=true';
/** Không treo vô hạn khi seller-us còn request ngầm. */
const FETCH_TIMEOUT_MS = 12_000;

interface JsonResult {
  ok: boolean;
  status: number;
  raw: unknown;
  message?: string;
  /** Chỉ preload: chặng nào lấy được số (`bridge`, `isolated GET+csrf`…) để đọc log sync. */
  source?: string;
}

/* ─── Bridge MAIN world cho `/api/v1/product/*` (public/sellerProductBridge.js) ─── */

const PRODUCT_BRIDGE_PING = 'kb-tts-product-bridge-ping';
const PRODUCT_BRIDGE_PONG = 'kb-tts-product-bridge-pong';
const PRODUCT_BRIDGE_FETCH = 'kb-tts-product-fetch';
const PRODUCT_BRIDGE_RESULT = 'kb-tts-product-fetch-result';
const PRELOAD_CAPTURE_MSG = 'kb-tts-preload-capture';
const HARVEST_GET_MSG = 'kb-tts-harvest-get';
const HARVEST_RESULT_MSG = 'kb-tts-harvest-result';
/** Đọc từ sessionStorage của page nên trả gần như tức thì. */
const HARVEST_GET_TIMEOUT_MS = 1_000;
/** Số liệu shop đổi chậm; 10 phút đủ tươi cho một lượt sync mà tiết kiệm hẳn request. */
const HARVEST_TTL_MS = 10 * 60_000;
/** Ping đi-về trong cùng page nên chỉ vài ms; hết hạn nghĩa là script MAIN không được nạp. */
const PRODUCT_BRIDGE_PING_TIMEOUT_MS = 500;
/** Phải nhỏ hơn timeout của message RUN/REFRESH ở background để còn chỗ cho fallback isolated. */
const PRODUCT_BRIDGE_FETCH_TIMEOUT_MS = 7_000;

let bridgeSeq = 0;
/**
 * `true` giữ mãi (script MAIN đã nạp thì không mất). `false` chỉ giữ tạm: background có thể
 * inject hook vào tab đang mở (`chrome.scripting`, world MAIN) nên phải cho probe lại.
 */
let bridgeAlive: boolean | null = null;
let bridgeDeadAt = 0;
/** Gộp các lần ping song song (bundle gọi nhiều product API cùng lúc) về một lượt. */
let bridgeProbe: Promise<boolean> | null = null;
let bridgeProbeSeq = 0;
const BRIDGE_DEAD_TTL_MS = 20_000;

function nextBridgeId(): string {
  bridgeSeq += 1;
  return `${Date.now().toString(36)}-${bridgeSeq}`;
}

interface BridgeReply {
  ok?: boolean;
  status?: number;
  raw?: unknown;
  message?: string;
  entries?: Record<string, { raw?: unknown; at?: number; sellerId?: string } | null>;
}

/**
 * Gửi một message sang MAIN world và chờ reply mang đúng `id`. Hết hạn thì `null` để phía gọi
 * rơi xuống fallback isolated, thay vì làm cả bước sync trượt.
 */
function askProductBridge(
  request: Record<string, unknown>,
  resultSource: string,
  timeoutMs: number,
): Promise<BridgeReply | null> {
  return new Promise((resolve) => {
    let done = false;
    const finish = (value: BridgeReply | null) => {
      if (done) return;
      done = true;
      window.clearTimeout(timer);
      window.removeEventListener('message', onMessage);
      resolve(value);
    };
    const onMessage = (event: MessageEvent) => {
      if (event.source !== window) return;
      const data = event.data as { source?: string; id?: string } | null;
      if (!data || data.source !== resultSource || data.id !== request.id) return;
      finish(data as BridgeReply);
    };
    const timer = window.setTimeout(() => finish(null), timeoutMs);
    window.addEventListener('message', onMessage);
    try {
      window.postMessage(request, location.origin);
    } catch {
      finish(null);
    }
  });
}

/** Chỉ lượt probe mới nhất được ghi kết quả: lượt cũ (bắt đầu trước khi inject) hay nói «chết». */
function probeProductBridge(): Promise<boolean> {
  const token = (bridgeProbeSeq += 1);
  const probe = askProductBridge(
    { source: PRODUCT_BRIDGE_PING, id: nextBridgeId() },
    PRODUCT_BRIDGE_PONG,
    PRODUCT_BRIDGE_PING_TIMEOUT_MS,
  ).then((reply) => {
    const alive = reply != null;
    if (token === bridgeProbeSeq) {
      bridgeAlive = alive;
      bridgeDeadAt = alive ? 0 : Date.now();
      bridgeProbe = null;
    }
    return alive;
  });
  bridgeProbe = probe;
  return probe;
}

function isProductBridgeAlive(force?: boolean): Promise<boolean> {
  if (force) return probeProductBridge();
  if (bridgeAlive === true) return Promise.resolve(true);
  if (bridgeAlive === false && Date.now() - bridgeDeadAt < BRIDGE_DEAD_TTL_MS) {
    return Promise.resolve(false);
  }
  return bridgeProbe ?? probeProductBridge();
}

/** Khớp `HARVEST_TARGETS[].key` trong `public/sellerProductBridge.js`. */
type HarvestKey =
  | 'seller_common'
  | 'passport'
  | 'onboard'
  | 'preload'
  | 'pay_onboard'
  | 'settlement'
  | 'statement_stat'
  | 'products_list'
  | 'ahr'
  | 'ses';

type HarvestMap = Partial<Record<HarvestKey, JsonResult>>;
type HarvestSnapshot = Record<string, { raw?: unknown; at?: number; sellerId?: string } | null>;

/**
 * Payload các endpoint không tham số mà **trang tự gọi** khi người dùng đi lại trong Seller
 * Center. Dùng lại được thì bước sync không tốn request nào và không bị gateway chặn.
 * Hỏi MAIN world một lần cho cả lượt sync, lọc TTL + `sellerId` ở `pickHarvest`.
 */
async function readHarvestSnapshot(): Promise<HarvestSnapshot> {
  if (!(await isProductBridgeAlive())) return {};
  const reply = await askProductBridge(
    { source: HARVEST_GET_MSG, id: nextBridgeId() },
    HARVEST_RESULT_MSG,
    HARVEST_GET_TIMEOUT_MS,
  );
  return reply?.entries ?? {};
}

/** Tách khỏi lượt đọc để gọi lại được sau khi `sellerId` mới lộ ra từ seller/common/get. */
function pickHarvest(snapshot: HarvestSnapshot, sellerId?: string): HarvestMap {
  const want = (sellerId || '').trim();
  const now = Date.now();
  const map: HarvestMap = {};
  for (const key of Object.keys(snapshot) as HarvestKey[]) {
    const entry = snapshot[key];
    if (!entry || entry.raw == null) continue;
    if (!entry.at || now - entry.at > HARVEST_TTL_MS) continue;
    // Người dùng đổi shop trong cùng tab thì entry cũ là số của shop khác.
    if (want && entry.sellerId && entry.sellerId !== want) continue;
    map[key] = { ok: true, status: 200, raw: entry.raw, source: 'page-harvest' };
  }
  return map;
}

/** Bao nhiêu slot của bước này dùng payload hớt được — để note sync log nói rõ nguồn. */
function countHarvested(results: readonly (JsonResult | undefined)[]): number {
  return results.filter((res) => res?.source === 'page-harvest').length;
}

/**
 * Gọi product API bằng `window.fetch` của page để lớp bọc webmssdk tự ký URL
 * (`X-Bogus` / `_signature` / `msToken`) — isolated world không ký được nên bị gateway
 * trả `code=10000`. `null` = MAIN world im lặng, phía gọi tự fallback.
 */
async function fetchViaProductBridge(
  url: string,
  init?: { method?: string; body?: string },
): Promise<JsonResult | null> {
  if (!(await isProductBridgeAlive())) return null;
  const reply = await askProductBridge(
    {
      source: PRODUCT_BRIDGE_FETCH,
      id: nextBridgeId(),
      url,
      method: init?.method || 'GET',
      body: init?.body,
    },
    PRODUCT_BRIDGE_RESULT,
    PRODUCT_BRIDGE_FETCH_TIMEOUT_MS,
  );
  if (!reply) return null;
  return {
    ok: reply.ok === true,
    status: Number(reply.status) || 0,
    raw: reply.raw ?? null,
    message: reply.message,
  };
}

interface FetchResult {
  ok: boolean;
  status?: number;
  url: string;
  at: number;
  raw: unknown;
  message?: string;
  accountOk?: boolean;
  accountRaw?: unknown;
  accountMessage?: string;
  onboardOk?: boolean;
  onboardRaw?: unknown;
  onboardMessage?: string;
  preloadOk?: boolean;
  preloadRaw?: unknown;
  preloadMessage?: string;
  preloadSource?: string;
  harvestUsed?: number;
  harvestTotal?: number;
  payOnboardOk?: boolean;
  payOnboardRaw?: unknown;
  payOnboardMessage?: string;
  settlementOk?: boolean;
  settlementRaw?: unknown;
  settlementMessage?: string;
  statementStatOk?: boolean;
  statementStatRaw?: unknown;
  statementStatMessage?: string;
  /** Chỉ true khi products/list chứa key khóa identity video. */
  identityVideoVerificationLock?: boolean;
}

function readCookie(name: string): string | undefined {
  const parts = document.cookie.split(';');
  for (const part of parts) {
    const idx = part.indexOf('=');
    if (idx < 0) continue;
    const key = part.slice(0, idx).trim();
    if (key !== name) continue;
    return decodeURIComponent(part.slice(idx + 1).trim());
  }
  return undefined;
}

function resolveSellerId(): string | undefined {
  return (
    readCookie('oec_seller_id_unified_seller_env') ||
    readCookie('global_seller_id_unified_seller_env') ||
    undefined
  );
}

function buildSellerUrl(): string {
  return `${location.origin}${SELLER_COMMON_GET_PATH}?${SELLER_COMMON_GET_QUERY}`;
}

function buildPassportUrl(): string {
  return `${location.origin}${PASSPORT_ACCOUNT_INFO_PATH}?${PASSPORT_ACCOUNT_INFO_QUERY}`;
}

function buildSellerApiUrl(path: string, sellerId: string): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
  return `${location.origin}${path}?${q.toString()}`;
}

async function fetchJson(
  url: string,
  headers: Record<string, string>,
  init?: { method?: string; body?: string; timeoutMs?: number },
): Promise<JsonResult> {
  const timeoutMs = init?.timeoutMs && init.timeoutMs > 0 ? init.timeoutMs : FETCH_TIMEOUT_MS;
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetch(url, {
      method: init?.method || 'GET',
      credentials: 'include',
      signal: controller.signal,
      headers,
      body: init?.body,
    });
    const raw = await res.json().catch(() => null);
    return { ok: res.ok, status: res.status, raw };
  } catch (err: unknown) {
    const aborted =
      (err && typeof err === 'object' && 'name' in err && (err as { name?: string }).name === 'AbortError') ||
      (typeof DOMException !== 'undefined' && err instanceof DOMException && err.name === 'AbortError');
    const message = aborted
      ? `Timeout ${timeoutMs / 1000}s — ${url} không trả lời.`
      : err && typeof err === 'object' && 'message' in err && typeof (err as Error).message === 'string'
        ? (err as Error).message
        : 'Fetch thất bại.';
    return { ok: false, status: 0, raw: null, message };
  } finally {
    window.clearTimeout(timer);
  }
}

function fetchSellerApiGet(path: string, sellerId: string, headers: Record<string, string>) {
  return fetchJson(buildSellerApiUrl(path, sellerId), headers);
}

/**
 * Header cho namespace `/api/v1/product/*` khi phải fetch từ isolated world. Chỉ là phương án
 * chót: CSRF lấy từ cookie không phải token gateway phát, và không có `X-Bogus`/`_signature`
 * nên hay bị trả `code=10000`. Đường chính là bridge MAIN world.
 */
function productApiHeaders(extra?: Record<string, string>): Record<string, string> {
  const headers: Record<string, string> = {
    accept: '*/*',
    'accept-language': 'en-US,en;q=0.9',
    'x-tt-oec-region': 'US',
    ...extra,
  };
  const csrf =
    readCookie('tt_csrf_token') ||
    readCookie('passport_csrf_token') ||
    readCookie('passport_csrf_token_default') ||
    '';
  if (csrf) {
    headers['x-secsdk-csrf-token'] = csrf;
    headers['x-tt-passport-csrf-token'] = csrf;
  }
  return headers;
}

/**
 * Biến thể fetch isolated cho preload. TikTok đổi cách nhận request theo version Seller Center
 * nên thử lần lượt, dừng ở lần đầu có `data`. Chỉ chạy khi bridge MAIN world không dùng được.
 */
const PRODUCT_PRELOAD_ATTEMPTS: {
  label: string;
  run: (sellerId: string, timeoutMs: number) => Promise<JsonResult>;
}[] = [
  {
    label: 'GET+csrf',
    run: (sellerId, timeoutMs) =>
      fetchJson(buildSellerApiUrl(PRODUCT_PRELOAD_PATH, sellerId), productApiHeaders(), {
        timeoutMs,
      }),
  },
  {
    label: 'GET+shop_region',
    run: (sellerId, timeoutMs) => {
      const url = new URL(buildSellerApiUrl(PRODUCT_PRELOAD_PATH, sellerId));
      url.searchParams.set('shop_region', 'US');
      url.searchParams.set('user_is_login', 'true');
      return fetchJson(url.toString(), productApiHeaders(), { timeoutMs });
    },
  },
  {
    label: 'POST{}',
    run: (sellerId, timeoutMs) =>
      fetchJson(
        buildSellerApiUrl(PRODUCT_PRELOAD_PATH, sellerId),
        productApiHeaders({ 'content-type': 'application/json' }),
        { method: 'POST', body: '{}', timeoutMs },
      ),
  },
];

/** Ngân sách cho cả thang isolated, để bridge + fallback vẫn kịp trước timeout của background. */
const PRELOAD_ISOLATED_BUDGET_MS = 9_000;
const PRELOAD_ISOLATED_ATTEMPT_MS = 4_000;
/** products/list chỉ có một biến thể isolated; bridge 7s + 5s vẫn dưới 14s của background. */
const PRODUCTS_LIST_ISOLATED_MS = 5_000;

function describePreloadFailure(res: JsonResult): string {
  const raw = res.raw as { code?: number; message?: string } | null;
  if (!res.ok) return res.message || `HTTP ${res.status}`;
  return `code=${raw?.code ?? '—'}${raw?.message ? ` ${raw.message}` : ''}`;
}

function hasPreloadData(res: JsonResult): boolean {
  const raw = res.raw as { code?: number; data?: unknown } | null;
  if (!res.ok || !raw) return false;
  const codeOk = raw.code == null || Number(raw.code) === 0;
  return codeOk && raw.data != null;
}

/**
 * Bridge trước (request được page ký), im lặng hoặc bị từ chối thì mới xuống thang isolated.
 * Trượt hết thì `message` liệt kê từng chặng để log sync chỉ đúng chỗ tắc.
 */
async function fetchProductPreloadRaw(sellerId: string): Promise<JsonResult> {
  const notes: string[] = [];
  const viaBridge = await fetchViaProductBridge(buildSellerApiUrl(PRODUCT_PRELOAD_PATH, sellerId));
  if (viaBridge && hasPreloadData(viaBridge)) return { ...viaBridge, source: 'bridge' };
  notes.push(viaBridge ? `bridge: ${describePreloadFailure(viaBridge)}` : 'bridge: không trả lời');

  // Bridge trả lời mà vẫn trượt tức request có chữ ký của page cũng bị từ chối — biến thể
  // isolated khó hơn được, chỉ thử một lần cho kịp deadline; MAIN im lặng thì quét cả thang.
  const attempts = viaBridge ? PRODUCT_PRELOAD_ATTEMPTS.slice(0, 1) : PRODUCT_PRELOAD_ATTEMPTS;
  const deadline = Date.now() + (viaBridge ? PRELOAD_ISOLATED_ATTEMPT_MS + 500 : PRELOAD_ISOLATED_BUDGET_MS);
  for (const attempt of attempts) {
    const remaining = deadline - Date.now();
    if (remaining < 1_000) {
      notes.push('hết thời gian cho biến thể còn lại');
      break;
    }
    const res = await attempt.run(sellerId, Math.min(PRELOAD_ISOLATED_ATTEMPT_MS, remaining));
    if (hasPreloadData(res)) return { ...res, source: `isolated ${attempt.label}` };
    notes.push(`isolated ${attempt.label}: ${describePreloadFailure(res)}`);
  }
  return { ok: false, status: 0, raw: null, message: `preload bị từ chối — ${notes.join(' · ')}` };
}

/**
 * Chỉ gọi product_creation/preload — dùng khi bước Info thiếu `publish_limit`,
 * để không phải chạy lại cả chùm 8 request của seller/common/get.
 */
async function fetchProductPreload(sellerIdInput: string): Promise<{
  ok: boolean;
  status?: number;
  message?: string;
  sellerId?: string;
  raw?: unknown;
  source?: string;
}> {
  const sellerId = (sellerIdInput || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Không đọc được seller_id từ cookie.' };
  }
  const res = await fetchProductPreloadRaw(sellerId);
  return {
    ok: res.ok,
    status: res.status,
    sellerId,
    raw: res.raw,
    message: res.ok ? undefined : res.message,
    source: res.source,
  };
}

function buildStatementStatInfoUrl(sellerId: string): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  q.set('app_id', '4068');
  q.set('timezone_name', 'America/Los_Angeles');
  q.set('amount_stat_type', '1');
  q.set('statement_version', '1');
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
  return `${location.origin}${STATEMENT_STAT_INFO_PATH}?${q.toString()}`;
}

function fetchStatementStatInfo(sellerId: string, headers: Record<string, string>) {
  return fetchJson(buildStatementStatInfoUrl(sellerId), headers);
}

function fetchPayOnboardInfo(sellerId: string, headers: Record<string, string>) {
  return fetchJson(
    buildSellerApiUrl(PAY_ONBOARD_INFO_PATH, sellerId),
    {
      ...headers,
      'content-type': 'application/json',
    },
    { method: 'POST', body: PAY_ONBOARD_INFO_BODY },
  );
}

function buildProductsListUrl(sellerId: string): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  q.set('tab_id', '19');
  q.set('page_number', '1');
  q.set('page_size', '1');
  q.set('sku_number', '1');
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
  return `${location.origin}${PRODUCTS_LIST_PATH}?${q.toString()}`;
}

/**
 * Bridge trước rồi mới isolated. Tổng thời gian phải nằm dưới timeout message của background,
 * nên nhánh isolated bị cắt ngắn hơn mặc định.
 */
async function fetchProductsList(sellerId: string): Promise<JsonResult> {
  const url = buildProductsListUrl(sellerId);
  const viaBridge = await fetchViaProductBridge(url);
  if (viaBridge?.ok && viaBridge.raw != null) return { ...viaBridge, source: 'bridge' };
  return fetchJson(url, productApiHeaders(), { timeoutMs: PRODUCTS_LIST_ISOLATED_MS });
}

/** Shop-level 365-day lock — key lặp trên mọi SKU khi TTS enforce. */
function detectIdentityVideoVerificationLock(raw: unknown): boolean {
  if (!raw || typeof raw !== 'object') return false;
  const data = (raw as { data?: { products?: unknown } }).data;
  const products = data?.products;
  if (!Array.isArray(products)) return false;
  for (const product of products) {
    if (!product || typeof product !== 'object') continue;
    const traffic = (product as {
      suspend_reason?: { traffic_control_reasons?: unknown };
    }).suspend_reason?.traffic_control_reasons;
    if (!Array.isArray(traffic)) continue;
    for (const reason of traffic) {
      if (!reason || typeof reason !== 'object') continue;
      const subs = (reason as { sub_reasons?: unknown }).sub_reasons;
      if (!Array.isArray(subs)) continue;
      for (const sub of subs) {
        if (!sub || typeof sub !== 'object') continue;
        const key = (sub as { reason?: { key?: unknown } }).reason?.key;
        if (key === IDENTITY_VIDEO_VERIFICATION_REASON_KEY) return true;
      }
    }
  }
  return false;
}

async function fetchSellerCommonGet(): Promise<FetchResult> {
  const url = buildSellerUrl();
  const at = Date.now();
  const passportUrl = buildPassportUrl();
  const csrf =
    readCookie('passport_csrf_token') || readCookie('passport_csrf_token_default') || '';
  const passportHeaders: Record<string, string> = {
    accept: 'application/json, text/javascript',
    'accept-language': 'en-US,en;q=0.9',
  };
  if (csrf) passportHeaders['x-tt-passport-csrf-token'] = csrf;

  let sellerId = resolveSellerId();
  const commonHeaders = {
    accept: '*/*',
    'accept-language': 'en-US,en;q=0.9',
  };
  const oecHeaders = {
    ...commonHeaders,
    'x-tt-oec-region': 'US',
  };

  const snapshot = await readHarvestSnapshot();
  let harvest = pickHarvest(snapshot, sellerId);
  /** Có payload hớt được thì **không** gọi request — đó là toàn bộ mục đích của harvest. */
  const slot = (hit: JsonResult | undefined, run: () => Promise<JsonResult>) =>
    hit ? Promise.resolve(hit) : run();

  const tasks: Promise<JsonResult>[] = [
    slot(harvest.seller_common, () => fetchJson(url, commonHeaders)),
    slot(harvest.passport, () => fetchJson(passportUrl, passportHeaders)),
  ];
  if (sellerId) {
    const sid = sellerId;
    tasks.push(
      slot(harvest.onboard, () => fetchSellerApiGet(ONBOARD_CONFIG_PATH, sid, oecHeaders)),
      slot(harvest.preload, () => fetchProductPreloadRaw(sid)),
      slot(harvest.pay_onboard, () => fetchPayOnboardInfo(sid, oecHeaders)),
      slot(harvest.settlement, () => fetchSellerApiGet(SETTLEMENT_ACCOUNT_PATH, sid, oecHeaders)),
      slot(harvest.statement_stat, () => fetchStatementStatInfo(sid, oecHeaders)),
      slot(harvest.products_list, () => fetchProductsList(sid)),
    );
  }

  const results = await Promise.all(tasks);
  const shopRes = results[0];
  const accountRes = results[1];
  let onboardRes = sellerId ? results[2] : undefined;
  let preloadRes = sellerId ? results[3] : undefined;
  let payOnboardRes = sellerId ? results[4] : undefined;
  let settlementRes = sellerId ? results[5] : undefined;
  let statementStatRes = sellerId ? results[6] : undefined;
  let productsListRes = sellerId ? results[7] : undefined;

  const raw = shopRes.raw as {
    code?: number;
    message?: string;
    data?: { seller?: { seller_id?: string } };
  } | null;
  const ok =
    shopRes.ok && !!raw && Number(raw.code) === 0 && !!raw.data?.seller;

  const sidFromShop = String(raw?.data?.seller?.seller_id || '').trim() || undefined;
  if (!sellerId && sidFromShop) {
    sellerId = sidFromShop;
  }
  if (
    sellerId &&
    (!onboardRes ||
      !preloadRes ||
      !payOnboardRes ||
      !settlementRes ||
      !statementStatRes ||
      !productsListRes)
  ) {
    const sid = sellerId;
    // sellerId chỉ lộ ra ở đây (cookie trống) nên lọc lại harvest theo shop vừa biết.
    harvest = pickHarvest(snapshot, sid);
    const [ob, pr, pay, st, ss, pl] = await Promise.all([
      onboardRes ??
        slot(harvest.onboard, () => fetchSellerApiGet(ONBOARD_CONFIG_PATH, sid, oecHeaders)),
      preloadRes ?? slot(harvest.preload, () => fetchProductPreloadRaw(sid)),
      payOnboardRes ?? slot(harvest.pay_onboard, () => fetchPayOnboardInfo(sid, oecHeaders)),
      settlementRes ??
        slot(harvest.settlement, () => fetchSellerApiGet(SETTLEMENT_ACCOUNT_PATH, sid, oecHeaders)),
      statementStatRes ?? slot(harvest.statement_stat, () => fetchStatementStatInfo(sid, oecHeaders)),
      productsListRes ?? slot(harvest.products_list, () => fetchProductsList(sid)),
    ]);
    onboardRes = ob;
    preloadRes = pr;
    payOnboardRes = pay;
    settlementRes = st;
    statementStatRes = ss;
    productsListRes = pl;
  }

  const accountRaw = accountRes.raw as {
    message?: string;
    data?: { email?: string; mobile?: string; name?: string };
  } | null;
  const accountOk =
    accountRes.ok &&
    !!accountRaw &&
    String(accountRaw.message || '').toLowerCase() === 'success' &&
    !!accountRaw.data;

  const onboardRaw = (onboardRes?.raw ?? null) as {
    code?: number;
    message?: string;
    data?: { customized_config?: { product_categories?: unknown[] } };
  } | null;
  const onboardOk =
    !!onboardRes?.ok &&
    !!onboardRaw &&
    Number(onboardRaw.code) === 0 &&
    Array.isArray(onboardRaw.data?.customized_config?.product_categories);

  const preloadRaw = (preloadRes?.raw ?? null) as {
    code?: number;
    message?: string;
    data?: {
      warehouse_logistics_services?: unknown[];
      publish_limit?: { total_limit?: number | string; published_num?: number | string };
    };
  } | null;
  // Có version preload không trả `code`; chỉ loại khi code có mà ≠ 0 — nếu không sẽ bỏ luôn
  // publish_limit hợp lệ. Việc thiếu chính field publish_limit do background báo (đọc raw).
  const preloadOk =
    !!preloadRes?.ok &&
    !!preloadRaw &&
    (preloadRaw.code == null || Number(preloadRaw.code) === 0) &&
    !!preloadRaw.data;

  const payOnboardRaw = (payOnboardRes?.raw ?? null) as {
    code?: number;
    message?: string;
    data?: { enterprise?: unknown };
  } | null;
  const payOnboardOk =
    !!payOnboardRes?.ok &&
    !!payOnboardRaw &&
    Number(payOnboardRaw.code) === 0 &&
    !!payOnboardRaw.data?.enterprise;

  const settlementRaw = (settlementRes?.raw ?? null) as {
    code?: number;
    message?: string;
    data?: { bank_account_number?: string };
  } | null;
  const settlementOk =
    !!settlementRes?.ok &&
    !!settlementRaw &&
    Number(settlementRaw.code) === 0 &&
    !!(settlementRaw.data?.bank_account_number || '').trim();

  const statementStatRaw = (statementStatRes?.raw ?? null) as {
    code?: number;
    message?: string;
    data?: { to_settle_amount_stat?: { amount?: { amount?: string | number } } };
  } | null;
  const statementStatOk =
    !!statementStatRes?.ok &&
    !!statementStatRaw &&
    Number(statementStatRaw.code) === 0 &&
    statementStatRaw.data?.to_settle_amount_stat?.amount?.amount != null;

  const identityVideoVerificationLock = detectIdentityVideoVerificationLock(
    productsListRes?.raw,
  );

  // Chỉ đếm slot đã chạy: thiếu sellerId thì 6 slot sau không chạy, tính vào tổng là nói sai.
  const infoSlots = [
    shopRes,
    accountRes,
    onboardRes,
    preloadRes,
    payOnboardRes,
    settlementRes,
    statementStatRes,
    productsListRes,
  ].filter((res): res is JsonResult => !!res);

  return {
    ok,
    harvestUsed: countHarvested(infoSlots),
    harvestTotal: infoSlots.length,
    status: shopRes.status || undefined,
    url,
    at,
    raw,
    message: ok
      ? undefined
      : shopRes.message ||
        (raw && typeof raw.message === 'string' && raw.message
          ? String(raw.message)
          : shopRes.ok
            ? 'Response seller/common/get không hợp lệ (chưa login seller-us?).'
            : `HTTP ${shopRes.status}`),
    accountOk,
    accountRaw,
    accountMessage: accountOk
      ? undefined
      : accountRes.message ||
        (accountRes.ok
          ? 'Response passport/account/info không hợp lệ.'
          : `HTTP ${accountRes.status}`),
    onboardOk,
    onboardRaw,
    onboardMessage: onboardOk
      ? undefined
      : !sellerId
        ? 'Thiếu seller_id để gọi onboard config.'
        : onboardRes?.message ||
          (onboardRes?.ok
            ? 'Response onboard config thiếu product_categories.'
            : `HTTP ${onboardRes?.status ?? 0}`),
    preloadOk,
    preloadRaw,
    preloadSource: preloadRes?.source,
    preloadMessage: preloadOk
      ? undefined
      : !sellerId
        ? 'Thiếu seller_id để gọi product preload.'
        : preloadRes?.message ||
          (preloadRes?.ok
            ? `Response product preload code=${preloadRaw?.code ?? '—'}${
                preloadRaw?.message ? ` (${preloadRaw.message})` : ''
              } thiếu data.`
            : `HTTP ${preloadRes?.status ?? 0}`),
    payOnboardOk,
    payOnboardRaw,
    payOnboardMessage: payOnboardOk
      ? undefined
      : !sellerId
        ? 'Thiếu seller_id để gọi pay onboard info.'
        : payOnboardRes?.message ||
          (payOnboardRes?.ok
            ? 'Response pay/onboard_info thiếu enterprise.'
            : `HTTP ${payOnboardRes?.status ?? 0}`),
    settlementOk,
    settlementRaw,
    settlementMessage: settlementOk
      ? undefined
      : !sellerId
        ? 'Thiếu seller_id để gọi settlement account.'
        : settlementRes?.message ||
          (settlementRes?.ok
            ? 'Response settlement/account thiếu bank_account_number.'
            : `HTTP ${settlementRes?.status ?? 0}`),
    statementStatOk,
    statementStatRaw,
    statementStatMessage: statementStatOk
      ? undefined
      : !sellerId
        ? 'Thiếu seller_id để gọi statement/stat/info.'
        : statementStatRes?.message ||
          (statementStatRes?.ok
            ? 'Response statement/stat/info thiếu to_settle_amount_stat.'
            : `HTTP ${statementStatRes?.status ?? 0}`),
    identityVideoVerificationLock: identityVideoVerificationLock || undefined,
  };
}

function reportPageReady(): void {
  if (document.readyState !== 'complete') {
    schedulePageReady();
    return;
  }
  try {
    if (!chrome?.runtime?.id) return;
    void chrome.runtime.sendMessage({ type: 'SELLER_PAGE_READY', url: location.href });
  } catch {
    // extension context invalidated
  }
}

/** Debounce sau load / History API — TTS SPA đổi URL trước khi UI xong. */
const PAGE_READY_DEBOUNCE_MS = 800;

let pageReadyTimer: number | undefined;
function schedulePageReady(): void {
  window.clearTimeout(pageReadyTimer);
  pageReadyTimer = window.setTimeout(() => reportPageReady(), PAGE_READY_DEBOUNCE_MS);
}

function installPageReadyObserver(): void {
  const notify = () => schedulePageReady();

  if (document.readyState === 'complete') {
    notify();
  } else {
    window.addEventListener('load', notify, { once: true });
  }

  window.addEventListener('popstate', notify);

  const origPush = history.pushState.bind(history);
  history.pushState = (...args) => {
    origPush(...args);
    notify();
  };

  const origReplace = history.replaceState.bind(history);
  history.replaceState = (...args) => {
    origReplace(...args);
    notify();
  };
}

function reportToBackground(result: FetchResult): void {
  try {
    if (!chrome?.runtime?.id) return;
    void chrome.runtime.sendMessage({
      type: 'SELLER_COMMON_GET_RESULT',
      ...result,
    });
  } catch {
    // extension context invalidated
  }
}

const kbTtsWindow = window as unknown as { __kbTtsContent?: boolean };
if (!kbTtsWindow.__kbTtsContent) {
  kbTtsWindow.__kbTtsContent = true;
  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (!message || typeof message !== 'object') return;
    if (message.type === 'PING_CONTENT') {
      sendResponse({ ok: true });
      return;
    }
    if (message.type === 'PING_PRODUCT_BRIDGE') {
      // `force`: background gọi ngay sau khi inject nên cache «đã chết» phải bị bỏ qua.
      void isProductBridgeAlive(true).then((ok) => sendResponse({ ok }));
      return true;
    }
    if (message.type === 'REQUEST_NETWORK_SYNC') {
      try {
        window.postMessage({ source: 'kb-tts-net-ask' }, '*');
      } catch {
        /* ignore */
      }
      sendResponse({ ok: true });
      return;
    }
    if (message.type === 'FETCH_SELLER_COMMON_GET') {
      void fetchSellerCommonGet().then((result) => {
        reportToBackground(result);
        sendResponse(result);
      });
      return true;
    }
    if (message.type === 'FETCH_PRODUCT_PRELOAD') {
      void fetchProductPreload(String(message.sellerId || '')).then(sendResponse);
      return true;
    }
    if (message.type === 'FETCH_PAYOUT_PAYMENTS') {
      void fetchPayoutPayments({
        sellerId: String(message.sellerId || ''),
        maxDate: message.maxDate ?? null,
        cursorOccurredAt: message.cursorOccurredAt ?? null,
        cursorPaymentId: message.cursorPaymentId ?? null,
      }).then(sendResponse);
      return true;
    }
    if (message.type === 'FETCH_OPPORTUNITY_LEADS') {
      void fetchOpportunityLeads({
        sellerId: String(message.sellerId || ''),
        categoryInfos: Array.isArray(message.categoryInfos) ? message.categoryInfos : [],
        categoryInfoGroups: Array.isArray(message.categoryInfoGroups)
          ? message.categoryInfoGroups
          : undefined,
      }).then(sendResponse);
      return true;
    }
    if (message.type === 'FETCH_LISTING_ANALYTICS') {
      void fetchListingAnalytics({
        sellerId: String(message.sellerId || ''),
        start: message.start ?? null,
        end: message.end ?? null,
      }).then(sendResponse);
      return true;
    }
    if (message.type === 'FETCH_HEALTH_RECORDS') {
      void fetchHealthRecords(String(message.sellerId || '')).then(sendResponse);
      return true;
    }
    if (message.type === 'FETCH_SHOP_SCORES') {
      void fetchShopScores(String(message.sellerId || '')).then(sendResponse);
      return true;
    }
    return;
  });

  window.addEventListener('message', (event: MessageEvent) => {
    if (event.source !== window) return;
    const data = event.data as { source?: string; inflight?: number; raw?: unknown } | null;
    if (!data) return;
    // Người dùng mở trang tạo sản phẩm thì bridge hớt luôn `publish_limit` từ response của
    // chính TikTok — số sạch nhất, và bước Info sau đó không phải gọi request nào.
    if (data.source === PRELOAD_CAPTURE_MSG) {
      try {
        if (!chrome?.runtime?.id) return;
        void chrome.runtime.sendMessage({
          type: 'SELLER_PRELOAD_CAPTURED',
          sellerId: resolveSellerId(),
          raw: data.raw,
        });
      } catch {
        // extension context invalidated
      }
      return;
    }
    if (data.source !== 'kb-tts-net') return;
    try {
      if (!chrome?.runtime?.id) return;
      void chrome.runtime.sendMessage({
        type: 'SELLER_PAGE_NETWORK',
        inflight: Number(data.inflight) || 0,
      });
    } catch {
      // extension context invalidated
    }
  });
  try {
    window.postMessage({ source: 'kb-tts-net-ask' }, '*');
  } catch {
    /* ignore */
  }

  installPageReadyObserver();

  void fetchSellerCommonGet().then((result) => {
    reportToBackground(result);
  });
}

/* ─── Payout / payment list scrape ─── */

const PAYMENT_LIST_PATH = '/api/v1/pay/payout/seller/payment/list';
/** payment_id → statement_records (page_type=5). */
const STATEMENT_LIST_DETAIL_PATH = '/api/v1/pay/statement/list/detail';
const STATEMENT_ORDER_LIST_PATH = '/api/v1/pay/statement/order/list';
const PAYMENT_PAGE_SIZE = 10;
const STATEMENT_DETAIL_PAGE_SIZE = 10;
const MAX_PAYMENT_PAGES = 30;
/** Số payout xử lý song song (detail + orders). */
const ORDER_LINK_CONCURRENCY = 5;

type PayoutOrderRow = {
  tradeOrderId: string;
  settlementAmount?: number;
  earningAmount?: number;
  fees?: number;
  settlementTime?: string;
};

type StatementOrderRow = PayoutOrderRow & { paymentId: string; statementId?: string };

function parseAmount(raw: unknown): number {
  if (typeof raw === 'number' && Number.isFinite(raw)) return Math.abs(raw);
  if (raw && typeof raw === 'object' && 'amount' in (raw as object)) {
    const a = Number((raw as { amount?: unknown }).amount);
    return Number.isFinite(a) ? Math.abs(a) : 0;
  }
  const n = Number(raw);
  return Number.isFinite(n) ? Math.abs(n) : 0;
}

function parseMs(raw: unknown): number | null {
  if (raw == null || raw === '') return null;
  const n = Number(raw);
  if (!Number.isFinite(n) || n <= 0) return null;
  return n > 1e12 ? n : n * 1000;
}

function floorMsFromMaxDate(maxDate: string | null | undefined): number | null {
  if (!maxDate || typeof maxDate !== 'string') return null;
  const trimmed = maxDate.trim();
  if (!/^\d{4}-\d{2}-\d{2}/.test(trimmed)) return null;
  const iso = trimmed.slice(0, 10) + 'T00:00:00.000Z';
  const ms = Date.parse(iso);
  return Number.isFinite(ms) ? ms : null;
}

function cursorFloorMs(cursorOccurredAt: string | null | undefined): number | null {
  if (!cursorOccurredAt) return null;
  const ms = Date.parse(cursorOccurredAt);
  return Number.isFinite(ms) ? ms : null;
}

function buildPaymentListUrl(sellerId: string, from: number, size: number): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  q.set('app_id', '4068');
  q.set('timezone_name', 'America/Los_Angeles');
  q.set('pagination_type', '1');
  q.set('from', String(from));
  q.set('size', String(size));
  q.set('need_total_amount', 'false');
  q.set('page_type', '1');
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
  return `${location.origin}${PAYMENT_LIST_PATH}?${q.toString()}`;
}

function appendSellerPayQuery(
  q: URLSearchParams,
  sellerId: string,
  extra?: Record<string, string>,
): void {
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  q.set('timezone_name', 'America/Los_Angeles');
  q.set('pagination_type', '1');
  q.set('need_total_amount', 'false');
  if (extra) {
    for (const [k, v] of Object.entries(extra)) q.set(k, v);
  }
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
}

/** Step 2: payment_id → statement_id (Finance payout detail). */
function buildStatementListDetailUrl(
  sellerId: string,
  paymentId: string,
  from: number,
  size: number,
): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  appendSellerPayQuery(q, sellerId, {
    from: String(from),
    size: String(size),
    page_type: '5',
    payment_id: paymentId,
    statement_version: '1',
  });
  return `${location.origin}${STATEMENT_LIST_DETAIL_PATH}?${q.toString()}`;
}

/** Step 3: statement_id → order_records. */
function buildStatementOrderListUrl(
  sellerId: string,
  statementId: string,
  from: number,
  size: number,
): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  appendSellerPayQuery(q, sellerId, {
    from: String(from),
    size: String(size),
    terminal_type: '1',
    page_type: '6',
    statement_id: statementId,
    settlement_status: '2',
    no_need_sku_record: 'false',
    statement_version: '1',
  });
  return `${location.origin}${STATEMENT_ORDER_LIST_PATH}?${q.toString()}`;
}

function readStatementId(rec: Record<string, unknown>): string {
  return String(
    rec.statement_id ?? rec.bill_id ?? rec.id ?? rec.statementId ?? '',
  ).trim();
}

function readPaymentId(rec: Record<string, unknown>): string {
  return String(rec.payment_id ?? rec.settlement_id ?? rec.payout_id ?? '').trim();
}

async function fetchPayoutPayments(opts: {
  sellerId: string;
  maxDate?: string | null;
  cursorOccurredAt?: string | null;
  cursorPaymentId?: string | null;
}): Promise<{
  ok: boolean;
  message?: string;
  sellerId?: string;
  payments?: Array<{
    paymentId: string;
    amount: number;
    currency?: string;
    successTime?: string;
    bankCardMask?: string;
    note?: string;
    orders?: PayoutOrderRow[];
  }>;
  ordersLinked?: number;
}> {
  const sellerId = (opts.sellerId || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Thiếu sellerId để lấy payment list.' };
  }

  const maxFloor = floorMsFromMaxDate(opts.maxDate);
  const cursorFloor = cursorFloorMs(opts.cursorOccurredAt);
  const cursorPaymentId = (opts.cursorPaymentId || '').trim();

  const payments: Array<{
    paymentId: string;
    amount: number;
    currency?: string;
    successTime?: string;
    bankCardMask?: string;
    note?: string;
    successMs: number;
  }> = [];

  let hitCursor = false;
  for (let page = 0; page < MAX_PAYMENT_PAGES; page++) {
    const from = page * PAYMENT_PAGE_SIZE;
    const url = buildPaymentListUrl(sellerId, from, PAYMENT_PAGE_SIZE);
    const res = await fetchJson(url, { 'x-tt-oec-region': 'US' });
    if (!res.ok) {
      return {
        ok: false,
        message: res.message || `HTTP ${res.status} payment/list`,
        sellerId,
      };
    }
    const raw = res.raw as {
      code?: number;
      message?: string;
      data?: {
        search_next_has_more?: boolean;
        payment_records?: Array<Record<string, unknown>>;
      };
    } | null;
    if (!raw || Number(raw.code) !== 0) {
      return {
        ok: false,
        message: (raw && String(raw.message || '')) || 'payment/list thất bại.',
        sellerId,
      };
    }
    const records = Array.isArray(raw.data?.payment_records) ? raw.data!.payment_records! : [];
    if (records.length === 0) break;

    for (const rec of records) {
      const paymentId = String(rec.payment_id ?? '').trim();
      if (!paymentId) continue;

      // Only successful payouts (TikTok payment_status 20 in seller-us samples).
      const status = Number(rec.payment_status);
      if (Number.isFinite(status) && status !== 20) continue;

      const successMs = parseMs(rec.payment_success_time);
      if (successMs == null || successMs <= 0) {
        continue;
      }

      if (cursorPaymentId && paymentId === cursorPaymentId) {
        hitCursor = true;
        break;
      }
      // maxDate = inclusive floor; cursor = stop at/before already-synced history.
      if (maxFloor != null && successMs < maxFloor) {
        hitCursor = true;
        break;
      }
      if (cursorFloor != null && successMs <= cursorFloor) {
        hitCursor = true;
        break;
      }

      const amountObj = rec.payment_amount as { amount?: unknown; currency?: string } | undefined;
      const amount = parseAmount(amountObj ?? rec.payment_amount);
      if (!(amount > 0)) continue;

      payments.push({
        paymentId,
        amount,
        currency: amountObj?.currency ? String(amountObj.currency) : 'USD',
        successTime: String(successMs),
        bankCardMask: rec.bank_card_mask_code
          ? String(rec.bank_card_mask_code)
          : undefined,
        note: rec.payment_note ? String(rec.payment_note) : undefined,
        successMs,
      });
    }

    if (hitCursor) break;
    if (!raw.data?.search_next_has_more) break;
  }

  // Only NEW payouts (past cursor) — fetch statement/orders once on insert.
  const ordersByPayment = await linkOrdersToPayments(
    sellerId,
    payments.map((p) => p.paymentId),
  );

  let ordersLinked = 0;
  for (const rows of ordersByPayment.values()) {
    if (rows.length > 0) ordersLinked++;
  }

  return {
    ok: true,
    sellerId,
    ordersLinked,
    payments: payments.map(({ successMs: _ms, ...p }) => ({
      ...p,
      orders: ordersByPayment.get(p.paymentId) || [],
    })),
  };
}

/**
 * 3-step bridge for NEW payouts only:
 * payment_id → statement/list/detail → statement/order/list.
 * Chạy song song tối đa ORDER_LINK_CONCURRENCY payout cùng lúc.
 */
async function linkOrdersToPayments(
  sellerId: string,
  paymentIds: string[],
): Promise<Map<string, PayoutOrderRow[]>> {
  const ordersByPayment = new Map<string, PayoutOrderRow[]>();
  const wanted = [...new Set(paymentIds.filter(Boolean))];
  if (wanted.length === 0) return ordersByPayment;

  const results = await mapPool(wanted, ORDER_LINK_CONCURRENCY, async (paymentId) => {
    const rows: PayoutOrderRow[] = [];
    try {
      const statementIds = await fetchStatementIdsForPayment(sellerId, paymentId);
      const orderBatches = await Promise.all(
        statementIds.map((statementId) => fetchAllStatementOrders(sellerId, statementId)),
      );
      const seen = new Set<string>();
      for (const batch of orderBatches) {
        for (const row of batch) {
          const pid = (row.paymentId || paymentId).trim();
          if (pid && pid !== paymentId) continue;
          const key = row.tradeOrderId;
          if (!key || seen.has(key)) continue;
          seen.add(key);
          rows.push({
            tradeOrderId: row.tradeOrderId,
            settlementAmount: row.settlementAmount,
            earningAmount: row.earningAmount,
            fees: row.fees,
            settlementTime: row.settlementTime,
          });
        }
      }
    } catch {
      // Order linking best-effort — payment still syncs without breakdown.
    }
    return { paymentId, rows };
  });

  for (const { paymentId, rows } of results) {
    if (rows.length > 0) ordersByPayment.set(paymentId, rows);
  }

  return ordersByPayment;
}

/** Chạy async mapper với giới hạn concurrency. */
async function mapPool<T, R>(
  items: T[],
  concurrency: number,
  mapper: (item: T, index: number) => Promise<R>,
): Promise<R[]> {
  const limit = Math.max(1, concurrency);
  const out = new Array<R>(items.length);
  let next = 0;

  async function worker(): Promise<void> {
    while (true) {
      const i = next++;
      if (i >= items.length) return;
      out[i] = await mapper(items[i]!, i);
    }
  }

  const workers = Array.from({ length: Math.min(limit, items.length) }, () => worker());
  await Promise.all(workers);
  return out;
}

async function fetchStatementIdsForPayment(
  sellerId: string,
  paymentId: string,
): Promise<string[]> {
  const out: string[] = [];
  const seen = new Set<string>();

  for (let page = 0; page < 10; page++) {
    const from = page * STATEMENT_DETAIL_PAGE_SIZE;
    const url = buildStatementListDetailUrl(
      sellerId,
      paymentId,
      from,
      STATEMENT_DETAIL_PAGE_SIZE,
    );
    const res = await fetchJson(url, { 'x-tt-oec-region': 'US' });
    if (!res.ok) break;
    const raw = res.raw as {
      code?: number;
      data?: {
        search_next_has_more?: boolean;
        statement_records?: Array<Record<string, unknown>>;
      };
    } | null;
    if (!raw || Number(raw.code) !== 0) break;
    const list = Array.isArray(raw.data?.statement_records)
      ? raw.data!.statement_records!
      : [];
    if (list.length === 0) break;

    for (const st of list) {
      const sid = readStatementId(st);
      if (!sid || seen.has(sid)) continue;
      seen.add(sid);
      out.push(sid);
    }

    if (!raw.data?.search_next_has_more) break;
  }

  return out;
}

async function fetchAllStatementOrders(
  sellerId: string,
  statementId: string,
): Promise<StatementOrderRow[]> {
  if (!statementId) return [];

  const out: StatementOrderRow[] = [];

  for (let page = 0; page < 20; page++) {
    const from = page * 5;
    const url = buildStatementOrderListUrl(sellerId, statementId, from, 5);
    const res = await fetchJson(url, { 'x-tt-oec-region': 'US' });
    if (!res.ok) break;
    const raw = res.raw as {
      code?: number;
      data?: {
        search_next_has_more?: boolean;
        order_records?: Array<Record<string, unknown>>;
      };
    } | null;
    if (!raw || Number(raw.code) !== 0) break;
    const records = Array.isArray(raw.data?.order_records) ? raw.data!.order_records! : [];
    if (records.length === 0) break;

    for (const rec of records) {
      const tradeOrderId = String(rec.trade_order_id ?? rec.reference_id ?? '').trim();
      if (!tradeOrderId) continue;
      const paymentId = readPaymentId(rec);
      // TikTok UI "Statement date" = bill_period (shop Pacific), not settlement_time event.
      const statementMs =
        parseMs(rec.bill_period) ??
        parseMs(rec.estimate_settle_time) ??
        parseMs(rec.settlement_time);
      out.push({
        paymentId,
        statementId: String(rec.statement_id ?? statementId).trim() || undefined,
        tradeOrderId,
        settlementAmount: parseAmount(rec.settlement_amount),
        earningAmount: parseAmount(rec.earning_amount),
        fees: parseAmount(rec.fees),
        settlementTime: statementMs != null ? String(statementMs) : undefined,
      });
    }

    if (!raw.data?.search_next_has_more) break;
  }

  return out;
}

/* ─── Product opportunities / trending keywords ─── */

const OPPORTUNITY_LEAD_LIST_PATH =
  '/api/v1/product/oc/seller_product_opportunity/seller/lead/list';
const OPPORTUNITY_PAGE_SIZE = 100;
const MAX_OPPORTUNITY_PAGES = 5;
/** Match backend SyncProductOpportunitiesValidator.MaxItems. */
const MAX_OPPORTUNITY_ITEMS = 500;
const OPPORTUNITY_PAGE_DELAY_MS = 400;
const OPPORTUNITY_FETCH_TIMEOUT_MS = 25_000;

function waitMs(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function asRecord(value: unknown): Record<string, any> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, any>)
    : null;
}

function opportunityLeadListHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    accept: 'application/json',
    'content-type': 'application/json',
    'x-tt-oec-region': 'US',
  };
  const csrf =
    readCookie('tt_csrf_token') ||
    readCookie('passport_csrf_token') ||
    readCookie('passport_csrf_token_default') ||
    '';
  if (csrf) {
    headers['x-secsdk-csrf-token'] = csrf;
    headers['x-tt-passport-csrf-token'] = csrf;
  }
  return headers;
}

function asList(value: unknown): unknown[] {
  if (value == null) return [];
  return Array.isArray(value) ? value : [value];
}

/** TikTok often sends i18n blobs `{ text, starling_key }` instead of a plain string. */
function pickDisplayText(value: unknown, depth = 0): string {
  if (value == null || depth > 3) return '';
  if (typeof value === 'string') {
    const t = value.trim();
    return !t || t === '[object Object]' ? '' : t;
  }
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  if (Array.isArray(value)) {
    for (const item of value) {
      const t = pickDisplayText(item, depth + 1);
      if (t) return t;
    }
    return '';
  }
  const rec = asRecord(value);
  if (!rec) return '';
  const keys = [
    'lead_tag_text',
    'leadTagText',
    'tag_text',
    'tagText',
    'starling_text',
    'starlingText',
    'text',
    'value',
    'name',
    'label',
    'title',
    'en',
    'en_US',
    'message',
  ];
  for (const key of keys) {
    if (rec[key] == null) continue;
    const t = pickDisplayText(rec[key], depth + 1);
    if (t) return t;
  }
  return pickDisplayText(rec.lead_tag ?? rec.leadTag ?? rec.tag, depth + 1);
}

function parseVolumeInt(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return Math.max(0, Math.floor(value));
  if (typeof value !== 'string') return 0;
  const digits = value.replace(/[^\d]/g, '');
  if (!digits) return 0;
  const n = Number(digits);
  return Number.isFinite(n) ? n : 0;
}

function parseOnlineProducts(value: unknown): { value?: number; raw?: string } {
  if (value == null || value === '') return {};
  if (typeof value === 'number' && Number.isFinite(value)) {
    return { value: Math.max(0, Math.floor(value)) };
  }
  const raw = String(value).trim();
  if (!raw) return {};
  if (/^\d[\d,]*$/.test(raw)) return { value: parseVolumeInt(raw) };
  return { raw };
}

const MAX_PIC_URLS = 8;
const MAX_CATEGORIES = 20;
const MAX_LEAD_TAG_TEXTS = 8;

function trimRing(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function parseOptionalDecimal(value: unknown): { value?: number; raw?: string } {
  if (value == null || value === '') return {};
  if (typeof value === 'number' && Number.isFinite(value)) {
    return { value: Math.max(0, value) };
  }
  const raw = String(value).trim();
  if (!raw) return {};
  const cleaned = raw.replace(/[$,\s]/g, '');
  if (/^\d+(\.\d+)?$/.test(cleaned)) {
    const n = Number(cleaned);
    if (Number.isFinite(n)) return { value: n };
  }
  return { raw };
}

function allPicUrls(value: unknown): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  const push = (url: unknown) => {
    if (typeof url !== 'string') return;
    const t = url.trim();
    if (!t || seen.has(t) || out.length >= MAX_PIC_URLS) return;
    seen.add(t);
    out.push(t);
  };
  if (typeof value === 'string') {
    push(value);
    return out;
  }
  if (!Array.isArray(value)) return out;
  for (const item of value) {
    if (out.length >= MAX_PIC_URLS) break;
    if (typeof item === 'string') {
      push(item);
      continue;
    }
    const rec = asRecord(item);
    push(rec?.url ?? rec?.pic_url ?? rec?.picUrl);
  }
  return out;
}

function mapCategoryItems(
  categories: unknown[],
): { cateId: string; cateName: string; level?: number }[] {
  const out: { cateId: string; cateName: string; level?: number }[] = [];
  const seen = new Set<string>();
  for (const item of categories) {
    if (out.length >= MAX_CATEGORIES) break;
    const rec = asRecord(item);
    if (!rec) continue;
    const idRaw = rec.cate_id ?? rec.cateId ?? rec.id;
    const nameRaw = rec.cate_name ?? rec.cateName ?? rec.name;
    const cateId = idRaw != null && String(idRaw).trim() ? String(idRaw).trim() : '';
    const cateName = typeof nameRaw === 'string' && nameRaw.trim() ? nameRaw.trim() : '';
    if (!cateId && !cateName) continue;
    const key = cateId || cateName;
    if (seen.has(key)) continue;
    seen.add(key);
    const lv = Number(rec.level ?? rec.cate_level ?? rec.cateLevel);
    out.push({
      cateId,
      cateName,
      level: Number.isFinite(lv) && lv > 0 ? lv : undefined,
    });
  }
  return out;
}

function collectUniqueTexts(value: unknown, max: number): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  for (const item of asList(value)) {
    if (out.length >= max) break;
    const rec = asRecord(item);
    const raw = rec
      ? rec.lead_tag_text ??
        rec.leadTagText ??
        rec.tag_text ??
        rec.tagText ??
        rec.lead_tag ??
        rec.leadTag ??
        rec.text ??
        rec.name ??
        rec.label ??
        item
      : item;
    const text = pickDisplayText(raw);
    if (!text) continue;
    const key = text.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(text);
  }
  return out;
}

function mapLeadTagTexts(...values: unknown[]): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  for (const value of values) {
    for (const text of collectUniqueTexts(value, MAX_LEAD_TAG_TEXTS)) {
      if (out.length >= MAX_LEAD_TAG_TEXTS) return out;
      const key = text.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      out.push(text);
    }
  }
  return out;
}

function mapTagCodes(value: unknown): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  for (const item of asList(value)) {
    const rec = asRecord(item);
    const raw = rec
      ? rec.tag_code ?? rec.tagCode ?? rec.code ?? rec.lead_tag_code ?? rec.leadTagCode
      : item;
    const text = typeof raw === 'string' ? raw.trim() : pickDisplayText(raw);
    if (!text || text === '[object Object]') continue;
    const key = text.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(text);
  }
  return out;
}

function pickCate(categories: unknown[], level: number): { id?: string; name?: string } {
  for (const item of categories) {
    const rec = asRecord(item);
    if (!rec) continue;
    const lv = Number(rec.level ?? rec.cate_level ?? rec.cateLevel);
    if (lv !== level) continue;
    const id = rec.cate_id ?? rec.cateId ?? rec.id;
    const name = rec.cate_name ?? rec.cateName ?? rec.name;
    return {
      id: id != null && String(id).trim() ? String(id).trim() : undefined,
      name: typeof name === 'string' && name.trim() ? name.trim() : undefined,
    };
  }
  return {};
}

function pickCateByIndex(categories: unknown[], index: number): { id?: string; name?: string } {
  const rec = asRecord(categories[index]);
  if (!rec) return {};
  const id = rec.cate_id ?? rec.cateId ?? rec.id;
  const name = rec.cate_name ?? rec.cateName ?? rec.name;
  return {
    id: id != null && String(id).trim() ? String(id).trim() : undefined,
    name: typeof name === 'string' && name.trim() ? name.trim() : undefined,
  };
}

function mapOpportunityLead(raw: unknown): {
  leadId: string;
  leadName: string;
  picUrl?: string | null;
  picUrls?: string[];
  searchVolume: number;
  onlineProducts?: number | null;
  onlineProductsRaw?: string | null;
  searchVolumeRingRatio?: string | null;
  onlineProductsRingRatio?: string | null;
  orderCount?: number | null;
  orderRaw?: string | null;
  orderRingRatio?: string | null;
  gmv?: number | null;
  gmvRaw?: string | null;
  gmvRingRatio?: string | null;
  categories?: { cateId: string; cateName: string; level?: number }[];
  level1CateId?: string | null;
  level1CateName?: string | null;
  level2CateId?: string | null;
  level2CateName?: string | null;
  level3CateId?: string | null;
  level3CateName?: string | null;
  tagCodes: string[];
  leadTagTexts?: string[];
  leadSourceTag?: string | null;
  leadClusterSourceTag?: string | null;
  opportunityType?: number | null;
} | null {
  const rec = asRecord(raw);
  if (!rec) return null;
  const inner = asRecord(rec.lead);
  const leadId = String(rec.lead_id ?? rec.leadId ?? inner?.lead_id ?? inner?.leadId ?? '').trim();
  if (!leadId) return null;
  const leadName =
    pickDisplayText(rec.lead_name ?? rec.leadName ?? inner?.lead_name ?? inner?.leadName) ||
    leadId;
  const categoriesRaw = Array.isArray(rec.categories)
    ? rec.categories
    : Array.isArray(inner?.categories)
      ? inner.categories
      : [];
  const categories = mapCategoryItems(categoriesRaw);
  let l1 = pickCate(categoriesRaw, 1);
  let l2 = pickCate(categoriesRaw, 2);
  let l3 = pickCate(categoriesRaw, 3);
  if (!l1.id && !l2.id && !l3.id && categoriesRaw.length > 0) {
    l1 = pickCateByIndex(categoriesRaw, 0);
    l2 = pickCateByIndex(categoriesRaw, 1);
    l3 = pickCateByIndex(categoriesRaw, 2);
  }
  const tagCodes = mapTagCodes(rec.tag_codes ?? rec.tagCodes ?? inner?.tag_codes ?? inner?.tagCodes);
  let leadTagTexts = mapLeadTagTexts(
    rec.lead_demands ?? rec.leadDemands,
    rec.lead_demand ?? rec.leadDemand,
    inner?.lead_demands ?? inner?.leadDemands,
    inner?.lead_demand ?? inner?.leadDemand,
  );
  if (leadTagTexts.length === 0) {
    leadTagTexts = mapLeadTagTexts(
      rec.tag_codes ?? rec.tagCodes ?? inner?.tag_codes ?? inner?.tagCodes,
    );
  }
  const online = parseOnlineProducts(
    rec.online_products ?? rec.onlineProducts ?? inner?.online_products ?? inner?.onlineProducts,
  );
  const order = parseOnlineProducts(
    rec.order ?? rec.order_count ?? rec.orders ?? rec.orderCount ?? inner?.order ?? inner?.orderCount,
  );
  const gmv = parseOptionalDecimal(rec.gmv ?? rec.gmv_usd ?? rec.gmvUsd ?? inner?.gmv);
  const picUrls = allPicUrls(rec.pic_url ?? rec.picUrl ?? inner?.pic_url ?? inner?.picUrl);
  const oppType = rec.opportunity_type ?? rec.opportunityType ?? inner?.opportunity_type;
  const leadSourceTag = pickDisplayText(
    rec.lead_source_tag ?? rec.leadSourceTag ?? inner?.lead_source_tag,
  );
  const leadClusterSourceTag = pickDisplayText(
    rec.lead_cluster_source_tag ?? rec.leadClusterSourceTag ?? inner?.lead_cluster_source_tag,
  );
  return {
    leadId,
    leadName,
    picUrl: picUrls[0] ?? null,
    picUrls,
    searchVolume: parseVolumeInt(
      rec.search_volume ?? rec.searchVolume ?? inner?.search_volume ?? inner?.searchVolume,
    ),
    onlineProducts: online.value ?? null,
    onlineProductsRaw: online.raw ?? null,
    searchVolumeRingRatio: trimRing(
      rec.search_volume_ring_ratio ?? rec.searchVolumeRingRatio ?? inner?.search_volume_ring_ratio,
    ),
    onlineProductsRingRatio: trimRing(
      rec.online_products_ring_ratio ??
        rec.onlineProductsRingRatio ??
        inner?.online_products_ring_ratio,
    ),
    orderCount: order.value ?? null,
    orderRaw: order.raw ?? null,
    orderRingRatio: trimRing(rec.order_ring_ratio ?? rec.orderRingRatio),
    gmv: gmv.value ?? null,
    gmvRaw: gmv.raw ?? null,
    gmvRingRatio: trimRing(rec.gmv_ring_ratio ?? rec.gmvRingRatio),
    categories,
    level1CateId: l1.id ?? null,
    level1CateName: l1.name ?? null,
    level2CateId: l2.id ?? null,
    level2CateName: l2.name ?? null,
    level3CateId: l3.id ?? null,
    level3CateName: l3.name ?? null,
    tagCodes,
    leadTagTexts,
    leadSourceTag: leadSourceTag || null,
    leadClusterSourceTag: leadClusterSourceTag || null,
    opportunityType:
      typeof oppType === 'number' && Number.isFinite(oppType) ? oppType : null,
  };
}

async function fetchOpportunityPageJson(url: string, body: string): Promise<JsonResult> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), OPPORTUNITY_FETCH_TIMEOUT_MS);
  try {
    const res = await fetch(url, {
      method: 'POST',
      credentials: 'include',
      signal: controller.signal,
      headers: opportunityLeadListHeaders(),
      body,
    });
    const raw = await res.json().catch(() => null);
    return { ok: res.ok, status: res.status, raw };
  } catch (err: unknown) {
    const aborted =
      (err && typeof err === 'object' && 'name' in err && (err as { name?: string }).name === 'AbortError') ||
      (typeof DOMException !== 'undefined' && err instanceof DOMException && err.name === 'AbortError');
    return {
      ok: false,
      status: 0,
      raw: null,
      message: aborted
        ? `Timeout ${OPPORTUNITY_FETCH_TIMEOUT_MS / 1000}s — lead/list không trả lời.`
        : 'Fetch lead/list thất bại.',
    };
  } finally {
    window.clearTimeout(timer);
  }
}

async function fetchOpportunityLeads(opts: {
  sellerId: string;
  categoryInfos: { cateId: number; level: number }[];
  categoryInfoGroups?: { cateId: number; level: number }[][];
}): Promise<{
  ok: boolean;
  message?: string;
  sellerId?: string;
  items?: NonNullable<ReturnType<typeof mapOpportunityLead>>[];
  totalProductCount?: number;
}> {
  const sellerId = (opts.sellerId || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Không đọc được seller_id từ cookie.' };
  }
  const rawGroups =
    Array.isArray(opts.categoryInfoGroups) && opts.categoryInfoGroups.length > 0
      ? opts.categoryInfoGroups
      : [opts.categoryInfos || []];
  const groups = rawGroups
    .map((group) =>
      (group || [])
        .filter((c) => c && Number(c.cateId) > 0 && Number(c.level) > 0)
        .map((c) => {
          const cateId = Number(c.cateId);
          return { cate_id: cateId, cateId, level: Number(c.level) };
        }),
    )
    .filter((group) => group.length > 0);
  if (groups.length === 0) {
    return { ok: false, message: 'Shop chưa gán danh mục niche.' };
  }

  const url = buildSellerApiUrl(OPPORTUNITY_LEAD_LIST_PATH, sellerId);
  const items: NonNullable<ReturnType<typeof mapOpportunityLead>>[] = [];
  const seen = new Set<string>();
  let totalProductCount = 0;
  let lastUnread: string | undefined;

  for (let g = 0; g < groups.length; g++) {
    const categoryInfos = groups[g]!;
    let groupTotal = 0;
    let groupFetched = 0;
    let skipGroup: string | undefined;
    for (let page = 1; page <= MAX_OPPORTUNITY_PAGES; page++) {
      const bodies = buildLeadListRequestBodies(categoryInfos, page, OPPORTUNITY_PAGE_SIZE);
      let rec: Record<string, any> | null = null;
      let data: unknown;
      let picked = { found: false, list: [] as unknown[] };
      let pageError: string | undefined;
      const attempts = page === 1 ? bodies : bodies.slice(0, 1);
      for (let b = 0; b < attempts.length; b++) {
        const res = await fetchOpportunityPageJson(url, attempts[b]!);
        rec = asRecord(res.raw);
        if (!rec) {
          pageError = `Danh mục ${g + 1} trang ${page}: lead/list không trả JSON hợp lệ — giữ snapshot cũ.`;
          continue;
        }
        const code = rec.code;
        if (!res.ok || (code != null && Number(code) !== 0)) {
          const msg =
            (typeof rec.msg === 'string' && rec.msg) ||
            (typeof rec.message === 'string' && rec.message) ||
            res.message ||
            `lead/list HTTP ${res.status}`;
          pageError = `Danh mục ${g + 1} trang ${page} lỗi: ${msg}`;
          continue;
        }
        pageError = undefined;
        data = rec.data;
        picked = pickLeadList(data);
        if (!picked.found) picked = pickLeadList(rec);
        if (!picked.found && isEmptyLeadPayload(data)) {
          picked = { found: true, list: [] };
        }
        if (picked.found) break;
        pageError = `Danh mục ${g + 1} trang ${page}: không đọc được danh sách lead (${describeLeadListShape(data)}) — giữ snapshot cũ.`;
      }
      if (!picked.found) {
        skipGroup = pageError;
        break;
      }
      const list = picked.list;
      const parsedTotal = pickLeadTotal(rec, data);
      if (parsedTotal > 0) {
        groupTotal = parsedTotal;
      }

      for (const row of list) {
        const mapped = mapOpportunityLead(row);
        if (!mapped) continue;
        groupFetched += 1;
        if (!seen.has(mapped.leadId) && items.length < MAX_OPPORTUNITY_ITEMS) {
          seen.add(mapped.leadId);
          items.push(mapped);
        }
        if (items.length >= MAX_OPPORTUNITY_ITEMS) break;
      }

      const hasMoreByTotal = groupTotal > 0 && groupFetched < groupTotal;
      const pageFull = list.length >= OPPORTUNITY_PAGE_SIZE;
      if (items.length >= MAX_OPPORTUNITY_ITEMS) break;
      if (list.length === 0) break;
      if (!hasMoreByTotal && !pageFull) break;
      if (groupTotal > 0 && groupFetched >= groupTotal) break;
      if (page === MAX_OPPORTUNITY_PAGES && (hasMoreByTotal || pageFull)) {
        return {
          ok: false,
          message: `Danh mục ${g + 1}: chưa scrape hết (dừng ở ${MAX_OPPORTUNITY_PAGES} trang) — giữ snapshot cũ.`,
        };
      }
      if (page < MAX_OPPORTUNITY_PAGES) await waitMs(OPPORTUNITY_PAGE_DELAY_MS);
    }

    if (skipGroup) {
      lastUnread = skipGroup;
      if (g + 1 < groups.length) await waitMs(OPPORTUNITY_PAGE_DELAY_MS);
      continue;
    }
    if (groupTotal > 0 && groupFetched === 0) {
      lastUnread = `Danh mục ${g + 1}: lead/list có total nhưng không map được keyword — giữ snapshot cũ.`;
      if (g + 1 < groups.length) await waitMs(OPPORTUNITY_PAGE_DELAY_MS);
      continue;
    }
    if (items.length >= MAX_OPPORTUNITY_ITEMS) break;
    if (g + 1 < groups.length) await waitMs(OPPORTUNITY_PAGE_DELAY_MS);
    totalProductCount += groupTotal;
  }

  if (items.length === 0 && lastUnread) {
    return { ok: false, message: lastUnread };
  }

  return { ok: true, sellerId, items, totalProductCount };
}

/* ─── Listing analytics (Compass → Product analysis) ─── */

const INSIGHTS_PRODUCT_LIST_PATH = '/api/v3/insights/seller/ttp/product/list';
const ANALYTICS_PAGE_SIZE = 100;
/** 30 × 100 = MAX_ANALYTICS_ITEMS; hết trang mà còn dữ liệu thì báo lỗi thay vì ingest thiếu. */
const MAX_ANALYTICS_PAGES = 30;
/** Match backend SyncListingAnalyticsValidator.MaxItems. */
const MAX_ANALYTICS_ITEMS = 3000;
const ANALYTICS_PAGE_DELAY_MS = 400;
const ANALYTICS_FETCH_TIMEOUT_MS = 25_000;
/** Compass preset `last7days`: mỗi sync = tổng cửa sổ trượt 7 ngày Pacific (end = hôm nay shop, start = end − 7). */
const ANALYTICS_RANGE_DAYS = 7;
const ANALYTICS_TIMEZONE = 'America/Los_Angeles';
/** Cắt raw stats để payload không phình khi shop có nhiều product. */
const MAX_ANALYTICS_RAW_LENGTH = 8000;

type AnalyticsItem = {
  productId: string;
  productName?: string;
  mainImage?: string;
  currency?: string;
  gmv?: number;
  orders?: number;
  impressions?: number;
  clicks?: number;
  ctr?: number;
  addToCartUnits?: number;
  addToCartBuyers?: number;
  gmvRatio?: number;
  ordersRatio?: number;
  impressionsRatio?: number;
  clicksRatio?: number;
  ctrRatio?: number;
  addToCartUnitsRatio?: number;
  addToCartBuyersRatio?: number;
  rawStats?: Record<string, any>;
};

const PRODUCT_NAME_KEYS = ['product_name', 'productName', 'title', 'product_title', 'name'];
const PRODUCT_IMAGE_KEYS = [
  'product_image',
  'productImage',
  'main_image_url',
  'mainImageUrl',
  'main_image',
  'mainImage',
  'cover_image',
  'coverImage',
  'image_url',
  'imageUrl',
  'image',
  'pic_url',
  'picUrl',
  'thumbnail',
];
function buildAnalyticsUrl(sellerId: string): string {
  const q = new URLSearchParams(SELLER_API_BASE_QUERY);
  q.set('oec_seller_id', sellerId);
  q.set('seller_id', sellerId);
  q.set('use_content_type_definition', '1');
  q.set('timezone_name', ANALYTICS_TIMEZONE);
  const fp = readCookie('s_v_web_id');
  if (fp) q.set('fp', fp);
  return `${location.origin}${INSIGHTS_PRODUCT_LIST_PATH}?${q.toString()}`;
}

async function fetchAnalyticsPageJson(url: string, body: string): Promise<JsonResult> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), ANALYTICS_FETCH_TIMEOUT_MS);
  try {
    const res = await fetch(url, {
      method: 'POST',
      credentials: 'include',
      signal: controller.signal,
      headers: {
        accept: '*/*',
        'content-type': 'application/json',
        'x-tt-oec-region': 'US',
      },
      body,
    });
    const raw = await res.json().catch(() => null);
    return { ok: res.ok, status: res.status, raw };
  } catch (err: unknown) {
    const aborted =
      (err && typeof err === 'object' && 'name' in err && (err as { name?: string }).name === 'AbortError') ||
      (typeof DOMException !== 'undefined' && err instanceof DOMException && err.name === 'AbortError');
    return {
      ok: false,
      status: 0,
      raw: null,
      message: aborted
        ? `Timeout ${ANALYTICS_FETCH_TIMEOUT_MS / 1000}s — product/list không trả lời.`
        : 'Fetch product/list thất bại.',
    };
  } finally {
    window.clearTimeout(timer);
  }
}

function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

/** Hôm nay theo timezone shop (Compass tính ngày theo giờ shop, không phải giờ máy). */
function analyticsToday(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: ANALYTICS_TIMEZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  return /^\d{4}-\d{2}-\d{2}$/.test(parts) ? parts : new Date().toISOString().slice(0, 10);
}

function shiftIsoDate(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const shifted = new Date(Date.UTC(y || 1970, (m || 1) - 1, d || 1) + days * 86_400_000);
  return `${shifted.getUTCFullYear()}-${pad2(shifted.getUTCMonth() + 1)}-${pad2(shifted.getUTCDate())}`;
}

function isIsoDate(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value.trim());
}

function pickString(rec: Record<string, any>, keys: string[]): string | undefined {
  const value = firstDefined(rec, keys);
  const text = pickDisplayText(value);
  return text || undefined;
}

function pickImageUrl(rec: Record<string, any>, keys: string[]): string | undefined {
  for (const key of keys) {
    const raw = rec[key];
    if (raw == null) continue;
    const [url] = allPicUrls(raw);
    if (url) return url;
    const inner = asRecord(raw);
    if (!inner) continue;
    const [nested] = allPicUrls(inner.url ?? inner.url_list ?? inner.urlList ?? inner.uri);
    if (nested) return nested;
  }
  return undefined;
}

/** Trích ngắn dữ liệu lạ để nhét vào thông báo lỗi (không dựa vào devtools). */
function previewJson(value: unknown, maxLength = 200): string {
  try {
    const json = JSON.stringify(value) ?? String(value);
    return json.length > maxLength ? `${json.slice(0, maxLength)}…` : json;
  } catch {
    return String(value);
  }
}

function trimRawStats(stats: Record<string, any>): Record<string, any> | undefined {
  try {
    const json = JSON.stringify(stats);
    if (!json || json.length > MAX_ANALYTICS_RAW_LENGTH) return undefined;
    return stats;
  } catch {
    return undefined;
  }
}

function mapAnalyticsRow(
  row: unknown,
): { item: AnalyticsItem; statsKeys: string[] } | null {
  // Có version TTS bọc mỗi dòng trong một mảng con — lấy record đầu tiên.
  const rec = asRecord(row) ?? (Array.isArray(row) ? asRecord(row[0]) : null);
  if (!rec) return null;

  // Tên wrapper product info đổi theo version endpoint → quét mọi record lồng thay vì đoán tên.
  const infoSources = nestedRecords(rec, 2);

  let productName: string | undefined;
  let mainImage: string | undefined;
  let currency: string | undefined;
  for (const source of infoSources) {
    productName ??= pickString(source, PRODUCT_NAME_KEYS);
    mainImage ??= pickImageUrl(source, PRODUCT_IMAGE_KEYS);
    currency ??= pickString(source, CURRENCY_KEYS);
  }

  const productId = findProductId(rec) ?? '';
  if (!productId) return null;

  const { total: stats, previous } = pickAnalyticsStatsPair(rec);
  const current = stats ?? {};
  const prev = previous ?? {};

  /**
   * Compass không gửi ratio theo từng metric mà gửi cả cụm `total_prev`, nên % thay đổi tự tính.
   * Vẫn ưu tiên ratio nếu payload có sẵn (version khác của endpoint).
   */
  const read = (
    keys: string[],
    normalize: (raw: unknown) => number | undefined,
  ): { value?: number; ratio?: number } => {
    const node = readMetric(current, keys);
    const value = normalize(node.value);
    const ratio =
      analyticsRatio(node.ratio) ?? analyticsDeltaRatio(value, normalize(readMetric(prev, keys).value));
    return { value, ratio };
  };

  const gmv = read(GMV_KEYS, analyticsMoney);
  const orders = read(ORDER_KEYS, analyticsInt);
  const impressions = read(IMPRESSION_KEYS, analyticsInt);
  const clicks = read(CLICK_KEYS, analyticsInt);
  const ctr = read(CTR_KEYS, analyticsRate);
  const atcUnits = read(ATC_UNIT_KEYS, analyticsInt);
  const atcBuyers = read(ATC_BUYER_KEYS, analyticsInt);

  // `gmv` là object `{ amount, currency_code, … }` → đó là chỗ duy nhất payload nói tiền tệ.
  currency ??= pickString(current, CURRENCY_KEYS);
  if (!currency) {
    const gmvRaw = asRecord(firstDefined(current, GMV_KEYS));
    if (gmvRaw) currency = pickString(gmvRaw, CURRENCY_KEYS);
  }

  const item: AnalyticsItem = {
    productId,
    productName,
    mainImage,
    currency: currency ? currency.toUpperCase().slice(0, 8) : undefined,
    gmv: gmv.value,
    orders: orders.value,
    impressions: impressions.value,
    clicks: clicks.value,
    ctr: ctr.value,
    addToCartUnits: atcUnits.value,
    addToCartBuyers: atcBuyers.value,
    gmvRatio: gmv.ratio,
    ordersRatio: orders.ratio,
    impressionsRatio: impressions.ratio,
    clicksRatio: clicks.ratio,
    ctrRatio: ctr.ratio,
    addToCartUnitsRatio: atcUnits.ratio,
    addToCartBuyersRatio: atcBuyers.ratio,
    rawStats: trimRawStats(compactAnalyticsStats(current)),
  };

  return { item, statsKeys: Object.keys(current) };
}

async function fetchListingAnalytics(opts: {
  sellerId: string;
  start?: string | null;
  end?: string | null;
}): Promise<{
  ok: boolean;
  message?: string;
  sellerId?: string;
  preset?: string;
  rangeStart?: string;
  rangeEnd?: string;
  items?: AnalyticsItem[];
  total?: number;
  statsKeys?: string[];
  truncated?: boolean;
}> {
  const sellerId = (opts.sellerId || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Không đọc được seller_id từ cookie.' };
  }

  const end = isIsoDate(opts.end) ? opts.end.trim() : analyticsToday();
  const start = isIsoDate(opts.start) ? opts.start.trim() : shiftIsoDate(end, -ANALYTICS_RANGE_DAYS);
  const url = buildAnalyticsUrl(sellerId);

  const items: AnalyticsItem[] = [];
  const seen = new Set<string>();
  let total = 0;
  let rowsSeen = 0;
  let statsKeys: string[] = [];
  let firstRow: unknown = null;

  for (let page = 0; page < MAX_ANALYTICS_PAGES; page++) {
    const body = JSON.stringify({
      request: {
        time_descriptor: { start, end },
        search: { category_ids: [], status: 1, gmv_ranges: [], channel_type: 0 },
        list_control: {
          pagination: { page, size: ANALYTICS_PAGE_SIZE },
          rules: [{ field: 'gmv', direction: 2 }],
        },
      },
    });

    const res = await fetchAnalyticsPageJson(url, body);
    const rec = asRecord(res.raw);
    if (!rec) {
      return {
        ok: false,
        message: `Trang ${page + 1}: product/list không trả JSON hợp lệ — giữ snapshot cũ.`,
      };
    }

    const code = rec.code;
    if (!res.ok || (code != null && Number(code) !== 0)) {
      const msg =
        (typeof rec.msg === 'string' && rec.msg) ||
        (typeof rec.message === 'string' && rec.message) ||
        res.message ||
        `product/list HTTP ${res.status}`;
      return { ok: false, message: `Trang ${page + 1} lỗi: ${msg}` };
    }

    const data = rec.data;
    const picked = pickAnalyticsList(data);
    if (!picked.found) {
      return {
        ok: false,
        message: `Trang ${page + 1}: không đọc được danh sách product — giữ snapshot cũ.`,
      };
    }

    const list = picked.list;
    const parsedTotal = pickAnalyticsTotal(rec, data);
    if (parsedTotal > 0) total = parsedTotal;
    const hasMore = pickAnalyticsHasMore(data);

    for (const row of list) {
      rowsSeen += 1;
      if (firstRow === null) firstRow = row;
      const mapped = mapAnalyticsRow(row);
      if (!mapped) continue;
      if (statsKeys.length === 0) statsKeys = mapped.statsKeys;
      if (seen.has(mapped.item.productId)) continue;
      seen.add(mapped.item.productId);
      items.push(mapped.item);
      if (items.length >= MAX_ANALYTICS_ITEMS) break;
    }

    if (items.length >= MAX_ANALYTICS_ITEMS) break;
    if (list.length === 0) break;
    // `has_more` là nguồn tin cậy nhất; chỉ suy ra từ số dòng khi payload không có cờ này.
    if (hasMore === false) break;
    if (hasMore === undefined && list.length < ANALYTICS_PAGE_SIZE) break;
    if (total > 0 && rowsSeen >= total) break;
    if (page === MAX_ANALYTICS_PAGES - 1) {
      return {
        ok: false,
        message: `Chưa scrape hết product (dừng ở ${MAX_ANALYTICS_PAGES} trang) — giữ snapshot cũ.`,
      };
    }
    await waitMs(ANALYTICS_PAGE_DELAY_MS);
  }

  if (items.length === 0 && rowsSeen > 0) {
    // Kèm shape thật của dòng đầu: TTS đổi schema thì đọc thông báo là biết thiếu alias nào.
    const keys = describeRowKeys(firstRow, 16);
    const shape = keys.length ? `key dòng đầu: ${keys.join(', ')}` : `dòng đầu: ${previewJson(firstRow)}`;
    console.warn('[kb-tts] analytics row không có product_id:', firstRow);
    return {
      ok: false,
      message: `product/list trả ${rowsSeen} dòng nhưng không đọc được product_id (${shape}) — giữ snapshot cũ.`,
    };
  }

  // `undefined` = không đọc được key (khác `0` = TTS báo thật là 0). Cả trang không có metric nào
  // nghĩa là mapper trượt schema — ingest lúc này sẽ ghi đè snapshot hôm nay bằng toàn số 0.
  const withMetric = items.some(
    (item) =>
      item.gmv !== undefined ||
      item.orders !== undefined ||
      item.impressions !== undefined ||
      item.clicks !== undefined,
  );
  if (items.length > 0 && !withMetric) {
    const shape = statsKeys.length
      ? `key stats: ${statsKeys.slice(0, 16).join(', ')}`
      : `dòng đầu: ${previewJson(firstRow)}`;
    console.warn('[kb-tts] analytics đọc được product nhưng không có metric:', firstRow);
    return {
      ok: false,
      message: `Đọc được ${items.length} product nhưng không có metric nào (${shape}) — giữ snapshot cũ.`,
    };
  }

  return {
    ok: true,
    sellerId,
    preset: 'last7days',
    rangeStart: start,
    rangeEnd: end,
    items,
    total,
    statsKeys,
    truncated: items.length >= MAX_ANALYTICS_ITEMS && (total === 0 || total > items.length),
  };
}

async function fetchHealthRecordType(
  sellerId: string,
  path: string,
  recordType: 'violation' | 'warning',
): Promise<{ ok: boolean; message?: string; items: SyncHealthRecordItem[] }> {
  const headers = {
    accept: '*/*',
    'content-type': 'application/json',
    'x-tt-oec-region': 'US',
  };
  const items: SyncHealthRecordItem[] = [];
  const seen = new Set<string>();
  let offset = 0;
  let total = 0;

  while (items.length < MAX_HEALTH_RECORDS_PER_TYPE) {
    const url = buildSellerApiUrl(path, sellerId);
    const body = JSON.stringify({
      limit: HEALTH_RECORDS_PAGE_SIZE,
      offset,
      view_source: 0,
    });
    const res = await fetchJson(url, headers, { method: 'POST', body });
    const rec = asRecord(res.raw);
    if (!rec) {
      return { ok: false, message: `${recordType}: JSON không hợp lệ.`, items };
    }

    const code = rec.code;
    if (!res.ok || (code != null && Number(code) !== 0)) {
      const msg =
        (typeof rec.message === 'string' && rec.message) ||
        res.message ||
        `${recordType} HTTP ${res.status}`;
      return { ok: false, message: msg, items };
    }

    const data = asRecord(rec.data);
    const records = data && Array.isArray(data.records) ? data.records : [];
    total = data && typeof data.total === 'number' ? Number(data.total) : total;

    for (const row of records) {
      const mapped = mapHealthRecord(row, recordType);
      if (!mapped || seen.has(mapped.recordId)) continue;
      seen.add(mapped.recordId);
      items.push(mapped);
      if (items.length >= MAX_HEALTH_RECORDS_PER_TYPE) break;
    }

    offset += records.length;
    if (records.length === 0 || offset >= total || items.length >= MAX_HEALTH_RECORDS_PER_TYPE) {
      break;
    }
  }

  return { ok: true, items };
}

async function fetchHealthRecords(sellerIdInput: string): Promise<{
  ok: boolean;
  message?: string;
  sellerId?: string;
  items?: SyncHealthRecordItem[];
}> {
  const sellerId = (sellerIdInput || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Không đọc được seller_id từ cookie.' };
  }

  const items: SyncHealthRecordItem[] = [];
  const seen = new Set<string>();

  for (const spec of [
    { path: VIOLATION_RECORDS_PATH, type: 'violation' as const },
    { path: WARNING_RECORDS_PATH, type: 'warning' as const },
  ]) {
    const result = await fetchHealthRecordType(sellerId, spec.path, spec.type);
    if (!result.ok) {
      return { ok: false, message: result.message, sellerId, items };
    }
    for (const item of result.items) {
      if (seen.has(item.recordId)) continue;
      seen.add(item.recordId);
      items.push(item);
    }
  }

  return { ok: true, sellerId, items };
}

/** Query riêng của AHR: TikTok yêu cầu loại tài khoản + quốc gia ngoài base query. */
function buildAhrScoreUrl(sellerId: string): string {
  const url = new URL(buildSellerApiUrl(AHR_SCORE_PATH, sellerId));
  url.searchParams.set('account_type', '2');
  url.searchParams.set('country_code', 'US');
  url.searchParams.set('account_level', '1');
  return url.toString();
}

/** `shop_id` rỗng = shop mặc định của seller; `ses_version=beta` khớp trang health-center. */
function buildSesScoreOverviewUrl(sellerId: string): string {
  const url = new URL(buildSellerApiUrl(SES_SCORE_OVERVIEW_PATH, sellerId));
  url.searchParams.set('shop_id', '');
  url.searchParams.set('ses_version', 'beta');
  return url.toString();
}

/** Đọc payload `{ code, message, data }`; trả message lỗi khi TikTok từ chối. */
function readSellerApiPayload(res: JsonResult, label: string): { raw: unknown; error?: string } {
  const rec = asRecord(res.raw);
  if (!rec) {
    return { raw: null, error: `${label}: JSON không hợp lệ.` };
  }

  const code = rec.code;
  if (!res.ok || (code != null && Number(code) !== 0)) {
    const message =
      (typeof rec.message === 'string' && rec.message) || res.message || `${label} HTTP ${res.status}`;
    return { raw: null, error: `${label}: ${message}` };
  }

  return { raw: res.raw };
}

/**
 * Đọc AHR score + Shop Performance Score.
 *
 * Hai request độc lập nhau nên hỏng một cái vẫn gửi phần còn lại lên backend — chỉ báo thất bại
 * khi cả hai đều không lấy được.
 */
async function fetchShopScores(sellerIdInput: string): Promise<{
  ok: boolean;
  message?: string;
  sellerId?: string;
  scores?: ShopScoreSnapshot;
  warnings?: string[];
  harvestUsed?: number;
  harvestTotal?: number;
}> {
  const sellerId = (sellerIdInput || resolveSellerId() || '').trim();
  if (!sellerId) {
    return { ok: false, message: 'Không đọc được seller_id từ cookie.' };
  }

  const headers = { accept: '*/*', 'x-tt-oec-region': 'US' };
  const scores = emptyShopScoreSnapshot();
  const warnings: string[] = [];
  const harvest = pickHarvest(await readHarvestSnapshot(), sellerId);

  const ahrRes = harvest.ahr ?? (await fetchJson(buildAhrScoreUrl(sellerId), headers));
  const ahr = readSellerApiPayload(ahrRes, 'AHR score');
  if (ahr.error) {
    warnings.push(ahr.error);
  } else {
    scores.ahrScore = mapAhrScore(ahr.raw);
    if (scores.ahrScore == null) warnings.push('AHR score: payload không có data.score.');
  }

  const sesRes = harvest.ses ?? (await fetchJson(buildSesScoreOverviewUrl(sellerId), headers));
  const ses = readSellerApiPayload(sesRes, 'Shop Performance Score');
  if (ses.error) {
    warnings.push(ses.error);
  } else {
    Object.assign(scores, mapSesScoreOverview(ses.raw));
  }

  const harvestUsed = countHarvested([ahrRes, sesRes]);

  if (!hasAnyShopScore(scores)) {
    return {
      ok: false,
      message: warnings.join(' · ') || 'Không đọc được điểm nào từ Health Center.',
      sellerId,
      warnings,
    };
  }

  return { ok: true, sellerId, scores, warnings, harvestUsed, harvestTotal: 2 };
}
