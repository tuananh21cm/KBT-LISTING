import { findBestMatchingTask, matchesTaskUrl, normalizeTaskUrl } from './taskUrlMatch';
import type {
  SellerUsTabSnapshot,
  ShopDailyTaskPlan,
  TaskNavigationState,
} from './types';

/** Spinner điều hướng quá hạn — tránh kẹt vĩnh viễn khi Chrome không bắn complete. */
export const TASK_NAVIGATION_TIMEOUT_MS = 120_000;
/**
 * TikTok Seller là SPA: document `complete` / `pushState` tới sớm, UI còn gọi API.
 * - Đã đúng URL, chưa thấy XHR: chờ ngắn rồi bàn giao (trang thật sự đã idle).
 * - Chưa thấy XHR sau khi đổi URL: chờ grace vì burst API thường tới trễ.
 * - Đã thấy XHR: chỉ bàn giao khi mạng yên (observer), fallback để không kẹt.
 */
export const TASK_PAGE_ALREADY_THERE_MS = 800;
export const TASK_PAGE_NAV_GRACE_MS = 5_000;
export const TASK_PAGE_NETWORK_IDLE_MS = 1_000;
export const TASK_PAGE_READY_FALLBACK_MS = 20_000;

export interface TaskPageState {
  snapshot: SellerUsTabSnapshot | null;
  nav: TaskNavigationState | null;
}

export type TaskPageEvent =
  | {
      type: 'nav-start';
      taskId: string;
      targetUrl: string;
      tabId: number;
      fromUrl: string;
      startedAt: number;
      currentUrl: string;
      tabComplete: boolean;
      urlMatchPattern?: string;
    }
  | { type: 'before-navigate'; tabId: number; url: string }
  | { type: 'loading'; tabId: number; url: string }
  | { type: 'url'; tabId: number; url: string; loading: boolean }
  | { type: 'complete'; tabId: number; url: string }
  | { type: 'spa'; tabId: number; url: string }
  | { type: 'page-ready'; tabId: number; url: string }
  | { type: 'network-busy'; tabId: number }
  | { type: 'network-idle'; tabId: number; url: string }
  | { type: 'settle'; tabId: number; url: string }
  | { type: 'nav-error'; tabId: number; url: string }
  | { type: 'removed'; tabId: number };

export type TaskPageEffect =
  | { type: 'noop' }
  | { type: 'page-ready'; url: string }
  | { type: 'schedule-settle'; tabId: number; waitMs: number }
  | { type: 'tab-closed' };

export interface TaskPageReduceContext {
  now: number;
  /** URL có khớp nhiệm vụ đang spinner không. */
  navMatches: (url: string) => boolean;
  /** Nhiệm vụ pending khớp URL (menu TikTok, chưa có nav). */
  findPendingTask: (url: string) => { taskId: string; targetUrl: string } | null;
}

export interface ReduceResult {
  state: TaskPageState;
  effect: TaskPageEffect;
}

export function sameTaskPath(a: string, b: string): boolean {
  const left = normalizeTaskUrl(a);
  const right = normalizeTaskUrl(b);
  return !!left && !!right && left.toLowerCase() === right.toLowerCase();
}

export function isTrackedTab(state: TaskPageState, tabId: number): boolean {
  const tracked = state.nav?.tabId || state.snapshot?.tabId;
  if (!tracked) return true;
  return tracked === tabId;
}

export function buildTaskPageContext(
  plan: ShopDailyTaskPlan | null,
  nav: TaskNavigationState | null,
  now = Date.now(),
): TaskPageReduceContext {
  const navTask = nav ? plan?.items.find((item) => item.id === nav.taskId) : undefined;
  return {
    now,
    navMatches: (url) => {
      if (!nav) return false;
      return matchesTaskUrl(url, navTask?.url ?? nav.targetUrl, navTask?.urlMatchPattern);
    },
    findPendingTask: (url) => {
      const task = findBestMatchingTask(plan?.items, url);
      return task ? { taskId: task.id, targetUrl: task.url } : null;
    },
  };
}

/** Task đang hiện spinner: ưu tiên nav (task vừa bấm), không lan sang nhiệm vụ prefix ngắn hơn. */
export function resolveNavigatingTaskId(
  plan: ShopDailyTaskPlan | null | undefined,
  nav: TaskNavigationState | null,
  snapshot: SellerUsTabSnapshot | null,
): string | null {
  if (nav?.taskId) return nav.taskId;
  if (snapshot?.status === 'loading') {
    return findBestMatchingTask(plan?.items, snapshot.url)?.id ?? null;
  }
  return null;
}

export function isTaskNavigating(
  taskId: string,
  navigatingTaskId: string | null,
  isWatching: boolean,
): boolean {
  if (isWatching || !navigatingTaskId) return false;
  return navigatingTaskId === taskId;
}

function snap(
  tabId: number,
  url: string,
  status: SellerUsTabSnapshot['status'],
  now: number,
): SellerUsTabSnapshot {
  return { tabId, url, status, updatedAt: now };
}

function noop(state: TaskPageState): ReduceResult {
  return { state, effect: { type: 'noop' } };
}

function isStaleFromUrl(url: string, nav: TaskNavigationState, ctx: TaskPageReduceContext): boolean {
  const from = nav.fromUrl || '';
  if (!from) return false;
  return sameTaskPath(url, from) && !ctx.navMatches(url);
}

function loadingSnapshotUrl(
  state: TaskPageState,
  url: string,
  nav: TaskNavigationState | null,
  ctx: TaskPageReduceContext,
): string {
  if (!nav) return url;
  if (ctx.navMatches(url)) return url;
  const current = state.snapshot?.url || '';
  if (current && ctx.navMatches(current)) return current;
  return nav.targetUrl;
}

function beginNavFromPending(
  state: TaskPageState,
  tabId: number,
  url: string,
  status: SellerUsTabSnapshot['status'],
  ctx: TaskPageReduceContext,
): TaskPageState {
  const pending = ctx.findPendingTask(url);
  const snapshot = snap(tabId, url, status, ctx.now);
  if (state.nav || !pending) {
    return { snapshot, nav: state.nav };
  }
  return {
    snapshot,
    nav: {
      taskId: pending.taskId,
      targetUrl: pending.targetUrl,
      tabId,
      fromUrl: state.snapshot?.url || '',
      startedAt: ctx.now,
    },
  };
}

function markDestSeen(
  nav: TaskNavigationState,
  url: string,
  ctx: TaskPageReduceContext,
): TaskNavigationState {
  if (!ctx.navMatches(url)) return nav;
  return { ...nav, destSeenAt: nav.destSeenAt ?? ctx.now };
}

function settleWaitMs(nav: TaskNavigationState): number {
  if (nav.sawNetwork) return TASK_PAGE_READY_FALLBACK_MS;
  if (nav.alreadyThere) return TASK_PAGE_ALREADY_THERE_MS;
  return TASK_PAGE_NAV_GRACE_MS;
}

function keepNavLoading(
  state: TaskPageState,
  tabId: number,
  url: string,
  ctx: TaskPageReduceContext,
  destConfirmed: boolean,
): ReduceResult {
  const nav = state.nav;
  if (!nav) return noop(state);
  const destUrl = loadingSnapshotUrl(state, url, nav, ctx);
  const nextNav = destConfirmed ? markDestSeen(nav, url, ctx) : nav;
  const settled = destConfirmed && ctx.navMatches(url);
  return {
    state: { snapshot: snap(tabId, destUrl, 'loading', ctx.now), nav: nextNav },
    effect: settled
      ? { type: 'schedule-settle', tabId, waitMs: settleWaitMs(nextNav) }
      : { type: 'noop' },
  };
}

function handoffReady(
  tabId: number,
  url: string,
  ctx: TaskPageReduceContext,
): ReduceResult {
  return {
    state: { snapshot: snap(tabId, url, 'complete', ctx.now), nav: null },
    effect: { type: 'page-ready', url },
  };
}

function considerReady(
  state: TaskPageState,
  tabId: number,
  url: string,
  ctx: TaskPageReduceContext,
): ReduceResult {
  if (!isTrackedTab(state, tabId)) return noop(state);

  const nav = state.nav;
  if (nav && isStaleFromUrl(url, nav, ctx)) {
    return noop(state);
  }

  const matchesNav = nav ? ctx.navMatches(url) : false;
  const pending = ctx.findPendingTask(url);

  if (nav && !matchesNav) {
    // URL trung gian / document cũ: giữ spinner đến khi đích ready (timeout mới bỏ).
    return noop(state);
  }

  if (matchesNav || pending) {
    return handoffReady(tabId, url, ctx);
  }

  // Complete lệch snapshot khi đã complete ở URL khác = document cũ, không phải điều hướng mới.
  if (
    state.snapshot &&
    state.snapshot.status === 'complete' &&
    !sameTaskPath(url, state.snapshot.url)
  ) {
    return noop(state);
  }

  return {
    state: { snapshot: snap(tabId, url, 'complete', ctx.now), nav: null },
    effect: { type: 'page-ready', url },
  };
}

/**
 * Quyết định spinner / snapshot / bàn giao dwell từ một sự kiện tab — thuần, không Chrome API.
 */
export function reduceTaskPageEvent(
  state: TaskPageState,
  event: TaskPageEvent,
  ctx: TaskPageReduceContext,
): ReduceResult {
  const now = ctx.now;
  const nav =
    state.nav && now - state.nav.startedAt > TASK_NAVIGATION_TIMEOUT_MS ? null : state.nav;
  const current: TaskPageState = { snapshot: state.snapshot, nav };

  if (event.type === 'nav-start') {
    const url = event.currentUrl || event.fromUrl;
    const matchesNow = matchesTaskUrl(url, event.targetUrl, event.urlMatchPattern);
    const nextNav: TaskNavigationState = {
      taskId: event.taskId,
      targetUrl: event.targetUrl,
      tabId: event.tabId,
      fromUrl: event.fromUrl,
      startedAt: event.startedAt,
      alreadyThere: !!(event.tabComplete && matchesNow),
    };
    return {
      state: {
        snapshot: snap(
          event.tabId,
          matchesNow ? url : event.targetUrl,
          'loading',
          now,
        ),
        nav: nextNav,
      },
      effect: nextNav.alreadyThere
        ? { type: 'schedule-settle', tabId: event.tabId, waitMs: TASK_PAGE_ALREADY_THERE_MS }
        : { type: 'noop' },
    };
  }

  if (event.type === 'removed') {
    if (!isTrackedTab(current, event.tabId)) return noop(current);
    return {
      state: { snapshot: null, nav: null },
      effect: { type: 'tab-closed' },
    };
  }

  if (!isTrackedTab(current, event.tabId)) return noop(current);

  if (event.type === 'nav-error') {
    return {
      state: {
        snapshot: snap(event.tabId, event.url || current.snapshot?.url || '', 'complete', now),
        nav: null,
      },
      effect: event.url ? { type: 'page-ready', url: event.url } : { type: 'noop' },
    };
  }

  if (event.type === 'before-navigate' || event.type === 'loading') {
    const dest = loadingSnapshotUrl(current, event.url, current.nav, ctx);
    const pendingUrl = ctx.navMatches(event.url) || ctx.findPendingTask(event.url) ? event.url : dest;
    const next = current.nav
      ? {
          snapshot: snap(event.tabId, dest, 'loading', now),
          nav: current.nav,
        }
      : beginNavFromPending(current, event.tabId, pendingUrl, 'loading', ctx);
    return { state: next, effect: { type: 'noop' } };
  }

  if (event.type === 'url') {
    if (current.nav && isStaleFromUrl(event.url, current.nav, ctx) && event.loading) {
      const dest = loadingSnapshotUrl(current, event.url, current.nav, ctx);
      return {
        state: { snapshot: snap(event.tabId, dest, 'loading', now), nav: current.nav },
        effect: { type: 'noop' },
      };
    }
    if (event.loading) {
      const dest = loadingSnapshotUrl(current, event.url, current.nav, ctx);
      const next = current.nav
        ? { snapshot: snap(event.tabId, dest, 'loading', now), nav: current.nav }
        : beginNavFromPending(current, event.tabId, dest, 'loading', ctx);
      return { state: next, effect: { type: 'noop' } };
    }
    if (current.nav) {
      return keepNavLoading(current, event.tabId, event.url, ctx, ctx.navMatches(event.url));
    }
    const adopted: TaskPageState = {
      snapshot: snap(event.tabId, event.url, 'complete', now),
      nav: current.nav,
    };
    return considerReady(adopted, event.tabId, event.url, ctx);
  }

  if (event.type === 'spa') {
    if (current.nav) {
      if (ctx.navMatches(event.url)) {
        return keepNavLoading(current, event.tabId, event.url, ctx, true);
      }
      return noop(current);
    }
    const adopted: TaskPageState = {
      snapshot: snap(event.tabId, event.url, 'complete', now),
      nav: null,
    };
    return considerReady(adopted, event.tabId, event.url, ctx);
  }

  if (event.type === 'complete') {
    if (current.nav) {
      if (ctx.navMatches(event.url)) {
        return keepNavLoading(current, event.tabId, event.url, ctx, true);
      }
      return noop(current);
    }
    return considerReady(current, event.tabId, event.url, ctx);
  }

  if (event.type === 'page-ready') {
    if (current.nav) {
      if (ctx.navMatches(event.url)) {
        return keepNavLoading(current, event.tabId, event.url, ctx, true);
      }
      return noop(current);
    }
    return considerReady(current, event.tabId, event.url, ctx);
  }

  if (event.type === 'network-busy') {
    if (!current.nav) return noop(current);
    return {
      state: {
        snapshot: snap(
          event.tabId,
          current.snapshot?.url || current.nav.targetUrl,
          'loading',
          now,
        ),
        nav: { ...current.nav, sawNetwork: true },
      },
      effect: {
        type: 'schedule-settle',
        tabId: event.tabId,
        waitMs: TASK_PAGE_READY_FALLBACK_MS,
      },
    };
  }

  if (event.type === 'network-idle') {
    if (!current.nav) return noop(current);
    if (!ctx.navMatches(event.url)) return noop(current);
    return handoffReady(event.tabId, event.url, ctx);
  }

  if (event.type === 'settle') {
    if (!current.nav) return noop(current);
    if (!ctx.navMatches(event.url)) {
      if (current.nav.destSeenAt || current.nav.alreadyThere) {
        return {
          state: { snapshot: snap(event.tabId, event.url, 'complete', now), nav: null },
          effect: { type: 'noop' },
        };
      }
      return noop(current);
    }
    const waitMs = settleWaitMs(current.nav);
    const lastAt =
      current.snapshot?.updatedAt ?? current.nav.destSeenAt ?? current.nav.startedAt;
    const elapsed = now - lastAt;
    if (elapsed < waitMs) {
      return {
        state: current,
        effect: {
          type: 'schedule-settle',
          tabId: event.tabId,
          waitMs: Math.max(50, waitMs - elapsed),
        },
      };
    }
    return handoffReady(event.tabId, event.url, ctx);
  }

  return noop(current);
}
