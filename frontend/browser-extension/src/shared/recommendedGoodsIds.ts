import { STORAGE_KEYS } from './config';

/** FIFO cap — click-from-Search ids kept so PDP add still counts as recommended. */
export const RECOMMENDED_GOODS_IDS_CAP = 200;

/** Click / Search list older than this is not treated as recommended (tránh PDP thêm tay bị gán nhầm). */
export const RECOMMENDED_GOODS_TTL_MS = 24 * 60 * 60 * 1000;

export type ListingAddedFrom = 'recommended' | 'manual';

export type RecommendedGoodsEntry = { id: string; at: number };

export function parseRecommendedGoodsEntries(
  raw: unknown,
  now = Date.now(),
): RecommendedGoodsEntry[] {
  const cutoff = now - RECOMMENDED_GOODS_TTL_MS;
  const out: RecommendedGoodsEntry[] = [];
  const seen = new Set<string>();
  const push = (id: string, at: number) => {
    const gid = String(id || '').trim();
    if (!gid || seen.has(gid) || !Number.isFinite(at) || at < cutoff) return;
    seen.add(gid);
    out.push({ id: gid, at });
  };
  if (!Array.isArray(raw)) return out;
  for (const item of raw) {
    if (typeof item === 'string') {
      push(item, now);
      continue;
    }
    if (!item || typeof item !== 'object') continue;
    const row = item as { id?: unknown; at?: unknown };
    const at = typeof row.at === 'number' ? row.at : Number(row.at);
    push(String(row.id ?? ''), Number.isFinite(at) && at > 0 ? at : now);
  }
  return out;
}

export function parseGoodsIdList(raw: unknown): string[] {
  return parseRecommendedGoodsEntries(raw).map((row) => row.id);
}

/** Merge goodsIds into `kbt_recommended_goods_ids` (TTL + cap FIFO). */
export async function rememberRecommendedGoodsIds(ids: string[]): Promise<void> {
  const now = Date.now();
  const incoming = [...new Set(ids.map((id) => String(id || '').trim()).filter(Boolean))];
  if (incoming.length === 0) return;
  const raw = await chrome.storage.local.get(STORAGE_KEYS.recommendedGoodsIds);
  const prev = parseRecommendedGoodsEntries(raw[STORAGE_KEYS.recommendedGoodsIds], now);
  const keep = prev.filter((row) => !incoming.includes(row.id));
  const merged = [...keep, ...incoming.map((id) => ({ id, at: now }))];
  const capped =
    merged.length > RECOMMENDED_GOODS_IDS_CAP
      ? merged.slice(merged.length - RECOMMENDED_GOODS_IDS_CAP)
      : merged;
  await chrome.storage.local.set({ [STORAGE_KEYS.recommendedGoodsIds]: capped });
}

export function isRecommendedGoodsId(
  goodsId: string | null | undefined,
  searchGoodsIds: Iterable<string>,
  remembered: Iterable<string>,
): boolean {
  const id = String(goodsId ?? '').trim();
  if (!id) return false;
  for (const row of searchGoodsIds) {
    if (String(row || '').trim() === id) return true;
  }
  for (const row of remembered) {
    if (String(row || '').trim() === id) return true;
  }
  return false;
}

export function isSearchStateFresh(updatedAt: number | undefined, now = Date.now()): boolean {
  const at = Number(updatedAt) || 0;
  return at > 0 && now - at <= RECOMMENDED_GOODS_TTL_MS;
}
