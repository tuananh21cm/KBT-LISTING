import { describe, expect, it } from 'vitest';
import {
  SHEIN_LIST_MAX_PAGES_ALL,
  SHEIN_LIST_MAX_PAGES_NEW,
  SHEIN_LIST_SHALLOW,
  sheinListFetchOptionsForMode,
  sheinListFetchOptionsFull,
  sheinListFetchOptionsManualAll,
  sheinListFetchOptionsShallow,
} from './sheinOrders';

/**
 * Cửa sổ list SHEIN theo chế độ sync.
 *
 * Trước đây nút Sync thủ công luôn gọi ManualAll kể cả khi Settings = «Chỉ đơn mới», nên kéo
 * toàn bộ lịch sử giống mode all trong khi backend chỉ chạy incremental — lãng phí và lệch với
 * ý nghĩa của setting. Các test dưới khóa hợp đồng: manual all = full, manual new = lookback,
 * auto nông = page 20.
 */
describe('sheinListFetchOptions by sync mode', () => {
  it('manual all: full list, no lookback cut', () => {
    const opts = sheinListFetchOptionsManualAll();
    expect(opts.pageSize).toBe(50);
    expect(opts.maxPages).toBe(SHEIN_LIST_MAX_PAGES_ALL);
    expect(opts.lookbackDays).toBeUndefined();
  });

  it('manual new / auto deep: lookback-bounded list', () => {
    const opts = sheinListFetchOptionsFull(18);
    expect(opts.pageSize).toBe(50);
    expect(opts.maxPages).toBe(SHEIN_LIST_MAX_PAGES_NEW);
    expect(opts.lookbackDays).toBe(18);
  });

  it('auto shallow: pageSize 20 × 1 page', () => {
    const opts = sheinListFetchOptionsShallow();
    expect(opts.pageSize).toBe(SHEIN_LIST_SHALLOW.pageSize);
    expect(opts.maxPages).toBe(SHEIN_LIST_SHALLOW.maxPages);
    expect(opts.pageSize).toBe(20);
    expect(opts.maxPages).toBe(1);
    expect(opts.lookbackDays).toBeUndefined();
  });

  it('sheinListFetchOptionsForMode maps all → full and new → lookback', () => {
    const all = sheinListFetchOptionsForMode('all', 14);
    expect(all.maxPages).toBe(SHEIN_LIST_MAX_PAGES_ALL);
    expect(all.lookbackDays).toBeUndefined();

    const neu = sheinListFetchOptionsForMode('new', 14);
    expect(neu.maxPages).toBe(SHEIN_LIST_MAX_PAGES_NEW);
    expect(neu.lookbackDays).toBe(14);
  });
});
