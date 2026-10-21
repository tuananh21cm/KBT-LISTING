// Content script độc lập — inject sau khi trang load xong; tránh MutationObserver loop.
import type { ActiveMatch, CheckoutCostSnapshot } from '../shared/types';
import { extractProductId } from '../shared/productId';
import {
  clampSheinDetailConcurrency,
  clampSheinLookbackDays,
  enrichSheinOrdersWithDetails,
  extractSheinOrderedAt,
  hasSheinListCapture,
  isSheinListPageOutsideLookback,
  isSheinOrderWithinLookback,
  normalizeSheinOrder,
  setSheinRawCaptureEnabled,
  SHEIN_DETAIL_CONCURRENCY_DEFAULT,
  type SheinDetailEnrichResult,
  type SheinListFetchOptions,
  type SheinOrderPayload,
} from '../shared/sheinOrders';
import {
  canonicalSizeToken,
  normLabel,
  pickSizeOptionIndex,
  sizesMatch,
  type SizeOptionFields,
} from '../shared/sheinVariantSelect';

const AUTH_KEY = 'kb_fulfil_auth';
const SOURCES_KEY = 'kb_fulfil_product_sources';
const ACTIVE_MATCH_KEY = 'kb_fulfil_active_match';
const PENDING_SELECT_KEY = 'kb_fulfil_pending_select';
const VARIANT_FIT_KEY = 'kb_fulfil_variant_fit';
const CHECKOUT_COST_KEY = 'kb_fulfil_checkout_cost';
const INJECT_ID = 'kb-fulfil-inject';
const STYLE_ID = 'kb-fulfil-style';
const SHEIN_CAPTURE_KEY = 'kb_fulfil_shein_capture';
const SHEIN_CAPTURE_MSG = 'kb-fulfil-shein-capture';

/** Cart / checkout / payment — giữ status cũ, không re-evaluate size. */
function isPurchaseFlowUrl(url: string): boolean {
  try {
    const path = new URL(url).pathname.toLowerCase();
    return (
      /\/(cart|checkout|checkouts?|bag|basket|payment|pay)(\/|$)/i.test(path) ||
      path.includes('/cart') ||
      path.includes('/checkout')
    );
  } catch {
    return false;
  }
}

/** Chỉ trang checkout — đọc tổng thanh toán để tính profit. */
function isCheckoutUrl(url: string): boolean {
  try {
    return /\/checkout/i.test(new URL(url).pathname);
  } catch {
    return /\/checkout/i.test(url);
  }
}

/** Anchor SHEIN: đặt inject ngay phía trên nút Add (product-intro__add). */
const SHEIN_ADD_SELECTORS = [
  '#goods-detail-mian > div.goods-detail.product-intro > div.atf-container > div.atf-right > div > div.product-add.product-intro__add',
  '#goods-detail-mian .product-add.product-intro__add',
  '#goods-detail-main .product-add.product-intro__add',
  '.atf-right .product-add.product-intro__add',
  '.product-intro__add',
  '.product-add.product-intro__add',
];

/** Fallback cũ (color) nếu chưa thấy nút Add. */
const SHEIN_COLOR_SELECTORS = [
  '#goods-detail-mian > div.goods-detail.product-intro > div.atf-container > div.atf-right > div.purchase-control > div.main-sales-attr__fold.product-intro__color',
  '#goods-detail-mian .purchase-control .product-intro__color',
  '#goods-detail-main .purchase-control .product-intro__color',
  '.purchase-control .main-sales-attr__fold.product-intro__color',
  '.product-intro__color',
];

interface PendingSelect {
  goodsId?: string;
  skuCode?: string;
  sizeLabel?: string | null;
  colorLabel?: string | null;
  variantAttr?: string | null;
  at: number;
}

let extensionDead = false;
let authenticated = false;
let activeMatch: ActiveMatch | null = null;
let lastHref = location.href;
/** Tránh ghi DOM liên tục khi nội dung không đổi. */
let lastInjectKey = '';
/** Debounce render sau load / đổi URL. */
let renderTimer: number | null = null;
let waitingForLoad = false;
/** Đã thử auto-select size cho pending này. */
let lastSelectKey = '';
let selectInFlight = false;

interface VariantFitResult {
  fit: 'ok' | 'bad' | 'warn';
  detail: string;
  sizeWant?: string | null;
  colorWant?: string | null;
  sizeSel?: string | null;
  colorSel?: string | null;
}

/** Fit gần nhất — dùng khi sync sticky không re-render full. */
let lastFitStatus: 'ok' | 'bad' | 'warn' | null = null;
/** Kết quả fit đã khóa — không đọc lại DOM khi SHEIN re-html lúc scroll. */
let lockedFitResult: VariantFitResult | null = null;
let lockedFitMatchKey = '';
let stickySyncBound = false;
let stickyRaf = 0;
/** Debounce refresh fit sau click size/color. */
let fitRefreshTimer: number | null = null;
let fitRefreshRecompute = false;
let selectionWatchBound = false;
/** Tránh persist storage lặp khi fit không đổi. */
let lastPersistedFitKey = '';
/** Watch modal Shipping Address trên checkout. */
let checkoutAddrWatchBound = false;
let checkoutAddrObserver: MutationObserver | null = null;
/** Watch tổng thanh toán checkout → storage (profit). */
let checkoutCostWatchBound = false;
let checkoutCostObserver: MutationObserver | null = null;
let checkoutCostPollTimer: number | null = null;
let checkoutCostSyncTimer: number | null = null;
let lastCheckoutCostKey = '';

function isExtensionAlive(): boolean {
  try {
    return !extensionDead && typeof chrome !== 'undefined' && !!chrome.runtime?.id;
  } catch {
    return false;
  }
}

function markExtensionDead(): void {
  if (extensionDead) return;
  extensionDead = true;
  if (renderTimer !== null) {
    window.clearTimeout(renderTimer);
    renderTimer = null;
  }
  unbindSelectionWatch();
  unbindStickySync();
  unbindCheckoutAddressAssist();
  removeInject();
  // Gỡ panel cũ nếu còn trên trang (đã bỏ overlay floating).
  document.getElementById('kb-fulfil-panel')?.remove();
}

function isSheinHost(): boolean {
  return /shein\./i.test(location.hostname);
}

function ensureStyle(): void {
  if (document.getElementById(STYLE_ID)) return;
  const style = document.createElement('style');
  style.id = STYLE_ID;
  style.textContent = `
#kb-fulfil-inject {
  margin: 6px 0;
  padding: 5px 8px;
  border: 1px solid #ffe58f;
  border-radius: 4px;
  background: #fffbe6;
  color: rgba(0,0,0,0.88);
  font: 11px/1.35 Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  box-sizing: border-box;
  width: 100%;
  max-width: 100%;
  flex: 0 0 auto;
  align-self: stretch;
  transition: background 0.2s ease, border-color 0.2s ease;
}
#kb-fulfil-inject.kb-ok {
  border-color: #73d13d;
  background: #f6ffed;
}
#kb-fulfil-inject.kb-bad {
  border-color: #ff4d4f;
  background: #fff2f0;
}
#kb-fulfil-inject.kb-warn {
  border-color: #ffe58f;
  background: #fffbe6;
}
#kb-fulfil-inject .kb-inject-line {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  overflow: hidden;
  font-size: 11px;
  font-weight: 500;
  line-height: 18px;
  white-space: nowrap;
}
#kb-fulfil-inject .kb-inject-label,
#kb-fulfil-inject .kb-inject-variant,
#kb-fulfil-inject .kb-inject-sku {
  display: inline-flex;
  align-items: center;
  height: 18px;
  line-height: 18px;
  min-width: 0;
}
#kb-fulfil-inject .kb-inject-label {
  flex: 0 0 auto;
  color: rgba(0,0,0,0.55);
}
#kb-fulfil-inject .kb-inject-variant {
  flex: 0 1 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  font-weight: 700;
  color: #1677ff;
}
#kb-fulfil-inject .kb-inject-qty {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
  height: 18px;
  padding: 0 5px;
  font-size: 11px;
  font-weight: 700;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  color: #cf1322;
  background: #ffccc4;
  border: 1px solid #ffccc7;
  border-radius: 3px;
}
#kb-fulfil-inject .kb-inject-sku {
  flex: 1 1 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 500;
  color: rgba(0,0,0,0.45);
}

/* Đổi màu nút mua hàng theo trạng thái — đồng bộ panel + inject */
.product-add.product-intro__add.kb-fulfil-add-ok,
.product-add.kb-fulfil-add-ok {
  --kb-buy-bg: #389e0d;
  --kb-buy-border: #237804;
}
.product-add.product-intro__add.kb-fulfil-add-bad,
.product-add.kb-fulfil-add-bad {
  --kb-buy-bg: #cf1322;
  --kb-buy-border: #a8071a;
}
.product-add.product-intro__add.kb-fulfil-add-warn,
.product-add.kb-fulfil-add-warn {
  --kb-buy-bg: #d48806;
  --kb-buy-border: #ad6800;
}
.product-add.kb-fulfil-add-ok button,
.product-add.kb-fulfil-add-ok [role="button"],
.product-add.kb-fulfil-add-ok a[class*="btn"],
.product-add.kb-fulfil-add-ok [class*="Button"],
.product-add.kb-fulfil-add-ok [class*="button"],
.product-add.kb-fulfil-add-bad button,
.product-add.kb-fulfil-add-bad [role="button"],
.product-add.kb-fulfil-add-bad a[class*="btn"],
.product-add.kb-fulfil-add-bad [class*="Button"],
.product-add.kb-fulfil-add-bad [class*="button"],
.product-add.kb-fulfil-add-warn button,
.product-add.kb-fulfil-add-warn [role="button"],
.product-add.kb-fulfil-add-warn a[class*="btn"],
.product-add.kb-fulfil-add-warn [class*="Button"],
.product-add.kb-fulfil-add-warn [class*="button"] {
  background-color: var(--kb-buy-bg) !important;
  background-image: none !important;
  border-color: var(--kb-buy-border) !important;
  color: #fff !important;
}
#kb-fulfil-inject.kb-sticky-pin {
  margin: 0 !important;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.14);
  pointer-events: auto;
}
#kb-fulfil-inject.kb-sticky-pin[data-kb-pin="top"] {
  /* Banner đầu trang khi Size còn hiện — không đè vùng chọn size */
  backdrop-filter: saturate(1.1);
}
#kb-fulfil-fill-addr {
  margin-left: auto;
  flex-shrink: 0;
  padding: 4px 12px;
  border: none;
  border-radius: 4px;
  background: #1677ff;
  color: #fff;
  font: 600 12px/1.3 Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(22,119,255,0.35);
}
#kb-fulfil-fill-addr:hover { background: #0958d9; }
#kb-fulfil-fill-addr:disabled { opacity: 0.6; cursor: wait; }
#kb-fulfil-fill-addr.kb-fill-ok { background: #389e0d; }
#kb-fulfil-fill-addr.kb-fill-bad { background: #cf1322; }
.kb-fulfil-addr-header {
  display: flex !important;
  align-items: center !important;
  justify-content: space-between !important;
  gap: 12px !important;
  width: 100%;
  box-sizing: border-box;
}
.kb-fulfil-addr-finder-host {
  position: relative !important;
}
#kb-fulfil-fill-addr-finder {
  position: absolute;
  left: calc(100% + 6px);
  top: 50%;
  transform: translateY(-50%);
  z-index: 20;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: #1677ff;
  color: #fff;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  box-shadow: 0 2px 6px rgba(22,119,255,0.35);
}
#kb-fulfil-fill-addr-finder:hover { background: #0958d9; }
#kb-fulfil-fill-addr-finder:disabled { opacity: 0.6; cursor: wait; }
#kb-fulfil-fill-addr-finder.kb-fill-ok { background: #389e0d; }
#kb-fulfil-fill-addr-finder.kb-fill-bad { background: #cf1322; }
#kb-fulfil-fill-addr-finder svg {
  width: 14px;
  height: 14px;
  display: block;
  pointer-events: none;
}
.kb-fulfil-addr-finder-host {
  overflow: visible !important;
}
`;
  (document.head ?? document.documentElement).appendChild(style);
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function findSheinAddAnchor(): Element | null {
  for (const sel of SHEIN_ADD_SELECTORS) {
    const el = document.querySelector(sel);
    if (el) return el;
  }
  return null;
}

/** SHEIN hết hàng: nút Add → Sold out (productSoldOutBtn). */
function isSheinSoldOut(): boolean {
  if (document.querySelector('button.productSoldOutBtn, .productSoldOutBtn')) return true;

  const add = findSheinAddAnchor();
  const scope = add ?? document.querySelector('.atf-right, .product-intro, .purchase-control');
  if (!scope) return false;

  const buttons = scope.querySelectorAll(
    'button, [role="button"], .add-cart-basebtn, [class*="add-cart"], [class*="AddCart"]',
  );
  for (const btn of buttons) {
    if (!(btn instanceof HTMLElement)) continue;
    if (btn.classList.contains('productSoldOutBtn')) return true;
    const cls = typeof btn.className === 'string' ? btn.className : '';
    if (/sold\s*out|soldout|productSoldOut/i.test(cls)) return true;
    const text = (btn.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
    if (/^sold\s*out$/.test(text) || text === 'hết hàng' || /^out\s*of\s*stock$/.test(text)) {
      return true;
    }
  }
  return false;
}

function findSheinColorAnchor(): Element | null {
  for (const sel of SHEIN_COLOR_SELECTORS) {
    const el = document.querySelector(sel);
    if (el) return el;
  }
  return null;
}

/**
 * Khối Size SHEIN — gắn #kb-fulfil-inject ngay DƯỚI div này:
 * ...product-intro__bsSize > div:nth-child(1) > div
 */
const SHEIN_SIZE_INJECT_SELECTORS = [
  '#goods-detail-mian > div.goods-detail.product-intro > div.atf-container > div.atf-right > div > div.product-intro__bsSize > div:nth-child(1) > div',
  '.product-intro__bsSize > div:nth-child(1) > div',
  '.product-intro__bsSize > div:first-child > div',
  '.product-intro__bsSize > div:nth-child(1)',
  '.product-intro__bsSize',
];

function findSheinSizeInjectAnchor(): Element | null {
  for (const sel of SHEIN_SIZE_INJECT_SELECTORS) {
    const el = document.querySelector(sel);
    if (el) return el;
  }
  return null;
}

/**
 * Anchor inject: ngay dưới hàng Size (bsSize > div:nth-child(1) > div).
 * Fallback: color / size roots / product-add.
 */
function findInjectAnchor(): { el: Element; place: 'inside' | 'before' | 'after' } | null {
  const bsSize = findSheinSizeInjectAnchor();
  if (bsSize) return { el: bsSize, place: 'after' };

  const color = findSheinColorAnchor();
  if (color) return { el: color, place: 'after' };

  const size = document.querySelector(SIZE_ROOTS[0]!);
  if (size) return { el: size, place: 'before' };

  const add = findSheinAddAnchor();
  if (add) return { el: add, place: 'inside' };

  const purchase = document.querySelector('.purchase-control');
  if (purchase) return { el: purchase, place: 'after' };

  return null;
}

function removeInject(): void {
  clearBuyButtonTheme();
  unbindStickySync();
  document.getElementById(INJECT_ID)?.remove();
  lastInjectKey = '';
  lastFitStatus = null;
  lockedFitResult = null;
  lockedFitMatchKey = '';
  lastPersistedFitKey = '';
  void chrome.storage.local.remove(VARIANT_FIT_KEY).catch(() => undefined);
}

function persistVariantFit(
  match: ActiveMatch,
  fit: { fit: 'ok' | 'bad' | 'warn'; detail: string; sizeSel?: string | null; colorSel?: string | null },
): void {
  if (!isExtensionAlive()) return;
  const key = `${match.orderId}|${match.itemId}|${fit.fit}|${fit.detail}|${fit.sizeSel ?? ''}|${fit.colorSel ?? ''}`;
  if (key === lastPersistedFitKey) return;
  lastPersistedFitKey = key;
  void chrome.storage.local
    .set({
      [VARIANT_FIT_KEY]: {
        fit: fit.fit,
        detail: fit.detail,
        orderId: match.orderId,
        itemId: match.itemId,
        sizeSel: fit.sizeSel ?? null,
        colorSel: fit.colorSel ?? null,
        at: Date.now(),
      },
    })
    .catch(() => undefined);
}

function clearBuyButtonTheme(): void {
  document.querySelectorAll('.kb-fulfil-add-ok, .kb-fulfil-add-bad, .kb-fulfil-add-warn').forEach((el) => {
    el.classList.remove('kb-fulfil-add-ok', 'kb-fulfil-add-bad', 'kb-fulfil-add-warn');
  });
}

function applyBuyButtonTheme(addEl: Element | null, fit: 'ok' | 'bad' | 'warn'): void {
  clearBuyButtonTheme();
  if (!addEl) return;
  const cls =
    fit === 'ok' ? 'kb-fulfil-add-ok' : fit === 'bad' ? 'kb-fulfil-add-bad' : 'kb-fulfil-add-warn';
  addEl.classList.add(cls);
}

/** Đồng bộ màu vàng (warn) khi đang chờ size / chưa gắn inject. */
function applyCheckingWarnTheme(match: ActiveMatch): void {
  ensureStyle();
  lastFitStatus = 'warn';
  applyBuyButtonTheme(findSheinAddAnchor(), 'warn');
  persistVariantFit(match, { fit: 'warn', detail: 'Đang kiểm tra size…' });
}

function clearInjectPinStyles(box: HTMLElement): void {
  box.classList.remove('kb-sticky-pin');
  box.style.position = '';
  box.style.left = '';
  box.style.right = '';
  box.style.top = '';
  box.style.bottom = '';
  box.style.width = '';
  box.style.zIndex = '';
}

function elementPosition(el: Element): string {
  try {
    return getComputedStyle(el).position;
  } catch {
    return '';
  }
}

function isStickyOrFixed(el: Element): boolean {
  const pos = elementPosition(el);
  return pos === 'sticky' || pos === 'fixed';
}

/** Tìm khối nút mua đang visible — ưu tiên sticky/fixed (khi cuộn). */
function findVisibleBuySurface(): HTMLElement | null {
  const seen = new Set<HTMLElement>();
  const candidates: HTMLElement[] = [];

  const push = (el: Element | null) => {
    if (!(el instanceof HTMLElement) || seen.has(el)) return;
    seen.add(el);
    candidates.push(el);
  };

  for (const sel of SHEIN_ADD_SELECTORS) {
    document.querySelectorAll(sel).forEach(push);
  }
  document
    .querySelectorAll(
      '.product-add, .product-intro__add, [class*="goods-ctas"], [class*="bsc-ctas"], [class*="sticky"] .product-add',
    )
    .forEach(push);

  let best: HTMLElement | null = null;
  let bestScore = -Infinity;

  for (const el of candidates) {
    const rect = el.getBoundingClientRect();
    if (rect.width < 48 || rect.height < 24) continue;
    if (rect.bottom < 0 || rect.top > window.innerHeight) continue;

    let score = rect.width * 0.01;
    let node: HTMLElement | null = el;
    for (let i = 0; i < 6 && node; i++) {
      if (isStickyOrFixed(node)) {
        score += 200 - i * 10;
        break;
      }
      node = node.parentElement;
    }
    // Ưu tiên gần đáy viewport (thanh mua sticky thường ở dưới).
    if (rect.bottom > window.innerHeight * 0.55) score += 40;
    if (rect.top >= 0 && rect.bottom <= window.innerHeight) score += 20;

    if (score > bestScore) {
      bestScore = score;
      best = el;
    }
  }

  return best;
}

function isBuySurfaceStuck(buy: HTMLElement): boolean {
  let node: HTMLElement | null = buy;
  for (let i = 0; i < 6 && node; i++) {
    const pos = elementPosition(node);
    if (pos === 'fixed') return true;
    if (pos === 'sticky') {
      const rect = node.getBoundingClientRect();
      // Sticky đã “dính”: gần top hoặc gần bottom.
      if (rect.top <= 12 || rect.bottom >= window.innerHeight - 12) return true;
    }
    node = node.parentElement;
  }

  // Main in-flow add đã ra khỏi view nhưng vẫn còn buy surface visible.
  const main = document.querySelector(
    '#goods-detail-mian .product-add.product-intro__add, #goods-detail-main .product-add.product-intro__add',
  ) as HTMLElement | null;
  if (main && main !== buy) {
    const mr = main.getBoundingClientRect();
    const br = buy.getBoundingClientRect();
    const mainGone = mr.bottom < 60 || mr.top > window.innerHeight - 40;
    const buyVisible = br.top < window.innerHeight && br.bottom > 0;
    if (mainGone && buyVisible) return true;
  }
  return false;
}

/** Khối size SHEIN (bsSize ưu tiên) — quyết định giữ inject trong flow. */
function findSizeBlock(): HTMLElement | null {
  const bs =
    document.querySelector('.product-intro__bsSize') ||
    document.querySelector('[class*="product-intro__bsSize"]');
  if (bs instanceof HTMLElement) return bs;
  for (const sel of SIZE_ROOTS) {
    const el = document.querySelector(sel);
    if (el instanceof HTMLElement) return el;
  }
  return null;
}

function isElementMostlyVisible(el: HTMLElement, minRatio = 0.25): boolean {
  const rect = el.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return false;
  const visibleTop = Math.max(rect.top, 0);
  const visibleBottom = Math.min(rect.bottom, window.innerHeight);
  const visible = Math.max(0, visibleBottom - visibleTop);
  return visible / rect.height >= minRatio;
}

/**
 * Đính cứng #kb-fulfil-inject ngay dưới hàng Size khi Size còn trên màn.
 * Chỉ float trên nút mua khi Size đã cuộn mất + buy sticky.
 * (Trước đây luôn float lên đầu viewport → user không thấy inject dưới Size.)
 */
function syncInjectToBuyButton(): void {
  const box = document.getElementById(INJECT_ID) as HTMLElement | null;
  if (!box || !isSheinHost() || !activeMatch) return;

  const sizeEl = findSizeBlock();
  const sizeVisible = !!sizeEl && isElementMostlyVisible(sizeEl, 0.12);
  const buy = findVisibleBuySurface() || (findSheinAddAnchor() as HTMLElement | null);

  // Size còn thấy → luôn giữ inject trong DOM dưới hàng Size (không sticky).
  if (sizeVisible || !buy || !isBuySurfaceStuck(buy)) {
    clearInjectPinStyles(box);
    box.removeAttribute('data-kb-pin');
    const anchor = findInjectAnchor();
    if (anchor) placeInjectBox(box, anchor);
    if (lastFitStatus) applyBuyButtonTheme(findSheinAddAnchor() || buy, lastFitStatus);
    return;
  }

  // Size đã khuất + buy sticky → float ngay trên nút mua.
  if (box.parentElement !== document.body) {
    document.body.appendChild(box);
  }

  const buyRect = buy.getBoundingClientRect();
  const gap = 6;
  const height = box.offsetHeight || 56;
  const left = Math.max(8, Math.round(buyRect.left));
  const width = Math.round(Math.min(buyRect.width, window.innerWidth - left - 8));
  const aboveBuy = buyRect.top - height - gap;
  const top = aboveBuy >= 8 ? aboveBuy : 8;
  const mode: 'top' | 'above-buy' = aboveBuy >= 8 ? 'above-buy' : 'top';

  box.classList.add('kb-sticky-pin');
  box.dataset.kbPin = mode;
  box.style.position = 'fixed';
  box.style.left = `${left}px`;
  box.style.width = `${width}px`;
  box.style.top = `${Math.round(top)}px`;
  box.style.bottom = 'auto';
  box.style.right = 'auto';
  box.style.zIndex = '2147483000';

  if (lastFitStatus) applyBuyButtonTheme(buy, lastFitStatus);
}

function onStickyScrollOrResize(): void {
  if (stickyRaf) return;
  stickyRaf = window.requestAnimationFrame(() => {
    stickyRaf = 0;
    syncInjectToBuyButton();
  });
}

function bindStickySync(): void {
  if (stickySyncBound) return;
  stickySyncBound = true;
  window.addEventListener('scroll', onStickyScrollOrResize, { passive: true, capture: true });
  window.addEventListener('resize', onStickyScrollOrResize, { passive: true });
}

function unbindStickySync(): void {
  if (!stickySyncBound) return;
  stickySyncBound = false;
  window.removeEventListener('scroll', onStickyScrollOrResize, true);
  window.removeEventListener('resize', onStickyScrollOrResize);
  if (stickyRaf) {
    window.cancelAnimationFrame(stickyRaf);
    stickyRaf = 0;
  }
}

function placeInjectBox(
  box: HTMLElement,
  anchor: { el: Element; place: 'inside' | 'before' | 'after' },
): void {
  if (anchor.place === 'inside') {
    if (box.parentElement !== anchor.el || anchor.el.firstElementChild !== box) {
      anchor.el.insertBefore(box, anchor.el.firstChild);
    }
    return;
  }
  const where = anchor.place === 'before' ? 'beforebegin' : 'afterend';
  const ok =
    box.parentElement === anchor.el.parentElement &&
    (anchor.place === 'before'
      ? box.nextElementSibling === anchor.el
      : box.previousElementSibling === anchor.el);
  if (!ok) {
    anchor.el.insertAdjacentElement(where, box);
  }
}

function isInjectPlaced(
  box: HTMLElement,
  anchor: { el: Element; place: 'inside' | 'before' | 'after' },
): boolean {
  if (anchor.place === 'inside') {
    return box.parentElement === anchor.el && anchor.el.firstElementChild === box;
  }
  return (
    box.parentElement === anchor.el.parentElement &&
    (anchor.place === 'before'
      ? box.nextElementSibling === anchor.el
      : box.previousElementSibling === anchor.el)
  );
}

function matchKey(match: ActiveMatch): string {
  const { item, order } = match;
  return [
    order.id,
    item.id,
    item.title ?? '',
    item.sellerSku ?? '',
    item.variantAttr ?? '',
    item.quantity,
    order.buyerName ?? '',
    order.city ?? '',
    order.state ?? '',
    order.buyerMemo ?? '',
    order.platformOrderId ?? '',
  ].join('|');
}

const SIZE_ROOTS = [
  '.product-intro__bsSize',
  '.product-intro__size',
  '.main-sales-attr__fold.product-intro__size',
  '.purchase-control .product-intro__size',
  '.purchase-control [class*="product-intro__size"]',
  '[class*="product-intro__bsSize"]',
  '[class*="product-intro__size"]',
  '.goods-size',
  '.product-intro__size-radio',
];

const COLOR_ROOTS = [
  '.product-intro__color',
  '.main-sales-attr__fold.product-intro__color',
  '.purchase-control .product-intro__color',
  '[class*="product-intro__color"]',
];

function labelsMatch(a: string, b: string): boolean {
  const na = normLabel(a);
  const nb = normLabel(b);
  if (!na || !nb) return false;
  return na === nb || na.includes(nb) || nb.includes(na);
}

function cleanOptionText(el: Element): string | null {
  if (!(el instanceof HTMLElement)) return null;
  const fromAttr =
    el.getAttribute('data-attr_value_name') ||
    el.getAttribute('data-name') ||
    el.getAttribute('aria-label') ||
    el.getAttribute('title') ||
    '';
  let t = (fromAttr || el.innerText || el.textContent || '').trim();
  t = t.split('\n')[0]?.trim() || '';
  t = t.replace(/\s+/g, ' ');
  if (!t || t.length > 28) return null;
  if (/^(size|select|choose|qty|quantity|color|colour)\b/i.test(t)) return null;
  return t;
}

function elementClassName(el: Element | null | undefined): string {
  if (!el) return '';
  const c = el.className as unknown;
  if (typeof c === 'string') return c;
  if (c && typeof c === 'object' && 'baseVal' in c) {
    const base = (c as { baseVal?: unknown }).baseVal;
    return typeof base === 'string' ? base : '';
  }
  return '';
}

function isSelectedEl(el: Element): boolean {
  if (!(el instanceof HTMLElement)) return false;
  if (el.getAttribute('aria-checked') === 'true') return true;
  if (el.getAttribute('aria-selected') === 'true') return true;
  if (el.getAttribute('aria-pressed') === 'true') return true;
  if (el.getAttribute('data-selected') === 'true') return true;
  if (el.getAttribute('data-checked') === '1' || el.getAttribute('data-checked') === 'true') {
    return true;
  }

  const cls = elementClassName(el);
  if (/\b(active|selected|checked|current|is-active|is-selected|is-checked|on)\b/i.test(cls)) {
    return true;
  }
  // SHEIN đôi khi gắn active ở wrapper
  const parentCls = elementClassName(el.parentElement);
  if (/\b(active|selected|checked|is-active)\b/i.test(parentCls)) {
    const t = cleanOptionText(el);
    if (t) return true;
  }

  if (el instanceof HTMLInputElement && (el.type === 'radio' || el.type === 'checkbox') && el.checked) {
    return true;
  }
  const input = el.querySelector('input[type="radio"], input[type="checkbox"]');
  if (input instanceof HTMLInputElement && input.checked) return true;

  return false;
}

/** Có khối color trên trang không (SP chỉ size thì thường không có). */
function pageHasColorOptions(): boolean {
  for (const sel of COLOR_ROOTS) {
    const root = document.querySelector(sel);
    if (!root) continue;
    const opts = root.querySelectorAll(
      'div, span, button, li, [role="radio"], img, [class*="item"], [class*="radio"]',
    );
    if (opts.length >= 1) return true;
  }
  return false;
}

function readSelectedInRoots(roots: string[]): string | null {
  for (const rootSel of roots) {
    const rootsFound = document.querySelectorAll(rootSel);
    for (const root of rootsFound) {
      const candidates = Array.from(
        root.querySelectorAll(
          'div, span, button, li, label, a, [role="radio"], [role="option"], input[type="radio"]',
        ),
      );
      for (const el of candidates) {
        if (!isSelectedEl(el)) continue;
        // input radio: lấy label
        if (el instanceof HTMLInputElement) {
          const lab =
            (el.id && document.querySelector(`label[for="${el.id}"]`)) ||
            el.closest('label');
          const t = lab ? cleanOptionText(lab) : cleanOptionText(el);
          if (t) return t;
          continue;
        }
        const t = cleanOptionText(el);
        if (t) return t;
      }
    }
  }
  return null;
}

/** Đọc size đang chọn — ưu tiên khối size, fallback scan class active gần size. */
function readSelectedSize(): string | null {
  const fromRoots = readSelectedInRoots(SIZE_ROOTS);
  if (fromRoots) return fromRoots;

  // Fallback: mọi phần tử trong purchase-control có class active và text giống size
  const purchase = document.querySelector('.purchase-control, .atf-right, .product-intro');
  if (!purchase) return null;
  const actives = purchase.querySelectorAll(
    '.active, .selected, .checked, [aria-checked="true"], [class*="active"]',
  );
  for (const el of actives) {
    // bỏ qua color swatch (thường có img / không text size)
    if (el.closest('[class*="color"], .product-intro__color')) continue;
    const t = cleanOptionText(el);
    if (!t) continue;
    if (
      /^(?:XX?S|XX?L|[2-5]XL|X?L|M|S|One\s*Size|Free\s*Size|\d{1,2}(?:\.\d)?)$/i.test(
        t.replace(/\s+/g, ' '),
      ) ||
      t.length <= 8
    ) {
      return t;
    }
  }
  return null;
}

function readSelectedColor(): string | null {
  if (!pageHasColorOptions()) return null;
  return readSelectedInRoots(COLOR_ROOTS);
}

/** So khớp size (và color chỉ khi đơn/trang có màu). */
function evaluateVariantFit(match: ActiveMatch): VariantFitResult {
  const sizeWant = extractSizeFromVariant(match.item.variantAttr);
  const colorWant = extractColorFromVariant(match.item.variantAttr);
  const checkColor = !!colorWant && pageHasColorOptions();
  const sizeSel = readSelectedSize();
  const colorSel = checkColor ? readSelectedColor() : null;
  const hasVariant = !!match.item.variantAttr?.trim();
  const sizeWantOut = sizeWant || (hasVariant && !colorWant ? match.item.variantAttr : null);
  const colorWantOut = checkColor ? colorWant : null;

  // Hết hàng trên SHEIN → luôn đỏ (ưu tiên hơn cả khớp size).
  if (isSheinSoldOut()) {
    return {
      fit: 'bad',
      detail: 'Hết hàng (Sold out)',
      sizeWant: sizeWantOut,
      colorWant: colorWantOut,
      sizeSel,
      colorSel,
    };
  }

  if (!hasVariant && !sizeWant && !colorWant) {
    return {
      fit: 'bad',
      detail: 'Đơn không có size / variant',
      sizeWant,
      colorWant: checkColor ? colorWant : null,
      sizeSel,
      colorSel,
    };
  }

  const pending: string[] = [];
  const mismatches: string[] = [];

  // Ưu tiên check size — SP không có color chỉ cần size.
  if (sizeWant) {
    if (!sizeSel) pending.push(`Chưa chọn size (cần ${sizeWant})`);
    else if (!sizesMatch(sizeSel, sizeWant)) {
      mismatches.push(`Size: ${sizeSel} ≠ ${sizeWant}`);
    }
  } else if (hasVariant && !colorWant) {
    // variantAttr có nhưng không parse được — coi cả chuỗi là size cần chọn
    const want = match.item.variantAttr!.trim();
    if (!sizeSel) pending.push(`Chưa chọn size (cần ${want})`);
    else if (!sizesMatch(sizeSel, want)) {
      mismatches.push(`Size: ${sizeSel} ≠ ${want}`);
    }
  }

  if (checkColor && colorWant) {
    if (!colorSel) pending.push(`Chưa chọn màu (cần ${colorWant})`);
    else if (!labelsMatch(colorSel, colorWant)) {
      mismatches.push(`Màu: ${colorSel} ≠ ${colorWant}`);
    }
  }

  if (mismatches.length > 0) {
    return {
      fit: 'bad',
      detail: [...mismatches, ...pending].join(' · '),
      sizeWant: sizeWantOut,
      colorWant: colorWantOut,
      sizeSel,
      colorSel,
    };
  }

  if (pending.length > 0) {
    // Chỉ hiện «Đang kiểm tra…» khi auto-select đang chạy; xong rồi → không khớp (bad) rõ lý do.
    if (selectInFlight) {
      return {
        fit: 'warn',
        detail: 'Đang kiểm tra size…',
        sizeWant: sizeWantOut,
        colorWant: colorWantOut,
        sizeSel,
        colorSel,
      };
    }
    return {
      fit: 'bad',
      detail: pending.join(' · '),
      sizeWant: sizeWantOut,
      colorWant: colorWantOut,
      sizeSel,
      colorSel,
    };
  }

  const okParts: string[] = [];
  if (sizeSel) okParts.push(`Size ${sizeSel}`);
  if (checkColor && colorSel) okParts.push(`Màu ${colorSel}`);
  return {
    fit: 'ok',
    detail: okParts.length > 0 ? `Đã khớp: ${okParts.join(' · ')}` : 'Size đã khớp',
    sizeWant,
    colorWant: checkColor ? colorWant : null,
    sizeSel,
    colorSel,
  };
}

function buildInjectHtml(match: ActiveMatch, fit: VariantFitResult): string {
  const { item } = match;
  const color = (fit.colorWant || extractColorFromVariant(item.variantAttr) || '').trim();
  const size = (fit.sizeWant || extractSizeFromVariant(item.variantAttr) || '').trim();

  // Không dùng labelsMatch (includes) — "Multicolor" chứa "M" sẽ nuốt size M/S/L.
  const uniq: string[] = [];
  if (color) uniq.push(color);
  if (size && (!color || normLabel(size) !== normLabel(color))) {
    uniq.push(size);
  }
  // Fallback: không parse được color/size → dùng nguyên variantAttr
  if (uniq.length === 0 && item.variantAttr?.trim()) {
    uniq.push(item.variantAttr.trim());
  }

  const parts = [
    ...uniq.map((v) => `<span class="kb-inject-variant">${escapeHtml(v)}</span>`),
    `<span class="kb-inject-qty">x${escapeHtml(String(item.quantity))}</span>`,
  ];
  if (item.sellerSku?.trim()) {
    parts.push(
      `<span class="kb-inject-sku">${escapeHtml(item.sellerSku.trim())}</span>`,
    );
  }

  return `<div class="kb-inject-line"><span class="kb-inject-label">Chọn :</span>${parts.join('')}</div>`;
}

function waitForWindowLoad(): Promise<void> {
  if (document.readyState === 'complete') return Promise.resolve();
  return new Promise((resolve) => {
    window.addEventListener('load', () => resolve(), { once: true });
  });
}

async function waitForInjectAnchor(
  timeoutMs: number,
): Promise<{ el: Element; place: 'inside' | 'before' | 'after' } | null> {
  const start = Date.now();
  let found = findInjectAnchor();
  if (found) return found;
  while (Date.now() - start < timeoutMs) {
    await new Promise((r) => setTimeout(r, 300));
    if (!isExtensionAlive()) return null;
    found = findInjectAnchor();
    if (found) return found;
  }
  return findInjectAnchor();
}

function extractSizeFromVariant(variantAttr?: string | null): string | null {
  if (!variantAttr?.trim()) return null;
  const v = variantAttr.trim();
  // Chỉ có Color: → không lấy size
  if (/^colou?r\s*[=:]/i.test(v) && !/size/i.test(v)) return null;

  const sizeEq = v.match(/size\s*[=:]\s*([^,/|;]+)/i);
  if (sizeEq?.[1]) return sizeEq[1].trim();

  const withoutColor = v
    .replace(/colou?r\s*[=:][^,/|;]*/gi, '')
    .replace(/^[,;/|\s]+|[,;/|\s]+$/g, '')
    .trim();
  if (!withoutColor) return null;

  const alone = withoutColor.match(
    /\b((?:XX?S)|(?:XX?L)|(?:[2-5]XL)|(?:X?L)|(?:M)|(?:S)|(?:One\s*Size)|(?:Free\s*Size)|\d{1,2}(?:\.\d)?)\b/i,
  );
  if (alone?.[1]) return alone[1].replace(/\s+/g, ' ').trim();

  if (withoutColor.length <= 16 && !/colou?r/i.test(withoutColor)) {
    return withoutColor;
  }
  return null;
}

function extractColorFromVariant(variantAttr?: string | null): string | null {
  if (!variantAttr?.trim()) return null;
  const colorEq = variantAttr.match(/colou?r\s*[=:]\s*([^,/|;]+)/i);
  if (colorEq?.[1]?.trim()) return colorEq[1].trim();

  // "Apricot, XXL" / "White | M" — phần cuối là size thì phần trước là màu.
  const parts = variantAttr
    .split(/[,|/]/)
    .map((p) => p.trim())
    .filter(Boolean);
  if (parts.length >= 2) {
    const last = parts[parts.length - 1]!;
    if (canonicalSizeToken(last)) {
      const color = parts.slice(0, -1).join(', ').trim();
      if (color && !canonicalSizeToken(color)) return color;
    }
  }
  return null;
}

function clickFirst(els: Element[]): boolean {
  for (const el of els) {
    if (!(el instanceof HTMLElement)) continue;
    if (el.getAttribute('aria-disabled') === 'true') continue;
    if (el.classList.contains('disabled') || el.classList.contains('soldout')) continue;
    el.click();
    return true;
  }
  return false;
}

const SIZE_RADIO_SELECTORS = [
  '.product-intro__size-choose [role="radio"]',
  '.product-intro__size-radio[role="radio"]',
  '.product-intro__size [role="radio"][data-attr_value_name]',
  '.product-intro__bsSize [role="radio"]',
];

/** Chỉ lấy radio size thật — bỏ wrapper span.size-radio và nút Curve (role=button). */
function collectSizeRadios(): HTMLElement[] {
  const seen = new Set<HTMLElement>();
  const out: HTMLElement[] = [];

  for (const sel of SIZE_RADIO_SELECTORS) {
    for (const el of document.querySelectorAll(sel)) {
      if (!(el instanceof HTMLElement)) continue;
      if (el.getAttribute('role') !== 'radio') continue;
      if (seen.has(el)) continue;
      seen.add(el);
      out.push(el);
    }
  }

  // Fallback: quét SIZE_ROOTS nhưng chỉ giữ [role=radio] hoặc có data-attr_value_name.
  if (out.length === 0) {
    for (const rootSel of SIZE_ROOTS) {
      const root = document.querySelector(rootSel);
      if (!root) continue;
      for (const el of root.querySelectorAll(
        '[role="radio"], [data-attr_value_name].product-intro__size-radio, .product-intro__size-radio[data-attr_value_name]',
      )) {
        if (!(el instanceof HTMLElement)) continue;
        if (seen.has(el)) continue;
        // Bỏ Curve / link không phải size radio
        if (el.getAttribute('role') === 'button') continue;
        if (
          el.getAttribute('role') !== 'radio' &&
          !el.getAttribute('data-attr_value_name')
        ) {
          continue;
        }
        seen.add(el);
        out.push(el);
      }
    }
  }

  return out;
}

function sizeRadioToFields(el: HTMLElement): SizeOptionFields {
  const ariaDisabled = el.getAttribute('aria-disabled') === 'true';
  const cls = elementClassName(el);
  return {
    attrValueName: el.getAttribute('data-attr_value_name'),
    ariaLabel: el.getAttribute('aria-label'),
    text: cleanOptionText(el) || el.textContent?.trim() || null,
    disabled: ariaDisabled || /\bdisabled\b/i.test(cls),
    soldOut: /\bsold\s*out|soldout\b/i.test(cls),
  };
}

function isSizeRadioChecked(el: HTMLElement): boolean {
  return el.getAttribute('aria-checked') === 'true' || isSelectedEl(el);
}

/**
 * Click đúng div.product-intro__size-radio[role=radio] theo nhãn size.
 * Đã aria-checked → coi như thành công, không click lại.
 */
function tryClickSizeByLabel(label: string): boolean {
  const radios = collectSizeRadios();
  if (radios.length === 0) return false;

  const fields = radios.map(sizeRadioToFields);
  const idx = pickSizeOptionIndex(fields, label);
  if (idx < 0) return false;

  const el = radios[idx]!;
  if (isSizeRadioChecked(el)) return true;
  if (el.getAttribute('aria-disabled') === 'true') return false;
  el.click();
  return true;
}

/** Swatch color [role=radio] trong container màu. */
function collectColorRadios(): HTMLElement[] {
  const roots = [
    '.main-sales-attr__color-container',
    '.main-sales-attr__color',
    ...COLOR_ROOTS,
  ];
  const seen = new Set<HTMLElement>();
  const out: HTMLElement[] = [];
  for (const rootSel of roots) {
    for (const root of document.querySelectorAll(rootSel)) {
      for (const el of root.querySelectorAll('[role="radio"]')) {
        if (!(el instanceof HTMLElement)) continue;
        if (seen.has(el)) continue;
        seen.add(el);
        out.push(el);
      }
    }
    if (out.length > 0) break;
  }
  return out;
}

function colorRadioLabel(el: HTMLElement): string {
  return (
    el.getAttribute('aria-label') ||
    el.querySelector('img')?.getAttribute('alt') ||
    cleanOptionText(el) ||
    ''
  ).trim();
}

/**
 * Color: giữ swatch active / đúng goodsId; chỉ click khi khớp đúng 1 swatch.
 * Nhiều swatch cùng tên (Black x2) → không click (tránh nhảy sang goods khác).
 */
function tryClickColorSafely(
  colorLabel: string,
  opts?: { goodsIdMatched?: boolean },
): boolean {
  if (!pageHasColorOptions()) return false;
  const want = normLabel(colorLabel);
  if (!want) return false;

  const radios = collectColorRadios();
  if (radios.length === 0) {
    // Fallback mềm: tryClickAttrByLabel cũ (chỉ [role=radio])
    return tryClickAttrByLabel(COLOR_ROOTS, colorLabel, { radioOnly: true });
  }

  const active = radios.find((el) => el.getAttribute('aria-checked') === 'true');
  const activeLabel = active ? colorRadioLabel(active) : '';
  if (activeLabel && labelsMatch(activeLabel, colorLabel)) {
    return true; // đã đúng màu
  }
  // Đúng goodsId → màu đã đúng theo URL PDP; không click swatch (tránh trùng tên).
  if (opts?.goodsIdMatched) {
    return true;
  }

  const matches = radios.filter((el) => {
    const lab = colorRadioLabel(el);
    return lab && labelsMatch(lab, colorLabel);
  });
  if (matches.length !== 1) return false;

  const el = matches[0]!;
  if (el.getAttribute('aria-checked') === 'true') return true;
  if (el.getAttribute('aria-disabled') === 'true') return false;
  el.click();
  return true;
}

/** Tìm và click option size/color trên SHEIN theo nhãn. */
function tryClickAttrByLabel(
  roots: string[],
  label: string,
  opts?: { radioOnly?: boolean },
): boolean {
  const want = normLabel(label);
  if (!want) return false;
  const isSizeRoot = roots === SIZE_ROOTS || roots.some((r) => /size|bsSize/i.test(r));
  if (isSizeRoot) {
    return tryClickSizeByLabel(label);
  }

  for (const rootSel of roots) {
    const root = document.querySelector(rootSel);
    if (!root) continue;
    const candidates = Array.from(
      root.querySelectorAll(
        opts?.radioOnly
          ? '[role="radio"]'
          : 'div, span, button, li, [role="radio"], [role="option"], [class*="item"], [class*="radio"]',
      ),
    );
    const exact: Element[] = [];
    const soft: Element[] = [];
    for (const el of candidates) {
      // Tránh click wrapper / tiêu đề Color (h2.color-block)
      if (el.tagName === 'H2' || el.closest('h2')) continue;
      if (!(el instanceof HTMLElement)) continue;
      if (el.getAttribute('role') !== 'radio' && opts?.radioOnly) continue;

      const attrName = el.getAttribute('data-attr_value_name') || '';
      const aria = el.getAttribute('aria-label') || '';
      const text = (attrName || aria || cleanOptionText(el) || '').trim();
      if (!text || text.length > 40) continue;

      const n = normLabel(text);
      if (n === want) exact.push(el);
      else if (n.includes(want) || want.includes(n)) soft.push(el);
    }
    if (clickFirst(exact) || clickFirst(soft)) return true;
  }
  return false;
}

function tryClickBySkuCode(skuCode: string): boolean {
  const code = skuCode.trim();
  if (!code) return false;
  const selectors = [
    `[data-sku="${code}"]`,
    `[data-sku_code="${code}"]`,
    `[data-attr_value_id="${code}"]`,
    `[data-goods_sn="${code}"]`,
    `[sku="${code}"]`,
  ];
  for (const sel of selectors) {
    const el = document.querySelector(sel);
    if (el instanceof HTMLElement) {
      el.click();
      return true;
    }
  }
  // Không fallback quét mọi [title]/[aria-label] + includes (dễ click nhầm).
  return false;
}

function pendingSelectKey(pending: PendingSelect): string {
  return `${pending.goodsId}|${pending.skuCode}|${pending.sizeLabel}|${pending.colorLabel}|${pending.at}`;
}

function pageGoodsIdMatches(pendingGoodsId?: string): boolean {
  if (!pendingGoodsId?.trim()) return true; // không có goodsId → không chặn
  const pageId = extractProductId(location.href, 'shein');
  if (!pageId) return false;
  return pageId === pendingGoodsId.trim();
}

async function waitForSizeRadios(timeoutMs: number): Promise<HTMLElement[]> {
  const start = Date.now();
  let radios = collectSizeRadios();
  if (radios.length > 0) return radios;
  while (Date.now() - start < timeoutMs) {
    await new Promise((r) => setTimeout(r, 250));
    if (!isExtensionAlive()) return [];
    radios = collectSizeRadios();
    if (radios.length > 0) return radios;
  }
  return collectSizeRadios();
}

function verifySizeSelected(label: string): boolean {
  const radios = collectSizeRadios();
  if (radios.length === 0) return false;
  const fields = radios.map(sizeRadioToFields);
  const idx = pickSizeOptionIndex(fields, label);
  if (idx < 0) return false;
  return isSizeRadioChecked(radios[idx]!);
}

async function applyPendingSelect(): Promise<void> {
  if (!isSheinHost() || !isExtensionAlive() || selectInFlight) return;
  if (document.readyState !== 'complete') return;
  if (isOnSheinPurchaseFlow()) return;

  const raw = await chrome.storage.local.get(PENDING_SELECT_KEY);
  const pending = raw[PENDING_SELECT_KEY] as PendingSelect | undefined;
  if (!pending || Date.now() - (pending.at || 0) > 120_000) {
    if (pending) await chrome.storage.local.remove(PENDING_SELECT_KEY);
    return;
  }

  // Trang cũ / goods khác → giữ pending để PDP đúng xử lý.
  if (!pageGoodsIdMatches(pending.goodsId)) {
    return;
  }

  const key = pendingSelectKey(pending);
  if (key === lastSelectKey) return;

  selectInFlight = true;
  try {
    await waitForInjectAnchor(8_000);
    const radios = await waitForSizeRadios(6_000);
    void radios;

    const goodsIdMatched = pageGoodsIdMatches(pending.goodsId);

    const size =
      pending.sizeLabel ||
      extractSizeFromVariant(pending.variantAttr) ||
      (activeMatch ? extractSizeFromVariant(activeMatch.item.variantAttr) : null) ||
      (pending.variantAttr && !extractColorFromVariant(pending.variantAttr)
        ? pending.variantAttr.trim()
        : null);

    const color =
      pending.colorLabel ||
      extractColorFromVariant(pending.variantAttr) ||
      (activeMatch ? extractColorFromVariant(activeMatch.item.variantAttr) : null);

    let sizeOk = !size; // không cần size → coi ok
    let colorOk = !color || !pageHasColorOptions();

    for (let attempt = 0; attempt < 3; attempt++) {
      if (!isExtensionAlive()) return;

      if (pending.skuCode) {
        tryClickBySkuCode(pending.skuCode);
      }

      if (size) {
        tryClickSizeByLabel(size);
        await new Promise((r) => setTimeout(r, 120));
        sizeOk = verifySizeSelected(size);
      }

      if (color && pageHasColorOptions()) {
        colorOk = tryClickColorSafely(color, { goodsIdMatched });
      }

      if (sizeOk && colorOk) break;
      await new Promise((r) => setTimeout(r, 350));
    }

    lastSelectKey = key;
    await chrome.storage.local.remove(PENDING_SELECT_KEY);

    // Kết thúc auto-select trước khi đánh giá — tránh khóa warn «Đang kiểm tra…».
    selectInFlight = false;
    await new Promise((r) => setTimeout(r, 80));
    renderSheinInject(true, { recomputeFit: true });
    scheduleFitRefresh(160, true);
  } finally {
    selectInFlight = false;
  }
}

/**
 * Cập nhật fit khi user chọn size/color — không poll/MutationObserver
 * (SHEIN re-html lúc scroll làm fit nhấp nháy).
 */
function scheduleFitRefresh(delayMs = 50, recomputeFit = true): void {
  if (recomputeFit) fitRefreshRecompute = true;
  if (fitRefreshTimer !== null) window.clearTimeout(fitRefreshTimer);
  fitRefreshTimer = window.setTimeout(() => {
    fitRefreshTimer = null;
    const recompute = fitRefreshRecompute;
    fitRefreshRecompute = false;
    refreshFitFromPage({ recomputeFit: recompute });
  }, delayMs);
}

function refreshFitFromPage(opts?: { recomputeFit?: boolean; forceRefresh?: boolean }): void {
  if (!isExtensionAlive() || !isSheinHost() || !authenticated || !activeMatch) return;
  if (document.readyState !== 'complete') return;
  // Cart / checkout: giữ nguyên status đã lưu từ PDP — không re-evaluate.
  if (isPurchaseFlowUrl(location.href)) {
    removeInject();
    clearBuyButtonTheme();
    return;
  }
  renderSheinInject(!!opts?.forceRefresh, { recomputeFit: !!opts?.recomputeFit });
}

function isOnSheinPurchaseFlow(): boolean {
  return isSheinHost() && isPurchaseFlowUrl(location.href);
}

/** Chỉ option size/color — không gồm purchase-control/add (đổi class khi scroll). */
function isVariantOptionTarget(el: Element): boolean {
  if (el.closest(`#${INJECT_ID}`)) return false;
  return !!el.closest(
    [
      '.product-intro__bsSize',
      '.product-intro__size',
      '.product-intro__color',
      '.product-intro__size-radio',
      '[class*="product-intro__bsSize"]',
      '[class*="product-intro__size"]',
      '[class*="product-intro__color"]',
    ].join(', '),
  );
}

function onSelectionInteraction(ev: Event): void {
  const t = ev.target;
  if (!(t instanceof Element) || !isVariantOptionTarget(t)) return;
  // Lần 1 gần như ngay; lần 2 bắt class active SHEIN gắn muộn.
  scheduleFitRefresh(40, true);
  window.setTimeout(() => scheduleFitRefresh(120, true), 120);
}

function bindSelectionWatch(): void {
  if (selectionWatchBound) return;
  selectionWatchBound = true;
  document.addEventListener('click', onSelectionInteraction, true);
  document.addEventListener('keydown', onSelectionInteraction, true);
}

function unbindSelectionWatch(): void {
  if (!selectionWatchBound) return;
  selectionWatchBound = false;
  document.removeEventListener('click', onSelectionInteraction, true);
  document.removeEventListener('keydown', onSelectionInteraction, true);
  if (fitRefreshTimer !== null) {
    window.clearTimeout(fitRefreshTimer);
    fitRefreshTimer = null;
  }
  fitRefreshRecompute = false;
}

/** Inject gắn ngay dưới hàng Size (product-intro__bsSize). */
function renderSheinInject(
  forceRefresh = false,
  opts?: { recomputeFit?: boolean },
): boolean {
  if (!isSheinHost() || !authenticated || !activeMatch) {
    removeInject();
    return false;
  }
  // Cart / checkout: không inject, không ghi đè variantFit (giữ status cũ).
  if (isOnSheinPurchaseFlow()) {
    removeInject();
    clearBuyButtonTheme();
    return false;
  }
  if (document.readyState !== 'complete') {
    applyCheckingWarnTheme(activeMatch);
    return false;
  }

  const anchor = findInjectAnchor();
  if (!anchor?.el || (anchor.place !== 'inside' && !anchor.el.parentElement)) {
    applyCheckingWarnTheme(activeMatch);
    return false;
  }

  const mk = matchKey(activeMatch);
  // Không khóa warn — «Đang kiểm tra size…» chỉ tạm; lần sau phải chốt ok/bad.
  const shouldRecompute =
    !!opts?.recomputeFit ||
    !lockedFitResult ||
    lockedFitMatchKey !== mk ||
    lockedFitResult.fit === 'warn';
  const fit = shouldRecompute ? evaluateVariantFit(activeMatch) : lockedFitResult!;
  if (shouldRecompute) {
    // Chỉ khóa kết quả đã chốt (ok/bad). Warn để lần render sau còn cập nhật.
    if (fit.fit !== 'warn') {
      lockedFitResult = fit;
      lockedFitMatchKey = mk;
    } else {
      lockedFitResult = null;
      lockedFitMatchKey = '';
    }
  }

  const statusKey = `${fit.fit}|${fit.detail}|${fit.sizeSel ?? ''}|${fit.colorSel ?? ''}`;
  const key = `${mk}|${statusKey}|${anchor.place}`;
  const existing = document.getElementById(INJECT_ID) as HTMLElement | null;
  const placedOk = !!existing && isInjectPlaced(existing, anchor);

  // Cập nhật màu ngay — không đợi rebuild HTML.
  if (existing) {
    existing.classList.toggle('kb-ok', fit.fit === 'ok');
    existing.classList.toggle('kb-bad', fit.fit === 'bad');
    existing.classList.toggle('kb-warn', fit.fit === 'warn');
  }
  lastFitStatus = fit.fit;
  applyBuyButtonTheme(findSheinAddAnchor(), fit.fit);
  persistVariantFit(activeMatch, fit);
  bindSelectionWatch();

  if (placedOk && lastInjectKey === key && !forceRefresh) {
    bindStickySync();
    syncInjectToBuyButton();
    return true;
  }

  ensureStyle();
  let box = existing;
  if (!box) {
    box = document.createElement('div');
    box.id = INJECT_ID;
  }

  placeInjectBox(box, anchor);

  box.classList.toggle('kb-ok', fit.fit === 'ok');
  box.classList.toggle('kb-bad', fit.fit === 'bad');
  box.classList.toggle('kb-warn', fit.fit === 'warn');
  box.innerHTML = buildInjectHtml(activeMatch, fit);

  lastInjectKey = key;
  bindStickySync();
  syncInjectToBuyButton();
  return true;
}

async function renderUiWhenReady(): Promise<void> {
  if (!isExtensionAlive() || waitingForLoad) return;
  waitingForLoad = true;
  try {
    await waitForWindowLoad();
    if (!isExtensionAlive()) return;

    // SHEIN PDP: đợi khối size/color. Cart/checkout: bỏ qua (giữ status cũ).
    if (isSheinHost() && authenticated && activeMatch && !isOnSheinPurchaseFlow()) {
      await waitForInjectAnchor(10_000);
    }
    if (!isExtensionAlive()) return;

    // Gỡ overlay floating cũ nếu còn sót.
    document.getElementById('kb-fulfil-panel')?.remove();

    if (isOnSheinPurchaseFlow()) {
      removeInject();
      clearBuyButtonTheme();
      bindCheckoutAddressAssist();
      syncCheckoutFillButton();
      if (isCheckoutUrl(location.href)) {
        bindCheckoutCostWatch();
        void syncCheckoutCost();
      } else {
        unbindCheckoutCostWatch();
        void clearCheckoutCost();
      }
      return;
    }

    unbindCheckoutAddressAssist();
    unbindCheckoutCostWatch();
    void clearCheckoutCost();
    renderSheinInject();
    void applyPendingSelect();
  } finally {
    waitingForLoad = false;
  }
}

/* —— Checkout: tổng thanh toán (giá thực trả) → profit trên side panel —— */

const CHECKOUT_TOTAL_SELECTORS = [
  '#checkout-app .checkout-price-detail__total .checkout-price-detail__total-right',
  '#checkout-app .checkout-price-detail__total-right',
  '.checkout-price-detail__total .checkout-price-detail__total-right',
  '.checkout-price-detail__total-right',
];

function parseCheckoutMoneyText(raw: string): number | null {
  const cleaned = raw.replace(/[^0-9.-]/g, '');
  if (!cleaned || cleaned === '-' || cleaned === '.') return null;
  const num = Number.parseFloat(cleaned);
  return Number.isFinite(num) ? num : null;
}

function readCheckoutTotalFromDom(): { amount: number; raw: string } | null {
  for (const sel of CHECKOUT_TOTAL_SELECTORS) {
    const el = document.querySelector(sel);
    if (!el) continue;
    // Ưu tiên div con trong total-right (đúng selector user chỉ).
    const target =
      (el.querySelector(':scope > div') as HTMLElement | null) || (el as HTMLElement);
    const raw = (target.textContent || '').replace(/\s+/g, ' ').trim();
    if (!raw) continue;
    const amount = parseCheckoutMoneyText(raw);
    if (amount == null) continue;
    return { amount, raw };
  }
  return null;
}

async function clearCheckoutCost(): Promise<void> {
  lastCheckoutCostKey = '';
  try {
    await chrome.storage.local.remove(CHECKOUT_COST_KEY);
  } catch {
    // ignore
  }
}

async function syncCheckoutCost(): Promise<void> {
  if (!isExtensionAlive() || !isSheinHost() || !authenticated) return;
  if (!isCheckoutUrl(location.href)) {
    await clearCheckoutCost();
    return;
  }
  if (!activeMatch) {
    await clearCheckoutCost();
    return;
  }

  const read = readCheckoutTotalFromDom();
  if (!read) return;

  const snap: CheckoutCostSnapshot = {
    amount: read.amount,
    currency: 'USD',
    raw: read.raw,
    orderId: activeMatch.orderId,
    itemId: activeMatch.itemId,
    tabUrl: location.href,
    at: Date.now(),
  };
  const key = `${snap.orderId}|${snap.itemId}|${snap.amount}|${snap.raw}`;
  if (key === lastCheckoutCostKey) return;
  lastCheckoutCostKey = key;
  try {
    await chrome.storage.local.set({ [CHECKOUT_COST_KEY]: snap });
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err ?? '');
    if (/extension context invalidated/i.test(msg)) markExtensionDead();
  }
}

function scheduleCheckoutCostSync(delayMs = 120): void {
  if (checkoutCostSyncTimer !== null) window.clearTimeout(checkoutCostSyncTimer);
  checkoutCostSyncTimer = window.setTimeout(() => {
    checkoutCostSyncTimer = null;
    void syncCheckoutCost();
  }, delayMs);
}

function bindCheckoutCostWatch(): void {
  if (checkoutCostWatchBound) {
    scheduleCheckoutCostSync(0);
    return;
  }
  checkoutCostWatchBound = true;
  checkoutCostObserver = new MutationObserver(() => {
    scheduleCheckoutCostSync(150);
  });
  const root =
    document.querySelector('#checkout-app .checkout-price-detail') ||
    document.querySelector('#checkout-app') ||
    document.body;
  checkoutCostObserver.observe(root, {
    childList: true,
    subtree: true,
    characterData: true,
  });
  checkoutCostPollTimer = window.setInterval(() => {
    void syncCheckoutCost();
  }, 2000);
  void syncCheckoutCost();
}

function unbindCheckoutCostWatch(): void {
  if (!checkoutCostWatchBound) return;
  checkoutCostWatchBound = false;
  checkoutCostObserver?.disconnect();
  checkoutCostObserver = null;
  if (checkoutCostPollTimer !== null) {
    window.clearInterval(checkoutCostPollTimer);
    checkoutCostPollTimer = null;
  }
  if (checkoutCostSyncTimer !== null) {
    window.clearTimeout(checkoutCostSyncTimer);
    checkoutCostSyncTimer = null;
  }
}

/* —— Checkout: Fill Shipping Address từ đơn đang mua —— */

const FILL_ADDR_BTN_ID = 'kb-fulfil-fill-addr';
const FILL_ADDR_FINDER_BTN_ID = 'kb-fulfil-fill-addr-finder';

const FILL_ADDR_FINDER_ICON = `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path fill="currentColor" d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-9 14l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>`;

function sleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

function splitBuyerName(full: string): { first: string; last: string } {
  const parts = full.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return { first: '', last: '' };
  if (parts.length === 1) return { first: parts[0]!, last: 'Customer' };
  return { first: parts[0]!, last: parts.slice(1).join(' ') };
}

function normalizeUsPhone(phone: string): string {
  let digits = phone.replace(/\D/g, '');
  if (digits.length === 11 && digits.startsWith('1')) digits = digits.slice(1);
  return digits;
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Chuỗi tìm trong Address Finder: ưu tiên street, kèm zip/city. */
function addressFinderQuery(order: ActiveMatch['order']): string {
  let street = (order.address || '').trim();
  for (const part of [order.countryCode, 'United States', 'USA', order.postalCode, order.state, order.city]) {
    if (!part?.trim()) continue;
    street = street.replace(new RegExp(`[,\\s]+${escapeRegExp(part.trim())}\\s*$`, 'i'), '').trim();
  }
  const parts = [street, order.city?.trim(), order.postalCode?.trim()].filter(Boolean);
  if (parts.length > 0) return parts.join(', ');
  return [order.postalCode, order.city, order.state].filter(Boolean).join(', ').trim();
}

function findAddrField(name: string): HTMLElement | null {
  const root = document.querySelector('.checkout-address-write, .sui-business-address');
  if (!root) return null;
  return root.querySelector(`[data-field-name="${name}"]`) as HTMLElement | null;
}

function getNativeValueSetter(
  el: HTMLInputElement | HTMLTextAreaElement,
): ((this: HTMLInputElement | HTMLTextAreaElement, v: string) => void) | undefined {
  const proto =
    el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  return Object.getOwnPropertyDescriptor(proto, 'value')?.set;
}

/** Mở khóa input SHEIN (thường readonly đến khi click). */
async function unlockAddrInput(
  field: HTMLElement,
  input: HTMLInputElement | HTMLTextAreaElement,
): Promise<void> {
  input.removeAttribute('readonly');
  input.removeAttribute('disabled');
  input.readOnly = false;
  input.disabled = false;

  const clickTarget =
    (field.querySelector(
      '.sui-input-titlewarp, .sui-input-title, .addr-input__title, .sui-input, .addr-search-content',
    ) as HTMLElement | null) || input;
  clickTarget.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window }));
  clickTarget.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window }));
  clickTarget.click();
  input.focus({ preventScroll: true });
  // SHEIN cần một nhịp tick sau click mới bỏ readonly thực sự.
  await sleep(40);

  input.removeAttribute('readonly');
  input.readOnly = false;
}

/** Xóa hết rồi ghi value mới — đi qua pipeline input để Vue/SHEIN nhận. */
async function replaceAddrInputValue(
  input: HTMLInputElement | HTMLTextAreaElement,
  value: string,
): Promise<void> {
  const setter = getNativeValueSetter(input);

  input.focus({ preventScroll: true });
  input.select();
  try {
    input.setSelectionRange(0, (input.value || ' ').length);
  } catch {
    // ignore
  }

  let cleared = false;
  try {
    cleared = document.execCommand('selectAll', false) && document.execCommand('delete', false);
  } catch {
    cleared = false;
  }

  if (!cleared) {
    setter?.call(input, '');
    input.value = '';
    input.dispatchEvent(
      new InputEvent('input', {
        bubbles: true,
        cancelable: true,
        inputType: 'deleteContentBackward',
        data: null,
      }),
    );
  }
  await sleep(16);

  let inserted = false;
  try {
    inserted = document.execCommand('insertText', false, value);
  } catch {
    inserted = false;
  }

  if (!inserted || (input.value || '') !== value) {
    setter?.call(input, value);
    input.value = value;
    input.dispatchEvent(
      new InputEvent('input', {
        bubbles: true,
        cancelable: true,
        inputType: 'insertText',
        data: value,
      }),
    );
  }

  input.dispatchEvent(new Event('change', { bubbles: true }));
  input.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Process' }));
  await sleep(16);
  input.blur();
  input.dispatchEvent(new Event('blur', { bubbles: true }));
}

async function fillTextField(fieldName: string, value: string): Promise<boolean> {
  if (!value) return false;
  const field = findAddrField(fieldName);
  if (!field) return false;
  const input = field.querySelector(
    'input.addr-search-content__core, input.sui-input-title__inner, textarea.sui-textarea-title__inner, input.sui-input__inner, input:not([type="checkbox"]), textarea',
  ) as HTMLInputElement | HTMLTextAreaElement | null;
  if (!input) return false;

  await unlockAddrInput(field, input);
  await replaceAddrInputValue(input, value);

  // Vue revert → thử lần 2
  if ((input.value || '').trim() !== value.trim()) {
    await unlockAddrInput(field, input);
    await replaceAddrInputValue(input, value);
  }
  return (input.value || '').trim() === value.trim() || (input.value || '').includes(value.slice(0, 6));
}

/** Bỏ focus khỏi input SHEIN sau khi fill (tránh dừng ở Phone / Address Finder). */
function blurCheckoutAddressInputs(): void {
  const root = document.querySelector('.checkout-address-write, .sui-business-address');
  root?.querySelectorAll('input, textarea').forEach((el) => {
    if (el instanceof HTMLElement) {
      el.blur();
      el.dispatchEvent(new Event('blur', { bubbles: true }));
    }
  });
  const active = document.activeElement;
  if (active instanceof HTMLElement && active.matches('input, textarea, [contenteditable="true"]')) {
    active.blur();
  }
}

/** Đồng bộ nhanh từ storage (đúng dữ liệu side panel đang hiện) — không gọi API. */
async function syncActiveMatchFromStorage(): Promise<ActiveMatch | null> {
  if (!isExtensionAlive()) return activeMatch;
  try {
    const raw = await chrome.storage.local.get(ACTIVE_MATCH_KEY);
    const stored = (raw[ACTIVE_MATCH_KEY] as ActiveMatch | undefined) ?? null;
    if (stored) activeMatch = stored;
  } catch {
    // ignore
  }
  return activeMatch;
}

/**
 * Lấy buyer mới nhất của đúng sản phẩm đang active.
 * Timeout ngắn — hết giờ thì dùng bản storage (đã cập nhật khi bấm View).
 */
async function softRefreshActiveBuyer(timeoutMs = 400): Promise<ActiveMatch | null> {
  await syncActiveMatchFromStorage();
  if (!isExtensionAlive() || !activeMatch || activeMatch.orderId === 'pending') {
    return activeMatch;
  }
  try {
    const refreshed = await Promise.race([
      new Promise<ActiveMatch | null>((resolve) => {
        try {
          chrome.runtime.sendMessage({ type: 'REFRESH_ACTIVE_BUYER' }, (r) => {
            void chrome.runtime.lastError;
            resolve((r as { match?: ActiveMatch | null } | undefined)?.match ?? null);
          });
        } catch {
          resolve(null);
        }
      }),
      sleep(timeoutMs).then(() => null as ActiveMatch | null),
    ]);
    if (refreshed) activeMatch = refreshed;
  } catch {
    // giữ storage
  }
  return activeMatch;
}

/** Fill theo Sản phẩm đang mua — sync storage + soft refresh buyer. */
async function fillCheckoutAddressFromMatch(): Promise<{ ok: boolean; detail: string }> {
  if (!activeMatch) return { ok: false, detail: 'Chưa có sản phẩm đang mua' };
  const order = activeMatch.order;
  const { first, last } = splitBuyerName(order.buyerName || '');
  const phone = normalizeUsPhone(order.buyerPhone || '');
  const finder = addressFinderQuery(order);

  if (!first && !phone && !finder) {
    return { ok: false, detail: 'Đơn thiếu tên / phone / address' };
  }

  // First Name → Last Name → Phone → Address Finder
  const okFirst = first ? await fillTextField('first_name', first) : true;
  await sleep(40);
  const okLast = last ? await fillTextField('last_name', last) : true;
  await sleep(40);
  const okPhone = phone ? await fillTextField('tel', phone) : true;
  await sleep(40);
  const okFinder = finder ? await fillTextField('associated_address', finder) : true;

  blurCheckoutAddressInputs();
  const finderBtn = document.getElementById(FILL_ADDR_FINDER_BTN_ID) as HTMLButtonElement | null;
  finderBtn?.focus({ preventScroll: true });

  const ok = okFirst && okLast && okPhone && okFinder;
  return {
    ok,
    detail: ok ? 'Đã nhập' : 'Nhập chưa nhận',
  };
}

async function fillAddressFinderFromMatch(): Promise<{ ok: boolean; detail: string }> {
  if (!activeMatch) return { ok: false, detail: 'Chưa có sản phẩm đang mua' };
  const finder = addressFinderQuery(activeMatch.order);
  if (!finder) return { ok: false, detail: 'Đơn thiếu address' };
  const ok = await fillTextField('associated_address', finder);
  blurCheckoutAddressInputs();
  const iconBtn = document.getElementById(FILL_ADDR_FINDER_BTN_ID) as HTMLButtonElement | null;
  iconBtn?.focus({ preventScroll: true });
  return {
    ok,
    detail: ok ? 'Đã fill' : 'Fill chưa nhận',
  };
}

function removeCheckoutAddrAssistUi(): void {
  document.getElementById(FILL_ADDR_BTN_ID)?.remove();
  document.getElementById(FILL_ADDR_FINDER_BTN_ID)?.remove();
  document.querySelectorAll('.kb-fulfil-addr-finder-host').forEach((el) => {
    el.classList.remove('kb-fulfil-addr-finder-host');
  });
}

function syncAddressFinderFillIcon(): void {
  if (!isOnSheinPurchaseFlow() || !authenticated || !activeMatch) {
    document.getElementById(FILL_ADDR_FINDER_BTN_ID)?.remove();
    return;
  }

  const field = findAddrField('associated_address');
  const input = (field?.querySelector(
    'input.addr-search-content__core, input[search-type="list"], input[aria-controls="associate-listbox"]',
  ) ||
    document.querySelector(
      '.checkout-address-write input.addr-search-content__core, .sui-business-address input.addr-search-content__core',
    )) as HTMLInputElement | null;

  if (!input) {
    document.getElementById(FILL_ADDR_FINDER_BTN_ID)?.remove();
    return;
  }

  const host = (input.closest('.addr-search-content, .sui-input, .sui-input-wrapper') as HTMLElement | null) ||
    (input.parentElement as HTMLElement | null);
  if (!host) {
    document.getElementById(FILL_ADDR_FINDER_BTN_ID)?.remove();
    return;
  }

  ensureStyle();
  host.classList.add('kb-fulfil-addr-finder-host');

  let btn = document.getElementById(FILL_ADDR_FINDER_BTN_ID) as HTMLButtonElement | null;
  if (!btn) {
    btn = document.createElement('button');
    btn.id = FILL_ADDR_FINDER_BTN_ID;
    btn.type = 'button';
    btn.title = 'Fill Address Finder từ sản phẩm đang mua';
    btn.setAttribute('aria-label', 'Fill address');
    btn.innerHTML = FILL_ADDR_FINDER_ICON;
    btn.addEventListener('click', (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      void (async () => {
        const b = document.getElementById(FILL_ADDR_FINDER_BTN_ID) as HTMLButtonElement | null;
        if (!b || b.disabled) return;
        b.disabled = true;
        b.classList.remove('kb-fill-ok', 'kb-fill-bad');
        try {
          await softRefreshActiveBuyer(400);
          const res = await fillAddressFinderFromMatch();
          b.classList.add(res.ok ? 'kb-fill-ok' : 'kb-fill-bad');
          b.title = res.detail;
        } catch {
          b.classList.add('kb-fill-bad');
          b.title = 'Fill lỗi';
        } finally {
          window.setTimeout(() => {
            const again = document.getElementById(FILL_ADDR_FINDER_BTN_ID) as HTMLButtonElement | null;
            if (!again) return;
            again.disabled = false;
            again.classList.remove('kb-fill-ok', 'kb-fill-bad');
            again.title = 'Fill Address Finder từ sản phẩm đang mua';
            again.innerHTML = FILL_ADDR_FINDER_ICON;
          }, 400);
        }
      })();
    });
  }
  if (btn.parentElement !== host) {
    host.appendChild(btn);
  }
}

function syncCheckoutFillButton(): void {
  if (!isOnSheinPurchaseFlow() || !authenticated || !activeMatch) {
    removeCheckoutAddrAssistUi();
    return;
  }

  const title =
    (document.querySelector('.checkout-address-write .header-title') as HTMLElement | null) ||
    (document.querySelector('.sui-business-address .header-title') as HTMLElement | null);
  const header =
    (title?.parentElement as HTMLElement | null) ||
    (document.querySelector('.checkout-address-write .layout-header.addr-field') as HTMLElement | null) ||
    (document.querySelector('.sui-business-address .layout-header') as HTMLElement | null);
  if (!header) {
    document.getElementById(FILL_ADDR_BTN_ID)?.remove();
  } else {
    ensureStyle();
    header.classList.add('kb-fulfil-addr-header');

    let btn = document.getElementById(FILL_ADDR_BTN_ID) as HTMLButtonElement | null;
    if (!btn) {
      btn = document.createElement('button');
      btn.id = FILL_ADDR_BTN_ID;
      btn.type = 'button';
      btn.textContent = 'Auto fill';
      btn.title = 'Điền theo Sản phẩm đang mua (First / Last / Phone / Address Finder)';
      btn.addEventListener('click', (ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        void (async () => {
          const b = document.getElementById(FILL_ADDR_BTN_ID) as HTMLButtonElement | null;
          if (!b || b.disabled) return;
          b.disabled = true;
          b.classList.remove('kb-fill-ok', 'kb-fill-bad');
          try {
            // Theo đúng Sản phẩm đang mua (storage + soft refresh buyer).
            await softRefreshActiveBuyer(400);
            const res = await fillCheckoutAddressFromMatch();
            b.classList.add(res.ok ? 'kb-fill-ok' : 'kb-fill-bad');
            if (!res.ok) b.textContent = res.detail;
          } catch {
            b.classList.add('kb-fill-bad');
            b.textContent = 'Lỗi';
          } finally {
            window.setTimeout(() => {
              const again = document.getElementById(FILL_ADDR_BTN_ID) as HTMLButtonElement | null;
              if (!again) return;
              again.disabled = false;
              again.classList.remove('kb-fill-ok', 'kb-fill-bad');
              again.textContent = 'Auto fill';
            }, 450);
          }
        })();
      });
    }
    if (btn.parentElement !== header) {
      header.appendChild(btn);
    }
  }

  syncAddressFinderFillIcon();
}

function bindCheckoutAddressAssist(): void {
  if (checkoutAddrWatchBound) {
    syncCheckoutFillButton();
    return;
  }
  checkoutAddrWatchBound = true;
  syncCheckoutFillButton();

  checkoutAddrObserver = new MutationObserver(() => {
    syncCheckoutFillButton();
  });
  checkoutAddrObserver.observe(document.body, {
    childList: true,
    subtree: true,
  });
}

function unbindCheckoutAddressAssist(): void {
  if (!checkoutAddrWatchBound) return;
  checkoutAddrWatchBound = false;
  checkoutAddrObserver?.disconnect();
  checkoutAddrObserver = null;
  removeCheckoutAddrAssistUi();
}

function scheduleRender(delayMs = 120): void {
  if (renderTimer !== null) window.clearTimeout(renderTimer);
  renderTimer = window.setTimeout(() => {
    renderTimer = null;
    void renderUiWhenReady();
  }, delayMs);
}

async function syncFromStorage(): Promise<void> {
  if (!isExtensionAlive()) return;
  try {
    if (location.href !== lastHref) {
      lastHref = location.href;
      removeInject();
      document.getElementById('kb-fulfil-panel')?.remove();
      lastSelectKey = '';
      unbindSelectionWatch();
      unbindCheckoutAddressAssist();
      unbindCheckoutCostWatch();
      // SPA đổi URL: đợi paint rồi mới inject.
      scheduleRender(300);
    }

    const raw = await chrome.storage.local.get([
      AUTH_KEY,
      SOURCES_KEY,
      ACTIVE_MATCH_KEY,
      PENDING_SELECT_KEY,
    ]);
    authenticated = !!raw[AUTH_KEY];
    const nextMatch = (raw[ACTIVE_MATCH_KEY] as ActiveMatch | undefined) ?? null;
    if (
      nextMatch?.orderId !== activeMatch?.orderId ||
      nextMatch?.itemId !== activeMatch?.itemId
    ) {
      lockedFitResult = null;
      lockedFitMatchKey = '';
    }
    activeMatch = nextMatch;

    if (activeMatch?.tabUrl) {
      try {
        const matchHost = new URL(activeMatch.tabUrl).hostname;
        if (matchHost !== location.hostname) activeMatch = null;
      } catch {
        activeMatch = null;
      }
    }

    const pending = raw[PENDING_SELECT_KEY] as PendingSelect | undefined;
    const pendingKey = pending ? pendingSelectKey(pending) : '';
    const hasNewPending =
      !!pending &&
      Date.now() - (pending.at || 0) <= 120_000 &&
      pendingKey !== lastSelectKey;

    // Đã có inject → giữ vị trí/màu đã khóa; nhưng nếu pending mới (đổi đơn cùng tab) thì auto-select lại.
    if (
      document.readyState === 'complete' &&
      isSheinHost() &&
      authenticated &&
      activeMatch &&
      document.getElementById(INJECT_ID)
    ) {
      if (hasNewPending) {
        void applyPendingSelect();
      } else {
        renderSheinInject(false, { recomputeFit: false });
      }
      return;
    }

    scheduleRender(document.readyState === 'complete' ? 80 : 0);
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err ?? '');
    if (/extension context invalidated/i.test(msg)) markExtensionDead();
  }
}

chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (
    changes[AUTH_KEY] ||
    changes[SOURCES_KEY] ||
    changes[ACTIVE_MATCH_KEY] ||
    changes[PENDING_SELECT_KEY]
  ) {
    void syncFromStorage();
  }
});

if (document.readyState === 'complete') {
  void syncFromStorage();
} else {
  window.addEventListener(
    'load',
    () => {
      void syncFromStorage();
    },
    { once: true },
  );
}

// Fallback nhẹ: giữ vị trí inject/sticky; chỉ evaluate fit lần đầu nếu chưa khóa.
// Không poll recompute — SHEIN re-html khi scroll làm màu trạng thái nhấp nháy.
setInterval(() => {
  if (!isExtensionAlive()) return;
  if (document.readyState !== 'complete') return;
  if (isSheinHost() && authenticated && activeMatch && !isOnSheinPurchaseFlow()) {
    renderSheinInject(false, { recomputeFit: false });
  }
}, 1200);

setInterval(() => {
  if (!isExtensionAlive()) return;
  if (document.readyState !== 'complete') return;
  void syncFromStorage();
}, 4000);

/* ============================================================================
 * SHEIN Sync Order — bắt header + request body do capture (MAIN world) gửi sang,
 * lưu site-wide (`kb_fulfil_shein_capture`), replay order/list (+ get_order_info
 * chọn lọc). Mirror `shared/sheinOrders.ts` — content script MV3 không load ES chunks.
 * ==========================================================================*/

interface SheinCaptureState {
  headers: Record<string, string>;
  listUrl?: string;
  detailUrl?: string;
  listMethod?: string;
  listBody?: string;
  detailMethod?: string;
  detailBody?: string;
  accountEmail?: string;
  accountId?: string;
  userAgent?: string;
  at: number;
}

interface SheinCapturePayload {
  source: typeof SHEIN_CAPTURE_MSG;
  url: string;
  kind: 'list' | 'detail' | 'other';
  method?: string;
  headers: Record<string, string>;
  body?: string;
  accountEmail?: string;
  accountId?: string;
  userAgent?: string;
  at: number;
}

function isSheinCapturePayload(data: unknown): data is SheinCapturePayload {
  return (
    !!data &&
    typeof data === 'object' &&
    (data as { source?: unknown }).source === SHEIN_CAPTURE_MSG
  );
}

function isSheinListApiUrl(url: string): boolean {
  if (/\/user\/orders?\b/i.test(url)) return false;
  return (
    /\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(url) ||
    /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
      url,
    )
  );
}

function mergeSheinCapture(
  prev: SheinCaptureState | undefined,
  data: SheinCapturePayload,
): SheinCaptureState {
  const base: SheinCaptureState = prev ?? { headers: {}, at: 0 };
  const listApiUrl =
    data.kind === 'list' && isSheinListApiUrl(data.url) ? data.url : undefined;
  return {
    headers: { ...base.headers, ...data.headers },
    listUrl: listApiUrl || base.listUrl,
    detailUrl: data.kind === 'detail' ? data.url : base.detailUrl,
    listMethod: data.kind === 'list' && data.method ? data.method : base.listMethod,
    listBody: data.kind === 'list' && data.body ? data.body : base.listBody,
    detailMethod: data.kind === 'detail' && data.method ? data.method : base.detailMethod,
    detailBody: data.kind === 'detail' && data.body ? data.body : base.detailBody,
    accountEmail: data.accountEmail || base.accountEmail,
    accountId: data.accountId || base.accountId,
    userAgent: data.userAgent || base.userAgent,
    at: data.at,
  };
}

window.addEventListener('message', (event) => {
  if (event.source !== window) return;
  const data = event.data;
  if (!isSheinCapturePayload(data)) return;
  if (!isExtensionAlive()) return;
  void (async () => {
    try {
      const raw = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
      const prev = raw[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
      await chrome.storage.local.set({ [SHEIN_CAPTURE_KEY]: mergeSheinCapture(prev, data) });
    } catch {
      // ignore
    }
  })();
});

/** Đồng bộ sessionStorage bridge (capture MAIN) → chrome.storage nếu postMessage bị miss. */
async function flushSheinCaptureBridge(): Promise<void> {
  if (!isSheinHost() || !isExtensionAlive()) return;
  try {
    const raw = sessionStorage.getItem('kb_fulfil_shein_capture_bridge');
    if (!raw) return;
    const bridge = JSON.parse(raw) as SheinCaptureState;
    if (!bridge || typeof bridge !== 'object') return;
    const stored = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
    const prev = stored[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
    const merged: SheinCaptureState = {
      headers: { ...(prev?.headers || {}), ...(bridge.headers || {}) },
      listUrl:
        (bridge.listUrl && isSheinListApiUrl(bridge.listUrl) && bridge.listUrl) ||
        (prev?.listUrl && isSheinListApiUrl(prev.listUrl) && prev.listUrl) ||
        undefined,
      detailUrl: bridge.detailUrl || prev?.detailUrl,
      listMethod: bridge.listMethod || prev?.listMethod,
      listBody: bridge.listBody || prev?.listBody,
      detailMethod: bridge.detailMethod || prev?.detailMethod,
      detailBody: bridge.detailBody || prev?.detailBody,
      accountEmail: bridge.accountEmail || prev?.accountEmail,
      accountId: bridge.accountId || prev?.accountId,
      at: Math.max(bridge.at || 0, prev?.at || 0),
    };
    await chrome.storage.local.set({ [SHEIN_CAPTURE_KEY]: merged });
  } catch {
    // ignore
  }
}

if (isSheinHost()) {
  void flushSheinCaptureBridge();
  setTimeout(() => void flushSheinCaptureBridge(), 1000);
  setTimeout(() => void flushSheinCaptureBridge(), 3000);
}

/** Trần chờ một lời gọi SHEIN từ content script. */
const SHEIN_FETCH_TIMEOUT_MS = 30_000;

async function sheinFetchJson(opts: {
  url: string;
  method: string;
  headers: Record<string, string>;
  body?: string;
}): Promise<any> {
  const method = (opts.method || 'GET').toUpperCase();
  const headers: Record<string, string> = {
    accept: 'application/json, text/plain, */*',
    'x-requested-with': 'XMLHttpRequest',
  };
  for (const [k, v] of Object.entries(opts.headers || {})) {
    if (v == null || v === '') continue;
    const name = k.toLowerCase();
    if (method === 'GET' || method === 'HEAD') {
      if (name === 'content-type' || name === 'content-length') continue;
    }
    headers[name] = v;
  }
  const init: RequestInit = { method, credentials: 'include', headers };
  if (method !== 'GET' && method !== 'HEAD' && opts.body != null) {
    init.body = opts.body;
    if (!headers['content-type']) {
      headers['content-type'] = 'application/json;charset=UTF-8';
    }
  }
  // Timeout bắt buộc: một request treo ở đây giữ `sheinSyncInFlight = true` vĩnh viễn (không có
  // đường nào reset cờ đó ngoài việc promise kết thúc), nên mọi lượt sync sau đều bị chặn im lặng
  // cho tới khi người dùng tải lại tab.
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), SHEIN_FETCH_TIMEOUT_MS);
  init.signal = abort.signal;

  let res: Response;
  try {
    res = await fetch(opts.url, init);
  } catch (err) {
    if (abort.signal.aborted) {
      throw new Error(
        `SHEIN ${new URL(opts.url).pathname} quá ${Math.round(SHEIN_FETCH_TIMEOUT_MS / 1000)}s không trả lời`,
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

function buildListUrl(template: string | undefined, page: number, limit: number): string {
  const fallback = `${location.origin}/bff-api/order/list?_ver=1.1.8&_lang=en`;
  const isApi =
    !!template &&
    !/\/user\/orders?\b/i.test(template) &&
    (/\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(template) ||
      /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
        template,
      ));
  const base = isApi ? template! : fallback;
  const u = new URL(base, location.origin);
  if (!u.searchParams.has('_ver')) u.searchParams.set('_ver', '1.1.8');
  if (!u.searchParams.has('_lang')) u.searchParams.set('_lang', 'en');
  u.searchParams.set('page', String(page));
  u.searchParams.set('limit', String(limit));
  return u.href;
}

/** Phase 1: chỉ order/list — phân trang theo listOpts (mode all lấy nhiều trang hơn). */
async function fetchSheinOrderList(listOpts?: SheinListFetchOptions): Promise<{
  ok: boolean;
  orders?: SheinOrderPayload[];
  message?: string;
  pagesFetched?: number;
}> {
  if (!isSheinHost()) {
    return { ok: false, message: 'Hãy mở một trang SHEIN để đồng bộ đơn.' };
  }
  const raw = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
  const capture = raw[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
  if (!capture || !hasSheinListCapture(capture)) {
    return {
      ok: false,
      message:
        'Chưa bắt được header SHEIN. Hãy mở trang "My Orders" trên SHEIN rồi thử lại.',
    };
  }

  const limit = Math.max(1, listOpts?.pageSize ?? 50);
  const maxPages = Math.max(1, listOpts?.maxPages ?? 20);
  const pageDelayMs = Math.max(0, listOpts?.pageDelayMs ?? 0);
  const lookbackDays =
    listOpts?.lookbackDays != null && listOpts.lookbackDays > 0
      ? clampSheinLookbackDays(listOpts.lookbackDays)
      : undefined;
  const nowMs = Date.now();
  const collected: any[] = [];
  let total = Infinity;
  let pagesFetched = 0;

  try {
    for (let page = 1; page <= maxPages && collected.length < total; page++) {
      if (page > 1 && pageDelayMs > 0) await sleep(pageDelayMs);
      const listUrl = buildListUrl(undefined, page, limit);
      const json = await sheinFetchJson({
        url: listUrl,
        method: 'GET',
        headers: capture.headers,
      });
      const info = json?.info ?? {};
      const list = Array.isArray(info.order_list) ? info.order_list : [];
      pagesFetched = page;
      if (typeof info.sum === 'number') total = info.sum;

      if (lookbackDays != null) {
        if (isSheinListPageOutsideLookback(list, lookbackDays, nowMs)) break;
        const within = list.filter((o: Record<string, any>) =>
          isSheinOrderWithinLookback(extractSheinOrderedAt(o), lookbackDays, nowMs),
        );
        collected.push(...within);
        if (within.length < list.length) break;
      } else {
        collected.push(...list);
      }
      if (list.length < limit) break;
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Không lấy được danh sách đơn SHEIN.';
    // eslint-disable-next-line no-console
    console.error('[kb-fulfil] SHEIN list error:', message);
    return { ok: false, message, pagesFetched };
  }

  const orders = collected
    .map((o) => normalizeSheinOrder(o, location.origin))
    .filter((o) => o.billno);
  return { ok: true, orders, pagesFetched };
}

/** Phase 2: get_order_info — retry/adaptive nằm ở shared `enrichSheinOrdersWithDetails`. */
async function enrichSheinOrderDetails(
  orders: SheinOrderPayload[],
  billnos: string[],
  concurrency: number = SHEIN_DETAIL_CONCURRENCY_DEFAULT,
): Promise<SheinDetailEnrichResult> {
  const raw = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
  const capture = raw[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
  if (!capture?.headers || Object.keys(capture.headers).length === 0) {
    return {
      orders,
      aborted: 'Thiếu header SHEIN để gọi get_order_info.',
      incompleteBillnos: billnos.map((b) => b.trim()).filter(Boolean),
      richBillnos: [],
      sparseBillnos: [],
      notFoundBillnos: [],
    };
  }
  return enrichSheinOrdersWithDetails(
    capture,
    orders,
    billnos,
    location.origin,
    undefined,
    concurrency,
  );
}

chrome.runtime.onMessage.addListener(
  (
    message: {
      type?: string;
      billnos?: string[];
      orders?: SheinOrderPayload[];
      concurrency?: number;
    },
    _sender,
    sendResponse,
  ) => {
    if (message?.type === 'FLUSH_SHEIN_CAPTURE') {
      void (async () => {
        try {
          await flushSheinCaptureBridge();
          const raw = await chrome.storage.local.get(SHEIN_CAPTURE_KEY);
          const capture = raw[SHEIN_CAPTURE_KEY] as SheinCaptureState | undefined;
          sendResponse({
            ok: true,
            hasBody: !!(capture?.listBody && capture.listBody.length > 0),
          });
        } catch {
          sendResponse({ ok: false, hasBody: false });
        }
      })();
      return true;
    }

    if (message?.type === 'SHEIN_FETCH_ORDERS' || message?.type === 'SHEIN_FETCH_ORDERS_LIST') {
      void (async () => {
        try {
          const listOpts = (message as { listOpts?: SheinListFetchOptions }).listOpts;
          setSheinRawCaptureEnabled((message as { captureRaw?: boolean }).captureRaw === true);
          sendResponse(await fetchSheinOrderList(listOpts));
        } catch (err) {
          sendResponse({
            ok: false,
            message: err instanceof Error ? err.message : 'Lỗi đồng bộ SHEIN.',
          });
        }
      })();
      return true;
    }

    if (message?.type === 'SHEIN_FETCH_ORDER_DETAILS') {
      void (async () => {
        try {
          const billnos = Array.isArray(message.billnos) ? message.billnos : [];
          const baseOrders = Array.isArray(message.orders) ? message.orders : [];
          const concurrency = clampSheinDetailConcurrency(
            typeof message.concurrency === 'number' ? message.concurrency : undefined,
          );
          setSheinRawCaptureEnabled((message as { captureRaw?: boolean }).captureRaw === true);
          const result = await enrichSheinOrderDetails(baseOrders, billnos, concurrency);
          sendResponse({ ok: true, ...result });
        } catch (err) {
          sendResponse({
            ok: false,
            message: err instanceof Error ? err.message : 'Lỗi lấy chi tiết đơn SHEIN.',
          });
        }
      })();
      return true;
    }

    return undefined;
  },
);