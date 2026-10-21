/** Origin Shein mặc định khi Tìm kiếm (us) + hay gặp. Cookie đăng nhập không nằm trong list xóa. */
export const SHEIN_DEFAULT_CACHE_ORIGINS = [
  'https://us.shein.com',
  'https://www.shein.com',
  'https://shein.com',
] as const;

/** `cache` (HTTP disk) không lọc theo origin — không gửi, tránh xóa cache cả trình duyệt. */
export const SHEIN_SITE_CACHE_DATA_TYPES: chrome.browsingData.DataTypeSet = {
  cacheStorage: true,
  indexedDB: true,
  localStorage: true,
  serviceWorkers: true,
};

export function isSheinHostname(hostname: string): boolean {
  const host = hostname.replace(/^www\./i, '').toLowerCase();
  return host === 'shein.com' || host.endsWith('.shein.com');
}

export function sheinOriginFromUrl(url?: string | null): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    if (!/^https?:$/i.test(parsed.protocol) || !isSheinHostname(parsed.hostname)) return null;
    return parsed.origin;
  } catch {
    return null;
  }
}

export function mergeSheinCacheOrigins(extra: Array<string | null | undefined>): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of [...SHEIN_DEFAULT_CACHE_ORIGINS, ...extra]) {
    const origin = String(raw || '').trim().replace(/\/+$/, '');
    if (!origin || seen.has(origin) || !sheinOriginFromUrl(`${origin}/`)) continue;
    seen.add(origin);
    out.push(origin);
  }
  return out;
}
