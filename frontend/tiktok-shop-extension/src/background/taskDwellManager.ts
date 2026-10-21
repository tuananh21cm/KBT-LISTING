import { STORAGE_KEYS } from '../shared/config';
import type { ShopDailyTask } from '../shared/types';
import {
  buildDwellEntry,
  parseTaskDwellAlarmName,
  taskDwellAlarmName,
  type TaskDwellMap,
} from '../shared/taskDwell';

async function readDwellMap(): Promise<TaskDwellMap> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.taskDwells);
    const map = (raw[STORAGE_KEYS.taskDwells] as TaskDwellMap | undefined) ?? {};
    return map && typeof map === 'object' ? map : {};
  } catch {
    return {};
  }
}

async function writeDwellMap(map: TaskDwellMap): Promise<void> {
  const keys = Object.keys(map);
  if (keys.length === 0) {
    await chrome.storage.local.remove(STORAGE_KEYS.taskDwells);
    return;
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.taskDwells]: map });
}

async function clearDwellAlarm(taskId: string): Promise<void> {
  await chrome.alarms.clear(taskDwellAlarmName(taskId));
}

export async function readTaskDwellMap(): Promise<TaskDwellMap> {
  return readDwellMap();
}

export async function clearTaskDwell(taskId: string): Promise<void> {
  const map = await readDwellMap();
  if (!map[taskId]) return;
  delete map[taskId];
  await clearDwellAlarm(taskId);
  await writeDwellMap(map);
}

export async function clearAllTaskDwells(): Promise<void> {
  const map = await readDwellMap();
  await Promise.all(Object.keys(map).map((taskId) => clearDwellAlarm(taskId)));
  await chrome.storage.local.remove(STORAGE_KEYS.taskDwells);
}

/**
 * Bắt đầu (hoặc reset) đếm thời gian xem trang cho một nhiệm vụ.
 * Gọi lại khi SPA đổi URL nhưng vẫn khớp — timer tính lại từ đầu.
 */
export async function startTaskDwell(task: ShopDailyTask, url: string): Promise<void> {
  const duration = Math.max(0, Math.floor(task.viewDurationSeconds ?? 0));
  if (duration <= 0) return;

  const now = Date.now();
  const entry = buildDwellEntry(task.id, url, duration, now);
  const map = await readDwellMap();
  map[task.id] = entry;
  await writeDwellMap(map);
  await chrome.alarms.create(taskDwellAlarmName(task.id), { when: entry.endsAt });
}

/** Huỷ các dwell không còn khớp URL đang mở. */
export async function clearStaleTaskDwells(
  url: string,
  isMatch: (task: ShopDailyTask, openUrl: string) => boolean,
  planItems: ShopDailyTask[],
): Promise<void> {
  const map = await readDwellMap();
  const byId = new Map(planItems.map((task) => [task.id, task]));
  let changed = false;

  for (const [taskId] of Object.entries(map)) {
    const task = byId.get(taskId);
    if (!task || task.done || !isMatch(task, url)) {
      delete map[taskId];
      await clearDwellAlarm(taskId);
      changed = true;
    }
  }

  if (changed) await writeDwellMap(map);
}

export function installTaskDwellAlarmListener(
  onDue: (taskId: string) => void | Promise<void>,
): void {
  chrome.alarms.onAlarm.addListener((alarm) => {
    const taskId = parseTaskDwellAlarmName(alarm.name);
    if (!taskId) return;
    void onDue(taskId);
  });
}
