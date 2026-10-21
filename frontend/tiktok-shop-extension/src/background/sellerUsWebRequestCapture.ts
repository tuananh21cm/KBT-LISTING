import { SELLER_COMMON_GET_PATH, SELLER_US_HOST, STORAGE_KEYS } from '../shared/config';
import type { TikTokCaptureState } from '../shared/types';

function isSellerCommonGetUrl(url: string): boolean {
  try {
    const u = new URL(url);
    if (!u.hostname.endsWith(SELLER_US_HOST) && u.hostname !== SELLER_US_HOST) return false;
    return u.pathname.includes(SELLER_COMMON_GET_PATH);
  } catch {
    return /seller-us\.tiktok\.com.*\/api\/v3\/seller\/common\/get/i.test(url);
  }
}

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
  return ['authorization', 'content-type'].includes(n);
}

let persistChain: Promise<void> = Promise.resolve();

function persistHeaders(url: string, headers: Record<string, string>): Promise<void> {
  persistChain = persistChain
    .then(async () => {
      const raw = await chrome.storage.local.get(STORAGE_KEYS.capture);
      const prev = (raw[STORAGE_KEYS.capture] as TikTokCaptureState | undefined) ?? null;
      const next: TikTokCaptureState = {
        headers: { ...(prev?.headers || {}), ...headers },
        at: Date.now(),
        lastUrl: url,
        shopOk: prev?.shopOk,
      };
      await chrome.storage.local.set({ [STORAGE_KEYS.capture]: next });
    })
    .catch(() => undefined);
  return persistChain;
}

let installed = false;

/** Bắt header request thật tới seller/common/get (không nhồi Cookie). */
export function installSellerUsWebRequestCapture(): void {
  if (installed) return;
  installed = true;

  const filter: chrome.webRequest.RequestFilter = {
    urls: [`*://${SELLER_US_HOST}/*`],
  };

  const onHeaders = (details: chrome.webRequest.WebRequestHeadersDetails) => {
    if (!isSellerCommonGetUrl(details.url)) return;
    const headers: Record<string, string> = {};
    for (const h of details.requestHeaders || []) {
      if (!h.name || h.value == null || h.value === '') continue;
      const name = h.name.toLowerCase();
      if (!shouldKeepHeader(name)) continue;
      headers[name] = h.value;
    }
    if (Object.keys(headers).length === 0) {
      // Vẫn ghi lastUrl để biết request đã chạy.
      void persistHeaders(details.url, {});
      return;
    }
    void persistHeaders(details.url, headers);
  };

  try {
    chrome.webRequest.onSendHeaders.addListener(onHeaders, filter, [
      'requestHeaders',
      'extraHeaders',
    ]);
  } catch {
    chrome.webRequest.onSendHeaders.addListener(onHeaders, filter, ['requestHeaders']);
  }
}
