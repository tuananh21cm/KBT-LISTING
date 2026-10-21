import type { ShopDailyTaskPlan } from './types';

export interface DailyTaskProgress {
  total: number;
  doneCount: number;
  requiredCount: number;
  syncUnlocked: boolean;
}

/** Ngưỡng 50%: doneCount * 2 >= total (tránh float). */
export function getDailyTaskProgress(plan: ShopDailyTaskPlan | null | undefined): DailyTaskProgress {
  const total = plan?.items.length ?? 0;
  const doneCount = plan?.items.filter((task) => task.done).length ?? 0;
  const requiredCount = total === 0 ? 0 : Math.ceil(total * 0.5);
  const syncUnlocked = total === 0 || doneCount >= requiredCount;
  return { total, doneCount, requiredCount, syncUnlocked };
}

export function formatDailyTaskSyncBlockMessage(
  plan: ShopDailyTaskPlan | null | undefined,
): string {
  const { requiredCount, total } = getDailyTaskProgress(plan);
  if (total === 0) return '';
  return `Hoàn thành ít nhất ${requiredCount}/${total} nhiệm vụ (50%) trước khi Sync.`;
}

export function formatDailyTaskSyncUnlockHint(
  plan: ShopDailyTaskPlan | null | undefined,
): string {
  const { requiredCount, total, syncUnlocked } = getDailyTaskProgress(plan);
  if (total === 0 || syncUnlocked) return '';
  return `Hoàn thành ít nhất ${requiredCount}/${total} nhiệm vụ để mở khóa Sync.`;
}
