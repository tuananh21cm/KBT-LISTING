import { describe, expect, it } from 'vitest';
import { shortSheinBadgeStatus } from './sheinBadgeStatus';

describe('shortSheinBadgeStatus', () => {
  it('chỉ hiện Sync… khi syncing=true, không latch theo message Đang sync…', () => {
    const stuck = shortSheinBadgeStatus({
      syncing: false,
      preparing: false,
      captureReady: true,
      syncStatus: { ok: true, message: 'Đang sync chi tiết 5/20 đơn…' },
    });
    expect(stuck?.label).toBe('Ready');

    const running = shortSheinBadgeStatus({
      syncing: true,
      preparing: false,
      captureReady: true,
      syncStatus: { ok: true, message: 'Đang sync chi tiết 5/20 đơn…' },
    });
    expect(running?.label).toBe('Sync…');
  });

  it('«Đang sync — thử lại sau.» khi không in-flight → Busy, không Sync… mãi', () => {
    const badge = shortSheinBadgeStatus({
      syncing: false,
      preparing: false,
      captureReady: true,
      syncStatus: { ok: false, message: 'Đang sync — thử lại sau.' },
    });
    expect(badge?.label).toBe('Busy');
    expect(badge?.tone).toBe('warn');
  });

  it('Auto bỏ qua / Khớp → Skip / Done', () => {
    expect(
      shortSheinBadgeStatus({
        syncing: false,
        preparing: false,
        captureReady: true,
        syncStatus: {
          ok: true,
          message: 'Auto bỏ qua (snapshot không đổi) · đã kiểm tra 20',
        },
      })?.label,
    ).toBe('Skip');

    expect(
      shortSheinBadgeStatus({
        syncing: false,
        preparing: false,
        captureReady: true,
        syncStatus: {
          ok: true,
          fetched: 12,
          matched: 10,
          message: 'Khớp 10/12 · track local +2 · phí +1',
        },
      })?.label,
    ).toBe('Done');
  });
});
