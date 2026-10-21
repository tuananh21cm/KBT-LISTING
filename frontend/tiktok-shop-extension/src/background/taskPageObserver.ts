import { SELLER_US_HOST, STORAGE_KEYS } from '../shared/config';
import {
  TASK_NAVIGATION_TIMEOUT_MS,
  TASK_PAGE_NETWORK_IDLE_MS,
  TASK_PAGE_READY_FALLBACK_MS,
  buildTaskPageContext,
  reduceTaskPageEvent,
  type TaskPageEvent,
  type TaskPageState,
} from '../shared/taskPageState';
import type {
  SellerUsTabSnapshot,
  ShopDailyTask,
  ShopDailyTaskPlan,
  TaskNavigationState,
} from '../shared/types';

export interface TaskPageObserverDeps {
  readPlan: () => Promise<ShopDailyTaskPlan | null>;
  onPageReady: (url: string) => Promise<void>;
  onTabClosed: () => Promise<void>;
}

let deps: TaskPageObserverDeps | null = null;
let queue: Promise<void> = Promise.resolve();
/** Hủy setTimeout settle cũ khi có event mới — alarm < 1 phút không đáng tin trên MV3. */
let settleToken = 0;
/** fetch/XHR đang chạy theo tab (MAIN world hook). */
const inflightByTab = new Map<number, number>();
let networkIdleToken = 0;

function enqueue<T>(fn: () => Promise<T>): Promise<T> {
  const run = queue.then(fn, fn);
  queue = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

function isSellerUsUrl(url: string): boolean {
  try {
    return new URL(url).host === SELLER_US_HOST;
  } catch {
    return false;
  }
}

export async function readTaskNavigation(): Promise<TaskNavigationState | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.taskNavigation);
    const nav = (raw[STORAGE_KEYS.taskNavigation] as TaskNavigationState | undefined) ?? null;
    if (!nav) return null;
    if (Date.now() - nav.startedAt > TASK_NAVIGATION_TIMEOUT_MS) {
      await writeTaskNavigation(null);
      return null;
    }
    return nav;
  } catch {
    return null;
  }
}

export async function writeTaskNavigation(nav: TaskNavigationState | null): Promise<void> {
  if (nav) {
    await chrome.storage.local.set({ [STORAGE_KEYS.taskNavigation]: nav });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.taskNavigation);
  }
}

export async function readSellerUsTabSnapshot(): Promise<SellerUsTabSnapshot | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.sellerUsTab);
    return (raw[STORAGE_KEYS.sellerUsTab] as SellerUsTabSnapshot | undefined) ?? null;
  } catch {
    return null;
  }
}

export async function writeSellerUsTabSnapshot(
  snapshot: SellerUsTabSnapshot | null,
): Promise<void> {
  if (snapshot) {
    await chrome.storage.local.set({ [STORAGE_KEYS.sellerUsTab]: snapshot });
  } else {
    await chrome.storage.local.remove(STORAGE_KEYS.sellerUsTab);
  }
}

async function loadState(): Promise<TaskPageState> {
  const [nav, snapshot] = await Promise.all([readTaskNavigation(), readSellerUsTabSnapshot()]);
  return { nav, snapshot };
}

function sameJson(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

async function persist(prev: TaskPageState, next: TaskPageState): Promise<void> {
  if (!sameJson(prev.nav, next.nav)) await writeTaskNavigation(next.nav);
  if (!sameJson(prev.snapshot, next.snapshot)) await writeSellerUsTabSnapshot(next.snapshot);
}

function inflightCount(tabId: number): number {
  return inflightByTab.get(tabId) ?? 0;
}

function cancelPageSettle(): void {
  settleToken += 1;
}

function schedulePageSettle(tabId: number, waitMs: number): void {
  const token = ++settleToken;
  globalThis.setTimeout(() => {
    if (token !== settleToken) return;
    void enqueue(async () => {
      if (token !== settleToken) return;
      const state = await loadState();
      const tracked = state.nav?.tabId ?? tabId;
      let url = state.snapshot?.url || '';
      try {
        const tab = await chrome.tabs.get(tracked);
        url = (tab.url || url).trim();
      } catch {
        // tab đã đóng
      }
      if (!url || !state.nav) return;

      const waited = Date.now() - state.nav.startedAt;
      if (inflightCount(tracked) > 0 && waited < TASK_PAGE_READY_FALLBACK_MS) {
        schedulePageSettle(tracked, TASK_PAGE_NETWORK_IDLE_MS);
        return;
      }

      await applyEvent({ type: 'settle', tabId: tracked, url });
    });
  }, waitMs);
}

function scheduleNetworkIdle(tabId: number): void {
  const token = ++networkIdleToken;
  globalThis.setTimeout(() => {
    if (token !== networkIdleToken) return;
    if (inflightCount(tabId) > 0) return;
    void enqueue(async () => {
      if (token !== networkIdleToken) return;
      if (inflightCount(tabId) > 0) return;
      let url = '';
      try {
        const tab = await chrome.tabs.get(tabId);
        url = (tab.url || '').trim();
      } catch {
        return;
      }
      if (!url) return;
      await applyEvent({ type: 'network-idle', tabId, url });
    });
  }, TASK_PAGE_NETWORK_IDLE_MS);
}

/** Content MAIN world báo số request đang bay — không tin document `complete`. */
export function handleContentNetwork(tabId: number | undefined, inflight: number): void {
  if (tabId == null || tabId < 0) return;
  const next = Math.max(0, Math.floor(inflight));
  const prev = inflightCount(tabId);
  inflightByTab.set(tabId, next);
  if (prev <= 0 && next > 0) {
    dispatch({ type: 'network-busy', tabId });
  } else if (next === 0 && prev > 0) {
    scheduleNetworkIdle(tabId);
  }
}

async function applyEvent(event: TaskPageEvent): Promise<{ handedOff: boolean }> {
  const installed = deps;
  if (!installed) return { handedOff: false };

  const prev = await loadState();
  const plan = await installed.readPlan();
  const ctx = buildTaskPageContext(plan, prev.nav);
  const result = reduceTaskPageEvent(prev, event, ctx);
  await persist(prev, result.state);

  if (!result.state.nav) {
    cancelPageSettle();
  } else if (result.effect.type === 'schedule-settle') {
    schedulePageSettle(result.effect.tabId, result.effect.waitMs);
  }

  if (result.effect.type === 'page-ready' && result.effect.url && isSellerUsUrl(result.effect.url)) {
    await installed.onPageReady(result.effect.url);
    return { handedOff: true };
  }
  if (result.effect.type === 'tab-closed') {
    await installed.onTabClosed();
  }
  return { handedOff: false };
}

function dispatch(event: TaskPageEvent): void {
  void enqueue(() => applyEvent(event));
}

/**
 * Bấm nhiệm vụ trên panel: ghi nav theo tabId. Đã đúng URL vẫn giữ spinner đến khi mạng yên.
 */
export async function startTaskNavigation(input: {
  task: ShopDailyTask;
  tabId: number;
  fromUrl: string;
  currentUrl: string;
  tabComplete: boolean;
}): Promise<{ handedOff: boolean }> {
  return enqueue(async () => {
    const started = await applyEvent({
      type: 'nav-start',
      taskId: input.task.id,
      targetUrl: input.task.url,
      tabId: input.tabId,
      fromUrl: input.fromUrl,
      startedAt: Date.now(),
      currentUrl: input.currentUrl,
      tabComplete: input.tabComplete,
      urlMatchPattern: input.task.urlMatchPattern,
    });
    if (!started.handedOff && inflightCount(input.tabId) > 0) {
      await applyEvent({ type: 'network-busy', tabId: input.tabId });
    }
    try {
      await chrome.tabs.sendMessage(input.tabId, { type: 'REQUEST_NETWORK_SYNC' });
    } catch {
      // content chưa inject
    }
    return started;
  });
}

/** Content script: chỉ tin tab gửi message, không tin URL trong payload (có thể là document cũ). */
export function handleContentPageReady(senderTab?: chrome.tabs.Tab | null): void {
  const tabId = senderTab?.id;
  const url = (senderTab?.url || '').trim();
  if (tabId == null || !url || !isSellerUsUrl(url)) return;
  dispatch({ type: 'page-ready', tabId, url });
}

async function seedSnapshot(): Promise<void> {
  const prev = await loadState();
  if (prev.snapshot) return;
  try {
    const tabs = await chrome.tabs.query({ url: `*://${SELLER_US_HOST}/*` });
    const tab = tabs.find((item) => item.active && item.id != null) ?? tabs.find((item) => item.id != null);
    if (tab?.id == null || !tab.url) return;
    await writeSellerUsTabSnapshot({
      tabId: tab.id,
      url: tab.url,
      status: tab.status === 'loading' ? 'loading' : 'complete',
      updatedAt: Date.now(),
    });
  } catch {
    // query tabs có thể fail khi SW mới khởi động
  }
}

const SELLER_US_NAV_FILTER = [{ hostEquals: SELLER_US_HOST }];

export function installTaskPageObserver(next: TaskPageObserverDeps): void {
  deps = next;

  chrome.webNavigation.onBeforeNavigate.addListener((details) => {
    if (details.frameId !== 0 || !isSellerUsUrl(details.url)) return;
    dispatch({ type: 'before-navigate', tabId: details.tabId, url: details.url });
  }, { url: SELLER_US_NAV_FILTER });

  chrome.webNavigation.onCommitted.addListener((details) => {
    if (details.frameId !== 0 || !isSellerUsUrl(details.url)) return;
    dispatch({ type: 'loading', tabId: details.tabId, url: details.url });
  }, { url: SELLER_US_NAV_FILTER });

  chrome.webNavigation.onCompleted.addListener((details) => {
    if (details.frameId !== 0 || !isSellerUsUrl(details.url)) return;
    dispatch({ type: 'complete', tabId: details.tabId, url: details.url });
  }, { url: SELLER_US_NAV_FILTER });

  chrome.webNavigation.onHistoryStateUpdated.addListener((details) => {
    if (details.frameId !== 0 || !isSellerUsUrl(details.url)) return;
    dispatch({ type: 'spa', tabId: details.tabId, url: details.url });
  }, { url: SELLER_US_NAV_FILTER });

  chrome.webNavigation.onErrorOccurred.addListener((details) => {
    if (details.frameId !== 0 || !isSellerUsUrl(details.url)) return;
    dispatch({ type: 'nav-error', tabId: details.tabId, url: details.url });
  }, { url: SELLER_US_NAV_FILTER });

  chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    const url = changeInfo.url || tab.url || '';
    if (!url || !isSellerUsUrl(url)) return;

    if (changeInfo.status === 'loading') {
      dispatch({ type: 'loading', tabId, url });
    }
    if (changeInfo.url) {
      dispatch({
        type: 'url',
        tabId,
        url: changeInfo.url,
        loading: changeInfo.status === 'loading' || tab.status === 'loading',
      });
    }
    if (changeInfo.status === 'complete') {
      dispatch({ type: 'complete', tabId, url });
    }
  });

  chrome.tabs.onRemoved.addListener((tabId) => {
    inflightByTab.delete(tabId);
    dispatch({ type: 'removed', tabId });
  });

  void enqueue(() => seedSnapshot());
}
