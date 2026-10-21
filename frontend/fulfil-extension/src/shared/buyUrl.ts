/**
 * Build URL mua hàng từ SellerSku / SourceUrl.
 * SHEIN SKU dạng `461067711-I0mpr1v994kdem` → `https://us.shein.com/a-p-461067711.html`
 */

export interface ParsedSellerSku {
  /** Phần trước dấu `-` (goods id). */
  goodsId: string;
  /** Phần sau dấu `-` (sku / variant code), nếu có. */
  skuCode?: string;
  raw: string;
}

export function parseSellerSku(sellerSku?: string | null): ParsedSellerSku | null {
  const raw = sellerSku?.trim();
  if (!raw) return null;
  const m = raw.match(/^(\d+)(?:-(.+))?$/);
  if (!m?.[1]) return null;
  return {
    goodsId: m[1],
    skuCode: m[2]?.trim() || undefined,
    raw,
  };
}

function sheinHostFromUrl(sourceUrl?: string | null): string {
  if (sourceUrl) {
    try {
      const u = new URL(sourceUrl);
      if (/shein\./i.test(u.hostname)) return u.hostname;
    } catch {
      // ignore
    }
  }
  return 'us.shein.com';
}

/**
 * URL mở mua: SHEIN ưu tiên a-p-{goodsId}.html từ SKU; fallback sourceUrl/listingUrl.
 */
export function resolveBuyUrl(item: {
  sellerSku?: string | null;
  sourceUrl?: string | null;
  listingUrl?: string | null;
}): string | undefined {
  const parsed = parseSellerSku(item.sellerSku);
  const fallback = (item.sourceUrl || item.listingUrl)?.trim() || undefined;

  const clearlyNonShein =
    !!fallback &&
    !/shein\./i.test(fallback) &&
    !(item.sourceUrl && /shein\./i.test(item.sourceUrl));

  // SKU `digits-suffix` → SHEIN deep link (trừ khi URL nguồn rõ ràng khác nền tảng).
  if (parsed && !clearlyNonShein) {
    const host = sheinHostFromUrl(item.sourceUrl || item.listingUrl);
    return `https://${host}/a-p-${parsed.goodsId}.html`;
  }

  return fallback;
}

/** Trích nhãn size từ VariantAttr (vd. "Size:M / Color:Black", "M,Black"). */
export function extractSizeLabel(variantAttr?: string | null): string | null {
  if (!variantAttr?.trim()) return null;
  const v = variantAttr.trim();

  const sizeEq = v.match(/size\s*[=:]\s*([^,/|;]+)/i);
  if (sizeEq?.[1]) return sizeEq[1].trim();

  const sizeWord = v.match(/(?:^|[|,;/])\s*size\s+([^,/|;]+)/i);
  if (sizeWord?.[1]) return sizeWord[1].trim();

  // Một token ngắn kiểu XS/S/M/L/XL/XXL/3XL…
  const alone = v.match(/\b((?:XX?S)|(?:XX?L)|(?:[2-5]XL)|(?:X?L)|(?:M)|(?:S))\b/i);
  if (alone?.[1] && v.length <= 40) return alone[1].toUpperCase();

  return null;
}

/** Trích color từ VariantAttr nếu có. */
export function extractColorLabel(variantAttr?: string | null): string | null {
  if (!variantAttr?.trim()) return null;
  const colorEq = variantAttr.match(/colou?r\s*[=:]\s*([^,/|;]+)/i);
  return colorEq?.[1]?.trim() || null;
}
