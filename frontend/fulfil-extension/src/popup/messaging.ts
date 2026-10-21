import type { ExtMessage } from '../shared/messages';

const STALE_SW_RELOAD_AT = 'kb_fulfil_sw_stale_reload_at';
const STALE_SW_RELOAD_COOLDOWN_MS = 15_000;

export const STALE_WORKER_HINT =
  'Service worker chưa cập nhật. Bấm Reload extension rồi mở lại panel.';

type RuntimeResponse = { ok?: boolean; message?: string };

function asRuntimeResponse(res: unknown): RuntimeResponse | null {
  if (!res || typeof res !== 'object') return null;
  return res as RuntimeResponse;
}

export function isStaleWorkerError(message?: string | null): boolean {
  if (!message) return false;
  return /Unknown message|Không nhận lệnh|Service worker chưa cập nhật|đang tải lại|Reload extension/i.test(
    message,
  );
}

function isStaleWorkerResponse(res: unknown): boolean {
  const r = asRuntimeResponse(res);
  return r?.ok === false && isStaleWorkerError(r.message);
}

/** Unpacked MV3: file dist đổi nhưng SW cũ vẫn sống → lệnh chat mới bị default `Unknown message.` */
async function reloadStaleWorkerOnce(): Promise<boolean> {
  try {
    const raw = await chrome.storage.local.get(STALE_SW_RELOAD_AT);
    const at = Number(raw[STALE_SW_RELOAD_AT] ?? 0);
    if (Number.isFinite(at) && Date.now() - at < STALE_SW_RELOAD_COOLDOWN_MS) {
      return false;
    }
    await chrome.storage.local.set({ [STALE_SW_RELOAD_AT]: Date.now() });
    chrome.runtime.reload();
    return true;
  } catch {
    return false;
  }
}

export function reloadExtension(): void {
  chrome.runtime.reload();
}

/** Gửi message tới background service worker và chờ response. */
export async function send<T = unknown>(message: ExtMessage): Promise<T> {
  let res: unknown;
  try {
    res = await chrome.runtime.sendMessage(message);
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    if (/Receiving end does not exist|message port closed/i.test(msg)) {
      const reloading = await reloadStaleWorkerOnce();
      throw new Error(
        reloading
          ? 'Extension đang tải lại. Mở lại panel sau vài giây.'
          : STALE_WORKER_HINT,
      );
    }
    throw err;
  }

  if (res == null) {
    throw new Error('Không nhận được phản hồi từ extension. Thử Reload extension.');
  }

  if (isStaleWorkerResponse(res)) {
    const reloading = await reloadStaleWorkerOnce();
    throw new Error(reloading ? 'Extension đang tải lại. Mở lại panel sau vài giây.' : STALE_WORKER_HINT);
  }

  return res as T;
}
