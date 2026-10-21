import { findSourceByUrl } from './domain';
import type { ProductSourceOption } from './types';

/** Kết quả nhận diện luồng scrape theo URL trang hiện tại. */
export interface ProductPageDetection {
  /** Đủ điều kiện bật nút Thêm sản phẩm. */
  ok: boolean;
  /** Nguồn khớp domain (nếu có). */
  source?: ProductSourceOption;
  /** Mã luồng nội bộ: shein | temu | amazon | … */
  flow?: string;
  /** Lý do không active (cho UI). */
  reason?: string;
}

/**
 * Nhận diện trang chi tiết sản phẩm theo URL + danh sách nguồn đã cấu hình.
 * Domain phải khớp ProductSource.Domains; path/query phải là PDP (không phải home/category).
 */
export function detectProductPage(
  url: string | undefined,
  sources: ProductSourceOption[],
): ProductPageDetection {
  if (!url || !/^https?:/i.test(url)) {
    return { ok: false, reason: 'Không có trang http(s) đang mở.' };
  }

  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return { ok: false, reason: 'URL không hợp lệ.' };
  }

  const source = findSourceByUrl(url, sources);
  if (!source) {
    return { ok: false, reason: 'Trang hiện tại không thuộc nguồn hàng đã cấu hình.' };
  }

  const flow = resolveFlow(parsed, source);
  if (!isProductDetailUrl(parsed, flow)) {
    return {
      ok: false,
      source,
      flow,
      reason: `Đang ở ${source.name} nhưng chưa phải trang sản phẩm.`,
    };
  }

  return { ok: true, source, flow };
}

function resolveFlow(url: URL, source: ProductSourceOption): string {
  const host = url.hostname.toLowerCase();
  const code = (source.code || '').toLowerCase();
  if (code.includes('shein') || host.includes('shein')) return 'shein';
  if (code.includes('temu') || host.includes('temu')) return 'temu';
  if (code.includes('amazon') || host.includes('amazon')) return 'amazon';
  if (code.includes('walmart') || host.includes('walmart')) return 'walmart';
  if (code.includes('ebay') || host.includes('ebay')) return 'ebay';
  if (code.includes('aliexpress') || host.includes('aliexpress')) return 'aliexpress';
  if (code.includes('etsy') || host.includes('etsy')) return 'etsy';
  if (code.includes('cj') || host.includes('cjdropshipping')) return 'cj';
  if (code.includes('otto') || host.includes('otto.de')) return 'otto';
  return code || 'generic';
}

/** Kiểm tra path/query có phải trang chi tiết sản phẩm của luồng đã nhận diện. */
export function isProductDetailUrl(url: URL, flow: string): boolean {
  const path = url.pathname.toLowerCase();
  switch (flow) {
    case 'shein':
      // https://us.shein.com/...-p-123456.html
      return /-p-\d+/i.test(path);
    case 'temu':
      // goods.html?goods_id=… hoặc …-g-123.html
      return (
        (path.includes('goods.html') && !!url.searchParams.get('goods_id')) ||
        /-g-\d+(\.html)?$/i.test(path)
      );
    case 'amazon':
      return /\/(dp|gp\/product)\//i.test(path);
    case 'walmart':
      return /\/ip\//i.test(path);
    case 'ebay':
      return /\/itm(\/|$)/i.test(path);
    case 'aliexpress':
      return /\/item\//i.test(path);
    case 'etsy':
      return /\/listing\//i.test(path);
    case 'cj':
      return /\/product\//i.test(path);
    case 'otto':
      return /\/p\//i.test(path) || /\/produkt\//i.test(path);
    default:
      return isGenericProductPath(url);
  }
}

/**
 * Cart / checkout / payment — vẫn thuộc luồng mua hàng (giữ active match).
 * Ví dụ: https://us.shein.com/cart , https://us.shein.com/checkout?
 */
export function isPurchaseFlowUrl(url: string | undefined, flow?: string): boolean {
  if (!url) return false;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  const path = parsed.pathname.toLowerCase();
  const generic =
    /\/(cart|checkout|checkouts?|bag|basket|payment|pay|order-confirm|order\/confirm|orders\/confirm|thank[-_]?you|success)(\/|$|\?)/i.test(
      path,
    ) ||
    /\/cart(\/|$)/i.test(path) ||
    /\/checkout/i.test(path);

  if (generic) return true;

  if (flow === 'shein') {
    return (
      path.includes('/cart') ||
      path.includes('/checkout') ||
      path.includes('/pay') ||
      path.includes('/order')
    );
  }
  return false;
}

/** Trang checkout — đọc giá thực trả để tính profit (khác cart). */
export function isCheckoutUrl(url: string | undefined): boolean {
  if (!url) return false;
  try {
    return /\/checkout/i.test(new URL(url).pathname);
  } catch {
    return /\/checkout/i.test(url);
  }
}

function isGenericProductPath(url: URL): boolean {
  const path = url.pathname.toLowerCase();
  if (/-p-\d+/i.test(path) || /-g-\d+/i.test(path)) return true;
  if (/\/(product|products|item|dp|goods|listing|ip)(\/|$)/i.test(path)) return true;
  if (url.searchParams.has('goods_id') || url.searchParams.has('product_id')) return true;
  return false;
}
