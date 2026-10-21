// MAIN-world content script — chạy trên *.shein.com trước khi trang tải xong.
// Nhiệm vụ: bắt header bảo mật + method/body của order/list + get_order_info
// để replay Sync Order. Nếu chưa có body, click Sync Order sẽ tự mở
// /user/orders/list để trang gọi API và script này bắt request.
//
// Script này KHÔNG import gì (build ra capture.js dạng classic) và chỉ postMessage
// + ghi sessionStorage bridge. Isolated sheinBridge.js / content.js nhận và lưu chrome.storage.

const CAPTURE_MSG = 'kb-fulfil-shein-capture';
const BRIDGE_KEY = 'kb_fulfil_shein_capture_bridge';

/** Header ưu tiên (SHEIN bảo mật). */
const HEADER_ALLOWLIST = new Set([
  'armortoken',
  'x-csrf-token',
  'smdeviceid',
  'x-gw-auth',
  'x-cs-random',
  'x-oest',
  'x-ad-flag',
  'webversion',
  'x-requested-with',
  'content-type',
  '_ver',
  '_lang',
  'authorization',
  'anti-in',
  'blackbox',
  'uat',
]);

/** Bỏ qua header trình duyệt / không cần replay. */
const HEADER_DENYLIST = new Set([
  'host',
  'origin',
  'referer',
  'connection',
  'content-length',
  'accept',
  'accept-encoding',
  'accept-language',
  'user-agent',
  'cookie',
  'sec-fetch-site',
  'sec-fetch-mode',
  'sec-fetch-dest',
  'sec-fetch-user',
  'sec-ch-ua',
  'sec-ch-ua-mobile',
  'sec-ch-ua-platform',
  'priority',
  'cache-control',
  'pragma',
]);

interface CapturePayload {
  source: typeof CAPTURE_MSG;
  url: string;
  kind: 'list' | 'detail' | 'other';
  method: string;
  headers: Record<string, string>;
  /** Request body gốc (string) — dùng lại trên toàn site khi Sync. */
  body?: string;
  /** Email tài khoản SHEIN (bắt từ profile API response). */
  accountEmail?: string;
  /** Id thành viên SHEIN (member_id / user_id…). */
  accountId?: string;
  /**
   * UA thật của trình duyệt. Không lấy từ header (Chrome không cho đọc `user-agent`
   * trong fetch/XHR init) mà từ `navigator.userAgent`, và cố tình nằm ngoài
   * allowlist header vì backend cần nó để khớp `cf_clearance` (Cloudflare bind IP + UA).
   */
  userAgent?: string;
  at: number;
}

interface BridgeState {
  headers: Record<string, string>;
  listUrl?: string;
  detailUrl?: string;
  listMethod?: string;
  listBody?: string;
  detailMethod?: string;
  detailBody?: string;
  accountEmail?: string;
  accountId?: string;
  userAgent?: string;
  at: number;
}

function classifyUrl(url: string): CapturePayload['kind'] | null {
  // Chỉ API order/list — không gồm trang HTML /user/orders/list
  let path = url;
  try {
    path = new URL(url, location.href).pathname;
  } catch {
    // keep raw
  }
  if (/\/user\/orders?\b/i.test(path)) {
    // trang storefront — bỏ qua
  } else if (
    /\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(path) ||
    /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
      path,
    )
  ) {
    return 'list';
  }
  // Detail (storefront): /api/orders/base/get_order_info/get  (± bff-api prefix)
  if (/\/(?:bff-api\/)?api\/orders\/base\/get_order_info/i.test(url)) return 'detail';
  // Legacy path (nếu SHEIN còn gọi)
  if (/\/bff-api\/order\/detail/i.test(url)) return 'detail';
  // Bắt header từ mọi /bff-api/order/* hoặc /api/order* khác (không lưu làm list/detail)
  if (/\/bff-api\/order\//i.test(url)) return 'other';
  if (/\/(?:bff-api\/)?api\/orders?\//i.test(url)) return 'other';
  // Profile / member — bắt response để lấy email tài khoản.
  if (
    /\/bff-api\/user(?:-api)?\//i.test(path) ||
    /\/user-api\//i.test(path) ||
    /\/(?:bff-api\/)?(?:user|member|account|customer)\//i.test(path) ||
    /get_user|user_info|member_info|account_info|personal_center|profile/i.test(path)
  ) {
    return 'other';
  }
  return null;
}

function isAccountInfoUrl(url: string): boolean {
  let path = url;
  try {
    path = new URL(url, location.href).pathname;
  } catch {
    // keep
  }
  return (
    /\/bff-api\/user(?:-api)?\//i.test(path) ||
    /\/user-api\//i.test(path) ||
    /\/(?:bff-api\/)?(?:user|member|account|customer)\//i.test(path) ||
    /get_user|user_info|member_info|account_info|personal_center|profile/i.test(path)
  );
}

const EMAIL_RE = /^[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}$/i;

function normalizeEmail(value: unknown): string | undefined {
  if (value == null) return undefined;
  const s = String(value).trim().toLowerCase();
  return s && EMAIL_RE.test(s) ? s : undefined;
}

function extractEmailFromJson(input: unknown, budget = 300): string | undefined {
  if (input == null || budget <= 0) return undefined;
  if (typeof input === 'string') {
    const t = input.trim();
    if (!t) return undefined;
    try {
      if (t.startsWith('{') || t.startsWith('[')) return extractEmailFromJson(JSON.parse(t), budget - 1);
    } catch {
      return normalizeEmail(t);
    }
    return normalizeEmail(t);
  }
  if (typeof input !== 'object') return undefined;
  if (Array.isArray(input)) {
    for (const item of input.slice(0, 30)) {
      const hit = extractEmailFromJson(item, budget - 1);
      if (hit) return hit;
    }
    return undefined;
  }
  const obj = input as Record<string, unknown>;
  for (const key of Object.keys(obj)) {
    const lower = key.toLowerCase();
    const val = obj[key];
    if (lower.includes('email') || lower === 'mail' || lower.includes('login')) {
      const hit = normalizeEmail(val);
      if (hit) return hit;
    }
  }
  for (const key of Object.keys(obj)) {
    const val = obj[key];
    if (val && typeof val === 'object') {
      const hit = extractEmailFromJson(val, budget - 1);
      if (hit) return hit;
    }
  }
  return undefined;
}

const ACCOUNT_ID_KEYS = new Set([
  'member_id',
  'memberid',
  'user_id',
  'userid',
  'account_id',
  'accountid',
  'uid',
  'member_uid',
  'memberuid',
]);

function normalizeAccountId(value: unknown): string | undefined {
  if (value == null) return undefined;
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || value <= 0) return undefined;
    return String(Math.trunc(value));
  }
  const s = String(value).trim();
  if (!s || EMAIL_RE.test(s)) return undefined;
  if (/^\d{4,}$/.test(s)) return s;
  if (/^[a-z0-9_-]{6,64}$/i.test(s) && !/^(true|false|null|undefined)$/i.test(s)) return s;
  return undefined;
}

function extractAccountIdFromJson(input: unknown, budget = 300): string | undefined {
  if (input == null || budget <= 0) return undefined;
  if (typeof input === 'string') {
    const t = input.trim();
    if (!t) return undefined;
    try {
      if (t.startsWith('{') || t.startsWith('[')) {
        return extractAccountIdFromJson(JSON.parse(t), budget - 1);
      }
    } catch {
      return normalizeAccountId(t);
    }
    return normalizeAccountId(t);
  }
  if (typeof input !== 'object') return undefined;
  if (Array.isArray(input)) {
    for (const item of input.slice(0, 30)) {
      const hit = extractAccountIdFromJson(item, budget - 1);
      if (hit) return hit;
    }
    return undefined;
  }
  const obj = input as Record<string, unknown>;
  for (const key of Object.keys(obj)) {
    const lower = key.toLowerCase().replace(/[^a-z0-9_]/g, '');
    if (!ACCOUNT_ID_KEYS.has(lower)) continue;
    const hit = normalizeAccountId(obj[key]);
    if (hit) return hit;
  }
  for (const key of Object.keys(obj)) {
    const val = obj[key];
    if (val && typeof val === 'object') {
      const hit = extractAccountIdFromJson(val, budget - 1);
      if (hit) return hit;
    }
  }
  return undefined;
}

function looksLikeListBody(body?: string): boolean {
  if (!body?.trim()) return false;
  try {
    const obj = JSON.parse(body) as Record<string, unknown>;
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return false;
    if ('typeStatus' in obj || 'type_status' in obj) return true;
    const hasPage = 'page' in obj || 'pageNum' in obj || 'current' in obj;
    const hasLimit = 'limit' in obj || 'pageSize' in obj || 'size' in obj;
    if (hasPage && hasLimit) return true;
    if ('orderStatusList' in obj || 'order_status_list' in obj) return true;
    return false;
  } catch {
    return false;
  }
}

function looksLikeDetailBody(body?: string): boolean {
  if (!body?.trim()) return false;
  try {
    const obj = JSON.parse(body) as Record<string, unknown>;
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return false;
    return ['billno', 'billNo', 'order_no', 'orderNo'].some((k) => {
      const v = obj[k];
      return typeof v === 'string' ? v.trim().length > 0 : v != null && String(v).trim().length > 0;
    });
  } catch {
    return false;
  }
}

function resolveKind(
  url: string,
  body?: string,
  hinted?: CapturePayload['kind'] | null,
): CapturePayload['kind'] | null {
  const fromUrl = hinted ?? classifyUrl(url);
  if (fromUrl === 'list' || fromUrl === 'detail') return fromUrl;
  if (looksLikeListBody(body)) return 'list';
  if (looksLikeDetailBody(body)) return 'detail';
  return fromUrl;
}

function toAbsoluteUrl(input: string): string {
  try {
    return new URL(input, location.href).href;
  } catch {
    return input;
  }
}

function normalizeHeaderName(name: string): string {
  return name.trim().toLowerCase();
}

function shouldKeepHeader(name: string): boolean {
  if (HEADER_DENYLIST.has(name)) return false;
  if (HEADER_ALLOWLIST.has(name)) return true;
  if (name.startsWith('x-') || name.startsWith('_')) return true;
  return false;
}

function pickHeaders(entries: Iterable<[string, string]>): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [rawName, value] of entries) {
    const name = normalizeHeaderName(rawName);
    if (!shouldKeepHeader(name)) continue;
    if (value == null || value === '') continue;
    out[name] = String(value);
  }
  return out;
}

function headersFromInit(init?: RequestInit, request?: Request): Record<string, string> {
  const collected: Array<[string, string]> = [];

  const push = (h: HeadersInit | Headers | undefined) => {
    if (!h) return;
    if (h instanceof Headers) {
      h.forEach((v, k) => collected.push([k, v]));
    } else if (Array.isArray(h)) {
      for (const pair of h) {
        if (Array.isArray(pair) && pair.length >= 2) collected.push([pair[0]!, pair[1]!]);
      }
    } else {
      for (const k of Object.keys(h)) collected.push([k, (h as Record<string, string>)[k]!]);
    }
  };

  if (request?.headers) push(request.headers);
  push(init?.headers);
  return pickHeaders(collected);
}

function decodeBytes(bytes: ArrayBuffer | ArrayBufferView): string | undefined {
  try {
    const view =
      bytes instanceof ArrayBuffer
        ? new Uint8Array(bytes)
        : new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    return new TextDecoder().decode(view);
  } catch {
    return undefined;
  }
}

async function bodyToString(body: BodyInit | null | undefined): Promise<string | undefined> {
  if (body == null) return undefined;
  if (typeof body === 'string') return body;
  if (body instanceof URLSearchParams) return body.toString();
  if (typeof Blob !== 'undefined' && body instanceof Blob) {
    try {
      return await body.text();
    } catch {
      return undefined;
    }
  }
  if (body instanceof ArrayBuffer) return decodeBytes(body);
  if (ArrayBuffer.isView(body)) return decodeBytes(body);
  return undefined;
}

function readBridge(): BridgeState {
  try {
    const raw = sessionStorage.getItem(BRIDGE_KEY);
    if (!raw) return { headers: {}, at: 0 };
    const parsed = JSON.parse(raw) as BridgeState;
    if (!parsed || typeof parsed !== 'object') return { headers: {}, at: 0 };
    return {
      headers: parsed.headers && typeof parsed.headers === 'object' ? parsed.headers : {},
      listUrl: parsed.listUrl,
      detailUrl: parsed.detailUrl,
      listMethod: parsed.listMethod,
      listBody: parsed.listBody,
      detailMethod: parsed.detailMethod,
      detailBody: parsed.detailBody,
      accountEmail: typeof parsed.accountEmail === 'string' ? parsed.accountEmail : undefined,
      accountId: typeof parsed.accountId === 'string' ? parsed.accountId : undefined,
      userAgent: typeof parsed.userAgent === 'string' ? parsed.userAgent : undefined,
      at: typeof parsed.at === 'number' ? parsed.at : 0,
    };
  } catch {
    return { headers: {}, at: 0 };
  }
}

function writeBridge(payload: CapturePayload): void {
  try {
    const prev = readBridge();
    const isListApi =
      payload.kind === 'list' &&
      !/\/user\/orders?\b/i.test(payload.url) &&
      (/\/bff-api\/order\/(?:list|order_list|get_order_list|query_list)\b/i.test(payload.url) ||
        /\/(?:bff-api\/)?api\/orders?(?:\/base)?\/(?:list|order_list|get_order_list|query_list)\b/i.test(
          payload.url,
        ));
    const next: BridgeState = {
      headers: { ...prev.headers, ...payload.headers },
      listUrl: isListApi ? payload.url : prev.listUrl,
      detailUrl: payload.kind === 'detail' ? payload.url : prev.detailUrl,
      listMethod: payload.kind === 'list' ? payload.method : prev.listMethod,
      listBody: payload.kind === 'list' && payload.body ? payload.body : prev.listBody,
      detailMethod: payload.kind === 'detail' ? payload.method : prev.detailMethod,
      detailBody: payload.kind === 'detail' && payload.body ? payload.body : prev.detailBody,
      accountEmail: payload.accountEmail || prev.accountEmail,
      accountId: payload.accountId || prev.accountId,
      userAgent: payload.userAgent || prev.userAgent,
      at: payload.at,
    };
    sessionStorage.setItem(BRIDGE_KEY, JSON.stringify(next));
  } catch {
    // private mode / quota — bỏ qua, vẫn postMessage
  }
}

function emit(
  url: string,
  kind: CapturePayload['kind'],
  method: string,
  headers: Record<string, string>,
  body?: string,
  accountEmail?: string,
  accountId?: string,
): void {
  if (Object.keys(headers).length === 0 && !body && !accountEmail && !accountId) return;
  const payload: CapturePayload = {
    source: CAPTURE_MSG,
    url: toAbsoluteUrl(url),
    kind,
    method: (method || 'GET').toUpperCase(),
    headers,
    body: body && body.length > 0 ? body : undefined,
    accountEmail: accountEmail || undefined,
    accountId: accountId || undefined,
    userAgent: navigator.userAgent || undefined,
    at: Date.now(),
  };
  writeBridge(payload);
  try {
    window.postMessage(payload, location.origin);
  } catch {
    // ignore
  }
}

async function captureFetchBody(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<{ method: string; body?: string }> {
  const request = input instanceof Request ? input : undefined;
  const method = (init?.method || request?.method || 'GET').toUpperCase();
  let body = await bodyToString(init?.body ?? null);
  if (body == null && request && method !== 'GET' && method !== 'HEAD') {
    try {
      body = await request.clone().text();
    } catch {
      body = undefined;
    }
  }
  return { method, body };
}

/* ---- Patch fetch ---- */
const originalFetch = window.fetch;
if (typeof originalFetch === 'function') {
  window.fetch = function patchedFetch(
    this: unknown,
    input: RequestInfo | URL,
    init?: RequestInit,
  ): Promise<Response> {
    try {
      const request = input instanceof Request ? input : undefined;
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.href
            : request?.url ?? String(input);
      const hinted = classifyUrl(url);
      if (hinted) {
        const headers = headersFromInit(init, request);
        // Đọc body async (clone Request / Blob) rồi emit — không chặn request gốc.
        void captureFetchBody(input, init).then(({ method, body }) => {
          const kind = resolveKind(url, body, hinted) ?? hinted;
          emit(url, kind, method, headers, body);
        });
      }
    } catch {
      // ignore capture errors — không được làm hỏng request gốc
    }
    const result = originalFetch.apply(
      this,
      arguments as unknown as [RequestInfo | URL, RequestInit?],
    );
    // Profile API: clone response lấy email + account id tài khoản.
    try {
      const request = input instanceof Request ? input : undefined;
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.href
            : request?.url ?? String(input);
      if (isAccountInfoUrl(url)) {
        void Promise.resolve(result).then(async (res) => {
          try {
            const clone = res.clone();
            const text = await clone.text();
            const email = extractEmailFromJson(text);
            const accountId = extractAccountIdFromJson(text);
            if (email || accountId) {
              const headers = headersFromInit(init, request);
              emit(
                url,
                'other',
                (init?.method || request?.method || 'GET').toUpperCase(),
                headers,
                undefined,
                email,
                accountId,
              );
            }
          } catch {
            // ignore
          }
        });
      }
    } catch {
      // ignore
    }
    return result;
  } as typeof window.fetch;
}

/* ---- Patch XMLHttpRequest ---- */
const XHR = window.XMLHttpRequest;
if (XHR) {
  const originalOpen = XHR.prototype.open;
  const originalSetHeader = XHR.prototype.setRequestHeader;
  const originalSend = XHR.prototype.send;

  interface CapturingXhr extends XMLHttpRequest {
    __kbUrl?: string;
    __kbMethod?: string;
    __kbHeaders?: Record<string, string>;
  }

  XHR.prototype.open = function (
    this: CapturingXhr,
    method: string,
    url: string | URL,
    ..._rest: unknown[]
  ) {
    try {
      this.__kbUrl = typeof url === 'string' ? url : url.href;
      this.__kbMethod = (method || 'GET').toUpperCase();
      this.__kbHeaders = {};
    } catch {
      // ignore
    }
    // eslint-disable-next-line prefer-rest-params
    return originalOpen.apply(this, arguments as never);
  } as typeof XHR.prototype.open;

  XHR.prototype.setRequestHeader = function (
    this: CapturingXhr,
    name: string,
    value: string,
  ) {
    try {
      const key = normalizeHeaderName(name);
      if (shouldKeepHeader(key) && this.__kbHeaders) {
        this.__kbHeaders[key] = String(value);
      }
    } catch {
      // ignore
    }
    // eslint-disable-next-line prefer-rest-params
    return originalSetHeader.apply(this, arguments as never);
  } as typeof XHR.prototype.setRequestHeader;

  XHR.prototype.send = function (this: CapturingXhr, body?: Document | XMLHttpRequestBodyInit | null) {
    try {
      const url = this.__kbUrl ?? '';
      const hinted = classifyUrl(url);
      if (hinted && this.__kbHeaders) {
        void bodyToString(body as BodyInit | null | undefined).then((bodyStr) => {
          const kind = resolveKind(url, bodyStr, hinted) ?? hinted;
          emit(url, kind, this.__kbMethod || 'GET', this.__kbHeaders || {}, bodyStr);
        });
      }
      if (isAccountInfoUrl(url) && this.__kbHeaders) {
        this.addEventListener('load', () => {
          try {
            const email = extractEmailFromJson(this.responseText);
            const accountId = extractAccountIdFromJson(this.responseText);
            if (email || accountId) {
              emit(
                url,
                'other',
                this.__kbMethod || 'GET',
                this.__kbHeaders || {},
                undefined,
                email,
                accountId,
              );
            }
          } catch {
            // ignore
          }
        });
      }
    } catch {
      // ignore
    }
    // eslint-disable-next-line prefer-rest-params
    return originalSend.apply(this, arguments as never);
  } as typeof XHR.prototype.send;
}
