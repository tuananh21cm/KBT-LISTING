import { STORAGE_KEYS, WTS_CACHE_MAX_SHOPS } from '../shared/config';
import type { WhatToSellTodayResult } from '../shared/messages';

export interface WhatToSellCacheEntry {
  savedAt: number;
  data: WhatToSellTodayResult;
}

type CacheMap = Record<string, WhatToSellCacheEntry>;

async function readCache(): Promise<CacheMap> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.whatToSellCache);
    const map = raw?.[STORAGE_KEYS.whatToSellCache];
    return map && typeof map === 'object' ? (map as CacheMap) : {};
  } catch {
    return {};
  }
}

/** Kết quả đã lưu của shop, `null` khi chưa phân tích lần nào. */
export async function loadWhatToSell(shopLocalId: string): Promise<WhatToSellCacheEntry | null> {
  if (!shopLocalId) return null;
  const map = await readCache();
  const entry = map[shopLocalId];
  return entry?.data ? entry : null;
}

/** Ghi đè kết quả của shop, giữ tối đa {@link WTS_CACHE_MAX_SHOPS} shop gần nhất. */
export async function saveWhatToSell(
  shopLocalId: string,
  data: WhatToSellTodayResult,
): Promise<void> {
  if (!shopLocalId) return;
  try {
    const map = await readCache();
    map[shopLocalId] = { savedAt: Date.now(), data };

    const keys = Object.keys(map);
    if (keys.length > WTS_CACHE_MAX_SHOPS) {
      keys
        .sort((a, b) => (map[b]?.savedAt ?? 0) - (map[a]?.savedAt ?? 0))
        .slice(WTS_CACHE_MAX_SHOPS)
        .forEach((k) => delete map[k]);
    }

    await chrome.storage.local.set({ [STORAGE_KEYS.whatToSellCache]: map });
  } catch {
    // Cache hỏng không được chặn luồng phân tích.
  }
}
