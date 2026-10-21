import { describe, expect, it } from 'vitest';
import { shouldTrackSellerPageRequest } from './sellerPageIdle';

describe('shouldTrackSellerPageRequest', () => {
  it('đếm API seller', () => {
    expect(
      shouldTrackSellerPageRequest('https://seller-us.tiktok.com/api/v1/product/manage/list'),
    ).toBe(true);
  });

  it('bỏ telemetry / log', () => {
    expect(shouldTrackSellerPageRequest('https://seller-us.tiktok.com/api/v1/slardar/batch')).toBe(
      false,
    );
    expect(shouldTrackSellerPageRequest('https://seller-us.tiktok.com/monitor/collect')).toBe(false);
    expect(shouldTrackSellerPageRequest('chrome-extension://abc/content.js')).toBe(false);
  });
});
