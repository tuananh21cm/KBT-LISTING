/**
 * Trích product id ổn định từ URL PDP theo luồng nguồn.
 * Key lưu local: `{flow}:{id}` (vd. shein:12345678).
 * Value: danh sách FourSellerShopId đã thêm product đó.
 */

/** Cache “đã thêm”: productKey → shop ids. */
export type AddedProductShopMap = Record<string, number[]>;

/** none = chưa shop nào; partial = thiếu shop trong selection; full = đủ mọi shop đang chọn. */
export type AddedStatus = 'none' | 'partial' | 'full';

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

/** Giới hạn số product key khi sync / mark (FIFO khi vượt). */
export const ADDED_PRODUCT_IDS_MAX = 5000;

/**
 * Giữ mark local sau khi Thêm thành công — draft sync debounce 12s + pull 4Seller
 * có thể chưa kịp ghi Listing.SourceUrl. Reconcile server không được xoá mark này.
 */
export const ADDED_LOCAL_MARK_GRACE_MS = 10 * 60 * 1000;

/** productKey → epoch ms lần mark local gần nhất. */
export type AddedProductMarkMeta = Record<string, number>;

/** Chuẩn hóa storage (map mới hoặc legacy string[] → map rỗng chờ sync). */
export function normalizeAddedProductShopMap(raw: unknown): AddedProductShopMap {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    return {};
  }
  const out: AddedProductShopMap = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof key !== 'string' || !key.includes(':')) continue;
    if (!Array.isArray(value)) continue;
    const shops = value
      .map((x) => (typeof x === 'number' ? x : Number(x)))
      .filter((n) => Number.isFinite(n) && n > 0);
    if (shops.length > 0) out[key] = [...new Set(shops)];
  }
  return out;
}

export function normalizeAddedProductMarkMeta(raw: unknown): AddedProductMarkMeta {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const out: AddedProductMarkMeta = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof key !== 'string' || !key.includes(':')) continue;
    const n = typeof value === 'number' ? value : Number(value);
    if (Number.isFinite(n) && n > 0) out[key] = n;
  }
  return out;
}

/**
 * Gộp map server với mark local còn trong grace / đang thêm.
 * Tránh ghi đè `kbt_added_product_ids` ngay sau post-html (listing chưa vào DB).
 */
export function reconcileAddedProductShopMap(opts: {
  server: AddedProductShopMap;
  local: AddedProductShopMap;
  markedAt: AddedProductMarkMeta;
  inFlightKeys?: Iterable<string>;
  now?: number;
  graceMs?: number;
}): { map: AddedProductShopMap; markedAt: AddedProductMarkMeta } {
  const now = opts.now ?? Date.now();
  const graceMs = opts.graceMs ?? ADDED_LOCAL_MARK_GRACE_MS;
  const inFlight = new Set(
    [...(opts.inFlightKeys ?? [])].filter((k) => typeof k === 'string' && k.includes(':')),
  );
  const merged: AddedProductShopMap = {};
  const nextMeta: AddedProductMarkMeta = {};
  const keys = new Set([...Object.keys(opts.server), ...Object.keys(opts.local)]);

  for (const key of keys) {
    const serverShops = opts.server[key] ?? [];
    const localShops = opts.local[key] ?? [];
    const markedAt = opts.markedAt[key] ?? 0;
    const flying = inFlight.has(key);
    const recent = markedAt > 0 && now - markedAt < graceMs;
    const legacyLocalOnly = markedAt === 0 && localShops.length > 0 && serverShops.length === 0;

    if (serverShops.length > 0 && (recent || flying) && localShops.length > 0) {
      merged[key] = [...new Set([...serverShops, ...localShops])];
      if (recent) nextMeta[key] = markedAt;
    } else if (serverShops.length > 0) {
      merged[key] = [...new Set(serverShops)];
    } else if ((recent || flying || legacyLocalOnly) && localShops.length > 0) {
      merged[key] = [...new Set(localShops)];
      nextMeta[key] = recent ? markedAt : now;
    }
  }

  const orderedKeys = Object.keys(merged);
  if (orderedKeys.length <= ADDED_PRODUCT_IDS_MAX) {
    return { map: merged, markedAt: nextMeta };
  }

  const trimmed: AddedProductShopMap = {};
  const trimmedMeta: AddedProductMarkMeta = {};
  for (const k of orderedKeys.slice(0, ADDED_PRODUCT_IDS_MAX)) {
    trimmed[k] = merged[k];
    if (nextMeta[k]) trimmedMeta[k] = nextMeta[k];
  }
  return { map: trimmed, markedAt: trimmedMeta };
}

export function getAddedStatus(
  productKey: string | undefined,
  map: AddedProductShopMap,
  selectedShopIds: number[],
): AddedStatus {
  if (!productKey || selectedShopIds.length === 0) return 'none';
  const have = map[productKey] ?? [];
  if (have.length === 0) return 'none';
  let hit = 0;
  for (const id of selectedShopIds) {
    if (have.includes(id)) hit += 1;
  }
  if (hit === 0) return 'none';
  if (hit >= selectedShopIds.length) return 'full';
  return 'partial';
}

/** Shop trong selection chưa có product. */
export function getMissingShopIds(
  productKey: string | undefined,
  map: AddedProductShopMap,
  selectedShopIds: number[],
): number[] {
  if (!productKey) return selectedShopIds;
  const have = map[productKey] ?? [];
  return selectedShopIds.filter((id) => !have.includes(id));
}

export function countAddedAmongSelected(
  productKey: string | undefined,
  map: AddedProductShopMap,
  selectedShopIds: number[],
): { hit: number; total: number } {
  const total = selectedShopIds.length;
  if (!productKey || total === 0) return { hit: 0, total };
  const have = map[productKey] ?? [];
  const hit = selectedShopIds.filter((id) => have.includes(id)).length;
  return { hit, total };
}

/** Union shop ids vào map; giữ tối đa ADDED_PRODUCT_IDS_MAX keys (newest first). */
export function unionShopsIntoMap(
  map: AddedProductShopMap,
  productKey: string,
  shopIds: number[],
): AddedProductShopMap {
  const valid = [...new Set(shopIds.filter((n) => Number.isFinite(n) && n > 0))];
  if (valid.length === 0) return map;
  const prev = map[productKey] ?? [];
  const merged = [...new Set([...valid, ...prev])];
  const next: AddedProductShopMap = { [productKey]: merged };
  for (const [k, v] of Object.entries(map)) {
    if (k === productKey) continue;
    next[k] = v;
  }
  const keys = Object.keys(next);
  if (keys.length <= ADDED_PRODUCT_IDS_MAX) return next;
  const trimmed: AddedProductShopMap = {};
  for (const k of keys.slice(0, ADDED_PRODUCT_IDS_MAX)) {
    trimmed[k] = next[k];
  }
  return trimmed;
}

/** Gỡ một shop khỏi key; xóa key nếu hết shop. */
export function removeShopFromMap(
  map: AddedProductShopMap,
  productKey: string,
  shopId: number,
): AddedProductShopMap {
  const prev = map[productKey];
  if (!prev) return map;
  const nextShops = prev.filter((id) => id !== shopId);
  const next: AddedProductShopMap = { ...map };
  if (nextShops.length === 0) delete next[productKey];
  else next[productKey] = nextShops;
  return next;
}
