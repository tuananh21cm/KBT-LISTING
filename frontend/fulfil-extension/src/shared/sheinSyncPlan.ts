import {
  SHEIN_AUTO_FULL_EVERY_N,
  sheinListFetchOptionsFull,
  sheinListFetchOptionsManualAll,
  sheinListFetchOptionsShallow,
  type SheinListFetchOptions,
} from './sheinOrders';

/** Cửa sổ list SHEIN theo trigger Sync Order / Auto Sync. */
export type SheinSyncListDepth =
  | 'manual_all'
  | 'manual_new'
  | 'auto_full'
  | 'auto_shallow';

export interface ResolveSheinSyncPlanInput {
  /** Nút Sync Order thủ công (`manual` / `ensureCapture`). */
  isManual: boolean;
  /** Settings `updateMode` — chỉ áp dụng khi thủ công. */
  updateMode?: string | null;
  lookbackDays?: number | null;
  /** Số lần Auto đã chạy (sau khi +1 trong caller). */
  autoSyncCount?: number;
}

export interface SheinSyncPlan {
  listOpts: SheinListFetchOptions;
  listDepth: SheinSyncListDepth;
  /** Gửi lên `POST …/shein-sync`. Auto luôn `new`. */
  effectiveUpdateMode: 'new' | 'all';
  /** Manual / auto_full: xóa hiệu lực snapshot + POST đủ list. Auto nông: fingerprint diff. */
  forceFullPost: boolean;
}

/**
 * Chọn cửa sổ list + updateMode cho một lượt Sync Order.
 *
 * Ba chế độ dùng chung một pipeline list→detail; chỉ khác `listOpts` / `effectiveUpdateMode` /
 * `forceFullPost`. Backend tự xếp `first_seen` / `new_link` / status-only.
 */
export function resolveSheinSyncPlan(
  input: ResolveSheinSyncPlanInput,
): SheinSyncPlan {
  const syncUpdateMode: 'new' | 'all' =
    input.updateMode === 'all' ? 'all' : 'new';

  if (input.isManual) {
    const isUpdateAll = syncUpdateMode === 'all';
    return {
      listOpts: isUpdateAll
        ? sheinListFetchOptionsManualAll()
        : sheinListFetchOptionsFull(input.lookbackDays),
      listDepth: isUpdateAll ? 'manual_all' : 'manual_new',
      effectiveUpdateMode: syncUpdateMode,
      forceFullPost: true,
    };
  }

  const count =
    Number.isFinite(input.autoSyncCount) && (input.autoSyncCount ?? 0) > 0
      ? Math.floor(input.autoSyncCount!)
      : 1;
  const doFull = count % SHEIN_AUTO_FULL_EVERY_N === 0;
  return {
    listOpts: doFull
      ? sheinListFetchOptionsFull(input.lookbackDays)
      : sheinListFetchOptionsShallow(),
    listDepth: doFull ? 'auto_full' : 'auto_shallow',
    effectiveUpdateMode: 'new',
    // auto_full: POST đủ list (bỏ fingerprint skip) để gỡ đơn kẹt snapshot sai / thiếu giá.
    forceFullPost: doFull,
  };
}
