import { STORAGE_KEYS } from '../shared/config';
import type {
  ActiveTabStatusResponse,
  ExtMessage,
  ListOrdersResponse,
  MarkPurchasedResponse,
  UnmarkPurchasedResponse,
  RefreshActiveBuyerResponse,
  SetActiveOrderItemResponse,
  SheinSyncConfigResponse,
  SheinSyncResponse,
  StateResponse,
  UpdateOrderMessageResponse,
  ListIssueTagsResponse,
  CreateIssueTagResponse,
  UpdateIssueTagResponse,
  DeleteIssueTagResponse,
  SetOrderIssueResponse,
} from '../shared/messages';
import type { ActiveMatch, CheckoutCostSnapshot, FulfilOrder, PendingSelect, ProductSourceOption, VariantFitSnapshot } from '../shared/types';
import {
  applyClaimItemToOrder,
  applyClaimResultToOrders,
  claimOrder,
  getAuth,
  ensureAuthRoles,
  getOrderDetail,
  getProductSources,
  getSheinSyncConfig,
  listActiveClaims,
  listNeedsPurchaseOrders,
  listPurchasedTodayOrders,
  searchOrders,
  login,
  logout,
  markOrderPurchased,
  releaseOrderClaim,
  releaseSessionClaims,
  unmarkOrderPurchased,
  postSheinOrders,
  setAuth,
  updateOrderMessage,
  listIssueTags,
  createIssueTag,
  updateIssueTag,
  deleteIssueTag,
  setOrderIssueTags,
  setOrderIssueStatus,
  ApiError,
  type FulfilClaimApiResult,
} from './kinhbacApi';
import { pushSheinSessionIfChanged } from './sheinSession';
import { detectProductPage, isCheckoutUrl, isPurchaseFlowUrl } from '../shared/productPage';
import { extractProductId } from '../shared/productId';
import { findSourceByUrl } from '../shared/domain';
import {
  extractColorLabel,
  extractSizeLabel,
  parseSellerSku,
  resolveBuyUrl,
} from '../shared/buyUrl';
import {
  clampSheinDetailConcurrency,
  enrichSheinOrdersWithDetails,
  fetchSheinOrderListFromCapture,
  hasNonCostDetailSignal,
  hasSheinListCapture,
  isRichSheinBreakdown,
  isSheinRawCaptureEnabled,
  isSheinSyncCaptureReady,
  setSheinRawCaptureEnabled,
  SHEIN_DETAIL_CONCURRENCY_DEFAULT,
  buildSheinListSnapshot,
  filterSheinListOrdersChanged,
  sheinOrdersListUrl,
  stripSheinListOrdersForPost,
  withSheinCaptureAccountEmail,
  type SheinCaptureState,
  type SheinDetailEnrichResult,
  type SheinListFetchOptions,
  type SheinOrderPayload,
} from '../shared/sheinOrders';
import { resolveSheinSyncPlan } from '../shared/sheinSyncPlan';
import { installSheinWebRequestCapture } from './sheinWebRequestCapture';

installSheinWebRequestCapture();

/** Prevent overlapping SHEIN Sync Order runs (popup auto-sync + manual). */
let sheinSyncInFlight = false;
/** Warm-up session (sau login / CTA Chuẩn bị) — không chồng lên nhau. */
let sheinPrepareInFlight = false;

const SHEIN_CAPTURE_WAIT_MS = 25_000;
const SHEIN_CAPTURE_POLL_MS = 400;
const SHEIN_TAB_LOAD_WAIT_MS = 20_000;
/** Auto Sync: làm mới session khi capture cũ hơn ngưỡng này. */
const SHEIN_CAPTURE_MAX_AGE_MS = 30 * 60 * 1000;

/** Message lỗi/timeout khi chờ tab load — hướng dẫn thao tác nút Sync. */
const SHEIN_TAB_LOAD_TIMEOUT_MSG =
  'Trang đơn load quá lâu — F5 tab SHEIN rồi bấm Chuẩn bị session.';

const SHEIN_PREPARE_FAIL_MSG =
  'Chưa bắt đủ session SHEIN (header/email/account id) — đăng nhập SHEIN rồi bấm Chuẩn bị session.';

async function setSheinSessionPreparing(preparing: boolean): Promise<void> {
  if (preparing) {
    await chrome.storage.local.set({ [STORAGE_KEYS.sheinSessionPreparing]: true });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.sheinSessionPreparing);
  }
}

/** Cờ Sync in-flight cho side panel badge — clear khi SW wake (lượt cũ chắc chắn đã chết). */
async function setSheinSyncRunning(running: boolean): Promise<void> {
  if (running) {
    await chrome.storage.local.set({ [STORAGE_KEYS.sheinSyncRunning]: true });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.sheinSyncRunning);
  }
}

void chrome.storage.local.remove(STORAGE_KEYS.sheinSyncRunning);

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Ghi trạng thái Sync đang chạy — popup lắng nghe storage để khớp với nút. */
async function publishSheinSyncProgress(message: string): Promise<void> {
  await persistSheinSyncStatus({ ok: true, message });
}

/**
 * Gắn listener TRƯỚC khi gọi `action` (reload/navigate), đợi loading → complete.
 * Tránh race: complete cháy trước khi listener kịp đăng ký → sync chạy khi trang chưa load.
 */
async function navigateAndWaitForTabLoad(
  tabId: number,
  action: () => Promise<unknown>,
  timeoutMs: number = SHEIN_TAB_LOAD_WAIT_MS,
): Promise<{ ok: boolean; message?: string }> {
  return new Promise((resolve) => {
    let done = false;
    let seenLoading = false;
    let actionStarted = false;

    const finish = (ok: boolean, message?: string) => {
      if (done) return;
      done = true;
      chrome.tabs.onUpdated.removeListener(onUpdated);
      clearTimeout(timer);
      resolve(ok ? { ok: true } : { ok: false, message });
    };

    const onUpdated = (id: number, changeInfo: chrome.tabs.TabChangeInfo) => {
      if (id !== tabId) return;
      if (changeInfo.status === 'loading') seenLoading = true;
      // Chỉ nhận complete sau khi đã kick off navigate/reload (tránh complete của trang cũ).
      if (changeInfo.status === 'complete' && actionStarted && seenLoading) finish(true);
    };

    chrome.tabs.onUpdated.addListener(onUpdated);
    const timer = setTimeout(() => {
      finish(false, SHEIN_TAB_LOAD_TIMEOUT_MSG);
    }, timeoutMs);

    void (async () => {
      try {
        const cur = await chrome.tabs.get(tabId).catch(() => null);
        if (cur?.status === 'loading') seenLoading = true;

        actionStarted = true;
        await action();

        // Poll tới khi complete (phòng miss event).
        const pollDeadline = Date.now() + timeoutMs;
        while (!done && Date.now() < pollDeadline) {
          const after = await chrome.tabs.get(tabId).catch(() => null);
          if (!after) break;
          if (after.status === 'loading') seenLoading = true;
          if (after.status === 'complete' && seenLoading) {
            finish(true);
            return;
          }
          await sleep(SHEIN_CAPTURE_POLL_MS);
        }
      } catch (err) {
        finish(false, err instanceof Error ? err.message : 'Không mở được trang đơn.');
      }
    })();
  });
}

/** Đợi tab `status === 'complete'` (dùng sau tabs.create khi không gắn được listener trước). */
async function waitUntilTabComplete(
  tabId: number,
  timeoutMs: number = SHEIN_TAB_LOAD_WAIT_MS,
): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const cur = await chrome.tabs.get(tabId).catch(() => null);
    if (cur?.status === 'complete') return true;
    await sleep(SHEIN_CAPTURE_POLL_MS);
  }
  return false;
}

/** Đợi content script trên tab trả lời (trang đã inject xong). */
async function waitForSheinContentScript(
  tabId: number,
  timeoutMs: number = 12_000,
): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = (await chrome.tabs.sendMessage(tabId, {
        type: 'FLUSH_SHEIN_CAPTURE',
      })) as { ok?: boolean } | undefined;
      if (res?.ok === true || res != null) return true;
    } catch {
      // CS chưa inject / đang navigate
    }
    await sleep(SHEIN_CAPTURE_POLL_MS);
  }
  return false;
}

/** Nhắc content/sheinBridge flush sessionStorage → chrome.storage. */
async function flushSheinCaptureFromTab(tabId: number): Promise<void> {
  try {
    await chrome.tabs.sendMessage(tabId, { type: 'FLUSH_SHEIN_CAPTURE' });
  } catch {
    // tab chưa inject / đang navigate
  }
}

/**
 * Poll storage tới khi capture sẵn sàng sync; mỗi vòng flush bridge trên tab.
 * Nếu có `minCapturedAt` — chỉ chấp nhận capture mới sau thời điểm navigate/reload.
 */
async function waitForSheinListBodyCapture(
  tabId: number | undefined,
  timeoutMs: number = SHEIN_CAPTURE_WAIT_MS,
  minCapturedAt?: number,
): Promise<SheinCaptureState | null> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (tabId != null) await flushSheinCaptureFromTab(tabId);
    const capture = await readSheinCapture();
    if (
      isSheinSyncCaptureReady(capture) &&
      (minCapturedAt == null || (capture?.at ?? 0) >= minCapturedAt)
    ) {
      return capture;
    }
    await sleep(SHEIN_CAPTURE_POLL_MS);
  }
  if (tabId != null) await flushSheinCaptureFromTab(tabId);
  const last = await readSheinCapture();
  if (
    isSheinSyncCaptureReady(last) &&
    (minCapturedAt == null || (last?.at ?? 0) >= minCapturedAt)
  ) {
    return last;
  }
  return null;
}

/**
 * Khi Sync Order (manual) mà chưa đủ header:
 * mở/reload /user/orders/list → chờ trang load xong + CS inject + bắt header mới → mới cho sync.
 * `maxAgeMs`: nếu capture sẵn nhưng quá cũ → vẫn navigate để làm mới (Auto Sync).
 * `forceRefresh`: bỏ early-return (dùng sau LOGIN Kinhbac — luôn reload SPA My Orders).
 */
async function ensureSheinListBodyCapture(opts?: {
  maxAgeMs?: number;
  forceRefresh?: boolean;
  /** Auto Sync: mở/dùng tab nền My Orders — không cướp tab đang mua. */
  preferBackground?: boolean;
}): Promise<{ ok: boolean; message?: string }> {
  const existing = await readSheinCapture();
  if (!opts?.forceRefresh && isSheinSyncCaptureReady(existing)) {
    const maxAge = opts?.maxAgeMs;
    if (
      maxAge == null ||
      (existing!.at > 0 && Date.now() - existing!.at <= maxAge)
    ) {
      return { ok: true };
    }
    // Stale — fall through to refresh session.
  }

  const preferBackground = opts?.preferBackground === true;
  const allTabs = await listSheinTabs();
  const originHint = allTabs[0]?.url;
  const targetUrl = sheinOrdersListUrl(originHint);
  const navStartedAt = Date.now();
  let tab: chrome.tabs.Tab | null = null;

  try {
    if (preferBackground) {
      // Mirror ensureSheinOrdersListTabForDetail: list → tab nền → tạo tab nền.
      // Không bao giờ navigate/reload tab đang PDP/cart/checkout.
      const existingList = await findSheinOrdersListTab();
      const inactive = allTabs.find(
        (t) =>
          t.id &&
          !t.active &&
          !t.discarded &&
          t.id !== existingList?.id &&
          !isSheinBuyingFlowTab(t.url),
      );
      const navigateTarget =
        (existingList?.id && !existingList.discarded ? existingList : null) ??
        inactive ??
        null;

      if (navigateTarget?.id) {
        tab = navigateTarget;
        const tabId = navigateTarget.id;
        const sameUrl = isSheinOrdersListNavTopUrl(navigateTarget.url);
        await publishSheinSyncProgress(
          sameUrl ? 'Đang reload trang đơn…' : 'Đang mở trang đơn SHEIN…',
        );
        const loadWait = await navigateAndWaitForTabLoad(tabId, async () => {
          if (sameUrl) {
            await chrome.tabs.reload(tabId);
          } else {
            await chrome.tabs.update(tabId, {
              url: targetUrl,
              ...(navigateTarget.active ? {} : { active: false }),
            });
          }
        });
        if (!loadWait.ok) {
          return {
            ok: false,
            message: loadWait.message ?? SHEIN_TAB_LOAD_TIMEOUT_MSG,
          };
        }
      } else {
        await publishSheinSyncProgress('Đang mở trang đơn SHEIN…');
        tab = await chrome.tabs.create({ url: targetUrl, active: false });
        if (!tab?.id) {
          return { ok: false, message: 'Không mở được tab — thử Chuẩn bị session lại.' };
        }
        await publishSheinSyncProgress('Đang chờ trang đơn load…');
        const loaded = await waitUntilTabComplete(tab.id);
        if (!loaded) {
          return { ok: false, message: SHEIN_TAB_LOAD_TIMEOUT_MSG };
        }
      }
    } else {
      tab = await findSheinTab();
      if (!tab?.id) {
        await publishSheinSyncProgress('Đang mở trang đơn SHEIN…');
        tab = await chrome.tabs.create({ url: targetUrl, active: true });
        if (!tab?.id) {
          return { ok: false, message: 'Không mở được tab — thử Chuẩn bị session lại.' };
        }
        await publishSheinSyncProgress('Đang chờ trang đơn load…');
        const loaded = await waitUntilTabComplete(tab.id);
        if (!loaded) {
          return { ok: false, message: SHEIN_TAB_LOAD_TIMEOUT_MSG };
        }
      } else {
        const tabId = tab.id;
        const sameUrl = isSheinOrdersListNavTopUrl(tab.url);
        await publishSheinSyncProgress(
          sameUrl ? 'Đang reload trang đơn…' : 'Đang mở trang đơn SHEIN…',
        );
        const loadWait = await navigateAndWaitForTabLoad(tabId, async () => {
          if (sameUrl) {
            await chrome.tabs.reload(tabId);
          } else {
            await chrome.tabs.update(tabId, { url: targetUrl, active: true });
          }
        });
        if (!loadWait.ok) {
          return {
            ok: false,
            message: loadWait.message ?? SHEIN_TAB_LOAD_TIMEOUT_MSG,
          };
        }
      }
    }
  } catch (err) {
    return {
      ok: false,
      message: err instanceof Error ? err.message : 'Không mở được trang đơn — thử Chuẩn bị session lại.',
    };
  }

  if (!tab?.id) {
    return { ok: false, message: 'Không mở được tab — thử Chuẩn bị session lại.' };
  }

  // Đợi URL đúng trang đơn (SPA có thể complete sớm rồi mới đổi path).
  await publishSheinSyncProgress('Đang chờ URL trang đơn…');
  {
    const urlDeadline = Date.now() + 10_000;
    while (Date.now() < urlDeadline) {
      const cur = await chrome.tabs.get(tab.id).catch(() => null);
      if (cur?.url && isSheinOrdersListUrl(cur.url) && cur.status === 'complete') break;
      await sleep(SHEIN_CAPTURE_POLL_MS);
    }
  }

  // Content script phải sẵn sàng trước khi sync (tránh fetch khi trang còn trắng).
  await publishSheinSyncProgress('Đang chờ trang sẵn sàng…');
  const csReady = await waitForSheinContentScript(tab.id);
  if (!csReady) {
    return {
      ok: false,
      message: 'Trang chưa sẵn sàng — F5 tab SHEIN rồi bấm Chuẩn bị session.',
    };
  }

  // SPA SHEIN gọi order/list + profile sau complete — đợi capture MỚI (header + email + accountId).
  await publishSheinSyncProgress('Đang bắt header & tài khoản SHEIN…');
  await sleep(500);
  const capture = await waitForSheinListBodyCapture(
    tab.id,
    SHEIN_CAPTURE_WAIT_MS,
    navStartedAt - 500,
  );
  if (!capture) {
    return {
      ok: false,
      message: SHEIN_PREPARE_FAIL_MSG,
    };
  }
  return { ok: true };
}

/**
 * Chỉ warm-up session (navigate My Orders + bắt capture) — không chạy Sync Order.
 * Dùng từ panel sau LOGIN (`forceRefresh`) và CTA «Chuẩn bị session».
 */
async function prepareSheinSession(opts?: {
  forceRefresh?: boolean;
}): Promise<{ ok: boolean; message?: string }> {
  if (sheinPrepareInFlight || sheinSyncInFlight) {
    return { ok: false, message: 'Đang chuẩn bị / sync — thử lại sau.' };
  }
  sheinPrepareInFlight = true;
  await setSheinSessionPreparing(true);
  try {
    const auth = await getAuth();
    if (!auth) {
      const res = { ok: false, message: 'Chưa đăng nhập.' };
      await persistSheinSyncStatus(res);
      return res;
    }
    await publishSheinSyncProgress('Đang chuẩn bị session SHEIN…');
    const prepared = await ensureSheinListBodyCapture({
      forceRefresh: opts?.forceRefresh === true,
    });
    if (!prepared.ok) {
      const res = {
        ok: false,
        message: prepared.message ?? SHEIN_PREPARE_FAIL_MSG,
      };
      await persistSheinSyncStatus(res);
      return res;
    }
    const res = { ok: true, message: 'Session SHEIN sẵn sàng — có thể Sync Order.' };
    await persistSheinSyncStatus(res);
    // Prepare OK (kể cả đã ready sẵn / forceRefresh) → bật Auto Sync, không cần tick tay.
    lastSheinCaptureReady = true;
    await enableSheinAutoSync();
    return res;
  } catch (err) {
    const res = {
      ok: false,
      message: err instanceof Error ? err.message : SHEIN_PREPARE_FAIL_MSG,
    };
    await persistSheinSyncStatus(res);
    return res;
  } finally {
    sheinPrepareInFlight = false;
    await setSheinSessionPreparing(false);
  }
}

async function readProductSources(): Promise<ProductSourceOption[]> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.productSources);
  return (raw[STORAGE_KEYS.productSources] as ProductSourceOption[] | undefined) ?? [];
}

async function persistProductSources(sources: ProductSourceOption[]): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.productSources]: sources });
}

async function persistOrdersCache(orders: FulfilOrder[]): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.ordersCache]: orders });
}

async function readOrdersCache(): Promise<FulfilOrder[]> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.ordersCache);
  const list = raw[STORAGE_KEYS.ordersCache];
  return Array.isArray(list) ? (list as FulfilOrder[]) : [];
}

async function persistActiveMatch(match: ActiveMatch | null): Promise<void> {
  if (match) {
    await chrome.storage.local.set({
      [STORAGE_KEYS.activeMatch]: match,
      [STORAGE_KEYS.activeMatchDismissed]: false,
    });
  } else {
    await chrome.storage.local.remove([
      STORAGE_KEYS.activeMatch,
      STORAGE_KEYS.variantFit,
      STORAGE_KEYS.checkoutCost,
    ]);
    // Chặn GET_ACTIVE_TAB_STATUS auto findMatchForUrl / keepMatchOnSource
    // cho đến khi user click chọn đơn lại.
    await chrome.storage.local.set({ [STORAGE_KEYS.activeMatchDismissed]: true });
  }
}

async function readActiveMatch(): Promise<ActiveMatch | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.activeMatch);
  const match = raw[STORAGE_KEYS.activeMatch] as ActiveMatch | undefined;
  return match ?? null;
}

async function isActiveMatchDismissed(): Promise<boolean> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.activeMatchDismissed);
  return raw[STORAGE_KEYS.activeMatchDismissed] === true;
}

/** Giữ match khi vẫn cùng nguồn (cart/checkout/PDP khác…). */
function keepMatchOnSource(
  stored: ActiveMatch | null,
  source: ProductSourceOption | undefined,
  sources: ProductSourceOption[],
): ActiveMatch | null {
  if (!stored || !source) return null;
  const buyUrl =
    resolveBuyUrl(stored.item) || stored.item.sourceUrl || stored.item.listingUrl || stored.tabUrl;
  const matchSource = findSourceByUrl(buyUrl, sources) ?? findSourceByUrl(stored.tabUrl, sources);
  if (matchSource?.id && matchSource.id === source.id) return stored;
  // Fallback: cùng hostname gốc với tab match trước đó
  try {
    const prevHost = new URL(stored.tabUrl).hostname.toLowerCase();
    const domains = (source.domains || '').toLowerCase();
    if (domains && prevHost && domains.split(/[\s,;]+/).some((d) => {
      const p = d.trim();
      return p && (prevHost === p || prevHost.endsWith('.' + p) || p.endsWith(prevHost));
    })) {
      return stored;
    }
  } catch {
    // ignore
  }
  return null;
}

/** Ghép lại order/item mới nhất từ cache (phone, address, zip…). */
function enrichActiveMatch(
  stored: ActiveMatch | null,
  orders: FulfilOrder[],
): ActiveMatch | null {
  if (!stored) return null;
  // Pending PDP select — chưa gắn đơn thật.
  if (stored.orderId === 'pending') return stored;
  const order = orders.find((o) => o.id === stored.orderId);
  // Không còn trong hàng đợi (đã mua / search) — giữ bản đã ghim để xem lại khách đặt.
  if (!order) return stored;
  const item = order.lineItems.find((i) => i.id === stored.itemId) ?? stored.item;
  return {
    ...stored,
    order,
    item,
    sourceName: stored.sourceName,
    productId: stored.productId,
  };
}

/** TTL giữ lựa chọn thủ công trước khi cho phép rematch theo URL trang cũ. */
const USER_PIN_TTL_MS = 60_000;

function isUserPinActive(stored: ActiveMatch | null): boolean {
  const at = stored?.userPinnedAt;
  return typeof at === 'number' && Number.isFinite(at) && Date.now() - at < USER_PIN_TTL_MS;
}

function clearUserPin(match: ActiveMatch): ActiveMatch {
  if (match.userPinnedAt == null) return match;
  const { userPinnedAt: _removed, ...rest } = match;
  return rest;
}

/** Match đã ghim có cùng product id với PDP hiện tại (SKU goodsId / productId / buy URL). */
function activeMatchMatchesProductId(
  stored: ActiveMatch,
  productId: string | null | undefined,
  sources: ProductSourceOption[],
): boolean {
  if (!productId) return false;
  if (stored.productId && stored.productId === productId) return true;
  const fromSku = parseSellerSku(stored.item.sellerSku)?.goodsId;
  if (fromSku && fromSku === productId) return true;
  const buyUrl =
    resolveBuyUrl(stored.item) || stored.item.sourceUrl || stored.item.listingUrl || '';
  if (!buyUrl) return false;
  const detection = detectProductPage(buyUrl, sources);
  if (detection.ok && detection.flow) {
    const itemPid = extractProductId(buyUrl, detection.flow);
    if (itemPid && itemPid === productId) return true;
  }
  const m = buyUrl.match(/[-/]p-(\d+)/i) || buyUrl.match(/goods_id=(\d+)/i);
  return !!m?.[1] && m[1] === productId;
}

function hasBuyerContact(order: FulfilOrder): boolean {
  return !!(
    order.buyerPhone ||
    order.buyerEmail ||
    order.address ||
    order.postalCode ||
    order.countryCode
  );
}

/** Cập nhật cache hàng đợi nếu đơn chưa mua (không nhét đơn đã mua vào queue). */
async function upsertNeedsPurchaseCache(order: FulfilOrder): Promise<void> {
  if (order.fulfilPurchasedAt) return;
  const orders = await readOrdersCache();
  const idx = orders.findIndex((o) => o.id === order.id);
  if (idx >= 0) {
    orders[idx] = order;
    await persistOrdersCache(orders);
  }
}

/** Ghim đơn/item lên panel đang mua / đã mua (không mở tab). */
async function setActiveMatchFromOrder(
  orderId: string,
  itemId?: string,
): Promise<ActiveMatch | null> {
  const sources = await readProductSources();
  const orders = await readOrdersCache();
  let order = orders.find((o) => o.id === orderId) ?? null;

  try {
    order = await getOrderDetail(orderId);
    await upsertNeedsPurchaseCache(order);
  } catch {
    // giữ bản từ cache nếu có
  }
  if (!order) return null;

  const line =
    (itemId ? order.lineItems.find((li) => li.id === itemId) : undefined) ??
    order.lineItems[0];
  if (!line) return null;

  const url = resolveBuyUrl(line) || line.sourceUrl || line.listingUrl || '';
  const parsed = parseSellerSku(line.sellerSku);
  const source = url ? findSourceByUrl(url, sources) : undefined;

  await chrome.storage.local.remove(STORAGE_KEYS.variantFit);
  await chrome.storage.local.remove(STORAGE_KEYS.checkoutCost);

  const match: ActiveMatch = {
    orderId: order.id,
    itemId: line.id,
    order,
    item: line,
    tabUrl: url || 'about:blank',
    sourceName: source?.name,
    productId: parsed?.goodsId,
    userPinnedAt: Date.now(),
  };
  await persistActiveMatch(match);
  return match;
}

async function readVariantFit(): Promise<VariantFitSnapshot | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.variantFit);
  const fit = raw[STORAGE_KEYS.variantFit] as VariantFitSnapshot | undefined;
  return fit ?? null;
}

async function readCheckoutCost(): Promise<CheckoutCostSnapshot | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.checkoutCost);
  const cost = raw[STORAGE_KEYS.checkoutCost] as CheckoutCostSnapshot | undefined;
  return cost ?? null;
}

async function clearUserCaches(): Promise<void> {
  await chrome.storage.local.remove([
    STORAGE_KEYS.productSources,
    STORAGE_KEYS.ordersCache,
    STORAGE_KEYS.activeMatch,
    STORAGE_KEYS.activeMatchDismissed,
    STORAGE_KEYS.variantFit,
    STORAGE_KEYS.checkoutCost,
  ]);
}

/**
 * Tab đang xem cạnh side panel.
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
    // ignore
  }

  const [focused] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  if (isHttpTab(focused)) return focused;

  const actives = await chrome.tabs.query({ active: true });
  return actives.find(isHttpTab) ?? null;
}

/** Tìm tab SHEIN để chạy content script fetch order (ưu tiên tab đang xem). */
async function findSheinTab(): Promise<chrome.tabs.Tab | null> {
  const tabs = await listSheinTabs();
  return tabs[0] ?? null;
}

/** Trang My Orders — Referer đúng để get_order_info trả đủ breakdown. */
function isSheinOrdersListUrl(url?: string | null): boolean {
  return !!url && /\/user\/orders\/list/i.test(url);
}

function isSheinOrdersListNavTopUrl(url?: string | null): boolean {
  return isSheinOrdersListUrl(url) && /[?&]from=navTop\b/i.test(url ?? '');
}

/**
 * Tab SHEIN đang trong luồng mua (PDP / cart / checkout) — không phải My Orders.
 * Không dùng `isPurchaseFlowUrl(..., 'shein')` vì nhánh đó khớp cả `/order` → dính list.
 */
function isSheinBuyingFlowTab(url?: string | null): boolean {
  if (!url || isSheinOrdersListUrl(url)) return false;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  if (!/shein\.com/i.test(parsed.hostname)) return false;
  // PDP: …-p-123456.html
  if (/-p-\d+/i.test(parsed.pathname)) return true;
  // Cart / checkout / pay / confirm (generic — không truyền flow=shein).
  return isPurchaseFlowUrl(url);
}

/** Tab đang ở My Orders — ưu tiên `?from=navTop`, rồi bất kỳ `/user/orders/list`. */
async function findSheinOrdersListTab(): Promise<chrome.tabs.Tab | null> {
  const tabs = await listSheinTabs();
  const withNavTop = tabs.find((t) => isSheinOrdersListNavTopUrl(t.url));
  if (withNavTop) return withNavTop;
  return tabs.find((t) => isSheinOrdersListUrl(t.url)) ?? null;
}

/**
 * Đảm bảo có tab SHEIN trên My Orders trước get_order_info.
 * Ưu tiên: tab đã ở list → tab nền không đang mua → tạo tab nền.
 * Không navigate tab PDP/cart/checkout.
 */
async function ensureSheinOrdersListTabForDetail(): Promise<{
  tabId: number | null;
  message?: string;
}> {
  const existing = await findSheinOrdersListTab();
  if (existing?.id) {
    const ready = await waitForSheinContentScript(existing.id, 2_500);
    if (ready) return { tabId: existing.id };
    // Tab list còn sống nhưng CS chưa sẵn — đợi thêm trước khi navigate chỗ khác.
    const slowReady = await waitForSheinContentScript(existing.id, 8_000);
    if (slowReady) return { tabId: existing.id };
  }

  const allTabs = await listSheinTabs();
  const originHint = allTabs[0]?.url ?? existing?.url;
  const targetUrl = sheinOrdersListUrl(originHint);

  // Ưu tiên: tab list đã biết (CS chậm) → tab nền không đang mua → tab live không đang mua.
  // Không navigate tab PDP/cart/checkout — tạo tab nền thay thế.
  const inactive = allTabs.find(
    (t) =>
      t.id &&
      !t.active &&
      !t.discarded &&
      t.id !== existing?.id &&
      !isSheinBuyingFlowTab(t.url),
  );
  const anyLiveNonBuying = allTabs.find(
    (t) => t.id && !t.discarded && !isSheinBuyingFlowTab(t.url),
  );
  const navigateTarget =
    (existing?.id && !existing.discarded ? existing : null) ??
    inactive ??
    anyLiveNonBuying ??
    null;

  let tabId: number | null = null;

  try {
    if (navigateTarget?.id) {
      tabId = navigateTarget.id;
      const sameUrl = isSheinOrdersListNavTopUrl(navigateTarget.url);
      await publishSheinSyncProgress(
        sameUrl ? 'Đang reload trang đơn để lấy chi tiết…' : 'Đang mở trang đơn để lấy chi tiết…',
      );
      const loadWait = await navigateAndWaitForTabLoad(tabId, async () => {
        if (sameUrl) {
          await chrome.tabs.reload(tabId!);
        } else {
          // inactive: giữ focus; active chỉ khi tab đã ở list / không phải luồng mua.
          await chrome.tabs.update(tabId!, {
            url: targetUrl,
            ...(navigateTarget.active ? {} : { active: false }),
          });
        }
      });
      if (!loadWait.ok) {
        return {
          tabId: null,
          message: loadWait.message ?? SHEIN_TAB_LOAD_TIMEOUT_MSG,
        };
      }
    } else {
      await publishSheinSyncProgress('Đang mở trang đơn để lấy chi tiết…');
      const created = await chrome.tabs.create({ url: targetUrl, active: false });
      if (!created?.id) {
        return { tabId: null, message: 'Không mở được tab My Orders.' };
      }
      tabId = created.id;
      const loaded = await waitUntilTabComplete(tabId);
      if (!loaded) {
        return { tabId: null, message: SHEIN_TAB_LOAD_TIMEOUT_MSG };
      }
    }
  } catch (err) {
    return {
      tabId: null,
      message: err instanceof Error ? err.message : 'Không mở được trang đơn để lấy chi tiết.',
    };
  }

  if (tabId == null) {
    return { tabId: null, message: 'Không mở được tab My Orders.' };
  }

  // SPA có thể complete sớm rồi mới đổi path.
  const urlDeadline = Date.now() + 10_000;
  while (Date.now() < urlDeadline) {
    const cur = await chrome.tabs.get(tabId).catch(() => null);
    if (cur?.url && isSheinOrdersListUrl(cur.url) && cur.status === 'complete') break;
    await sleep(SHEIN_CAPTURE_POLL_MS);
  }

  const csReady = await waitForSheinContentScript(tabId);
  if (!csReady) {
    return {
      tabId: null,
      message: 'Trang đơn chưa sẵn sàng — F5 tab SHEIN rồi thử lại.',
    };
  }

  return { tabId };
}

/** Mọi tab SHEIN còn sống — active trước, bỏ tab discarded xuống cuối. */
async function listSheinTabs(): Promise<chrome.tabs.Tab[]> {
  const isShein = (tab: chrome.tabs.Tab | undefined | null): tab is chrome.tabs.Tab =>
    !!tab?.id && !!tab.url && /^https?:\/\/([^/]+\.)?shein\.com\//i.test(tab.url);

  const active = await getActiveHttpTab();
  const all = await chrome.tabs.query({ url: '*://*.shein.com/*' });
  const httpTabs = all.filter(isShein);

  const ranked: chrome.tabs.Tab[] = [];
  const seen = new Set<number>();
  const push = (tab: chrome.tabs.Tab | null | undefined) => {
    if (!isShein(tab) || seen.has(tab.id!)) return;
    seen.add(tab.id!);
    ranked.push(tab);
  };

  push(active);
  for (const t of httpTabs.filter((t) => t.active && !t.discarded)) push(t);
  for (const t of httpTabs.filter((t) => !t.discarded)) push(t);
  for (const t of httpTabs) push(t);
  return ranked;
}

async function readSheinCapture(): Promise<SheinCaptureState | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinCapture);
  return (raw[STORAGE_KEYS.sheinCapture] as SheinCaptureState | undefined) ?? null;
}

/** Gọi content script lấy list đơn SHEIN; fallback capture khi không nối được CS. */
async function fetchSheinOrderListFromTab(
  listOpts?: SheinListFetchOptions,
): Promise<{
  ok: boolean;
  orders?: SheinOrderPayload[];
  message?: string;
  pagesFetched?: number;
}> {
  const capture = await readSheinCapture();
  const tabs = await listSheinTabs();

  for (const tab of tabs) {
    if (!tab.id || tab.discarded) continue;
    // Tab nền / vừa reload extension: CS có thể chưa inject — đợi ngắn rồi mới gửi.
    const ready = await waitForSheinContentScript(tab.id, 2_500);
    if (!ready) continue;
    try {
      const res = (await chrome.tabs.sendMessage(tab.id, {
        type: 'SHEIN_FETCH_ORDERS_LIST',
        listOpts,
        captureRaw: isSheinRawCaptureEnabled(),
      })) as {
        ok: boolean;
        orders?: SheinOrderPayload[];
        message?: string;
        pagesFetched?: number;
      };
      if (res?.ok) return res;
      // CS trả lỗi nghiệp vụ — thử tab khác trước khi fallback capture.
      if (!res?.ok && tabs.length > 1) continue;
      if (hasSheinListCapture(capture) && capture) {
        const fallback = await fetchSheinOrderListFromCapture(capture, undefined, listOpts);
        if (fallback.ok) return fallback;
      }
      return res ?? { ok: false, message: 'Không phản hồi từ trang SHEIN.' };
    } catch {
      // Thử tab SHEIN khác
    }
  }

  if (hasSheinListCapture(capture) && capture) {
    return fetchSheinOrderListFromCapture(capture, undefined, listOpts);
  }

  if (tabs.length > 0) {
    return {
      ok: false,
      message: 'Chưa kết nối được tab SHEIN — F5 trang SHEIN rồi thử lại.',
    };
  }

  return { ok: false, message: 'Chưa mở tab SHEIN.' };
}

/**
 * POST shein-sync theo lô. Mỗi lô nạp lại pool đơn local ở backend nên lô quá nhỏ
 * khiến chi phí đó lặp nhiều lần; ngân sách Auto Mark Ship mỗi request vẫn giới hạn
 * riêng nên lô lớn hơn không kéo dài thêm phần gọi remote.
 *
 * Đo thực tế (mode tất cả, pool 490 đơn): riêng query pool tốn 4–14s mỗi request nên
 * 169 đơn chia 5 lô mất ~49s. Vẫn giữ trần vì khi bật "lưu payload gốc" mỗi đơn nặng
 * ~15KB, tài khoản nhiều đơn sẽ tạo body quá lớn nếu gộp hết vào một request.
 */
const SHEIN_SYNC_LIST_BATCH_SIZE = 120;
const SHEIN_SYNC_DETAIL_BATCH_SIZE = 25;

/** Số billno mỗi lần nhờ content script gọi get_order_info. */
const SHEIN_DETAIL_CHUNK_SIZE = 25;

/** Trần thời gian một lượt sync — hết giờ thì để phần còn lại cho lượt sau. */
const SHEIN_SYNC_DEADLINE_MS = 5 * 60_000;

/** Mã tra cứu nhật ký lượt sync — ngắn để in vừa dòng thông báo dưới nút Sync Order. */
function newSheinRunId(): string {
  const rand =
    typeof crypto?.randomUUID === 'function'
      ? crypto.randomUUID().replace(/-/g, '').slice(0, 8)
      : Math.random().toString(36).slice(2, 10);
  const stamp = new Date().toISOString().replace(/[-:T.Z]/g, '').slice(2, 12);
  return `ext-${stamp}-${rand}`;
}

/** Nhãn tiếng Việt cho mã lý do bỏ qua từ backend (`Shared.Constants.SheinSkipReasons`). */
const SHEIN_SKIP_LABELS: Record<string, string> = {
  no_local_match: 'không khớp pool TikTok',
  sibling_linked: 'cùng khách',
  terminal_unlinked: 'huỷ/hoàn (đã gỡ)',
  unpaid_ghost: 'chưa thanh toán',
  membership_virtual: 'Club/Saver',
  missing_address: 'thiếu địa chỉ',
  address_conflict: 'lệch phone/zip',
  ambiguous_match: 'khớp nhập nhằng',
  duplicate_billno: 'mã thuộc đơn khác',
  purchased_before_order: 'mua trước khi đặt đơn',
  ledger_deleted: 'đã khóa (xoá sổ cái — dữ liệu cũ)',
  manual_detach: 'đã gỡ tay',
  operator_ignored: 'đã bỏ qua bởi người xử lý',
  awaiting_detail: 'chờ chi tiết lần đầu',
  rebalanced_to_sibling: 'chuyển sang đơn cùng khách',
  manual_link_locked: 'giữ theo đơn gắn tay',
  platform_canceled: 'đơn sàn đã hủy — mua sau hủy',
  moved_off_canceled_order: 'chuyển khỏi đơn đã hủy',
};

/**
 * Dòng "chưa gắn" của thông báo sync: ba nhóm lớn nhất, phần còn lại gộp `+N khác`.
 *
 * Bản cũ chỉ đếm hai loại nên `khớp + bỏ qua` không bao giờ bằng số đơn đã lấy và không ai biết 11
 * đơn còn lại đi đâu; giờ tổng luôn khớp và phần lệch được nói thẳng.
 *
 * Chữ "bỏ qua" cũng đã đổi thành "chưa gắn" kèm chỗ xử lý tiếp: mọi billno nay đều được ghi vào
 * `SupplierPurchases` trước khi chạy khớp, nên không còn cái nào bị mất — chúng nằm ở hàng đợi chờ
 * gắn tay. Đọc "bỏ qua 20" mà không biết đi đâu tìm là lý do 20 lần mua thật từng bị coi như không
 * tồn tại.
 *
 * `awaiting_detail` không tính vào "chưa gắn" — đó là hoãn khớp chờ get_order_info trong cùng lượt,
 * không phải hàng đợi gắn tay.
 */
function formatSheinSkipNote(skipCounts: Record<string, number>): string {
  const entries = Object.entries(skipCounts)
    .filter(([reason, count]) => count > 0 && reason !== 'awaiting_detail')
    .sort((a, b) => b[1] - a[1]);
  if (entries.length === 0) return '';

  const total = entries.reduce((sum, [, count]) => sum + count, 0);
  const shown = entries
    .slice(0, 3)
    .map(([reason, count]) => `${count} ${SHEIN_SKIP_LABELS[reason] ?? reason}`);
  const rest = entries.slice(3).reduce((sum, [, count]) => sum + count, 0);
  if (rest > 0) shown.push(`+${rest} khác`);
  return ` · chưa gắn ${total} (${shown.join(' · ')}) — xem trang Đơn chưa khớp`;
}

async function postSheinOrdersInBatches(
  orders: SheinOrderPayload[],
  phase: 'list' | 'detail',
  batchSize = phase === 'detail' ? SHEIN_SYNC_DETAIL_BATCH_SIZE : SHEIN_SYNC_LIST_BATCH_SIZE,
  updateMode?: 'new' | 'all',
  runId?: string,
  // Phase detail gọi hàm này một lần cho mỗi chunk 25 billno, nên `i` luôn bắt đầu từ 0 và mọi lô
  // đều mang batchIndex = 1. Backend coi batchIndex ≤ 1 là "lô đầu của lượt" và chạy ExecuteDelete
  // dọn nhật ký, tức dọn lại ở từng chunk; nhật ký cũng không phân biệt được các lô với nhau.
  batchIndexStart = 1,
): Promise<{
  fetched: number;
  matched: number;
  updatedTracking: number;
  autoShipped: number;
  autoShipFailed: number;
  autoShipBlocked: number;
  updatedCost: number;
  unmatched: number;
  unchanged: number;
  batches: number;
  /** Billno thuộc lô mà backend nhận nhưng không ghi được — phải gửi lại lượt sau. */
  saveFailedBillnos: string[];
  unmatchedBillnos: string[];
  needsDetailBillnos: string[];
  incompleteCostBillnos: string[];
  skipCounts: Record<string, number>;
  skipReasonByBillno: Record<string, string>;
  skipOutcomes: { billno: string; reason: string; detail?: string | null }[];
  needsDetailReasons: Record<string, string>;
  autoShipNotes: string[];
}> {
  const merged = {
    fetched: 0,
    matched: 0,
    updatedTracking: 0,
    autoShipped: 0,
    autoShipFailed: 0,
    autoShipBlocked: 0,
    updatedCost: 0,
    unmatched: 0,
    unchanged: 0,
    batches: 0,
    saveFailedBillnos: [] as string[],
    unmatchedBillnos: [] as string[],
    needsDetailBillnos: [] as string[],
    incompleteCostBillnos: [] as string[],
    skipCounts: {} as Record<string, number>,
    skipReasonByBillno: {} as Record<string, string>,
    skipOutcomes: [] as { billno: string; reason: string; detail?: string | null }[],
    needsDetailReasons: {} as Record<string, string>,
    autoShipNotes: [] as string[],
  };
  const needSet = new Set<string>();
  const unmatchedSet = new Set<string>();
  const incompleteSet = new Set<string>();
  const capture = await readSheinCapture();
  const ordersToPost = withSheinCaptureAccountEmail(orders, capture?.accountEmail);

  for (let i = 0; i < ordersToPost.length; i += batchSize) {
    const chunk = ordersToPost.slice(i, i + batchSize);
    merged.batches += 1;
    const part = await postSheinOrders(
      chunk,
      phase,
      updateMode,
      runId,
      batchIndexStart + Math.floor(i / batchSize),
    );
    if (part.saveFailed) {
      merged.saveFailedBillnos.push(...chunk.map((o) => o.billno.trim()).filter(Boolean));
    }
    merged.fetched += part.fetched;
    merged.matched += part.matched;
    merged.updatedTracking += part.updatedTracking;
    merged.autoShipped += part.autoShipped ?? 0;
    merged.autoShipFailed += part.autoShipFailed ?? 0;
    merged.autoShipBlocked += part.autoShipBlocked ?? 0;
    merged.updatedCost += part.updatedCost;
    merged.unmatched += part.unmatched;
    merged.unchanged += part.unchanged ?? 0;
    for (const [reason, count] of Object.entries(part.skipCounts ?? {})) {
      merged.skipCounts[reason] = (merged.skipCounts[reason] ?? 0) + count;
    }
    for (const outcome of part.skipOutcomes ?? []) {
      const key = outcome.billno.trim().toUpperCase();
      if (key) merged.skipReasonByBillno[key] = outcome.reason;
      merged.skipOutcomes.push(outcome);
    }
    for (const [billno, reason] of Object.entries(part.needsDetailReasons ?? {})) {
      const key = billno.trim().toUpperCase();
      if (key && reason) merged.needsDetailReasons[key] = reason;
    }
    for (const b of part.unmatchedBillnos ?? []) {
      const key = b.trim().toUpperCase();
      if (key && !unmatchedSet.has(key)) {
        unmatchedSet.add(key);
        merged.unmatchedBillnos.push(b.trim());
      }
    }
    for (const b of part.needsDetailBillnos ?? []) {
      const key = b.trim().toUpperCase();
      if (key && !needSet.has(key)) {
        needSet.add(key);
        merged.needsDetailBillnos.push(b.trim());
      }
    }
    for (const b of part.incompleteCostBillnos ?? []) {
      const key = b.trim().toUpperCase();
      if (key && !incompleteSet.has(key)) {
        incompleteSet.add(key);
        merged.incompleteCostBillnos.push(b.trim());
      }
    }
    for (const n of part.notes ?? []) {
      if (/Auto Mark Shipped|Mark Shipped thủ công|cần xử lý thủ công/i.test(n)) {
        merged.autoShipNotes.push(n);
      }
    }
  }

  return merged;
}

/** Phase 2 detail: get_order_info từ tab My Orders (Referer đúng); fallback SW capture. */
async function enrichSheinDetailsFromTab(
  orders: SheinOrderPayload[],
  billnos: string[],
  concurrency: number = SHEIN_DETAIL_CONCURRENCY_DEFAULT,
): Promise<SheinDetailEnrichResult> {
  if (!billnos.length) {
    return {
      orders,
      incompleteBillnos: [],
      richBillnos: [],
      sparseBillnos: [],
      notFoundBillnos: [],
    };
  }
  const capture = await readSheinCapture();
  const workers = clampSheinDetailConcurrency(concurrency);

  const ensured = await ensureSheinOrdersListTabForDetail();
  const triedTabIds = new Set<number>();

  const tryFetchOnTab = async (
    tabId: number,
  ): Promise<SheinDetailEnrichResult | null> => {
    if (triedTabIds.has(tabId)) return null;
    triedTabIds.add(tabId);
    try {
      const res = (await chrome.tabs.sendMessage(tabId, {
        type: 'SHEIN_FETCH_ORDER_DETAILS',
        billnos,
        orders,
        concurrency: workers,
        captureRaw: isSheinRawCaptureEnabled(),
      })) as SheinDetailEnrichResult & {
        ok?: boolean;
        message?: string;
      };
      if (res?.orders) {
        return {
          orders: res.orders,
          aborted: res.aborted,
          incompleteBillnos: res.incompleteBillnos ?? [],
          richBillnos: res.richBillnos ?? [],
          sparseBillnos: res.sparseBillnos ?? [],
          notFoundBillnos: res.notFoundBillnos ?? [],
        };
      }
    } catch {
      // thử tab list khác / fallback SW
    }
    return null;
  };

  if (ensured.tabId != null) {
    const primary = await tryFetchOnTab(ensured.tabId);
    if (primary) return primary;
  }

  // Fallback: mọi tab đang ở My Orders (tránh gửi sang PDP).
  for (const tab of await listSheinTabs()) {
    if (!tab.id || tab.discarded || !isSheinOrdersListUrl(tab.url)) continue;
    const ready = await waitForSheinContentScript(tab.id, 2_500);
    if (!ready) continue;
    const res = await tryFetchOnTab(tab.id);
    if (res) return res;
  }

  if (capture && Object.keys(capture.headers || {}).length > 0) {
    return enrichSheinOrdersWithDetails(
      capture,
      orders,
      billnos,
      undefined,
      undefined,
      workers,
    );
  }

  return {
    orders,
    aborted: ensured.message ?? 'Thiếu session SHEIN.',
    incompleteBillnos: billnos.map((b) => b.trim()).filter(Boolean),
    richBillnos: [],
    sparseBillnos: [],
    notFoundBillnos: [],
  };
}

/**
 * Billno chưa lấy đủ breakdown, chờ thử lại ở lượt sync sau.
 * `attempts` chặn đơn không bao giờ có detail (free, đã huỷ, billno chết) khỏi
 * nằm mãi trong hàng đợi và làm mỗi lượt sync chậm dần.
 */
interface SheinIncompleteEntry {
  billno: string;
  attempts: number;
  firstSeenAt: number;
}

/** Cost-incomplete: retry lâu hơn vì đơn mới có thể chưa có priceDetailList ngay. */
const SHEIN_INCOMPLETE_MAX_ATTEMPTS = 10;
const SHEIN_INCOMPLETE_TTL_MS = 7 * 24 * 60 * 60 * 1000;
const SHEIN_INCOMPLETE_MAX_ENTRIES = 100;

async function readIncompleteEntries(): Promise<SheinIncompleteEntry[]> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinIncompleteBillnos);
  const list = raw[STORAGE_KEYS.sheinIncompleteBillnos];
  if (!Array.isArray(list)) return [];
  const now = Date.now();
  const entries: SheinIncompleteEntry[] = [];
  for (const item of list) {
    // Format cũ là mảng string — coi như vừa gặp lần đầu.
    const billno = typeof item === 'string' ? item.trim() : String(item?.billno ?? '').trim();
    if (!billno) continue;
    const attempts = typeof item === 'string' ? 0 : Number(item?.attempts);
    const firstSeenAt = typeof item === 'string' ? now : Number(item?.firstSeenAt);
    const entry: SheinIncompleteEntry = {
      billno,
      attempts: Number.isFinite(attempts) && attempts > 0 ? Math.floor(attempts) : 0,
      firstSeenAt: Number.isFinite(firstSeenAt) && firstSeenAt > 0 ? firstSeenAt : now,
    };
    if (now - entry.firstSeenAt > SHEIN_INCOMPLETE_TTL_MS) continue;
    entries.push(entry);
  }
  return entries
    .sort((a, b) => b.firstSeenAt - a.firstSeenAt)
    .slice(0, SHEIN_INCOMPLETE_MAX_ENTRIES);
}

/** Billno còn đáng thử lại (chưa hết lượt, chưa quá hạn). */
async function readIncompleteBillnos(): Promise<string[]> {
  const entries = await readIncompleteEntries();
  return entries
    .filter((e) => e.attempts < SHEIN_INCOMPLETE_MAX_ATTEMPTS)
    .map((e) => e.billno);
}

/**
 * `stillIncomplete` tăng `attempts`; billno trong `attempted` mà không còn thiếu thì xoá.
 * `deferred` (chưa kịp gọi vì hết giờ) được xếp hàng mà không tính là một lần thử.
 * Billno ngoài `attempted` giữ nguyên để không mất lịch sử của lượt sync khác.
 */
async function persistIncompleteBillnos(
  stillIncomplete: string[],
  attempted: string[],
  deferred: string[] = [],
): Promise<void> {
  const prior = await readIncompleteEntries();
  const priorByKey = new Map(prior.map((e) => [e.billno.toUpperCase(), e]));
  const attemptedKeys = new Set(
    attempted.map((b) => b.trim().toUpperCase()).filter(Boolean),
  );
  const now = Date.now();
  const next: SheinIncompleteEntry[] = [];
  const seen = new Set<string>();

  for (const raw of stillIncomplete) {
    const billno = raw.trim();
    const key = billno.toUpperCase();
    if (!key || seen.has(key)) continue;
    seen.add(key);
    const before = priorByKey.get(key);
    next.push({
      billno,
      attempts: (before?.attempts ?? 0) + 1,
      firstSeenAt: before?.firstSeenAt ?? now,
    });
  }

  for (const raw of deferred) {
    const billno = raw.trim();
    const key = billno.toUpperCase();
    if (!key || seen.has(key)) continue;
    seen.add(key);
    const before = priorByKey.get(key);
    next.push({
      billno,
      attempts: before?.attempts ?? 0,
      firstSeenAt: before?.firstSeenAt ?? now,
    });
  }

  for (const entry of prior) {
    const key = entry.billno.toUpperCase();
    if (seen.has(key) || attemptedKeys.has(key)) continue;
    seen.add(key);
    next.push(entry);
  }

  const capped = next
    .sort((a, b) => b.firstSeenAt - a.firstSeenAt)
    .slice(0, SHEIN_INCOMPLETE_MAX_ENTRIES);
  if (capped.length === 0) {
    await chrome.storage.local.remove(STORAGE_KEYS.sheinIncompleteBillnos);
  } else {
    await chrome.storage.local.set({ [STORAGE_KEYS.sheinIncompleteBillnos]: capped });
  }
}

async function persistSheinSyncStatus(status: SheinSyncResponse): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.sheinSyncStatus]: status });
}

function findMatchForUrl(
  url: string,
  orders: FulfilOrder[],
  sources: ProductSourceOption[],
): ActiveMatch | null {
  const detection = detectProductPage(url, sources);
  if (!detection.ok || !detection.flow) return null;

  const productId = extractProductId(url, detection.flow);
  if (!productId) return null;

  for (const order of orders) {
    for (const item of order.lineItems) {
      const parsed = parseSellerSku(item.sellerSku);
      // Match goodsId từ SKU (SHEIN a-p-{id}) hoặc product id trong SourceUrl.
      if (parsed?.goodsId === productId) {
        return {
          orderId: order.id,
          itemId: item.id,
          order,
          item,
          tabUrl: url,
          sourceName: detection.source?.name,
          productId,
        };
      }

      const buyUrl = item.sourceUrl || item.listingUrl;
      if (!buyUrl) continue;
      const itemSource = findSourceByUrl(buyUrl, sources);
      if (itemSource && itemSource.id !== detection.source?.id) continue;

      const itemProductId = extractProductId(buyUrl, detection.flow);
      if (itemProductId && itemProductId === productId) {
        return {
          orderId: order.id,
          itemId: item.id,
          order,
          item,
          tabUrl: url,
          sourceName: detection.source?.name,
          productId,
        };
      }
    }
  }

  // Fallback: cùng source, chỉ có 1 line item trong queue → coi là match.
  const sameSourceItems: ActiveMatch[] = [];
  for (const order of orders) {
    for (const item of order.lineItems) {
      const buyUrl = resolveBuyUrl(item) || item.sourceUrl || item.listingUrl;
      if (!buyUrl) continue;
      const itemSource =
        findSourceByUrl(item.sourceUrl || item.listingUrl || buyUrl, sources) ??
        detection.source;
      if (itemSource?.id === detection.source?.id) {
        sameSourceItems.push({
          orderId: order.id,
          itemId: item.id,
          order,
          item,
          tabUrl: url,
          sourceName: detection.source?.name,
          productId,
        });
      }
    }
  }
  return sameSourceItems.length === 1 ? sameSourceItems[0]! : null;
}

async function buildState(): Promise<StateResponse> {
  let auth = await getAuth();
  if (!auth) {
    return { authenticated: false, user: null, productSources: [] };
  }
  auth = await ensureAuthRoles(auth);
  const sources = await readProductSources();
  return {
    authenticated: true,
    user: auth.user,
    productSources: sources,
  };
}

async function refreshSources(): Promise<ProductSourceOption[]> {
  const sources = await getProductSources();
  await persistProductSources(sources);
  return sources;
}

chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true }).catch(() => undefined);

chrome.action.onClicked.addListener((tab) => {
  if (tab.windowId !== undefined) {
    chrome.sidePanel.open({ windowId: tab.windowId }).catch(() => undefined);
  }
});

/** Alarm name — Auto Sync SHEIN chạy ngầm khi đóng side panel. */
const SHEIN_AUTO_SYNC_ALARM = 'kb-fulfil-shein-auto-sync';
/** Chrome alarms thường không dưới ~30s; panel vẫn hiện chu kỳ Settings. */
const SHEIN_AUTO_SYNC_MIN_INTERVAL_SEC = 30;

async function isSheinAutoSyncEnabled(): Promise<boolean> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinAutoSync);
  return raw[STORAGE_KEYS.sheinAutoSync] === true;
}

/**
 * Bật Auto Sync khi session SHEIN vừa sẵn sàng (header + email + account id).
 * Không ghi đè nếu đã bật; listener storage sẽ arm alarm.
 */
async function enableSheinAutoSync(): Promise<void> {
  const auth = await getAuth();
  if (!auth) return;
  if (await isSheinAutoSyncEnabled()) return;
  await chrome.storage.local.set({ [STORAGE_KEYS.sheinAutoSync]: true });
}

/** Theo dõi chuyển not-ready → ready để tự bật Auto Sync (cho phép tắt tay khi vẫn ready). */
let lastSheinCaptureReady: boolean | null = null;

void (async () => {
  try {
    lastSheinCaptureReady = isSheinSyncCaptureReady(await readSheinCapture());
  } catch {
    lastSheinCaptureReady = false;
  }
})();

function onSheinCaptureReadyChanged(nextReady: boolean): void {
  // Chưa seed xong → bỏ qua (không ghi đè); prepareSheinSession vẫn bật khi warm-up OK.
  if (lastSheinCaptureReady === null) return;
  const was = lastSheinCaptureReady;
  lastSheinCaptureReady = nextReady;
  if (!was && nextReady) void enableSheinAutoSync();
}

async function resolveSheinAutoSyncIntervalSec(): Promise<number> {
  try {
    const cfg = await getSheinSyncConfig();
    const sec = cfg.intervalSeconds > 0 ? cfg.intervalSeconds : 60;
    return Math.max(SHEIN_AUTO_SYNC_MIN_INTERVAL_SEC, sec);
  } catch {
    return 60;
  }
}

async function clearSheinAutoSyncSchedule(): Promise<void> {
  await chrome.alarms.clear(SHEIN_AUTO_SYNC_ALARM);
  await chrome.storage.local.remove(STORAGE_KEYS.sheinNextAutoSyncAt);
}

/**
 * Lên lịch lần Auto Sync kế tiếp (one-shot alarm).
 * `delaySec` — ghi đè chu kỳ (vd. retry sớm khi đang in-flight).
 */
async function armSheinAutoSyncSchedule(opts?: { delaySec?: number }): Promise<void> {
  if (!(await isSheinAutoSyncEnabled())) {
    await clearSheinAutoSyncSchedule();
    return;
  }
  const auth = await getAuth();
  if (!auth) {
    await clearSheinAutoSyncSchedule();
    return;
  }

  const intervalSec =
    opts?.delaySec != null && opts.delaySec > 0
      ? Math.max(SHEIN_AUTO_SYNC_MIN_INTERVAL_SEC, opts.delaySec)
      : await resolveSheinAutoSyncIntervalSec();
  const when = Date.now() + intervalSec * 1000;
  await chrome.storage.local.set({ [STORAGE_KEYS.sheinNextAutoSyncAt]: when });
  await chrome.alarms.create(SHEIN_AUTO_SYNC_ALARM, { when });
}

/** Khôi phục alarm sau SW wake / reload extension nếu checkbox vẫn bật. */
async function ensureSheinAutoSyncSchedule(): Promise<void> {
  if (!(await isSheinAutoSyncEnabled())) {
    await clearSheinAutoSyncSchedule();
    return;
  }
  const auth = await getAuth();
  if (!auth) {
    await clearSheinAutoSyncSchedule();
    return;
  }
  const existing = await chrome.alarms.get(SHEIN_AUTO_SYNC_ALARM);
  if (existing?.scheduledTime) {
    await chrome.storage.local.set({
      [STORAGE_KEYS.sheinNextAutoSyncAt]: existing.scheduledTime,
    });
    return;
  }
  await armSheinAutoSyncSchedule();
}

async function handleSheinAutoSyncAlarm(): Promise<void> {
  if (!(await isSheinAutoSyncEnabled())) {
    await clearSheinAutoSyncSchedule();
    return;
  }
  const auth = await getAuth();
  if (!auth) {
    await clearSheinAutoSyncSchedule();
    return;
  }

  if (sheinSyncInFlight) {
    await armSheinAutoSyncSchedule({ delaySec: SHEIN_AUTO_SYNC_MIN_INTERVAL_SEC });
    return;
  }

  // Panel đếm ngược: null = đang sync.
  await chrome.storage.local.remove(STORAGE_KEYS.sheinNextAutoSyncAt);
  // runSheinSync: Auto tự ensureCapture khi thiếu/stale; finally arm lại chu kỳ.
  await runSheinSync({ manual: false, ensureCapture: false });
}

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name !== SHEIN_AUTO_SYNC_ALARM) return;
  void handleSheinAutoSyncAlarm();
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (changes[STORAGE_KEYS.sheinCapture]) {
    onSheinCaptureReadyChanged(
      isSheinSyncCaptureReady(
        changes[STORAGE_KEYS.sheinCapture].newValue as SheinCaptureState | undefined,
      ),
    );
  }
  if (!changes[STORAGE_KEYS.sheinAutoSync]) return;
  if (changes[STORAGE_KEYS.sheinAutoSync].newValue === true) {
    void armSheinAutoSyncSchedule({ delaySec: SHEIN_AUTO_SYNC_MIN_INTERVAL_SEC });
  } else {
    void clearSheinAutoSyncSchedule();
  }
});

chrome.runtime.onInstalled.addListener(() => {
  void ensureSheinAutoSyncSchedule();
});
chrome.runtime.onStartup.addListener(() => {
  void ensureSheinAutoSyncSchedule();
});
void ensureSheinAutoSyncSchedule();

/** Chạy Sync Order (manual hoặc auto). Dùng chung message handler + chrome.alarms. */
async function runSheinSync(message: {
  ensureCapture?: boolean;
  manual?: boolean;
}): Promise<SheinSyncResponse> {

          if (sheinSyncInFlight) {
            const res: SheinSyncResponse = {
              ok: false,
              message: 'Sync đang chạy — thử lại sau.',
            };
            return res;
          }
          sheinSyncInFlight = true;
          await setSheinSyncRunning(true);
          const syncDeadline = Date.now() + SHEIN_SYNC_DEADLINE_MS;
          // Một mã cho cả lượt (2 POST list + N POST detail) để backend gộp được nhật ký;
          // in ra thông báo để tra `dev/diagnostics/shein-sync-runs/{runId}` khi số liệu lạ.
          const runId = newSheinRunId();
          try {
          const auth = await getAuth();
          if (!auth) {
            const res: SheinSyncResponse = { ok: false, message: 'Chưa đăng nhập.' };
            await persistSheinSyncStatus(res);
            return res;
          }

          // Click Sync Order: thiếu body → tự mở /user/orders/list để capture.
          // Auto: chỉ navigate khi capture thiếu/cũ; dùng tab nền (preferBackground) — không kéo khỏi PDP.
          const isManual =
            message.manual === true || message.ensureCapture === true;
          if (message.ensureCapture) {
            const prepared = await ensureSheinListBodyCapture();
            if (!prepared.ok) {
              const res: SheinSyncResponse = {
                ok: false,
                message: prepared.message ?? 'Chưa bắt được session — bấm Sync Order lại.',
              };
              await persistSheinSyncStatus(res);
              return res;
            }
          } else if (!isManual) {
            const prepared = await ensureSheinListBodyCapture({
              maxAgeMs: SHEIN_CAPTURE_MAX_AGE_MS,
              preferBackground: true,
            });
            if (!prepared.ok) {
              const soft: SheinSyncResponse = {
                ok: true,
                message:
                  prepared.message ??
                  'Auto: chưa bắt được session SHEIN — F5 tab đơn rồi đợi chu kỳ sau.',
              };
              await persistSheinSyncStatus(soft);
              return soft;
            }
          }
          // sync body continues below; finally always clears sheinSyncInFlight

          const syncConfig = await getSheinSyncConfig().catch(() => null);
          const syncUpdateMode: 'new' | 'all' =
            syncConfig?.updateMode === 'all' ? 'all' : 'new';

          await publishSheinSyncProgress('Đang lấy danh sách đơn…');
          // Đặt cho ngữ cảnh background (đường fallback từ capture); content script nhận cờ
          // riêng qua message vì nó là bundle độc lập, không chia sẻ module state.
          setSheinRawCaptureEnabled(syncConfig?.captureRawPayload === true);
          const detailConcurrency = clampSheinDetailConcurrency(
            syncConfig?.concurrentThreads,
          );

          // Manual Sync Order: list theo Setting "Chế độ cập nhật" — `all` kéo hết trang (không
          // lookback), `new` cắt theo Lookback. Luôn xóa snapshot và POST đủ list đã kéo (tay bấm
          // phải chạy thật, không skip fingerprint).
          // Auto → nông (~20); cứ N lần → full lookback + forceFullPost; snapshot nông có thể bỏ POST.
          // Auto luôn `updateMode=new`. Ba chế độ dùng chung pipeline list→detail; khác nhau chỉ ở
          // plan (cửa sổ list + updateMode). Backend xếp first_seen / new_link / status-only.
          const lookbackDays = syncConfig?.refreshLookbackDays;

          let autoSyncCount = 0;
          let nextAutoSyncCount = 0;
          if (!isManual) {
            const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinAutoSyncCount);
            const prev = Number(raw[STORAGE_KEYS.sheinAutoSyncCount]);
            const base = Number.isFinite(prev) && prev > 0 ? Math.floor(prev) : 0;
            nextAutoSyncCount = base + 1;
            // Dùng số lần kế tiếp để chọn nông/full — chỉ ghi storage khi lấy list thành công.
            autoSyncCount = nextAutoSyncCount;
          }

          const syncPlan = resolveSheinSyncPlan({
            isManual,
            updateMode: syncUpdateMode,
            lookbackDays,
            autoSyncCount,
          });
          const {
            listOpts,
            listDepth,
            effectiveUpdateMode,
            forceFullPost,
          } = syncPlan;

          if (isManual) {
            await chrome.storage.local.remove(STORAGE_KEYS.sheinListSnapshot);
          }

          const fetched = await fetchSheinOrderListFromTab(listOpts);
          if (!fetched.ok || !fetched.orders) {
            const msg = fetched.message ?? 'Không lấy được đơn.';
            const softConn =
              !isManual &&
              /Chưa kết nối được tab SHEIN|Chưa mở tab SHEIN/i.test(msg);
            if (softConn) {
              // Auto ngầm: CS chưa inject (thường sau Reload extension) — nhắc F5, không báo lỗi đỏ.
              const soft: SheinSyncResponse = {
                ok: true,
                message: 'Auto: F5 tab SHEIN để gắn lại content script.',
              };
              await persistSheinSyncStatus(soft);
              return soft;
            }
            const res: SheinSyncResponse = {
              ok: false,
              message: msg,
            };
            await persistSheinSyncStatus(res);
            return res;
          }

          if (!isManual && nextAutoSyncCount > 0) {
            await chrome.storage.local.set({
              [STORAGE_KEYS.sheinAutoSyncCount]: nextAutoSyncCount,
            });
          }

          // List vừa chạy được nghĩa là cookie + header còn sống. Đây là lúc duy nhất chắc chắn
          // session hợp lệ, nên đẩy lên backend ở đây để job nền còn tự gọi get_order_info khi
          // trình duyệt đã đóng. Không chặn lượt sync nếu đẩy lỗi.
          void (async () => {
            const capture = await readSheinCapture();
            const tab = await findSheinTab();
            const pushed = await pushSheinSessionIfChanged(capture, tab?.url);
            if (!pushed.pushed && pushed.reason) {
              console.debug('[fulfil] bỏ đẩy session SHEIN:', pushed.reason);
            }
          })();

          let listOrders = fetched.orders ?? [];

          if (listOrders.length === 0) {
            const res: SheinSyncResponse = {
              ok: true,
              fetched: 0,
              matched: 0,
              updatedTracking: 0,
              updatedCost: 0,
              unmatched: 0,
              message: 'Chưa có đơn để sync.',
            };
            await persistSheinSyncStatus(res);
            return res;
          }

          // Auto Sync: so fingerprint list với snapshot trình duyệt — không đổi thì bỏ POST list
          // (và detail nếu cũng không còn incomplete). Manual: luôn POST cả list.
          // Incomplete retry không bắt buộc còn trên list nông — get_order_info dùng stub.
          const priorIncomplete = await readIncompleteBillnos();
          // Backend còn billno chờ đẩy mã lên sàn (hoãn theo ngân sách request) hoặc chờ reconcile.
          // Chúng đã có mã vận đơn nên fingerprint ngừng đổi — bỏ qua theo snapshot là bỏ luôn.
          const pendingMarkShipKeys = new Set(
            (syncConfig?.pendingAutoMarkShipBillnos ?? [])
              .map((b) => b.trim().toUpperCase())
              .filter(Boolean),
          );
          const rawSnap = await chrome.storage.local.get(STORAGE_KEYS.sheinListSnapshot);
          const snapObj = rawSnap[STORAGE_KEYS.sheinListSnapshot] as
            | Record<string, string>
            | undefined;
          const prevSnap = !forceFullPost ? snapObj : undefined;
          const changedOrders = !forceFullPost
            ? filterSheinListOrdersChanged(prevSnap, listOrders, pendingMarkShipKeys)
            : listOrders;
          const skipListPost = !forceFullPost && changedOrders.length === 0;
          const skipDbEntirely = skipListPost && priorIncomplete.length === 0;

          if (skipDbEntirely) {
            await chrome.storage.local.set({
              [STORAGE_KEYS.sheinListSnapshot]: buildSheinListSnapshot(listOrders),
            });
            const pagesNote =
              fetched.pagesFetched != null ? ` · ${fetched.pagesFetched} trang SHEIN` : '';
            const modeNote =
              listDepth === 'auto_shallow' ? ' · auto 20' : ' · auto lookback';
            const res: SheinSyncResponse = {
              ok: true,
              fetched: listOrders.length,
              matched: 0,
              updatedTracking: 0,
              updatedCost: 0,
              unmatched: 0,
              message: `Auto bỏ qua (snapshot không đổi) · đã kiểm tra ${listOrders.length}${pagesNote}${modeNote}`,
            };
            await persistSheinSyncStatus(res);
            return res;
          }

          // Phase 1: list SHEIN → match (mới: phone+zip; đã có billno: cập nhật).
          // Auto chỉ POST đơn mới/đổi; Manual POST cả list.
          // Phase 2 detail: needsDetail từ list đã POST + incomplete (kể cả ngoài list nông).
          // force_billnos không còn tham gia Sync Order.
          let updatedTracking = 0;
          let autoShipped = 0;
          let autoShipFailed = 0;
          let autoShipBlocked = 0;
          const autoShipNotes: string[] = [];
          let updatedCost = 0;
          let matched = 0;
          let unmatched = 0;
          let unchanged = 0;
          const skipCounts: Record<string, number> = {};
          const skipReasonByBillno: Record<string, string> = {};
          /** Billno backend nhận nhưng không ghi được — không được ghi vào snapshot. */
          const saveFailedKeys = new Set<string>();
          const needsDetailSet = new Set<string>();
          const needsDetailOrdered: string[] = [];

          const pushNeed = (billno: string) => {
            const key = billno.trim().toUpperCase();
            if (!key || needsDetailSet.has(key)) return;
            needsDetailSet.add(key);
            needsDetailOrdered.push(billno.trim());
          };

          let listIncompleteCost: string[] = [];

          // Retry cost-incomplete kể cả billno ngoài cửa sổ list nông (stub + get_order_info).
          for (const b of priorIncomplete) {
            pushNeed(b);
          }

          let listOrdersToPost = forceFullPost ? listOrders : changedOrders;

          // List missing Shipping phone/zip -> get_order_info before match.
          if (listOrdersToPost.length > 0 && !skipListPost) {
            const missingAddr = listOrdersToPost.filter(
              (o) => !o.buyerPhone?.trim() || !o.postalCode?.trim(),
            );
            if (missingAddr.length > 0) {
              await publishSheinSyncProgress(
                `Đang bổ sung địa chỉ Shipping ${missingAddr.length} đơn…`,
              );
              const pre = await enrichSheinDetailsFromTab(
                missingAddr,
                missingAddr.map((o) => o.billno),
                detailConcurrency,
              );
              const byKey = new Map(
                (pre.orders ?? []).map((o) => [o.billno.toUpperCase(), o] as const),
              );
              const mergeAddr = (o: SheinOrderPayload): SheinOrderPayload =>
                byKey.get(o.billno.toUpperCase()) ?? o;
              listOrders = listOrders.map(mergeAddr);
              listOrdersToPost = listOrdersToPost.map(mergeAddr);
            }
          }

          const rematchKeys = new Set<string>();
          /** Billnos matched that need get_order_info for ledger/cost (not rematch). */
          const costDetailKeys = new Set<string>();
          /**
           * Billno lần đầu hoãn khớp trên list (`awaiting_detail`) — detail refresh mới được
           * cộng `matched` / đổi skip (tránh đếm đôi với NewLink đã khớp trên list).
           */
          const deferredFirstSeenKeys = new Set<string>();
          if (listOrdersToPost.length > 0 && !skipListPost) {
            // List không gửi breakdown tiền (coupon/wallet/subtotal…) — chỉ tin sau get_order_info.
            const listResult = await postSheinOrdersInBatches(
              stripSheinListOrdersForPost(listOrdersToPost),
              'list',
              undefined,
              effectiveUpdateMode,
              runId,
            );
            updatedTracking = listResult.updatedTracking;
            autoShipped = listResult.autoShipped;
            autoShipFailed = listResult.autoShipFailed;
            autoShipBlocked = listResult.autoShipBlocked;
            autoShipNotes.push(...listResult.autoShipNotes);
            updatedCost = listResult.updatedCost;
            matched = listResult.matched;
            unmatched = listResult.unmatched;
            unchanged = listResult.unchanged;
            for (const b of listResult.saveFailedBillnos) saveFailedKeys.add(b.toUpperCase());
            for (const [reason, count] of Object.entries(listResult.skipCounts)) {
              skipCounts[reason] = (skipCounts[reason] ?? 0) + count;
            }
            Object.assign(skipReasonByBillno, listResult.skipReasonByBillno);
            for (const [key, reason] of Object.entries(listResult.skipReasonByBillno)) {
              if (reason === 'awaiting_detail') deferredFirstSeenKeys.add(key);
            }
            for (const b of listResult.needsDetailBillnos ?? []) {
              pushNeed(b);
              const key = b.trim().toUpperCase();
              if (!key) continue;
              // Mark Ship bị hoãn theo ngân sách/request cần một lượt POST nữa, không cần breakdown
              // giá. Cho nó vào cost queue là bắt nó qua cổng `detailCostProven` và đơn nào detail
              // trả về sparse sẽ bị loại — đúng thứ đang chặn mã vận đơn lên sàn.
              if (listResult.needsDetailReasons?.[key] === 'pending_auto_mark_ship') continue;
              costDetailKeys.add(key);
            }
            // Rematch chỉ khi list thiếu shipping_telephone/postcode — detail mới có ích.
            // Đơn đã có phone+zip mà không khớp local → không kéo get_order_info hàng loạt.
            // (Billno first_seen/awaiting_detail đã nằm trong needsDetail + costDetailKeys.)
            const listByKey = new Map(
              listOrdersToPost.map((o) => [o.billno.trim().toUpperCase(), o] as const),
            );
            for (const b of listResult.unmatchedBillnos ?? []) {
              const key = b.trim().toUpperCase();
              if (!key) continue;
              const order = listByKey.get(key);
              if (order?.buyerPhone?.trim() && order?.postalCode?.trim()) continue;
              rematchKeys.add(key);
              pushNeed(b);
            }
            listIncompleteCost = listResult.incompleteCostBillnos ?? [];
            // Backend báo matched nhưng thiếu subtotal/total — xếp hàng get_order_info.
            for (const b of listIncompleteCost) {
              pushNeed(b);
              const key = b.trim().toUpperCase();
              if (key) costDetailKeys.add(key);
            }
          }

          // Incomplete local cũng thuộc cost queue (đã match trước đó, chờ giá).
          for (const b of priorIncomplete) {
            const key = b.trim().toUpperCase();
            if (key) costDetailKeys.add(key);
          }

          const needsDetail = needsDetailOrdered;
          let detailNote = '';
          const incompleteAfter: string[] = [];
          const incompleteAfterKeys = new Set<string>();
          const pushIncomplete = (billno: string) => {
            const trimmed = billno.trim();
            const key = trimmed.toUpperCase();
            if (!key || incompleteAfterKeys.has(key)) return;
            incompleteAfterKeys.add(key);
            incompleteAfter.push(trimmed);
          };

          if (needsDetail.length > 0) {
            // Chia lô: mỗi lần gọi content script trả về trong vài giây nên service
            // worker không nằm im đủ lâu để bị suspend giữa lượt sync, và popup thấy
            // tiến trình thật thay vì một khoảng chờ dài.
            const orderByBillno = new Map(
              listOrders.map((o) => [o.billno.toUpperCase(), o] as const),
            );
            const richKeys = new Set<string>();
            /** Rich detail for matched cost queue — sổ cái / phí NCC. */
            const richCostDetailKeys = new Set<string>();
            let rematchAttempted = 0;
            let rematchMatched = 0;
            /** SHEIN đã trả lời dứt khoát "không có đơn này" — đừng xếp hàng lại. */
            const notFoundKeys = new Set<string>();
            const attemptedDetail: string[] = [];
            let detailAborted: string | undefined;
            let deadlineHit = false;
            /** Số lô detail đã POST — batchIndex phải liên tục trong cả lượt, không reset mỗi chunk. */
            let detailBatchIndex = 0;

            for (let i = 0; i < needsDetail.length; i += SHEIN_DETAIL_CHUNK_SIZE) {
              if (Date.now() > syncDeadline) {
                deadlineHit = true;
                break;
              }
              const chunk = needsDetail.slice(i, i + SHEIN_DETAIL_CHUNK_SIZE);
              await publishSheinSyncProgress(
                `Đang sync chi tiết ${Math.min(i + chunk.length, needsDetail.length)}/${needsDetail.length} đơn…`,
              );

              const chunkInput = chunk
                .map((b) => orderByBillno.get(b.toUpperCase()))
                .filter((o): o is SheinOrderPayload => !!o);
              const enriched = await enrichSheinDetailsFromTab(
                chunkInput,
                chunk,
                detailConcurrency,
              );
              for (const o of enriched.orders ?? []) {
                orderByBillno.set(o.billno.toUpperCase(), o);
              }

              if (enriched.aborted) {
                // Rớt phiên/captcha giữa chunk: chỉ billno mà SHEIN thật sự đã trả lời mới tính là
                // "đã thử". Đẩy cả 25 billno vào attemptedDetail đốt một lần retry cho những billno
                // chưa hề được gọi, và sau ba lượt rớt phiên chúng bị loại vĩnh viễn khỏi hàng đợi.
                const answered = new Set<string>(
                  [
                    ...(enriched.richBillnos ?? []),
                    ...(enriched.sparseBillnos ?? []),
                    ...(enriched.notFoundBillnos ?? []),
                  ]
                    .map((b) => b.trim().toUpperCase())
                    .filter(Boolean),
                );
                attemptedDetail.push(
                  ...chunk.filter((b) => answered.has(b.trim().toUpperCase())),
                );
              } else {
                attemptedDetail.push(...chunk);
              }

              const chunkRich = new Set(
                (enriched.richBillnos ?? []).map((b) => b.trim().toUpperCase()).filter(Boolean),
              );
              for (const key of chunkRich) {
                richKeys.add(key);
                if (costDetailKeys.has(key)) richCostDetailKeys.add(key);
              }
              for (const b of enriched.notFoundBillnos ?? []) {
                const key = b.trim().toUpperCase();
                if (key) notFoundKeys.add(key);
              }
              for (const b of enriched.incompleteBillnos ?? []) pushIncomplete(b);

              // Gate: cost queue → chỉ POST khi rich hoặc subtotal từ detail thật (detailCostProven);
              // rematch đủ addr; tracking/milestone (không thuộc cost) → POST sparse.
              const detailOrders = (enriched.orders ?? []).filter((o) => {
                const key = o.billno.toUpperCase();
                if (!needsDetailSet.has(key)) return false;
                if (chunkRich.has(key) || isRichSheinBreakdown(o)) return true;
                if (costDetailKeys.has(key)) {
                  // Không dùng hasNonCostDetailSignal cho cost — tránh POST sparse “chốt” sớm.
                  return (o.subtotal ?? 0) > 0 && o.detailCostProven === true;
                }
                if (
                  rematchKeys.has(key) &&
                  o.buyerPhone?.trim() &&
                  o.postalCode?.trim()
                ) {
                  return true;
                }
                return hasNonCostDetailSignal(o);
              });
              if (detailOrders.length > 0) {
                const rematchOrders = detailOrders.filter((o) =>
                  rematchKeys.has(o.billno.toUpperCase()),
                );
                const refreshOrders = detailOrders.filter(
                  (o) => !rematchKeys.has(o.billno.toUpperCase()),
                );

                const applyDetailPart = (
                  part: Awaited<ReturnType<typeof postSheinOrdersInBatches>>,
                  mode: 'rematch' | 'refresh',
                ) => {
                  detailBatchIndex += part.batches;
                  for (const b of part.saveFailedBillnos) saveFailedKeys.add(b.toUpperCase());
                  updatedTracking += part.updatedTracking;
                  autoShipped += part.autoShipped;
                  autoShipFailed += part.autoShipFailed;
                  autoShipBlocked += part.autoShipBlocked;
                  autoShipNotes.push(...part.autoShipNotes);
                  updatedCost += part.updatedCost;
                  if (mode === 'rematch') {
                    rematchAttempted += rematchOrders.length;
                    rematchMatched += part.matched;
                    matched += part.matched;
                    unmatched = Math.max(0, unmatched - part.matched);
                    const stillUnmatched = new Set(
                      (part.unmatchedBillnos ?? []).map((x) => x.trim().toUpperCase()),
                    );
                    for (const o of rematchOrders) {
                      const key = o.billno.toUpperCase();
                      if (stillUnmatched.has(key)) continue;
                      rematchKeys.delete(key);
                      // Đã khớp được sau detail: gỡ khỏi thống kê bỏ qua của phase list,
                      // nếu không thông báo sẽ vừa đếm là khớp vừa đếm là bỏ qua.
                      const reason = skipReasonByBillno[key];
                      if (reason && skipCounts[reason]) {
                        skipCounts[reason] -= 1;
                        if (skipCounts[reason] <= 0) delete skipCounts[reason];
                        delete skipReasonByBillno[key];
                      }
                    }
                  } else {
                    // Không chạm `matched` cho billno đã khớp trên list (NewLink/mode_all) — tránh
                    // đếm đôi. Billno `awaiting_detail` (hoãn khớp lần đầu) mới cộng matched /
                    // đổi skip sau detail.
                    const detailSkipByKey = new Map<string, string>();
                    for (const outcome of part.skipOutcomes ?? []) {
                      const key = outcome.billno.trim().toUpperCase();
                      if (key) detailSkipByKey.set(key, outcome.reason);
                    }
                    let deferredMatched = 0;
                    for (const o of refreshOrders) {
                      const key = o.billno.toUpperCase();
                      if (!deferredFirstSeenKeys.has(key)) continue;
                      const wasAwaiting = skipReasonByBillno[key] === 'awaiting_detail';
                      if (wasAwaiting) {
                        skipCounts.awaiting_detail = (skipCounts.awaiting_detail ?? 1) - 1;
                        if ((skipCounts.awaiting_detail ?? 0) <= 0) {
                          delete skipCounts.awaiting_detail;
                        }
                        delete skipReasonByBillno[key];
                        unmatched = Math.max(0, unmatched - 1);
                      }
                      const detailReason = detailSkipByKey.get(key);
                      const saveFailed = part.saveFailedBillnos.some(
                        (b) => b.toUpperCase() === key,
                      );
                      if (detailReason) {
                        skipReasonByBillno[key] = detailReason;
                        skipCounts[detailReason] = (skipCounts[detailReason] ?? 0) + 1;
                        unmatched += 1;
                      } else if (saveFailed) {
                        // Chưa ghi được — giữ awaiting để snapshot loại và lượt sau thử lại.
                        if (wasAwaiting) {
                          skipReasonByBillno[key] = 'awaiting_detail';
                          skipCounts.awaiting_detail =
                            (skipCounts.awaiting_detail ?? 0) + 1;
                          unmatched += 1;
                        }
                      } else {
                        deferredMatched += 1;
                      }
                    }
                    matched += deferredMatched;

                    // Detail cost applied → clear chờ giá for these billnos unless still incomplete.
                    const stillInc = new Set(
                      (part.incompleteCostBillnos ?? []).map((x) => x.trim().toUpperCase()),
                    );
                    for (const o of refreshOrders) {
                      const key = o.billno.toUpperCase();
                      if (!stillInc.has(key)) {
                        listIncompleteCost = listIncompleteCost.filter(
                          (b) => b.trim().toUpperCase() !== key,
                        );
                      }
                    }
                    // Keep chỉ billno vẫn incomplete sau detail
                    listIncompleteCost = [
                      ...new Set([
                        ...listIncompleteCost.filter((b) =>
                          stillInc.has(b.trim().toUpperCase()),
                        ),
                        ...(part.incompleteCostBillnos ?? []).map((b) => b.trim()).filter(Boolean),
                      ]),
                    ];
                  }
                  for (const b of part.incompleteCostBillnos ?? []) {
                    // Hàng đợi incomplete chỉ cho cost-detail (đã match), không nhét rematch fail.
                    if (mode === 'refresh' || costDetailKeys.has(b.trim().toUpperCase())) {
                      pushIncomplete(b);
                    }
                  }
                };

                if (rematchOrders.length > 0) {
                  applyDetailPart(
                    await postSheinOrdersInBatches(
                      rematchOrders,
                      'detail',
                      undefined,
                      effectiveUpdateMode,
                      runId,
                      detailBatchIndex + 1,
                    ),
                    'rematch',
                  );
                }
                if (refreshOrders.length > 0) {
                  applyDetailPart(
                    await postSheinOrdersInBatches(
                      refreshOrders,
                      'detail',
                      undefined,
                      effectiveUpdateMode,
                      runId,
                      detailBatchIndex + 1,
                    ),
                    'refresh',
                  );
                }
              }

              if (enriched.aborted) {
                detailAborted = enriched.aborted;
                break;
              }
            }

            for (const b of attemptedDetail) {
              const key = b.toUpperCase();
              // Chỉ theo dõi incomplete cost cho đơn đã match cần giá — không đếm rematch.
              if (!costDetailKeys.has(key)) continue;
              if (richCostDetailKeys.has(key) || notFoundKeys.has(key)) continue;
              const order = orderByBillno.get(key);
              if (!order || !isRichSheinBreakdown(order)) {
                pushIncomplete(b);
              }
            }

            const attemptedKeys = new Set(attemptedDetail.map((b) => b.toUpperCase()));
            const deferredDetail = needsDetail.filter(
              (b) => !attemptedKeys.has(b.toUpperCase()),
            );
            await persistIncompleteBillnos(incompleteAfter, attemptedDetail, deferredDetail);

            const costNeed = costDetailKeys.size;
            const costRich = richCostDetailKeys.size;
            const incompleteCount = incompleteAfter.length;
            const rematchNote =
              rematchAttempted > 0
                ? ` · rematch ${rematchMatched}/${rematchAttempted}`
                : '';
            if (detailAborted) {
              detailNote =
                ` · dừng detail: ${detailAborted} (phí ${costRich}/${costNeed})` + rematchNote;
            } else {
              const incompleteNote =
                incompleteCount > 0 ? ` · thiếu ${incompleteCount}` : '';
              const notFoundNote =
                notFoundKeys.size > 0 ? ` · ${notFoundKeys.size} không thuộc tài khoản này` : '';
              const deadlineNote = deadlineHit
                ? ` · hết giờ, còn ${needsDetail.length - attemptedDetail.length} đơn để lượt sau`
                : '';
              // detail = cost/ledger cho đơn đã khớp — không gộp rematch unmatched.
              detailNote =
                (costNeed > 0 || costRich > 0
                  ? ` · detail phí ${costRich}/${costNeed}`
                  : '') +
                rematchNote +
                incompleteNote +
                notFoundNote +
                deadlineNote;
            }
          } else {
            // Không thử billno nào lượt này — chỉ prune TTL/cap, giữ lịch sử attempts.
            await persistIncompleteBillnos([], []);
          }

          const pagesNote =
            fetched.pagesFetched != null ? ` · ${fetched.pagesFetched} trang SHEIN` : '';
          const modeNote =
            listDepth === 'auto_shallow'
              ? ' · auto 20'
              : listDepth === 'auto_full'
                ? ' · auto lookback'
                : listDepth === 'manual_new'
                  ? ` · manual lookback ${listOpts.lookbackDays ?? lookbackDays ?? 14}`
                  : ' · mode all';
          const listSkipNote = skipListPost
            ? ' · bỏ POST list (không đổi)'
            : !forceFullPost && listOrdersToPost.length > 0 && listOrdersToPost.length < listOrders.length
              ? ` · gửi ${listOrdersToPost.length}/${listOrders.length} đổi`
              : '';
          const skipNote =
            formatSheinSkipNote(skipCounts) ||
            (unmatched > 0 ? ` · chưa gắn ${unmatched} — xem trang Đơn chưa khớp` : '');
          const fetchedCount = forceFullPost ? listOrders.length : listOrdersToPost.length;
          const unchangedNote = unchanged > 0 ? ` · giữ nguyên ${unchanged}` : '';
          // Mỗi billno đã lấy phải rơi vào đúng một nhóm; lệch là dấu hiệu backend có nhánh
          // bỏ qua chưa khai báo lý do, đáng báo ngay thay vì để người đọc tự cộng tay.
          const skipTotal = Object.values(skipCounts).reduce((sum, n) => sum + n, 0);
          const awaitingNote =
            (skipCounts.awaiting_detail ?? 0) > 0
              ? ` · chờ detail ${skipCounts.awaiting_detail}`
              : '';
          const accounted = matched + unchanged + skipTotal;
          const balanceNote =
            !skipListPost && accounted !== fetchedCount
              ? ` · ⚠ lệch ${fetchedCount - accounted} đơn chưa phân loại (run ${runId})`
              : '';
          const autoShipNote =
            autoShipped + autoShipFailed + autoShipBlocked > 0
              ? ` · auto ship ${autoShipped} OK/${autoShipFailed} lỗi/${autoShipBlocked} thủ công`
              : '';
          const manualNote =
            autoShipNotes.length > 0 ? ` · ${autoShipNotes.slice(0, 2).join(' | ')}` : '';
          const pendingCostNote =
            listIncompleteCost.length > 0 ? ` · chờ giá ${listIncompleteCost.length}` : '';
          // Snapshot là dấu "đã xử lý xong billno này ở phiên bản dữ liệu này": Auto Sync so
          // fingerprint để bỏ POST những đơn không đổi. Giữ ngoài snapshot: saveFailed,
          // skip unmatched, và billno còn thiếu giá (incomplete) — nếu không Auto coi xong
          // rồi không bao giờ gọi lại get_order_info.
          if (listOrders.length > 0) {
            const retryKeys = new Set<string>([
              ...saveFailedKeys,
              ...Object.keys(skipReasonByBillno),
              ...incompleteAfterKeys,
              ...listIncompleteCost.map((b) => b.trim().toUpperCase()).filter(Boolean),
            ]);
            const snapshotOrders =
              retryKeys.size > 0
                ? listOrders.filter((o) => !retryKeys.has(o.billno.trim().toUpperCase()))
                : listOrders;
            await chrome.storage.local.set({
              [STORAGE_KEYS.sheinListSnapshot]: buildSheinListSnapshot(snapshotOrders),
            });
          }
          const res: SheinSyncResponse = {
            ok: true,
            fetched: fetchedCount,
            matched,
            updatedTracking,
            autoShipped,
            autoShipFailed,
            autoShipBlocked,
            updatedCost,
            unmatched,
            incompleteDetail: incompleteAfter.length,
            runId,
            message:
              `Khớp ${matched}/${fetchedCount} · track local +${updatedTracking} · phí +${updatedCost}` +
              autoShipNote +
              detailNote +
              pendingCostNote +
              skipNote +
              awaitingNote +
              unchangedNote +
              balanceNote +
              listSkipNote +
              pagesNote +
              modeNote +
              manualNote,
          };
          await persistSheinSyncStatus(res);
          return res;
          } catch (err) {
            const res: SheinSyncResponse = {
              ok: false,
              message: err instanceof Error ? err.message : 'Lỗi gửi đơn.',
            };
            await persistSheinSyncStatus(res);
            return res;
          } finally {
            sheinSyncInFlight = false;
            await setSheinSyncRunning(false);
            // Manual Sync cũng reset chu kỳ Auto Sync (nếu đang bật) — giống UX panel cũ.
            if (await isSheinAutoSyncEnabled()) {
              void armSheinAutoSyncSchedule();
            }
          }
}

chrome.runtime.onMessage.addListener((message: ExtMessage, sender, sendResponse) => {
  void (async () => {
    try {
      if (!message || typeof message !== 'object' || !message.type) {
        sendResponse({ ok: false, message: 'Unknown message (missing type).' });
        return;
      }
      switch (message.type) {
        case 'WHOAMI': {
          sendResponse({ ok: true, tabId: sender.tab?.id });
          return;
        }

        case 'OPEN_SIDE_PANEL': {
          const windowId =
            sender.tab?.windowId ??
            (await chrome.windows.getLastFocused().catch(() => null))?.id;
          if (windowId !== undefined) {
            await chrome.sidePanel.open({ windowId });
          }
          sendResponse({ ok: true });
          return;
        }

        case 'GET_STATE': {
          sendResponse(await buildState());
          return;
        }

        case 'LOGIN': {
          await login(message.pin);
          // Status sync cũ (vd. "Chưa đăng nhập.") không được mang sang phiên mới.
          await chrome.storage.local.remove(STORAGE_KEYS.sheinSyncStatus);
          try {
            await refreshSources();
          } catch {
            // sources optional at login
          }
          void ensureSheinAutoSyncSchedule();
          // Warm-up SHEIN do panel gọi sau login (LOGIN chỉ auth cho nhanh).
          sendResponse({ ...(await buildState()), ok: true });
          return;
        }

        case 'LOGOUT': {
          // Nhả mọi claim của phiên hiện tại trên server (không phụ thuộc ordersCache).
          try {
            await releaseSessionClaims();
          } catch {
            // best-effort — TTL / SignalR orphan sweep vẫn dọn nếu fail
          }
          await persistActiveMatch(null);
          await logout();
          await clearUserCaches();
          await chrome.storage.local.remove(STORAGE_KEYS.sheinSyncStatus);
          await chrome.storage.local.remove(STORAGE_KEYS.sheinSessionPreparing);
          await chrome.storage.local.remove(STORAGE_KEYS.sheinSyncRunning);
          await clearSheinAutoSyncSchedule();
          sendResponse({ ok: true });
          return;
        }

        case 'LIST_ORDERS': {
          const auth = await getAuth();
          if (!auth) {
            sendResponse({
              ok: false,
              message: 'Chưa đăng nhập.',
              orders: [],
              total: 0,
            } satisfies ListOrdersResponse);
            return;
          }

          let sources = await readProductSources();
          if (sources.length === 0) {
            try {
              sources = await refreshSources();
            } catch (err) {
              sendResponse({
                ok: false,
                message: err instanceof Error ? err.message : 'Không tải được nguồn hàng.',
                orders: [],
                total: 0,
              } satisfies ListOrdersResponse);
              return;
            }
          }

          const productSourceId = message.productSourceId;
          const search = typeof message.search === 'string' ? message.search.trim() : '';
          const sourceName = productSourceId
            ? sources.find((s) => s.id === productSourceId)?.name
            : undefined;

          // Ô tìm: mọi trạng thái + mọi nguồn (kể cả đã mua) — không ghi đè cache hàng đợi / ghim.
          if (search) {
            try {
              const { orders, total } = await searchOrders({
                search,
                pageSize: 50,
              });
              sendResponse({
                ok: true,
                orders,
                total,
                productSourceId,
                sourceName,
              } satisfies ListOrdersResponse);
            } catch (err) {
              sendResponse({
                ok: false,
                message: err instanceof Error ? err.message : 'Không tìm được đơn.',
                orders: [],
                total: 0,
                productSourceId,
                sourceName,
              } satisfies ListOrdersResponse);
            }
            return;
          }

          const listMode =
            message.mode === 'purchasedToday' ? 'purchasedToday' : 'needsPurchase';

          // Tab «Đã mua»: không ghi đè cache hàng đợi / active match.
          if (listMode === 'purchasedToday') {
            try {
              const { orders, total } = await listPurchasedTodayOrders({
                productSourceId,
                pageSize: 50,
              });
              sendResponse({
                ok: true,
                orders,
                total,
                productSourceId,
                sourceName,
              } satisfies ListOrdersResponse);
            } catch (err) {
              sendResponse({
                ok: false,
                message: err instanceof Error ? err.message : 'Không tải được đơn đã mua.',
                orders: [],
                total: 0,
                productSourceId,
                sourceName,
              } satisfies ListOrdersResponse);
            }
            return;
          }

          const platformOrderStatus = message.platformOrderStatus ?? 'AWAITING_SHIPMENT';
          // listNeedsPurchaseOrders phân trang đến hết (chunk 200, API max).
          const { orders: fetched, total } = await listNeedsPurchaseOrders({
            productSourceId,
            platformOrderStatus,
          });
          // Không cache đơn đã có FulfilPurchasedAt (đã đánh dấu Đã mua).
          const orders = fetched.filter((o) => !o.fulfilPurchasedAt);
          await persistOrdersCache(orders);

          const stored = await readActiveMatch();
          const enriched = enrichActiveMatch(stored, orders);
          if (enriched) {
            // List đã có buyer fields mới → cập nhật card đang mua.
            // Đơn đã mua (không còn trong cache) → giữ enriched từ storage, không clear.
            if (!hasBuyerContact(enriched.order) && enriched.orderId !== 'pending') {
              try {
                const detail = await getOrderDetail(enriched.orderId);
                if (detail.fulfilPurchasedAt) {
                  await persistOrdersCache(orders.filter((o) => o.id !== detail.id));
                }
                const item =
                  detail.lineItems.find((i) => i.id === enriched.itemId) ?? enriched.item;
                await persistActiveMatch({ ...enriched, order: detail, item });
              } catch {
                await persistActiveMatch(enriched);
              }
            } else {
              await persistActiveMatch(enriched);
            }
          }

          sendResponse({
            ok: true,
            orders,
            total,
            productSourceId,
            sourceName,
          } satisfies ListOrdersResponse);
          return;
        }

        case 'REFRESH_ACTIVE_BUYER': {
          const stored = await readActiveMatch();
          if (!stored) {
            sendResponse({
              ok: false,
              message: 'Chưa có sản phẩm đang mua.',
              match: null,
            } satisfies RefreshActiveBuyerResponse);
            return;
          }

          const orders = await readOrdersCache();
          let match = enrichActiveMatch(stored, orders) ?? stored;

          if (match.orderId !== 'pending') {
            try {
              const detail = await getOrderDetail(match.orderId);
              const item =
                detail.lineItems.find((i) => i.id === match.itemId) ?? match.item;
              match = { ...match, order: detail, item };
              // Đã mua → bỏ khỏi hàng đợi nhưng giữ panel xem lại (tab Đã mua).
              if (detail.fulfilPurchasedAt) {
                await persistOrdersCache(
                  orders.filter((o) => o.id !== detail.id && !o.fulfilPurchasedAt),
                );
                await persistActiveMatch(match);
                sendResponse({
                  ok: true,
                  match,
                } satisfies RefreshActiveBuyerResponse);
                return;
              }
              const idx = orders.findIndex((o) => o.id === detail.id);
              if (idx >= 0) {
                orders[idx] = detail;
                await persistOrdersCache(orders);
              }
              // Không prepend đơn lạ vào cache (tránh đưa đơn đã mua trở lại list).
            } catch {
              // Giữ bản đã enrich từ cache / side panel.
            }
          }

          await persistActiveMatch(match);
          sendResponse({ ok: true, match } satisfies RefreshActiveBuyerResponse);
          return;
        }

        case 'GET_ACTIVE_TAB_STATUS': {
          const tab = await getActiveHttpTab();
          if (!tab?.url || !tab.id) {
            // Không xóa match đã chọn — chỉ tạm không có tab để báo.
            sendResponse({
              ok: true,
              ready: false,
              status: 'Không có tab http(s) đang mở.',
              match: await readActiveMatch(),
              variantFit: await readVariantFit(),
            } satisfies ActiveTabStatusResponse);
            return;
          }

          const sources = await readProductSources();
          const detection = detectProductPage(tab.url, sources);
          const orders = await readOrdersCache();
          const stored = await readActiveMatch();
          const dismissed = !stored && (await isActiveMatchDismissed());

          let match: ActiveMatch | null = null;

          if (dismissed) {
            // User đã Nhả / Đã mua — không tự chọn lại theo URL cho đến khi click đơn.
            match = null;
          } else if (detection.ok) {
            const pageProductId =
              detection.flow && tab.url
                ? extractProductId(tab.url, detection.flow)
                : null;

            // 1) User vừa click đơn → giữ đúng đơn đã ghim (kể cả lúc tab chưa kịp navigate).
            // 2) Cùng productId với PDP → giữ đơn đã chọn (tránh nhiều đơn trùng goodsId
            //    khiến findMatchForUrl lấy nhầm đơn đầu trong cache).
            if (stored && isUserPinActive(stored)) {
              match = enrichActiveMatch(stored, orders) ?? stored;
              if (pageProductId && activeMatchMatchesProductId(stored, pageProductId, sources)) {
                // Đã vào đúng PDP của SP đã chọn → bỏ pin, vẫn giữ đúng orderId/itemId.
                match = {
                  ...clearUserPin(match),
                  tabUrl: tab.url,
                  productId: pageProductId,
                };
              }
            } else if (
              stored &&
              pageProductId &&
              activeMatchMatchesProductId(stored, pageProductId, sources)
            ) {
              const enriched = enrichActiveMatch(stored, orders) ?? stored;
              match = {
                ...clearUserPin(enriched),
                tabUrl: tab.url,
                productId: pageProductId,
              };
            } else {
              // PDP: match theo URL; không khớp SP trong queue → giữ match đang mua (cùng nguồn).
              match = findMatchForUrl(tab.url, orders, sources);
              if (match) {
                await persistActiveMatch(match);
              } else {
                match = keepMatchOnSource(stored, detection.source, sources);
              }
            }
          } else if (detection.source) {
            // Cart / checkout / trang khác trên cùng nguồn → luôn giữ sản phẩm đang mua.
            match = keepMatchOnSource(stored, detection.source, sources);
            if (
              !match &&
              stored &&
              isPurchaseFlowUrl(tab.url, detection.flow)
            ) {
              match = stored;
            }
            // Pin còn hiệu lực trên cart/checkout: ưu tiên stored dù keepMatchOnSource null.
            if (!match && stored && isUserPinActive(stored)) {
              match = stored;
            }
          } else {
            // Ngoài nguồn cấu hình: vẫn hiện card nếu còn match (tiện theo dõi đơn đang mua).
            match = stored;
          }

          // Luôn gắn lại order mới từ cache (phone / address / zip).
          match = enrichActiveMatch(match, orders) ?? match;

          // Cache list thiếu địa chỉ → lấy detail một lần rồi ghi lại match.
          if (match && !hasBuyerContact(match.order) && match.orderId !== 'pending') {
            try {
              const detail = await getOrderDetail(match.orderId);
              const item =
                detail.lineItems.find((i) => i.id === match!.itemId) ?? match.item;
              match = { ...match, order: detail, item };
              await persistActiveMatch(match);
              // Cập nhật cache đơn tương ứng
              const idx = orders.findIndex((o) => o.id === detail.id);
              if (idx >= 0) {
                orders[idx] = detail;
                await persistOrdersCache(orders);
              }
            } catch {
              // giữ match hiện có
            }
          } else if (match) {
            await persistActiveMatch(match);
          }

          const productId =
            detection.flow && detection.ok && tab.url
              ? extractProductId(tab.url, detection.flow) ?? undefined
              : match?.productId;

          const storedFit = await readVariantFit();
          const variantFit =
            match &&
            storedFit &&
            storedFit.orderId === match.orderId &&
            storedFit.itemId === match.itemId
              ? storedFit
              : null;

          const storedCost = await readCheckoutCost();
          const checkoutCost =
            match &&
            isCheckoutUrl(tab.url) &&
            storedCost &&
            storedCost.orderId === match.orderId &&
            Number.isFinite(storedCost.amount)
              ? storedCost
              : null;

          const onPurchaseFlow =
            !!detection.source &&
            (detection.ok || isPurchaseFlowUrl(tab.url, detection.flow));

          sendResponse({
            ok: true,
            ready: detection.ok || (!!match && onPurchaseFlow),
            status: detection.reason,
            url: tab.url,
            tabId: tab.id,
            isProductPage: detection.ok,
            sourceName: detection.source?.name ?? match?.sourceName,
            sourceId: detection.source?.id,
            flow: detection.flow,
            productId,
            match,
            variantFit,
            checkoutCost,
          } satisfies ActiveTabStatusResponse);
          return;
        }

        case 'OPEN_PRODUCT_URL': {
          const url = message.url?.trim();
          if (!url || !/^https?:/i.test(url)) {
            sendResponse({ ok: false, message: 'URL không hợp lệ.' });
            return;
          }
          const tab = await getActiveHttpTab();
          if (tab?.id) {
            await chrome.tabs.update(tab.id, { url });
          } else {
            await chrome.tabs.create({ url });
          }
          sendResponse({ ok: true });
          return;
        }

        case 'OPEN_ORDER_ITEM': {
          const item = message.item;
          const url = resolveBuyUrl(item);
          if (!url) {
            sendResponse({
              ok: false,
              message: 'Không có URL / SKU để mở sản phẩm.',
            });
            return;
          }

          const wantedOrderId =
            typeof message.orderId === 'string' && message.orderId.trim()
              ? message.orderId.trim()
              : null;

          // Claim trước khi navigate — tránh 2 fulfil mở cùng đơn.
          let claimPayload: {
            claim?: FulfilClaimApiResult['claim'];
            siblingClaims?: FulfilClaimApiResult['siblingClaims'];
            releasedClaims?: FulfilClaimApiResult['releasedClaims'];
            serverTime?: string;
          } = {};
          if (wantedOrderId) {
            try {
              const claimRes = await claimOrder(
                wantedOrderId,
                message.siblingOrderIds,
              );
              const cached = await readOrdersCache();
              const next = await applyClaimResultToOrders(cached, claimRes);
              await persistOrdersCache(next);
              claimPayload = {
                claim: claimRes.claim,
                siblingClaims: claimRes.siblingClaims,
                releasedClaims: claimRes.releasedClaims,
                serverTime: claimRes.serverTime,
              };
            } catch (err) {
              const status = err instanceof ApiError ? err.status : 0;
              const msg =
                err instanceof Error ? err.message : 'Không giữ được đơn.';
              // Đã stamp mua — vẫn mở PDP/ghim xem lại, không chặn bằng toast.
              const alreadyPurchased =
                status === 409 &&
                /đã được đánh dấu mua|không thể giữ/i.test(msg);
              if (!alreadyPurchased) {
                sendResponse({
                  ok: false,
                  message: msg,
                  conflict: status === 409,
                });
                return;
              }
            }
          }

          const parsed = parseSellerSku(item.sellerSku);
          const pending: PendingSelect = {
            goodsId: parsed?.goodsId,
            skuCode: parsed?.skuCode,
            sizeLabel: extractSizeLabel(item.variantAttr),
            colorLabel: extractColorLabel(item.variantAttr),
            variantAttr: item.variantAttr,
            at: Date.now(),
          };
          await chrome.storage.local.set({ [STORAGE_KEYS.pendingSelect]: pending });

          // Ghim ngay card đang mua / đã mua trước khi vào PDP / cart / checkout.
          const sources = await readProductSources();
          const orders = await readOrdersCache();
          let order =
            (wantedOrderId
              ? orders.find((o) => o.id === wantedOrderId)
              : undefined) ??
            orders.find((o) => o.lineItems.some((li) => li.id === item.id)) ??
            null;

          // Lấy detail để có đủ phone / address / zip (list đôi khi thiếu;
          // tab Đã mua không nằm trong ordersCache → bắt buộc fetch theo orderId).
          if (order || wantedOrderId) {
            try {
              const detail = await getOrderDetail(order?.id ?? wantedOrderId!);
              // Detail không mang sessionIds — giữ lease vừa claim từ cache/payload.
              const cachedClaim = (await readOrdersCache()).find((o) => o.id === detail.id)
                ?.claim;
              order = cachedClaim ? { ...detail, claim: cachedClaim } : detail;
              if (claimPayload.claim && order.id === claimPayload.claim.orderId) {
                order = await applyClaimItemToOrder(order, claimPayload.claim);
              }
              await upsertNeedsPurchaseCache(order);
            } catch {
              // giữ order từ list nếu có
            }
          }

          const line =
            order?.lineItems.find((li) => li.id === item.id) ??
            ({
              id: item.id,
              sellerSku: item.sellerSku,
              title: item.title,
              sourceUrl: item.sourceUrl,
              listingUrl: item.listingUrl,
              variantAttr: item.variantAttr,
              quantity: item.quantity ?? 1,
            } as ActiveMatch['item']);
          const source = findSourceByUrl(url, sources);
          await persistActiveMatch({
            orderId: order?.id ?? 'pending',
            itemId: line.id,
            order: order ?? {
              id: 'pending',
              orderStatus: 'AWAITING_SHIPMENT',
              lineItems: [line],
            },
            item: line,
            tabUrl: url,
            sourceName: source?.name,
            productId: parsed?.goodsId,
            userPinnedAt: Date.now(),
          });

          const tab = await getActiveHttpTab();
          if (tab?.id) {
            await chrome.tabs.update(tab.id, { url });
          } else {
            await chrome.tabs.create({ url });
          }
          sendResponse({ ok: true, url, ...claimPayload });
          return;
        }

        case 'SET_ACTIVE_ORDER_ITEM': {
          const match = await setActiveMatchFromOrder(message.orderId, message.itemId);
          if (!match) {
            sendResponse({
              ok: false,
              message: 'Không tìm thấy đơn / sản phẩm trong danh sách.',
              match: null,
            } satisfies SetActiveOrderItemResponse);
            return;
          }
          sendResponse({ ok: true, match } satisfies SetActiveOrderItemResponse);
          return;
        }

        case 'MARK_PURCHASED': {
          try {
            await markOrderPurchased(message.orderId, !!message.force);
          } catch (err) {
            const status = err instanceof ApiError ? err.status : 0;
            const msg =
              err instanceof Error ? err.message : 'Không đánh dấu được.';
            sendResponse({
              ok: false,
              message: msg,
              needsForce: status === 409,
            } satisfies MarkPurchasedResponse);
            return;
          }
          const cached = await readOrdersCache();
          // Gỡ đơn đã mua khỏi cache — list UI ẩn ngay, không chờ LIST_ORDERS.
          const next = cached.filter(
            (o) => o.id !== message.orderId && !o.fulfilPurchasedAt,
          );
          await persistOrdersCache(next);
          await persistActiveMatch(null);
          sendResponse({
            ok: true,
            message: 'Đã đánh dấu mua hàng.',
          } satisfies MarkPurchasedResponse);
          return;
        }

        case 'CLAIM_ORDER': {
          try {
            const res = await claimOrder(message.orderId, message.siblingOrderIds);
            const cached = await readOrdersCache();
            const next = await applyClaimResultToOrders(cached, res);
            await persistOrdersCache(next);
            sendResponse({
              ok: true,
              message: 'Đã giữ đơn.',
              claim: res.claim,
              serverTime: res.serverTime,
            });
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Không giữ được đơn.',
            });
          }
          return;
        }

        case 'RELEASE_ORDER_CLAIM': {
          try {
            const res = await releaseOrderClaim(
              message.orderId,
              !!message.allSessions,
            );
            const cached = await readOrdersCache();
            const next = await Promise.all(
              cached.map(async (o) =>
                o.id === message.orderId
                  ? applyClaimItemToOrder(o, res.claim)
                  : o,
              ),
            );
            await persistOrdersCache(next);
            // Nhả = bỏ chọn hẳn trên phiên này (panel + inject overlay).
            const active = await readActiveMatch();
            if (active?.orderId === message.orderId) {
              await persistActiveMatch(null);
            }
            sendResponse({
              ok: true,
              message:
                res.claim.sessionCount > 0
                  ? `Đã nhả phiên này — đơn vẫn giữ ở ${res.claim.sessionCount} phiên khác.`
                  : 'Đã nhả đơn.',
              claim: res.claim,
              serverTime: res.serverTime,
            });
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Không nhả được đơn.',
            });
          }
          return;
        }

        case 'RENEW_ORDER_CLAIM': {
          try {
            const res = await claimOrder(message.orderId);
            const cached = await readOrdersCache();
            const next = await applyClaimResultToOrders(cached, res);
            await persistOrdersCache(next);
            sendResponse({
              ok: true,
              message: 'Đã gia hạn giữ đơn.',
              claim: res.claim,
              serverTime: res.serverTime,
            });
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Không gia hạn được.',
            });
          }
          return;
        }

        case 'LIST_ORDER_CLAIMS': {
          try {
            const res = await listActiveClaims();
            const cached = await readOrdersCache();
            const byId = new Map(res.items.map((i) => [i.orderId, i]));
            const next = await Promise.all(
              cached.map(async (o) => {
                const item = byId.get(o.id);
                if (item) return applyClaimItemToOrder(o, item);
                // Đơn trong cache nhưng không còn trong active claims → clear claim local.
                if (o.claim) return { ...o, claim: null };
                return o;
              }),
            );
            await persistOrdersCache(next);
            sendResponse({
              ok: true,
              items: res.items,
              serverTime: res.serverTime,
            });
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Không tải claims.',
              items: [],
            });
          }
          return;
        }

        case 'UNMARK_PURCHASED': {
          await unmarkOrderPurchased(message.orderId);
          const detail = await getOrderDetail(message.orderId).catch(() => null);

          if (detail && !detail.fulfilPurchasedAt) {
            const cached = await readOrdersCache();
            const without = cached.filter(
              (o) => o.id !== message.orderId && !o.fulfilPurchasedAt,
            );
            without.unshift(detail);
            await persistOrdersCache(without);
          }

          const active = await readActiveMatch();
          if (active && active.orderId === message.orderId) {
            await persistActiveMatch({
              ...active,
              order: detail
                ? detail
                : { ...active.order, fulfilPurchasedAt: null },
            });
          }

          sendResponse({
            ok: true,
            message: 'Đã chuyển về chưa mua.',
          } satisfies UnmarkPurchasedResponse);
          return;
        }

        case 'UPDATE_ORDER_MESSAGE': {
          const saved = await updateOrderMessage(message.orderId, message.message);
          const detail = await getOrderDetail(message.orderId).catch(() => null);
          const cached = await readOrdersCache();
          const next = cached.map((o) =>
            o.id === message.orderId
              ? {
                  ...o,
                  message: detail?.message ?? saved,
                  issueTags: detail?.issueTags ?? o.issueTags,
                  issueStatus: detail?.issueStatus ?? o.issueStatus,
                }
              : o,
          );
          await persistOrdersCache(next);
          const active = await readActiveMatch();
          if (active?.orderId === message.orderId) {
            await persistActiveMatch({
              ...active,
              order: {
                ...active.order,
                message: detail?.message ?? saved,
                issueTags: detail?.issueTags ?? active.order.issueTags,
                issueStatus: detail?.issueStatus ?? active.order.issueStatus,
              },
            });
          }
          sendResponse({
            ok: true,
            message: saved ? 'Đã cập nhật thông báo.' : 'Đã xoá thông báo.',
            orderMessage: saved,
            issueTags: detail?.issueTags,
            issueStatus: detail?.issueStatus,
          } satisfies UpdateOrderMessageResponse);
          return;
        }

        case 'LIST_ISSUE_TAGS': {
          const tags = await listIssueTags();
          sendResponse({ ok: true, tags } satisfies ListIssueTagsResponse);
          return;
        }

        case 'CREATE_ISSUE_TAG': {
          const tag = await createIssueTag(message.label);
          sendResponse({ ok: true, tag } satisfies CreateIssueTagResponse);
          return;
        }

        case 'UPDATE_ISSUE_TAG': {
          const tag = await updateIssueTag(message.tagId, message.label);
          sendResponse({
            ok: true,
            message: 'Đã cập nhật nhãn.',
            tag,
          } satisfies UpdateIssueTagResponse);
          return;
        }

        case 'DELETE_ISSUE_TAG': {
          await deleteIssueTag(message.tagId);
          sendResponse({
            ok: true,
            message: 'Đã xóa nhãn.',
          } satisfies DeleteIssueTagResponse);
          return;
        }

        case 'SET_ORDER_ISSUE_TAGS': {
          const order = await setOrderIssueTags(message.orderId, message.tagIds);
          const cached = await readOrdersCache();
          const next = cached.map((o) => (o.id === message.orderId ? { ...o, ...order } : o));
          await persistOrdersCache(next);
          const active = await readActiveMatch();
          if (active?.orderId === message.orderId) {
            await persistActiveMatch({ ...active, order: { ...active.order, ...order } });
          }
          sendResponse({
            ok: true,
            message: 'Đã cập nhật tag lỗi.',
            order,
          } satisfies SetOrderIssueResponse);
          return;
        }

        case 'SET_ORDER_ISSUE_STATUS': {
          const order = await setOrderIssueStatus(message.orderId, message.status);
          const cached = await readOrdersCache();
          const next = cached.map((o) => (o.id === message.orderId ? { ...o, ...order } : o));
          await persistOrdersCache(next);
          const active = await readActiveMatch();
          if (active?.orderId === message.orderId) {
            await persistActiveMatch({ ...active, order: { ...active.order, ...order } });
          }
          sendResponse({
            ok: true,
            message: 'Đã cập nhật trạng thái lỗi.',
            order,
          } satisfies SetOrderIssueResponse);
          return;
        }

        case 'GET_SHEIN_SYNC_CONFIG': {
          const auth = await getAuth();
          if (!auth) {
            sendResponse({
              ok: false,
              message: 'Chưa đăng nhập.',
              enabled: false,
              intervalSeconds: 0,
              updateMode: 'new',
              concurrentThreads: SHEIN_DETAIL_CONCURRENCY_DEFAULT,
              refreshLookbackDays: 14,
              refreshBillnos: [],
              headerSource: 'realtime',
            } satisfies SheinSyncConfigResponse);
            return;
          }
          try {
            const cfg = await getSheinSyncConfig();
            const mode =
              cfg.updateMode === 'all' || cfg.updateMode === 'new' ? cfg.updateMode : 'new';
            sendResponse({
              ok: true,
              enabled: !!cfg.enabled,
              intervalSeconds: cfg.intervalSeconds ?? 0,
              updateMode: mode,
              concurrentThreads: clampSheinDetailConcurrency(cfg.concurrentThreads),
              refreshLookbackDays: cfg.refreshLookbackDays ?? 14,
              refreshBillnos: Array.isArray(cfg.refreshBillnos) ? cfg.refreshBillnos : [],
              captureRawPayload: cfg.captureRawPayload === true,
              headerSource: 'realtime',
            } satisfies SheinSyncConfigResponse);
          } catch (err) {
            sendResponse({
              ok: false,
              message: err instanceof Error ? err.message : 'Không tải được cấu hình đồng bộ.',
              enabled: false,
              intervalSeconds: 0,
              updateMode: 'new',
              concurrentThreads: SHEIN_DETAIL_CONCURRENCY_DEFAULT,
              refreshLookbackDays: 14,
              refreshBillnos: [],
              headerSource: 'realtime',
            } satisfies SheinSyncConfigResponse);
          }
          return;
        }


        case 'SHEIN_SYNC': {
          const res = await runSheinSync(message);
          sendResponse(res);
          return;
        }

        case 'PREPARE_SHEIN_SESSION': {
          const res = await prepareSheinSession({
            forceRefresh: message.forceRefresh === true,
          });
          sendResponse(res);
          return;
        }

        case 'RELOAD_ACTIVE_TAB': {
          const tab = await getActiveHttpTab();
          if (!tab?.id) {
            sendResponse({ ok: false, message: 'Không có tab để tải lại.' });
            return;
          }
          await chrome.tabs.reload(tab.id);
          sendResponse({ ok: true });
          return;
        }

        default: {
          const unknownType = String((message as { type?: string }).type ?? '?');
          console.warn('[fulfil] Unknown extension message:', unknownType);
          sendResponse({
            ok: false,
            message: `Unknown message (${unknownType}). Reload KBT Fulfil trên chrome://extensions.`,
          });
        }
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Lỗi không xác định.';
      if (msg.includes('đăng nhập') || msg.includes('Phiên')) {
        await setAuth(null);
        await clearUserCaches();
      }
      sendResponse({ ok: false, message: msg });
    }
  })();
  return true;
});
