/** Local + server online-session payload shapes for seller-us tab presence. */

export type ShopOnlineCloseReason =
  | 'TabClosed'
  | 'BrowserRestartReconcile'
  | 'HeartbeatTimeout'
  | 'DayBoundary'
  | 'Explicit';

/** Persisted in `chrome.storage.local` (`kb_tts_online_session`). */
export interface LocalOnlineSession {
  clientSessionId: string;
  externalShopId: string;
  startedAtMs: number;
  lastSeenAtMs: number;
  pendingClose?: boolean;
  closeReason?: ShopOnlineCloseReason;
}

/** Persisted day accumulator (`kb_tts_online_today`) — closed sessions only. */
export interface LocalOnlineToday {
  /** `yyyy-MM-dd` in Asia/Ho_Chi_Minh. */
  vnDate: string;
  closedSeconds: number;
  externalShopId: string;
}

export interface ShopOnlineHeartbeatPayload {
  externalShopId: string;
  clientSessionId: string;
  startedAt: string;
  lastSeenAt: string;
}

export interface ShopOnlineClosePayload {
  externalShopId: string;
  clientSessionId: string;
  endedAt: string;
  reason: ShopOnlineCloseReason;
}

export interface ShopOnlineSessionResponse {
  id: string;
  shopId: string;
  externalShopId: string;
  clientSessionId: string;
  startedAt: string;
  lastSeenAt: string;
  endedAt?: string | null;
  closeReason?: string | null;
  isOpen: boolean;
}

export interface TodayOnlineSnapshot {
  seconds: number;
  isLive: boolean;
  vnDate: string;
  externalShopId: string | null;
}

const VN_TZ = 'Asia/Ho_Chi_Minh';

/** Mint a new client session id (hex uuid without dashes). */
export function newClientSessionId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID().replace(/-/g, '');
  }
  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2, 10)}`;
}

/** Build heartbeat body from local session. */
export function toHeartbeatPayload(session: LocalOnlineSession): ShopOnlineHeartbeatPayload {
  return {
    externalShopId: session.externalShopId,
    clientSessionId: session.clientSessionId,
    startedAt: new Date(session.startedAtMs).toISOString(),
    lastSeenAt: new Date(session.lastSeenAtMs).toISOString(),
  };
}

/**
 * Build close body for a pending / restart reconcile session.
 * Uses lastSeenAt as endedAt so a sudden browser kill still has a credible end watermark.
 */
export function toClosePayload(
  session: LocalOnlineSession,
  reason: ShopOnlineCloseReason,
): ShopOnlineClosePayload {
  const endedAtMs = Math.max(session.startedAtMs, session.lastSeenAtMs);
  return {
    externalShopId: session.externalShopId,
    clientSessionId: session.clientSessionId,
    endedAt: new Date(endedAtMs).toISOString(),
    reason,
  };
}

/** Apply server clientSessionId rotation (day-boundary split) onto local state. */
export function adoptServerSession(
  local: LocalOnlineSession,
  server: Pick<ShopOnlineSessionResponse, 'clientSessionId' | 'startedAt' | 'lastSeenAt' | 'isOpen'>,
  nowMs = Date.now(),
): LocalOnlineSession | null {
  if (!server.isOpen) {
    return null;
  }

  const startedAtMs = Date.parse(server.startedAt);
  const lastSeenAtMs = Date.parse(server.lastSeenAt);
  return {
    clientSessionId: server.clientSessionId || local.clientSessionId,
    externalShopId: local.externalShopId,
    startedAtMs: Number.isFinite(startedAtMs) ? startedAtMs : local.startedAtMs,
    lastSeenAtMs: Number.isFinite(lastSeenAtMs)
      ? Math.max(lastSeenAtMs, nowMs)
      : Math.max(local.lastSeenAtMs, nowMs),
    pendingClose: false,
  };
}

/** Calendar date `yyyy-MM-dd` in Vietnam timezone for an epoch ms. */
export function vnDateFromMs(ms: number): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: VN_TZ,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(ms));
}

export function vnDateNow(nowMs = Date.now()): string {
  return vnDateFromMs(nowMs);
}

export function sessionDurationSeconds(startedAtMs: number, endedAtMs: number): number {
  if (!Number.isFinite(startedAtMs) || !Number.isFinite(endedAtMs)) return 0;
  return Math.max(0, Math.floor((endedAtMs - startedAtMs) / 1000));
}

/** Reset bucket when VN day or shop changes. */
export function ensureOnlineToday(
  prev: LocalOnlineToday | null | undefined,
  externalShopId: string,
  nowMs = Date.now(),
): LocalOnlineToday {
  const today = vnDateNow(nowMs);
  const shopId = externalShopId.trim();
  if (prev && prev.vnDate === today && prev.externalShopId === shopId) {
    return {
      vnDate: prev.vnDate,
      externalShopId: prev.externalShopId,
      closedSeconds: Math.max(0, Math.floor(prev.closedSeconds) || 0),
    };
  }
  return { vnDate: today, closedSeconds: 0, externalShopId: shopId };
}

/**
 * Credit a closed session into today's accumulator.
 * Only adds duration when the session started on today's VN date (DayBoundary leftovers stay out).
 */
export function applyClosedSessionToToday(
  prev: LocalOnlineToday | null | undefined,
  session: Pick<LocalOnlineSession, 'externalShopId' | 'startedAtMs'>,
  endedAtMs: number,
  nowMs = Date.now(),
): LocalOnlineToday {
  const shopId = session.externalShopId.trim();
  const base = ensureOnlineToday(prev, shopId, nowMs);
  const sessionDay = vnDateFromMs(session.startedAtMs);
  if (sessionDay !== base.vnDate) {
    return base;
  }
  const add = sessionDurationSeconds(session.startedAtMs, endedAtMs);
  if (add <= 0) return base;
  return { ...base, closedSeconds: base.closedSeconds + add };
}

/** True when heartbeat response rotated the open session (typically VN day boundary). */
export function didServerRotateSession(
  before: LocalOnlineSession,
  after: LocalOnlineSession,
): boolean {
  return (
    before.clientSessionId !== after.clientSessionId || before.startedAtMs !== after.startedAtMs
  );
}

/**
 * Live today total = closedSeconds (same VN day + shop) + open session elapsed when live.
 */
export function computeTodayOnlineSeconds(
  today: LocalOnlineToday | null | undefined,
  openSession: LocalOnlineSession | null | undefined,
  nowMs = Date.now(),
): TodayOnlineSnapshot {
  const vnDate = vnDateNow(nowMs);
  const liveSession =
    openSession &&
    !openSession.pendingClose &&
    Number.isFinite(openSession.startedAtMs) &&
    vnDateFromMs(openSession.startedAtMs) === vnDate
      ? openSession
      : null;

  const shopId = (liveSession?.externalShopId || today?.externalShopId || '').trim() || null;

  let closed = 0;
  if (
    today &&
    today.vnDate === vnDate &&
    shopId &&
    today.externalShopId === shopId &&
    Number.isFinite(today.closedSeconds)
  ) {
    closed = Math.max(0, Math.floor(today.closedSeconds));
  } else if (today && today.vnDate === vnDate && !shopId && Number.isFinite(today.closedSeconds)) {
    closed = Math.max(0, Math.floor(today.closedSeconds));
  }

  const openExtra = liveSession
    ? sessionDurationSeconds(liveSession.startedAtMs, nowMs)
    : 0;

  return {
    seconds: closed + openExtra,
    isLive: !!liveSession,
    vnDate,
    externalShopId: shopId,
  };
}

function pad2(n: number): string {
  return n < 10 ? `0${n}` : String(n);
}

/**
 * Format like seller-web `Xh Ym`; when `live` include seconds (`2h 15m 03s` / `45m 12s` / `12s`).
 */
export function formatOnlineDuration(
  seconds?: number | null,
  options?: { live?: boolean },
): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return '—';
  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const secs = total % 60;
  const live = options?.live === true;

  if (live) {
    if (hours > 0) {
      return minutes > 0
        ? `${hours}h ${minutes}m ${pad2(secs)}s`
        : `${hours}h ${pad2(secs)}s`;
    }
    if (minutes > 0) return `${minutes}m ${pad2(secs)}s`;
    return `${secs}s`;
  }

  if (hours <= 0) {
    if (minutes > 0) return `${minutes}m`;
    return `${secs}s`;
  }
  if (minutes <= 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
}
