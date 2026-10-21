/**
 * Dựng session SHEIN (header bảo mật + cookie + UA) để backend replay `get_order_info`
 * khi không có trình duyệt.
 *
 * Cookie chỉ đọc đủ được ở ngữ cảnh extension có quyền `cookies`: `AT`, `sessionID_shein`,
 * `cf_clearance` đều HttpOnly nên `document.cookie` trên trang SHEIN không thấy chúng.
 */
import {
  SHEIN_SYNC_CORE_HEADERS,
  normalizeSheinAccountEmail,
  type SheinCaptureState,
} from './sheinOrders';

/** Origin dùng khi không suy được từ capture / tab. */
export const SHEIN_DEFAULT_ORIGIN = 'https://us.shein.com';

/**
 * Đẩy lại session dù hash không đổi sau mốc này. Cookie giữ nguyên nhưng
 * `SheinSessionCapturedAt` cần tươi — job nền bỏ qua session quá cũ.
 */
export const SHEIN_SESSION_RESEND_INTERVAL_MS = 5 * 60 * 1000;

/** Body `POST /admin/fulfil/accounts/shein-session`. */
export interface SheinSessionPayload {
  accountEmail: string;
  origin: string;
  headers: Record<string, string>;
  cookie: string;
  capturedAt: string;
}

/** Dấu vết lần đẩy gần nhất, lưu ở `STORAGE_KEYS.sheinSessionPushed`. */
export interface SheinSessionPushedMarker {
  hash: string;
  at: number;
}

/** Cookie SHEIN dạng header liền (`a=1; b=2`), gồm cả cookie HttpOnly. */
export async function collectSheinCookieHeader(origin?: string): Promise<string> {
  if (!chrome?.cookies?.getAll) return '';

  // Lọc theo url khi biết storefront: cookie của `us.shein.com` và `ar.shein.com` khác nhau,
  // gộp cả hai vào một header là gửi lên SHEIN token của site không dùng.
  const query: chrome.cookies.GetAllDetails = { domain: 'shein.com' };
  if (origin) {
    try {
      query.url = new URL(origin).origin;
    } catch {
      // origin lỗi — giữ query theo domain
    }
  }

  try {
    const cookies = await chrome.cookies.getAll(query);
    return cookies
      .filter((c) => !!c.name && c.value != null && c.value !== '')
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((c) => `${c.name}=${c.value}`)
      .join('; ');
  } catch {
    return '';
  }
}

/** Origin storefront suy từ URL đã bắt, rồi tới tab SHEIN đang mở. */
export function resolveSheinOrigin(
  capture: SheinCaptureState | null | undefined,
  tabUrl?: string | null,
): string {
  for (const candidate of [capture?.listUrl, capture?.detailUrl, tabUrl]) {
    if (!candidate) continue;
    try {
      const u = new URL(candidate);
      if (/(^|\.)shein\.com$/i.test(u.hostname)) return u.origin;
    } catch {
      // bỏ qua URL lỗi
    }
  }
  return SHEIN_DEFAULT_ORIGIN;
}

/**
 * Payload gửi backend. Trả `null` khi thiếu thứ backend bắt buộc — POST một session không
 * dùng được chỉ làm `SheinSessionLastError` nhấp nháy và ghi đè cookie đang tốt.
 */
export function buildSheinSessionPayload(
  capture: SheinCaptureState | null | undefined,
  cookie: string,
  origin: string,
): SheinSessionPayload | null {
  const accountEmail = normalizeSheinAccountEmail(capture?.accountEmail);
  if (!accountEmail) return null;
  if (!cookie.trim()) return null;

  const headers: Record<string, string> = {};
  for (const [name, value] of Object.entries(capture?.headers ?? {})) {
    const key = name.trim().toLowerCase();
    if (!key || value == null || value === '') continue;
    // Cookie đi ở field riêng; content-* thuộc HttpContent nên backend từ chối.
    if (key === 'cookie' || key.startsWith('content-')) continue;
    headers[key] = String(value);
  }

  // UA thật để khớp `cf_clearance` (Cloudflare bind theo IP + UA). Không có UA thì backend
  // fallback một UA Chrome cố định và session dễ bị đẩy sang trang xác minh.
  const userAgent = capture?.userAgent?.trim();
  if (userAgent) headers['user-agent'] = userAgent;

  const missing = SHEIN_SYNC_CORE_HEADERS.filter((h) => !headers[h]);
  if (missing.length > 0) return null;

  const capturedAtMs = capture?.at && capture.at > 0 ? capture.at : Date.now();
  return {
    accountEmail,
    origin,
    headers,
    cookie,
    capturedAt: new Date(capturedAtMs).toISOString(),
  };
}

/** Hash rẻ (djb2) để biết session có đổi so với lần đẩy trước. */
export function hashSheinSession(payload: SheinSessionPayload): string {
  const canonical = [
    payload.accountEmail,
    payload.origin,
    Object.keys(payload.headers)
      .sort()
      .map((k) => `${k}=${payload.headers[k]}`)
      .join('&'),
    payload.cookie,
  ].join('|');

  let hash = 5381;
  for (let i = 0; i < canonical.length; i++) {
    hash = ((hash << 5) + hash + canonical.charCodeAt(i)) | 0;
  }
  return (hash >>> 0).toString(36);
}

/** True khi cần POST lại: chưa từng đẩy, hash đổi, hoặc quá hạn làm tươi. */
export function shouldPushSheinSession(
  hash: string,
  marker: SheinSessionPushedMarker | null | undefined,
  now = Date.now(),
): boolean {
  if (!marker || marker.hash !== hash) return true;
  return now - marker.at >= SHEIN_SESSION_RESEND_INTERVAL_MS;
}
