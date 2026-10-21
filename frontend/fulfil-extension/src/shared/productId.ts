/**
 * Trích product id ổn định từ URL PDP theo luồng nguồn.
 * Key lưu local: `{flow}:{id}` (vd. shein:12345678).
 */
export function extractProductId(url: string, flow: string): string | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  const path = parsed.pathname;
  const q = parsed.searchParams;

  switch (flow) {
    case 'shein': {
      const m = path.match(/-p-(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'temu': {
      const goodsId = q.get('goods_id');
      if (goodsId?.trim()) return goodsId.trim();
      const m = path.match(/-g-(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'amazon': {
      const m = path.match(/\/(?:dp|gp\/product)\/([A-Z0-9]{8,})/i);
      return m?.[1]?.toUpperCase() ?? null;
    }
    case 'walmart': {
      const m = path.match(/\/ip\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'ebay': {
      const m = path.match(/\/itm\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'aliexpress': {
      const m = path.match(/\/item\/(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'etsy': {
      const m = path.match(/\/listing\/(\d+)/i);
      return m?.[1] ?? null;
    }
    case 'cj': {
      const m = path.match(/\/product\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? q.get('id')?.trim() ?? null;
    }
    case 'otto': {
      const m = path.match(/\/(?:p|produkt)\/(?:[^/]+\/)?(\d+)/i);
      return m?.[1] ?? null;
    }
    default: {
      const goodsId = q.get('goods_id') || q.get('product_id') || q.get('id');
      if (goodsId?.trim()) return goodsId.trim();
      const m =
        path.match(/-p-(\d+)/i) ||
        path.match(/-g-(\d+)/i) ||
        path.match(/\/(?:product|products|item|dp|goods|listing|ip)\/(?:[^/]+\/)?([A-Za-z0-9_-]+)/i);
      return m?.[1] ?? null;
    }
  }
}

/** Key lưu trong chrome.storage.local. */
export function makeAddedProductKey(flow: string, productId: string): string {
  return `${flow.toLowerCase()}:${productId}`;
}

/** Giới hạn số id giữ lại (FIFO khi vượt). */
export const ADDED_PRODUCT_IDS_MAX = 500;
