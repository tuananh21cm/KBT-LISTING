import { STORAGE_KEYS } from '../shared/config';
import type {
  QuickAddJobRecord,
  QuickAddListProductResponse,
  QuickAddListStatus,
} from '../shared/messages';
import {
  adjustQuickAddActiveCount,
  isSearchOrBatchAddBlocking,
} from './productSearch';

/** Pump: tối đa bao nhiêu tab PDP / post-html song song. */
export const QUICK_ADD_MAX_CONCURRENT = 5;

/** Dọn job success/failed khỏi storage sau TTL. */
export const QUICK_ADD_JOB_TTL_MS = 30_000;

export type QuickAddJobMap = Record<string, QuickAddJobRecord>;

export type QuickAddQueueDeps = {
  hasAuth(): Promise<boolean>;
  hasSelectedShops(): Promise<boolean>;
  isProductFullyAdded(goodsId: string): Promise<boolean>;
  resolvePdpUrl(goodsId: string, urlHint?: string): string;
  /**
   * Chạy 1 job: mở tab → capture → post-html → đóng tab.
   * Gọi `onPdpTab` khi có tabId để persist.
   */
  runJob(args: {
    goodsId: string;
    pdpUrl: string;
    listTabId?: number;
    onPdpTab: (tabId: number) => void;
  }): Promise<QuickAddListProductResponse>;
  notifyStatus(
    listTabId: number | undefined,
    goodsId: string,
    status: QuickAddListStatus,
    message?: string,
  ): Promise<void>;
};

type QueuedItem = {
  goodsId: string;
  urlHint?: string;
  listTabId?: number;
  resolve: (res: QuickAddListProductResponse) => void;
};

const waitQueue: QueuedItem[] = [];
/** goodsId đang chạy (trong slot). */
const runningIds = new Set<string>();
/** Promise đang chờ cho goodsId (queued hoặc running) — dedup. */
const inflight = new Map<string, Promise<QuickAddListProductResponse>>();

let deps: QuickAddQueueDeps | null = null;
let pumpScheduled = false;

export function configureQuickAddQueue(next: QuickAddQueueDeps): void {
  deps = next;
}

async function readJobs(): Promise<QuickAddJobMap> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.quickAddJobs);
  const map = raw[STORAGE_KEYS.quickAddJobs];
  if (!map || typeof map !== 'object' || Array.isArray(map)) return {};
  return map as QuickAddJobMap;
}

async function writeJobs(map: QuickAddJobMap): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.quickAddJobs]: map });
}

async function upsertJob(
  goodsId: string,
  patch: Partial<QuickAddJobRecord> & { status: QuickAddJobRecord['status'] },
): Promise<void> {
  const map = await readJobs();
  const prev = map[goodsId];
  map[goodsId] = {
    status: patch.status,
    message: patch.message ?? prev?.message,
    listTabId: patch.listTabId ?? prev?.listTabId,
    url: patch.url ?? prev?.url,
    pdpTabId: patch.pdpTabId ?? prev?.pdpTabId,
    at: Date.now(),
  };
  await writeJobs(map);
  scheduleJobCleanup();
}

let cleanupTimer = 0;
function scheduleJobCleanup(): void {
  if (cleanupTimer) return;
  cleanupTimer = setTimeout(() => {
    cleanupTimer = 0;
    void pruneTerminalJobs();
  }, 5_000) as unknown as number;
}

async function pruneTerminalJobs(): Promise<void> {
  const map = await readJobs();
  const now = Date.now();
  let changed = false;
  for (const [id, job] of Object.entries(map)) {
    if (
      (job.status === 'success' || job.status === 'failed') &&
      now - (job.at || 0) >= QUICK_ADD_JOB_TTL_MS
    ) {
      delete map[id];
      changed = true;
    }
  }
  if (changed) await writeJobs(map);
}

async function finishJob(
  goodsId: string,
  listTabId: number | undefined,
  res: QuickAddListProductResponse,
): Promise<void> {
  const status: QuickAddJobRecord['status'] = res.ok ? 'success' : 'failed';
  await upsertJob(goodsId, {
    status,
    message: res.message,
    listTabId,
  });
  if (deps) {
    await deps.notifyStatus(listTabId, goodsId, status, res.message);
  }
}

/**
 * Enqueue Thêm nhanh. Resolve khi job của goodsId xong (sau queue + chạy).
 * Dedup: cùng goodsId đang queued/running → chung Promise.
 */
export function enqueueQuickAdd(
  goodsIdRaw: string,
  urlHint: string | undefined,
  listTabId: number | undefined,
): Promise<QuickAddListProductResponse> {
  const goodsId = String(goodsIdRaw || '').trim();
  if (!goodsId) {
    return Promise.resolve({ ok: false, message: 'Thiếu goodsId.' });
  }
  if (!deps) {
    return Promise.resolve({ ok: false, message: 'Quick-add chưa sẵn sàng.', goodsId });
  }

  const existing = inflight.get(goodsId);
  if (existing) return existing;

  const promise = new Promise<QuickAddListProductResponse>((resolve) => {
    void (async () => {
      const d = deps!;
      if (!(await d.hasAuth())) {
        const msg = 'Chưa đăng nhập KBT Craper.';
        await upsertJob(goodsId, { status: 'failed', message: msg, listTabId });
        await d.notifyStatus(listTabId, goodsId, 'failed', msg);
        resolve({ ok: false, message: msg, goodsId });
        return;
      }
      if (!(await d.hasSelectedShops())) {
        const msg = 'Chọn ít nhất 1 shop Active trước khi thêm sản phẩm.';
        await upsertJob(goodsId, { status: 'failed', message: msg, listTabId });
        await d.notifyStatus(listTabId, goodsId, 'failed', msg);
        resolve({ ok: false, message: msg, goodsId });
        return;
      }
      if (await d.isProductFullyAdded(goodsId)) {
        const msg = 'Sản phẩm này đã được thêm cho mọi shop đang chọn.';
        await upsertJob(goodsId, { status: 'success', message: msg, listTabId });
        await d.notifyStatus(listTabId, goodsId, 'success', msg);
        resolve({ ok: true, message: msg, goodsId });
        return;
      }
      if (isSearchOrBatchAddBlocking()) {
        const msg = 'Đang tìm hoặc thêm từ Search — đợi xong rồi thử lại.';
        await upsertJob(goodsId, { status: 'failed', message: msg, listTabId });
        await d.notifyStatus(listTabId, goodsId, 'failed', msg);
        resolve({ ok: false, message: msg, goodsId });
        return;
      }

      const pdpUrl = d.resolvePdpUrl(goodsId, urlHint);
      if (!pdpUrl) {
        const msg = 'Không tạo được URL sản phẩm.';
        await upsertJob(goodsId, { status: 'failed', message: msg, listTabId });
        await d.notifyStatus(listTabId, goodsId, 'failed', msg);
        resolve({ ok: false, message: msg, goodsId });
        return;
      }

      await upsertJob(goodsId, {
        status: 'queued',
        listTabId,
        url: pdpUrl,
      });
      await d.notifyStatus(listTabId, goodsId, 'queued');

      waitQueue.push({
        goodsId,
        urlHint,
        listTabId,
        resolve: (res) => resolve({ ...res, goodsId }),
      });
      schedulePump();
    })();
  });

  inflight.set(goodsId, promise);
  void promise.finally(() => {
    if (inflight.get(goodsId) === promise) inflight.delete(goodsId);
  });
  return promise;
}

function schedulePump(): void {
  if (pumpScheduled) return;
  pumpScheduled = true;
  queueMicrotask(() => {
    pumpScheduled = false;
    void pump();
  });
}

async function pump(): Promise<void> {
  if (!deps) return;
  while (runningIds.size < QUICK_ADD_MAX_CONCURRENT && waitQueue.length > 0) {
    if (isSearchOrBatchAddBlocking()) {
      // Không start job mới khi Search/batch chiếm; job queued giữ nguyên.
      break;
    }
    const item = waitQueue.shift();
    if (!item) break;
    if (runningIds.has(item.goodsId)) continue;
    runningIds.add(item.goodsId);
    adjustQuickAddActiveCount(1);
    void runQueuedItem(item).finally(() => {
      runningIds.delete(item.goodsId);
      adjustQuickAddActiveCount(-1);
      schedulePump();
    });
  }
}

async function runQueuedItem(item: QueuedItem): Promise<void> {
  const d = deps;
  if (!d) {
    item.resolve({ ok: false, message: 'Quick-add chưa sẵn sàng.', goodsId: item.goodsId });
    return;
  }

  const pdpUrl = d.resolvePdpUrl(item.goodsId, item.urlHint);
  await upsertJob(item.goodsId, {
    status: 'running',
    listTabId: item.listTabId,
    url: pdpUrl || undefined,
  });
  await d.notifyStatus(item.listTabId, item.goodsId, 'running');

  if (!pdpUrl) {
    const res = { ok: false, message: 'Không tạo được URL sản phẩm.', goodsId: item.goodsId };
    await finishJob(item.goodsId, item.listTabId, res);
    item.resolve(res);
    return;
  }

  try {
    if (await d.isProductFullyAdded(item.goodsId)) {
      const res = {
        ok: true,
        message: 'Sản phẩm này đã được thêm cho mọi shop đang chọn.',
        goodsId: item.goodsId,
      };
      await finishJob(item.goodsId, item.listTabId, res);
      item.resolve(res);
      return;
    }

    const res = await d.runJob({
      goodsId: item.goodsId,
      pdpUrl,
      listTabId: item.listTabId,
      onPdpTab: (tabId) => {
        void upsertJob(item.goodsId, {
          status: 'running',
          listTabId: item.listTabId,
          url: pdpUrl,
          pdpTabId: tabId,
        });
      },
    });
    await finishJob(item.goodsId, item.listTabId, res);
    item.resolve(res);
  } catch (err) {
    const res = {
      ok: false,
      message: (err as Error).message || 'Lỗi thêm nhanh sản phẩm.',
      goodsId: item.goodsId,
    };
    await finishJob(item.goodsId, item.listTabId, res);
    item.resolve(res);
  }
}
