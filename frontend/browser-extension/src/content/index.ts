// Content script CHẠY ĐỘC LẬP (chỉ import type — bị xóa lúc build — nên không có runtime import,
// đảm bảo content.js nạp được như classic content script của MV3).
// Logic nhận diện URL PDP mirror `shared/productPage.ts`.
import type { ProductSourceOption } from '../shared/types';
import type {
  AddProductResponse,
  ContentMessage,
  FetchSheinSearchResponse,
  QuickAddListStatus,
  SizeChartStatusResponse,
  UploadSizeChartResponse,
} from '../shared/messages';
import {
  isSheinSizeGuide,
  parseSheinSizeGuideResponse,
  SHEIN_SIZE_GUIDE_FETCH_MSG,
  SHEIN_SIZE_GUIDE_FETCH_RESULT_MSG,
  SHEIN_SIZE_GUIDE_STORAGE_KEY,
  type SheinSizeGuide,
} from '../shared/sheinSizeGuide';
import { initSheinSizeGuideModalResize } from './sheinSizeGuideModal';

function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  const chunkSize = 0x8000;
  for (let i = 0; i < bytes.length; i += chunkSize) {
    const slice = bytes.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...slice);
  }
  return btoa(binary);
}

const AUTH_KEY = 'kbt_auth';
const SOURCES_KEY = 'kbt_product_sources';
const FS_STATUS_KEY = 'kbt_fs_status';
const SELECTED_SHOPS_KEY = 'kbt_selected_shop_ids';
const ADDED_IDS_KEY = 'kbt_added_product_ids';
const ADDING_BY_TAB_KEY = 'kbt_adding_by_tab';
const SIZE_CHART_UPLOADED_KEY = 'kbt_size_chart_uploaded';
const SIZE_CHART_PENDING_KEY = 'kbt_size_chart_pending';
const CONTENT_TAB_STATUS_KEY = 'kbt_content_tab_status';
const QUICK_ADD_JOBS_KEY = 'kbt_quick_add_jobs';
const SHEIN_SEARCH_STORAGE_KEY = 'kbt_shein_search_cache';
const SHEIN_SEARCH_MSG = 'kbt-shein-search-capture';
const SHEIN_SEARCH_SESSION_KEY = 'kbt_shein_search_session';
const SHEIN_SEARCH_SESSION_MSG = 'kbt-shein-search-session';
const SHEIN_SEARCH_RESET_MSG = 'kbt-shein-search-reset';
const SHEIN_SEARCH_FETCH_MSG = 'kbt-shein-search-fetch';
const SHEIN_SEARCH_FETCH_RESULT_MSG = 'kbt-shein-search-fetch-result';
const SHEIN_SEARCH_FETCH_TIMEOUT_MS = 45_000;
const SHEIN_SIZE_GUIDE_CACHE_WAIT_MS = 1500;
const SHEIN_SIZE_GUIDE_FETCH_TIMEOUT_MS = 10_000;
const SHEIN_SEARCH_HEADER_DENY = new Set([
  'host',
  'origin',
  'referer',
  'connection',
  'content-length',
  'cookie',
  'user-agent',
  'accept-encoding',
]);
const BUTTON_ID = 'kbt-add-product-btn';
const SIZE_CHART_BUTTON_ID = 'kbt-upload-size-chart-btn';
const SIZE_CHART_INPUT_ID = 'kbt-upload-size-chart-input';
const SIZE_CHART_WARN_ID = 'kbt-size-chart-warn';
const STYLE_ID = 'kbt-add-product-style';
const SIZE_CHART_MAX_BYTES = 5 * 1024 * 1024;
const LIST_BADGE_CLASS = 'kbt-list-saved-badge';
const LIST_BADGE_HOST_CLASS = 'kbt-list-saved-host';
const LIST_QUICK_ADD_CLASS = 'kbt-list-quick-add';
/**
 * Shein product cards for saved badge + quick-add:
 * - search/category cũ: `.product-list … .product-list__item`
 * - PDP recommend (Customers Also Viewed, …): `.recommend-bottom_list …`
 * - store panel: `#store-panel-items`
 * - list-v2 browse (`#product-list-v2`, `product-list-v2__main_view-new`): ô grid
 *   `.product-list-v2__section > div > *` — card thường **không** có class `.product-card`
 */
const SHEIN_LIST_CARD_SELECTOR = [
  '.product-list.j-expose__product-list .product-card.j-expose__product-item.product-list__item',
  '.recommend-bottom_list .product-card.j-expose__product-item',
  '#store-panel-items .product-card',
  '.product-list-v2__product-list .product-card',
  '.product-list-v2__container .product-card',
  '.product-list-v2__section .product-card',
  '#product-list-v2 .j-expose__product-item',
  '#product-list-v2 .product-list-v2__section > div > *',
  '.product-list-v2__section > div > *',
].join(', ');

const SHEIN_LIST_ROOT_SELECTOR = [
  '.product-list.j-expose__product-list',
  '.recommend-bottom_list',
  '#store-panel-items',
  '#product-list-v2',
  '.product-list-v2__container',
  '.product-list-v2__product-list',
  '.product-list-v2__section',
  '.product-list-v2__main',
].join(', ');

/** Trạng thái Thêm nhanh trên card (hydrate từ storage + message). */
type LocalQuickAddJob = {
  status: 'queued' | 'running' | 'failed';
  message?: string;
  at: number;
};
const quickAddJobsLocal: Record<string, LocalQuickAddJob> = {};
const QUICK_ADD_FAIL_FLASH_MS = 2800;

interface ContentTabStatusSnapshot {
  href: string;
  pageReady: boolean;
  sessionReady: boolean;
  shopsSelected: boolean;
  alreadyAdded: boolean;
  addedStatus: 'none' | 'partial' | 'full';
  isAdding: boolean;
  isProductPage: boolean;
  at: number;
}

type AddedStatus = 'none' | 'partial' | 'full';
type AddedProductShopMap = Record<string, number[]>;

let pageReady = false;
let isProductPage = false;
let currentSource: ProductSourceOption | null = null;
/** Đã đăng nhập kinhbac + cookie 4Seller live. */
let sessionReady = false;
/** Đã chọn ≥1 shop trong side panel. */
let shopsSelected = false;
/** Shop ids đang chọn (để tính partial/full). */
let selectedShopIds: number[] = [];
/** Trạng thái đã thêm theo shop selection. */
let addedStatus: AddedStatus = 'none';
let addedShopHit = 0;
let addedShopTotal = 0;
/** Đủ mọi shop đang chọn → disable nút. */
let alreadyAdded = false;
/** Cho phép upload Size Chart sau khi thêm SP thành công (hoặc đã thêm trước đó). */
let sizeChartUploadEnabled = false;
/** Đang upload Size Chart. */
let sizeChartUploading = false;
/** Upload Size Chart thành công (vẫn cho upload lại). */
let sizeChartUploaded = false;
/** Key sản phẩm hiện tại — reset trạng thái upload khi đổi PDP. */
let sizeChartProductKey = '';
/**
 * true = đóng PDP chưa Size Chart sẽ mở lại (FAB / side panel).
 * false = tab quick-add mở tự động — user đóng thì thôi.
 */
let sizeChartReopenOnClose = true;
let sizeChartLeaveWarningAttached = false;
/** Đang thêm: hiển thị nút (local click hoặc side panel qua BG map). */
let isAdding = false;
/** Click Thêm sản phẩm trên tab này đang chạy (tránh evaluate ghi đè spinner). */
let addClickInFlight = false;
/** chrome.tabs.Tab.id của tab hiện tại. */
let myTabId: number | null = null;
/** Extension bị reload/cập nhật → content script cũ chết; ẩn nút, không tạo lại. */
let extensionDead = false;
/** Đang ở trang list Shein (không phải PDP) + có ProductSource — hiện badge đã thêm. */
let listBadgesEnabled = false;
/** Snapshot map / shop selection cho MutationObserver re-sync list. */
let lastAddedMapForList: AddedProductShopMap = {};
let lastSelectedShopIdsForList: number[] = [];
let listBadgeObserver: MutationObserver | null = null;
let listBadgeSyncTimer = 0;

function isExtensionAlive(): boolean {
  try {
    return !extensionDead && typeof chrome !== 'undefined' && !!chrome.runtime?.id;
  } catch {
    return false;
  }
}

function isContextInvalidatedError(err: unknown): boolean {
  const msg = err instanceof Error ? err.message : String(err ?? '');
  return /extension context invalidated/i.test(msg);
}

/** Context chết → gỡ nút, dừng mọi sync tiếp theo. */
function markExtensionDead(): void {
  if (extensionDead) return;
  extensionDead = true;
  if (sizeChartLeaveWarningAttached) {
    window.removeEventListener('beforeunload', onSizeChartLeaveWarning);
    sizeChartLeaveWarningAttached = false;
  }
  removeButton();
  removeSizeChartUploadUi();
  clearListSavedBadges();
  stopListBadgeObserver();
}

function ensureStyle(): void {
  if (document.getElementById(STYLE_ID)) return;
  const style = document.createElement('style');
  style.id = STYLE_ID;
  style.textContent = `
@keyframes kbt-ext-spin { to { transform: rotate(360deg); } }
@keyframes kbt-ext-ring {
  0% { transform: scale(1); opacity: 0.55; }
  100% { transform: scale(2.15); opacity: 0; }
}
#${BUTTON_ID} {
  position: fixed;
  z-index: 2147483647;
  top: 120px;
  right: 24px;
  width: 44px;
  height: 44px;
  padding: 0;
  margin: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  border: none;
  color: #fff;
  background: #1677ff;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(22, 119, 255, 0.35);
  overflow: visible;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}
#${BUTTON_ID}::before,
#${BUTTON_ID}::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 1px solid #1677ff;
  pointer-events: none;
  opacity: 0;
}
#${BUTTON_ID}.kbt-ext-fab--pulse::before,
#${BUTTON_ID}.kbt-ext-fab--pulse::after {
  animation: kbt-ext-ring 1.8s cubic-bezier(0.22, 1, 0.36, 1) infinite;
}
#${BUTTON_ID}.kbt-ext-fab--pulse::after {
  animation-delay: 0.9s;
}
#${BUTTON_ID}.kbt-ext-fab--muted {
  background: #8c8c8c;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  opacity: 0.95;
}
#${BUTTON_ID}.kbt-ext-fab--muted::before,
#${BUTTON_ID}.kbt-ext-fab--muted::after {
  border-color: #8c8c8c;
}
#${BUTTON_ID}.kbt-ext-fab--done {
  background: #52c41a;
  box-shadow: 0 2px 8px rgba(82, 196, 26, 0.35);
}
#${BUTTON_ID}.kbt-ext-fab--done::before,
#${BUTTON_ID}.kbt-ext-fab--done::after {
  border-color: #52c41a;
  animation: none;
  opacity: 0;
}
#${BUTTON_ID}.kbt-ext-fab--partial {
  background: #fa8c16;
  box-shadow: 0 2px 8px rgba(250, 140, 22, 0.4);
}
#${BUTTON_ID}.kbt-ext-fab--partial::before,
#${BUTTON_ID}.kbt-ext-fab--partial::after {
  border-color: #fa8c16;
}
#${BUTTON_ID}.kbt-ext-fab--loading {
  cursor: not-allowed;
  opacity: 0.85;
}
#${BUTTON_ID}.kbt-ext-fab--loading::before,
#${BUTTON_ID}.kbt-ext-fab--loading::after {
  animation: none;
  opacity: 0;
}
#${SIZE_CHART_BUTTON_ID} {
  position: fixed;
  z-index: 2147483646;
  top: 172px;
  right: 24px;
  width: 44px;
  height: 44px;
  padding: 0;
  margin: 0;
  display: none;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  border: none;
  color: #fff;
  background: #722ed1;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(114, 46, 209, 0.35);
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}
#${SIZE_CHART_BUTTON_ID}.kbt-ext-visible {
  display: flex;
}
#${SIZE_CHART_BUTTON_ID}.kbt-ext-fab--done {
  background: #52c41a;
  box-shadow: 0 2px 8px rgba(82, 196, 26, 0.35);
  color: #fff;
}
#${SIZE_CHART_BUTTON_ID}.kbt-ext-fab--done svg {
  fill: currentColor;
}
#${SIZE_CHART_BUTTON_ID}.kbt-ext-fab--loading {
  cursor: not-allowed;
  opacity: 0.85;
}
#${SIZE_CHART_BUTTON_ID} svg {
  width: 20px;
  height: 20px;
  fill: currentColor;
}
#${SIZE_CHART_WARN_ID} {
  position: fixed;
  z-index: 2147483645;
  top: 224px;
  right: 24px;
  max-width: none;
  white-space: nowrap;
  padding: 8px 12px;
  margin: 0;
  display: none;
  align-items: center;
  gap: 0;
  border: none;
  border-radius: 8px;
  background: #ff4d4f;
  color: #fff;
  box-shadow: 0 2px 8px rgba(255, 77, 79, 0.4);
  cursor: pointer;
  text-align: center;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  font-size: 12px;
  line-height: 1.2;
  font-weight: 600;
}
#${SIZE_CHART_WARN_ID}.kbt-ext-visible {
  display: block;
}
#${SIZE_CHART_WARN_ID}:hover {
  background: #ff7875;
}
.kbt-ext-spinner {
  width: 18px;
  height: 18px;
  border: 2px solid rgba(255, 255, 255, 0.45);
  border-top-color: #fff;
  border-radius: 50%;
  display: inline-block;
  animation: kbt-ext-spin 0.6s linear infinite;
}
.kbt-ext-add-icon,
.kbt-ext-check-icon {
  width: 20px;
  height: 20px;
  display: block;
  flex-shrink: 0;
  position: relative;
  z-index: 1;
}
.${LIST_BADGE_HOST_CLASS} {
  position: relative;
}
.${LIST_BADGE_CLASS} {
  position: absolute;
  top: 8px;
  right: 8px;
  z-index: 20;
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: #52c41a;
  color: #fff;
  box-shadow: 0 2px 6px rgba(82, 196, 26, 0.4);
  pointer-events: none;
}
.${LIST_BADGE_CLASS}.${LIST_BADGE_CLASS}--partial {
  background: #fa8c16;
  box-shadow: 0 2px 6px rgba(250, 140, 22, 0.45);
}
.${LIST_BADGE_CLASS} .kbt-ext-check-icon {
  width: 16px;
  height: 16px;
}
.${LIST_QUICK_ADD_CLASS} {
  position: absolute;
  top: 8px;
  right: 8px;
  z-index: 21;
  width: 28px;
  height: 28px;
  padding: 0;
  margin: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  border: none;
  color: #fff;
  background: #1677ff;
  box-shadow: 0 2px 6px rgba(22, 119, 255, 0.4);
  cursor: pointer;
  pointer-events: auto;
  font: inherit;
}
.${LIST_BADGE_HOST_CLASS}:has(.${LIST_BADGE_CLASS}) .${LIST_QUICK_ADD_CLASS} {
  right: 40px;
}
.${LIST_QUICK_ADD_CLASS}:hover:not(:disabled) {
  background: #4096ff;
}
.${LIST_QUICK_ADD_CLASS}:disabled {
  cursor: not-allowed;
  opacity: 0.85;
}
.${LIST_QUICK_ADD_CLASS}.${LIST_QUICK_ADD_CLASS}--muted {
  background: #8c8c8c;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
  cursor: pointer;
  opacity: 0.95;
}
.${LIST_QUICK_ADD_CLASS}.${LIST_QUICK_ADD_CLASS}--loading {
  cursor: wait;
}
.${LIST_QUICK_ADD_CLASS}.${LIST_QUICK_ADD_CLASS}--queued {
  opacity: 0.75;
  background: #69b1ff;
}
.${LIST_QUICK_ADD_CLASS}.${LIST_QUICK_ADD_CLASS}--error {
  background: #ff4d4f;
  box-shadow: 0 2px 6px rgba(255, 77, 79, 0.45);
}
.${LIST_QUICK_ADD_CLASS} .kbt-ext-add-icon,
.${LIST_QUICK_ADD_CLASS} .kbt-ext-check-icon {
  width: 16px;
  height: 16px;
}
.${LIST_QUICK_ADD_CLASS} .kbt-ext-spinner {
  width: 14px;
  height: 14px;
  border-width: 2px;
}`;
  (document.head ?? document.documentElement).appendChild(style);
}

function createAddIcon(): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.classList.add('kbt-ext-add-icon');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute(
    'd',
    'M8 2a.75.75 0 0 1 .75.75v4.5h4.5a.75.75 0 0 1 0 1.5h-4.5v4.5a.75.75 0 0 1-1.5 0v-4.5h-4.5a.75.75 0 0 1 0-1.5h4.5v-4.5A.75.75 0 0 1 8 2z',
  );
  path.setAttribute('fill', 'currentColor');
  svg.appendChild(path);
  return svg;
}

function createCheckIcon(): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.classList.add('kbt-ext-check-icon');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute(
    'd',
    'M12.207 4.793a1 1 0 0 1 0 1.414l-4.5 4.5a1 1 0 0 1-1.414 0l-2-2a1 1 0 1 1 1.414-1.414L7 8.586l3.793-3.793a1 1 0 0 1 1.414 0z',
  );
  path.setAttribute('fill', 'currentColor');
  svg.appendChild(path);
  return svg;
}

function parseDomains(domains?: string): string[] {
  if (!domains) return [];
  return domains
    .split(/[\s,;\n]+/)
    .map((d) => d.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/\/.*$/, ''))
    .filter(Boolean);
}

function hostMatches(host: string, pattern: string): boolean {
  const h = host.toLowerCase();
  const p = pattern.toLowerCase();
  return h === p || h.endsWith('.' + p);
}

/**
 * Khớp Domains đã cấu hình; nếu Domains trống (seed cũ) thì đoán theo Code
 * (vd. SHEIN → host chứa shein). Không có Domains + không đoán được → không hiện FAB.
 */
function findSource(sources: ProductSourceOption[]): ProductSourceOption | undefined {
  const host = location.hostname.toLowerCase();
  const byDomains = sources.find((s) =>
    parseDomains(s.domains).some((p) => hostMatches(host, p)),
  );
  if (byDomains) return byDomains;
  return sources.find((s) => {
    if (parseDomains(s.domains).length > 0) return false;
    const code = (s.code || '').toLowerCase().replace(/[^a-z0-9]/g, '');
    return !!code && host.includes(code);
  });
}

/** Mirror shared/productPage.ts — nhận diện luồng + PDP theo URL. */
function resolveFlow(host: string, source: ProductSourceOption): string {
  const h = host.toLowerCase();
  const code = (source.code || '').toLowerCase();
  if (code.includes('shein') || h.includes('shein')) return 'shein';
  if (code.includes('temu') || h.includes('temu')) return 'temu';
  if (code.includes('amazon') || h.includes('amazon')) return 'amazon';
  if (code.includes('walmart') || h.includes('walmart')) return 'walmart';
  if (code.includes('ebay') || h.includes('ebay')) return 'ebay';
  if (code.includes('aliexpress') || h.includes('aliexpress')) return 'aliexpress';
  if (code.includes('etsy') || h.includes('etsy')) return 'etsy';
  if (code.includes('cj') || h.includes('cjdropshipping')) return 'cj';
  if (code.includes('otto') || h.includes('otto.de')) return 'otto';
  return code || 'generic';
}

function isProductDetailUrl(url: URL, flow: string): boolean {
  const path = url.pathname.toLowerCase();
  switch (flow) {
    case 'shein':
      return /-p-\d+/i.test(path);
    case 'temu':
      return (
        (path.includes('goods.html') && !!url.searchParams.get('goods_id')) ||
        /-g-\d+(\.html)?$/i.test(path)
      );
    case 'amazon':
      return /\/(dp|gp\/product)\//i.test(path);
    case 'walmart':
      return /\/ip\//i.test(path);
    case 'ebay':
      return /\/itm(\/|$)/i.test(path);
    case 'aliexpress':
      return /\/item\//i.test(path);
    case 'etsy':
      return /\/listing\//i.test(path);
    case 'cj':
      return /\/product\//i.test(path);
    case 'otto':
      return /\/p\//i.test(path) || /\/produkt\//i.test(path);
    default:
      return (
        /-p-\d+/i.test(path) ||
        /-g-\d+/i.test(path) ||
        /\/(product|products|item|dp|goods|listing|ip)(\/|$)/i.test(path) ||
        url.searchParams.has('goods_id') ||
        url.searchParams.has('product_id')
      );
  }
}

function detectCurrentProductPage(
  sources: ProductSourceOption[],
): { source: ProductSourceOption; flow: string } | null {
  const source = findSource(sources);
  if (!source) return null;
  let parsed: URL;
  try {
    parsed = new URL(location.href);
  } catch {
    return null;
  }
  const flow = resolveFlow(parsed.hostname, source);
  if (!isProductDetailUrl(parsed, flow)) return null;
  return { source, flow };
}

/** Mirror shared/productId.ts */
function extractProductId(url: string, flow: string): string | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }
  const path = parsed.pathname;
  const q = parsed.searchParams;
  switch (flow) {
    case 'shein': {
      const m = path.match(/-p-(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'temu': {
      const goodsId = q.get('goods_id');
      if (goodsId?.trim()) return goodsId.trim();
      const m = path.match(/-g-(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'amazon': {
      const m = path.match(/\/(?:dp|gp\/product)\/([A-Z0-9]{8,})/i);
      return m?.[1]?.toUpperCase() ?? null;
    }
    case 'walmart': {
      const m = path.match(/\/ip\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'ebay': {
      const m = path.match(/\/itm\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'aliexpress': {
      const m = path.match(/\/item\/(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'etsy': {
      const m = path.match(/\/listing\/(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'cj': {
      const m = path.match(/\/product\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? q.get('id')?.trim() ?? null;
    }
    case 'otto': {
      const m = path.match(/\/(?:p|produkt)\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    default: {
      const goodsId = q.get('goods_id') || q.get('product_id') || q.get('id');
      if (goodsId?.trim()) return goodsId.trim();
      const m =
        path.match(/-p-(\d+)/i) ||
        path.match(/-g-(\d+)/i) ||
        path.match(/\/(?:product|products|item|dp|goods|listing|ip)\/(?:[^/]+\/)?([A-Za-z0-9_-]+)/i);
      return m?.[1] ?? null;
    }
  }
}

function makeAddedProductKey(flow: string, productId: string): string {
  return `${flow.toLowerCase()}:${productId}`;
}

function normalizeAddedMap(raw: unknown): AddedProductShopMap {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: AddedProductShopMap = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof key !== 'string' || !key.includes(':')) continue;
    if (!Array.isArray(value)) continue;
    const shops = value
      .map((x) => (typeof x === 'number' ? x : Number(x)))
      .filter((n) => Number.isFinite(n) && n > 0);
    if (shops.length > 0) out[key] = [...new Set(shops)];
  }
  return out;
}

function readSelectedShopIds(raw: unknown): number[] {
  if (!Array.isArray(raw)) return [];
  return raw
    .map((x) => (typeof x === 'number' ? x : Number(x)))
    .filter((n) => Number.isFinite(n) && n > 0);
}

function getAddedStatusForKey(
  productKey: string | undefined,
  map: AddedProductShopMap,
  shopIds: number[],
): { status: AddedStatus; hit: number; total: number } {
  const total = shopIds.length;
  if (!productKey || total === 0) return { status: 'none', hit: 0, total };
  const have = map[productKey] ?? [];
  const hit = shopIds.filter((id) => have.includes(id)).length;
  if (hit === 0) return { status: 'none', hit: 0, total };
  if (hit >= total) return { status: 'full', hit, total };
  return { status: 'partial', hit, total };
}

function isExtensionOwnedNode(node: Node | null): boolean {
  if (!node || node.nodeType !== Node.ELEMENT_NODE) return false;
  const el = node as Element;
  if (
    el.id === BUTTON_ID ||
    el.id === STYLE_ID ||
    el.id === SIZE_CHART_BUTTON_ID ||
    el.id === SIZE_CHART_WARN_ID
  ) {
    return true;
  }
  if (
    el.classList?.contains(LIST_BADGE_CLASS) ||
    el.classList?.contains(LIST_BADGE_HOST_CLASS) ||
    el.classList?.contains(LIST_QUICK_ADD_CLASS)
  ) {
    return true;
  }
  return typeof el.closest === 'function'
    ? !!el.closest(
        `#${BUTTON_ID}, #${SIZE_CHART_BUTTON_ID}, #${SIZE_CHART_WARN_ID}, .${LIST_BADGE_CLASS}, .${LIST_QUICK_ADD_CLASS}`,
      )
    : false;
}

function readSheinGoodsIdAttr(el: Element | null | undefined): string | null {
  if (!el) return null;
  const raw =
    el.getAttribute('data-id')?.trim() ||
    el.getAttribute('data-goods-id')?.trim() ||
    el.getAttribute('data-goodsId')?.trim() ||
    el.getAttribute('goods-id')?.trim() ||
    '';
  return /^\d+$/.test(raw) ? raw : null;
}

function extractSheinGoodsIdFromHref(href: string): string | null {
  if (!href) return null;
  const m =
    href.match(/-p-(\d+)/i) ||
    href.match(/[?&]goods_id=(\d+)/i) ||
    href.match(/[?&]goodsId=(\d+)/i);
  return m?.[1] ?? null;
}

function firstSheinPdpAnchor(card: Element): Element | null {
  return (
    card.querySelector('a.S-product-card__img-container') ||
    card.querySelector('a.goods-title-link') ||
    card.querySelector('a[href*="-p-"]') ||
    card.querySelector('a[href*="goods_id="]') ||
    card.querySelector('a[data-id]')
  );
}

function extractSheinGoodsIdFromCard(card: Element): string | null {
  const imgLink = card.querySelector('a.S-product-card__img-container');
  const fromImg = readSheinGoodsIdAttr(imgLink);
  if (fromImg) return fromImg;
  const fromCard = readSheinGoodsIdAttr(card);
  if (fromCard) return fromCard;
  const href = firstSheinPdpAnchor(card)?.getAttribute('href') || '';
  const fromHref = extractSheinGoodsIdFromHref(href);
  if (fromHref) return fromHref;
  const attrHit = card.querySelector('a[data-goods-id], [data-goods-id], [data-goodsId], [goods-id]');
  return readSheinGoodsIdAttr(attrHit);
}

function extractSheinPdpHrefFromCard(card: Element): string | null {
  const href = firstSheinPdpAnchor(card)?.getAttribute('href') || '';
  if (!href.trim()) return null;
  try {
    return new URL(href, location.href).href;
  } catch {
    return null;
  }
}

function querySheinListCards(): Element[] {
  const nodes = document.querySelectorAll(SHEIN_LIST_CARD_SELECTOR);
  const cards: Element[] = [];
  for (const el of nodes) {
    if (!(el instanceof Element)) continue;
    if (el.parentElement?.closest(SHEIN_LIST_CARD_SELECTOR)) continue;
    cards.push(el);
  }
  return cards;
}

/** Overlay host: ảnh card (không nhét button vào `<a>`). */
function findListCardOverlayHost(card: Element): Element {
  const preferred = card.querySelector(
    '.product-card__top-wrapper, .S-product-card__top-wrapper, .product-card__imgs',
  );
  if (preferred && preferred.tagName !== 'A') return preferred;
  return card;
}

function isSheinListRootMutation(el: Element): boolean {
  if (el.id === 'product-list-v2' || el.id === 'store-panel-items') return true;
  const cls = el.classList;
  return (
    cls.contains('product-list') ||
    cls.contains('recommend-bottom_list') ||
    cls.contains('product-list-v2__container') ||
    cls.contains('product-list-v2__product-list') ||
    cls.contains('product-list-v2__section') ||
    cls.contains('product-list-v2__main')
  );
}

function clearListSavedBadges(): void {
  document.querySelectorAll(`.${LIST_BADGE_CLASS}`).forEach((el) => el.remove());
  document.querySelectorAll(`.${LIST_QUICK_ADD_CLASS}`).forEach((el) => el.remove());
  document.querySelectorAll(`.${LIST_BADGE_HOST_CLASS}`).forEach((el) => {
    el.classList.remove(LIST_BADGE_HOST_CLASS);
  });
}

function stopListBadgeObserver(): void {
  listBadgeObserver?.disconnect();
  listBadgeObserver = null;
  window.clearTimeout(listBadgeSyncTimer);
  listBadgeSyncTimer = 0;
}

function ensureListBadgeOnHost(host: Element, status: 'partial' | 'full'): void {
  host.classList.add(LIST_BADGE_HOST_CLASS);
  let badge = host.querySelector(`.${LIST_BADGE_CLASS}`) as HTMLElement | null;
  if (!badge) {
    badge = document.createElement('span');
    badge.className = LIST_BADGE_CLASS;
    badge.appendChild(createCheckIcon());
    host.appendChild(badge);
  }
  const partial = status === 'partial';
  badge.classList.toggle(`${LIST_BADGE_CLASS}--partial`, partial);
  badge.title = partial ? 'Thiếu shop (chưa đủ shop đang chọn)' : 'Đã thêm';
  badge.setAttribute('aria-label', badge.title);
}

function removeListBadgeFromHost(host: Element): void {
  const existing = host.querySelector(`.${LIST_BADGE_CLASS}`);
  if (existing) existing.remove();
  if (!host.querySelector(`.${LIST_QUICK_ADD_CLASS}`)) {
    host.classList.remove(LIST_BADGE_HOST_CLASS);
  }
}

function setQuickAddButtonVisual(
  button: HTMLButtonElement,
  opts: {
    loading?: boolean;
    queued?: boolean;
    muted?: boolean;
    error?: boolean;
    label: string;
  },
): void {
  const loading = !!opts.loading;
  const queued = !!opts.queued && loading;
  const muted = !!opts.muted && !loading;
  const error = !!opts.error && !loading;
  button.disabled = loading;
  button.classList.toggle(`${LIST_QUICK_ADD_CLASS}--loading`, loading);
  button.classList.toggle(`${LIST_QUICK_ADD_CLASS}--queued`, queued);
  button.classList.toggle(`${LIST_QUICK_ADD_CLASS}--muted`, muted);
  button.classList.toggle(`${LIST_QUICK_ADD_CLASS}--error`, error);
  button.title = opts.label;
  button.setAttribute('aria-label', opts.label);
  if (loading) {
    const spinner = document.createElement('span');
    spinner.className = 'kbt-ext-spinner';
    button.replaceChildren(spinner);
  } else {
    button.replaceChildren(createAddIcon());
  }
}

function onQuickAddClick(ev: Event): void {
  ev.preventDefault();
  ev.stopPropagation();
  if (typeof ev.stopImmediatePropagation === 'function') ev.stopImmediatePropagation();
  const button = ev.currentTarget as HTMLButtonElement | null;
  if (!button || button.disabled) return;
  const goodsId = button.dataset.goodsId?.trim();
  if (!goodsId) return;
  void runQuickAddFromCard(button, goodsId, button.dataset.pdpUrl || undefined);
}

async function runQuickAddFromCard(
  button: HTMLButtonElement,
  goodsId: string,
  pdpUrl?: string,
): Promise<void> {
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  if (!sessionReady || !shopsSelected) {
    await openSidePanel();
    showToast(
      !sessionReady
        ? 'Mở side panel để đăng nhập KBT Craper / kiểm tra phiên 4Seller.'
        : 'Chọn ít nhất 1 shop trong side panel trước khi thêm.',
      false,
    );
    return;
  }
  const existing = quickAddJobsLocal[goodsId];
  if (existing?.status === 'queued' || existing?.status === 'running') return;

  applyQuickAddStatus(goodsId, 'queued');
  setQuickAddButtonVisual(button, {
    loading: true,
    queued: true,
    label: 'Trong hàng đợi...',
  });

  try {
    const res = (await chrome.runtime.sendMessage({
      type: 'QUICK_ADD_LIST_PRODUCT',
      goodsId,
      url: pdpUrl,
    })) as { ok?: boolean; message?: string } | undefined;
    const stillActive =
      quickAddJobsLocal[goodsId]?.status === 'queued' ||
      quickAddJobsLocal[goodsId]?.status === 'running';
    if (!res?.ok && !stillActive) {
      // BG đã báo failed qua status/storage
    } else if (!res?.ok) {
      applyQuickAddStatus(goodsId, 'failed', res?.message || 'Thêm thất bại.');
    } else if (stillActive) {
      applyQuickAddStatus(goodsId, 'success', res.message);
    }
  } catch (err) {
    if (isContextInvalidatedError(err)) {
      markExtensionDead();
      return;
    }
    applyQuickAddStatus(goodsId, 'failed', (err as Error).message || 'Thêm thất bại.');
  }
}

function applyQuickAddStatus(
  goodsId: string,
  status: QuickAddListStatus,
  message?: string,
): void {
  const normalized: QuickAddListStatus =
    status === 'pending' ? 'running' : status;

  if (normalized === 'queued' || normalized === 'running') {
    quickAddJobsLocal[goodsId] = {
      status: normalized,
      message,
      at: Date.now(),
    };
  } else if (normalized === 'success') {
    delete quickAddJobsLocal[goodsId];
  } else if (normalized === 'failed') {
    const prev = quickAddJobsLocal[goodsId];
    const wasActive = prev?.status === 'queued' || prev?.status === 'running';
    quickAddJobsLocal[goodsId] = {
      status: 'failed',
      message,
      at: Date.now(),
    };
    window.setTimeout(() => {
      const job = quickAddJobsLocal[goodsId];
      if (
        job?.status === 'failed' &&
        Date.now() - job.at >= QUICK_ADD_FAIL_FLASH_MS - 50
      ) {
        delete quickAddJobsLocal[goodsId];
        scheduleListBadgeSync();
      }
    }, QUICK_ADD_FAIL_FLASH_MS);
    if (message && wasActive) showToast(message, false);
  }
  scheduleListBadgeSync();
}

function hydrateQuickAddJobsFromStorage(raw: unknown): void {
  const map =
    raw && typeof raw === 'object' && !Array.isArray(raw)
      ? (raw as Record<string, { status?: string; message?: string; at?: number }>)
      : {};

  const seen = new Set<string>();
  for (const [id, job] of Object.entries(map)) {
    seen.add(id);
    const st = job?.status;
    if (st === 'queued' || st === 'running') {
      quickAddJobsLocal[id] = {
        status: st,
        message: job.message,
        at: Number(job.at) || Date.now(),
      };
    } else if (st === 'success') {
      delete quickAddJobsLocal[id];
    } else if (st === 'failed') {
      const at = Number(job.at) || Date.now();
      if (Date.now() - at < QUICK_ADD_FAIL_FLASH_MS) {
        quickAddJobsLocal[id] = { status: 'failed', message: job.message, at };
      } else {
        delete quickAddJobsLocal[id];
      }
    }
  }

  // Storage có thể trễ vài trăm ms sau click — giữ job local mới nếu chưa có trong map.
  for (const id of Object.keys(quickAddJobsLocal)) {
    const local = quickAddJobsLocal[id];
    if (
      (local.status === 'queued' || local.status === 'running') &&
      !seen.has(id) &&
      Date.now() - local.at > 2_000
    ) {
      delete quickAddJobsLocal[id];
    }
  }
  scheduleListBadgeSync();
}

function ensureListQuickAddOnHost(
  host: Element,
  goodsId: string,
  pdpUrl: string | null,
  opts: {
    muted: boolean;
    phase: 'idle' | 'queued' | 'running' | 'error';
    errorMessage?: string;
  },
): void {
  host.classList.add(LIST_BADGE_HOST_CLASS);
  let button = host.querySelector(`.${LIST_QUICK_ADD_CLASS}`) as HTMLButtonElement | null;
  if (!button) {
    button = document.createElement('button');
    button.type = 'button';
    button.className = LIST_QUICK_ADD_CLASS;
    button.addEventListener('click', onQuickAddClick, true);
    host.appendChild(button);
  }
  button.dataset.goodsId = goodsId;
  if (pdpUrl) button.dataset.pdpUrl = pdpUrl;
  else delete button.dataset.pdpUrl;

  const loading = opts.phase === 'queued' || opts.phase === 'running';
  const label =
    opts.phase === 'queued'
      ? 'Trong hàng đợi...'
      : opts.phase === 'running'
        ? 'Đang thêm...'
        : opts.phase === 'error'
          ? opts.errorMessage || 'Thêm thất bại — thử lại'
          : opts.muted
            ? 'Đăng nhập / chọn shop để thêm nhanh'
            : 'Thêm nhanh';
  setQuickAddButtonVisual(button, {
    loading,
    queued: opts.phase === 'queued',
    muted: opts.muted && !loading,
    error: opts.phase === 'error',
    label,
  });
}

function removeListQuickAddFromHost(host: Element): void {
  const btn = host.querySelector(`.${LIST_QUICK_ADD_CLASS}`);
  if (btn) btn.remove();
  if (!host.querySelector(`.${LIST_BADGE_CLASS}`)) {
    host.classList.remove(LIST_BADGE_HOST_CLASS);
  }
}

function syncListSavedBadges(
  map: AddedProductShopMap,
  shopIds: number[],
): void {
  if (!document.body || extensionDead) return;
  if (!listBadgesEnabled) {
    clearListSavedBadges();
    stopListBadgeObserver();
    return;
  }

  ensureStyle();
  const canQuickAdd = sessionReady && shopsSelected && shopIds.length > 0;
  const cards = querySheinListCards();
  for (const card of cards) {
    const goodsId = extractSheinGoodsIdFromCard(card);
    if (!goodsId) continue;
    const host = findListCardOverlayHost(card);
    const key = makeAddedProductKey('shein', goodsId);
    const { status } = getAddedStatusForKey(key, map, shopIds.length > 0 ? shopIds : lastSelectedShopIdsForList);
    const pdpUrl = extractSheinPdpHrefFromCard(card);

    if (status === 'partial' || status === 'full') {
      ensureListBadgeOnHost(host, status);
    } else {
      removeListBadgeFromHost(host);
    }

    if (status !== 'full') {
      const job = quickAddJobsLocal[goodsId];
      let phase: 'idle' | 'queued' | 'running' | 'error' = 'idle';
      if (job?.status === 'queued') phase = 'queued';
      else if (job?.status === 'running') phase = 'running';
      else if (
        job?.status === 'failed' &&
        Date.now() - job.at < QUICK_ADD_FAIL_FLASH_MS
      ) {
        phase = 'error';
      }
      ensureListQuickAddOnHost(host, goodsId, pdpUrl, {
        muted: !canQuickAdd && phase === 'idle',
        phase,
        errorMessage: job?.message,
      });
    } else {
      removeListQuickAddFromHost(host);
    }
  }

  ensureListBadgeObserver();
}

function scheduleListBadgeSync(): void {
  window.clearTimeout(listBadgeSyncTimer);
  listBadgeSyncTimer = window.setTimeout(() => {
    if (!listBadgesEnabled || extensionDead || !isExtensionAlive()) return;
    syncListSavedBadges(lastAddedMapForList, lastSelectedShopIdsForList);
  }, 200);
}

function ensureListBadgeObserver(): void {
  if (listBadgeObserver || !document.body || !listBadgesEnabled) return;
  listBadgeObserver = new MutationObserver((mutations) => {
    if (!listBadgesEnabled || extensionDead) return;
    const relevant = mutations.some((m) => {
      if (m.type !== 'childList') return false;
      for (const node of m.addedNodes) {
        if (isExtensionOwnedNode(node)) continue;
        if (node.nodeType !== Node.ELEMENT_NODE) continue;
        const el = node as Element;
        if (
          el.matches?.(SHEIN_LIST_CARD_SELECTOR) ||
          el.querySelector?.(SHEIN_LIST_CARD_SELECTOR) ||
          isSheinListRootMutation(el) ||
          !!el.closest?.('#product-list-v2, #store-panel-items') ||
          el.querySelector?.(SHEIN_LIST_ROOT_SELECTOR)
        ) {
          return true;
        }
      }
      return false;
    });
    if (relevant) scheduleListBadgeSync();
  });
  listBadgeObserver.observe(document.body, { childList: true, subtree: true });
}

/** Host Shein + có ProductSource — badge trên search list và block recommend PDP. */
function detectSheinSavedBadgeContext(sources: ProductSourceOption[]): boolean {
  const source = findSource(sources);
  if (!source) return false;
  let parsed: URL;
  try {
    parsed = new URL(location.href);
  } catch {
    return false;
  }
  return resolveFlow(parsed.hostname, source) === 'shein';
}

function showToast(text: string, ok: boolean): void {
  const toast = document.createElement('div');
  toast.textContent = text;
  Object.assign(toast.style, {
    position: 'fixed',
    zIndex: '2147483647',
    right: '16px',
    bottom: '76px',
    maxWidth: '320px',
    padding: '8px 12px',
    borderRadius: '6px',
    color: '#fff',
    fontSize: '12px',
    fontFamily: "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
    boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
    background: ok ? '#52c41a' : '#ff4d4f',
  } as CSSStyleDeclaration);
  document.body.appendChild(toast);
  setTimeout(() => toast.remove(), 5000);
}

/**
 * Chờ DOM settle ngắn sau load (SPA paint).
 * Không chờ “hoàn toàn yên” — SHEIN/Temu mutate liên tục (carousel/ads) nên max timeout
 * cũ 4s khiến nút Thêm kẹt spinner dù thanh trình duyệt đã dừng.
 */
function settleDom(quietMs = 180, maxMs = 700): Promise<void> {
  return new Promise((resolve) => {
    let quietTimer = 0;
    let settled = false;
    const done = () => {
      if (settled) return;
      settled = true;
      obs.disconnect();
      window.clearTimeout(quietTimer);
      window.clearTimeout(maxTimer);
      resolve();
    };

    const maxTimer = window.setTimeout(done, maxMs);
    const obs = new MutationObserver((mutations) => {
      const noisy = mutations.some((m) => {
        const node = m.target;
        const el =
          node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement;
        if (!el) return false;
        // Bỏ mutation do nút/style/badge extension — tránh tự reset timer.
        if (el.id === BUTTON_ID || el.id === STYLE_ID) return false;
        if (typeof el.closest === 'function') {
          if (el.closest(`#${BUTTON_ID}`)) return false;
          if (el.closest(`.${LIST_BADGE_CLASS}, .${LIST_QUICK_ADD_CLASS}`)) return false;
        }
        if (
          el.classList?.contains(LIST_BADGE_CLASS) ||
          el.classList?.contains(LIST_BADGE_HOST_CLASS) ||
          el.classList?.contains(LIST_QUICK_ADD_CLASS)
        ) {
          return false;
        }
        return true;
      });
      if (!noisy) return;
      window.clearTimeout(quietTimer);
      quietTimer = window.setTimeout(done, quietMs);
    });

    // Chỉ childList — attributes/characterData quá ồn trên SPA.
    obs.observe(document.documentElement, { childList: true, subtree: true });
    quietTimer = window.setTimeout(done, quietMs);
  });
}

async function whenPageReady(): Promise<void> {
  if (document.readyState !== 'complete') {
    await new Promise<void>((resolve) => {
      window.addEventListener('load', () => resolve(), { once: true });
    });
  }
  await settleDom();
}

async function captureHtml(): Promise<string> {
  // Ưu tiên DOM đã render (SPA SHEIN/Temu…). Bỏ nút extension khỏi HTML gửi lên.
  const clone = document.documentElement.cloneNode(true) as HTMLElement;
  clone.querySelector('#kbt-add-product-btn')?.remove();
  clone.querySelector('#kbt-upload-size-chart-btn')?.remove();
  clone.querySelector('#kbt-upload-size-chart-input')?.remove();
  clone.querySelector('#kbt-size-chart-warn')?.remove();
  clone.querySelector('#kbt-add-product-style')?.remove();
  clone.querySelectorAll(`.${LIST_BADGE_CLASS}, .${LIST_QUICK_ADD_CLASS}`).forEach((el) => el.remove());
  clone.querySelectorAll(`.${LIST_BADGE_HOST_CLASS}`).forEach((el) => {
    el.classList.remove(LIST_BADGE_HOST_CLASS);
  });
  const rendered = clone.outerHTML;
  if (rendered && rendered.length > 500) return rendered;

  try {
    const res = await fetch(location.href, { credentials: 'include' });
    if (res.ok) {
      const text = await res.text();
      if (text && text.length > 200) return text;
    }
  } catch {
    // ignore
  }
  return document.documentElement.outerHTML;
}

interface SheinPdpCache {
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
}

const SHEIN_PDP_STORAGE_KEY = 'kbt_shein_pdp_cache';
const SHEIN_REVIEW_LEGACY_KEY = 'kbt_shein_review_cache';

function goodsIdFromLocation(): string | null {
  try {
    const u = new URL(location.href);
    const q = u.searchParams.get('goods_id');
    if (q?.trim()) return q.trim();
    const path = u.pathname.match(/-p-(\d+)/i);
    return path?.[1] ?? null;
  } catch {
    return null;
  }
}

function toAbsoluteUrl(v: unknown): string | null {
  const t = toOptionalText(v);
  if (!t) return null;
  if (t.startsWith('//')) return `https:${t}`;
  return t;
}

function toOptionalInt(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) {
    const n = Math.floor(v);
    return n < 0 ? null : n;
  }
  if (typeof v === 'string' && v.trim()) {
    const n = parseInt(v, 10);
    if (Number.isFinite(n) && n >= 0) return n;
  }
  return null;
}

function toOptionalFloat(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'string' && v.trim()) {
    const n = parseFloat(v);
    if (Number.isFinite(n)) return n;
  }
  return null;
}

function toOptionalText(v: unknown): string | null {
  if (typeof v === 'number' && Number.isFinite(v)) return String(v);
  if (typeof v !== 'string') return null;
  const t = v.trim();
  return t || null;
}

function toOptionalBool(v: unknown): boolean | null {
  if (typeof v === 'boolean') return v;
  if (v === 1 || v === '1' || v === 'true') return true;
  if (v === 0 || v === '0' || v === 'false') return false;
  return null;
}

/** "Sold by kcruab-kp" → "kcruab-kp" */
function parseStoreTitleFromSoldByTips(tips: string | null): string | null {
  if (!tips) return null;
  const m = tips.match(/sold\s+by\s*[:\-]?\s*(.+)/i);
  if (!m) return null;
  const name = m[1].replace(/^["'\s]+|["'\s]+$/g, '').trim();
  return name || null;
}

function normalizeSheinPdp(raw: Record<string, unknown>): SheinPdpCache | null {
  const pageId = goodsIdFromLocation();
  const cacheId = toOptionalText(raw.goodsId);
  // SPA: sessionStorage còn metric SP cũ — không gắn nhầm vào URL mới.
  if (pageId && cacheId && pageId !== cacheId) return null;

  let reviewCount = toOptionalInt(raw.reviewCount);
  let averageRating = toOptionalFloat(raw.averageRating);
  if (averageRating != null && (averageRating < 0 || averageRating > 5)) {
    averageRating = null;
  }
  const stock = toOptionalInt(raw.stock);
  const shippingDays = toOptionalInt(raw.shippingDays);
  let shippingDayPercent = toOptionalFloat(raw.shippingDayPercent);
  if (shippingDayPercent != null && (shippingDayPercent < 0 || shippingDayPercent > 100)) {
    shippingDayPercent = null;
  }
  let shopRank = toOptionalFloat(raw.shopRank);
  if (shopRank != null && shopRank < 0) shopRank = null;
  const soldByTips = toOptionalText(raw.soldByTips);
  const cache: SheinPdpCache = {
    reviewCount,
    averageRating,
    stock,
    storeCode: toOptionalText(raw.storeCode),
    storeLogo: toAbsoluteUrl(raw.storeLogo),
    storeTitle: toOptionalText(raw.storeTitle) ?? parseStoreTitleFromSoldByTips(soldByTips),
    soldByTips,
    shipsFromTips: toOptionalText(raw.shipsFromTips),
    shopRank,
    storeAttention: toOptionalInt(raw.storeAttention),
    shippingDays,
    shippingDayPercent,
    shippingDayPercentDesc: toOptionalText(raw.shippingDayPercentDesc),
    isLocalProduct: toOptionalBool(raw.isLocalProduct),
    isSupportQuickShip: toOptionalBool(raw.isSupportQuickShip),
  };
  if (
    cache.reviewCount == null &&
    cache.averageRating == null &&
    cache.stock == null &&
    !cache.storeCode &&
    !cache.storeTitle &&
    !cache.soldByTips &&
    cache.shopRank == null &&
    cache.storeAttention == null &&
    cache.shippingDays == null &&
    cache.shippingDayPercent == null &&
    cache.isLocalProduct == null &&
    cache.isSupportQuickShip == null
  ) {
    return null;
  }
  return cache;
}

function readSheinPdpFromStorage(): SheinPdpCache | null {
  try {
    const raw =
      sessionStorage.getItem(SHEIN_PDP_STORAGE_KEY) ||
      sessionStorage.getItem(SHEIN_REVIEW_LEGACY_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object') return null;
    const obj = parsed as Record<string, unknown>;
    const byId = obj.byId;
    if (byId && typeof byId === 'object') {
      const map = byId as Record<string, unknown>;
      const pageId = goodsIdFromLocation();
      const pick =
        (pageId && map[pageId] && typeof map[pageId] === 'object'
          ? (map[pageId] as Record<string, unknown>)
          : null) ||
        (!pageId && typeof obj.latestId === 'string' && map[obj.latestId] && typeof map[obj.latestId] === 'object'
          ? (map[obj.latestId] as Record<string, unknown>)
          : null);
      return pick ? normalizeSheinPdp(pick) : null;
    }
    return normalizeSheinPdp(obj);
  } catch {
    return null;
  }
}

async function waitForSheinPdpCache(timeoutMs = 3500): Promise<SheinPdpCache | null> {
  if (!/shein\.com/i.test(location.hostname)) return null;
  const deadline = Date.now() + timeoutMs;
  let cached = readSheinPdpFromStorage();
  if (cached) return cached;
  while (Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 100));
    cached = readSheinPdpFromStorage();
    if (cached) return cached;
  }
  return readSheinPdpFromStorage();
}

function normalizeSheinSizeGuide(raw: Record<string, unknown>): SheinSizeGuide | null {
  if (!isSheinSizeGuide(raw)) return null;
  const pageId = goodsIdFromLocation();
  const cacheId = toOptionalText(raw.goodsId);
  if (pageId && cacheId && pageId !== cacheId) return null;
  return raw;
}

function readSheinSizeGuideFromStorage(): SheinSizeGuide | null {
  try {
    const raw = sessionStorage.getItem(SHEIN_SIZE_GUIDE_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object') return null;
    const obj = parsed as Record<string, unknown>;
    const byId = obj.byId;
    if (byId && typeof byId === 'object') {
      const map = byId as Record<string, unknown>;
      const pageId = goodsIdFromLocation();
      const pick =
        (pageId && map[pageId] && typeof map[pageId] === 'object'
          ? (map[pageId] as Record<string, unknown>)
          : null) ||
        (!pageId &&
        typeof obj.latestId === 'string' &&
        map[obj.latestId] &&
        typeof map[obj.latestId] === 'object'
          ? (map[obj.latestId] as Record<string, unknown>)
          : null);
      return pick ? normalizeSheinSizeGuide(pick) : null;
    }
    return normalizeSheinSizeGuide(obj);
  } catch {
    return null;
  }
}

async function waitForSheinSizeGuideCache(timeoutMs = SHEIN_SIZE_GUIDE_CACHE_WAIT_MS): Promise<SheinSizeGuide | null> {
  if (!/shein\.com/i.test(location.hostname)) return null;
  const deadline = Date.now() + timeoutMs;
  let cached = readSheinSizeGuideFromStorage();
  if (cached) return cached;
  while (Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 100));
    cached = readSheinSizeGuideFromStorage();
    if (cached) return cached;
  }
  return readSheinSizeGuideFromStorage();
}

function fetchSheinSizeGuideViaMainWorld(goodsId: string): Promise<SheinSizeGuide | null> {
  const id = `kbt-sg-fetch-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  return new Promise((resolve) => {
    const timer = window.setTimeout(() => {
      window.removeEventListener('message', onMessage);
      resolve(null);
    }, SHEIN_SIZE_GUIDE_FETCH_TIMEOUT_MS);

    const onMessage = (e: MessageEvent) => {
      if (e.source !== window) return;
      const data = e.data as {
        source?: string;
        id?: string;
        ok?: boolean;
        guide?: unknown;
        json?: unknown;
      } | null;
      if (!data || data.source !== SHEIN_SIZE_GUIDE_FETCH_RESULT_MSG || data.id !== id) return;
      window.clearTimeout(timer);
      window.removeEventListener('message', onMessage);
      if (data.guide && isSheinSizeGuide(data.guide)) {
        resolve(data.guide);
        return;
      }
      if (data.json != null) {
        resolve(parseSheinSizeGuideResponse(data.json, goodsId));
        return;
      }
      resolve(null);
    };

    window.addEventListener('message', onMessage);
    try {
      window.postMessage(
        { source: SHEIN_SIZE_GUIDE_FETCH_MSG, id, goodsId },
        location.origin,
      );
    } catch {
      window.clearTimeout(timer);
      window.removeEventListener('message', onMessage);
      resolve(null);
    }
  });
}

async function resolveSheinSizeGuide(): Promise<SheinSizeGuide | null> {
  if (!/shein\.com/i.test(location.hostname)) return null;
  const goodsId = goodsIdFromLocation();
  if (!goodsId) return null;

  const cached = await waitForSheinSizeGuideCache();
  if (cached) return cached;

  return fetchSheinSizeGuideViaMainWorld(goodsId);
}

function readSheinSearchCapture(): {
  keywords: string;
  page: number;
  total: number;
  products: unknown[];
  facets: unknown[];
  at: number;
} | null {
  try {
    const raw = sessionStorage.getItem(SHEIN_SEARCH_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed.products)) return null;
    return {
      keywords: String(parsed.keywords ?? ''),
      page: Number(parsed.page) || 1,
      total: Number(parsed.total) || parsed.products.length,
      products: parsed.products,
      facets: Array.isArray(parsed.facets) ? parsed.facets : [],
      at: Number(parsed.at) || Date.now(),
    };
  } catch {
    return null;
  }
}

function forwardSheinSearchCapture(capture: NonNullable<ReturnType<typeof readSheinSearchCapture>>): void {
  if (!isExtensionAlive()) return;
  try {
    void chrome.runtime.sendMessage({ type: 'SHEIN_SEARCH_CAPTURE', capture });
  } catch {
    /* ignore */
  }
}

function readSheinSearchSession(): {
  templateUrl: string;
  method?: string;
  headers: Record<string, string>;
  body?: string | null;
  at: number;
} | null {
  try {
    const raw = sessionStorage.getItem(SHEIN_SEARCH_SESSION_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    if (!parsed || typeof parsed !== 'object') return null;
    const templateUrl = String(parsed.templateUrl ?? '').trim();
    if (!templateUrl || !/get_products_by_keywords/i.test(templateUrl)) return null;
    const headers =
      parsed.headers && typeof parsed.headers === 'object' && !Array.isArray(parsed.headers)
        ? (parsed.headers as Record<string, string>)
        : {};
    const body = parsed.body != null ? String(parsed.body) : '';
    return {
      templateUrl,
      method: parsed.method != null ? String(parsed.method) : 'POST',
      headers,
      body: body || null,
      at: Number(parsed.at) || Date.now(),
    };
  } catch {
    return null;
  }
}

function forwardSheinSearchSession(session: NonNullable<ReturnType<typeof readSheinSearchSession>>): void {
  if (!isExtensionAlive()) return;
  try {
    void chrome.runtime.sendMessage({ type: 'SHEIN_SEARCH_SESSION', session });
  } catch {
    /* ignore */
  }
}

function resetSheinSearchSession(): void {
  try {
    sessionStorage.removeItem(SHEIN_SEARCH_SESSION_KEY);
    sessionStorage.removeItem(SHEIN_SEARCH_STORAGE_KEY);
  } catch {
    /* ignore */
  }
  try {
    window.postMessage({ source: SHEIN_SEARCH_RESET_MSG }, location.origin);
  } catch {
    /* ignore */
  }
}

function isSheinPage(): boolean {
  const host = location.hostname.replace(/^www\./i, '').toLowerCase();
  return host === 'shein.com' || host.endsWith('.shein.com');
}

/**
 * Chỉ reset store search của extension theo tab (session + capture đã hớt).
 * KHÔNG `sessionStorage.clear()`: nó xoá luôn cache state của chính Shein, buộc SPA
 * re-init toàn bộ mỗi lượt tìm — vừa chậm vừa đáng ngờ với WAF. Chỉ gỡ 2 key của mình,
 * đúng như `resetSheinSearchSession`. localStorage/IndexedDB/Cache Storage/SW là
 * origin-wide nên cũng không đụng. Cookie đăng nhập giữ nguyên.
 */
async function clearSheinPageStorage(): Promise<void> {
  if (!isSheinPage()) return;
  resetSheinSearchSession();
}

function contentSleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

function safeDecode(raw: string): string {
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

/** So câu tìm bỏ qua dấu/hoa thường: path pdsearch có thể dùng `-`, `+` hay `%20`. */
function loosePhrase(raw: string): string {
  return String(raw || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

function isNodeVisible(el: Element): boolean {
  const rect = (el as HTMLElement).getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return false;
  const style = window.getComputedStyle(el as HTMLElement);
  return style.visibility !== 'hidden' && style.display !== 'none';
}

function isNodeDisabled(el: Element): boolean {
  if ((el as HTMLButtonElement).disabled) return true;
  if (el.getAttribute('aria-disabled') === 'true') return true;
  return /disabled/i.test(el.className || '');
}

/** Thanh phân trang pdsearch — Shein đổi class theo phiên bản nên dò nhiều lớp. */
function findSheinPaginationRoot(): HTMLElement | null {
  const selectors = [
    '.sui-pagination',
    '[class*="sui-pagination"]',
    '[class*="sui_pagination"]',
    '[class*="pagination"]',
    'nav[aria-label*="agina"]',
  ];
  for (const selector of selectors) {
    const nodes = Array.from(document.querySelectorAll<HTMLElement>(selector));
    const hit = nodes.find((node) => isNodeVisible(node) && /\d/.test(node.textContent || ''));
    if (hit) return hit;
  }
  return null;
}

/**
 * Phân trang render ở cuối list nên có thể chưa mount — cuộn xuống cho SPA dựng rồi dò lại.
 * Cuộn cũng là hành vi người thật trước khi bấm sang trang.
 */
async function ensureSheinPaginationMounted(timeoutMs = 8000): Promise<HTMLElement | null> {
  let root = findSheinPaginationRoot();
  if (root) return root;
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });
    await contentSleep(400);
    root = findSheinPaginationRoot();
    if (root) return root;
  }
  return null;
}

/** Node sâu nhất có đúng text số trang — SPA gắn handler ở `a`/`li`, click bubble lên cả hai. */
function findSheinPageButton(root: HTMLElement, page: number): HTMLElement | null {
  const want = String(page);
  const matches = Array.from(root.querySelectorAll<HTMLElement>('li,a,button,span,div')).filter(
    (el) => isNodeVisible(el) && !isNodeDisabled(el) && (el.textContent || '').trim() === want,
  );
  return matches.find((el) => !matches.some((other) => other !== el && el.contains(other))) ?? null;
}

/** Nút "sang trang sau" — dùng khi không thấy đúng số trang (Shein ẩn số khi list dài). */
function findSheinNextButton(root: HTMLElement): HTMLElement | null {
  const nodes = Array.from(
    root.querySelectorAll<HTMLElement>('[class*="next"],[class*="Next"],[aria-label*="ext"]'),
  );
  return nodes.find((el) => isNodeVisible(el) && !isNodeDisabled(el)) ?? null;
}

/** Chuỗi sự kiện như chuột thật: SPA nào bắt mousedown hay click đều nhận được. */
function fireSheinClick(el: HTMLElement): void {
  try {
    el.scrollIntoView({ block: 'center' });
  } catch {
    /* ignore */
  }
  const init: MouseEventInit = { bubbles: true, cancelable: true, composed: true, view: window };
  try {
    el.dispatchEvent(new PointerEvent('pointerdown', init));
  } catch {
    /* PointerEvent có thể không dựng được */
  }
  el.dispatchEvent(new MouseEvent('mousedown', init));
  try {
    el.dispatchEvent(new PointerEvent('pointerup', init));
  } catch {
    /* ignore */
  }
  el.dispatchEvent(new MouseEvent('mouseup', init));
  el.click();
}

/**
 * Sang trang bằng chính thanh phân trang của SPA — nội dung đổi, **không** load lại trang.
 * Request phân trang do Shein tự phát nên đã ký đầy đủ; extension chỉ hứng JSON.
 */
async function gotoSheinSearchPage(page: number, query: string): Promise<{ ok: boolean; message?: string }> {
  const target = Math.max(1, Math.floor(Number(page) || 0));
  if (target < 1) return { ok: false, message: 'Số trang không hợp lệ.' };
  const path = /\/pdsearch\/([^/]+)/i.exec(location.pathname);
  if (!path) return { ok: false, message: 'Tab không ở trang pdsearch.' };
  // Tab có thể đã trôi sang câu tìm khác (keyword trước, /risk/ rồi back…). Chặn ở đây
  // để rơi sang điều hướng URL ngay, khỏi bấm bừa rồi chờ hết timeout hớt JSON.
  if (loosePhrase(safeDecode(path[1])) !== loosePhrase(query)) {
    return { ok: false, message: 'Tab đang ở câu tìm khác.' };
  }
  const root = await ensureSheinPaginationMounted();
  if (!root) return { ok: false, message: 'Không thấy thanh phân trang.' };

  const exact = findSheinPageButton(root, target);
  if (exact) {
    fireSheinClick(exact);
    return { ok: true };
  }
  const currentPage = Number(new URL(location.href).searchParams.get('page') || '1') || 1;
  if (target === currentPage + 1) {
    const next = findSheinNextButton(root);
    if (next) {
      fireSheinClick(next);
      return { ok: true };
    }
  }
  return { ok: false, message: `Không thấy nút trang ${target}.` };
}

/** Đổi `?page=` trên pdsearch đang mở — không load, không `popstate`. */
function syncSheinSearchDocumentUrl(page?: number): void {
  const n = Math.max(1, Math.floor(Number(page) || 0));
  if (n < 1) return;
  try {
    if (!/\/pdsearch\//i.test(location.pathname)) return;
    const next = new URL(location.href);
    const current = next.searchParams.get('page') || '1';
    if (String(n) === current) return;
    next.searchParams.set('page', String(n));
    history.replaceState(history.state, '', `${next.pathname}${next.search}${next.hash}`);
  } catch {
    /* ignore */
  }
}

function interpretSheinSearchHttp(status: number, json: unknown): FetchSheinSearchResponse {
  if (status < 200 || status >= 300) {
    return {
      ok: false,
      status,
      json,
      message: `SHEIN HTTP ${status}`,
    };
  }
  if (json && typeof json === 'object' && 'code' in json) {
    const code = String((json as { code?: unknown }).code ?? '');
    if (code && code !== '0' && code !== '200' && code.toLowerCase() !== 'success') {
      const info = json as Record<string, unknown>;
      const products =
        info.info && typeof info.info === 'object'
          ? (info.info as Record<string, unknown>).products
          : info.products;
      if (!Array.isArray(products) || products.length === 0) {
        const msg = `${(json as { msg?: unknown; tips?: unknown }).msg ?? (json as { tips?: unknown }).tips ?? ''}`.trim();
        if (/risk|captcha|login|illegal|401|403/i.test(`${code} ${msg}`)) {
          return {
            ok: false,
            status,
            json,
            message: `SHEIN code ${code}${msg ? `: ${msg}` : ''}`,
          };
        }
        return { ok: true, status, json };
      }
    }
  }
  return { ok: true, status, json };
}

function fetchSheinSearchViaMainWorld(opts: {
  url: string;
  method: string;
  body?: string;
  page?: number;
}): Promise<FetchSheinSearchResponse | null> {
  const id = `kbt-shein-fetch-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  return new Promise((resolve) => {
    const timer = window.setTimeout(() => {
      window.removeEventListener('message', onMessage);
      resolve(null);
    }, SHEIN_SEARCH_FETCH_TIMEOUT_MS + 2000);

    const onMessage = (e: MessageEvent) => {
      if (e.source !== window) return;
      const data = e.data as {
        source?: string;
        id?: string;
        ok?: boolean;
        status?: number;
        json?: unknown;
        message?: string;
      } | null;
      if (!data || data.source !== SHEIN_SEARCH_FETCH_RESULT_MSG || data.id !== id) return;
      window.clearTimeout(timer);
      window.removeEventListener('message', onMessage);
      if (typeof data.status === 'number') {
        resolve(interpretSheinSearchHttp(data.status, data.json ?? null));
        return;
      }
      resolve({
        ok: !!data.ok,
        json: data.json,
        message: data.message,
      });
    };

    window.addEventListener('message', onMessage);
    try {
      window.postMessage(
        {
          source: SHEIN_SEARCH_FETCH_MSG,
          id,
          url: opts.url,
          method: opts.method,
          body: opts.body,
          page: opts.page,
        },
        location.origin,
      );
    } catch {
      window.clearTimeout(timer);
      window.removeEventListener('message', onMessage);
      resolve(null);
    }
  });
}

async function fetchSheinSearchApi(
  url: string,
  extraHeaders?: Record<string, string>,
  methodRaw?: string,
  body?: string,
  page?: number,
): Promise<FetchSheinSearchResponse> {
  const host = location.hostname.replace(/^www\./i, '');
  if (!/(^|\.)shein\.com$/i.test(host)) {
    return { ok: false, message: 'Tab hiện tại không phải Shein.' };
  }
  syncSheinSearchDocumentUrl(page);
  const session = readSheinSearchSession();
  const method = String(methodRaw || session?.method || 'POST').toUpperCase() === 'GET' ? 'GET' : 'POST';
  const payload = body != null && body !== '' ? body : undefined;

  const viaMain = await fetchSheinSearchViaMainWorld({
    url,
    method,
    body: payload,
    page,
  });
  if (viaMain) return viaMain;

  // Isolated fallback: Referer đã khớp URL sau replaceState; header vẫn bản hớt trang 1.
  const headers: Record<string, string> = {
    accept: 'application/json, text/plain, */*',
    'x-requested-with': 'XMLHttpRequest',
  };
  const merged = { ...(session?.headers || {}), ...(extraHeaders || {}) };
  for (const [rawKey, rawVal] of Object.entries(merged)) {
    if (rawVal == null || rawVal === '') continue;
    const name = rawKey.toLowerCase();
    if (SHEIN_SEARCH_HEADER_DENY.has(name) || name === 'content-type' || name === 'content-length') continue;
    headers[name] = String(rawVal);
  }
  if (payload) {
    const capturedCt = Object.entries(merged).find(([key]) => key.toLowerCase() === 'content-type')?.[1];
    headers['content-type'] = String(capturedCt || 'application/json;charset=UTF-8');
  }

  const abort = new AbortController();
  const timer = window.setTimeout(() => abort.abort(), SHEIN_SEARCH_FETCH_TIMEOUT_MS);
  try {
    const res = await fetch(url, {
      method,
      credentials: 'include',
      headers,
      body: method === 'GET' ? undefined : payload,
      signal: abort.signal,
    });
    const text = await res.text();
    let json: unknown = null;
    try {
      json = JSON.parse(text);
    } catch {
      json = null;
    }
    return interpretSheinSearchHttp(res.status, json);
  } catch (err) {
    if (abort.signal.aborted) {
      return {
        ok: false,
        message: `SHEIN search quá ${Math.round(SHEIN_SEARCH_FETCH_TIMEOUT_MS / 1000)}s không trả lời`,
      };
    }
    return { ok: false, message: (err as Error).message || 'Không gọi được API Shein.' };
  } finally {
    window.clearTimeout(timer);
  }
}

function sheinPdpMessageFields(cache: SheinPdpCache | null, platform?: string | null) {
  return {
    platform: platform || undefined,
    reviewCount: cache?.reviewCount ?? undefined,
    averageRating: cache?.averageRating ?? undefined,
    stock: cache?.stock ?? undefined,
    storeCode: cache?.storeCode ?? undefined,
    storeLogo: cache?.storeLogo ?? undefined,
    storeTitle: cache?.storeTitle ?? undefined,
    soldByTips: cache?.soldByTips ?? undefined,
    shipsFromTips: cache?.shipsFromTips ?? undefined,
    shopRank: cache?.shopRank ?? undefined,
    storeAttention: cache?.storeAttention ?? undefined,
    shippingDays: cache?.shippingDays ?? undefined,
    shippingDayPercent: cache?.shippingDayPercent ?? undefined,
    shippingDayPercentDesc: cache?.shippingDayPercentDesc ?? undefined,
    isLocalProduct: cache?.isLocalProduct ?? undefined,
    isSupportQuickShip: cache?.isSupportQuickShip ?? undefined,
  };
}

function setButtonVisual(
  button: HTMLButtonElement,
  opts: {
    enabled: boolean;
    loading?: boolean;
    done?: boolean;
    partial?: boolean;
    muted?: boolean;
    /** Chỉ dùng cho title / a11y — FAB tròn không hiện chữ. */
    label: string;
  },
): void {
  const loading = !!opts.loading;
  const done = !!opts.done && !loading;
  const partial = !!opts.partial && !loading && !done;
  const muted = !!opts.muted && !loading && !done && !partial;
  const pulse = opts.enabled && !loading && !done && !muted;

  button.disabled = (!opts.enabled && !muted && !partial) || loading || done;
  button.style.opacity = '';
  button.style.cursor = '';
  button.style.background = '';
  button.style.boxShadow = '';

  button.classList.toggle('kbt-ext-fab--pulse', pulse || muted || partial);
  button.classList.toggle('kbt-ext-fab--muted', muted);
  button.classList.toggle('kbt-ext-fab--done', done);
  button.classList.toggle('kbt-ext-fab--partial', partial);
  button.classList.toggle('kbt-ext-fab--loading', loading);

  if (loading) {
    const spinner = document.createElement('span');
    spinner.className = 'kbt-ext-spinner';
    button.replaceChildren(spinner);
  } else if (done || partial) {
    button.replaceChildren(createCheckIcon());
  } else {
    button.replaceChildren(createAddIcon());
  }

  button.setAttribute('aria-label', opts.label);
}

function canActivate(): boolean {
  return (
    sessionReady &&
    shopsSelected &&
    pageReady &&
    isProductPage &&
    !!currentSource &&
    !alreadyAdded &&
    !isAdding
  );
}

async function ensureMyTabId(): Promise<number | null> {
  if (myTabId !== null) return myTabId;
  try {
    const res = (await chrome.runtime.sendMessage({ type: 'WHOAMI' })) as {
      tabId?: number;
    };
    if (typeof res?.tabId === 'number') myTabId = res.tabId;
  } catch (err) {
    if (isContextInvalidatedError(err)) markExtensionDead();
  }
  return myTabId;
}

function readAddingForTab(
  raw: unknown,
  tabId: number | null,
): boolean {
  if (tabId === null || !raw || typeof raw !== 'object') return false;
  return !!(raw as Record<string, unknown>)[String(tabId)];
}

async function openSidePanel(): Promise<void> {
  try {
    await chrome.runtime.sendMessage({ type: 'OPEN_SIDE_PANEL' });
  } catch (err) {
    if (isContextInvalidatedError(err)) markExtensionDead();
  }
}

async function onAddClick(button: HTMLButtonElement): Promise<void> {
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  if (!sessionReady || !shopsSelected) {
    await openSidePanel();
    showToast(
      !sessionReady
        ? 'Mở side panel để đăng nhập KBT Craper / kiểm tra phiên 4Seller.'
        : 'Chọn ít nhất 1 shop trong side panel trước khi thêm.',
      false,
    );
    return;
  }
  if (!canActivate() || button.disabled) return;

  addClickInFlight = true;
  isAdding = true;
  setButtonVisual(button, {
    enabled: false,
    loading: true,
    label: 'Đang gửi...',
  });

  try {
    const html = await captureHtml();
    const [pdp, sizeGuide] = await Promise.all([waitForSheinPdpCache(), resolveSheinSizeGuide()]);
    const res = (await chrome.runtime.sendMessage({
      type: 'ADD_PRODUCT',
      url: location.href,
      html,
      ...sheinPdpMessageFields(pdp, currentSource?.code),
      sizeGuide: sizeGuide ?? undefined,
    })) as AddProductResponse;
    if (res.ok) {
      alreadyAdded = true;
      sizeChartUploadEnabled = true;
      await applySizeChartLeaveGuard(
        'Thêm sản phẩm thành công — hãy upload Size Chart (nút tím) trước khi đóng tab.',
      );
    } else {
      showToast(res.message || 'Thêm sản phẩm thất bại.', false);
    }
  } catch (err) {
    if (isContextInvalidatedError(err)) {
      markExtensionDead();
      showToast('Extension đã cập nhật — tải lại trang (F5) để tiếp tục.', false);
      return;
    }
    showToast(`Lỗi: ${(err as Error).message}`, false);
  } finally {
    addClickInFlight = false;
    isAdding = false;
    if (!extensionDead) await evaluate();
  }
}

function ensureButton(_source: ProductSourceOption): HTMLButtonElement {
  ensureStyle();
  let button = document.getElementById(BUTTON_ID) as HTMLButtonElement | null;
  if (!button) {
    button = document.createElement('button');
    button.id = BUTTON_ID;
    button.type = 'button';
    button.className = 'kbt-ext-fab';
    button.title = 'Thêm sản phẩm này';
    button.setAttribute('aria-label', 'Thêm sản phẩm này');

    button.addEventListener('click', () => {
      if (!isExtensionAlive() || !currentSource) {
        markExtensionDead();
        return;
      }
      void onAddClick(button!);
    });
    document.body.appendChild(button);
  }

  // Góc trên-phải; gỡ inline cũ (pill) nếu còn từ bản trước.
  button.style.top = '120px';
  button.style.right = '24px';
  button.style.bottom = '';
  button.style.left = '';
  button.style.width = '';
  button.style.height = '';
  button.style.padding = '';
  button.style.gap = '';
  button.style.borderRadius = '';
  button.style.background = '';
  button.style.boxShadow = '';
  button.style.opacity = '';
  button.style.cursor = '';
  button.style.display = '';
  return button;
}

function syncButton(): void {
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  // Hiện trên PDP khi đã có nguồn cấu hình (kể cả chưa đăng nhập — hướng user mở panel).
  if (!isProductPage || !currentSource || !document.body) {
    removeButton();
    void publishTabStatus(myTabId);
    return;
  }

  const button = ensureButton(currentSource);
  const active = canActivate();
  let label = 'Thêm sản phẩm này';
  let title = 'Thêm sản phẩm này';
  let muted = false;

  if (!pageReady) {
    label = 'Đang tải trang...';
    title = 'Đang chờ trang tải xong...';
  } else if (isAdding) {
    label = 'Đang gửi...';
    title = 'Đang thêm sản phẩm...';
  } else if (!sessionReady) {
    label = 'Mở KBT Craper';
    title = 'Đăng nhập / kiểm tra phiên trong side panel';
    muted = true;
  } else if (!shopsSelected) {
    label = 'Chọn shop';
    title = 'Chọn ít nhất 1 shop trong side panel';
    muted = true;
  } else if (alreadyAdded) {
    label = 'Đã thêm';
    title = 'Sản phẩm này đã được thêm cho mọi shop đang chọn';
  } else if (addedStatus === 'partial') {
    label = `Thiếu shop (${addedShopHit}/${addedShopTotal})`;
    title = `Đã có ở ${addedShopHit}/${addedShopTotal} shop — bấm để thêm shop còn thiếu`;
  }

  setButtonVisual(button, {
    enabled: active || muted || addedStatus === 'partial',
    loading: !pageReady || isAdding,
    done: alreadyAdded && !isAdding,
    partial: addedStatus === 'partial' && !isAdding,
    muted,
    label,
  });
  button.title = title;
  syncSizeChartUploadButton();
}

function sizeChartFileIconSvg(): string {
  return `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-1 2 5 5h-5V4zM8 13h8v2H8v-2zm0 4h8v2H8v-2z"/></svg>`;
}

type SizeChartUploadedMap = Record<string, number>;

function normalizeSizeChartUploadedMap(raw: unknown): SizeChartUploadedMap {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: SizeChartUploadedMap = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof key !== 'string' || !key.includes(':')) continue;
    const at = typeof value === 'number' ? value : Number(value);
    if (Number.isFinite(at) && at > 0) out[key] = at;
  }
  return out;
}

/** Quick-add ghi `reopenOnClose: false`. Thiếu field = FAB/panel (mở lại khi đóng). */
function readPendingReopenOnClose(raw: unknown, productKey: string): boolean {
  if (!productKey || !raw || typeof raw !== 'object' || Array.isArray(raw)) return true;
  const row = (raw as Record<string, unknown>)[productKey];
  if (!row || typeof row !== 'object' || Array.isArray(row)) return true;
  return (row as { reopenOnClose?: unknown }).reopenOnClose !== false;
}

async function markSizeChartUploadedForKey(productKey: string): Promise<void> {
  if (!productKey) return;
  try {
    const raw = await chrome.storage.local.get([SIZE_CHART_UPLOADED_KEY, SIZE_CHART_PENDING_KEY]);
    const prev = normalizeSizeChartUploadedMap(raw[SIZE_CHART_UPLOADED_KEY]);
    prev[productKey] = Date.now();
    const pending =
      raw[SIZE_CHART_PENDING_KEY] &&
      typeof raw[SIZE_CHART_PENDING_KEY] === 'object' &&
      !Array.isArray(raw[SIZE_CHART_PENDING_KEY])
        ? { ...(raw[SIZE_CHART_PENDING_KEY] as Record<string, unknown>) }
        : {};
    delete pending[productKey];
    await chrome.storage.local.set({
      [SIZE_CHART_UPLOADED_KEY]: prev,
      [SIZE_CHART_PENDING_KEY]: pending,
    });
  } catch (err) {
    if (isContextInvalidatedError(err)) markExtensionDead();
  }
}

/**
 * Cảnh báo khi đóng/điều hướng PDP đã thêm nhưng chưa upload Size Chart.
 * Chỉ gắn khi `sizeChartReopenOnClose` (FAB/panel). Quick-add không chặn đóng tab.
 */
function onSizeChartLeaveWarning(e: BeforeUnloadEvent): void {
  e.preventDefault();
  e.returnValue = 'Bạn chưa thêm file Size Chart';
}

function syncSizeChartLeaveWarning(): void {
  const shouldWarn =
    isProductPage &&
    sizeChartUploadEnabled &&
    !sizeChartUploaded &&
    sizeChartReopenOnClose;
  if (shouldWarn && !sizeChartLeaveWarningAttached) {
    window.addEventListener('beforeunload', onSizeChartLeaveWarning);
    sizeChartLeaveWarningAttached = true;
  } else if (!shouldWarn && sizeChartLeaveWarningAttached) {
    window.removeEventListener('beforeunload', onSizeChartLeaveWarning);
    sizeChartLeaveWarningAttached = false;
  }
}

async function applySizeChartLeaveGuard(
  toastMessage?: string,
  reopenOnClose = true,
): Promise<void> {
  sizeChartReopenOnClose = reopenOnClose;
  await evaluate();
  sizeChartReopenOnClose = reopenOnClose;
  if (isProductPage && !sizeChartUploaded) {
    sizeChartUploadEnabled = true;
    if (addedStatus === 'none') {
      // Map chưa kịp sync — vẫn bật FAB + guard
      addedStatus = 'full';
      alreadyAdded = true;
    }
    syncSizeChartUploadButton();
    syncSizeChartLeaveWarning();
    syncSizeChartWarnBanner();
  }
  if (toastMessage) showToast(toastMessage, false);
}

const SIZE_CHART_WARN_TEXT = 'Chưa thêm Size Chart';

function sizeChartWarnTitle(): string {
  return sizeChartReopenOnClose
    ? 'Chưa thêm file Size Chart — bấm để upload. Đóng tab sẽ mở lại.'
    : 'Chưa thêm file Size Chart — bấm để upload.';
}

function ensureSizeChartWarnBanner(): HTMLButtonElement {
  ensureStyle();
  let banner = document.getElementById(SIZE_CHART_WARN_ID) as HTMLButtonElement | null;
  if (!banner) {
    banner = document.createElement('button');
    banner.id = SIZE_CHART_WARN_ID;
    banner.type = 'button';
    banner.textContent = SIZE_CHART_WARN_TEXT;
    banner.title = sizeChartWarnTitle();
    banner.setAttribute('aria-label', sizeChartWarnTitle());
    banner.addEventListener('click', () => {
      if (!sizeChartUploadEnabled || sizeChartUploading) return;
      ensureSizeChartUploadInput().click();
    });
    document.body.appendChild(banner);
  }
  return banner;
}

function syncSizeChartWarnBanner(): void {
  const show =
    isExtensionAlive() &&
    isProductPage &&
    !!currentSource &&
    sizeChartUploadEnabled &&
    !sizeChartUploaded;
  if (!show) {
    document.getElementById(SIZE_CHART_WARN_ID)?.remove();
    return;
  }
  const banner = ensureSizeChartWarnBanner();
  banner.classList.add('kbt-ext-visible');
  banner.textContent = SIZE_CHART_WARN_TEXT;
  banner.title = sizeChartWarnTitle();
  banner.setAttribute('aria-label', sizeChartWarnTitle());
}

async function refreshSizeChartUploadStatusFromServer(): Promise<void> {
  if (!sizeChartUploadEnabled || !sessionReady) return;
  const href = location.href;
  try {
    const res = (await chrome.runtime.sendMessage({
      type: 'GET_SIZE_CHART_STATUS',
      url: href,
    })) as SizeChartStatusResponse;
    if (location.href !== href || !sizeChartUploadEnabled) return;
    if (res.ok && res.hasImage) {
      sizeChartUploaded = true;
      if (sizeChartProductKey) {
        await markSizeChartUploadedForKey(sizeChartProductKey);
      }
      syncSizeChartUploadButton();
    }
  } catch (err) {
    if (isContextInvalidatedError(err)) markExtensionDead();
  }
}

function ensureSizeChartUploadInput(): HTMLInputElement {
  let input = document.getElementById(SIZE_CHART_INPUT_ID) as HTMLInputElement | null;
  if (!input) {
    input = document.createElement('input');
    input.id = SIZE_CHART_INPUT_ID;
    input.type = 'file';
    input.accept = 'image/png,image/jpeg,image/jpg';
    input.style.display = 'none';
    input.addEventListener('change', () => {
      const file = input!.files?.[0];
      if (!file) return;
      void onSizeChartFileSelected(file).finally(() => {
        input!.value = '';
      });
    });
    document.body.appendChild(input);
  }
  return input;
}

function ensureSizeChartUploadButton(): HTMLButtonElement {
  ensureStyle();
  let button = document.getElementById(SIZE_CHART_BUTTON_ID) as HTMLButtonElement | null;
  if (!button) {
    button = document.createElement('button');
    button.id = SIZE_CHART_BUTTON_ID;
    button.type = 'button';
    button.className = 'kbt-ext-fab';
    button.innerHTML = sizeChartFileIconSvg();
    button.title = 'Upload Size Chart (PNG/JPG · tối đa 5 MB)';
    button.setAttribute('aria-label', 'Upload Size Chart');
    button.addEventListener('click', () => {
      if (!sizeChartUploadEnabled || sizeChartUploading) return;
      ensureSizeChartUploadInput().click();
    });
    document.body.appendChild(button);
  }
  return button;
}

async function onSizeChartFileSelected(file: File): Promise<void> {
  if (!sizeChartUploadEnabled) {
    showToast('Hãy thêm sản phẩm trước khi upload Size Chart.', false);
    return;
  }
  if (!file.type.startsWith('image/')) {
    showToast('Chỉ hỗ trợ file ảnh PNG/JPG.', false);
    return;
  }
  if (file.size > SIZE_CHART_MAX_BYTES) {
    showToast('Size Chart tối đa 5 MB.', false);
    return;
  }

  sizeChartUploading = true;
  syncSizeChartUploadButton();
  try {
    const buffer = await file.arrayBuffer();
    if (!buffer.byteLength) {
      showToast('File trống hoặc không đọc được.', false);
      return;
    }
    const fileBase64 = arrayBufferToBase64(buffer);
    const res = (await chrome.runtime.sendMessage({
      type: 'UPLOAD_SIZE_CHART',
      url: location.href,
      fileName: file.name || 'size-chart.png',
      mimeType: file.type || 'image/png',
      fileBase64,
    })) as UploadSizeChartResponse;
    if (res.ok) {
      sizeChartUploaded = true;
      if (sizeChartProductKey) {
        await markSizeChartUploadedForKey(sizeChartProductKey);
      }
      showToast(res.message || 'Đã lưu ảnh Size Chart.', true);
    } else {
      showToast(res.message || 'Upload Size Chart thất bại.', false);
    }
  } catch (err) {
    if (isContextInvalidatedError(err)) {
      markExtensionDead();
      showToast('Extension đã cập nhật — tải lại trang (F5) để tiếp tục.', false);
      return;
    }
    showToast(`Lỗi: ${(err as Error).message}`, false);
  } finally {
    sizeChartUploading = false;
    syncSizeChartUploadButton();
  }
}

function syncSizeChartUploadButton(): void {
  if (!isExtensionAlive() || !isProductPage || !currentSource) {
    removeSizeChartUploadUi();
    return;
  }
  if (!sizeChartUploadEnabled) {
    removeSizeChartUploadUi();
    return;
  }

  const button = ensureSizeChartUploadButton();
  button.classList.add('kbt-ext-visible');
  button.disabled = sizeChartUploading;
  button.classList.toggle('kbt-ext-fab--loading', sizeChartUploading);
  button.classList.toggle('kbt-ext-fab--done', sizeChartUploaded && !sizeChartUploading);
  button.innerHTML = sizeChartUploading
    ? '<span class="kbt-ext-spinner" aria-hidden="true"></span>'
    : sizeChartFileIconSvg();
  button.title = sizeChartUploading
    ? 'Đang upload Size Chart...'
    : sizeChartUploaded
      ? 'Đã upload Size Chart — bấm để thay file (PNG/JPG · tối đa 5 MB)'
      : sizeChartReopenOnClose
        ? 'Upload Size Chart (PNG/JPG · tối đa 5 MB) — đóng tab sẽ mở lại nếu chưa upload'
        : 'Upload Size Chart (PNG/JPG · tối đa 5 MB)';
  syncSizeChartLeaveWarning();
  syncSizeChartWarnBanner();
}

function removeSizeChartUploadUi(): void {
  document.getElementById(SIZE_CHART_BUTTON_ID)?.remove();
  document.getElementById(SIZE_CHART_INPUT_ID)?.remove();
  document.getElementById(SIZE_CHART_WARN_ID)?.remove();
  syncSizeChartLeaveWarning();
}

/** Đăng status lên storage để side panel / BG khớp nút nổi. */
async function publishTabStatus(tabId: number | null): Promise<void> {
  if (tabId === null || !isExtensionAlive()) return;
  try {
    const raw = await chrome.storage.local.get(CONTENT_TAB_STATUS_KEY);
    const prev = (raw[CONTENT_TAB_STATUS_KEY] as Record<string, ContentTabStatusSnapshot> | undefined) ?? {};
    const next: Record<string, ContentTabStatusSnapshot> = { ...prev };
    next[String(tabId)] = {
      href: location.href,
      pageReady,
      sessionReady,
      shopsSelected,
      alreadyAdded,
      addedStatus,
      isAdding,
      isProductPage,
      at: Date.now(),
    };
    // Giữ tối đa ~40 tab gần nhất.
    const keys = Object.keys(next);
    if (keys.length > 40) {
      const sorted = keys.sort((a, b) => (next[a].at ?? 0) - (next[b].at ?? 0));
      for (const k of sorted.slice(0, keys.length - 40)) delete next[k];
    }
    await chrome.storage.local.set({ [CONTENT_TAB_STATUS_KEY]: next });
  } catch (err) {
    if (isContextInvalidatedError(err)) markExtensionDead();
  }
}

function removeButton(): void {
  document.getElementById(BUTTON_ID)?.remove();
  removeSizeChartUploadUi();
}

function isFsCookieLive(raw: unknown): boolean {
  if (!raw || typeof raw !== 'object') return false;
  const fs = raw as { isLoggedIn?: boolean };
  return fs.isLoggedIn === true;
}

async function evaluate(): Promise<void> {
  if (!document.body || extensionDead) return;
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  const hrefAtStart = location.href;
  try {
    const store = await chrome.storage.local.get([
      AUTH_KEY,
      SOURCES_KEY,
      FS_STATUS_KEY,
      SELECTED_SHOPS_KEY,
      ADDED_IDS_KEY,
      ADDING_BY_TAB_KEY,
      SIZE_CHART_UPLOADED_KEY,
      SIZE_CHART_PENDING_KEY,
      QUICK_ADD_JOBS_KEY,
    ]);
    // Tab/SPA đổi URL trong lúc await storage → bỏ kết quả cũ.
    if (location.href !== hrefAtStart || extensionDead) return;

    const tabId = await ensureMyTabId();
    if (location.href !== hrefAtStart || extensionDead) return;

    const authed = !!store[AUTH_KEY];
    const fsLive = isFsCookieLive(store[FS_STATUS_KEY]);
    sessionReady = authed && fsLive;

    const selectedRaw = store[SELECTED_SHOPS_KEY];
    selectedShopIds = readSelectedShopIds(selectedRaw);
    shopsSelected = selectedShopIds.length > 0;

    const sources = (store[SOURCES_KEY] as ProductSourceOption[] | undefined) ?? [];
    // Nhận diện PDP khi có sources (kể cả chưa login) để hiện nút hướng dẫn.
    const detected = sources.length ? detectCurrentProductPage(sources) : null;

    isProductPage = !!detected;
    currentSource = detected?.source ?? null;

    const addedMap = normalizeAddedMap(store[ADDED_IDS_KEY]);
    addedStatus = 'none';
    addedShopHit = 0;
    addedShopTotal = selectedShopIds.length;
    alreadyAdded = false;
    if (detected) {
      const productId = extractProductId(location.href, detected.flow);
      if (productId) {
        const key = makeAddedProductKey(detected.flow, productId);
        if (key !== sizeChartProductKey) {
          sizeChartProductKey = key;
          sizeChartUploaded = false;
          sizeChartUploadEnabled = false;
        }
        const info = getAddedStatusForKey(key, addedMap, selectedShopIds);
        addedStatus = info.status;
        addedShopHit = info.hit;
        addedShopTotal = info.total;
        alreadyAdded = info.status === 'full';
        if (alreadyAdded || info.status === 'partial') {
          sizeChartUploadEnabled = true;
        }
        const sizeChartMap = normalizeSizeChartUploadedMap(store[SIZE_CHART_UPLOADED_KEY]);
        if (sizeChartMap[key]) {
          sizeChartUploaded = true;
        }
        sizeChartReopenOnClose = readPendingReopenOnClose(store[SIZE_CHART_PENDING_KEY], key);
      }
    }

    if (sizeChartUploadEnabled && sessionReady) {
      void refreshSizeChartUploadStatusFromServer();
    }

    // Sync isAdding từ BG; không ghi đè nếu local click vẫn chạy.
    const bgAdding = readAddingForTab(store[ADDING_BY_TAB_KEY], tabId);
    if (addClickInFlight || bgAdding) isAdding = true;
    else isAdding = false;

    syncButton();

    lastAddedMapForList = addedMap;
    lastSelectedShopIdsForList = selectedShopIds;
    listBadgesEnabled = sources.length > 0 && detectSheinSavedBadgeContext(sources);
    hydrateQuickAddJobsFromStorage(store[QUICK_ADD_JOBS_KEY]);
    syncListSavedBadges(lastAddedMapForList, lastSelectedShopIdsForList);
    syncSizeChartLeaveWarning();

    void publishTabStatus(tabId);
  } catch (err) {
    if (isContextInvalidatedError(err)) {
      markExtensionDead();
      return;
    }
    throw err;
  }
}

async function bootPageReady(): Promise<void> {
  pageReady = false;
  await evaluate();
  await whenPageReady();
  pageReady = true;
  await evaluate();
}

try {
  chrome.storage.onChanged.addListener((changes, area) => {
    if (!isExtensionAlive()) {
      markExtensionDead();
      return;
    }
    if (
      area === 'local' &&
      (changes[AUTH_KEY] ||
        changes[SOURCES_KEY] ||
        changes[FS_STATUS_KEY] ||
        changes[SELECTED_SHOPS_KEY] ||
        changes[ADDED_IDS_KEY] ||
        changes[ADDING_BY_TAB_KEY])
    ) {
      void evaluate();
    } else if (area === 'local' && changes[QUICK_ADD_JOBS_KEY]) {
      hydrateQuickAddJobsFromStorage(changes[QUICK_ADD_JOBS_KEY].newValue);
    }
  });

  chrome.runtime.onMessage.addListener((message: ContentMessage, _sender, sendResponse) => {
    if (!isExtensionAlive()) {
      markExtensionDead();
      return undefined;
    }
    if (message?.type === 'CAPTURE_HTML') {
      void (async () => {
        const html = await captureHtml();
        const [pdp, sizeGuide] = await Promise.all([waitForSheinPdpCache(), resolveSheinSizeGuide()]);
        sendResponse({
          url: location.href,
          html,
          ...sheinPdpMessageFields(pdp, currentSource?.code),
          sizeGuide: sizeGuide ?? undefined,
        });
      })();
      return true;
    }
    if (message?.type === 'GET_SHEIN_SEARCH_CAPTURE') {
      sendResponse({ capture: readSheinSearchCapture() });
      return true;
    }
    if (message?.type === 'GET_SHEIN_SEARCH_SESSION') {
      sendResponse({ session: readSheinSearchSession() });
      return true;
    }
    if (message?.type === 'RESET_SHEIN_SEARCH_SESSION') {
      resetSheinSearchSession();
      sendResponse({ ok: true });
      return true;
    }
    if (message?.type === 'CLEAR_SHEIN_SITE_CACHE') {
      void clearSheinPageStorage()
        .then(() => sendResponse({ ok: true }))
        .catch(() => sendResponse({ ok: false }));
      return true;
    }
    if (message?.type === 'FETCH_SHEIN_SEARCH') {
      void fetchSheinSearchApi(message.url, message.headers, message.method, message.body, message.page)
        .then((res) => sendResponse(res))
        .catch((err) =>
          sendResponse({
            ok: false,
            message: (err as Error).message || 'Không gọi được API Shein.',
          }),
        );
      return true;
    }
    if (message?.type === 'SHEIN_SEARCH_GOTO_PAGE') {
      void gotoSheinSearchPage(message.page, message.query)
        .then((res) => sendResponse(res))
        .catch((err) =>
          sendResponse({
            ok: false,
            message: (err as Error).message || 'Không bấm được phân trang Shein.',
          }),
        );
      return true;
    }
    if (message?.type === 'NAVIGATE_ASSIGN') {
      const target = String(message.url || '').trim();
      try {
        const next = new URL(target, location.href);
        if (next.origin !== location.origin || !/^https?:$/i.test(next.protocol)) {
          sendResponse({ ok: false });
          return true;
        }
        sendResponse({ ok: true });
        location.assign(next.href);
      } catch {
        sendResponse({ ok: false });
      }
      return true;
    }
    if (message?.type === 'QUICK_ADD_LIST_STATUS') {
      const goodsId = String(message.goodsId || '').trim();
      if (goodsId) {
        applyQuickAddStatus(goodsId, message.status, message.message);
      }
      sendResponse({ ok: true });
      return true;
    }
    if (message?.type === 'SIZE_CHART_LEAVE_GUARD') {
      void applySizeChartLeaveGuard(message.message, message.reopenOnClose !== false)
        .then(() => sendResponse({ ok: true }))
        .catch(() => sendResponse({ ok: false }));
      return true;
    }
    return undefined;
  });
} catch (err) {
  if (isContextInvalidatedError(err)) markExtensionDead();
}

// SPA đổi URL → nhận diện lại luồng + chờ DOM settle rồi mới active nút.
// Đồng thời phát hiện extension reload → ẩn nút.
let lastHref = location.href;
window.setInterval(() => {
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  if (location.href === lastHref) return;
  lastHref = location.href;
  pageReady = false;
  alreadyAdded = false;
  addedStatus = 'none';
  void evaluate();
  void whenPageReady().then(async () => {
    if (location.href !== lastHref || !isExtensionAlive()) return;
    pageReady = true;
    await evaluate();
  });
}, 600);

// Quay lại tab (bfcache / focus) → sync lại trạng thái Đã thêm theo URL tab này.
window.addEventListener('pageshow', () => {
  if (!isExtensionAlive()) {
    markExtensionDead();
    return;
  }
  void evaluate();
});
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible' && isExtensionAlive()) {
    void evaluate();
  }
});

void bootPageReady();
initSheinSizeGuideModalResize();
try {
  const existingSession = readSheinSearchSession();
  if (existingSession) forwardSheinSearchSession(existingSession);
} catch {
  /* ignore */
}

window.addEventListener('message', (event: MessageEvent) => {
  if (event.source !== window || event.origin !== location.origin) return;
  const data = event.data as { source?: string; payload?: unknown } | null;
  if (!data?.payload || typeof data.payload !== 'object') return;
  const payload = data.payload as Record<string, unknown>;
  if (data.source === SHEIN_SEARCH_SESSION_MSG) {
    const templateUrl = String(payload.templateUrl ?? '').trim();
    if (!templateUrl || !/get_products_by_keywords/i.test(templateUrl)) return;
    const headers =
      payload.headers && typeof payload.headers === 'object' && !Array.isArray(payload.headers)
        ? (payload.headers as Record<string, string>)
        : {};
    try {
      sessionStorage.setItem(SHEIN_SEARCH_SESSION_KEY, JSON.stringify(payload));
    } catch {
      /* ignore */
    }
    forwardSheinSearchSession({
      templateUrl,
      method: payload.method != null ? String(payload.method) : 'POST',
      headers,
      body: payload.body != null ? String(payload.body) : null,
      at: Number(payload.at) || Date.now(),
    });
    return;
  }
  if (data.source !== SHEIN_SEARCH_MSG) return;
  if (!Array.isArray(payload.products)) return;
  forwardSheinSearchCapture({
    keywords: String(payload.keywords ?? ''),
    page: Number(payload.page) || 1,
    total: Number(payload.total) || payload.products.length,
    products: payload.products,
    facets: Array.isArray(payload.facets) ? payload.facets : [],
    at: Number(payload.at) || Date.now(),
  });
});
