import { describe, expect, it } from 'vitest';
import {
  TASK_PAGE_ALREADY_THERE_MS,
  TASK_PAGE_NAV_GRACE_MS,
  TASK_PAGE_READY_FALLBACK_MS,
  buildTaskPageContext,
  isTaskNavigating,
  reduceTaskPageEvent,
  resolveNavigatingTaskId,
  type TaskPageEvent,
  type TaskPageReduceContext,
  type TaskPageState,
} from './taskPageState';
import type { ShopDailyTaskPlan } from './types';

const TASK_ID = 'task-1';
const TAB = 42;
const OTHER = 99;
const HOME = 'https://seller-us.tiktok.com/homepage';
const PRODUCT = 'https://seller-us.tiktok.com/product/manage';
const FINANCE = 'https://seller-us.tiktok.com/finance';
const NOW = 1_700_000_000_000;

const plan: ShopDailyTaskPlan = {
  date: '2026-08-24',
  sellerId: 's1',
  fetchedAt: NOW,
  items: [
    {
      id: TASK_ID,
      name: 'Product',
      url: PRODUCT,
      urlMatchPattern: 'https://seller-us.tiktok.com/product/manage',
      viewDurationSeconds: 30,
      done: false,
    },
  ],
};

function ctx(navTaskId: string | null = TASK_ID): TaskPageReduceContext {
  const nav =
    navTaskId == null
      ? null
      : {
          taskId: navTaskId,
          targetUrl: PRODUCT,
          tabId: TAB,
          fromUrl: HOME,
          startedAt: NOW,
        };
  return buildTaskPageContext(plan, nav, NOW);
}

function homeComplete(): TaskPageState {
  return {
    snapshot: { tabId: TAB, url: HOME, status: 'complete', updatedAt: NOW },
    nav: null,
  };
}

function navigating(): TaskPageState {
  return {
    snapshot: { tabId: TAB, url: PRODUCT, status: 'loading', updatedAt: NOW },
    nav: {
      taskId: TASK_ID,
      targetUrl: PRODUCT,
      tabId: TAB,
      fromUrl: HOME,
      startedAt: NOW,
    },
  };
}

function apply(state: TaskPageState, event: TaskPageEvent, context = ctx()) {
  return reduceTaskPageEvent(state, event, context);
}

describe('reduceTaskPageEvent', () => {
  it('bấm nhiệm vụ khi đã đứng sẵn đúng URL complete — giữ spinner, không bàn giao ngay', () => {
    const started = apply(homeComplete(), {
      type: 'nav-start',
      taskId: TASK_ID,
      targetUrl: PRODUCT,
      tabId: TAB,
      fromUrl: PRODUCT,
      startedAt: NOW,
      currentUrl: PRODUCT,
      tabComplete: true,
    });
    expect(started.state.nav?.taskId).toBe(TASK_ID);
    expect(started.state.nav?.alreadyThere).toBe(true);
    expect(started.state.snapshot?.status).toBe('loading');
    expect(started.effect).toEqual({
      type: 'schedule-settle',
      tabId: TAB,
      waitMs: TASK_PAGE_ALREADY_THERE_MS,
    });

    const ready = apply(
      started.state,
      { type: 'settle', tabId: TAB, url: PRODUCT },
      buildTaskPageContext(plan, started.state.nav, NOW + TASK_PAGE_ALREADY_THERE_MS),
    );
    expect(ready.effect).toEqual({ type: 'page-ready', url: PRODUCT });
    expect(ready.state.nav).toBeNull();
  });

  it('giữ spinner khi complete/favicon URL cũ trong lúc đang load đích', () => {
    let state = homeComplete();
    const start = apply(state, {
      type: 'nav-start',
      taskId: TASK_ID,
      targetUrl: PRODUCT,
      tabId: TAB,
      fromUrl: HOME,
      startedAt: NOW,
      currentUrl: HOME,
      tabComplete: true,
    });
    expect(start.state.nav?.taskId).toBe(TASK_ID);
    expect(start.state.snapshot?.status).toBe('loading');
    expect(start.state.snapshot?.url).toBe(PRODUCT);
    state = start.state;

    const staleComplete = apply(state, { type: 'complete', tabId: TAB, url: HOME });
    expect(staleComplete.effect.type).toBe('noop');
    expect(staleComplete.state.nav?.taskId).toBe(TASK_ID);

    const loadingOld = apply(state, { type: 'loading', tabId: TAB, url: HOME });
    expect(loadingOld.state.nav?.taskId).toBe(TASK_ID);
    expect(loadingOld.state.snapshot?.url).toBe(PRODUCT);
    expect(loadingOld.state.snapshot?.status).toBe('loading');

    const urlOld = apply(state, {
      type: 'url',
      tabId: TAB,
      url: HOME,
      loading: true,
    });
    expect(urlOld.state.snapshot?.url).toBe(PRODUCT);
    expect(urlOld.state.nav?.taskId).toBe(TASK_ID);

    const destUrlWhileLoading = apply(loadingOld.state, {
      type: 'url',
      tabId: TAB,
      url: PRODUCT,
      loading: false,
    });
    expect(destUrlWhileLoading.effect.type).toBe('schedule-settle');
    expect(destUrlWhileLoading.state.nav?.taskId).toBe(TASK_ID);
    expect(destUrlWhileLoading.state.snapshot?.status).toBe('loading');

    const docComplete = apply(loadingOld.state, { type: 'complete', tabId: TAB, url: PRODUCT });
    expect(docComplete.effect).toEqual({
      type: 'schedule-settle',
      tabId: TAB,
      waitMs: TASK_PAGE_NAV_GRACE_MS,
    });
    expect(docComplete.state.nav?.taskId).toBe(TASK_ID);

    const ping = apply(docComplete.state, { type: 'page-ready', tabId: TAB, url: PRODUCT });
    expect(ping.effect.type).toBe('schedule-settle');
    expect(ping.state.nav?.taskId).toBe(TASK_ID);

    const tooSoon = apply(
      ping.state,
      { type: 'settle', tabId: TAB, url: PRODUCT },
      buildTaskPageContext(plan, ping.state.nav, NOW + 1_500),
    );
    expect(tooSoon.effect.type).toBe('schedule-settle');
    expect(tooSoon.state.nav?.taskId).toBe(TASK_ID);

    const idle = apply(ping.state, { type: 'network-idle', tabId: TAB, url: PRODUCT });
    expect(idle.effect).toEqual({ type: 'page-ready', url: PRODUCT });
    expect(idle.state.nav).toBeNull();
  });

  it('bỏ qua sự kiện tab khác — không xóa spinner', () => {
    const result = apply(navigating(), { type: 'complete', tabId: OTHER, url: HOME });
    expect(result.effect.type).toBe('noop');
    expect(result.state.nav?.taskId).toBe(TASK_ID);
  });

  it('page-ready tab cũ không xóa nav/dwell tab đang theo dõi', () => {
    const result = apply(navigating(), { type: 'page-ready', tabId: OTHER, url: HOME });
    expect(result.effect.type).toBe('noop');
    expect(result.state.nav?.taskId).toBe(TASK_ID);
    expect(result.state.snapshot?.url).toBe(PRODUCT);
  });

  it('SPA history tới URL đích — giữ spinner đến network-idle', () => {
    const spa = apply(navigating(), { type: 'spa', tabId: TAB, url: `${PRODUCT}?shop_region=US` });
    expect(spa.effect).toEqual({
      type: 'schedule-settle',
      tabId: TAB,
      waitMs: TASK_PAGE_NAV_GRACE_MS,
    });
    expect(spa.state.nav?.taskId).toBe(TASK_ID);
    expect(spa.state.snapshot?.status).toBe('loading');

    const ping = apply(spa.state, {
      type: 'page-ready',
      tabId: TAB,
      url: `${PRODUCT}?shop_region=US`,
    });
    expect(ping.effect.type).toBe('schedule-settle');
    expect(ping.state.nav).not.toBeNull();

    const busy = apply(ping.state, { type: 'network-busy', tabId: TAB });
    expect(busy.state.nav?.sawNetwork).toBe(true);
    expect(busy.effect).toEqual({
      type: 'schedule-settle',
      tabId: TAB,
      waitMs: TASK_PAGE_READY_FALLBACK_MS,
    });

    const idle = apply(busy.state, {
      type: 'network-idle',
      tabId: TAB,
      url: `${PRODUCT}?shop_region=US`,
    });
    expect(idle.effect.type).toBe('page-ready');
    expect(idle.state.nav).toBeNull();
  });

  it('beforeNavigate menu TikTok (chưa có nav) bật spinner rồi complete bàn giao', () => {
    const loading = apply(
      homeComplete(),
      { type: 'before-navigate', tabId: TAB, url: PRODUCT },
      ctx(null),
    );
    expect(loading.state.nav?.taskId).toBe(TASK_ID);
    expect(loading.state.snapshot?.status).toBe('loading');
    expect(loading.state.snapshot?.url).toBe(PRODUCT);

    const docComplete = apply(
      loading.state,
      { type: 'complete', tabId: TAB, url: PRODUCT },
      ctx(TASK_ID),
    );
    expect(docComplete.state.nav?.taskId).toBe(TASK_ID);
    expect(docComplete.effect.type).toBe('schedule-settle');

    const ping = apply(docComplete.state, { type: 'page-ready', tabId: TAB, url: PRODUCT }, ctx(TASK_ID));
    expect(ping.effect.type).toBe('schedule-settle');

    const ready = apply(
      ping.state,
      { type: 'settle', tabId: TAB, url: PRODUCT },
      buildTaskPageContext(plan, ping.state.nav, NOW + TASK_PAGE_NAV_GRACE_MS),
    );
    expect(ready.effect).toEqual({ type: 'page-ready', url: PRODUCT });
    expect(ready.state.nav).toBeNull();
  });

  it('settle khi đã rời URL đích — tắt spinner, không bàn giao dwell', () => {
    const ping = apply(navigating(), { type: 'page-ready', tabId: TAB, url: PRODUCT });
    expect(ping.state.nav?.destSeenAt).toBe(NOW);

    const left = apply(
      ping.state,
      { type: 'settle', tabId: TAB, url: FINANCE },
      buildTaskPageContext(plan, ping.state.nav, NOW + TASK_PAGE_NAV_GRACE_MS),
    );
    expect(left.effect.type).toBe('noop');
    expect(left.state.nav).toBeNull();
  });

  it('complete URL khác khi đang nav — giữ spinner (trang trung gian)', () => {
    const result = apply(navigating(), { type: 'complete', tabId: TAB, url: FINANCE });
    expect(result.effect.type).toBe('noop');
    expect(result.state.nav?.taskId).toBe(TASK_ID);
    expect(result.state.snapshot?.url).toBe(PRODUCT);
  });

  it('url event khi tab complete (SPA tabs.onUpdated) tới trang khác → page-ready để huỷ dwell', () => {
    const watching: TaskPageState = {
      snapshot: { tabId: TAB, url: PRODUCT, status: 'complete', updatedAt: NOW },
      nav: null,
    };
    const result = apply(
      watching,
      { type: 'url', tabId: TAB, url: FINANCE, loading: false },
      ctx(null),
    );
    expect(result.effect).toEqual({ type: 'page-ready', url: FINANCE });
    expect(result.state.snapshot?.url).toBe(FINANCE);
  });

  it('complete lệch snapshot sau khi đã ready = document cũ, không phát page-ready', () => {
    const watching: TaskPageState = {
      snapshot: { tabId: TAB, url: PRODUCT, status: 'complete', updatedAt: NOW },
      nav: null,
    };
    const result = apply(watching, { type: 'complete', tabId: TAB, url: HOME }, ctx(null));
    expect(result.effect.type).toBe('noop');
    expect(result.state.snapshot?.url).toBe(PRODUCT);
  });

  it('đóng tab đang theo dõi → tab-closed', () => {
    const result = apply(navigating(), { type: 'removed', tabId: TAB });
    expect(result.effect.type).toBe('tab-closed');
    expect(result.state.nav).toBeNull();
    expect(result.state.snapshot).toBeNull();
  });
});

describe('isTaskNavigating', () => {
  const task = plan.items[0];

  it('hiện spinner theo nav.taskId', () => {
    expect(isTaskNavigating(task.id, TASK_ID, false)).toBe(true);
    expect(isTaskNavigating('other', TASK_ID, false)).toBe(false);
  });

  it('không spinner khi đang countdown', () => {
    expect(isTaskNavigating(task.id, TASK_ID, true)).toBe(false);
  });
});

describe('resolveNavigatingTaskId', () => {
  const broad: ShopDailyTaskPlan = {
    ...plan,
    items: [
      {
        id: 'home',
        name: 'Home',
        url: 'https://seller-us.tiktok.com',
        urlMatchPattern: 'https://seller-us.tiktok.com',
        viewDurationSeconds: 30,
        done: false,
      },
      ...plan.items,
    ],
  };

  it('khi đang nav tới product, không gắn spinner cho nhiệm vụ prefix ngắn hơn', () => {
    const nav = {
      taskId: TASK_ID,
      targetUrl: PRODUCT,
      tabId: TAB,
      fromUrl: HOME,
      startedAt: NOW,
    };
    const snapshot = {
      tabId: TAB,
      url: PRODUCT,
      status: 'loading' as const,
      updatedAt: NOW,
    };
    expect(resolveNavigatingTaskId(broad, nav, snapshot)).toBe(TASK_ID);
  });

  it('menu TikTok (không nav): chọn pattern dài nhất khi snapshot đang load', () => {
    const snapshot = {
      tabId: TAB,
      url: PRODUCT,
      status: 'loading' as const,
      updatedAt: NOW,
    };
    expect(resolveNavigatingTaskId(broad, null, snapshot)).toBe(TASK_ID);
  });
});
