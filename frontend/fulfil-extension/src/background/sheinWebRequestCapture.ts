/**
 * Backup capture SHEIN order/list + get_order_info qua chrome.webRequest.
 * Không phụ thuộc patch fetch/XHR trên trang (SHEIN có thể bypass MAIN-world hook).
 */
import { STORAGE_KEYS } from '../shared/config';
import {
  classifySheinOrderUrl,
  mergeSheinCapture,
  normalizeSheinBody,
  resolveSheinCaptureKind,
  type SheinCapturePayload,
  type SheinCaptureState,
} from '../shared/sheinOrders';

const SHEIN_CAPTURE_MSG = 'kb-fulfil-shein-capture' as const;

/** URL order API gần nhất (chẩn đoán khi fail). */
const LAST_ORDER_URLS_KEY = 'kb_fulfil_shein_last_order_urls';

interface PendingReq {
  url: string;
  method: string;
  body?: string;
  kind: 'list' | 'detail' | 'other';
}

const pending = new Map<string, PendingReq>();

/** Serialize storage writes — tránh race mất listBody khi onBeforeRequest + onSendHeaders chồng nhau. */
let persistChain: Promise<void> = Promise.resolve();

function shouldKeepHeader(name: string): boolean {
  const n = name.toLowerCase();
  if (
    n === 'host' ||
    n === 'origin' ||
    n === 'referer' ||
    n === 'connection' ||
    n === 'content-length' ||
    n === 'accept' ||
    n === 'accept-encoding' ||
    n === 'accept-language' ||
    n === 'user-agent' ||
    n === 'cookie' ||
    n.startsWith('sec-') ||
    n === 'priority' ||
    n === 'cache-control' ||
    n === 'pragma'
  ) {
    return false;
  }
  if (n.startsWith('x-') || n.startsWith('_')) return true;
  return [
    'armortoken',
    'smdeviceid',
    'webversion',
    'content-type',
    'authorization',
    'anti-in',
    'blackbox',
    'uat',
  ].includes(n);
}

function decodeRequestBody(body?: chrome.webRequest.WebRequestBody | null): string | undefined {
  if (!body) return undefined;
  if (body.raw && body.raw.length > 0) {
    const parts: string[] = [];
    for (const part of body.raw) {
      if (!part.bytes) continue;
      try {
        parts.push(new TextDecoder().decode(new Uint8Array(part.bytes)));
      } catch {
        // ignore chunk
      }
    }
    return normalizeSheinBody(parts.join(''));
  }
  if (body.formData) {
    try {
      // SHEIN order/list thường JSON; formData ít gặp — vẫn lưu để debug/replay thô.
      // formData value là string[] — flatten 1 phần tử về scalar cho gần body gốc.
      const flat: Record<string, unknown> = {};
      for (const [key, values] of Object.entries(body.formData)) {
        if (!Array.isArray(values)) {
          flat[key] = values;
          continue;
        }
        flat[key] = values.length === 1 ? values[0] : values;
      }
      return normalizeSheinBody(JSON.stringify(flat));
    } catch {
      return undefined;
    }
  }
  return undefined;
}

async function rememberOrderUrl(url: string, kind: string): Promise<void> {
  try {
    const raw = await chrome.storage.local.get(LAST_ORDER_URLS_KEY);
    const prev = (raw[LAST_ORDER_URLS_KEY] as string[] | undefined) ?? [];
    const next = [`${kind}:${url}`, ...prev.filter((u) => u !== `${kind}:${url}`)].slice(0, 8);
    await chrome.storage.local.set({ [LAST_ORDER_URLS_KEY]: next });
  } catch {
    // ignore
  }
}

export async function readLastSheinOrderUrls(): Promise<string[]> {
  try {
    const raw = await chrome.storage.local.get(LAST_ORDER_URLS_KEY);
    return (raw[LAST_ORDER_URLS_KEY] as string[] | undefined) ?? [];
  } catch {
    return [];
  }
}

function persistPayload(payload: SheinCapturePayload): Promise<void> {
  persistChain = persistChain
    .then(async () => {
      const raw = await chrome.storage.local.get(STORAGE_KEYS.sheinCapture);
      const prev = raw[STORAGE_KEYS.sheinCapture] as SheinCaptureState | undefined;
      await chrome.storage.local.set({
        [STORAGE_KEYS.sheinCapture]: mergeSheinCapture(prev, payload),
      });
    })
    .catch(() => {
      // giữ chain sống sau lỗi tạm thời
    });
  return persistChain;
}

async function commit(
  kind: PendingReq['kind'],
  url: string,
  method: string,
  headers: Record<string, string>,
  body?: string,
): Promise<void> {
  const normalizedBody = normalizeSheinBody(body);
  const resolved =
    resolveSheinCaptureKind(url, normalizedBody, kind) ?? kind;
  await rememberOrderUrl(url, resolved);
  if (Object.keys(headers).length === 0 && !normalizedBody && resolved === 'other') return;
  await persistPayload({
    source: SHEIN_CAPTURE_MSG,
    url,
    kind: resolved,
    method: (method || 'GET').toUpperCase(),
    headers,
    body: normalizedBody,
    at: Date.now(),
  });
}

/** Gọi một lần khi service worker khởi động. */
let webRequestCaptureInstalled = false;

export function installSheinWebRequestCapture(): void {
  if (webRequestCaptureInstalled) return;
  webRequestCaptureInstalled = true;

  const filter: chrome.webRequest.RequestFilter = {
    urls: ['*://*.shein.com/*', '*://shein.com/*'],
  };

  chrome.webRequest.onBeforeRequest.addListener(
    (details) => {
      const hinted = classifySheinOrderUrl(details.url);
      if (!hinted) return;
      const body = decodeRequestBody(details.requestBody);
      const kind = resolveSheinCaptureKind(details.url, body, hinted) ?? hinted;
      pending.set(String(details.requestId), {
        url: details.url,
        method: details.method,
        body,
        kind,
      });
      // Lưu sớm URL/body — header sẽ bổ sung ở onSendHeaders.
      void commit(kind, details.url, details.method, {}, body);
    },
    filter,
    ['requestBody'],
  );

  const onHeaders = (
    details: chrome.webRequest.WebRequestHeadersDetails,
  ) => {
    const hinted = classifySheinOrderUrl(details.url);
    if (!hinted) return;
    const headers: Record<string, string> = {};
    for (const h of details.requestHeaders || []) {
      if (!h.name || h.value == null || h.value === '') continue;
      const name = h.name.toLowerCase();
      if (!shouldKeepHeader(name)) continue;
      headers[name] = h.value;
    }
    const prev = pending.get(String(details.requestId));
    pending.delete(String(details.requestId));
    const body = prev?.body;
    const kind =
      resolveSheinCaptureKind(details.url, body, prev?.kind ?? hinted) ??
      prev?.kind ??
      hinted;
    void commit(
      kind,
      details.url,
      details.method || prev?.method || 'GET',
      headers,
      body,
    );
  };

  // extraHeaders: cần để đọc một số header tùy biến trên Chrome.
  try {
    chrome.webRequest.onSendHeaders.addListener(onHeaders, filter, [
      'requestHeaders',
      'extraHeaders',
    ]);
  } catch {
    chrome.webRequest.onSendHeaders.addListener(onHeaders, filter, ['requestHeaders']);
  }
}
