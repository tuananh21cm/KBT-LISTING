import type { ProductSourceOption } from './types';

/** Tách chuỗi domains ("shein.com, us.shein.com") thành danh sách host chuẩn hóa. */
export function parseDomains(domains?: string): string[] {
  if (!domains) return [];
  return domains
    .split(/[\s,;\n]+/)
    .map((d) => d.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/\/.*$/, ''))
    .filter(Boolean);
}

/** Host có khớp với một pattern không (khớp chính nó hoặc subdomain). */
export function hostMatches(host: string, pattern: string): boolean {
  const h = host.toLowerCase();
  const p = pattern.toLowerCase();
  return h === p || h.endsWith('.' + p);
}

/**
 * Gợi ý domain khi `ProductSource.Domains` trống (seed cũ / admin chưa điền).
 * Chỉ dùng fallback — nếu Domains đã cấu hình thì khớp đúng list đó.
 */
const CODE_HOST_FALLBACKS: Record<string, string[]> = {
  shein: ['shein.com'],
  temu: ['temu.com'],
  cj: ['cjdropshipping.com'],
  amazon: ['amazon.com'],
  walmart: ['walmart.com'],
  ebay: ['ebay.com'],
  aliexpress: ['aliexpress.com'],
  etsy: ['etsy.com'],
  otto: ['otto.de'],
};

/** Nguồn có khớp host không (Domains trước; thiếu Domains thì đoán theo Code). */
export function sourceMatchesHost(source: ProductSourceOption, host: string): boolean {
  const h = host.toLowerCase();
  const configured = parseDomains(source.domains);
  if (configured.length > 0) {
    return configured.some((p) => hostMatches(h, p));
  }
  const code = (source.code || '').toLowerCase().replace(/[^a-z0-9]/g, '');
  if (!code) return false;
  if (h.includes(code)) return true;
  const hints = CODE_HOST_FALLBACKS[code];
  return !!hints?.some((p) => hostMatches(h, p));
}

/** Tìm nguồn sản phẩm khớp với một host. */
export function findSourceByHost(
  host: string | undefined,
  sources: ProductSourceOption[],
): ProductSourceOption | undefined {
  if (!host) return undefined;
  return sources.find((s) => sourceMatchesHost(s, host));
}

/** Tìm nguồn sản phẩm khớp với một URL đầy đủ. */
export function findSourceByUrl(
  url: string | undefined,
  sources: ProductSourceOption[],
): ProductSourceOption | undefined {
  if (!url) return undefined;
  try {
    return findSourceByHost(new URL(url).hostname, sources);
  } catch {
    return undefined;
  }
}
