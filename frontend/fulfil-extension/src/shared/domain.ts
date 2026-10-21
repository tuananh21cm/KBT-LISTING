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

/** Tìm nguồn sản phẩm khớp với một host. */
export function findSourceByHost(
  host: string | undefined,
  sources: ProductSourceOption[],
): ProductSourceOption | undefined {
  if (!host) return undefined;
  return sources.find((s) => parseDomains(s.domains).some((p) => hostMatches(host, p)));
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
