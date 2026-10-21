import { describe, expect, it } from 'vitest';
import type { SheinCaptureState } from './sheinOrders';
import {
  SHEIN_DEFAULT_ORIGIN,
  SHEIN_SESSION_RESEND_INTERVAL_MS,
  buildSheinSessionPayload,
  hashSheinSession,
  resolveSheinOrigin,
  shouldPushSheinSession,
} from './sheinSession';

const COOKIE = 'AT=abc; cf_clearance=xyz; sessionID_shein=123';

/** Capture đủ điều kiện đẩy: 4 header bắt buộc + email + UA. */
function readyCapture(overrides: Partial<SheinCaptureState> = {}): SheinCaptureState {
  return {
    headers: {
      armortoken: 'armor-1',
      smdeviceid: 'device-1',
      'x-csrf-token': 'csrf-1',
      'x-gw-auth': 'gw-1',
    },
    accountEmail: 'Buyer@Example.com',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/131.0.0.0',
    at: Date.parse('2026-09-02T04:00:00.000Z'),
    ...overrides,
  };
}

describe('buildSheinSessionPayload', () => {
  it('builds a payload with normalized email and captured UA', () => {
    const payload = buildSheinSessionPayload(readyCapture(), COOKIE, SHEIN_DEFAULT_ORIGIN);

    expect(payload).not.toBeNull();
    expect(payload!.accountEmail).toBe('buyer@example.com');
    expect(payload!.cookie).toBe(COOKIE);
    expect(payload!.origin).toBe(SHEIN_DEFAULT_ORIGIN);
    expect(payload!.capturedAt).toBe('2026-09-02T04:00:00.000Z');
    // UA thật phải đi kèm: cf_clearance bị Cloudflare khoá theo IP + UA.
    expect(payload!.headers['user-agent']).toBe(
      'Mozilla/5.0 (Windows NT 10.0) Chrome/131.0.0.0',
    );
  });

  it('returns null without a cookie', () => {
    // Header bảo mật một mình không gọi được API — POST chỉ ghi đè cookie đang tốt.
    expect(buildSheinSessionPayload(readyCapture(), '', SHEIN_DEFAULT_ORIGIN)).toBeNull();
    expect(buildSheinSessionPayload(readyCapture(), '   ', SHEIN_DEFAULT_ORIGIN)).toBeNull();
  });

  it('returns null without an account email', () => {
    // Backend map tài khoản fulfil bằng email; không có email thì không biết lưu vào đâu.
    expect(
      buildSheinSessionPayload(
        readyCapture({ accountEmail: undefined }),
        COOKIE,
        SHEIN_DEFAULT_ORIGIN,
      ),
    ).toBeNull();
  });

  it('returns null when a required header is missing', () => {
    const capture = readyCapture({
      headers: { armortoken: 'a', smdeviceid: 'b', 'x-csrf-token': 'c' },
    });

    expect(buildSheinSessionPayload(capture, COOKIE, SHEIN_DEFAULT_ORIGIN)).toBeNull();
  });

  it('drops cookie and content-* headers', () => {
    const capture = readyCapture({
      headers: {
        ...readyCapture().headers,
        cookie: 'should-not-be-sent',
        'content-type': 'application/json',
        'content-length': '42',
      },
    });

    const payload = buildSheinSessionPayload(capture, COOKIE, SHEIN_DEFAULT_ORIGIN)!;

    expect(payload.headers).not.toHaveProperty('cookie');
    expect(payload.headers).not.toHaveProperty('content-type');
    expect(payload.headers).not.toHaveProperty('content-length');
  });

  it('omits user-agent when the capture never saw one', () => {
    const payload = buildSheinSessionPayload(
      readyCapture({ userAgent: undefined }),
      COOKIE,
      SHEIN_DEFAULT_ORIGIN,
    )!;

    expect(payload.headers).not.toHaveProperty('user-agent');
  });
});

describe('resolveSheinOrigin', () => {
  it('prefers the captured list URL', () => {
    const origin = resolveSheinOrigin(
      readyCapture({ listUrl: 'https://ar.shein.com/bff-api/order/list?page=1' }),
      'https://us.shein.com/user/orders/list',
    );

    expect(origin).toBe('https://ar.shein.com');
  });

  it('falls back to the detail URL then the tab URL', () => {
    expect(
      resolveSheinOrigin(
        readyCapture({ detailUrl: 'https://de.shein.com/api/orders/base/get_order_info/get' }),
      ),
    ).toBe('https://de.shein.com');

    expect(resolveSheinOrigin(readyCapture(), 'https://uk.shein.com/user/orders/list')).toBe(
      'https://uk.shein.com',
    );
  });

  it('ignores non-shein hosts and bad URLs', () => {
    expect(resolveSheinOrigin(readyCapture({ listUrl: 'https://evil.example.com/x' }))).toBe(
      SHEIN_DEFAULT_ORIGIN,
    );
    expect(resolveSheinOrigin(readyCapture({ listUrl: 'not-a-url' }))).toBe(SHEIN_DEFAULT_ORIGIN);
    expect(resolveSheinOrigin(null)).toBe(SHEIN_DEFAULT_ORIGIN);
  });
});

describe('hashSheinSession', () => {
  const payload = buildSheinSessionPayload(readyCapture(), COOKIE, SHEIN_DEFAULT_ORIGIN)!;

  it('is stable across header ordering', () => {
    const reordered = buildSheinSessionPayload(
      readyCapture({
        headers: {
          'x-gw-auth': 'gw-1',
          'x-csrf-token': 'csrf-1',
          smdeviceid: 'device-1',
          armortoken: 'armor-1',
        },
      }),
      COOKIE,
      SHEIN_DEFAULT_ORIGIN,
    )!;

    expect(hashSheinSession(reordered)).toBe(hashSheinSession(payload));
  });

  it('changes when the cookie changes', () => {
    const rotated = { ...payload, cookie: `${COOKIE}; extra=1` };
    expect(hashSheinSession(rotated)).not.toBe(hashSheinSession(payload));
  });

  it('changes when a header value changes', () => {
    const rotated = { ...payload, headers: { ...payload.headers, armortoken: 'armor-2' } };
    expect(hashSheinSession(rotated)).not.toBe(hashSheinSession(payload));
  });
});

describe('shouldPushSheinSession', () => {
  const now = Date.parse('2026-09-02T04:00:00.000Z');

  it('pushes when nothing was ever pushed', () => {
    expect(shouldPushSheinSession('h1', null, now)).toBe(true);
    expect(shouldPushSheinSession('h1', undefined, now)).toBe(true);
  });

  it('pushes when the hash changed', () => {
    expect(shouldPushSheinSession('h2', { hash: 'h1', at: now }, now)).toBe(true);
  });

  it('skips an unchanged session inside the refresh window', () => {
    expect(shouldPushSheinSession('h1', { hash: 'h1', at: now - 60_000 }, now)).toBe(false);
  });

  it('re-pushes an unchanged session once the window elapses', () => {
    // capturedAt phải tươi: vòng quét nền bỏ qua session quá cũ dù cookie vẫn đúng.
    const marker = { hash: 'h1', at: now - SHEIN_SESSION_RESEND_INTERVAL_MS };
    expect(shouldPushSheinSession('h1', marker, now)).toBe(true);
  });
});
