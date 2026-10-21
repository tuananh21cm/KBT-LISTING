/**
 * Base URL của API kinhbac (kèm /api/v1).
 * Dev/local: `.env.development` → `http://localhost:5000/api/v1` (`npm run build:local` / `npm run dev`).
 * Production: fallback hoặc `.env.production`.
 */
export const API_BASE_URL: string =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/+$/, '') ??
  'https://4seller-api.kbt.global/api/v1';

/** SignalR hub `/hubs/notifications` từ API base (`…/api/v1` → origin). */
export function notificationsHubUrl(apiBaseUrl: string = API_BASE_URL): string {
  const base = apiBaseUrl.replace(/\/+$/, '');
  if (base.endsWith('/api/v1')) {
    return `${base.slice(0, -'/api/v1'.length)}/hubs/notifications`;
  }
  if (base.endsWith('/api')) {
    return `${base.slice(0, -'/api'.length)}/hubs/notifications`;
  }
  return `${base}/hubs/notifications`;
}

/** Khóa lưu trong chrome.storage.local (prefix riêng để không đụng scraper). */
export const STORAGE_KEYS = {
  auth: 'kb_fulfil_auth',
  productSources: 'kb_fulfil_product_sources',
  /** Cache danh sách đơn cần mua gần nhất (cho content overlay). */
  ordersCache: 'kb_fulfil_orders_cache',
  /** Item đang active match với tab (nếu có). */
  activeMatch: 'kb_fulfil_active_match',
  /**
   * User vừa Nhả / Đã mua → không auto-ghim lại theo URL PDP cho đến khi click đơn mới.
   * `persistActiveMatch(null)` bật; `persistActiveMatch(match)` tắt.
   */
  activeMatchDismissed: 'kb_fulfil_active_match_dismissed',
  /** Pending size/SKU select sau khi mở SHEIN PDP. */
  pendingSelect: 'kb_fulfil_pending_select',
  /** Kết quả khớp size/màu trên PDP (content → side panel cảnh báo). */
  variantFit: 'kb_fulfil_variant_fit',
  /** Tổng thanh toán DOM checkout SHEIN (content → side panel tính profit). */
  checkoutCost: 'kb_fulfil_checkout_cost',
  /** Header + request body SHEIN đã bắt (capture MAIN world) để replay order API trên toàn site. */
  sheinCapture: 'kb_fulfil_shein_capture',
  /** Đang warm-up session SHEIN sau login / CTA Chuẩn bị session. */
  sheinSessionPreparing: 'kb_fulfil_shein_session_preparing',
  /**
   * Dấu vết lần đẩy session SHEIN (cookie + header) lên backend gần nhất: `{ hash, at }`.
   * Chỉ POST lại khi hash đổi hoặc quá hạn — cookie không đổi thì đẩy lại là request rỗng.
   */
  sheinSessionPushed: 'kb_fulfil_shein_session_pushed',
  /** Trạng thái lần Sync Order gần nhất (hiển thị dưới nút). */
  sheinSyncStatus: 'kb_fulfil_shein_sync_status',
  /**
   * Sync Order / Auto Sync đang chạy trên background (true khi in-flight).
   * Side panel badge Sync… chỉ theo cờ này — không latch theo message «Đang sync…» còn sót.
   */
  sheinSyncRunning: 'kb_fulfil_shein_sync_running',
  /** Checkbox Auto Sync trên side panel (bật/tắt sync chu kỳ — chạy ngầm qua chrome.alarms). */
  sheinAutoSync: 'kb_fulfil_shein_auto_sync',
  /** Timestamp (ms) lần Auto Sync kế tiếp — panel đếm ngược; background ghi khi arm alarm. */
  sheinNextAutoSyncAt: 'kb_fulfil_shein_next_auto_sync_at',
  /** Bộ lọc tuổi đơn (giờ) trên hàng đợi — 0 = tất cả; mặc định 2. */
  waitHoursFilter: 'kb_fulfil_wait_hours_filter',
  /**
   * Định danh phiên Chrome profile (UUID). `chrome.storage.local` tách theo profile —
   * mỗi profile tự có sessionId riêng; mở lại panel không sinh mới.
   */
  sessionId: 'kb_fulfil_session_id',
  /** Đếm số lần Auto Sync — cứ N lần thì full lookback. */
  sheinAutoSyncCount: 'kb_fulfil_shein_auto_sync_count',
  /**
   * Snapshot fingerprint list SHEIN gần nhất (billno → fingerprint).
   * Auto Sync so sánh trước khi POST shein-sync — không đổi thì bỏ qua DB.
   */
  sheinListSnapshot: 'kb_fulfil_shein_list_snapshot',
  /** Billnos thiếu order detail (cost chưa rich) — ưu tiên đầu hàng lần sync sau. */
  sheinIncompleteBillnos: 'kb_fulfil_shein_incomplete_billnos',
} as const;

/**
 * Thời gian tối đa giữ phiên đăng nhập kinhbac trên extension (từ lần login).
 * Hết hạn → xóa auth, yêu cầu đăng nhập lại.
 */
export const AUTH_SESSION_MAX_DAYS = 30;
export const AUTH_SESSION_MAX_MS = AUTH_SESSION_MAX_DAYS * 24 * 60 * 60 * 1000;

/** Header gửi kèm mọi API call — định danh Chrome profile phiên fulfil. */
export const FULFIL_SESSION_HEADER = 'X-Fulfil-Session';

/**
 * Lấy hoặc tạo sessionId bền trong chrome.storage.local (một UUID / Chrome profile).
 */
export async function getOrCreateSessionId(): Promise<string> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.sessionId);
  const existing = raw[STORAGE_KEYS.sessionId];
  if (typeof existing === 'string' && existing.trim().length >= 32) {
    return existing.trim();
  }
  const id =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}-${Math.random()
          .toString(16)
          .slice(2)}`;
  await chrome.storage.local.set({ [STORAGE_KEYS.sessionId]: id });
  return id;
}

export const BRAND_NAME = 'KBT Fulfil';

/** Version extension đang chạy (đồng bộ `manifest.json`). */
export const APP_VERSION = chrome.runtime.getManifest().version;
