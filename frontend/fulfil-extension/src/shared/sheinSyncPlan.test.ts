import { describe, expect, it } from 'vitest';
import {
  SHEIN_AUTO_FULL_EVERY_N,
  SHEIN_LIST_MAX_PAGES_ALL,
  SHEIN_LIST_MAX_PAGES_NEW,
  SHEIN_LIST_SHALLOW,
} from './sheinOrders';
import { resolveSheinSyncPlan } from './sheinSyncPlan';

describe('resolveSheinSyncPlan', () => {
  it('manual all: full list, updateMode all, forceFullPost', () => {
    const plan = resolveSheinSyncPlan({
      isManual: true,
      updateMode: 'all',
      lookbackDays: 14,
    });
    expect(plan.listDepth).toBe('manual_all');
    expect(plan.effectiveUpdateMode).toBe('all');
    expect(plan.forceFullPost).toBe(true);
    expect(plan.listOpts.maxPages).toBe(SHEIN_LIST_MAX_PAGES_ALL);
    expect(plan.listOpts.lookbackDays).toBeUndefined();
  });

  it('manual new: lookback list, updateMode new, forceFullPost', () => {
    const plan = resolveSheinSyncPlan({
      isManual: true,
      updateMode: 'new',
      lookbackDays: 18,
    });
    expect(plan.listDepth).toBe('manual_new');
    expect(plan.effectiveUpdateMode).toBe('new');
    expect(plan.forceFullPost).toBe(true);
    expect(plan.listOpts.maxPages).toBe(SHEIN_LIST_MAX_PAGES_NEW);
    expect(plan.listOpts.lookbackDays).toBe(18);
  });

  it('auto shallow: page 20 × 1, always updateMode new', () => {
    const plan = resolveSheinSyncPlan({
      isManual: false,
      updateMode: 'all', // Settings all bị bỏ qua
      lookbackDays: 14,
      autoSyncCount: 1,
    });
    expect(plan.listDepth).toBe('auto_shallow');
    expect(plan.effectiveUpdateMode).toBe('new');
    expect(plan.forceFullPost).toBe(false);
    expect(plan.listOpts.pageSize).toBe(SHEIN_LIST_SHALLOW.pageSize);
    expect(plan.listOpts.maxPages).toBe(1);
  });

  it('auto full every N: lookback list, forceFullPost, still updateMode new', () => {
    const plan = resolveSheinSyncPlan({
      isManual: false,
      updateMode: 'new',
      lookbackDays: 14,
      autoSyncCount: SHEIN_AUTO_FULL_EVERY_N,
    });
    expect(plan.listDepth).toBe('auto_full');
    expect(plan.effectiveUpdateMode).toBe('new');
    expect(plan.forceFullPost).toBe(true);
    expect(plan.listOpts.maxPages).toBe(SHEIN_LIST_MAX_PAGES_NEW);
    expect(plan.listOpts.lookbackDays).toBe(14);
  });
});
