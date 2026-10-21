/**
 * Đẩy session SHEIN lên backend để job nền tự gọi `get_order_info` không cần trình duyệt.
 * Logic dựng payload / hash nằm ở `shared/sheinSession.ts` (side panel dùng lại phần đọc cookie).
 */
import { STORAGE_KEYS } from '../shared/config';
import type { SheinCaptureState } from '../shared/sheinOrders';
import {
  buildSheinSessionPayload,
  collectSheinCookieHeader,
  hashSheinSession,
  resolveSheinOrigin,
  shouldPushSheinSession,
  type SheinSessionPushedMarker,
} from '../shared/sheinSession';
import { postSheinSession } from './kinhbacApi';

/**
 * Đẩy session lên backend nếu có gì mới. Không bao giờ throw: lỗi ở đây không được làm
 * hỏng lượt Sync Order đang chạy.
 */
export async function pushSheinSessionIfChanged(
  capture: SheinCaptureState | null | undefined,
  tabUrl?: string | null,
): Promise<{ pushed: boolean; reason?: string }> {
  try {
    const origin = resolveSheinOrigin(capture, tabUrl);
    const cookie = await collectSheinCookieHeader(origin);
    const payload = buildSheinSessionPayload(capture, cookie, origin);
    if (!payload) return { pushed: false, reason: 'capture chưa đủ header/cookie/email' };

    const hash = hashSheinSession(payload);
    const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinSessionPushed);
    const marker = raw[STORAGE_KEYS.sheinSessionPushed] as SheinSessionPushedMarker | undefined;
    if (!shouldPushSheinSession(hash, marker)) return { pushed: false, reason: 'không đổi' };

    const saved = await postSheinSession(payload);
    await chrome.storage.local.set({
      [STORAGE_KEYS.sheinSessionPushed]: {
        hash,
        at: Date.now(),
      } satisfies SheinSessionPushedMarker,
    });
    return {
      pushed: true,
      reason: saved.ready
        ? `session lưu cho ${saved.accountName}`
        : `session lưu nhưng thiếu ${(saved.missingHeaders ?? []).join(', ') || 'cookie'}`,
    };
  } catch (error) {
    return { pushed: false, reason: (error as Error)?.message || 'lỗi không rõ' };
  }
}
