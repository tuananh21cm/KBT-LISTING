import {
  AUTH_REFRESH_ALARM,
  SELLER_US_HOST,
  SELLER_US_ORIGIN,
  STORAGE_KEYS,
} from '../shared/config';
import type {
  ExtMessage,
  FetchHealthRecordsResponse,
  FetchListingAnalyticsResponse,
  FetchOpportunityLeadsResponse,
  FetchPayoutPaymentsResponse,
  FetchProductPreloadResponse,
  FetchSellerCommonGetResponse,
  FetchShopScoresResponse,
  StateResponse,
} from '../shared/messages';
import {
  mergeIdentityLockIntoShop,
  isCaptureReady,
  mergePassportIntoShop,
  mergePayOnboardIntoShop,
  mergeProductCategoriesIntoShop,
  mergeProductPreloadIntoShop,
  mergeSettlementAccountIntoShop,
  mergeStatementStatIntoShop,
  parsePassportAccountInfo,
  parsePayOnboardInfo,
  parseProductCategories,
  parseProductPreload,
  parseSellerCommonGet,
  parseSettlementAccount,
  parseStatementStatInfo,
} from '../shared/shop';
import type {
  ProductPreloadResponse,
  SellerUsSessionStatus,
  ShopDailyTask,
  ShopDailyTaskPlan,
  TikTokCaptureState,
  TikTokShopInfo
} from '../shared/types';
import {
  formatDailyTaskSyncBlockMessage,
  getDailyTaskProgress,
} from '../shared/dailyTaskProgress';
import { findBestMatchingTask, matchesTaskUrl } from '../shared/taskUrlMatch';
import { resolveNavigatingTaskId } from '../shared/taskPageState';
import {
  getAuth,
  getCachedTheme,
  getShopDailyTasks,
  getTikTokOpportunitySyncContext,
  getTikTokPayoutSyncConfig,
  getTikTokPayoutSyncCursor,
  getTikTokUnifiedSyncConfig,
  login,
  logout,
  maintainAuthSession,
  persistTheme,
  reportShopTaskVisit,
  syncThemeFromAccount,
  syncTikTokListingAnalytics,
  syncTikTokHealthRecords,
  syncTikTokOpportunities,
  syncTikTokPayouts,
  syncTikTokShopProfile,
  syncTikTokShopScores,
} from './kinhbacApi';
import {
  createFastSyncState,
  createUnifiedSyncState,
  finishUnifiedSync,
  logSkippedStep,
  patchUnifiedStep,
  readUnifiedSyncState,
  writeUnifiedSyncState,
} from './unifiedSyncState';
import { installSellerUsWebRequestCapture } from './sellerUsWebRequestCapture';
import {
  clearAllTaskDwells,
  clearStaleTaskDwells,
  clearTaskDwell,
  installTaskDwellAlarmListener,
  readTaskDwellMap,
  startTaskDwell,
} from './taskDwellManager';
import {
  handleContentNetwork,
  handleContentPageReady,
  installTaskPageObserver,
  readSellerUsTabSnapshot,
  readTaskNavigation,
  startTaskNavigation,
  writeTaskNavigation,
} from './taskPageObserver';
import { installOnlineSessionTracker } from './onlineSessionTracker';
import {
  beginAutoDailyTasksRun,
  installAutoDailyTaskRunner,
  readAutoDailyTasksEnabled,
  scheduleAutoDailyTasksTick,
  stopAutoDailyTasksRun,
} from './autoDailyTaskRunner';

installSellerUsWebRequestCapture();
installTaskDwellAlarmListener((taskId) => handleTaskDwellDue(taskId));
installOnlineSessionTracker();

/** Giữ phiên đăng nhập — refresh sớm trước khi access JWT hết hạn (~15 phút). */
function installAuthSessionMaintenance(): void {
  void maintainAuthSession();
  chrome.alarms.create(AUTH_REFRESH_ALARM, { periodInMinutes: 10 }).catch(() => undefined);
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === AUTH_REFRESH_ALARM) void maintainAuthSession();
  });
  chrome.runtime.onStartup.addListener(() => void maintainAuthSession());
  chrome.runtime.onInstalled.addListener(() => void maintainAuthSession());
}

installAuthSessionMaintenance();

let shopSyncInFlight = false;
let payoutSyncInFlight = false;
let opportunitySyncInFlight = false;
let analyticsSyncInFlight = false;
let healthCenterSyncInFlight = false;
let shopScoreSyncInFlight = false;
let unifiedSyncInFlight = false;


async function readCapture(): Promise<TikTokCaptureState | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.capture);
    return (raw[STORAGE_KEYS.capture] as TikTokCaptureState | undefined) ?? null;
  } catch {
    return null;
  }
}

async function readShopInfo(): Promise<TikTokShopInfo | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.shopInfo);
    return (raw[STORAGE_KEYS.shopInfo] as TikTokShopInfo | undefined) ?? null;
  } catch {
    return null;
  }
}

async function writeShopInfo(shop: TikTokShopInfo | null): Promise<void> {
  if (shop) {
    await chrome.storage.local.set({ [STORAGE_KEYS.shopInfo]: shop });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.shopInfo);
  }
}

async function markCaptureShopOk(url?: string, at?: number): Promise<void> {
  const prev = await readCapture();
  const next: TikTokCaptureState = {
    headers: prev?.headers || {},
    at: at ?? Date.now(),
    lastUrl: url || prev?.lastUrl,
    shopOk: true,
  };
  await chrome.storage.local.set({ [STORAGE_KEYS.capture]: next });
}

async function hasSellerUsTab(): Promise<chrome.tabs.Tab | null> {
  try {
    const tabs = await chrome.tabs.query({ url: `*://${SELLER_US_HOST}/*` });
    const usable = tabs.filter((t) => !!t.id);
    if (usable.length === 0) return null;
    const score = (t: chrome.tabs.Tab) => {
      let s = 0;
      if (t.active) s += 16;
      if (t.highlighted) s += 4;
      if (t.status === 'complete') s += 4;
      if (t.discarded) s -= 12;
      return s;
    };
    usable.sort((a, b) => score(b) - score(a) || (b.id ?? 0) - (a.id ?? 0));
    return usable[0] ?? null;
  } catch {
    return null;
  }
}

function resolveSellerUsStatus(
  tabOpen: boolean,
  capture: TikTokCaptureState | null,
  shopInfo: TikTokShopInfo | null,
): SellerUsSessionStatus {
  if (shopInfo || isCaptureReady(capture)) return 'logged_in';
  if (capture && Object.keys(capture.headers || {}).length > 0) return 'no_capture';
  if (tabOpen) return 'tab_open';
  return 'no_tab';
}

async function buildState(): Promise<StateResponse> {
  const auth = await getAuth();
  const theme = auth?.user?.theme ?? (await getCachedTheme());
  const unifiedSync = await readUnifiedSyncState();

  if (!auth) {
    return {
      authenticated: false,
      user: null,
      theme,
      shopInfo: null,
      capture: null,
      sellerUsTabOpen: false,
      sellerUsStatus: 'no_tab',
      unifiedSync,
      dailyTasks: null,
      taskLoadingTaskId: null,
      taskDwells: {},
      autoDailyTasks: false,
    };
  }

  void syncThemeFromAccount();

  const [capture, shopInfo, tab, dailyTasks, taskLoadingTaskId, taskDwells, autoDailyTasks] =
    await Promise.all([
      readCapture(),
      readShopInfo(),
      hasSellerUsTab(),
      readDailyTaskPlan(),
      readTaskLoadingTaskId(),
      readTaskDwellMap(),
      readAutoDailyTasksEnabled(),
    ]);
  const sellerUsTabOpen = !!tab;
  const sellerUsStatus = resolveSellerUsStatus(sellerUsTabOpen, capture, shopInfo);

  return {
    authenticated: true,
    user: auth.user,
    theme,
    shopInfo,
    capture,
    sellerUsTabOpen,
    sellerUsStatus,
    unifiedSync,
    dailyTasks,
    taskLoadingTaskId,
    taskDwells,
    autoDailyTasks,
  };
}

async function openSellerUsTab(): Promise<chrome.tabs.Tab> {
  const existing = await hasSellerUsTab();
  if (existing?.id != null) {
    await chrome.tabs.update(existing.id, { active: true });
    if (existing.windowId != null) {
      await chrome.windows.update(existing.windowId, { focused: true });
    }
    return existing;
  }
  return chrome.tabs.create({ url: SELLER_US_ORIGIN });
}

async function persistFetchResult(
  result: FetchSellerCommonGetResponse,
): Promise<{
  ok: boolean;
  message: string;
  shopInfo: TikTokShopInfo | null;
  /** null khi đã có publish_limit; ngược lại là lý do cụ thể để hiện trong log sync. */
  preloadReason: string | null;
}> {
  const passport = parsePassportAccountInfo(result.accountRaw);
  const categories = parseProductCategories(result.onboardRaw);
  const preload = parseProductPreload(result.preloadRaw);
  const payOnboard = parsePayOnboardInfo(result.payOnboardRaw);
  const settlement = parseSettlementAccount(result.settlementRaw);
  const statementStat = parseStatementStatInfo(result.statementStatRaw);
  const hasPreload =
    (preload.logisticsServices && preload.logisticsServices.length > 0) ||
    preload.publishLimit != null;
  // Lỗi request (HTTP / timeout / code) do content báo; thiếu đúng field thì parser nói.
  const preloadReason =
    result.preloadOk === false
      ? result.preloadMessage ?? preload.publishLimitReason
      : preload.publishLimitReason;
  const hasAux = !!(
    passport ||
    categories ||
    hasPreload ||
    payOnboard ||
    settlement ||
    statementStat ||
    result.identityVideoVerificationLock === true
  );
  const prev = await readShopInfo();

  const mergeAll = (base: TikTokShopInfo) => {
    let merged = mergePassportIntoShop(base, {
      accountName: prev?.accountName ?? base.accountName ?? null,
      accountEmail: prev?.accountEmail ?? base.accountEmail ?? null,
      accountMobile: prev?.accountMobile ?? base.accountMobile ?? null,
      isBlocked: prev?.isBlocked ?? base.isBlocked ?? null,
      isBlocking: prev?.isBlocking ?? base.isBlocking ?? null,
    });
    merged = {
      ...merged,
      companyAddress: prev?.companyAddress ?? merged.companyAddress ?? null,
      companyCertNum: prev?.companyCertNum ?? merged.companyCertNum ?? null,
      companyCertType: prev?.companyCertType ?? merged.companyCertType ?? null,
      payEntityType: prev?.payEntityType ?? merged.payEntityType ?? null,
      owner: prev?.owner ?? merged.owner ?? null,
      bankAccountNumber: prev?.bankAccountNumber ?? merged.bankAccountNumber ?? null,
      settlementPaymentMethod:
        prev?.settlementPaymentMethod ?? merged.settlementPaymentMethod ?? null,
      settlementBeneficiaryName:
        prev?.settlementBeneficiaryName ?? merged.settlementBeneficiaryName ?? null,
      settlementAccountType:
        prev?.settlementAccountType ?? merged.settlementAccountType ?? null,
      settlementAccountStatus:
        prev?.settlementAccountStatus ?? merged.settlementAccountStatus ?? null,
      onHoldAmount: prev?.onHoldAmount ?? merged.onHoldAmount ?? null,
      onHoldInTransitAmount: prev?.onHoldInTransitAmount ?? merged.onHoldInTransitAmount ?? null,
      onHoldAwaitingSettlementAmount:
        prev?.onHoldAwaitingSettlementAmount ?? merged.onHoldAwaitingSettlementAmount ?? null,
      onHoldReserveAmount: prev?.onHoldReserveAmount ?? merged.onHoldReserveAmount ?? null,
      deli: prev?.deli ?? merged.deli ?? null,
    };
    merged = mergePassportIntoShop(merged, passport);
    merged = mergeProductCategoriesIntoShop(
      merged,
      categories ?? prev?.productCategories ?? base.productCategories,
    );
    // Số hớt thụ động từ trang (`prev.publishLimit`) giữ nguyên nhãn nguồn của nó, vì không
    // request nào lần này lấy được số — nhãn nói đúng chặng đã cho ra con số đang lưu.
    merged = mergeProductPreloadIntoShop(merged, {
      logisticsServices:
        preload.logisticsServices ?? prev?.logisticsServices ?? base.logisticsServices,
      publishLimit: preload.publishLimit ?? prev?.publishLimit ?? base.publishLimit,
      publishLimitSource:
        preload.publishLimit != null
          ? result.preloadSource ?? 'isolated'
          : prev?.publishLimitSource ?? base.publishLimitSource ?? null,
    });
    merged = mergePayOnboardIntoShop(merged, payOnboard);
    merged = mergeSettlementAccountIntoShop(merged, settlement);
    merged = mergeStatementStatIntoShop(merged, statementStat);
    merged = mergeIdentityLockIntoShop(merged, result.identityVideoVerificationLock);
    return merged;
  };

  if (!result.ok || !result.raw) {
    if (prev && hasAux) {
      const merged = mergeAll(prev);
      await writeShopInfo(merged);
      return {
        ok: true,
        message: `Đã cập nhật thông tin phụ cho «${merged.shopName}».`,
        shopInfo: merged,
        preloadReason,
      };
    }
    return {
      ok: false,
      message: result.message || 'Không lấy được shop từ seller-us.',
      shopInfo: prev,
      preloadReason,
    };
  }

  const shop = parseSellerCommonGet(result.raw, result.at ?? Date.now());
  if (!shop) {
    if (prev && hasAux) {
      const merged = mergeAll(prev);
      await writeShopInfo(merged);
      return {
        ok: true,
        message: `Đã cập nhật thông tin phụ cho «${merged.shopName}».`,
        shopInfo: merged,
        preloadReason,
      };
    }
    return {
      ok: false,
      message: result.message || 'Payload seller/common/get thiếu seller.',
      shopInfo: prev,
      preloadReason,
    };
  }

  const merged = mergeAll(shop);
  await writeShopInfo(merged);
  await markCaptureShopOk(result.url, result.at);
  return {
    ok: true,
    message: `Đã lấy shop «${merged.shopName}».`,
    shopInfo: merged,
    preloadReason,
  };
}

/**
 * Số hớt từ response product_creation/preload của chính trang (bridge MAIN world). Không tốn
 * request nào và không bị gateway product từ chối, nên đây là nguồn ưu tiên nhất cho
 * `publish_limit` — bước Info sau đó thấy sẵn số và bỏ hẳn vòng retry.
 */
async function persistCapturedPreload(
  raw: ProductPreloadResponse | null | undefined,
  sellerId?: string,
): Promise<boolean> {
  const preload = parseProductPreload(raw);
  if (!preload.publishLimit) return false;
  const shop = await readShopInfo();
  if (!shop) return false;
  // Tab khác shop thì bỏ, đừng gán số của shop này cho shop đang lưu.
  const captured = (sellerId || '').trim();
  if (captured && shop.sellerId && captured !== shop.sellerId) return false;
  const merged = mergeProductPreloadIntoShop(shop, {
    logisticsServices: preload.logisticsServices,
    publishLimit: preload.publishLimit,
    publishLimitSource: 'page-harvest',
  });
  await writeShopInfo(merged);
  return true;
}

/** Timeout chờ content fetch / sendMessage (không đợi request ngầm khác của seller-us). */
const REFRESH_SHOP_TIMEOUT_MS = 14_000;

function waitMs(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Chờ shopInfo mới hơn `sinceAt` (từ auto-fetch content / SELLER_COMMON_GET_RESULT).
 * Kết thúc sớm khi đã đủ thông tin — không phụ thuộc fetch message còn treo.
 */
function waitForShopInfoAfter(
  sinceAt: number,
  timeoutMs: number,
): Promise<TikTokShopInfo | null> {
  return new Promise((resolve) => {
    let done = false;
    const finish = (shop: TikTokShopInfo | null) => {
      if (done) return;
      done = true;
      chrome.storage.onChanged.removeListener(onChanged);
      clearTimeout(timer);
      resolve(shop);
    };

    const onChanged = (
      changes: { [key: string]: chrome.storage.StorageChange },
      area: string,
    ) => {
      if (area !== 'local') return;
      const change = changes[STORAGE_KEYS.shopInfo];
      if (!change?.newValue) return;
      const shop = change.newValue as TikTokShopInfo;
      const fetchedAt = shop.fetchedAt ?? 0;
      if (fetchedAt >= sinceAt - 500) finish(shop);
    };

    chrome.storage.onChanged.addListener(onChanged);
    const timer = setTimeout(() => {
      void readShopInfo().then((shop) => {
        if (shop && (shop.fetchedAt ?? 0) >= sinceAt - 500) finish(shop);
        else finish(null);
      });
    }, timeoutMs);

    void readShopInfo().then((shop) => {
      if (shop && (shop.fetchedAt ?? 0) >= sinceAt - 500) finish(shop);
    });
  });
}

async function waitTabComplete(tabId: number, timeoutMs: number): Promise<void> {
  const existing = await chrome.tabs.get(tabId).catch(() => null);
  if (existing?.status === 'complete') return;

  await new Promise<void>((resolve) => {
    let settled = false;
    const done = () => {
      if (settled) return;
      settled = true;
      chrome.tabs.onUpdated.removeListener(onUpdated);
      clearTimeout(timer);
      resolve();
    };
    const onUpdated = (id: number, info: chrome.tabs.TabChangeInfo) => {
      if (id === tabId && info.status === 'complete') done();
    };
    chrome.tabs.onUpdated.addListener(onUpdated);
    const timer = setTimeout(done, timeoutMs);
  });
}

function isNoReceiverError(err: unknown): boolean {
  const msg = err instanceof Error ? err.message : String(err);
  return /Receiving end does not exist|Could not establish connection/i.test(msg);
}

function mapContentConnectionError(err: unknown): Error {
  if (isNoReceiverError(err)) {
    return new Error(
      'Không nối được content script trên tab seller-us. F5 trang seller-us.tiktok.com rồi thử lại.',
    );
  }
  return err instanceof Error ? err : new Error(String(err));
}

async function pingContent(tabId: number): Promise<boolean> {
  try {
    const res = await Promise.race([
      chrome.tabs.sendMessage(tabId, { type: 'PING_CONTENT' }) as Promise<{ ok?: boolean }>,
      waitMs(600).then(() => null),
    ]);
    return !!res?.ok;
  } catch {
    return false;
  }
}

async function injectContentScript(tabId: number): Promise<boolean> {
  try {
    await chrome.scripting.executeScript({
      target: { tabId, allFrames: false },
      files: ['content.js'],
    });
    await waitMs(150);
    return pingContent(tabId);
  } catch {
    return false;
  }
}

/** Hook MAIN world đã sẵn sàng trên tab nào — xoá khi tab điều hướng / đóng. */
const mainHooksReady = new Set<number>();

/** Script MAIN world (`world: 'MAIN'`) — cả hai đều tự chốt cờ nên inject lại là no-op. */
const MAIN_WORLD_FILES = ['pageIdleHook.js', 'sellerProductBridge.js'];

async function pingProductBridge(tabId: number): Promise<boolean> {
  try {
    const res = await Promise.race([
      chrome.tabs.sendMessage(tabId, { type: 'PING_PRODUCT_BRIDGE' }) as Promise<{ ok?: boolean }>,
      waitMs(1_500).then(() => null),
    ]);
    return !!res?.ok;
  } catch {
    return false;
  }
}

async function injectMainWorldHooks(tabId: number): Promise<boolean> {
  try {
    await chrome.scripting.executeScript({
      target: { tabId, allFrames: false },
      world: 'MAIN',
      files: MAIN_WORLD_FILES,
    });
    return true;
  } catch {
    return false;
  }
}

/**
 * `content_scripts` chỉ nạp world MAIN lúc `document_start`, nên tab mở sẵn trước khi
 * cài/reload extension không có hook — trước đây phải F5 tay. Ping rồi inject bù.
 * Inject muộn chỉ hook được request **sau** đó; payload trang đã gửi trước thì mất.
 */
async function ensureSellerUsMainHooks(tabId: number): Promise<void> {
  if (mainHooksReady.has(tabId)) return;
  if (await pingProductBridge(tabId)) {
    mainHooksReady.add(tabId);
    return;
  }
  if (!(await injectMainWorldHooks(tabId))) return;
  await waitMs(120);
  if (await pingProductBridge(tabId)) mainHooksReady.add(tabId);
}

/** Tab tải trang mới → hook MAIN của document cũ không còn, phải probe lại. */
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === 'loading') mainHooksReady.delete(tabId);
});

chrome.tabs.onRemoved.addListener((tabId) => {
  mainHooksReady.delete(tabId);
});

/** Cài / cập nhật / khởi động Chrome: bù hook cho tab seller-us đang mở, khỏi chờ lượt sync. */
async function injectMainWorldHooksIntoOpenTabs(): Promise<void> {
  try {
    const tabs = await chrome.tabs.query({ url: `${SELLER_US_ORIGIN}/*` });
    for (const tab of tabs) {
      if (tab.id == null) continue;
      mainHooksReady.delete(tab.id);
      await injectMainWorldHooks(tab.id);
    }
  } catch {
    /* tab đóng giữa đường — lượt sync sau tự bù */
  }
}

chrome.runtime.onInstalled.addListener(() => {
  void injectMainWorldHooksIntoOpenTabs();
});

chrome.runtime.onStartup.addListener(() => {
  void injectMainWorldHooksIntoOpenTabs();
});

/** Content sống rồi thì bù luôn hook MAIN world — cùng một lý do «tab mở sẵn». */
async function ensureSellerUsContent(tabId: number): Promise<void> {
  await connectSellerUsContent(tabId);
  await ensureSellerUsMainHooks(tabId);
}

/**
 * Content script không tự inject vào tab đã mở trước khi load lại extension,
 * tab discarded, hoặc trang còn loading — tabs.sendMessage lúc đó ném
 * "Receiving end does not exist". Ping → inject → reload.
 */
async function connectSellerUsContent(tabId: number): Promise<void> {
  const tab = await chrome.tabs.get(tabId).catch(() => null);
  if (!tab) {
    throw new Error('Tab seller-us đã đóng.');
  }

  if (tab.discarded) {
    await chrome.tabs.reload(tabId);
    await waitTabComplete(tabId, 10_000);
    await waitMs(300);
  } else if (tab.status !== 'complete') {
    await waitTabComplete(tabId, 10_000);
    await waitMs(200);
  }

  if (await pingContent(tabId)) return;

  for (let i = 0; i < 4; i++) {
    await waitMs(250);
    if (await pingContent(tabId)) return;
  }

  if (await injectContentScript(tabId)) return;

  await chrome.tabs.reload(tabId);
  await waitTabComplete(tabId, 10_000);
  await waitMs(400);
  if (await pingContent(tabId)) return;
  if (await injectContentScript(tabId)) return;

  throw new Error(
    'Không nối được content script trên tab seller-us. F5 trang seller-us.tiktok.com rồi thử lại.',
  );
}

async function sendTabMessage<T>(
  tabId: number,
  payload: object,
  timeoutMs: number,
  timeoutLabel: string,
): Promise<T> {
  await ensureSellerUsContent(tabId);
  const send = () =>
    Promise.race([
      chrome.tabs.sendMessage(tabId, payload) as Promise<T>,
      waitMs(timeoutMs).then(() => {
        throw new Error(`Timeout ${timeoutMs / 1000}s — ${timeoutLabel}`);
      }),
    ]);
  try {
    return await send();
  } catch (err) {
    if (!isNoReceiverError(err)) throw mapContentConnectionError(err);
    await ensureSellerUsContent(tabId);
    try {
      return await send();
    } catch (retryErr) {
      throw mapContentConnectionError(retryErr);
    }
  }
}

async function sendFetchToTab(
  tabId: number,
  timeoutMs: number,
): Promise<FetchSellerCommonGetResponse> {
  return sendTabMessage<FetchSellerCommonGetResponse>(
    tabId,
    { type: 'FETCH_SELLER_COMMON_GET' },
    timeoutMs,
    'content không trả lời.',
  );
}

/**
 * `harvest 6/8` — bao nhiêu slot dùng payload trang tự gọi (hớt từ MAIN world) thay vì
 * extension tự gọi. Số này nói thẳng lượt sync tốn bao nhiêu request thật.
 */
function formatHarvestNote(used?: number, total?: number): string | null {
  if (!total || used == null) return null;
  return `harvest ${used}/${total}`;
}

/** Gửi FETCH tới content; luôn đợi response đầy đủ (dùng cho bước Info Sync). */
async function fetchShopFromTab(tabId: number): Promise<{
  ok: boolean;
  message: string;
  shopInfo: TikTokShopInfo | null;
  preloadReason: string | null;
  harvestNote: string | null;
}> {
  const result = await sendFetchToTab(tabId, REFRESH_SHOP_TIMEOUT_MS);
  const persisted = await persistFetchResult(result);
  return {
    ...persisted,
    harvestNote: formatHarvestNote(result.harvestUsed, result.harvestTotal),
  };
}

/** Content tự timeout 12s cho preload — cho thêm ~1s để message về kịp. */
const PRELOAD_RETRY_TIMEOUT_MS = 13_000;

/**
 * Thử lại **đúng** endpoint preload rồi merge `publishLimit` vào shopInfo đã lưu.
 * Rẻ hơn chạy lại cả chùm 8 request của `seller/common/get` chỉ để lấy một field.
 */
async function retryPublishLimit(
  tabId: number,
  shop: TikTokShopInfo,
): Promise<{ shopInfo: TikTokShopInfo; reason: string | null }> {
  let res: FetchProductPreloadResponse;
  try {
    res = await sendTabMessage<FetchProductPreloadResponse>(
      tabId,
      { type: 'FETCH_PRODUCT_PRELOAD', sellerId: shop.sellerId },
      PRELOAD_RETRY_TIMEOUT_MS,
      'content không trả product preload.',
    );
  } catch (err) {
    return { shopInfo: shop, reason: mapContentConnectionError(err).message };
  }

  if (!res.ok) {
    return { shopInfo: shop, reason: res.message ?? 'preload thất bại' };
  }

  const preload = parseProductPreload(res.raw);
  if (!preload.publishLimit) {
    return { shopInfo: shop, reason: preload.publishLimitReason };
  }

  const merged = mergeProductPreloadIntoShop(shop, {
    ...preload,
    publishLimitSource: res.source ?? 'isolated',
  });
  await writeShopInfo(merged);
  return { shopInfo: merged, reason: null };
}

/** Gửi FETCH tới content; race với shop đã lưu từ auto-fetch — đủ rồi thì thôi. */
async function refreshShopFromTab(): Promise<{
  ok: boolean;
  message: string;
}> {
  let tab = await hasSellerUsTab();
  if (!tab?.id) {
    return {
      ok: false,
      message: 'Chưa mở seller-us.tiktok.com — bấm «Mở seller-us» và đăng nhập seller trước.',
    };
  }

  const startedAt = Date.now();

  const raceOnce = async (tabId: number) => {
    const fromMessage = (async () => {
      const result = await sendFetchToTab(tabId, REFRESH_SHOP_TIMEOUT_MS);
      return { source: 'message' as const, ...(await persistFetchResult(result)) };
    })();

    // Chỉ resolve khi thật sự có shop mới — null/timeout không được thắng race.
    const fromStorage = new Promise<{
      source: 'storage';
      ok: true;
      message: string;
      shopInfo: TikTokShopInfo;
    }>((resolve) => {
      void waitForShopInfoAfter(startedAt, REFRESH_SHOP_TIMEOUT_MS).then((shop) => {
        if (!shop) return;
        resolve({
          source: 'storage',
          ok: true,
          message: `Đã lấy shop «${shop.shopName}».`,
          shopInfo: shop,
        });
      });
    });

    try {
      const winner = await Promise.race([fromMessage, fromStorage]);
      if (winner.ok) return { ok: true, message: winner.message };

      // Message fail — còn cửa ngắn từ auto-fetch song song.
      const shop = await waitForShopInfoAfter(startedAt, 2_000);
      if (shop) return { ok: true, message: `Đã lấy shop «${shop.shopName}».` };
      return { ok: false, message: winner.message };
    } catch (err) {
      const shop = await waitForShopInfoAfter(startedAt, 1_500);
      if (shop) return { ok: true, message: `Đã lấy shop «${shop.shopName}».` };
      throw err;
    }
  };

  try {
    return await raceOnce(tab.id);
  } catch {
    // Content chưa inject — reload, đợi complete (không cứng 2.5s), rồi thử lại.
    try {
      await chrome.tabs.reload(tab.id);
      await waitTabComplete(tab.id, 8_000);
      await waitMs(400);
      tab = (await hasSellerUsTab()) || tab;
      if (!tab.id) throw new Error('Tab seller-us mất sau reload.');
      return await raceOnce(tab.id);
    } catch (err) {
      const shop = await readShopInfo();
      if (shop && (shop.fetchedAt ?? 0) >= startedAt - 500) {
        return { ok: true, message: `Đã lấy shop «${shop.shopName}».` };
      }
      return {
        ok: false,
        message:
          (err as Error).message ||
          'Không nối được content script — F5 trang seller-us rồi bấm Làm mới.',
      };
    }
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

async function executeKinhbacShopSync(
  shop: TikTokShopInfo,
  runId?: string,
  syncNote?: string | null,
): Promise<{ ok: boolean; message: string }> {
  try {
    const auth = await getAuth();
    if (!auth) {
      return { ok: false, message: 'Chưa đăng nhập kinhbac.' };
    }
    await syncTikTokShopProfile(shop, runId, syncNote);
    const message = `Đã sync «${shop.shopName || shop.sellerId}» lên Kinhbac.`;
    return { ok: true, message };
  } catch (err) {
    const message = (err as Error).message || String(err);
    console.warn('[kb-tts] sync tiktok-profile failed:', message);
    return { ok: false, message: `Info thất bại: ${message}` };
  }
}

async function runShopSync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };
  if (shopSyncInFlight) return { ok: false, message: 'Đang Info…' };

  const runId = opts?.runId;
  shopSyncInFlight = true;
  if (runId) await patchUnifiedStep(runId, 'profile', { status: 'running', message: 'Đang Info…' });

  try {
    const tab = await hasSellerUsTab();
    if (!tab?.id) {
      const message = 'Chưa mở seller-us.tiktok.com.';
      if (runId) {
        await patchUnifiedStep(runId, 'profile', { status: 'skipped', message });
        await logSkippedStep(runId, 'profile', message);
      }
      return { ok: false, message };
    }

    const fetchResult = await fetchShopFromTab(tab.id);
    if (!fetchResult.ok) {
      if (runId) await patchUnifiedStep(runId, 'profile', { status: 'failed', message: fetchResult.message });
      return { ok: false, message: fetchResult.message };
    }

    let shop = fetchResult.shopInfo ?? (await readShopInfo());
    let limitReason = fetchResult.preloadReason;
    if (shop?.sellerId && !shop.publishLimit) {
      const retry = await retryPublishLimit(tab.id, shop);
      shop = retry.shopInfo;
      limitReason = retry.reason;
    }

    if (!shop?.sellerId) {
      const message = 'Chưa có sellerId — mở seller-us rồi bấm Sync.';
      if (runId) {
        await patchUnifiedStep(runId, 'profile', { status: 'skipped', message });
        await logSkippedStep(runId, 'profile', message);
      }
      return { ok: false, message };
    }

    // Nhãn nguồn (`page-harvest` / `bridge` / `isolated …`) đi kèm cả message bước Info và
    // TikTokShopSyncLogs, để log nói được vì sao lấy được hay không — không chỉ biết là trượt.
    const limit = shop.publishLimit;
    const limitHint =
      limit != null
        ? ` · Listing ${limit.publishedNum}/${limit.totalLimit} (${
            shop.publishLimitSource ?? 'nguồn cũ'
          }${limitReason ? ` · ${limitReason}` : ''})`
        : ` · chưa lấy được publish_limit từ TikTok preload${
            limitReason ? `: ${limitReason}` : ''
          }`;

    const harvestHint = fetchResult.harvestNote ? ` · ${fetchResult.harvestNote}` : '';
    const note = `${limitHint}${harvestHint}`.replace(/^ · /, '');

    const result = await executeKinhbacShopSync(shop, runId, note);
    if (runId) {
      await patchUnifiedStep(runId, 'profile', {
        status: result.ok ? 'completed' : 'failed',
        message: `${result.message}${limitHint}${harvestHint}`,
      });
    }
    return result;
  } catch (err) {
    const message = mapContentConnectionError(err).message || String(err);
    if (runId) await patchUnifiedStep(runId, 'profile', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    shopSyncInFlight = false;
  }
}

async function sendPayoutFetchToTab(
  tabId: number,
  payload: {
    sellerId: string;
    maxDate?: string | null;
    cursorOccurredAt?: string | null;
    cursorPaymentId?: string | null;
  },
  timeoutMs: number,
): Promise<FetchPayoutPaymentsResponse> {
  return sendTabMessage<FetchPayoutPaymentsResponse>(
    tabId,
    { type: 'FETCH_PAYOUT_PAYMENTS', ...payload },
    timeoutMs,
    'content không trả payout.',
  );
}

async function runPayoutSync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };

  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) {
      await patchUnifiedStep(runId, 'payout', { status: 'skipped', message });
      await logSkippedStep(runId, 'payout', message);
    }
    return { ok: false, message };
  }

  const tab = await hasSellerUsTab();
  if (!tab?.id) {
    const message = 'Chưa mở seller-us.tiktok.com.';
    if (runId) {
      await patchUnifiedStep(runId, 'payout', { status: 'skipped', message });
      await logSkippedStep(runId, 'payout', message, sellerId);
    }
    return { ok: false, message };
  }

  if (payoutSyncInFlight) return { ok: false, message: 'Đang Finance…' };

  payoutSyncInFlight = true;
  if (runId) await patchUnifiedStep(runId, 'payout', { status: 'running', message: 'Đang Finance…' });

  try {
    const [cfg, cursor] = await Promise.all([
      getTikTokPayoutSyncConfig().catch(() => null),
      getTikTokPayoutSyncCursor(sellerId).catch(() => null),
    ]);

    const fetched = await sendPayoutFetchToTab(
      tab.id,
      {
        sellerId,
        maxDate: cfg?.maxDate ?? null,
        cursorOccurredAt: cursor?.latestOccurredAt ?? null,
        cursorPaymentId: cursor?.latestExternalTransactionId ?? null,
      },
      120_000,
    );

    if (!fetched.ok) {
      const message = fetched.message || 'Không lấy được payment list.';
      if (runId) await patchUnifiedStep(runId, 'payout', { status: 'failed', message });
      return { ok: false, message };
    }

    const payments = fetched.payments ?? [];
    if (payments.length === 0) {
      // Vẫn gọi API để backend ghi TikTokShopSyncLog completed — nhiệm vụ "Đồng bộ" đếm log, không phụ thuộc có payout mới.
      await syncTikTokPayouts({ runId, externalShopId: sellerId, sellerId, payments: [] });
      const message = 'Không có payout mới hơn cursor / max date.';
      if (runId) await patchUnifiedStep(runId, 'payout', { status: 'completed', message });
      return { ok: true, message };
    }

    const result = await syncTikTokPayouts({ runId, externalShopId: sellerId, sellerId, payments });
    const linked = fetched.ordersLinked ?? payments.filter((p) => (p.orders?.length ?? 0) > 0).length;
    const message =
      `Finance: +${result.inserted} mới, ${result.updated} cập nhật` +
      (result.ordersMarkedPaid ? `, ${result.ordersMarkedPaid} đơn` : '') +
      `, chi tiết ${linked}/${payments.length}` +
      (result.telegramSent ? `, TG ${result.telegramSent}` : '') +
      '.';
    if (runId) await patchUnifiedStep(runId, 'payout', { status: 'completed', message });
    return { ok: true, message };
  } catch (err) {
    const message = mapContentConnectionError(err).message || String(err);
    if (runId) await patchUnifiedStep(runId, 'payout', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    payoutSyncInFlight = false;
  }
}

async function sendOpportunityFetchToTab(
  tabId: number,
  payload: {
    sellerId: string;
    categoryInfos: { cateId: number; level: number }[];
    categoryInfoGroups?: { cateId: number; level: number }[][];
  },
  timeoutMs: number,
): Promise<FetchOpportunityLeadsResponse> {
  return sendTabMessage<FetchOpportunityLeadsResponse>(
    tabId,
    { type: 'FETCH_OPPORTUNITY_LEADS', ...payload },
    timeoutMs,
    'content không trả keywords.',
  );
}

async function runOpportunitySync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };

  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) {
      await patchUnifiedStep(runId, 'opportunity', { status: 'skipped', message });
      await logSkippedStep(runId, 'opportunity', message);
    }
    return { ok: false, message };
  }

  const tab = await hasSellerUsTab();
  if (!tab?.id) {
    const message = 'Chưa có tab seller-us.tiktok.com.';
    if (runId) {
      await patchUnifiedStep(runId, 'opportunity', { status: 'skipped', message });
      await logSkippedStep(runId, 'opportunity', message, sellerId);
    }
    return { ok: false, message };
  }

  if (opportunitySyncInFlight) return { ok: false, message: 'Đang Keywords — đợi xong.' };

  opportunitySyncInFlight = true;
  if (runId) await patchUnifiedStep(runId, 'opportunity', { status: 'running', message: 'Đang Keywords…' });

  try {
    const ctx = await getTikTokOpportunitySyncContext(sellerId);
    const groups =
      Array.isArray(ctx.categoryInfoGroups) && ctx.categoryInfoGroups.length > 0
        ? ctx.categoryInfoGroups
        : ctx.categoryInfos?.length
          ? [ctx.categoryInfos]
          : [];
    if (!ctx.hasCatalog || groups.length === 0) {
      const message = 'Shop chưa gán danh mục niche';
      if (runId) {
        await patchUnifiedStep(runId, 'opportunity', { status: 'skipped', message });
        await logSkippedStep(runId, 'opportunity', message, sellerId);
      }
      return { ok: false, message };
    }

    const timeoutMs = Math.min(300_000, 90_000 + 60_000 * groups.length);
    const fetched = await sendOpportunityFetchToTab(
      tab.id,
      { sellerId, categoryInfos: groups[0] ?? [], categoryInfoGroups: groups },
      timeoutMs,
    );

    if (!fetched?.ok) {
      const message = fetched?.message || 'Scrape keywords thất bại — giữ snapshot cũ.';
      if (runId) await patchUnifiedStep(runId, 'opportunity', { status: 'failed', message });
      return { ok: false, message };
    }

    if (!Array.isArray(fetched.items)) {
      const message = 'Scrape keywords không trả danh sách — giữ snapshot cũ.';
      if (runId) await patchUnifiedStep(runId, 'opportunity', { status: 'failed', message });
      return { ok: false, message };
    }

    const result = await syncTikTokOpportunities({
      runId,
      externalShopId: sellerId,
      sellerId,
      items: fetched.items,
    });
    const niche =
      Array.isArray(ctx.catalogNames) && ctx.catalogNames.length > 0
        ? ctx.catalogNames.map((n) => n.split('/').pop()?.trim() || n).join(' · ')
        : '';
    const saved = result.linked ?? result.inserted;
    const message = niche ? `Đã lưu ${saved} keywords · ${niche}` : `Đã lưu ${saved} keywords.`;
    if (runId) await patchUnifiedStep(runId, 'opportunity', { status: 'completed', message });
    return { ok: true, message };
  } catch (err) {
    const message = mapContentConnectionError(err).message || 'Keywords thất bại.';
    if (runId) await patchUnifiedStep(runId, 'opportunity', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    opportunitySyncInFlight = false;
  }
}

async function sendAnalyticsFetchToTab(
  tabId: number,
  payload: { sellerId: string },
  timeoutMs: number,
): Promise<FetchListingAnalyticsResponse> {
  return sendTabMessage<FetchListingAnalyticsResponse>(
    tabId,
    { type: 'FETCH_LISTING_ANALYTICS', ...payload },
    timeoutMs,
    'content không trả analytics.',
  );
}

async function runAnalyticsSync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };

  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) {
      await patchUnifiedStep(runId, 'analytics', { status: 'skipped', message });
      await logSkippedStep(runId, 'analytics', message);
    }
    return { ok: false, message };
  }

  const tab = await hasSellerUsTab();
  if (!tab?.id) {
    const message = 'Chưa có tab seller-us.tiktok.com.';
    if (runId) {
      await patchUnifiedStep(runId, 'analytics', { status: 'skipped', message });
      await logSkippedStep(runId, 'analytics', message, sellerId);
    }
    return { ok: false, message };
  }

  if (analyticsSyncInFlight) return { ok: false, message: 'Đang Analytics — đợi xong.' };

  analyticsSyncInFlight = true;
  if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'running', message: 'Đang Analytics…' });

  try {
    const fetched = await sendAnalyticsFetchToTab(tab.id, { sellerId }, 240_000);

    if (!fetched?.ok) {
      const message = fetched?.message || 'Scrape analytics thất bại — giữ snapshot cũ.';
      if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'failed', message });
      return { ok: false, message };
    }

    if (!Array.isArray(fetched.items)) {
      const message = 'Scrape analytics không trả danh sách — giữ snapshot cũ.';
      if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'failed', message });
      return { ok: false, message };
    }

    if (fetched.statsKeys?.length) {
      console.info('[kb-tts] analytics stats keys:', fetched.statsKeys.join(', '));
    }

    if (fetched.items.length === 0) {
      // Vẫn gọi API để backend ghi TikTokShopSyncLog completed — extension checklist có thể xanh nhưng board cần log.
      await syncTikTokListingAnalytics({
        runId,
        externalShopId: sellerId,
        sellerId,
        preset: fetched.preset,
        rangeStart: fetched.rangeStart,
        rangeEnd: fetched.rangeEnd,
        items: [],
      });
      const message = 'Compass không có product Live nào trong 7 ngày.';
      if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'completed', message });
      return { ok: true, message };
    }

    const result = await syncTikTokListingAnalytics({
      runId,
      externalShopId: sellerId,
      sellerId,
      preset: fetched.preset,
      rangeStart: fetched.rangeStart,
      rangeEnd: fetched.rangeEnd,
      items: fetched.items,
    });

    const saved = result.inserted + result.updated;
    const range =
      fetched.rangeStart && fetched.rangeEnd ? ` · ${fetched.rangeStart}→${fetched.rangeEnd}` : '';
    const capped = fetched.truncated ? ' · chỉ top GMV (vượt trần 3000)' : '';
    const message = `Đã lưu ${saved} product (${result.linked} khớp listing)${range}${capped}`;
    if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'completed', message });
    return { ok: true, message };
  } catch (err) {
    const message = mapContentConnectionError(err).message || 'Analytics thất bại.';
    if (runId) await patchUnifiedStep(runId, 'analytics', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    analyticsSyncInFlight = false;
  }
}

async function sendHealthRecordsFetchToTab(
  tabId: number,
  payload: { sellerId: string },
  timeoutMs: number,
): Promise<FetchHealthRecordsResponse> {
  return sendTabMessage<FetchHealthRecordsResponse>(
    tabId,
    { type: 'FETCH_HEALTH_RECORDS', ...payload },
    timeoutMs,
    'content không trả health records.',
  );
}

async function runHealthCenterSync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };

  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) {
      await patchUnifiedStep(runId, 'healthCenter', { status: 'skipped', message });
      await logSkippedStep(runId, 'health_center', message);
    }
    return { ok: false, message };
  }

  const tab = await hasSellerUsTab();
  if (!tab?.id) {
    const message = 'Chưa có tab seller-us.tiktok.com.';
    if (runId) {
      await patchUnifiedStep(runId, 'healthCenter', { status: 'skipped', message });
      await logSkippedStep(runId, 'health_center', message, sellerId);
    }
    return { ok: false, message };
  }

  if (healthCenterSyncInFlight) return { ok: false, message: 'Đang Health Center — đợi xong.' };

  healthCenterSyncInFlight = true;
  if (runId) {
    await patchUnifiedStep(runId, 'healthCenter', { status: 'running', message: 'Đang Health Center…' });
  }

  try {
    const fetched = await sendHealthRecordsFetchToTab(tab.id, { sellerId }, 120_000);

    if (!fetched?.ok) {
      const message = fetched?.message || 'Scrape Health Center thất bại.';
      if (runId) await patchUnifiedStep(runId, 'healthCenter', { status: 'failed', message });
      return { ok: false, message };
    }

    const items = Array.isArray(fetched.items) ? fetched.items : [];
    const result = await syncTikTokHealthRecords({
      runId,
      externalShopId: sellerId,
      sellerId,
      items,
    });

    const violations = items.filter((x) => x.recordType === 'violation').length;
    const warnings = items.filter((x) => x.recordType === 'warning').length;
    const message = `Đã lưu ${result.inserted + result.updated} record (${violations} violation, ${warnings} warning).`;
    if (runId) await patchUnifiedStep(runId, 'healthCenter', { status: 'completed', message });
    return { ok: true, message };
  } catch (err) {
    const message = mapContentConnectionError(err).message || 'Health Center thất bại.';
    if (runId) await patchUnifiedStep(runId, 'healthCenter', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    healthCenterSyncInFlight = false;
  }
}

async function sendShopScoresFetchToTab(
  tabId: number,
  payload: { sellerId: string },
  timeoutMs: number,
): Promise<FetchShopScoresResponse> {
  return sendTabMessage<FetchShopScoresResponse>(
    tabId,
    { type: 'FETCH_SHOP_SCORES', ...payload },
    timeoutMs,
    'content không trả điểm shop.',
  );
}

function formatScore(value: number | null | undefined): string {
  return value == null ? '—' : String(value);
}

async function runShopScoreSync(opts?: { runId?: string }): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };

  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) {
      await patchUnifiedStep(runId, 'shopScore', { status: 'skipped', message });
      await logSkippedStep(runId, 'shop_score', message);
    }
    return { ok: false, message };
  }

  const tab = await hasSellerUsTab();
  if (!tab?.id) {
    const message = 'Chưa có tab seller-us.tiktok.com.';
    if (runId) {
      await patchUnifiedStep(runId, 'shopScore', { status: 'skipped', message });
      await logSkippedStep(runId, 'shop_score', message, sellerId);
    }
    return { ok: false, message };
  }

  if (shopScoreSyncInFlight) return { ok: false, message: 'Đang Shop Score — đợi xong.' };

  shopScoreSyncInFlight = true;
  if (runId) {
    await patchUnifiedStep(runId, 'shopScore', { status: 'running', message: 'Đang Shop Score…' });
  }

  try {
    const fetched = await sendShopScoresFetchToTab(tab.id, { sellerId }, 60_000);

    if (!fetched?.ok || !fetched.scores) {
      const message = fetched?.message || 'Scrape Shop Score thất bại.';
      if (runId) await patchUnifiedStep(runId, 'shopScore', { status: 'failed', message });
      return { ok: false, message };
    }

    const scores = fetched.scores;
    await syncTikTokShopScores({
      runId,
      externalShopId: sellerId,
      sellerId,
      ahrScore: scores.ahrScore,
      performanceScore: scores.performanceScore,
      productSatisfactionScore: scores.productSatisfactionScore,
      fulfillmentScore: scores.fulfillmentScore,
      customerServiceScore: scores.customerServiceScore,
    });

    const missing = fetched.warnings?.length ? ` · thiếu: ${fetched.warnings.join(' · ')}` : '';
    const harvestNote = formatHarvestNote(fetched.harvestUsed, fetched.harvestTotal);
    const harvested = harvestNote ? ` · ${harvestNote}` : '';
    const message =
      `SPS ${formatScore(scores.performanceScore)} · AHR ${formatScore(scores.ahrScore)}` +
      ` (SP ${formatScore(scores.productSatisfactionScore)}, FL ${formatScore(scores.fulfillmentScore)},` +
      ` CS ${formatScore(scores.customerServiceScore)})${missing}${harvested}`;
    if (runId) await patchUnifiedStep(runId, 'shopScore', { status: 'completed', message });
    return { ok: true, message };
  } catch (err) {
    const message = mapContentConnectionError(err).message || 'Shop Score thất bại.';
    if (runId) await patchUnifiedStep(runId, 'shopScore', { status: 'failed', message });
    return { ok: false, message };
  } finally {
    shopScoreSyncInFlight = false;
  }
}

async function writeDailyTaskPlan(plan: ShopDailyTaskPlan | null): Promise<void> {
  if (!plan) {
    await chrome.storage.local.remove(STORAGE_KEYS.dailyTasks);
    return;
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.dailyTasks]: plan });
}

/**
 * Mỗi lần bật Auto: đánh dấu lại mọi nhiệm vụ có URL là chưa xong (chỉ cache local)
 * để runner mở lại lần lượt thay vì nhảy thẳng Sync All.
 */
async function resetLocalDailyTasksForAutoRun(): Promise<void> {
  const plan = await readDailyTaskPlan();
  if (!plan?.items.length) return;
  await writeDailyTaskPlan({
    ...plan,
    items: plan.items.map((task) =>
      (task.url || '').trim() ? { ...task, done: false } : task,
    ),
  });
}

/**
 * Kế hoạch của ngày cũ không còn đúng (nhiệm vụ reset theo ngày VN) nên bỏ luôn thay vì
 * để side panel khoe "đã xong" của hôm qua.
 */
async function readDailyTaskPlan(): Promise<ShopDailyTaskPlan | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.dailyTasks);
    const plan = (raw[STORAGE_KEYS.dailyTasks] as ShopDailyTaskPlan | undefined) ?? null;
    if (!plan) return null;
    return plan.date === vietnamToday() ? plan : null;
  } catch {
    return null;
  }
}

/** Ngày lịch VN dạng `yyyy-MM-dd` — cùng mốc backend dùng để reset nhiệm vụ. */
function vietnamToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

async function readTaskLoadingTaskId(): Promise<string | null> {
  const [nav, snapshot, plan] = await Promise.all([
    readTaskNavigation(),
    readSellerUsTabSnapshot(),
    readDailyTaskPlan(),
  ]);
  return resolveNavigatingTaskId(plan, nav, snapshot);
}

function findTaskForOpen(
  plan: ShopDailyTaskPlan | null,
  url: string,
  taskId?: string,
): ShopDailyTask | undefined {
  if (taskId) {
    const byId = plan?.items.find((item) => item.id === taskId);
    if (byId && !byId.done) return byId;
  }
  return findBestMatchingTask(plan?.items, url);
}

/**
 * Điều hướng nhiệm vụ ngay trên tab seller-us đang mở (không đẻ tab mới) rồi đưa cửa sổ lên
 * trước để người dùng thấy trang. Việc ghi nhận hoàn thành do observer đảm nhiệm, giống hệt như
 * khi người dùng tự bấm menu TikTok — nên hai cách đi tới trang cho ra cùng một kết quả.
 */
async function openTaskUrl(
  url: string,
  taskId?: string,
): Promise<{ ok: boolean; message: string }> {
  const target = (url || '').trim();
  if (!target) return { ok: false, message: 'Nhiệm vụ thiếu URL.' };

  const plan = await readDailyTaskPlan();
  const task = findTaskForOpen(plan, target, taskId);

  try {
    const existing = await hasSellerUsTab();
    if (existing?.id != null) {
      const currentUrl = (existing.url || '').trim();
      if (task) {
        const result = await startTaskNavigation({
          task,
          tabId: existing.id,
          fromUrl: currentUrl,
          currentUrl,
          tabComplete: existing.status === 'complete',
        });
        const alreadyThere = matchesTaskUrl(currentUrl, task.url, task.urlMatchPattern);
        if (existing.windowId != null) {
          await chrome.windows.update(existing.windowId, { focused: true }).catch(() => undefined);
        }
        await chrome.tabs.update(existing.id, { active: true });
        if (alreadyThere) {
          return {
            ok: true,
            message: result.handedOff
              ? 'Đã ở trang nhiệm vụ.'
              : 'Đang tải trang nhiệm vụ…',
          };
        }
      }

      await chrome.tabs.update(existing.id, { url: target, active: true });
      if (existing.windowId != null) {
        await chrome.windows.update(existing.windowId, { focused: true }).catch(() => undefined);
      }
      return { ok: true, message: 'Đang mở trang nhiệm vụ…' };
    }

    const created = await chrome.tabs.create({ url: target, active: true });
    if (task && created.id != null) {
      const fresh = await chrome.tabs.get(created.id).catch(() => created);
      await startTaskNavigation({
        task,
        tabId: created.id,
        fromUrl: '',
        currentUrl: (fresh.url || target).trim(),
        tabComplete: fresh.status === 'complete',
      });
    }
    return { ok: true, message: 'Đã mở tab mới cho nhiệm vụ.' };
  } catch (err) {
    await writeTaskNavigation(null);
    return {
      ok: false,
      message: err instanceof Error ? err.message : 'Không mở được URL nhiệm vụ.',
    };
  }
}

/**
 * Tải danh sách nhiệm vụ truy cập URL của hôm nay về cache.
 *
 * Chỉ đọc — không mở tab, không tự làm thay người dùng. Nhiệm vụ được tính hoàn thành khi người
 * dùng thực sự tới trang (tự bấm menu TikTok hoặc bấm nhiệm vụ trong panel), do
 * {@link handleTaskPageReady} ghi nhận (sau khi giữ đủ thời gian xem nếu có).
 */
async function refreshDailyTaskPlan(opts?: { runId?: string }): Promise<{
  ok: boolean;
  message: string;
  plan?: ShopDailyTaskPlan;
}> {
  const runId = opts?.runId;
  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();

  if (!sellerId) {
    const message = 'Chưa có sellerId — chạy Info trước hoặc Refresh shop.';
    if (runId) await patchUnifiedStep(runId, 'tasks', { status: 'skipped', message });
    return { ok: false, message };
  }

  if (runId) {
    await patchUnifiedStep(runId, 'tasks', { status: 'running', message: 'Đang tải nhiệm vụ…' });
  }

  let fetched: Awaited<ReturnType<typeof getShopDailyTasks>>;
  try {
    fetched = await getShopDailyTasks(sellerId);
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Không lấy được nhiệm vụ.';
    if (runId) await patchUnifiedStep(runId, 'tasks', { status: 'failed', message });
    return { ok: false, message };
  }

  const items = fetched.items ?? [];
  const plan: ShopDailyTaskPlan = {
    date: fetched.date,
    sellerId,
    items,
    fetchedAt: Date.now(),
  };
  await writeDailyTaskPlan(plan);

  const remaining = items.filter((task) => !task.done).length;
  const message = items.length === 0
    ? 'Không có nhiệm vụ nào.'
    : remaining === 0
      ? `Đã xong ${items.length}/${items.length} nhiệm vụ hôm nay.`
      : `Còn ${remaining}/${items.length} nhiệm vụ cần truy cập.`;

  if (runId) await patchUnifiedStep(runId, 'tasks', { status: 'completed', message });
  return { ok: true, message, plan };
}

/**
 * Chống báo trùng khi Chrome bắn nhiều sự kiện cho cùng một lần điều hướng (loading → complete,
 * hoặc SPA đổi query liên tục). Chỉ chặn trong lúc request đang bay; kết quả cuối vẫn nằm ở cờ
 * `done` của cache và unique index phía backend.
 */
const taskReportInFlight = new Set<string>();

/**
 * Ghi nhận hoàn thành một hoặc nhiều nhiệm vụ (sau khi đã ở đủ thời gian xem, nếu có).
 */
async function completeTaskVisits(taskIds: string[], url: string): Promise<void> {
  if (taskIds.length === 0) return;

  const plan = await readDailyTaskPlan();
  if (!plan) return;

  const shop = await readShopInfo();
  const sellerId = (shop?.sellerId || '').trim();
  if (!sellerId || (plan.sellerId && plan.sellerId !== sellerId)) return;

  const completed: string[] = [];
  for (const taskId of taskIds) {
    const task = plan.items.find((item) => item.id === taskId);
    if (!task || task.done) continue;
    if (!matchesTaskUrl(url, task.url, task.urlMatchPattern)) continue;

    const guard = `${sellerId}:${taskId}`;
    if (taskReportInFlight.has(guard)) continue;
    taskReportInFlight.add(guard);
    try {
      await reportShopTaskVisit({
        definitionId: taskId,
        externalShopId: sellerId,
        visitedUrl: url,
        status: 'done',
      });
      completed.push(taskId);
    } catch (err) {
      console.warn('[kb-tts] report shop task failed:', (err as Error).message || err);
    } finally {
      taskReportInFlight.delete(guard);
    }
  }

  if (completed.length === 0) return;

  const latest = (await readDailyTaskPlan()) ?? plan;
  await writeDailyTaskPlan({
    ...latest,
    items: latest.items.map((task) =>
      completed.includes(task.id) ? { ...task, done: true } : task,
    ),
  });

  const nav = await readTaskNavigation();
  if (nav && completed.includes(nav.taskId)) {
    await writeTaskNavigation(null);
  }

  await Promise.all(completed.map((taskId) => clearTaskDwell(taskId)));
  scheduleAutoDailyTasksTick();
}

/**
 * Trang seller-us vừa load xong (hoặc SPA đổi URL): bắt đầu đếm thời gian xem hoặc báo ngay nếu duration = 0.
 */
async function handleTaskPageReady(url: string): Promise<void> {
  const plan = await readDailyTaskPlan();
  if (!plan) return;

  await clearStaleTaskDwells(
    url,
    (task, openUrl) => matchesTaskUrl(openUrl, task.url, task.urlMatchPattern),
    plan.items,
  );

  const best = findBestMatchingTask(plan.items, url);
  if (!best) return;

  const duration = Math.max(0, Math.floor(best.viewDurationSeconds ?? 0));
  if (duration <= 0) {
    await completeTaskVisits([best.id], url);
    return;
  }

  const dwellMap = await readTaskDwellMap();
  if (!dwellMap[best.id]) {
    await startTaskDwell(best, url);
  }
}

/** Alarm hết hạn — xác nhận vẫn ở đúng trang rồi ghi nhận nhiệm vụ. */
async function handleTaskDwellDue(taskId: string): Promise<void> {
  const plan = await readDailyTaskPlan();
  if (!plan) {
    await clearTaskDwell(taskId);
    return;
  }

  const task = plan.items.find((item) => item.id === taskId);
  if (!task || task.done) {
    await clearTaskDwell(taskId);
    return;
  }

  const dwellMap = await readTaskDwellMap();
  const dwell = dwellMap[taskId];
  if (!dwell) return;
  if (dwell.endsAt > Date.now() + 250) return;

  const snapshot = await readSellerUsTabSnapshot();
  const tab = await hasSellerUsTab();
  const currentUrl = (
    snapshot && (tab?.id == null || snapshot.tabId === tab.id)
      ? snapshot.url
      : tab?.url || snapshot?.url || ''
  ).trim();
  if (!currentUrl || !matchesTaskUrl(currentUrl, task.url, task.urlMatchPattern)) {
    await clearTaskDwell(taskId);
    return;
  }

  await clearTaskDwell(taskId);
  await completeTaskVisits([taskId], currentUrl);
}

installTaskPageObserver({
  readPlan: readDailyTaskPlan,
  onPageReady: handleTaskPageReady,
  onTabClosed: () => clearAllTaskDwells(),
});

function summarizeUnifiedSyncSteps(
  state: Awaited<ReturnType<typeof readUnifiedSyncState>>,
  mode: 'all' | 'fast',
): { ok: boolean; message: string } {
  const pending = { status: 'pending' as const };
  const fastSteps = [
    state?.payout ?? pending,
    state?.healthCenter ?? pending,
    state?.shopScore ?? pending,
  ];
  const allSteps = state
    ? [
        state.profile,
        state.payout,
        state.opportunity,
        state.analytics,
        state.healthCenter ?? pending,
        state.shopScore ?? pending,
        state.tasks,
      ]
    : [];
  const steps = mode === 'fast' ? fastSteps : allSteps;
  const failed = steps.filter((x) => x.status === 'failed').length;
  const skipped = steps.filter((x) => x.status === 'skipped').length;
  const completed = steps.filter((x) => x.status === 'completed').length;
  const ok = failed === 0;
  const prefix = mode === 'fast' ? 'Sync fast xong' : 'Sync xong';
  const message = ok
    ? `${prefix}: ${completed} thành công${skipped ? `, ${skipped} bỏ qua` : ''}.`
    : `${prefix}: ${completed} thành công, ${failed} lỗi${skipped ? `, ${skipped} bỏ qua` : ''}.`;
  return { ok, message };
}

async function runUnifiedSync(): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };
  if (unifiedSyncInFlight) return { ok: false, message: 'Đang Sync — đợi xong.' };

  const dailyPlan = await readDailyTaskPlan();
  if (!getDailyTaskProgress(dailyPlan).syncUnlocked) {
    return { ok: false, message: formatDailyTaskSyncBlockMessage(dailyPlan) };
  }

  unifiedSyncInFlight = true;
  const runId = crypto.randomUUID();
  await writeUnifiedSyncState(createUnifiedSyncState(runId));

  try {
    const steps = await getTikTokUnifiedSyncConfig();
    const skipMsg = 'Tắt trong Settings';

    if (steps.info) await runShopSync({ runId });
    else {
      await patchUnifiedStep(runId, 'profile', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'profile', skipMsg);
    }

    if (steps.finance) await runPayoutSync({ runId });
    else {
      await patchUnifiedStep(runId, 'payout', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'payout', skipMsg);
    }

    if (steps.keywords) await runOpportunitySync({ runId });
    else {
      await patchUnifiedStep(runId, 'opportunity', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'opportunity', skipMsg);
    }

    if (steps.analytics) await runAnalyticsSync({ runId });
    else {
      await patchUnifiedStep(runId, 'analytics', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'analytics', skipMsg);
    }

    if (steps.healthCenter) await runHealthCenterSync({ runId });
    else {
      await patchUnifiedStep(runId, 'healthCenter', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'health_center', skipMsg);
    }

    if (steps.shopScore) await runShopScoreSync({ runId });
    else {
      await patchUnifiedStep(runId, 'shopScore', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'shop_score', skipMsg);
    }

    await refreshDailyTaskPlan({ runId });
    await finishUnifiedSync(runId);

    const state = await readUnifiedSyncState();
    return summarizeUnifiedSyncSteps(state, 'all');
  } finally {
    unifiedSyncInFlight = false;
  }
}

async function runFastSync(): Promise<{ ok: boolean; message: string }> {
  const auth = await getAuth();
  if (!auth) return { ok: false, message: 'Chưa đăng nhập kinhbac.' };
  if (unifiedSyncInFlight) return { ok: false, message: 'Đang Sync — đợi xong.' };

  unifiedSyncInFlight = true;
  const runId = crypto.randomUUID();
  await writeUnifiedSyncState(createFastSyncState(runId));

  try {
    const steps = await getTikTokUnifiedSyncConfig();
    const skipMsg = 'Tắt trong Settings';

    if (steps.finance) await runPayoutSync({ runId });
    else {
      await patchUnifiedStep(runId, 'payout', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'payout', skipMsg);
    }

    if (steps.healthCenter) await runHealthCenterSync({ runId });
    else {
      await patchUnifiedStep(runId, 'healthCenter', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'health_center', skipMsg);
    }

    if (steps.shopScore) await runShopScoreSync({ runId });
    else {
      await patchUnifiedStep(runId, 'shopScore', { status: 'skipped', message: skipMsg });
      await logSkippedStep(runId, 'shop_score', skipMsg);
    }

    await finishUnifiedSync(runId);

    const state = await readUnifiedSyncState();
    return summarizeUnifiedSyncSteps(state, 'fast');
  } finally {
    unifiedSyncInFlight = false;
  }
}

installAutoDailyTaskRunner({
  isUnifiedSyncInFlight: () => unifiedSyncInFlight,
  readDailyTaskPlan,
  readTaskNavigation,
  readTaskDwellMap,
  openTaskUrl,
  runUnifiedSync,
  resetLocalDailyTasksForAutoRun,
  refreshDailyTaskPlanFromApi: async () => {
    await refreshDailyTaskPlan();
  },
});

/** MV3 wake / plan đổi khi Auto đang bật → tiếp tục chuỗi. */
function installAutoDailyTasksResume(): void {
  const resume = () => {
    void readAutoDailyTasksEnabled().then((on) => {
      if (on) scheduleAutoDailyTasksTick();
    });
  };
  chrome.runtime.onStartup.addListener(resume);
  chrome.runtime.onInstalled.addListener(resume);
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local') return;
    if (
      changes[STORAGE_KEYS.dailyTasks] ||
      changes[STORAGE_KEYS.taskDwells] ||
      changes[STORAGE_KEYS.taskNavigation]
    ) {
      resume();
    }
  });
  resume();
}

installAutoDailyTasksResume();

chrome.runtime.onMessage.addListener((message: ExtMessage, sender, sendResponse) => {
  (async () => {
    try {
      switch (message.type) {
        case 'GET_STATE':
          sendResponse(await buildState());
          return;
        case 'LOGIN': {
          await login(message.username, message.password);
          const tab = await hasSellerUsTab();
          if (tab) void refreshShopFromTab();
          sendResponse({ ok: true, ...(await buildState()) });
          return;
        }
        case 'LOGOUT': {
          await logout();
          await chrome.storage.local.remove([
            STORAGE_KEYS.auth,
            STORAGE_KEYS.capture,
            STORAGE_KEYS.shopInfo,
            STORAGE_KEYS.unifiedSync,
            STORAGE_KEYS.dailyTasks,
            STORAGE_KEYS.taskNavigation,
            STORAGE_KEYS.sellerUsTab,
            STORAGE_KEYS.taskDwells,
            STORAGE_KEYS.autoDailyTasks,
          ]);
          await clearAllTaskDwells();
          sendResponse({ ok: true, ...(await buildState()) });
          return;
        }
        case 'SET_AUTO_DAILY_TASKS': {
          if (message.enabled) {
            await beginAutoDailyTasksRun();
          } else {
            await stopAutoDailyTasksRun();
          }
          sendResponse({ ok: true, ...(await buildState()) });
          return;
        }
        case 'SET_THEME': {
          const theme = await persistTheme(message.theme);
          sendResponse({ ok: true, theme });
          return;
        }
        case 'REFRESH_SHOP': {
          const result = await refreshShopFromTab();
          sendResponse({
            ok: result.ok,
            message: result.message,
            ...(await buildState()),
          });
          return;
        }
        case 'SYNC_ALL': {
          const result = await runUnifiedSync();
          sendResponse({
            ok: result.ok,
            message: result.message,
            ...(await buildState()),
          });
          return;
        }
        case 'SYNC_FAST': {
          const result = await runFastSync();
          sendResponse({
            ok: result.ok,
            message: result.message,
            ...(await buildState()),
          });
          return;
        }
        case 'REFRESH_TASKS': {
          const result = await refreshDailyTaskPlan();
          sendResponse({
            ok: result.ok,
            message: result.message,
            ...(await buildState()),
          });
          return;
        }
        case 'OPEN_TASK_URL': {
          const result = await openTaskUrl(message.url, message.taskId);
          sendResponse({
            ok: result.ok,
            message: result.message,
            ...(await buildState()),
          });
          return;
        }
        case 'OPEN_SELLER_US': {
          await openSellerUsTab();
          // Đợi content load rồi lấy shop.
          void (async () => {
            await waitMs(2000);
            await refreshShopFromTab();
          })();
          sendResponse({ ok: true, ...(await buildState()) });
          return;
        }
        case 'SELLER_COMMON_GET_RESULT': {
          const saved = await persistFetchResult(message);
          sendResponse({ ok: saved.ok, message: saved.message });
          return;
        }
        case 'SELLER_PRELOAD_CAPTURED': {
          const saved = await persistCapturedPreload(message.raw, message.sellerId);
          sendResponse({ ok: saved });
          return;
        }
        case 'SELLER_PAGE_READY': {
          handleContentPageReady(sender.tab);
          sendResponse({ ok: true });
          return;
        }
        case 'SELLER_PAGE_NETWORK': {
          handleContentNetwork(sender.tab?.id, Number(message.inflight) || 0);
          sendResponse({ ok: true });
          return;
        }
        case 'PING_CONTENT':
        case 'PING_PRODUCT_BRIDGE':
        case 'REQUEST_NETWORK_SYNC':
        case 'FETCH_SELLER_COMMON_GET':
        case 'FETCH_PAYOUT_PAYMENTS':
        case 'FETCH_OPPORTUNITY_LEADS':
          // Chỉ content xử lý — background ignore.
          sendResponse({ ok: false, message: 'Chỉ chạy trên content seller-us.' });
          return;
        default:
          sendResponse({ ok: false, message: 'Unknown message.' });
      }
    } catch (err) {
      sendResponse({
        ok: false,
        message: mapContentConnectionError(err).message || 'Lỗi không xác định.',
      });
    }
  })();
  return true;
});
