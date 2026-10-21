import { describe, expect, it } from 'vitest';
import {
  adoptServerSession,
  applyClosedSessionToToday,
  computeTodayOnlineSeconds,
  didServerRotateSession,
  ensureOnlineToday,
  formatOnlineDuration,
  sessionDurationSeconds,
  toClosePayload,
  toHeartbeatPayload,
  vnDateFromMs,
  type LocalOnlineSession,
  type LocalOnlineToday,
} from './onlineSession';

describe('onlineSession', () => {
  const base: LocalOnlineSession = {
    clientSessionId: 'abc123',
    externalShopId: '7496154235269843519',
    startedAtMs: 1_700_000_000_000,
    lastSeenAtMs: 1_700_000_600_000,
  };

  it('builds heartbeat ISO timestamps from local ms', () => {
    expect(toHeartbeatPayload(base)).toEqual({
      externalShopId: base.externalShopId,
      clientSessionId: base.clientSessionId,
      startedAt: new Date(base.startedAtMs).toISOString(),
      lastSeenAt: new Date(base.lastSeenAtMs).toISOString(),
    });
  });

  it('closes at lastSeen for browser-restart reconcile', () => {
    const payload = toClosePayload(base, 'BrowserRestartReconcile');
    expect(payload.reason).toBe('BrowserRestartReconcile');
    expect(payload.endedAt).toBe(new Date(base.lastSeenAtMs).toISOString());
    expect(payload.clientSessionId).toBe(base.clientSessionId);
  });

  it('adopts rotated clientSessionId from server day-boundary split', () => {
    const adopted = adoptServerSession(
      base,
      {
        clientSessionId: 'rotated999',
        startedAt: new Date(1_700_086_400_000).toISOString(),
        lastSeenAt: new Date(1_700_086_500_000).toISOString(),
        isOpen: true,
      },
      1_700_086_500_000,
    );
    expect(adopted?.clientSessionId).toBe('rotated999');
    expect(adopted?.startedAtMs).toBe(1_700_086_400_000);
    expect(adopted?.pendingClose).toBe(false);
  });

  it('clears local state when server says session is closed', () => {
    expect(
      adoptServerSession(base, {
        clientSessionId: base.clientSessionId,
        startedAt: new Date(base.startedAtMs).toISOString(),
        lastSeenAt: new Date(base.lastSeenAtMs).toISOString(),
        isOpen: false,
      }),
    ).toBeNull();
  });

  it('detects server session rotation', () => {
    const adopted: LocalOnlineSession = {
      ...base,
      clientSessionId: 'rotated999',
      startedAtMs: 1_700_086_400_000,
      lastSeenAtMs: 1_700_086_500_000,
    };
    expect(didServerRotateSession(base, adopted)).toBe(true);
    expect(didServerRotateSession(base, { ...base, lastSeenAtMs: base.lastSeenAtMs + 1 })).toBe(
      false,
    );
  });
});

describe('onlineToday accumulator', () => {
  // 2024-01-15 10:00 VN = 2024-01-15 03:00 UTC
  const dayMs = Date.parse('2024-01-15T03:00:00.000Z');
  const shopId = '7496154235269843519';

  it('formats vnDate in Asia/Ho_Chi_Minh', () => {
    expect(vnDateFromMs(dayMs)).toBe('2024-01-15');
    // Still 2024-01-14 in VN just before midnight VN
    expect(vnDateFromMs(Date.parse('2024-01-14T16:59:00.000Z'))).toBe('2024-01-14');
    expect(vnDateFromMs(Date.parse('2024-01-14T17:00:00.000Z'))).toBe('2024-01-15');
  });

  it('resets closedSeconds when VN day or shop changes', () => {
    const prev: LocalOnlineToday = {
      vnDate: '2024-01-14',
      closedSeconds: 900,
      externalShopId: shopId,
    };
    expect(ensureOnlineToday(prev, shopId, dayMs)).toEqual({
      vnDate: '2024-01-15',
      closedSeconds: 0,
      externalShopId: shopId,
    });
    expect(
      ensureOnlineToday(
        { vnDate: '2024-01-15', closedSeconds: 120, externalShopId: 'other' },
        shopId,
        dayMs,
      ).closedSeconds,
    ).toBe(0);
    expect(
      ensureOnlineToday(
        { vnDate: '2024-01-15', closedSeconds: 120, externalShopId: shopId },
        shopId,
        dayMs,
      ).closedSeconds,
    ).toBe(120);
  });

  it('credits closed session only when started on today VN', () => {
    const startedToday = Date.parse('2024-01-15T02:00:00.000Z');
    const endedToday = Date.parse('2024-01-15T02:10:00.000Z');
    const next = applyClosedSessionToToday(
      null,
      { externalShopId: shopId, startedAtMs: startedToday },
      endedToday,
      dayMs,
    );
    expect(next).toEqual({
      vnDate: '2024-01-15',
      externalShopId: shopId,
      closedSeconds: 600,
    });

    const startedYesterday = Date.parse('2024-01-14T16:00:00.000Z');
    const afterBoundary = applyClosedSessionToToday(
      next,
      { externalShopId: shopId, startedAtMs: startedYesterday },
      Date.parse('2024-01-14T17:00:00.000Z'),
      dayMs,
    );
    expect(afterBoundary.closedSeconds).toBe(600);
  });

  it('accumulates multiple closed sessions same day', () => {
    const first = applyClosedSessionToToday(
      null,
      { externalShopId: shopId, startedAtMs: dayMs },
      dayMs + 60_000,
      dayMs,
    );
    const second = applyClosedSessionToToday(
      first,
      { externalShopId: shopId, startedAtMs: dayMs + 120_000 },
      dayMs + 180_000,
      dayMs + 180_000,
    );
    expect(second.closedSeconds).toBe(120);
  });

  it('sessionDurationSeconds floors and clamps', () => {
    expect(sessionDurationSeconds(1000, 2500)).toBe(1);
    expect(sessionDurationSeconds(2000, 1000)).toBe(0);
  });

  it('computeTodayOnlineSeconds adds live open session', () => {
    const today: LocalOnlineToday = {
      vnDate: '2024-01-15',
      closedSeconds: 300,
      externalShopId: shopId,
    };
    const open: LocalOnlineSession = {
      clientSessionId: 'live1',
      externalShopId: shopId,
      startedAtMs: dayMs,
      lastSeenAtMs: dayMs,
    };
    const snap = computeTodayOnlineSeconds(today, open, dayMs + 90_000);
    expect(snap.isLive).toBe(true);
    expect(snap.seconds).toBe(390);
    expect(snap.vnDate).toBe('2024-01-15');

    const idle = computeTodayOnlineSeconds(today, null, dayMs);
    expect(idle.isLive).toBe(false);
    expect(idle.seconds).toBe(300);

    const pending = computeTodayOnlineSeconds(
      today,
      { ...open, pendingClose: true },
      dayMs + 90_000,
    );
    expect(pending.isLive).toBe(false);
    expect(pending.seconds).toBe(300);
  });

  it('formatOnlineDuration matches seller-web and live seconds', () => {
    expect(formatOnlineDuration(0)).toBe('—');
    expect(formatOnlineDuration(null)).toBe('—');
    expect(formatOnlineDuration(45)).toBe('45s');
    expect(formatOnlineDuration(60)).toBe('1m');
    expect(formatOnlineDuration(3600)).toBe('1h');
    expect(formatOnlineDuration(3690)).toBe('1h 1m');
    expect(formatOnlineDuration(3690, { live: true })).toBe('1h 1m 30s');
    expect(formatOnlineDuration(75, { live: true })).toBe('1m 15s');
    expect(formatOnlineDuration(9, { live: true })).toBe('9s');
  });
});
