import { STORAGE_KEYS } from '../shared/config';
import { dwellRemainingSeconds, type TaskDwellMap } from '../shared/taskDwell';
import type { ShopDailyTaskPlan, TaskNavigationState } from '../shared/types';

export interface AutoDailyTaskRunnerDeps {
  isUnifiedSyncInFlight: () => boolean;
  readDailyTaskPlan: () => Promise<ShopDailyTaskPlan | null>;
  readTaskNavigation: () => Promise<TaskNavigationState | null>;
  readTaskDwellMap: () => Promise<TaskDwellMap>;
  openTaskUrl: (url: string, taskId?: string) => Promise<{ ok: boolean; message: string }>;
  runUnifiedSync: () => Promise<{ ok: boolean; message: string }>;
  /** Local: mọi task có URL → done=false để Auto chạy lại full list. */
  resetLocalDailyTasksForAutoRun: () => Promise<void>;
  /** Khôi phục done từ API khi tắt Auto giữa chừng. */
  refreshDailyTaskPlanFromApi: () => Promise<void>;
}

let deps: AutoDailyTaskRunnerDeps | null = null;
/** Hàng đợi serial — mỗi schedule chạy sau nhịp trước (không bỏ tick khi complete chồng open). */
let queue: Promise<void> = Promise.resolve();
/** Tránh mở lại cùng task ngay sau lần open fail (tắt Auto). */
let lastOpenFailedTaskId: string | null = null;

export function installAutoDailyTaskRunner(next: AutoDailyTaskRunnerDeps): void {
  deps = next;
}

export async function readAutoDailyTasksEnabled(): Promise<boolean> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.autoDailyTasks);
    return raw[STORAGE_KEYS.autoDailyTasks] === true;
  } catch {
    return false;
  }
}

export async function writeAutoDailyTasksEnabled(enabled: boolean): Promise<void> {
  if (enabled) {
    await chrome.storage.local.set({ [STORAGE_KEYS.autoDailyTasks]: true });
  } else {
    lastOpenFailedTaskId = null;
    await chrome.storage.local.remove(STORAGE_KEYS.autoDailyTasks);
  }
}

function hasActiveDwell(map: TaskDwellMap): boolean {
  const now = Date.now();
  return Object.values(map).some((entry) => dwellRemainingSeconds(entry.endsAt, now) > 0);
}

function firstPendingTask(plan: ShopDailyTaskPlan | null): { id: string; url: string } | null {
  if (!plan?.items.length) return null;
  for (const task of plan.items) {
    if (task.done) continue;
    const url = (task.url || '').trim();
    if (!url) continue;
    return { id: task.id, url };
  }
  return null;
}

async function runTick(): Promise<void> {
  if (!deps) return;

  const enabled = await readAutoDailyTasksEnabled();
  if (!enabled) return;

  if (deps.isUnifiedSyncInFlight()) return;

  const [nav, dwells, plan] = await Promise.all([
    deps.readTaskNavigation(),
    deps.readTaskDwellMap(),
    deps.readDailyTaskPlan(),
  ]);

  if (nav) return;
  if (hasActiveDwell(dwells)) return;

  // Chưa có cache nhiệm vụ — chờ REFRESH_TASKS / Sync / hydrate.
  if (!plan) return;

  const pending = firstPendingTask(plan);
  if (pending) {
    if (lastOpenFailedTaskId === pending.id) {
      await writeAutoDailyTasksEnabled(false);
      return;
    }

    const result = await deps.openTaskUrl(pending.url, pending.id);
    if (!result.ok) {
      lastOpenFailedTaskId = pending.id;
      await writeAutoDailyTasksEnabled(false);
      console.warn('[kb-tts] Auto daily tasks stopped:', result.message);
      return;
    }
    lastOpenFailedTaskId = null;
    return;
  }

  // Hết nhiệm vụ (items rỗng hoặc toàn done) → Sync All một lần rồi tắt Auto.
  await writeAutoDailyTasksEnabled(false);
  try {
    const sync = await deps.runUnifiedSync();
    if (!sync.ok) {
      console.warn('[kb-tts] Auto Sync All failed:', sync.message);
    }
  } catch (err) {
    console.warn('[kb-tts] Auto Sync All error:', (err as Error).message || err);
  }
}

/**
 * Một nhịp Auto: mở task pending tiếp theo, hoặc Sync All rồi tắt khi đã xong hết.
 * Idempotent khi đang nav / dwell / sync — sẽ được schedule lại khi state đổi.
 */
export function scheduleAutoDailyTasksTick(): void {
  queue = queue.then(runTick, runTick);
}

export async function tickAutoDailyTasks(): Promise<void> {
  const wait = queue.then(runTick, runTick);
  queue = wait.then(
    () => undefined,
    () => undefined,
  );
  await wait;
}

/**
 * Bật Auto: reset local done=false (replay full list) rồi tick.
 * Phải reset trước khi enable/tick — tránh nhảy thẳng Sync All khi cache đã done.
 */
export async function beginAutoDailyTasksRun(): Promise<void> {
  if (!deps) return;
  lastOpenFailedTaskId = null;
  await deps.resetLocalDailyTasksForAutoRun();
  await writeAutoDailyTasksEnabled(true);
  await tickAutoDailyTasks();
}

/** Tắt Auto (user bỏ check): dừng + khôi phục done từ API. */
export async function stopAutoDailyTasksRun(): Promise<void> {
  await writeAutoDailyTasksEnabled(false);
  if (!deps) return;
  try {
    await deps.refreshDailyTaskPlanFromApi();
  } catch (err) {
    console.warn('[kb-tts] Auto stop refresh tasks failed:', (err as Error).message || err);
  }
}
