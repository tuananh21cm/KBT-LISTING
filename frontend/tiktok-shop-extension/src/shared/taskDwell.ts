/** Một nhiệm vụ đang đếm thời gian xem trang sau khi load xong. */
export interface TaskDwellEntry {
  taskId: string;
  url: string;
  startedAt: number;
  endsAt: number;
}

/** Map taskId → dwell đang chạy (lưu chrome.storage). */
export type TaskDwellMap = Record<string, TaskDwellEntry>;

export const TASK_DWELL_ALARM_PREFIX = 'kb-task-dwell-';

export function taskDwellAlarmName(taskId: string): string {
  return `${TASK_DWELL_ALARM_PREFIX}${taskId}`;
}

export function parseTaskDwellAlarmName(name: string): string | null {
  if (!name.startsWith(TASK_DWELL_ALARM_PREFIX)) return null;
  const taskId = name.slice(TASK_DWELL_ALARM_PREFIX.length).trim();
  return taskId || null;
}

export function dwellRemainingSeconds(endsAt: number, now = Date.now()): number {
  return Math.max(0, Math.ceil((endsAt - now) / 1000));
}

/** Hiển thị đếm ngược còn lại theo giây trên side panel (`16s`). */
export function formatDwellCountdown(remainingSec: number): string {
  return `${Math.max(0, Math.floor(remainingSec))}s`;
}

export function buildDwellEntry(
  taskId: string,
  url: string,
  durationSec: number,
  now = Date.now(),
): TaskDwellEntry {
  const safeDuration = Math.max(0, Math.floor(durationSec));
  return {
    taskId,
    url,
    startedAt: now,
    endsAt: now + safeDuration * 1000,
  };
}
