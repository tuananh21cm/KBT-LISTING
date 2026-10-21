import { STORAGE_KEYS } from '../shared/config';
import type { AddProductResponse, FetchSheinSearchResponse } from '../shared/messages';
import { buildAudienceSearchQuery, normalizeAudienceKeyword } from '../shared/audienceSearchQuery';
import { extractProductId } from '../shared/productId';
import { rememberRecommendedGoodsIds } from '../shared/recommendedGoodsIds';
import {
  ADD_NAV_TIMEOUT_MS,
  ADD_PRODUCT_DELAY_MS,
  countByKeyword,
  DEFAULT_SEARCH_LIMIT,
  emptyProductSearch,
  fallbackEscalateDelayMs,
  MAX_SEARCH_PAGES,
  PAGE_SETTLE_MS,
  parseSheinSearchJson,
  passesClientFilters,
  productFingerprint,
  scoreSearchProducts,
  searchPageDelayMs,
  searchPoolCacheUsable,
  sortBySearchScore,
  SEARCH_POOL_CACHE_MAX,
  SPA_PAGE_CAPTURE_TIMEOUT_MS,
  SPA_SOFT_PAGE_CAPTURE_TIMEOUT_MS,
  CONTENT_SCRIPT_READY_MS,
  type ProductSearchCatalog,
  type ProductSearchKeywordInput,
  type ProductSearchState,
  type SavedKeywordRow,
  type SearchPoolCacheEntry,
  type SheinSearchCapture,
  type SheinSearchProduct,
} from '../shared/productSearch';
import { resolvePhrases } from '../shared/keywordMatchPhrases';
import {
  buildSheinSearchApiRequest,
  isSheinSearchAuthAbort,
  isSheinSearchRiskPayload,
  isSheinSearchSessionReady,
  sheinSearchAuthMessage,
  type SheinSearchSession,
} from '../shared/sheinSearchApi';
import {
  buildSheinFilteredSearchUrl,
  buildSheinPdpUrl,
  isSheinRiskUrl,
  sheinSearchPoolKey,
  sheinWarmupFilterKey,
} from '../shared/sheinSearchUrl';
import {
  learnStyleAttrsFromFacets,
  resolveStyleAttrs,
  type SheinStyleAttr,
} from '../shared/sheinStyleAttrs';
import {
  getAuth,
  getSavedKeywords,
  updateShopCatalogProfile,
  type CatalogProfileResult,
} from './kinhbacApi';
import type { FsShopOption } from '../shared/types';

export type PdpCapture = {
  url: string;
  html: string;
  reviewCount?: number | null;
  averageRating?: number | null;
  platform?: string | null;
  stock?: number | null;
  storeCode?: string | null;
  storeLogo?: string | null;
  storeTitle?: string | null;
  soldByTips?: string | null;
  shipsFromTips?: string | null;
  shopRank?: number | null;
  storeAttention?: number | null;
  shippingDays?: number | null;
  shippingDayPercent?: number | null;
  shippingDayPercentDesc?: string | null;
  isLocalProduct?: boolean | null;
  isSupportQuickShip?: boolean | null;
};

export type OpenUrlOpts = {
  /** omit = chrome.tabs.update, không Referer — giống dán URL thanh địa chỉ. */
  referrer?: 'keep' | 'omit';
  newTab?: boolean;
};

export type ProductSearchHost = {
  openUrl(url: string, opts?: OpenUrlOpts): Promise<{ ok: boolean; message?: string; tabId?: number }>;
  getActiveTab(): Promise<chrome.tabs.Tab | null>;
  resetSheinSearchSession(tabId: number): Promise<void>;
  /** Bấm thanh phân trang pdsearch của SPA — đổi nội dung, không load lại trang. */
  gotoSheinSearchPage(tabId: number, page: number, query: string): Promise<boolean>;
  captureActiveTab(): Promise<PdpCapture | null>;
  addProduct(
    url: string,
    html: string,
    pdp?: PdpCapture,
    addedFrom?: 'recommended' | 'manual',
  ): Promise<AddProductResponse>;
  beginTabAdding(tabId: number, url: string, flow?: string): Promise<void>;
  endTabAdding(tabId: number | undefined, outcome: 'success' | 'failed' | 'cancelled'): Promise<void>;
  loadShops(force?: boolean): Promise<FsShopOption[]>;
  patchShopCatalog(shopFsId: number, patch: CatalogProfileResult | ProductSearchCatalog): Promise<void>;
  hasSelectedShops(): Promise<boolean>;
  isProductFullyAdded(goodsId: string): Promise<boolean>;
};

let searchGen = 0;
let searchActiveGen = 0;
let cancelled = false;
let addGen = 0;
let addActiveGen = 0;
let addCancelled = false;
let latestSession: SheinSearchSession | null = null;
/** JSON list do CHÍNH SPA gọi trong lần warmup gần nhất — dùng được mà không cần forge request. */
let latestCapture: SheinSearchCapture | null = null;
let sessionWaiter: {
  resolve: (session: SheinSearchSession | null) => void;
} | null = null;
let productSearchWriteChain: Promise<unknown> = Promise.resolve();

function enqueueStateWrite<T>(fn: () => Promise<T>): Promise<T> {
  const run = productSearchWriteChain.then(fn);
  productSearchWriteChain = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

export async function recoverStaleProductSearch(): Promise<ProductSearchState> {
  return enqueueStateWrite(async () => {
    const current = await readProductSearch();
    let next = current;
    if (current.status === 'searching' && !isSearchActive()) {
      next = await writeProductSearch({
        ...next,
        status: 'cancelled',
        message: 'Tìm kiếm bị gián đoạn (extension reload).',
        currentKeyword: undefined,
        currentPage: undefined,
      });
    }
    if (next.add.status === 'adding' && !isAddActive()) {
      next = await writeProductSearch({
        ...next,
        add: {
          ...next.add,
          status: 'done',
          message: 'Thêm sản phẩm bị gián đoạn (extension reload).',
        },
      });
    }
    return next;
  });
}

function writeSearchIfCurrent(
  gen: number,
  patch: ProductSearchState,
): Promise<ProductSearchState | null> {
  return enqueueStateWrite(async () => {
    if (gen !== searchGen) return null;
    return writeProductSearch(patch);
  });
}

function writeAddIfCurrent(
  gen: number,
  patch: ProductSearchState,
): Promise<ProductSearchState | null> {
  return enqueueStateWrite(async () => {
    if (gen !== addGen) return null;
    return writeProductSearch(patch);
  });
}

export function markProductSearchInterrupted(): Promise<ProductSearchState> {
  return enqueueStateWrite(async () => {
    const current = await readProductSearch();
    if (current.status !== 'searching' && current.add.status !== 'adding') return current;
    return writeProductSearch({
      ...current,
      status: current.status === 'searching' ? 'cancelled' : current.status,
      message: 'Đã dừng.',
      currentKeyword: undefined,
      currentPage: undefined,
      add:
        current.add.status === 'adding'
          ? { ...current.add, status: 'done', message: 'Đã dừng thêm sản phẩm.' }
          : current.add,
    });
  });
}

export function onSheinSearchCapture(capture: SheinSearchCapture): void {
  if (!capture || !Array.isArray(capture.products) || capture.products.length === 0) return;
  if (!latestCapture || Number(capture.at) >= Number(latestCapture.at)) latestCapture = capture;
}

export function onSheinSearchSession(session: SheinSearchSession): void {
  if (!isSheinSearchSessionReady(session)) return;
  latestSession = session;
  if (sessionWaiter) {
    const { resolve } = sessionWaiter;
    sessionWaiter = null;
    resolve(session);
  }
}

export async function readProductSearch(): Promise<ProductSearchState> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.productSearch);
  const state = raw[STORAGE_KEYS.productSearch] as ProductSearchState | undefined;
  if (!state || !Array.isArray(state.products)) return emptyProductSearch();
  return state;
}

export async function writeProductSearch(state: ProductSearchState): Promise<ProductSearchState> {
  const next = { ...state, updatedAt: Date.now() };
  await chrome.storage.local.set({ [STORAGE_KEYS.productSearch]: next });
  return next;
}

export async function clearProductSearch(): Promise<void> {
  await chrome.storage.local.remove(STORAGE_KEYS.productSearch);
}

function isSearchActive(): boolean {
  return searchActiveGen !== 0;
}

function isAddActive(): boolean {
  return addActiveGen !== 0;
}

/** Số job quick-add list đang running (không tính queued). */
let quickAddActiveCount = 0;

export function adjustQuickAddActiveCount(delta: number): void {
  quickAddActiveCount = Math.max(0, quickAddActiveCount + delta);
}

export function getQuickAddActiveCount(): number {
  return quickAddActiveCount;
}

/** Batch Search add hoặc bất kỳ quick-add running. */
export function isProductAddActive(): boolean {
  return isAddActive() || quickAddActiveCount > 0;
}

/** Search đang chạy hoặc batch Search đang thêm — chặn enqueue quick-add. */
export function isSearchOrBatchAddBlocking(): boolean {
  return isSearchActive() || isAddActive();
}

/**
 * @deprecated Quick-add dùng queue + adjustQuickAddActiveCount; giữ cho tương thích.
 */
export function beginStandaloneProductAdd(): number | null {
  if (isSearchActive() || isAddActive()) return null;
  const gen = ++addGen;
  addActiveGen = gen;
  return gen;
}

export function endStandaloneProductAdd(gen: number): void {
  if (addActiveGen === gen) addActiveGen = 0;
}

async function readLearnedStyleAttrs(): Promise<SheinStyleAttr[]> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinStyleAttrs);
  const rows = raw[STORAGE_KEYS.sheinStyleAttrs];
  return Array.isArray(rows) ? (rows as SheinStyleAttr[]) : [];
}

async function writeLearnedStyleAttrs(rows: SheinStyleAttr[]): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.sheinStyleAttrs]: rows });
}

async function readPoolCache(): Promise<Record<string, SearchPoolCacheEntry>> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.searchPoolCache);
  const map = raw[STORAGE_KEYS.searchPoolCache];
  return map && typeof map === 'object' && !Array.isArray(map)
    ? (map as Record<string, SearchPoolCacheEntry>)
    : {};
}

async function readPoolCacheEntry(key: string): Promise<SearchPoolCacheEntry | null> {
  if (!key) return null;
  const map = await readPoolCache();
  return map[key] ?? null;
}

/** Ghi pool theo khóa (câu tìm + bộ lọc URL), giữ LRU theo `at` để không phình storage. */
async function writePoolCacheEntry(key: string, entry: SearchPoolCacheEntry): Promise<void> {
  if (!key || entry.products.length === 0) return;
  const map = await readPoolCache();
  map[key] = entry;
  const keys = Object.keys(map);
  if (keys.length > SEARCH_POOL_CACHE_MAX) {
    const stale = keys
      .sort((a, b) => (map[a]?.at ?? 0) - (map[b]?.at ?? 0))
      .slice(0, keys.length - SEARCH_POOL_CACHE_MAX);
    for (const k of stale) delete map[k];
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.searchPoolCache]: map });
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function clampLimit(n: number): number {
  if (!Number.isFinite(n)) return DEFAULT_SEARCH_LIMIT;
  return Math.min(50, Math.max(1, Math.floor(n)));
}

export async function handleListSavedKeywords(
  shopLocalId: string,
): Promise<{ ok: boolean; message?: string; items: SavedKeywordRow[] }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập KBT Craper.', items: [] };
  const id = (shopLocalId || '').trim();
  if (!id) return { ok: false, message: 'Thiếu shop.', items: [] };
  try {
    const data = await getSavedKeywords(id);
    const items = Array.isArray(data?.items) ? data.items : [];
    return { ok: true, items };
  } catch (err) {
    return {
      ok: false,
      message: (err as Error).message || 'Không tải được keyword đã lưu.',
      items: [],
    };
  }
}

export function cancelProductSearch(): void {
  cancelled = true;
  searchGen += 1;
  addCancelled = true;
  addGen += 1;
  if (sessionWaiter) {
    const { resolve } = sessionWaiter;
    sessionWaiter = null;
    resolve(null);
  }
}

const SHEIN_RISK_MESSAGE =
  'Shein đang chặn phiên (trang risk/limit). Mở us.shein.com trên tab, hoàn thành xác minh, rồi tìm lại.';
const SHEIN_AUTH_MESSAGE =
  'Header Shein hết hạn. Mở lại trang search, hoàn thành xác minh nếu có, rồi tìm lại.';
/** Số lần làm mới phiên vì 401/403 cho mỗi keyword. */
const MAX_AUTH_REWARMUPS = 3;
/** Trần tổng số lần warmup (403 + làm mới định kỳ) — chặn vòng lặp vô hạn. */
const MAX_TOTAL_REWARMUPS = 8;
/** Backoff giữa các lần làm mới phiên vì 403. */
const AUTH_REWARMUP_BACKOFF_MS = [800, 2000, 4000] as const;

type WarmupWaitResult =
  | { reason: 'ok'; session: SheinSearchSession; tabId?: number; capture?: SheinSearchCapture | null }
  | { reason: 'risk' }
  | { reason: 'timeout' }
  | { reason: 'cancelled' };

type ApiPageResult =
  | { reason: 'ok'; capture: SheinSearchCapture }
  | { reason: 'auth'; message: string }
  | { reason: 'risk' }
  | { reason: 'error'; message: string }
  | { reason: 'cancelled' };

/** Kết quả lấy 1 trang: capture SPA → Enter URL đúng trang → forge XHR (phương án cuối). */
type PageOutcome =
  | { kind: 'capture'; capture: SheinSearchCapture }
  | { kind: 'nomore' }
  | { kind: 'risk' }
  | { kind: 'auth'; message: string }
  | { kind: 'error'; message: string }
  | { kind: 'cancelled' };

async function tabSettledOnRisk(tabId?: number): Promise<boolean> {
  if (!tabId) return false;
  const tab = await chrome.tabs.get(tabId).catch(() => null);
  return !!tab && tab.status === 'complete' && isSheinRiskUrl(tab.url);
}

function filteredSearchUrl(
  keyword: string,
  catalog: ProductSearchCatalog,
  learned: SheinStyleAttr[],
  page = 1,
): string {
  const { attrIds, attrValues } = resolveStyleAttrs(catalog.styleCodes, learned);
  return buildSheinFilteredSearchUrl({
    keyword,
    page,
    priceMinUsd: catalog.priceMinUsd,
    priceMaxUsd: catalog.priceMaxUsd,
    attrIds,
    attrValues,
    preferQuickShip: !!catalog.preferQuickShip,
    sheinSort: catalog.sheinSort,
  });
}

function resolvedStyleAttrs(
  catalog: ProductSearchCatalog,
  learned: SheinStyleAttr[],
): { attrIds: string[]; attrValues: string[] } {
  return resolveStyleAttrs(catalog.styleCodes, learned);
}

async function pullSearchSession(
  host: ProductSearchHost,
  tabId?: number,
  minAt?: number,
): Promise<SheinSearchSession | null> {
  const freshEnough = (session: SheinSearchSession | null | undefined): session is SheinSearchSession =>
    isSheinSearchSessionReady(session) && (minAt == null || (session?.at ?? 0) >= minAt);
  const tab = tabId
    ? await chrome.tabs.get(tabId).catch(() => null)
    : await host.getActiveTab();
  if (!tab?.id) {
    return freshEnough(latestSession) ? latestSession : null;
  }
  try {
    const res = (await chrome.tabs.sendMessage(tab.id, { type: 'GET_SHEIN_SEARCH_SESSION' })) as
      | { session?: SheinSearchSession }
      | undefined;
    const session = res?.session;
    if (freshEnough(session)) {
      onSheinSearchSession(session);
      return session;
    }
    // Content sống — không dùng snapshot SW của document cũ (armorToken hết hạn → 403).
    if (res) return null;
  } catch {
    /* content chưa nạp */
  }
  return freshEnough(latestSession) ? latestSession : null;
}

function isCaptureUsable(
  capture: SheinSearchCapture | null | undefined,
  minAt?: number,
): capture is SheinSearchCapture {
  if (!capture || !Array.isArray(capture.products) || capture.products.length === 0) return false;
  return minAt == null || Number(capture.at) >= minAt;
}

/**
 * JSON SPA đã hớt được có đúng là trang / câu tìm đang cần không.
 * pdsearch và replay dùng cùng keyword gốc nên chỉ cần so chuẩn hóa một câu.
 */
function captureMatchesPage(
  capture: SheinSearchCapture | null | undefined,
  opts: { page: number; query: string; minAt?: number },
): capture is SheinSearchCapture {
  if (!isCaptureUsable(capture, opts.minAt)) return false;
  if (Math.max(1, Math.floor(Number(capture.page) || 1)) !== opts.page) return false;
  const got = normalizeAudienceKeyword(capture.keywords);
  if (!got) return true;
  return got === normalizeAudienceKeyword(opts.query);
}

async function pullSearchCapture(
  host: ProductSearchHost,
  tabId?: number,
  minAt?: number,
): Promise<SheinSearchCapture | null> {
  if (isCaptureUsable(latestCapture, minAt)) return latestCapture;
  const tab = tabId ? await chrome.tabs.get(tabId).catch(() => null) : await host.getActiveTab();
  if (!tab?.id) return null;
  try {
    const res = (await chrome.tabs.sendMessage(tab.id, { type: 'GET_SHEIN_SEARCH_CAPTURE' })) as
      | { capture?: SheinSearchCapture }
      | undefined;
    if (isCaptureUsable(res?.capture, minAt)) {
      onSheinSearchCapture(res.capture);
      return res.capture;
    }
  } catch {
    /* content chưa nạp */
  }
  return null;
}

/** Chờ đúng JSON trang do SPA phát (khớp `page` + `keywords`) sau khi điều hướng URL. */
async function waitForCapturePage(
  host: ProductSearchHost,
  tabId: number | undefined,
  wanted: { page: number; query: string; since: number },
  timeoutMs: number,
): Promise<SheinSearchCapture | null> {
  const deadline = Date.now() + timeoutMs;
  const matches = (c: SheinSearchCapture | null) =>
    captureMatchesPage(c, { page: wanted.page, query: wanted.query, minAt: wanted.since });
  while (Date.now() < deadline) {
    if (cancelled) return null;
    if (await tabSettledOnRisk(tabId)) return null;
    const capture = await pullSearchCapture(host, tabId, wanted.since);
    if (matches(capture)) return capture;
    await sleep(200);
  }
  const capture = await pullSearchCapture(host, tabId, wanted.since);
  return matches(capture) ? capture : null;
}

async function waitForContentScript(tabId: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (cancelled) return false;
    try {
      await chrome.tabs.sendMessage(tabId, { type: 'GET_SHEIN_SEARCH_SESSION' });
      return true;
    } catch {
      await sleep(150);
    }
  }
  return false;
}

/**
 * Mở pdsearch bằng `tabs.update` một lần — giống Enter thanh địa chỉ.
 * Không hard-reload (`chrome.tabs.reload`): reload cắt request SPA đang ký, chậm và dễ /risk/.
 * SPA tự fetch + tự ký; extension chỉ hứng JSON khớp `page`.
 */
async function openSearchPageLikeAddressBar(
  host: ProductSearchHost,
  keyword: string,
  catalog: ProductSearchCatalog,
  learned: SheinStyleAttr[],
  page: number,
): Promise<WarmupWaitResult> {
  const url = filteredSearchUrl(keyword, catalog, learned, page);
  if (!url) return { reason: 'timeout' };
  if (cancelled) return { reason: 'cancelled' };

  const existing = await host.getActiveTab();
  const existingTabId = existing?.id;
  latestCapture = null;

  const alreadyOnUrl =
    !!existing?.url && sheinWarmupFilterKey(existing.url) === sheinWarmupFilterKey(url);
  if (alreadyOnUrl && existingTabId) {
    const existingCapture = await waitForCapturePage(
      host,
      existingTabId,
      { page, query: keyword, since: 0 },
      1500,
    );
    if (existingCapture) {
      const session = (await pullSearchSession(host, existingTabId)) ?? latestSession;
      if (session && isSheinSearchSessionReady(session)) {
        return { reason: 'ok', session, tabId: existingTabId, capture: existingCapture };
      }
    }
  }

  if (existingTabId && !alreadyOnUrl) {
    await host.resetSheinSearchSession(existingTabId);
    latestSession = null;
  }

  const since = Date.now();
  const opened = await host.openUrl(url, { referrer: 'omit' });
  const tabId = opened.ok ? opened.tabId ?? existingTabId : undefined;
  if (!opened.ok || !tabId) return { reason: 'timeout' };

  const capture = await waitForCapturePage(
    host,
    tabId,
    { page, query: keyword, since },
    SPA_PAGE_CAPTURE_TIMEOUT_MS,
  );
  if (cancelled) return { reason: 'cancelled' };
  if (await tabSettledOnRisk(tabId)) return { reason: 'risk' };
  if (!capture) return { reason: 'timeout' };

  const session = (await pullSearchSession(host, tabId, since)) ?? latestSession;
  if (!session || !isSheinSearchSessionReady(session)) return { reason: 'timeout' };
  return { reason: 'ok', session, tabId, capture };
}

/**
 * Kết quả bấm phân trang SPA. `session` **không bắt buộc**: JSON đã hớt được là đủ cho trang
 * này, session chỉ dùng cho forge XHR về sau — thiếu thì giữ session cũ, đừng bỏ JSON tốt.
 */
type SoftPageResult =
  | { reason: 'ok'; capture: SheinSearchCapture; session: SheinSearchSession | null; tabId: number }
  | { reason: 'risk' }
  | { reason: 'timeout' }
  | { reason: 'cancelled' };

/**
 * Trang 2+: bấm thanh phân trang của chính SPA — nội dung đổi, **không** load lại trang.
 * Nhẹ và ít bị /risk/ hơn `tabs.update` (không dựng lại document, không mất ngữ cảnh phiên)
 * và sạch hơn forge XHR (request do Shein tự phát nên ký đủ). Extension chỉ hứng JSON.
 */
async function gotoPageViaSpaPagination(
  host: ProductSearchHost,
  keyword: string,
  page: number,
  tabId: number | undefined,
): Promise<SoftPageResult> {
  if (!tabId || page <= 1) return { reason: 'timeout' };
  if (cancelled) return { reason: 'cancelled' };
  if (await tabSettledOnRisk(tabId)) return { reason: 'risk' };

  const since = Date.now();
  latestCapture = null;
  if (!(await host.gotoSheinSearchPage(tabId, page, keyword))) return { reason: 'timeout' };

  const capture = await waitForCapturePage(
    host,
    tabId,
    { page, query: keyword, since },
    SPA_SOFT_PAGE_CAPTURE_TIMEOUT_MS,
  );
  if (cancelled) return { reason: 'cancelled' };
  if (await tabSettledOnRisk(tabId)) return { reason: 'risk' };
  if (!capture) return { reason: 'timeout' };

  // Hook lưu session ở MỌI request search nên click phân trang cũng làm mới header.
  const session =
    (await pullSearchSession(host, tabId, since)) ?? (await pullSearchSession(host, tabId));
  return { reason: 'ok', capture, session, tabId };
}

async function fetchSearchApiPage(
  host: ProductSearchHost,
  request: { url: string; method?: string; body?: string },
  tabId: number | undefined,
  headers: Record<string, string> | undefined,
  page: number,
): Promise<ApiPageResult> {
  if (cancelled) return { reason: 'cancelled' };
  if (await tabSettledOnRisk(tabId)) return { reason: 'risk' };
  const tab = tabId
    ? await chrome.tabs.get(tabId).catch(() => null)
    : await host.getActiveTab();
  if (!tab?.id) return { reason: 'error', message: 'Không có tab Shein để gọi API.' };
  let res: FetchSheinSearchResponse | undefined;
  for (let attempt = 0; attempt < 8; attempt++) {
    if (cancelled) return { reason: 'cancelled' };
    try {
      res = (await chrome.tabs.sendMessage(tab.id, {
        type: 'FETCH_SHEIN_SEARCH',
        url: request.url,
        method: request.method,
        body: request.body,
        headers,
        page,
      })) as FetchSheinSearchResponse | undefined;
      break;
    } catch {
      if (attempt === 7) {
        return { reason: 'error', message: 'Content script chưa sẵn sàng trên tab Shein.' };
      }
      await sleep(200);
    }
  }
  if (cancelled) return { reason: 'cancelled' };
  if ((await tabSettledOnRisk(tab?.id)) || isSheinSearchRiskPayload(res?.json)) return { reason: 'risk' };
  if (isSheinSearchAuthAbort(res?.message, res?.status)) {
    return { reason: 'auth', message: sheinSearchAuthMessage(res?.message, res?.status) };
  }
  if (!res?.ok) {
    return { reason: 'error', message: res?.message || 'Gọi API Shein thất bại.' };
  }
  const capture = parseSheinSearchJson(request.url, res.json);
  if (!capture) return { reason: 'error', message: 'API Shein không trả danh sách sản phẩm.' };
  return { reason: 'ok', capture };
}

async function waitForTabGoodsId(
  host: ProductSearchHost,
  goodsId: string,
  timeoutMs: number,
): Promise<chrome.tabs.Tab | null> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (addCancelled) return null;
    const tab = await host.getActiveTab();
    const url = tab?.url || '';
    if (extractProductId(url, 'shein') === goodsId) return tab;
    await sleep(250);
  }
  return host.getActiveTab();
}

function toSearched(
  row: SheinSearchCapture['products'][number],
  displayKeyword: string,
  searchQuery: string,
): SheinSearchProduct | null {
  const goodsId = String(row.goodsId || '').trim();
  if (!goodsId) return null;
  const url = buildSheinPdpUrl(goodsId, row.goodsUrlName, {
    keyword: (searchQuery || displayKeyword).trim(),
  });
  if (!url) return null;
  return {
    goodsId,
    name: row.name || goodsId,
    image: row.image ?? null,
    price: row.price ?? null,
    rating: row.rating ?? null,
    reviewCount: row.reviewCount ?? null,
    soldCount: row.soldCount ?? null,
    variantCount: row.variantCount ?? null,
    goodsUrlName: row.goodsUrlName ?? null,
    cateName: row.cateName ?? null,
    isLocal: row.isLocal,
    isQuickShip: row.isQuickShip,
    isSheinClub: row.isSheinClub,
    isUsWarehouse: row.isUsWarehouse,
    score: row.score ?? null,
    scoreTitle: row.scoreTitle ?? null,
    scoreProven: row.scoreProven ?? row.scoreSold ?? null,
    scoreSold: row.scoreProven ?? row.scoreSold ?? null,
    scoreRating: row.scoreRating ?? null,
    scorePrice: row.scorePrice ?? null,
    url,
    keyword: displayKeyword,
  };
}

async function persistCatalog(
  host: ProductSearchHost,
  shopLocalId: string,
  shopFsId: number,
  catalog: ProductSearchCatalog,
): Promise<string | undefined> {
  const categories = (catalog.categories ?? []).filter((c) => String(c.id || '').trim());
  if (categories.length === 0 && !catalog.categoryId?.trim()) {
    return 'Shop chưa gán danh mục — không ghi catalog, vẫn tìm theo bộ lọc hiện tại.';
  }
  try {
    const saved = await updateShopCatalogProfile(shopLocalId, {
      categories: categories.length
        ? categories
        : [{ id: catalog.categoryId!.trim(), name: catalog.categoryName?.trim() || catalog.categoryId!.trim() }],
      categoryId: catalog.categoryId,
      categoryName: catalog.categoryName,
      ageMin: catalog.ageMin ?? null,
      ageMax: catalog.ageMax ?? null,
      priceMinUsd: catalog.priceMinUsd ?? null,
      priceMaxUsd: catalog.priceMaxUsd ?? null,
      preferQuickShip: !!catalog.preferQuickShip,
      preferLocal: !!catalog.preferLocal,
      preferUsWarehouse: !!catalog.preferUsWarehouse,
      preferSheinClub: !!catalog.preferSheinClub,
      minRating: Number(catalog.minRating) || 0,
      minSoldCount: Number(catalog.minSoldCount) || 0,
      maxVariantCount: Number(catalog.maxVariantCount) || 0,
      bodySizeCode: catalog.bodySizeCode || null,
      styleCodes: catalog.styleCodes ?? [],
    });
    await host.patchShopCatalog(shopFsId, saved);
    return undefined;
  } catch (err) {
    return (err as Error).message || 'Không lưu được hồ sơ danh mục shop.';
  }
}

export async function handleSearchProducts(
  host: ProductSearchHost,
  payload: {
    shopLocalId: string;
    shopFsId: number;
    catalog: ProductSearchCatalog;
    keywords: ProductSearchKeywordInput[];
  },
): Promise<{ ok: boolean; message?: string; state: ProductSearchState }> {
  const auth = await getAuth();
  if (!auth) {
    const state = await writeProductSearch({
      ...emptyProductSearch(),
      status: 'error',
      message: 'Chưa đăng nhập KBT Craper.',
    });
    return { ok: false, message: state.message, state };
  }

  const keywords = (payload.keywords ?? [])
    .map((k) => {
      const keyword = String(k.keyword || '').trim();
      const searchQuery = buildAudienceSearchQuery(keyword, payload.catalog.bodySizeCode);
      const phrases = resolvePhrases(
        searchQuery,
        k.mustPhrases,
        k.shouldPhrases,
        k.minDistinctiveHits,
      );
      return {
        keyword,
        searchQuery,
        limit: clampLimit(k.limit),
        mustPhrases: phrases.must,
        shouldPhrases: phrases.should,
        minDistinctiveHits: phrases.minDistinctiveHits,
      };
    })
    .filter((k) => k.searchQuery.length > 0)
    .slice(0, 1);
  if (keywords.length === 0) {
    return {
      ok: false,
      message: 'Chọn 1 keyword.',
      state: await readProductSearch(),
    };
  }
  if (isProductAddActive()) {
    return {
      ok: false,
      message: 'Đang thêm sản phẩm — đợi xong hoặc bấm Dừng.',
      state: await readProductSearch(),
    };
  }

  cancelled = false;
  const gen = ++searchGen;
  searchActiveGen = gen;
  latestSession = null;
  latestCapture = null;
  const persistNote = (msg?: string) => (msg ? `${msg} ` : '');
  let lastIssue = '';

  try {
    const catalogWarning = await persistCatalog(
      host,
      payload.shopLocalId,
      payload.shopFsId,
      payload.catalog,
    );
    if (gen !== searchGen) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }

    const started = await writeSearchIfCurrent(gen, {
      ...emptyProductSearch(),
      status: 'searching',
      shopLocalId: payload.shopLocalId,
      shopFsId: payload.shopFsId,
      message: `${persistNote(catalogWarning)}Đang tìm sản phẩm...`,
      products: [],
      keywordCounts: {},
      startedAt: Date.now(),
    });
    if (!started) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }
    // Kiểu tường minh để closure làm mới phiên bên dưới không mất narrowing.
    let state: ProductSearchState = started;

    const learned = await readLearnedStyleAttrs();
    const seenIds = new Set<string>();

    const firstKeyword = keywords[0]?.searchQuery ?? '';
    const activeTab = await host.getActiveTab();
    if (activeTab?.id) {
      await host.resetSheinSearchSession(activeTab.id);
      latestSession = null;
    }
    if (cancelled || gen !== searchGen) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }

    const headerMsg = await writeSearchIfCurrent(gen, {
      ...state,
      status: 'searching',
      currentKeyword: firstKeyword,
      currentPage: 1,
      message: `${persistNote(catalogWarning)}Đang mở «${firstKeyword}» trên Shein…`,
    });
    if (!headerMsg) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }
    state = headerMsg;

    const warmup = await openSearchPageLikeAddressBar(host, firstKeyword, payload.catalog, learned, 1);
    if (cancelled || gen !== searchGen) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }
    if (warmup.reason === 'risk') {
      lastIssue = SHEIN_RISK_MESSAGE;
      const blocked = await writeSearchIfCurrent(gen, {
        ...state,
        status: 'error',
        message: persistNote(catalogWarning) + lastIssue,
        currentKeyword: undefined,
        currentPage: undefined,
      });
      state = blocked ?? (await readProductSearch());
      return { ok: false, message: state.message, state };
    }
    if (warmup.reason !== 'ok' || !warmup.session) {
      lastIssue =
        'Không bắt được dữ liệu Shein (timeout / captcha). Mở us.shein.com rồi tìm lại.';
      const failed = await writeSearchIfCurrent(gen, {
        ...state,
        status: 'error',
        message: persistNote(catalogWarning) + lastIssue,
        currentKeyword: undefined,
        currentPage: undefined,
      });
      state = failed ?? (await readProductSearch());
      return { ok: false, message: state.message, state };
    }

    let session = warmup.session;
    let searchTabId = warmup.tabId ?? (await host.getActiveTab())?.id;
    if (searchTabId) {
      await waitForContentScript(searchTabId, CONTENT_SCRIPT_READY_MS);
    }
    /** JSON trang do chính SPA gọi ở lần warmup gần nhất — dùng trước khi tự replay. */
    let pendingCapture: SheinSearchCapture | null = warmup.capture ?? null;
    let authRewarmups = 0;
    let totalRewarmups = 0;
    /** Tổng SP thô đã gom qua mọi keyword — mẫu số của "chọn X/Y" trên thông báo cuối. */
    let poolTotal = 0;

    const abortRisk = async (current: ProductSearchState) => {
      lastIssue = SHEIN_RISK_MESSAGE;
      const blocked = await writeSearchIfCurrent(gen, {
        ...current,
        status: 'error',
        message: persistNote(catalogWarning) + lastIssue,
        currentKeyword: undefined,
        currentPage: undefined,
      });
      return blocked ?? (await readProductSearch());
    };

    /** Mọi trang: `tabs.update` một lần (Enter URL) — không hard-reload, không replaceState. */
    const navigateToPage = async (
      keyword: string,
      page: number,
      note: string,
    ): Promise<'ok' | 'risk' | 'failed' | 'cancelled'> => {
      const notice = await writeSearchIfCurrent(gen, {
        ...state,
        status: 'searching',
        currentKeyword: keyword,
        currentPage: page,
        message: `${persistNote(catalogWarning)}${note}`,
      });
      if (!notice) return 'cancelled';
      state = notice;

      const again = await openSearchPageLikeAddressBar(host, keyword, payload.catalog, learned, page);
      if (again.reason === 'cancelled' || cancelled || gen !== searchGen) return 'cancelled';
      if (again.reason === 'risk') return 'risk';
      if (again.reason !== 'ok' || !again.session) return 'failed';
      session = again.session;
      searchTabId = again.tabId ?? searchTabId;
      if (searchTabId) await waitForContentScript(searchTabId, CONTENT_SCRIPT_READY_MS);
      pendingCapture = again.capture ?? null;
      return 'ok';
    };

    /** Làm mới phiên khi bị 401/403 — điều hướng lại đúng trang đang cần. */
    const refreshSession = async (
      keyword: string,
      page: number,
      note: string,
    ): Promise<'ok' | 'risk' | 'failed' | 'cancelled'> => {
      totalRewarmups += 1;
      return navigateToPage(keyword, page, note);
    };

    for (const item of keywords) {
      if (cancelled || gen !== searchGen) break;
      // Cùng `searchQuery` trên pdsearch và BFF: capture SPA khớp replay, không lệch pool.
      const query = item.searchQuery;
      const pool: SheinSearchCapture['products'] = [];
      const poolSeen = new Set<string>();
      let lastFingerprint = '';
      let pagesFetched = 0;
      let reachedEnd = false;
      let reportedTotal = 0;

      // Khóa cache theo (câu tìm + bộ lọc URL), bỏ `page` — cố định trong 1 lượt keyword.
      const poolKey = sheinSearchPoolKey(filteredSearchUrl(query, payload.catalog, learned, 1));
      const cached = await readPoolCacheEntry(poolKey);
      if (searchPoolCacheUsable(cached, item.limit)) {
        for (const raw of cached.products) {
          const goodsId = String(raw.goodsId || '').trim();
          if (!goodsId || poolSeen.has(goodsId)) continue;
          poolSeen.add(goodsId);
          pool.push(raw);
        }
        pagesFetched = cached.pagesFetched;
        reportedTotal = cached.total;
        // Cache đủ dùng — không phân trang lại (kể cả khi chưa đánh exhausted).
        reachedEnd = true;
        const hit = await writeSearchIfCurrent(gen, {
          ...state,
          status: 'searching',
          currentKeyword: query,
          message: `${persistNote(catalogWarning)}Dùng lại pool đã lưu «${query}» (${pool.length} SP).`,
        });
        if (hit) state = hit;
      }

      const persistPool = async () => {
        await writePoolCacheEntry(poolKey, {
          query,
          products: pool,
          total: reportedTotal,
          pagesFetched,
          exhausted: reachedEnd,
          at: Date.now(),
        });
      };

      /** Luôn `conservative`: chỉ overlay keyword + page, giữ mọi param SPA đã ký. */
      const replayRequest = (page: number) => {
        const style = resolvedStyleAttrs(payload.catalog, learned);
        return buildSheinSearchApiRequest({
          templateUrl: session.templateUrl,
          method: session.method,
          templateBody: session.body,
          keywords: query,
          page,
          catalog: payload.catalog,
          attrIds: style.attrIds,
          attrValues: style.attrValues,
          conservative: true,
        });
      };

      const fetchReplayPage = async (page: number): Promise<ApiPageResult> => {
        const fresh = await pullSearchSession(host, searchTabId);
        if (fresh) session = fresh;
        let api = await fetchSearchApiPage(
          host,
          replayRequest(page),
          searchTabId,
          session.headers,
          page,
        );
        if (api.reason === 'error') {
          // Cùng một request forge bắn lại — giãn đúng nhịp leo thang, không 600ms như trước.
          await sleep(fallbackEscalateDelayMs());
          if (cancelled || gen !== searchGen) return { reason: 'cancelled' };
          api = await fetchSearchApiPage(
            host,
            replayRequest(page),
            searchTabId,
            session.headers,
            page,
          );
        }

        while (
          api.reason === 'auth' &&
          authRewarmups < MAX_AUTH_REWARMUPS &&
          totalRewarmups < MAX_TOTAL_REWARMUPS
        ) {
          const backoff =
            AUTH_REWARMUP_BACKOFF_MS[Math.min(authRewarmups, AUTH_REWARMUP_BACKOFF_MS.length - 1)];
          authRewarmups += 1;
          await sleep(backoff);
          if (cancelled || gen !== searchGen) return { reason: 'cancelled' };
          const refreshed = await refreshSession(
            query,
            page,
            `Shein từ chối phiên — đang làm mới (${authRewarmups}/${MAX_AUTH_REWARMUPS})…`,
          );
          if (refreshed === 'cancelled') return { reason: 'cancelled' };
          if (refreshed === 'risk') return { reason: 'risk' };
          if (refreshed === 'failed') break;
          if (captureMatchesPage(pendingCapture, { page, query })) {
            const capture = pendingCapture;
            pendingCapture = null;
            return { reason: 'ok', capture };
          }
          api = await fetchSearchApiPage(host, replayRequest(page), searchTabId, session.headers, page);
        }

        return api;
      };

      /**
       * Lấy JSON một trang:
       * (1) capture SPA khớp — trang 1 gần như luôn lấy từ warmup (Enter URL);
       * (2) trang 2+: bấm thanh phân trang của SPA — đổi nội dung, KHÔNG load lại trang;
       * (3) Enter URL `pdsearch` đúng `page` (`tabs.update`, không tính trần 403) khi không
       *     thấy/không bấm được phân trang — điều hướng thật để SPA tự ký;
       * (4) phương án cuối: forge XHR MAIN-world; 403 thì điều hướng lại đúng trang đó.
       */
      const acquirePageJson = async (page: number): Promise<PageOutcome> => {
        const wanted = { page, query };

        if (captureMatchesPage(pendingCapture, wanted)) {
          const capture = pendingCapture;
          pendingCapture = null;
          return { kind: 'capture', capture };
        }

        /** Nghỉ trước khi leo sang cách mạnh tay hơn; trả `false` khi đã bị dừng. */
        const pauseBeforeEscalate = async (): Promise<boolean> => {
          await sleep(fallbackEscalateDelayMs());
          return !cancelled && gen === searchGen;
        };

        // Trang 2+: để SPA tự sang trang — nội dung đổi tại chỗ, không dựng lại document.
        if (page > 1) {
          const soft = await gotoPageViaSpaPagination(host, query, page, searchTabId);
          if (soft.reason === 'cancelled' || cancelled || gen !== searchGen) {
            return { kind: 'cancelled' };
          }
          if (soft.reason === 'risk') return { kind: 'risk' };
          if (soft.reason === 'ok') {
            // Không có session mới thì giữ session cũ — JSON trang này đã đủ dùng.
            if (soft.session) session = soft.session;
            searchTabId = soft.tabId;
            pendingCapture = null;
            return { kind: 'capture', capture: soft.capture };
          }
        }

        // Tới được đây là Shein vừa nhận một request của mình rồi mà không ra JSON cần:
        // warmup (trang 1) hoặc cú bấm phân trang (trang 2+). Giãn nhịp trước khi dựng lại
        // document, đừng chồng thêm request khi cái trước đã có dấu hiệu bất thường.
        if (!(await pauseBeforeEscalate())) return { kind: 'cancelled' };

        // Không sang trang được bằng SPA: điều hướng URL pdsearch đúng `page`.
        const nav = await navigateToPage(
          query,
          page,
          page > 1
            ? `Đang mở «${query}» trang ${page} trên Shein…`
            : `Đang mở «${query}» trên Shein…`,
        );
        if (nav === 'cancelled') return { kind: 'cancelled' };
        if (nav === 'risk') return { kind: 'risk' };
        if (nav === 'ok' && captureMatchesPage(pendingCapture, wanted)) {
          const capture = pendingCapture;
          pendingCapture = null;
          return { kind: 'capture', capture };
        }

        // Điều hướng thật cũng không hớt được JSON → giãn nhịp trước khi tự bắn request.
        if (!(await pauseBeforeEscalate())) return { kind: 'cancelled' };

        // Phương án cuối: forge XHR MAIN-world, bám sát param SPA đã ký.
        const api = await fetchReplayPage(page);
        if (api.reason === 'risk') return { kind: 'risk' };
        if (api.reason === 'cancelled') return { kind: 'cancelled' };
        if (api.reason === 'auth') return { kind: 'auth', message: api.message || SHEIN_AUTH_MESSAGE };
        if (api.reason !== 'ok') {
          return { kind: 'error', message: api.message || `Không lấy được «${query}» trang ${page}.` };
        }
        return { kind: 'capture', capture: api.capture };
      };

      /**
       * Hớt được JSON là xong về dữ liệu, nhưng SPA còn đang vẽ list — nghỉ `PAGE_SETTLE_MS`
       * để người dùng thấy trang thật sự đổi trước khi thông báo nhảy sang bước sau.
       */
      const acquirePage = async (page: number): Promise<PageOutcome> => {
        const outcome = await acquirePageJson(page);
        if (outcome.kind === 'capture' && !cancelled && gen === searchGen) {
          await sleep(PAGE_SETTLE_MS);
        }
        return outcome;
      };

      for (let page = 1; page <= MAX_SEARCH_PAGES && !reachedEnd; page++) {
        if (cancelled || gen !== searchGen) break;

        const written = await writeSearchIfCurrent(gen, {
          ...state,
          status: 'searching',
          currentKeyword: query,
          currentPage: page,
          message: `${persistNote(catalogWarning)}Đang lấy «${query}» · trang ${page} (gom ${pool.length}${reportedTotal > 0 ? ` / ${reportedTotal}` : ''})`,
        });
        if (!written) {
          return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
        }
        state = written;

        const outcome = await acquirePage(page);
        if (outcome.kind === 'cancelled' || cancelled || gen !== searchGen) break;
        if (outcome.kind === 'risk') {
          lastIssue = SHEIN_RISK_MESSAGE;
          await persistPool();
          if (pagesFetched === 0 && pool.length === 0) {
            state = await abortRisk(state);
            return { ok: false, message: state.message, state };
          }
          break;
        }
        if (outcome.kind === 'nomore') {
          reachedEnd = true;
          break;
        }
        if (outcome.kind === 'auth') {
          // Hết lượt làm mới — giữ pool đã gom thay vì trả lỗi trắng.
          lastIssue = outcome.message;
          break;
        }
        if (outcome.kind === 'error') {
          if (pagesFetched === 0) {
            lastIssue = outcome.message;
            const failed = await writeSearchIfCurrent(gen, {
              ...state,
              status: 'error',
              message: persistNote(catalogWarning) + lastIssue,
              currentKeyword: undefined,
              currentPage: undefined,
            });
            state = failed ?? (await readProductSearch());
            return { ok: false, message: state.message, state };
          }
          break;
        }

        const capture = outcome.capture;
        pagesFetched += 1;
        if (capture.total > reportedTotal) reportedTotal = capture.total;
        if (capture.facets?.length) {
          const nextAttrs = learnStyleAttrsFromFacets(capture.facets, learned);
          learned.splice(0, learned.length, ...nextAttrs);
          await writeLearnedStyleAttrs(nextAttrs);
        }

        if (capture.products.length === 0) {
          reachedEnd = true;
          const ended = await writeSearchIfCurrent(gen, {
            ...state,
            message: `${persistNote(catalogWarning)}«${query}» hết SP ở trang ${page} · đã gom ${pool.length}${reportedTotal > 0 ? ` / ${reportedTotal}` : ''}`,
          });
          if (ended) state = ended;
          break;
        }

        let added = 0;
        for (const raw of capture.products) {
          const goodsId = String(raw.goodsId || '').trim();
          if (!goodsId || poolSeen.has(goodsId)) continue;
          poolSeen.add(goodsId);
          pool.push(raw);
          added += 1;
        }

        const progressed = await writeSearchIfCurrent(gen, {
          ...state,
          message: `${persistNote(catalogWarning)}«${query}» trang ${page} · +${added} SP · gom ${pool.length}${reportedTotal > 0 ? ` / ${reportedTotal}` : ''}`,
        });
        if (progressed) state = progressed;

        if (reportedTotal > 0 && pool.length >= reportedTotal) {
          reachedEnd = true;
          break;
        }

        const fp = productFingerprint(capture.products);
        if (pagesFetched > 1 && fp && fp === lastFingerprint) {
          reachedEnd = true;
          break;
        }
        lastFingerprint = fp || lastFingerprint;
        if (page < MAX_SEARCH_PAGES) await sleep(searchPageDelayMs(page));
      }

      await persistPool();

      poolTotal += pool.length;
      const afterCatalog = pool
        .filter(
          (raw) =>
            passesClientFilters(raw, payload.catalog) && !seenIds.has(String(raw.goodsId || '')),
        )
        .map((raw) => toSearched(raw, item.keyword, query))
        .filter((p): p is SheinSearchProduct => !!p);
      const ranked = scoreSearchProducts(afterCatalog, {
        keyword: query,
        priceMinUsd: payload.catalog.priceMinUsd,
        priceMaxUsd: payload.catalog.priceMaxUsd,
        mustPhrases: item.mustPhrases,
        shouldPhrases: item.shouldPhrases,
        bodySizeCode: payload.catalog.bodySizeCode,
      }).slice(0, item.limit);
      for (const p of ranked) seenIds.add(p.goodsId);

      const products = [...state.products, ...ranked];
      const rankedState = await writeSearchIfCurrent(gen, {
        ...state,
        products,
        keywordCounts: countByKeyword(products),
        message: `${persistNote(catalogWarning)}«${query}» gom ${pool.length}${reportedTotal > 0 ? ` / ${reportedTotal}` : ''} · sau bộ lọc ${afterCatalog.length} · chọn ${ranked.length}/${item.limit}`,
      });
      if (!rankedState) {
        return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
      }
      state = rankedState;
    }

    if (gen !== searchGen) {
      return { ok: false, message: 'Đã dừng tìm kiếm.', state: await readProductSearch() };
    }

    const found = state.products.length;
    const emptyMsg =
      poolTotal > 0
        ? 'Bộ lọc catalog loại hết sản phẩm đã gom.'
        : 'Không có sản phẩm đủ tiêu chí.';
    const done = await writeSearchIfCurrent(gen, {
      ...state,
      status: cancelled ? 'cancelled' : found > 0 ? 'done' : 'error',
      message: cancelled
        ? 'Đã dừng tìm kiếm.'
        : found > 0
          ? // Shein cắt giữa đường vẫn trả kết quả đã gom, kèm lý do dừng sớm.
            `${persistNote(catalogWarning)}Tìm thấy ${found}/${poolTotal} sản phẩm.${lastIssue ? ` Dừng sớm: ${lastIssue}` : ''}`
          : `${persistNote(catalogWarning)}${lastIssue || emptyMsg}`,
      currentKeyword: undefined,
      currentPage: undefined,
    });
    state = done ?? (await readProductSearch());
    return { ok: found > 0 && !cancelled, message: state.message, state };
  } catch (err) {
    const failed = await writeSearchIfCurrent(gen, {
      ...(await readProductSearch()),
      status: 'error',
      message: (err as Error).message || 'Lỗi tìm sản phẩm.',
    });
    const state = failed ?? (await readProductSearch());
    return { ok: false, message: state.message, state };
  } finally {
    if (searchActiveGen === gen) searchActiveGen = 0;
  }
}

export async function handleRemoveSearchedProduct(
  goodsId: string,
): Promise<{ ok: boolean; state: ProductSearchState }> {
  const id = String(goodsId || '').trim();
  const current = await readProductSearch();
  if (!id) return { ok: false, state: current };
  if (isSearchActive() || current.status === 'searching') {
    return { ok: false, state: current };
  }
  const products = current.products.filter((p) => p.goodsId !== id);
  const state = await writeProductSearch({
    ...current,
    products,
    keywordCounts: countByKeyword(products),
  });
  return { ok: true, state };
}

async function captureMatchingPdp(
  host: ProductSearchHost,
  goodsId: string,
): Promise<PdpCapture | null> {
  const matches = (row: PdpCapture | null) =>
    !!row?.html && extractProductId(row.url, 'shein') === goodsId;
  let captured = await host.captureActiveTab();
  if (matches(captured)) return captured;
  await sleep(800);
  captured = await host.captureActiveTab();
  return matches(captured) ? captured : null;
}

export async function handleAddSearchedProducts(
  host: ProductSearchHost,
  goodsIds: string[],
): Promise<{ ok: boolean; message?: string; state: ProductSearchState }> {
  const current = await readProductSearch();
  if (isSearchActive() || current.status === 'searching') {
    return { ok: false, message: 'Đang tìm sản phẩm — đợi xong rồi thêm.', state: current };
  }
  if (isProductAddActive()) {
    return { ok: false, message: 'Đang thêm sản phẩm (list hoặc Search).', state: current };
  }
  const want = new Set((goodsIds ?? []).map((id) => String(id || '').trim()).filter(Boolean));
  const queue = sortBySearchScore(current.products.filter((p) => want.has(p.goodsId)));
  if (queue.length === 0) {
    return { ok: false, message: 'Chưa chọn sản phẩm nào.', state: current };
  }
  if (!(await host.hasSelectedShops())) {
    return {
      ok: false,
      message: 'Chọn ít nhất 1 shop Active trước khi thêm sản phẩm.',
      state: current,
    };
  }

  addCancelled = false;
  const gen = ++addGen;
  addActiveGen = gen;
  void rememberRecommendedGoodsIds(queue.map((p) => p.goodsId));
  let state =
    (await writeAddIfCurrent(gen, {
      ...current,
      add: {
        status: 'adding',
        total: queue.length,
        success: 0,
        failed: 0,
        message: `Đang thêm 0/${queue.length}`,
      },
    })) ?? current;

  let success = 0;
  let failed = 0;
  let lastFail = '';
  try {
    for (const product of queue) {
      if (addCancelled || gen !== addGen) break;
      const progress = await writeAddIfCurrent(gen, {
        ...state,
        add: {
          status: 'adding',
          total: queue.length,
          success,
          failed,
          currentUrl: product.url,
          message: `Đang thêm ${success + failed + 1}/${queue.length}: ${product.name}`,
        },
      });
      if (!progress) break;
      state = progress;

      if (await host.isProductFullyAdded(product.goodsId)) {
        success += 1;
        continue;
      }

      const opened = await host.openUrl(product.url);
      if (!opened.ok) {
        failed += 1;
        lastFail = opened.message || `Không mở được ${product.name}`;
        continue;
      }

      const tab = await waitForTabGoodsId(host, product.goodsId, ADD_NAV_TIMEOUT_MS);
      if (addCancelled || gen !== addGen) break;
      await sleep(400);

      const captured = await captureMatchingPdp(host, product.goodsId);
      if (!captured?.html) {
        failed += 1;
        lastFail = `Không lấy được trang sản phẩm ${product.goodsId} (SPA / sai PDP).`;
        continue;
      }

      if (tab?.id) await host.beginTabAdding(tab.id, captured.url, 'shein');
      let addRes: AddProductResponse = { ok: false, message: 'Thêm thất bại.' };
      try {
        addRes = await host.addProduct(captured.url, captured.html, captured, 'recommended');
      } finally {
        await host.endTabAdding(tab?.id, addRes.ok ? 'success' : 'failed');
      }
      if (addRes.ok) success += 1;
      else {
        failed += 1;
        lastFail = addRes.message || lastFail;
      }
      await sleep(ADD_PRODUCT_DELAY_MS);
    }

    if (gen !== addGen) {
      return { ok: false, message: 'Đã dừng thêm sản phẩm.', state: await readProductSearch() };
    }

    const stopped = addCancelled;
    const done = await writeAddIfCurrent(gen, {
      ...state,
      add: {
        status: 'done',
        total: queue.length,
        success,
        failed,
        message: stopped
          ? `Đã dừng — thêm ${success}/${queue.length}.`
          : `Đã thêm ${success}/${queue.length}${failed ? ` · lỗi ${failed}` : ''}.${
              lastFail && failed ? ` ${lastFail}` : ''
            }`,
      },
    });
    state = done ?? (await readProductSearch());
    return { ok: success > 0, message: state.add.message, state };
  } catch (err) {
    const failedState = await writeAddIfCurrent(gen, {
      ...(await readProductSearch()),
      add: {
        status: 'done',
        total: queue.length,
        success,
        failed: failed + 1,
        message: (err as Error).message || 'Lỗi thêm sản phẩm.',
      },
    });
    const state = failedState ?? (await readProductSearch());
    return { ok: success > 0, message: state.add.message, state };
  } finally {
    if (addActiveGen === gen) addActiveGen = 0;
  }
}
