import { STORAGE_KEYS } from '../shared/config';
import type { UnifiedSyncState, UnifiedSyncStepState } from '../shared/types';
import { recordTikTokSyncLog, type TikTokShopSyncType } from './kinhbacApi';

function pendingStep(): UnifiedSyncStepState {
  return { status: 'pending' };
}

export function createUnifiedSyncState(runId: string): UnifiedSyncState {
  return {
    runId,
    mode: 'all',
    phase: 'running',
    profile: pendingStep(),
    payout: pendingStep(),
    opportunity: pendingStep(),
    analytics: pendingStep(),
    healthCenter: pendingStep(),
    shopScore: pendingStep(),
    tasks: pendingStep(),
    startedAt: Date.now(),
  };
}

const FAST_SYNC_SKIP_MESSAGE = 'Sync fast — bỏ qua';

function skippedStep(message: string): UnifiedSyncStepState {
  return { status: 'skipped', message };
}

/** Chỉ chạy Finance, Health Center, Shop Score — các bước khác đánh dấu skipped ngay. */
export function createFastSyncState(runId: string): UnifiedSyncState {
  return {
    runId,
    mode: 'fast',
    phase: 'running',
    profile: skippedStep(FAST_SYNC_SKIP_MESSAGE),
    payout: pendingStep(),
    opportunity: skippedStep(FAST_SYNC_SKIP_MESSAGE),
    analytics: skippedStep(FAST_SYNC_SKIP_MESSAGE),
    healthCenter: pendingStep(),
    shopScore: pendingStep(),
    tasks: skippedStep(FAST_SYNC_SKIP_MESSAGE),
    startedAt: Date.now(),
  };
}

export async function readUnifiedSyncState(): Promise<UnifiedSyncState | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.unifiedSync);
    return (raw[STORAGE_KEYS.unifiedSync] as UnifiedSyncState | undefined) ?? null;
  } catch {
    return null;
  }
}

export async function writeUnifiedSyncState(state: UnifiedSyncState | null): Promise<void> {
  if (!state) {
    await chrome.storage.local.remove(STORAGE_KEYS.unifiedSync);
    return;
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.unifiedSync]: state });
}

export async function patchUnifiedStep(
  runId: string,
  step: keyof Pick<
    UnifiedSyncState,
    'profile' | 'payout' | 'opportunity' | 'analytics' | 'healthCenter' | 'shopScore' | 'tasks'
  >,
  patch: UnifiedSyncStepState,
): Promise<void> {
  const current = await readUnifiedSyncState();
  if (!current || current.runId !== runId) return;
  const next: UnifiedSyncState = {
    ...current,
    [step]: { ...patch, at: patch.at ?? Date.now() },
  };
  await writeUnifiedSyncState(next);
}

export async function finishUnifiedSync(runId: string): Promise<void> {
  const current = await readUnifiedSyncState();
  if (!current || current.runId !== runId) return;
  await writeUnifiedSyncState({
    ...current,
    phase: 'done',
    finishedAt: Date.now(),
  });
}

export async function logSkippedStep(
  runId: string,
  syncType: TikTokShopSyncType,
  message: string,
  externalShopId?: string,
): Promise<void> {
  try {
    await recordTikTokSyncLog({
      runId,
      syncType,
      status: 'skipped',
      errorMessage: message,
      externalShopId: externalShopId || undefined,
    });
  } catch (err) {
    console.warn('[kb-tts] record sync log failed:', (err as Error).message || err);
  }
}
