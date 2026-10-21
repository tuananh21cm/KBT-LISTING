import { AUTH_REFRESH_ALARM, FS_REVALIDATE_MS, SHOPS_CACHE_TTL_MS, STORAGE_KEYS } from '../shared/config';
import type {
  ActiveTabStatusResponse,
  AddProductResponse,
  DeleteDraftResponse,
  ExtMessage,
  ListDraftsResponse,
  ListTabCountsResponse,
  ListShopsResponse,
  QuickAddListProductResponse,
  QuickAddListStatus,
  StateResponse,
  UploadSizeChartResponse,
  WhatToSellTodayResponse,
  SizeChartStatusResponse,
} from '../shared/messages';
import type {
  DraftItem,
  FourSellerStatus,
  FsShopOption,
  ProductSourceOption,
} from '../shared/types';
import type { SheinSizeGuide } from '../shared/sheinSizeGuide';
import {
  checkFourSellerLogin,
  deleteMyDraftListing,
  getAddedSourceShops,
  getAuth,
  getCachedTheme,
  getMyRecentListings,
  getListingTabCounts,
  getPlatforms,
  getProductSources,
  getScraperShopOptions,
  getSupplierSizeChartImageStatus,
  login,
  logout,
  linkSupplierSizeChartImage,
  maintainAuthSession,
  persistTheme,
  postHtmlFileShop,
  postWhatToSellToday,
  syncThemeFromAccount,
  uploadUserFile,
  type CatalogProfileResult,
  type LocalListingDraft,
  type ScraperShopOption,
} from './kinhbacApi';
import { normalizeTheme } from '../shared/theme';
import { base64ByteLength, base64ToArrayBuffer } from '../shared/base64Transfer';
import { detectProductPage } from '../shared/productPage';
import { buildSheinPdpUrl, isSheinHttpUrl } from '../shared/sheinSearchUrl';
import {
  countAddedAmongSelected,
  extractProductId,
  getAddedStatus,
  getMissingShopIds,
  makeAddedProductKey,
  normalizeAddedProductMarkMeta,
  normalizeAddedProductShopMap,
  reconcileAddedProductShopMap,
  removeShopFromMap,
  unionShopsIntoMap,
  type AddedProductMarkMeta,
  type AddedProductShopMap,
  type AddedStatus,
} from '../shared/productId';
import {
  isRecommendedGoodsId,
  isSearchStateFresh,
  parseRecommendedGoodsEntries,
  type ListingAddedFrom,
} from '../shared/recommendedGoodsIds';
import { findSourceByUrl } from '../shared/domain';
import {
  ADD_NAV_TIMEOUT_MS,
  CONTENT_SCRIPT_READY_MS,
  type ProductSearchCatalog,
} from '../shared/productSearch';
import {
  cancelProductSearch,
  clearProductSearch,
  handleAddSearchedProducts,
  handleListSavedKeywords,
  handleRemoveSearchedProduct,
  handleSearchProducts,
  onSheinSearchCapture,
  onSheinSearchSession,
  recoverStaleProductSearch,
  markProductSearchInterrupted,
  readProductSearch,
  type OpenUrlOpts,
  type ProductSearchHost,
} from './productSearch';
import { configureQuickAddQueue, enqueueQuickAdd } from './quickAddQueue';

interface ShopsCache {
  shops: FsShopOption[];
  fetchedAt: number;
}

/** In-flight add theo tab — đồng bộ ra storage để UI đổi tab không mất status. */
interface AddingTabEntry {
  url: string;
  productKey?: string;
  startedAt: number;
}

const addingByTab = new Map<number, AddingTabEntry>();

interface AddBatchProgress {
  total: number;
  success: number;
  failed: number;
  updatedAt: number;
}

let addBatch: AddBatchProgress = { total: 0, success: 0, failed: 0, updatedAt: 0 };

async function persistAddingByTab(): Promise<void> {
  const payload: Record<string, AddingTabEntry> = {};
  for (const [tabId, entry] of addingByTab) {
    payload[String(tabId)] = entry;
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.addingByTab]: payload });
}

async function persistAddBatchProgress(): Promise<void> {
  addBatch = { ...addBatch, updatedAt: Date.now() };
  await chrome.storage.local.set({
    [STORAGE_KEYS.addBatchProgress]: {
      total: addBatch.total,
      success: addBatch.success,
      failed: addBatch.failed,
      inFlight: addingByTab.size,
      updatedAt: addBatch.updatedAt,
    },
  });
}

async function readAddBatchProgress(): Promise<AddBatchProgress & { inFlight: number }> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.addBatchProgress);
  const stored = raw[STORAGE_KEYS.addBatchProgress] as
    | (Partial<AddBatchProgress> & { inFlight?: number })
    | undefined;
  if (stored && typeof stored.total === 'number') {
    addBatch = {
      total: stored.total,
      success: stored.success ?? 0,
      failed: stored.failed ?? 0,
      updatedAt: stored.updatedAt ?? 0,
    };
  }
  return { ...addBatch, inFlight: addingByTab.size };
}

function snapshotAddProgress(): {
  total: number;
  success: number;
  failed: number;
  inFlight: number;
} {
  return {
    total: addBatch.total,
    success: addBatch.success,
    failed: addBatch.failed,
    inFlight: addingByTab.size,
  };
}

async function isTabAddingAsync(tabId: number): Promise<boolean> {
  if (addingByTab.has(tabId)) return true;
  const raw = await chrome.storage.local.get(STORAGE_KEYS.addingByTab);
  const map = raw[STORAGE_KEYS.addingByTab] as Record<string, AddingTabEntry> | undefined;
  const entry = map?.[String(tabId)];
  if (!entry) return false;
  addingByTab.set(tabId, entry);
  return true;
}

async function beginTabAdding(tabId: number, url: string, flow?: string): Promise<void> {
  const startingFresh = addingByTab.size === 0;
  const productId = flow ? extractProductId(url, flow) : null;
  const productKey =
    flow && productId ? makeAddedProductKey(flow, productId) : undefined;
  addingByTab.set(tabId, { url, productKey, startedAt: Date.now() });
  if (startingFresh) {
    addBatch = { total: 1, success: 0, failed: 0, updatedAt: Date.now() };
  } else {
    addBatch = {
      ...addBatch,
      total: addBatch.total + 1,
      updatedAt: Date.now(),
    };
  }
  await persistAddingByTab();
  await persistAddBatchProgress();
}

async function endTabAdding(
  tabId: number | undefined,
  outcome: 'success' | 'failed' | 'cancelled' = 'cancelled',
): Promise<void> {
  if (tabId === undefined) return;
  if (!addingByTab.delete(tabId)) return;
  if (outcome === 'success') {
    addBatch = { ...addBatch, success: addBatch.success + 1, updatedAt: Date.now() };
  } else if (outcome === 'failed') {
    addBatch = { ...addBatch, failed: addBatch.failed + 1, updatedAt: Date.now() };
  }
  await persistAddingByTab();
  await persistAddBatchProgress();
}
async function readAddedProductShopMap(): Promise<AddedProductShopMap> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.addedProductIds);
  return normalizeAddedProductShopMap(raw[STORAGE_KEYS.addedProductIds]);
}

async function readAddedProductMarkMeta(): Promise<AddedProductMarkMeta> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.addedProductIdsMarkedAt);
  return normalizeAddedProductMarkMeta(raw[STORAGE_KEYS.addedProductIdsMarkedAt]);
}

/** Serialise read-modify-write trên map «đã thêm» khi nhiều tab add song song. */
let addedMapLock: Promise<void> = Promise.resolve();

async function withAddedMapLock<T>(fn: () => Promise<T>): Promise<T> {
  const prev = addedMapLock;
  let release!: () => void;
  addedMapLock = new Promise<void>((resolve) => {
    release = resolve;
  });
  await prev;
  try {
    return await fn();
  } finally {
    release();
  }
}

async function markProductAdded(
  url: string,
  flow: string,
  shopIds: number[],
): Promise<void> {
  const productId = extractProductId(url, flow);
  if (!productId) return;
  const key = makeAddedProductKey(flow, productId);
  await withAddedMapLock(async () => {
    const prev = await readAddedProductShopMap();
    const next = unionShopsIntoMap(prev, key, shopIds);
    const meta = await readAddedProductMarkMeta();
    meta[key] = Date.now();
    await chrome.storage.local.set({
      [STORAGE_KEYS.addedProductIds]: next,
      [STORAGE_KEYS.addedProductIdsMarkedAt]: meta,
    });
  });
}

type SizeChartPendingRow = {
  url?: string;
  tabId?: number;
  at: number;
  /**
   * false = user đóng tab thì thôi (quick-add tab mở tự động).
   * thiếu / true = đóng khi chưa upload thì mở lại (FAB / side panel).
   */
  reopenOnClose?: boolean;
};

type SizeChartPendingMap = Record<string, SizeChartPendingRow>;

async function readSizeChartPendingMap(): Promise<SizeChartPendingMap> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sizeChartPending);
  const map = raw[STORAGE_KEYS.sizeChartPending];
  if (!map || typeof map !== 'object' || Array.isArray(map)) return {};
  return map as SizeChartPendingMap;
}

async function readSizeChartUploadedMap(): Promise<Record<string, number>> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sizeChartUploaded);
  const map = raw[STORAGE_KEYS.sizeChartUploaded];
  if (!map || typeof map !== 'object' || Array.isArray(map)) return {};
  const out: Record<string, number> = {};
  for (const [k, v] of Object.entries(map as Record<string, unknown>)) {
    const at = typeof v === 'number' ? v : Number(v);
    if (k.includes(':') && Number.isFinite(at) && at > 0) out[k] = at;
  }
  return out;
}

/** Đánh dấu SP đã thêm nhưng chưa có Size Chart — content beforeunload. */
async function markSizeChartPending(
  url: string,
  flow: string,
  tabId?: number,
  opts?: { reopenOnClose?: boolean },
): Promise<void> {
  const productId = extractProductId(url, flow);
  if (!productId) return;
  const key = makeAddedProductKey(flow, productId);
  const uploaded = await readSizeChartUploadedMap();
  if (uploaded[key]) return;
  const prev = await readSizeChartPendingMap();
  prev[key] = {
    url,
    ...(tabId != null ? { tabId } : {}),
    at: Date.now(),
    ...(opts?.reopenOnClose === false ? { reopenOnClose: false } : {}),
  };
  await chrome.storage.local.set({ [STORAGE_KEYS.sizeChartPending]: prev });
}

async function clearSizeChartPending(urlOrKey: string): Promise<void> {
  const raw = (urlOrKey || '').trim();
  if (!raw) return;
  const prev = await readSizeChartPendingMap();
  let key = raw.includes(':') && !/^https?:/i.test(raw) ? raw : '';
  if (!key) {
    const sources = await readProductSources();
    const detection = detectProductPage(raw, sources);
    if (detection.flow) {
      const productId = extractProductId(raw, detection.flow);
      if (productId) key = makeAddedProductKey(detection.flow, productId);
    }
  }
  if (!key || !prev[key]) return;
  delete prev[key];
  await chrome.storage.local.set({ [STORAGE_KEYS.sizeChartPending]: prev });
}

/** Ping content PDP: toast + gắn beforeunload (sau khi đã thêm, chưa Size Chart). */
async function notifySizeChartLeaveGuard(
  tabId: number,
  message?: string,
  opts?: { reopenOnClose?: boolean },
): Promise<void> {
  const reopenOnClose = opts?.reopenOnClose !== false;
  const deadline = Date.now() + 20_000;
  const payload = {
    type: 'SIZE_CHART_LEAVE_GUARD' as const,
    reopenOnClose,
    message:
      message ||
      (reopenOnClose
        ? 'Chưa thêm file Size Chart — bấm nút tím để upload. Đóng tab sẽ mở lại.'
        : 'Chưa thêm file Size Chart — bấm nút tím để upload.'),
  };
  while (Date.now() < deadline) {
    try {
      await chrome.tabs.sendMessage(tabId, payload);
      return;
    } catch {
      await sleepMs(250);
    }
  }
}

/**
 * Tab PDP đóng khi còn pending Size Chart.
 * FAB / side panel: mở lại + cảnh báo (Chrome bỏ beforeunload trên tab nền).
 * Quick-add: tab mở tự động — user đóng thì thôi, không mở lại.
 */
async function handleSizeChartPendingTabClosed(tabId: number): Promise<void> {
  const pending = await readSizeChartPendingMap();
  const hit = Object.entries(pending).find(([, row]) => row.tabId === tabId);
  if (!hit) return;
  const [key, info] = hit;
  const uploaded = await readSizeChartUploadedMap();
  if (uploaded[key] || info.reopenOnClose === false) {
    delete pending[key];
    await chrome.storage.local.set({ [STORAGE_KEYS.sizeChartPending]: pending });
    return;
  }
  const url = (info.url || '').trim();
  if (!/^https?:/i.test(url)) return;

  let created: chrome.tabs.Tab;
  try {
    created = await chrome.tabs.create({ url, active: true });
  } catch {
    return;
  }
  if (created.id == null) return;
  pending[key] = { ...info, url, tabId: created.id, at: Date.now() };
  await chrome.storage.local.set({ [STORAGE_KEYS.sizeChartPending]: pending });
  void notifySizeChartLeaveGuard(
    created.id,
    'Chưa thêm file Size Chart — tab đã mở lại. Bấm nút tím để upload rồi mới đóng.',
  );
}

/** Bỏ một shop khỏi product key (sau khi xóa draft). */
async function unmarkProductAdded(
  url: string | undefined,
  fourSellerShopId?: number,
): Promise<void> {
  if (!url || !fourSellerShopId) return;
  const sources = await readProductSources();
  const detection = detectProductPage(url, sources);
  if (!detection.flow) return;
  const productId = extractProductId(url, detection.flow);
  if (!productId) return;
  const key = makeAddedProductKey(detection.flow, productId);
  await withAddedMapLock(async () => {
    const prev = await readAddedProductShopMap();
    const next = removeShopFromMap(prev, key, fourSellerShopId);
    const meta = await readAddedProductMarkMeta();
    if (!next[key]) delete meta[key];
    await chrome.storage.local.set({
      [STORAGE_KEYS.addedProductIds]: next,
      [STORAGE_KEYS.addedProductIdsMarkedAt]: meta,
    });
  });
}

async function resolveProductAddedState(
  url: string,
  flow: string | undefined,
  selectedShopIds: number[],
): Promise<{
  productId?: string;
  productKey?: string;
  addedStatus: AddedStatus;
  alreadyAdded: boolean;
  addedShopHit: number;
  addedShopTotal: number;
}> {
  if (!flow) {
    return {
      addedStatus: 'none',
      alreadyAdded: false,
      addedShopHit: 0,
      addedShopTotal: selectedShopIds.length,
    };
  }
  const productId = extractProductId(url, flow) ?? undefined;
  if (!productId) {
    return {
      addedStatus: 'none',
      alreadyAdded: false,
      addedShopHit: 0,
      addedShopTotal: selectedShopIds.length,
    };
  }
  const productKey = makeAddedProductKey(flow, productId);
  const map = await readAddedProductShopMap();
  const addedStatus = getAddedStatus(productKey, map, selectedShopIds);
  const { hit, total } = countAddedAmongSelected(productKey, map, selectedShopIds);
  return {
    productId,
    productKey,
    addedStatus,
    alreadyAdded: addedStatus === 'full',
    addedShopHit: hit,
    addedShopTotal: total,
  };
}

/** Sync map từ DB mỗi lần mở panel / login (best-effort). */
async function syncAddedProductIdsFromServer(): Promise<void> {
  let sources = await readProductSources();
  if (sources.length === 0) {
    try {
      await refreshMetadata();
      sources = await readProductSources();
    } catch {
      return;
    }
  }
  try {
    const rows = await getAddedSourceShops();
    const map: AddedProductShopMap = {};
    for (const row of rows) {
      const url = (row.sourceUrl || '').trim();
      const shopId = Number(row.fourSellerShopId);
      if (!url || !Number.isFinite(shopId) || shopId <= 0) continue;
      const d = detectProductPage(url, sources);
      if (!d.flow) continue;
      const id = extractProductId(url, d.flow);
      if (!id) continue;
      const key = makeAddedProductKey(d.flow, id);
      const list = map[key] ?? (map[key] = []);
      if (!list.includes(shopId)) list.push(shopId);
    }
    await withAddedMapLock(async () => {
      const local = await readAddedProductShopMap();
      const markedAt = await readAddedProductMarkMeta();
      const inFlightKeys = [...addingByTab.values()]
        .map((e) => e.productKey)
        .filter((k): k is string => !!k);
      const reconciled = reconcileAddedProductShopMap({
        server: map,
        local,
        markedAt,
        inFlightKeys,
      });
      await chrome.storage.local.set({
        [STORAGE_KEYS.addedProductIds]: reconciled.map,
        [STORAGE_KEYS.addedProductIdsMarkedAt]: reconciled.markedAt,
      });
    });
  } catch {
    // Giữ cache cũ khi mạng lỗi.
  }
}

/**
 * Tab đang xem cạnh side panel.
 * Service worker không có "currentWindow" đáng tin — ưu tiên window của SIDE_PANEL,
 * rồi lastFocusedWindow.
 */
async function getActiveHttpTab(): Promise<chrome.tabs.Tab | null> {
  const isHttpTab = (tab: chrome.tabs.Tab | undefined): tab is chrome.tabs.Tab =>
    !!tab?.id && !!tab.url && /^https?:/i.test(tab.url);

  try {
    const contexts = await chrome.runtime.getContexts({
      contextTypes: ['SIDE_PANEL' as chrome.runtime.ContextType],
    });
    const windowIds = [
      ...new Set(
        contexts
          .map((c) => c.windowId)
          .filter((id): id is number => typeof id === 'number'),
      ),
    ];
    if (windowIds.length > 0) {
      // Ưu tiên cửa sổ đang focus (tab user đang xem cạnh side panel).
      const lastFocused = await chrome.windows.getLastFocused().catch(() => null);
      const ordered =
        lastFocused?.id !== undefined && windowIds.includes(lastFocused.id)
          ? [lastFocused.id, ...windowIds.filter((id) => id !== lastFocused.id)]
          : windowIds;
      for (const windowId of ordered) {
        const [tab] = await chrome.tabs.query({ active: true, windowId });
        if (isHttpTab(tab)) return tab;
      }
    }
  } catch {
    // getContexts / SIDE_PANEL có thể không có trên bản cũ.
  }

  const [focused] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  if (isHttpTab(focused)) return focused;

  const actives = await chrome.tabs.query({ active: true });
  return actives.find(isHttpTab) ?? null;
}

/**
 * Điều hướng tab đang xem sang URL mới. Side panel là document riêng nên link
 * thường sẽ nuốt mất chính panel — phải đi qua content/`chrome.tabs.update`.
 * Tab Shein → PDP Shein: `location.assign` để giữ Referer trang hiện tại (pdsearch).
 * pdsearch warmup: `referrer: 'omit'` → `tabs.update` (không Referer, giống dán URL).
 */
async function openUrlInActiveTab(
  url: string,
  opts?: OpenUrlOpts,
): Promise<{ ok: boolean; message?: string; tabId?: number }> {
  const target = (url || '').trim();
  if (!/^https?:/i.test(target)) {
    return { ok: false, message: 'URL không hợp lệ.' };
  }
  try {
    if (opts?.newTab) return openUrlInNewTab(target);
    const tab = await getActiveHttpTab();
    if (tab?.id) {
      const keepReferrer = opts?.referrer !== 'omit';
      if (keepReferrer && isSheinHttpUrl(tab.url) && isSheinHttpUrl(target)) {
        try {
          const res = (await chrome.tabs.sendMessage(tab.id, {
            type: 'NAVIGATE_ASSIGN',
            url: target,
          })) as { ok?: boolean } | undefined;
          if (res?.ok) {
            await chrome.tabs.update(tab.id, { active: true });
            return { ok: true, tabId: tab.id };
          }
        } catch {
          /* content script chưa sẵn — fallback tabs.update */
        }
      }
      await chrome.tabs.update(tab.id, { url: target, active: true });
      return { ok: true, tabId: tab.id };
    }
    const created = await chrome.tabs.create({ url: target });
    return { ok: true, tabId: created.id };
  } catch (err) {
    return { ok: false, message: (err as Error).message || 'Không mở được link.' };
  }
}

/** Mở URL tab mới — side panel không dùng `<a>` thường vì sẽ nuốt chính panel. */
async function openUrlInNewTab(url: string): Promise<{ ok: boolean; message?: string; tabId?: number }> {
  const target = (url || '').trim();
  if (!/^https?:/i.test(target)) {
    return { ok: false, message: 'URL không hợp lệ.' };
  }
  try {
    const tab = await getActiveHttpTab();
    const created = await chrome.tabs.create({
      url: target,
      active: true,
      ...(tab?.windowId != null ? { windowId: tab.windowId } : {}),
      ...(typeof tab?.index === 'number' ? { index: tab.index + 1 } : {}),
    });
    return { ok: true, tabId: created.id };
  } catch (err) {
    return { ok: false, message: (err as Error).message || 'Không mở được tab mới.' };
  }
}

function mapLocalDraft(item: LocalListingDraft, sources: ProductSourceOption[]): DraftItem {
  const source = findSourceByUrl(item.sourceUrl, sources);
  const createTime =
    item.platformCreateTime || item.createdAt || item.platformUpdateTime || item.lastSyncedAt;
  const fsShopId =
    item.fourSellerShopId != null && Number(item.fourSellerShopId) > 0
      ? Number(item.fourSellerShopId)
      : undefined;
  return {
    id: item.id || String(item.fourSellerListingId ?? crypto.randomUUID()),
    name: item.productName?.trim() || '(Không tên)',
    image: item.mainImage,
    shopName: item.shopName || item.shopProfileName,
    platform: item.platform,
    pageType: item.pageType,
    siteStatus: item.siteStatus ?? null,
    sourceUrl: item.sourceUrl,
    fourSellerShopId: fsShopId,
    createTime,
    lowPrice: item.lowPrice ?? null,
    highPrice: item.highPrice ?? null,
    sourceLogoUrl: source?.logoUrl,
    sourceName: source?.name,
    reviewCount: item.reviewCount ?? null,
    averageRating: item.averageRating ?? null,
    storeLogo: item.storeLogo ?? null,
    storeTitle: item.storeTitle ?? null,
    storeUrl: item.storeUrl ?? null,
    shopRank: item.shopRank ?? null,
    storeAttention: item.storeAttention ?? null,
    shippingDays: item.shippingDays ?? null,
    shippingDayPercent: item.shippingDayPercent ?? null,
    sourceStock: item.sourceStock ?? null,
    isLocalProduct: item.isLocalProduct ?? null,
    isSupportQuickShip: item.isSupportQuickShip ?? null,
  };
}

async function listLocalDrafts(
  sources: ProductSourceOption[],
  params: {
    pageSize?: number;
    pageCurrent?: number;
    pageType?: 'draft' | 'published';
    shopIds?: number[];
  } = {},
): Promise<{ drafts: DraftItem[]; total: number; pageCurrent: number; pageSize: number }> {
  const page = await getMyRecentListings({
    pageSize: params.pageSize ?? 20,
    pageCurrent: params.pageCurrent ?? 1,
    pageType: params.pageType,
    fourSellerShopIds: params.shopIds,
  });
  return {
    drafts: (page.items ?? []).map((item) => mapLocalDraft(item, sources)),
    total: page.total ?? 0,
    pageCurrent: page.pageCurrent ?? params.pageCurrent ?? 1,
    pageSize: page.pageSize ?? params.pageSize ?? 20,
  };
}

async function readProductSources(): Promise<ProductSourceOption[]> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.productSources);
  return (raw[STORAGE_KEYS.productSources] as ProductSourceOption[] | undefined) ?? [];
}

async function readFsStatus(): Promise<FourSellerStatus | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.fsStatus);
  return (raw[STORAGE_KEYS.fsStatus] as FourSellerStatus | undefined) ?? null;
}

async function readSelectedShopIds(): Promise<number[]> {
  const raw = await chrome.storage.local.get([
    STORAGE_KEYS.selectedShopIds,
    'kbt_selected_shop_id',
  ]);
  const list = raw[STORAGE_KEYS.selectedShopIds];
  if (Array.isArray(list)) {
    return list
      .map((x) => (typeof x === 'number' ? x : Number(x)))
      .filter((n) => Number.isFinite(n) && n > 0);
  }
  const legacy = raw['kbt_selected_shop_id'];
  if (legacy !== null && legacy !== undefined && legacy !== '') {
    const n = typeof legacy === 'number' ? legacy : Number(legacy);
    if (Number.isFinite(n) && n > 0) return [n];
  }
  return [];
}

async function writeSelectedShopIds(shopIds: number[]): Promise<void> {
  const unique = [...new Set(shopIds.filter((id) => Number.isFinite(id) && id > 0))];
  await chrome.storage.local.set({ [STORAGE_KEYS.selectedShopIds]: unique });
  await chrome.storage.local.remove(['kbt_selected_shop_id', 'kbt_selected_platform']);
}

/**
 * Giữ shop đã chọn còn tồn tại trên list mới.
 * List rỗng (lỗi mạng / FS down) → không ghi [] để khỏi mất selection.
 * Có shops mà chưa chọn → auto chọn shop đầu (Search cần đúng 1 shop để hydrate catalog).
 */
async function resolveShopSelection(shops: FsShopOption[]): Promise<number[]> {
  let selected = await readSelectedShopIds();
  if (shops.length === 0) return selected;
  const validIds = new Set(shops.map((s) => s.id));
  selected = selected.filter((id) => validIds.has(id));
  if (selected.length === 0) {
    selected = [shops[0].id];
  }
  await writeSelectedShopIds(selected);
  return selected;
}

/** Nạp product-sources + platforms. API lỗi → giữ cache, không ghi []. */
async function refreshMetadata(): Promise<void> {
  const [sources, platforms] = await Promise.all([
    getProductSources().catch(() => null),
    getPlatforms().catch(() => null),
  ]);
  const patch: Record<string, unknown> = {};
  if (sources !== null) patch[STORAGE_KEYS.productSources] = sources;
  if (platforms !== null) patch[STORAGE_KEYS.platforms] = platforms;
  if (Object.keys(patch).length) await chrome.storage.local.set(patch);
}

/** Kiểm tra phiên 4Seller qua API hệ thống (cookie không rời server). */
async function refreshFourSellerSession(): Promise<FourSellerStatus> {
  let status: FourSellerStatus;
  try {
    const probe = await checkFourSellerLogin();
    status = {
      hasCookie: probe.hasFourSellerCookie,
      loginStatus: probe.fourSellerCookieLoginStatus,
      isLoggedIn: probe.isLoggedIn,
      message: probe.message,
      checkedAt: new Date().toISOString(),
    };
  } catch (err) {
    status = {
      hasCookie: false,
      loginStatus: 'none',
      isLoggedIn: false,
      message: (err as Error).message,
      checkedAt: new Date().toISOString(),
    };
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.fsStatus]: status });
  return status;
}

async function ensureFourSellerReady(): Promise<FourSellerStatus> {
  const current = await readFsStatus();
  if (current?.isLoggedIn && current.checkedAt) {
    const checkedMs = Date.parse(current.checkedAt);
    if (Number.isFinite(checkedMs) && Date.now() - checkedMs < FS_REVALIDATE_MS) {
      return current;
    }
  }
  return refreshFourSellerSession();
}

function readField(obj: object, ...names: string[]): unknown {
  const rec = obj as Record<string, unknown>;
  for (const name of names) {
    if (rec[name] !== undefined && rec[name] !== null) return rec[name];
  }
  return undefined;
}

function readStr(obj: object, ...names: string[]): string {
  const raw = readField(obj, ...names);
  return raw == null ? '' : String(raw).trim();
}

function readNum(obj: object, ...names: string[]): number {
  const raw = readField(obj, ...names);
  const n = typeof raw === 'number' ? raw : Number(raw);
  return Number.isFinite(n) ? n : NaN;
}

function readStringList(obj: object, ...names: string[]): string[] {
  const raw = readField(obj, ...names);
  if (Array.isArray(raw)) {
    return raw.map((c) => String(c ?? '').trim()).filter(Boolean);
  }
  if (typeof raw === 'string' && raw.trim().startsWith('[')) {
    try {
      const parsed = JSON.parse(raw) as unknown;
      if (Array.isArray(parsed)) {
        return parsed.map((c) => String(c ?? '').trim()).filter(Boolean);
      }
    } catch {
      /* ignore */
    }
  }
  return [];
}

function mapNamedRows(
  raw: unknown,
  idKeys: string[],
  nameKeys: string[],
): { id: string; name: string }[] {
  if (!Array.isArray(raw)) return [];
  return raw
    .map((row) => {
      if (!row || typeof row !== 'object') return null;
      const id = readStr(row, ...idKeys);
      const name = readStr(row, ...nameKeys) || id;
      return id ? { id, name } : null;
    })
    .filter((row): row is { id: string; name: string } => !!row);
}

function mapScraperShop(shop: ScraperShopOption): FsShopOption | null {
  const fsId = readNum(shop, 'fourSellerShopId', 'FourSellerShopId');
  if (!Number.isFinite(fsId) || fsId <= 0) return null;
  const localId = readStr(shop, 'id', 'Id');
  const rawCats = readField(shop, 'productCategories', 'ProductCategories');
  const categories = mapNamedRows(
    readField(shop, 'categories', 'Categories'),
    ['id', 'Id'],
    ['name', 'Name'],
  );
  const ageMin = readNum(shop, 'ageMin', 'AgeMin');
  const ageMax = readNum(shop, 'ageMax', 'AgeMax');
  const priceMinUsd = readNum(shop, 'priceMinUsd', 'PriceMinUsd');
  const priceMaxUsd = readNum(shop, 'priceMaxUsd', 'PriceMaxUsd');
  return {
    id: fsId,
    localId: localId || undefined,
    platform: readStr(shop, 'platformCode', 'PlatformCode'),
    profileName: readStr(shop, 'profileName', 'ProfileName') || undefined,
    shopName: readStr(shop, 'name', 'Name') || undefined,
    marketplaceId: readStr(shop, 'region', 'Region') || undefined,
    platformAvatarUrl: readStr(shop, 'platformAvatarUrl', 'PlatformAvatarUrl') || undefined,
    productCategories: Array.isArray(rawCats)
      ? rawCats
          .map((c) => {
            if (!c || typeof c !== 'object') return null;
            const label = readStr(c, 'label', 'Label');
            const value = readStr(c, 'value', 'Value');
            return label || value ? { label, value } : null;
          })
          .filter((c): c is { label: string; value: string } => !!c)
      : [],
    categoryId: readStr(shop, 'categoryId', 'CategoryId') || categories[0]?.id || undefined,
    categoryName:
      readStr(shop, 'categoryName', 'CategoryName') ||
      (categories.length ? categories.map((c) => c.name).join(' · ') : undefined),
    categories: categories.length ? categories : undefined,
    ageMin: Number.isFinite(ageMin) ? ageMin : null,
    ageMax: Number.isFinite(ageMax) ? ageMax : null,
    priceMinUsd: Number.isFinite(priceMinUsd) ? priceMinUsd : null,
    priceMaxUsd: Number.isFinite(priceMaxUsd) ? priceMaxUsd : null,
    preferQuickShip: !!readField(shop, 'preferQuickShip', 'PreferQuickShip'),
    preferLocal: !!readField(shop, 'preferLocal', 'PreferLocal'),
    preferUsWarehouse: !!readField(shop, 'preferUsWarehouse', 'PreferUsWarehouse'),
    preferSheinClub: !!readField(shop, 'preferSheinClub', 'PreferSheinClub'),
    minRating: readNum(shop, 'minRating', 'MinRating') || 0,
    minSoldCount: readNum(shop, 'minSoldCount', 'MinSoldCount') || 0,
    maxVariantCount: readNum(shop, 'maxVariantCount', 'MaxVariantCount') || 0,
    bodySizeCode: readStr(shop, 'bodySizeCode', 'BodySizeCode') || null,
    styleCodes: readStringList(shop, 'styleCodes', 'StyleCodes'),
    activeCount: readNum(shop, 'activeCount', 'ActiveCount') || 0,
  };
}

async function readShopsCache(): Promise<ShopsCache | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.shopsCache);
  const cache = raw[STORAGE_KEYS.shopsCache] as ShopsCache | undefined;
  if (!cache || !Array.isArray(cache.shops) || typeof cache.fetchedAt !== 'number') return null;
  return cache;
}

async function writeShopsCache(shops: FsShopOption[]): Promise<void> {
  await chrome.storage.local.set({
    [STORAGE_KEYS.shopsCache]: { shops, fetchedAt: Date.now() } satisfies ShopsCache,
  });
}

async function clearShopsCache(): Promise<void> {
  await chrome.storage.local.remove(STORAGE_KEYS.shopsCache);
}

/**
 * Shop/catalog lấy từ API kinhbac — không phụ thuộc phiên 4Seller.
 * Cache trong TTL thì dùng luôn; hết TTL hoặc `force` thì fetch; lỗi mạng giữ cache (kể cả quá TTL).
 */
async function loadShopsIfReady(force = false): Promise<FsShopOption[]> {
  const cache = await readShopsCache();
  if (!force && cache && Date.now() - cache.fetchedAt < SHOPS_CACHE_TTL_MS) {
    return cache.shops;
  }
  try {
    const rows = await getScraperShopOptions();
    const shops = rows.map(mapScraperShop).filter((s): s is FsShopOption => !!s);
    await writeShopsCache(shops);
    return shops;
  } catch (err) {
    if (cache) return cache.shops;
    if (force) throw err;
    return [];
  }
}

async function snapshotState(
  auth: Awaited<ReturnType<typeof getAuth>>,
  shops: FsShopOption[],
  theme: Awaited<ReturnType<typeof getCachedTheme>>,
  selectedShopIds: number[],
): Promise<StateResponse> {
  return {
    authenticated: !!auth,
    user: auth?.user ?? null,
    theme,
    fourSeller: await readFsStatus(),
    productSources: await readProductSources(),
    shops,
    selectedShopIds,
  };
}

/** Login = force refresh song song. Mở panel = cache-first, network khi thiếu / hết TTL. */
async function bootstrapSession(mode: 'login' | 'panel'): Promise<StateResponse> {
  const auth = await getAuth();
  let theme = await getCachedTheme();
  if (!auth) {
    return snapshotState(null, [], theme, []);
  }

  let shops: FsShopOption[] = [];
  if (mode === 'login') {
    await clearShopsCache();
    const loaded = await Promise.all([
      refreshMetadata(),
      refreshFourSellerSession(),
      loadShopsIfReady(true).catch(() => [] as FsShopOption[]),
      syncAddedProductIdsFromServer(),
    ]);
    shops = loaded[2];
    theme = await getCachedTheme();
  } else {
    const needSources = (await readProductSources()).length === 0;
    const [themeNext, loadedShops] = await Promise.all([
      syncThemeFromAccount(),
      loadShopsIfReady(false),
      ensureFourSellerReady(),
      needSources ? refreshMetadata() : Promise.resolve(),
      syncAddedProductIdsFromServer(),
    ]);
    theme = themeNext;
    shops = loadedShops;
  }

  const selectedShopIds = await resolveShopSelection(shops);
  return snapshotState(auth, shops, theme, selectedShopIds);
}

type PdpMetrics = {
  platform?: string | null;
  reviewCount?: number | null;
  averageRating?: number | null;
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
  sizeGuide?: SheinSizeGuide | null;
};

async function resolveListingAddedFrom(url: string, flow?: string): Promise<ListingAddedFrom> {
  const goodsId =
    (flow ? extractProductId(url, flow) : null) || extractProductId(url, 'shein');
  if (!goodsId) return 'manual';
  const search = await readProductSearch();
  const searchIds = isSearchStateFresh(search.updatedAt)
    ? search.products.map((p) => p.goodsId)
    : [];
  const raw = await chrome.storage.local.get(STORAGE_KEYS.recommendedGoodsIds);
  const remembered = parseRecommendedGoodsEntries(raw[STORAGE_KEYS.recommendedGoodsIds]).map(
    (row) => row.id,
  );
  if (isRecommendedGoodsId(goodsId, searchIds, remembered)) {
    return 'recommended';
  }
  return 'manual';
}

async function handleAddProduct(
  url: string,
  html: string,
  pdp?: PdpMetrics,
  addedFrom?: ListingAddedFrom,
): Promise<AddProductResponse> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập KBT Craper.' };
  if (!html || html.length < 200) {
    return { ok: false, message: 'Không lấy được nội dung trang sản phẩm.' };
  }

  const fs = await ensureFourSellerReady();
  if (!fs.isLoggedIn) {
    return { ok: false, message: fs.message || 'Phiên 4Seller không hợp lệ.' };
  }

  const sources = await readProductSources();
  const detection = detectProductPage(url, sources);
  if (!detection.ok) {
    return {
      ok: false,
      message: detection.reason || 'URL hiện tại không phải trang sản phẩm nguồn.',
    };
  }

  const shops = await loadShopsIfReady();
  const selectedShopIds = await resolveShopSelection(shops);
  const selected = shops.filter((s) => selectedShopIds.includes(s.id));
  if (selected.length === 0) {
    return {
      ok: false,
      message: 'Chọn ít nhất 1 shop Active trước khi thêm sản phẩm.',
    };
  }

  const map = await readAddedProductShopMap();
  const productId = detection.flow
    ? extractProductId(url, detection.flow)
    : null;
  const productKey =
    detection.flow && productId
      ? makeAddedProductKey(detection.flow, productId)
      : undefined;
  const missingIds = getMissingShopIds(productKey, map, selected.map((s) => s.id));
  if (missingIds.length === 0) {
    return {
      ok: true,
      message: 'Sản phẩm này đã được thêm cho mọi shop đang chọn.',
      shopCount: selected.length,
      shopName: selected.map((s) => formatShopOptionLabel(s)).join(', '),
      url,
    };
  }

  const targets = selected.filter((s) => missingIds.includes(s.id));
  const siteList = [
    ...new Set(
      targets
        .map((s) => (s.marketplaceId || '').trim())
        .filter(Boolean),
    ),
  ];
  const site = siteList[0] ?? '';
  const origin = addedFrom ?? (await resolveListingAddedFrom(url, detection.flow));

  let result: { ok: boolean; message: string };
  try {
    result = await postHtmlFileShop({
      url,
      html,
      shopIdList: targets.map((s) => s.id),
      site,
      siteList,
      reviewCount: pdp?.reviewCount ?? undefined,
      averageRating: pdp?.averageRating ?? undefined,
      platform: pdp?.platform ?? detection.source?.code ?? undefined,
      stock: pdp?.stock ?? undefined,
      storeCode: pdp?.storeCode ?? undefined,
      storeLogo: pdp?.storeLogo ?? undefined,
      storeTitle: pdp?.storeTitle ?? undefined,
      soldByTips: pdp?.soldByTips ?? undefined,
      shipsFromTips: pdp?.shipsFromTips ?? undefined,
      shopRank: pdp?.shopRank ?? undefined,
      storeAttention: pdp?.storeAttention ?? undefined,
      shippingDays: pdp?.shippingDays ?? undefined,
      shippingDayPercent: pdp?.shippingDayPercent ?? undefined,
      shippingDayPercentDesc: pdp?.shippingDayPercentDesc ?? undefined,
      isLocalProduct: pdp?.isLocalProduct ?? undefined,
      isSupportQuickShip: pdp?.isSupportQuickShip ?? undefined,
      addedFrom: origin,
      sizeGuideJson: pdp?.sizeGuide ?? undefined,
    });
  } catch (err) {
    result = { ok: false, message: (err as Error).message || 'Không đẩy được sản phẩm.' };
  }

  if (result.ok) {
    if (detection.flow) {
      await markProductAdded(
        url,
        detection.flow,
        targets.map((s) => s.id),
      );
    }
    await chrome.storage.local.set({ [STORAGE_KEYS.draftsDirtyAt]: Date.now() });
  }
  const shopLabel = targets.map((s) => formatShopOptionLabel(s)).join(', ');
  const sourceLabel = detection.source?.name;
  const parts = [result.message];
  if (sourceLabel) parts.push(sourceLabel);
  if (shopLabel) parts.push(`${targets.length} shop: ${shopLabel}`);
  return {
    ok: result.ok,
    message: result.ok && parts.length > 1 ? `${result.message} (${parts.slice(1).join(' · ')})` : result.message,
    shopCount: targets.length,
    shopName: shopLabel,
    url,
  };
}

function productCategoryLabels(s: FsShopOption): string[] {
  return (s.productCategories ?? [])
    .map((c) => (typeof c === 'string' ? c : c.label)?.trim())
    .filter((t): t is string => !!t);
}

function formatShopOptionLabel(s: FsShopOption): string {
  const profile = s.profileName?.trim() || '';
  const name = s.shopName?.trim() || `Shop #${s.id}`;
  const label = profile && name ? `${profile} · ${name}` : profile || name;
  const cats = productCategoryLabels(s);
  const act = s.activeCount ?? 0;
  const parts = [label];
  if (cats.length > 0) parts.push(cats.join(' · '));
  parts.push(`${act} active`);
  return parts.join(' · ');
}

/** Chờ chu kỳ loading → complete sau khi gọi reload (không tin status `complete` cũ). */
function waitTabReloadComplete(tabId: number, timeoutMs = 45000): Promise<boolean> {
  return new Promise((resolve) => {
    let sawLoading = false;
    let settled = false;
    const finish = (ok: boolean) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      chrome.tabs.onUpdated.removeListener(onUpdated);
      resolve(ok);
    };

    const timer = setTimeout(() => finish(false), timeoutMs);

    function onUpdated(id: number, info: chrome.tabs.TabChangeInfo) {
      if (id !== tabId) return;
      if (info.status === 'loading') sawLoading = true;
      if (info.status === 'complete' && sawLoading) finish(true);
    }

    chrome.tabs.onUpdated.addListener(onUpdated);
  });
}

async function resolveHttpTab(preferredTabId?: number): Promise<chrome.tabs.Tab | null> {
  const isHttpTab = (tab: chrome.tabs.Tab | undefined): tab is chrome.tabs.Tab =>
    !!tab?.id && !!tab.url && /^https?:/i.test(tab.url);

  if (preferredTabId !== undefined) {
    try {
      const tab = await chrome.tabs.get(preferredTabId);
      if (isHttpTab(tab)) return tab;
    } catch {
      // Tab đã đóng — fallback active.
    }
  }
  return getActiveHttpTab();
}

async function readContentTabStatus(
  tabId: number,
): Promise<{
  href: string;
  pageReady: boolean;
  sessionReady: boolean;
  shopsSelected: boolean;
  alreadyAdded: boolean;
  addedStatus: AddedStatus;
  isAdding: boolean;
  isProductPage: boolean;
  at: number;
} | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.contentTabStatus);
  const map = raw[STORAGE_KEYS.contentTabStatus] as
    | Record<
        string,
        {
          href?: string;
          pageReady?: boolean;
          sessionReady?: boolean;
          shopsSelected?: boolean;
          alreadyAdded?: boolean;
          addedStatus?: AddedStatus;
          isAdding?: boolean;
          isProductPage?: boolean;
          at?: number;
        }
      >
    | undefined;
  const entry = map?.[String(tabId)];
  if (!entry?.href) return null;
  // Snapshot quá cũ (>2 phút) bỏ — tránh sticky sau khi content chết.
  if (typeof entry.at === 'number' && Date.now() - entry.at > 120_000) return null;
  const addedStatus: AddedStatus =
    entry.addedStatus === 'partial' || entry.addedStatus === 'full' || entry.addedStatus === 'none'
      ? entry.addedStatus
      : entry.alreadyAdded
        ? 'full'
        : 'none';
  return {
    href: entry.href,
    pageReady: !!entry.pageReady,
    sessionReady: !!entry.sessionReady,
    shopsSelected: !!entry.shopsSelected,
    alreadyAdded: addedStatus === 'full',
    addedStatus,
    isAdding: !!entry.isAdding,
    isProductPage: !!entry.isProductPage,
    at: entry.at ?? 0,
  };
}

function samePageUrl(a?: string | null, b?: string | null): boolean {
  if (!a || !b) return false;
  try {
    const ua = new URL(a);
    const ub = new URL(b);
    return (
      ua.origin === ub.origin &&
      ua.pathname === ub.pathname &&
      ua.search === ub.search
    );
  } catch {
    return a === b;
  }
}

async function getActiveTabStatus(): Promise<ActiveTabStatusResponse> {
  const tab = await getActiveHttpTab();
  if (!tab?.id) {
    return {
      ok: false,
      ready: false,
      isProductPage: false,
      message: 'Không có tab http(s) đang active.',
    };
  }

  const loaded = tab.status === 'complete';
  const sources = await readProductSources();
  const detection = detectProductPage(tab.url, sources);
  const selectedShopIds = await readSelectedShopIds();
  const added =
    detection.ok && tab.url
      ? await resolveProductAddedState(tab.url, detection.flow, selectedShopIds)
      : {
          addedStatus: 'none' as const,
          alreadyAdded: false,
          addedShopHit: 0,
          addedShopTotal: selectedShopIds.length,
        };

  const content = await readContentTabStatus(tab.id);
  const contentAligned = !!(content && samePageUrl(content.href, tab.url));
  const pageReady = contentAligned ? content!.pageReady : loaded;

  // Ưu tiên status từ BG map; nếu content cùng URL báo partial/full thì merge “cao hơn”.
  let addedStatus: AddedStatus = added.addedStatus;
  if (contentAligned) {
    const c = content!.addedStatus;
    if (c === 'full' || (c === 'partial' && addedStatus === 'none')) {
      addedStatus = c;
    }
  }
  const alreadyAdded = addedStatus === 'full';
  const adding =
    (await isTabAddingAsync(tab.id)) || (contentAligned && content!.isAdding);

  await readAddBatchProgress();
  const addProgress = snapshotAddProgress();

  const base = {
    tabId: tab.id,
    status: pageReady ? 'complete' : tab.status === 'complete' ? 'loading' : tab.status,
    url: tab.url,
    sourceName: detection.source?.name,
    flow: detection.flow,
    productId: added.productId,
    productKey: added.productKey,
    adding,
    pageReady,
    addProgress,
    addedStatus,
    alreadyAdded,
    addedShopHit: added.addedShopHit,
    addedShopTotal: added.addedShopTotal,
  };

  if (adding) {
    return {
      ok: true,
      ready: false,
      isProductPage: detection.ok,
      message: 'Đang thêm sản phẩm...',
      ...base,
    };
  }

  if (!pageReady) {
    return {
      ok: true,
      ready: false,
      isProductPage: detection.ok,
      message: 'Đang chờ trang tải xong...',
      ...base,
    };
  }

  if (!detection.ok) {
    return {
      ok: true,
      ready: false,
      isProductPage: false,
      message: detection.reason || 'Không nhận diện được trang sản phẩm.',
      ...base,
      alreadyAdded: false,
      addedStatus: 'none',
      productId: undefined,
      productKey: undefined,
      addedShopHit: 0,
    };
  }

  if (addedStatus === 'full') {
    return {
      ok: true,
      ready: false,
      isProductPage: true,
      message: 'Sản phẩm này đã được thêm cho mọi shop đang chọn.',
      ...base,
    };
  }

  if (addedStatus === 'partial') {
    return {
      ok: true,
      ready: true,
      isProductPage: true,
      message: `Đã có ở ${added.addedShopHit}/${added.addedShopTotal} shop — bấm để thêm shop còn thiếu.`,
      ...base,
    };
  }

  return {
    ok: true,
    ready: true,
    isProductPage: true,
    message: undefined,
    ...base,
  };
}

async function reloadActiveTab(preferredTabId?: number): Promise<ActiveTabStatusResponse> {
  const tab = await resolveHttpTab(preferredTabId);
  if (!tab?.id) {
    return {
      ok: false,
      ready: false,
      message: 'Không có tab http(s) để tải lại.',
    };
  }
  // Listener trước reload — tránh miss loading/complete.
  const loadedPromise = waitTabReloadComplete(tab.id);
  try {
    await chrome.tabs.reload(tab.id);
  } catch (err) {
    return { ok: false, ready: false, message: (err as Error).message, tabId: tab.id };
  }
  const loaded = await loadedPromise;
  if (!loaded) {
    return {
      ok: false,
      ready: false,
      tabId: tab.id,
      url: tab.url,
      message: 'Trang tải quá lâu — thử lại.',
    };
  }
  // Chờ content script settle ngắn rồi trả trạng thái sản phẩm thật.
  await new Promise((r) => setTimeout(r, 500));
  // Reconcile «đã thêm» với DB (xóa listing ngoài extension → bỏ badge/nút stale).
  await syncAddedProductIdsFromServer();
  const status = await getActiveTabStatus();
  return {
    ...status,
    ok: true,
    tabId: status.tabId ?? tab.id,
    message: status.ready
      ? 'Đã tải lại — trang sản phẩm sẵn sàng.'
      : status.message || 'Đã tải lại trang. Chưa nhận diện được trang sản phẩm.',
  };
}

async function captureActiveTab(): Promise<
  ({ url: string; html: string } & PdpMetrics) | null
> {
  const tab = await getActiveHttpTab();
  if (!tab?.id || !tab.url) return null;
  return captureTab(tab.id);
}

async function captureTab(tabId: number): Promise<({ url: string; html: string } & PdpMetrics) | null> {
  try {
    const res = (await chrome.tabs.sendMessage(tabId, { type: 'CAPTURE_HTML' })) as
      | ({ url: string; html: string } & PdpMetrics)
      | undefined;
    if (res?.html) return res;
  } catch {
    // Content script chưa nạp.
  }
  return null;
}

function sleepMs(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForTabContentScript(tabId: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      await chrome.tabs.sendMessage(tabId, { type: 'GET_SHEIN_SEARCH_SESSION' });
      return true;
    } catch {
      await sleepMs(150);
    }
  }
  return false;
}

async function waitForTabGoodsIdReady(
  tabId: number,
  goodsId: string,
  timeoutMs: number,
): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const tab = await chrome.tabs.get(tabId).catch(() => null);
    if (!tab) return false;
    const url = tab.url || '';
    if (tab.status === 'complete' && extractProductId(url, 'shein') === goodsId) {
      return waitForTabContentScript(tabId, Math.min(CONTENT_SCRIPT_READY_MS, deadline - Date.now()));
    }
    await sleepMs(250);
  }
  return false;
}

async function captureMatchingPdpTab(
  tabId: number,
  goodsId: string,
): Promise<({ url: string; html: string } & PdpMetrics) | null> {
  const matches = (row: ({ url: string; html: string } & PdpMetrics) | null) =>
    !!row?.html && extractProductId(row.url, 'shein') === goodsId;
  let captured = await captureTab(tabId);
  if (matches(captured)) return captured;
  await sleepMs(800);
  captured = await captureTab(tabId);
  return matches(captured) ? captured : null;
}

async function notifyQuickAddListStatus(
  listTabId: number | undefined,
  goodsId: string,
  status: QuickAddListStatus,
  message?: string,
): Promise<void> {
  if (listTabId == null) return;
  try {
    await chrome.tabs.sendMessage(listTabId, {
      type: 'QUICK_ADD_LIST_STATUS',
      goodsId,
      status,
      message,
    });
  } catch {
    /* tab list đã đóng / content chưa sẵn */
  }
}

function resolveQuickAddPdpUrl(goodsId: string, urlHint?: string): string {
  const hint = (urlHint || '').trim();
  if (hint) {
    try {
      const abs = new URL(hint);
      if (/^https?:$/i.test(abs.protocol) && extractProductId(abs.href, 'shein') === goodsId) {
        return abs.href;
      }
    } catch {
      /* fall through */
    }
    try {
      const abs = new URL(hint, 'https://us.shein.com/');
      if (extractProductId(abs.href, 'shein') === goodsId) return abs.href;
    } catch {
      /* fall through */
    }
  }
  return buildSheinPdpUrl(goodsId);
}

async function runQuickAddJob(args: {
  goodsId: string;
  pdpUrl: string;
  listTabId?: number;
  onPdpTab: (tabId: number) => void;
}): Promise<QuickAddListProductResponse> {
  const { goodsId, pdpUrl, listTabId, onPdpTab } = args;
  let pdpTabId: number | undefined;
  let addSucceeded = false;
  try {
    const opener = listTabId != null ? await chrome.tabs.get(listTabId).catch(() => null) : null;
    const created = await chrome.tabs.create({
      url: pdpUrl,
      active: false,
      ...(opener?.windowId != null ? { windowId: opener.windowId } : {}),
      ...(typeof opener?.index === 'number' ? { index: opener.index + 1 } : {}),
    });
    pdpTabId = created.id;
    if (pdpTabId == null) {
      return { ok: false, message: 'Không mở được tab sản phẩm.', goodsId };
    }
    onPdpTab(pdpTabId);

    const ready = await waitForTabGoodsIdReady(pdpTabId, goodsId, ADD_NAV_TIMEOUT_MS);
    if (!ready) {
      return { ok: false, message: `Không tải được trang sản phẩm ${goodsId}.`, goodsId };
    }

    await sleepMs(400);
    const captured = await captureMatchingPdpTab(pdpTabId, goodsId);
    if (!captured?.html) {
      return { ok: false, message: `Không lấy được HTML sản phẩm ${goodsId}.`, goodsId };
    }

    await beginTabAdding(pdpTabId, captured.url, 'shein');
    let addRes: AddProductResponse = { ok: false, message: 'Thêm thất bại.' };
    try {
      addRes = await handleAddProduct(captured.url, captured.html, captured);
    } finally {
      await endTabAdding(pdpTabId, addRes.ok ? 'success' : 'failed');
    }
    addSucceeded = addRes.ok;
    if (addRes.ok) {
      // Tab mở tự động: giữ để upload Size Chart nếu muốn, nhưng đóng thì không mở lại.
      await markSizeChartPending(captured.url, 'shein', pdpTabId, { reopenOnClose: false });
      void notifySizeChartLeaveGuard(pdpTabId, undefined, { reopenOnClose: false });
    }
    return { ok: addRes.ok, message: addRes.message, goodsId };
  } finally {
    // Success: giữ tab để user upload Size Chart. Fail: đóng tab dọn rác.
    if (pdpTabId != null && !addSucceeded) {
      try {
        await chrome.tabs.remove(pdpTabId);
      } catch {
        /* tab đã đóng */
      }
    }
  }
}

function ensureQuickAddQueueConfigured(): void {
  configureQuickAddQueue({
    hasAuth: async () => !!(await getAuth()),
    hasSelectedShops: hasSelectedShopsForSearch,
    isProductFullyAdded: isProductFullyAddedForSearch,
    resolvePdpUrl: resolveQuickAddPdpUrl,
    runJob: runQuickAddJob,
    notifyStatus: notifyQuickAddListStatus,
  });
}

async function handleQuickAddListProduct(
  goodsIdRaw: string,
  urlHint: string | undefined,
  listTabId: number | undefined,
): Promise<QuickAddListProductResponse> {
  ensureQuickAddQueueConfigured();
  return enqueueQuickAdd(goodsIdRaw, urlHint, listTabId);
}

async function patchShopCatalogFromSearch(
  shopFsId: number,
  patch: CatalogProfileResult | ProductSearchCatalog,
): Promise<void> {
  const cache = await readShopsCache();
  if (!cache) return;
  const shops = cache.shops.map((s) => {
    if (s.id !== shopFsId) return s;
    return {
      ...s,
      categories: patch.categories ?? s.categories,
      categoryId: patch.categoryId ?? s.categoryId,
      categoryName: patch.categoryName ?? s.categoryName,
      ageMin: patch.ageMin ?? s.ageMin,
      ageMax: patch.ageMax ?? s.ageMax,
      priceMinUsd: patch.priceMinUsd ?? s.priceMinUsd,
      priceMaxUsd: patch.priceMaxUsd ?? s.priceMaxUsd,
      preferQuickShip: patch.preferQuickShip !== undefined ? !!patch.preferQuickShip : s.preferQuickShip,
      preferLocal: patch.preferLocal !== undefined ? !!patch.preferLocal : s.preferLocal,
      preferUsWarehouse:
        patch.preferUsWarehouse !== undefined ? !!patch.preferUsWarehouse : s.preferUsWarehouse,
      preferSheinClub: patch.preferSheinClub !== undefined ? !!patch.preferSheinClub : s.preferSheinClub,
      minRating: patch.minRating ?? s.minRating,
      minSoldCount: patch.minSoldCount ?? s.minSoldCount,
      maxVariantCount: patch.maxVariantCount ?? s.maxVariantCount,
      bodySizeCode: patch.bodySizeCode ?? s.bodySizeCode,
      styleCodes: Array.isArray(patch.styleCodes) ? patch.styleCodes : s.styleCodes,
    };
  });
  await writeShopsCache(shops);
}

async function hasSelectedShopsForSearch(): Promise<boolean> {
  const shops = await loadShopsIfReady();
  const selected = await resolveShopSelection(shops);
  return selected.length > 0;
}

async function isProductFullyAddedForSearch(goodsId: string): Promise<boolean> {
  const id = String(goodsId || '').trim();
  if (!id) return false;
  const shops = await loadShopsIfReady();
  const selected = await resolveShopSelection(shops);
  if (selected.length === 0) return false;
  const map = await readAddedProductShopMap();
  return getMissingShopIds(makeAddedProductKey('shein', id), map, selected).length === 0;
}

function productSearchHost(): ProductSearchHost {
  return {
    openUrl: openUrlInActiveTab,
    getActiveTab: getActiveHttpTab,
    resetSheinSearchSession: async (tabId) => {
      try {
        await chrome.tabs.sendMessage(tabId, { type: 'RESET_SHEIN_SEARCH_SESSION' });
      } catch {
        /* content chưa nạp */
      }
    },
    gotoSheinSearchPage: async (tabId, page, query) => {
      try {
        const res = (await chrome.tabs.sendMessage(tabId, {
          type: 'SHEIN_SEARCH_GOTO_PAGE',
          page,
          query,
        })) as { ok?: boolean } | undefined;
        return !!res?.ok;
      } catch {
        return false;
      }
    },
    captureActiveTab,
    addProduct: (url, html, pdp, addedFrom) => handleAddProduct(url, html, pdp, addedFrom),
    beginTabAdding,
    endTabAdding,
    loadShops: loadShopsIfReady,
    patchShopCatalog: patchShopCatalogFromSearch,
    hasSelectedShops: hasSelectedShopsForSearch,
    isProductFullyAdded: isProductFullyAddedForSearch,
  };
}

async function handleListDrafts(
  pageType?: 'draft' | 'published',
  shopIds?: number[],
  pageCurrent = 1,
  pageSize = 20,
): Promise<ListDraftsResponse> {
  const empty = {
    drafts: [] as DraftItem[],
    total: 0,
    pageCurrent: pageCurrent < 1 ? 1 : pageCurrent,
    pageSize: pageSize < 1 ? 20 : pageSize,
  };
  const auth = await getAuth();
  if (!auth) {
    return { ok: false, message: 'Chưa đăng nhập KBT Craper.', ...empty };
  }
  try {
    // «Làm mới» phải đồng bộ map đã thêm — không chỉ fetch list.
    await syncAddedProductIdsFromServer();
    const sources = await readProductSources();
    const page = await listLocalDrafts(sources, { pageType, shopIds, pageCurrent, pageSize });
    return { ok: true, ...page };
  } catch (err) {
    return {
      ok: false,
      message: (err as Error).message || 'Không tải được listing local.',
      ...empty,
    };
  }
}

async function handleListTabCounts(shopIds?: number[]): Promise<ListTabCountsResponse> {
  const empty = { draftTotal: 0, publishLimitPublished: 0, publishLimitTotal: 0 };
  const auth = await getAuth();
  if (!auth) {
    return { ok: false, message: 'Chưa đăng nhập KBT Craper.', ...empty };
  }
  const ids = (shopIds ?? []).filter((id) => Number.isFinite(id) && id > 0);
  if (ids.length === 0) {
    return { ok: true, ...empty };
  }
  try {
    const counts = await getListingTabCounts(ids);
    return {
      ok: true,
      draftTotal: counts.draftTotal ?? 0,
      publishLimitPublished: counts.publishLimitPublished ?? 0,
      publishLimitTotal: counts.publishLimitTotal ?? 0,
    };
  } catch (err) {
    return {
      ok: false,
      message: (err as Error).message || 'Không tải được số listing.',
      ...empty,
    };
  }
}

async function handleDeleteDraft(
  id: string,
  sourceUrl?: string,
  fourSellerShopId?: number,
  pageType?: string,
): Promise<DeleteDraftResponse> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập KBT Craper.' };
  if ((pageType || '').toLowerCase() === 'published') {
    return { ok: false, message: 'Không xóa listing đã publish từ extension.' };
  }
  const listingId = (id || '').trim();
  if (!listingId) return { ok: false, message: 'Thiếu id draft.' };
  try {
    await deleteMyDraftListing(listingId);
    await unmarkProductAdded(sourceUrl, fourSellerShopId);
    // Fallback: unmark no-op khi thiếu sourceUrl/shopId → sync DB làm nguồn đúng.
    await syncAddedProductIdsFromServer();
    return { ok: true, message: 'Đã xóa draft.' };
  } catch (err) {
    return {
      ok: false,
      message: (err as Error).message || 'Không xóa được draft.',
    };
  }
}

async function handleListShops(): Promise<ListShopsResponse> {
  const auth = await getAuth();
  if (!auth) {
    return {
      ok: false,
      message: 'Chưa đăng nhập KBT Craper.',
      shops: [],
      selectedShopIds: [],
    };
  }
  try {
    const shops = await loadShopsIfReady(true);
    const selectedShopIds = await resolveShopSelection(shops);
    return { ok: true, shops, selectedShopIds };
  } catch (err) {
    const cache = await readShopsCache();
    const shops = cache?.shops ?? [];
    const selectedShopIds = await resolveShopSelection(shops);
    return {
      ok: false,
      message: (err as Error).message || 'Không tải được danh sách shop.',
      shops,
      selectedShopIds,
    };
  }
}

async function handleWhatToSellToday(shopLocalId: string): Promise<WhatToSellTodayResponse> {
  const auth = await getAuth();
  if (!auth) {
    return { ok: false, message: 'Chưa đăng nhập KBT Craper.' };
  }
  const id = (shopLocalId || '').trim();
  if (!id) {
    return { ok: false, message: 'Thiếu shop local id.' };
  }
  try {
    const data = await postWhatToSellToday(id);
    const mapSearch = (s: {
      keywords: string;
      url: string;
      rationale?: string | null;
    }) => ({
      keywords: s.keywords,
      url: s.url,
      rationale: s.rationale ?? null,
    });
    const trend = (
      data.searchesTrend ??
      data.searchesNow ??
      data.searches ??
      []
    ).map(mapSearch);
    const upcoming = (data.searchesUpcoming ?? []).map(mapSearch);
    const points =
      data.analysisPoints?.filter((p) => typeof p === 'string' && p.trim()) ??
      (data.analysis
        ? data.analysis
            .split('\n')
            .map((l) => l.replace(/^[-•*]\s*/, '').trim())
            .filter(Boolean)
        : []);
    return {
      ok: true,
      data: {
        analysis: data.analysis ?? points.join('\n'),
        analysisPoints: points,
        productCategories: data.productCategories ?? [],
        avgCost: data.avgCost ?? null,
        avgSalePrice: data.avgSalePrice ?? null,
        minPrice: data.minPrice,
        maxPrice: data.maxPrice,
        periodDays: data.periodDays,
        productCountUsed: data.productCountUsed,
        asOfDate: data.asOfDate,
        season: data.season,
        upcomingEvents: data.upcomingEvents ?? [],
        searchesTrend: trend,
        searchesUpcoming: upcoming,
      },
    };
  } catch (err) {
    return {
      ok: false,
      message: (err as Error).message || 'Không phân tích được.',
    };
  }
}

chrome.sidePanel
  ?.setPanelBehavior({ openPanelOnActionClick: true })
  .catch(() => undefined);

chrome.action.onClicked.addListener((tab) => {
  if (tab.windowId !== undefined) {
    chrome.sidePanel?.open({ windowId: tab.windowId }).catch(() => undefined);
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  void endTabAdding(tabId, 'cancelled');
  void handleSizeChartPendingTabClosed(tabId);
});

chrome.runtime.onMessage.addListener((message: ExtMessage, sender, sendResponse) => {
  (async () => {
    try {
      switch (message.type) {
        case 'GET_STATE':
          sendResponse(await bootstrapSession('panel'));
          return;
        case 'LOGIN': {
          await login(message.username, message.password);
          sendResponse({ ok: true, ...(await bootstrapSession('login')) });
          return;
        }
        case 'LOGOUT':
          cancelProductSearch();
          await logout();
          // Xóa toàn bộ dữ liệu phiên đã lưu (token + cache gắn user).
          await chrome.storage.local.remove([
            STORAGE_KEYS.auth,
            STORAGE_KEYS.fsStatus,
            STORAGE_KEYS.selectedShopIds,
            STORAGE_KEYS.shopsCache,
            'kbt_shops_cache',
            'kbt_shops_cache_v4',
            STORAGE_KEYS.addingByTab,
            STORAGE_KEYS.addBatchProgress,
            STORAGE_KEYS.productSources,
            STORAGE_KEYS.platforms,
            STORAGE_KEYS.addedProductIds,
            STORAGE_KEYS.addedProductIdsMarkedAt,
            STORAGE_KEYS.contentTabStatus,
            STORAGE_KEYS.draftsDirtyAt,
            STORAGE_KEYS.productSearch,
            STORAGE_KEYS.productSearchClicked,
            STORAGE_KEYS.recommendedGoodsIds,
            STORAGE_KEYS.sheinStyleAttrs,
            STORAGE_KEYS.whatToSellCache,
            STORAGE_KEYS.sheinSearchSort,
            STORAGE_KEYS.sizeChartUploaded,
            STORAGE_KEYS.sizeChartPending,
            STORAGE_KEYS.quickAddJobs,
            'kbt_selected_shop_id',
            'kbt_selected_platform',
          ]);
          await clearProductSearch();
          addingByTab.clear();
          addBatch = { total: 0, success: 0, failed: 0, updatedAt: 0 };
          sendResponse({ ok: true });
          return;
        case 'REFRESH_FS_STATUS':
          await refreshMetadata();
          await clearShopsCache();
          sendResponse({ ok: true, status: await refreshFourSellerSession() });
          return;
        case 'ADD_PRODUCT': {
          const tabId = sender.tab?.id;
          const sources = await readProductSources();
          const detection = detectProductPage(message.url, sources);
          if (tabId !== undefined) {
            await beginTabAdding(tabId, message.url, detection.ok ? detection.flow : undefined);
          }
          let addRes: AddProductResponse = { ok: false, message: 'Thêm sản phẩm thất bại.' };
          try {
            addRes = await handleAddProduct(message.url, message.html, {
              platform: message.platform,
              reviewCount: message.reviewCount,
              averageRating: message.averageRating,
              stock: message.stock,
              storeCode: message.storeCode,
              storeLogo: message.storeLogo,
              storeTitle: message.storeTitle,
              soldByTips: message.soldByTips,
              shipsFromTips: message.shipsFromTips,
              shopRank: message.shopRank,
              storeAttention: message.storeAttention,
              shippingDays: message.shippingDays,
              shippingDayPercent: message.shippingDayPercent,
              shippingDayPercentDesc: message.shippingDayPercentDesc,
              isLocalProduct: message.isLocalProduct,
              isSupportQuickShip: message.isSupportQuickShip,
              sizeGuide: message.sizeGuide,
            });
            if (addRes.ok && detection.ok && detection.flow) {
              await markSizeChartPending(message.url, detection.flow, tabId);
              if (tabId != null) void notifySizeChartLeaveGuard(tabId);
            }
            sendResponse(addRes);
          } finally {
            await endTabAdding(tabId, addRes.ok ? 'success' : 'failed');
          }
          return;
        }
        case 'UPLOAD_SIZE_CHART': {
          const maxBytes = 5 * 1024 * 1024;
          const fileBase64 = message.fileBase64?.trim() ?? '';
          const byteLength = base64ByteLength(fileBase64);
          if (!byteLength) {
            sendResponse({ ok: false, message: 'File trống.' } satisfies UploadSizeChartResponse);
            return;
          }
          if (byteLength > maxBytes) {
            sendResponse({
              ok: false,
              message: 'Size Chart tối đa 5 MB.',
            } satisfies UploadSizeChartResponse);
            return;
          }
          const mime = (message.mimeType || '').toLowerCase();
          if (!mime.startsWith('image/')) {
            sendResponse({
              ok: false,
              message: 'Chỉ hỗ trợ file ảnh PNG/JPG.',
            } satisfies UploadSizeChartResponse);
            return;
          }
          try {
            const buffer = base64ToArrayBuffer(fileBase64);
            const blob = new Blob([buffer], { type: mime || 'image/png' });
            const fileName = message.fileName?.trim() || 'size-chart.png';
            const uploaded = await uploadUserFile(blob, fileName);
            const publicUrl = uploaded.publicUrl?.trim();
            if (!publicUrl) {
              sendResponse({
                ok: false,
                message:
                  'Upload OK nhưng chưa có CDN URL. Kiểm tra bunny.cdn_hostname trong Settings.',
              } satisfies UploadSizeChartResponse);
              return;
            }
            await linkSupplierSizeChartImage({
              sourceUrl: message.url,
              publicUrl,
              fileId: uploaded.id,
            });
            await clearSizeChartPending(message.url);
            sendResponse({
              ok: true,
              message: 'Đã lưu ảnh Size Chart.',
              publicUrl,
            } satisfies UploadSizeChartResponse);
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Upload Size Chart thất bại.',
            } satisfies UploadSizeChartResponse);
          }
          return;
        }
        case 'GET_SIZE_CHART_STATUS': {
          try {
            const status = await getSupplierSizeChartImageStatus(message.url);
            const hasImage = Boolean(status?.hasImage);
            if (hasImage) await clearSizeChartPending(message.url);
            sendResponse({
              ok: true,
              hasImage,
            } satisfies SizeChartStatusResponse);
          } catch (err) {
            sendResponse({
              ok: false,
              hasImage: false,
              message: err instanceof Error ? err.message : 'Không kiểm tra được Size Chart.',
            } satisfies SizeChartStatusResponse);
          }
          return;
        }
        case 'ADD_CURRENT_TAB': {
          const status = await getActiveTabStatus();
          if (status.adding) {
            sendResponse({
              ok: false,
              message: 'Tab này đang thêm sản phẩm — vui lòng đợi xong.',
            });
            return;
          }
          if (!status.ready) {
            sendResponse({
              ok: false,
              message:
                status.message ||
                'Trang chưa sẵn sàng. Hãy đợi hoặc bấm «Tải lại trang».',
            });
            return;
          }
          const tabId = status.tabId;
          const captured = await captureActiveTab();
          if (!captured) {
            sendResponse({
              ok: false,
              message:
                'Không đọc được trang. Hãy tải lại (F5) trang sản phẩm rồi thử lại, hoặc bấm nút «Thêm sản phẩm» nổi trên trang.',
            });
            return;
          }
          if (tabId !== undefined) {
            await beginTabAdding(tabId, captured.url, status.flow);
          }
          let addRes: AddProductResponse = { ok: false, message: 'Thêm sản phẩm thất bại.' };
          try {
            addRes = await handleAddProduct(captured.url, captured.html, captured);
            if (addRes.ok && status.flow) {
              await markSizeChartPending(captured.url, status.flow, tabId);
              if (tabId != null) void notifySizeChartLeaveGuard(tabId);
            }
            sendResponse(addRes);
          } finally {
            await endTabAdding(tabId, addRes.ok ? 'success' : 'failed');
          }
          return;
        }
        case 'WHOAMI':
          sendResponse({ ok: true, tabId: sender.tab?.id });
          return;
        case 'GET_ACTIVE_TAB_STATUS':
          sendResponse(await getActiveTabStatus());
          return;
        case 'RELOAD_ACTIVE_TAB':
          sendResponse(await reloadActiveTab(message.tabId));
          return;
        case 'OPEN_URL_ACTIVE_TAB':
          sendResponse(await openUrlInActiveTab(message.url));
          return;
        case 'OPEN_URL_NEW_TAB':
          sendResponse(await openUrlInNewTab(message.url));
          return;
        case 'LIST_DRAFTS':
          sendResponse(
            await handleListDrafts(
              message.pageType,
              message.shopIds,
              message.pageCurrent,
              message.pageSize,
            ),
          );
          return;
        case 'LIST_TAB_COUNTS':
          sendResponse(await handleListTabCounts(message.shopIds));
          return;
        case 'DELETE_DRAFT':
          sendResponse(
            await handleDeleteDraft(
              message.id,
              message.sourceUrl,
              message.fourSellerShopId,
              message.pageType,
            ),
          );
          return;
        case 'LIST_SHOPS':
          sendResponse(await handleListShops());
          return;
        case 'WHAT_TO_SELL_TODAY':
          sendResponse(await handleWhatToSellToday(message.shopLocalId));
          return;
        case 'LIST_SAVED_KEYWORDS':
          sendResponse(await handleListSavedKeywords(message.shopLocalId));
          return;
        case 'GET_PRODUCT_SEARCH':
          sendResponse({ ok: true, state: await recoverStaleProductSearch() });
          return;
        case 'SEARCH_PRODUCTS':
          sendResponse(
            await handleSearchProducts(productSearchHost(), {
              shopLocalId: message.shopLocalId,
              shopFsId: message.shopFsId,
              catalog: message.catalog,
              keywords: message.keywords,
            }),
          );
          return;
        case 'SEARCH_CANCEL': {
          cancelProductSearch();
          sendResponse({ ok: true, state: await markProductSearchInterrupted() });
          return;
        }
        case 'REMOVE_SEARCHED_PRODUCT':
          sendResponse(await handleRemoveSearchedProduct(message.goodsId));
          return;
        case 'ADD_SEARCHED_PRODUCTS':
          sendResponse(await handleAddSearchedProducts(productSearchHost(), message.goodsIds));
          return;
        case 'QUICK_ADD_LIST_PRODUCT':
          sendResponse(
            await handleQuickAddListProduct(message.goodsId, message.url, sender.tab?.id),
          );
          return;
        case 'SHEIN_SEARCH_CAPTURE':
          onSheinSearchCapture(message.capture);
          sendResponse({ ok: true });
          return;
        case 'SHEIN_SEARCH_SESSION':
          onSheinSearchSession(message.session);
          sendResponse({ ok: true });
          return;
        case 'SET_SELECTED_SHOPS': {
          // Chỉ ghi storage — không await loadShopsIfReady (network) để tránh race/flicker UI.
          const rawIds = Array.isArray(message.shopIds)
            ? [
                ...new Set(
                  message.shopIds
                    .map((x) => (typeof x === 'number' ? x : Number(x)))
                    .filter((n) => Number.isFinite(n) && n > 0),
                ),
              ]
            : [];
          const cache = await readShopsCache();
          const shopIds =
            cache && cache.shops.length > 0
              ? rawIds.filter((id) => cache.shops.some((s) => s.id === id))
              : rawIds;
          await writeSelectedShopIds(shopIds);
          sendResponse({ ok: true, selectedShopIds: shopIds });
          return;
        }
        case 'SET_THEME': {
          const confirmed = await persistTheme(normalizeTheme(message.theme));
          sendResponse({ ok: true, theme: confirmed });
          return;
        }
        case 'OPEN_SIDE_PANEL': {
          const windowId = sender.tab?.windowId;
          if (windowId !== undefined) {
            await chrome.sidePanel?.open({ windowId }).catch(() => undefined);
          }
          sendResponse({ ok: true });
          return;
        }
        default:
          sendResponse({ ok: false, message: 'Message không hỗ trợ.' });
      }
    } catch (err) {
      sendResponse({ ok: false, message: (err as Error).message });
    }
  })();
  return true;
});

void recoverStaleProductSearch();

/**
 * Đổi prefix storage `kb_*` → `kbt_*` (tránh đụng KBI Craper trên trang Shein).
 * Copy value cũ nếu key mới chưa có, rồi xóa key `kb_*` trong storage của extension này.
 */
async function migrateLegacyKbStorageKeys(): Promise<void> {
  try {
    const pairs = Object.values(STORAGE_KEYS).map((next) => ({
      next,
      prev: next.startsWith('kbt_') ? `kb_${next.slice(4)}` : next,
    }));
    const prevKeys = [
      ...pairs.map((p) => p.prev),
      'kb_selected_shop_id',
      'kb_selected_platform',
      'kb_shops_cache',
      'kb_shops_cache_v4',
    ];
    const raw = await chrome.storage.local.get([...prevKeys, ...pairs.map((p) => p.next)]);
    const patch: Record<string, unknown> = {};
    const remove: string[] = [];
    for (const { prev, next } of pairs) {
      if (raw[prev] === undefined) continue;
      if (raw[next] === undefined) patch[next] = raw[prev];
      remove.push(prev);
    }
    for (const orphan of [
      'kb_selected_shop_id',
      'kb_selected_platform',
      'kb_shops_cache',
      'kb_shops_cache_v4',
    ]) {
      if (raw[orphan] !== undefined) remove.push(orphan);
    }
    if (Object.keys(patch).length) await chrome.storage.local.set(patch);
    if (remove.length) await chrome.storage.local.remove([...new Set(remove)]);
  } catch {
    /* ignore — SW cold start vẫn chạy được với key mới */
  }
}

/** Giữ phiên đăng nhập — refresh sớm trước khi access JWT hết hạn (~15 phút). */
function installAuthSessionMaintenance(): void {
  void migrateLegacyKbStorageKeys().then(() => maintainAuthSession());
  chrome.alarms.create(AUTH_REFRESH_ALARM, { periodInMinutes: 10 }).catch(() => undefined);
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === AUTH_REFRESH_ALARM) void maintainAuthSession();
  });
  chrome.runtime.onStartup.addListener(() => {
    void migrateLegacyKbStorageKeys().then(() => maintainAuthSession());
  });
  chrome.runtime.onInstalled.addListener(() => {
    void migrateLegacyKbStorageKeys().then(() => maintainAuthSession());
  });
}

installAuthSessionMaintenance();
