import type { SheinSyncResponse } from './messages';

/**
 * Nhãn ngắn trên badge source-bar (Reload / Ready / Sync…).
 * Full message giữ trong `title` để hover xem chi tiết.
 *
 * `syncing` phải là cờ in-flight thật (local click ∪ `kb_fulfil_shein_sync_running`).
 * Không suy Sync… từ message «Đang sync…» — message progress còn sót sau khi SW chết
 * hoặc «Đang sync — thử lại sau.» sẽ làm badge kẹt mãi.
 */
export function shortSheinBadgeStatus(opts: {
  syncing: boolean;
  preparing: boolean;
  captureReady: boolean;
  syncStatus: SheinSyncResponse | null;
}): { label: string; tone: 'ok' | 'warn' | 'err'; title?: string } | null {
  const msg = opts.syncStatus?.message?.trim() || '';
  const title = msg || undefined;
  const lower = msg.toLowerCase();

  if (opts.syncing) {
    return { label: 'Sync…', tone: 'warn', title: title || 'Đang sync…' };
  }
  if (opts.preparing) {
    if (/reload|đang reload/i.test(msg)) {
      return { label: 'Reload…', tone: 'warn', title };
    }
    if (/mở trang|đang mở/i.test(msg)) {
      return { label: 'Open…', tone: 'warn', title };
    }
    if (/chờ|bắt header|sẵn sàng|chuẩn bị/i.test(msg)) {
      return { label: 'Wait…', tone: 'warn', title };
    }
    return { label: 'Prep…', tone: 'warn', title: title || 'Đang chuẩn bị session…' };
  }
  if (opts.syncStatus && opts.syncStatus.ok === false) {
    if (/đang sync|thử lại sau|đang chạy/i.test(msg)) {
      return { label: 'Busy', tone: 'warn', title };
    }
    if (/login|đăng nhập/i.test(msg)) return { label: 'Login', tone: 'err', title };
    if (/timeout|quá lâu|f5/i.test(msg)) return { label: 'Retry', tone: 'err', title };
    return { label: 'Fail', tone: 'err', title };
  }
  if (opts.syncStatus?.ok && msg) {
    if (/sẵn sàng|ready/i.test(msg) || /có thể sync/i.test(msg)) {
      return { label: 'Ready', tone: 'ok', title };
    }
    if (/bỏ qua|snapshot/i.test(msg)) {
      return { label: 'Skip', tone: 'ok', title };
    }
    if (
      /khớp|cập nhật|đã lấy|matched|updated|đã kiểm tra|chưa có đơn/i.test(lower) ||
      opts.syncStatus.fetched != null
    ) {
      return { label: 'Done', tone: 'ok', title };
    }
    // Progress còn sót trong storage sau khi sync đã kết thúc → không giữ Sync…/Wait….
    if (/đang (reload|mở|chờ|bắt|lấy|chuẩn bị|sync)/i.test(msg)) {
      // fall through
    } else {
      return { label: 'Done', tone: 'ok', title };
    }
  }
  if (opts.captureReady) {
    return { label: 'Ready', tone: 'ok', title: title || 'Session SHEIN sẵn sàng' };
  }
  return null;
}
