import {
  ONLINE_HEARTBEAT_ALARM,
  ONLINE_HEARTBEAT_DEBOUNCE_MS,
  ONLINE_HEARTBEAT_PERIOD_MINUTES,
  SELLER_US_HOST,
  STORAGE_KEYS,
} from '../shared/config';
import {
  adoptServerSession,
  applyClosedSessionToToday,
  didServerRotateSession,
  ensureOnlineToday,
  newClientSessionId,
  toClosePayload,
  toHeartbeatPayload,
  type LocalOnlineSession,
  type LocalOnlineToday,
  type ShopOnlineCloseReason,
} from '../shared/onlineSession';
import type { TikTokShopInfo } from '../shared/types';
import { postOnlineClose, postOnlineHeartbeat } from './kinhbacApi';

let tickInFlight: Promise<void> | null = null;
let lastHeartbeatAtMs = 0;
let debounceTimer: ReturnType<typeof setTimeout> | null = null;

async function readLocalSession(): Promise<LocalOnlineSession | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.onlineSession);
  const value = raw[STORAGE_KEYS.onlineSession] as LocalOnlineSession | undefined;
  if (!value?.clientSessionId || !value.externalShopId) return null;
  if (!Number.isFinite(value.startedAtMs) || !Number.isFinite(value.lastSeenAtMs)) return null;
  return value;
}

async function writeLocalSession(session: LocalOnlineSession | null): Promise<void> {
  if (!session) {
    await chrome.storage.local.remove(STORAGE_KEYS.onlineSession);
    return;
  }
  await chrome.storage.local.set({ [STORAGE_KEYS.onlineSession]: session });
}

async function readOnlineToday(): Promise<LocalOnlineToday | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.onlineToday);
  const value = raw[STORAGE_KEYS.onlineToday] as LocalOnlineToday | undefined;
  if (!value?.vnDate || !value.externalShopId) return null;
  if (!Number.isFinite(value.closedSeconds)) return null;
  return value;
}

async function writeOnlineToday(today: LocalOnlineToday): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEYS.onlineToday]: today });
}

/** Credit closed session once; skip if already pendingClose (already credited). */
async function creditClosedSession(
  session: LocalOnlineSession,
  endedAtMs: number,
  nowMs = Date.now(),
): Promise<void> {
  if (session.pendingClose) return;
  const prev = await readOnlineToday();
  const next = applyClosedSessionToToday(prev, session, endedAtMs, nowMs);
  await writeOnlineToday(next);
}

async function ensureTodayBucket(externalShopId: string, nowMs = Date.now()): Promise<void> {
  const prev = await readOnlineToday();
  const next = ensureOnlineToday(prev, externalShopId, nowMs);
  if (
    !prev ||
    prev.vnDate !== next.vnDate ||
    prev.externalShopId !== next.externalShopId ||
    prev.closedSeconds !== next.closedSeconds
  ) {
    await writeOnlineToday(next);
  }
}

async function readExternalShopId(): Promise<string | null> {
  const raw = await chrome.storage.local.get(STORAGE_KEYS.shopInfo);
  const shop = raw[STORAGE_KEYS.shopInfo] as TikTokShopInfo | null | undefined;
  const id = shop?.sellerId?.trim();
  return id || null;
}

async function hasSellerUsTab(): Promise<boolean> {
  try {
    const tabs = await chrome.tabs.query({ url: `*://${SELLER_US_HOST}/*` });
    return (tabs?.length ?? 0) > 0;
  } catch {
    return false;
  }
}

function isSellerUsUrl(url: string | undefined): boolean {
  if (!url) return false;
  try {
    return new URL(url).hostname === SELLER_US_HOST;
  } catch {
    return false;
  }
}

async function flushClose(
  session: LocalOnlineSession,
  reason: ShopOnlineCloseReason,
): Promise<boolean> {
  const endedAtMs = Math.max(session.startedAtMs, session.lastSeenAtMs);
  await creditClosedSession(session, endedAtMs);

  try {
    await postOnlineClose(toClosePayload(session, reason));
    await writeLocalSession(null);
    return true;
  } catch {
    await writeLocalSession({ ...session, pendingClose: true, closeReason: reason });
    return false;
  }
}

async function flushHeartbeat(session: LocalOnlineSession, force = false): Promise<LocalOnlineSession> {
  const now = Date.now();
  if (!force && now - lastHeartbeatAtMs < ONLINE_HEARTBEAT_DEBOUNCE_MS) {
    return session;
  }

  try {
    const result = await postOnlineHeartbeat(toHeartbeatPayload(session));
    lastHeartbeatAtMs = Date.now();
    const adopted = adoptServerSession(session, result, lastHeartbeatAtMs);
    if (!adopted) {
      await creditClosedSession(session, Math.max(session.startedAtMs, session.lastSeenAtMs), lastHeartbeatAtMs);
      await writeLocalSession(null);
      return session;
    }
    if (didServerRotateSession(session, adopted)) {
      // Day-boundary (or server rotate): credit old segment up to new startedAt; do not add prior-day into today.
      const endedAtMs = Math.max(session.startedAtMs, Math.min(session.lastSeenAtMs, adopted.startedAtMs));
      await creditClosedSession(session, endedAtMs, lastHeartbeatAtMs);
      await ensureTodayBucket(adopted.externalShopId, lastHeartbeatAtMs);
    }
    await writeLocalSession(adopted);
    return adopted;
  } catch {
    // Soft-fail — local watermark still advances; reconcile later.
    return session;
  }
}

/**
 * Core tick: reconcile pending close / restart leftover, then open or bump while a seller-us tab
 * is present and shop id is known.
 */
export async function tickOnlineSession(options?: { forceHeartbeat?: boolean }): Promise<void> {
  if (tickInFlight) {
    await tickInFlight;
    return;
  }

  tickInFlight = (async () => {
    const force = options?.forceHeartbeat === true;
    const now = Date.now();
    let local = await readLocalSession();
    const online = await hasSellerUsTab();
    const shopId = await readExternalShopId();

    if (local?.pendingClose) {
      const closed = await flushClose(local, local.closeReason ?? 'Explicit');
      if (!closed) return;
      local = null;
    }

    if (!online || !shopId) {
      if (local) {
        await flushClose(local, 'TabClosed');
      }
      return;
    }

    await ensureTodayBucket(shopId, now);

    if (local && local.externalShopId !== shopId) {
      await flushClose(local, 'Explicit');
      local = null;
    }

    if (!local) {
      local = {
        clientSessionId: newClientSessionId(),
        externalShopId: shopId,
        startedAtMs: now,
        lastSeenAtMs: now,
      };
      await writeLocalSession(local);
      await flushHeartbeat(local, true);
      return;
    }

    local = { ...local, lastSeenAtMs: Math.max(local.lastSeenAtMs, now), pendingClose: false };
    await writeLocalSession(local);
    await flushHeartbeat(local, force);
  })().finally(() => {
    tickInFlight = null;
  });

  await tickInFlight;
}

/** After Chrome restart: close leftover local session at lastSeen, then start fresh if needed. */
export async function reconcileOnlineSessionOnStartup(): Promise<void> {
  const local = await readLocalSession();
  if (local) {
    await flushClose(local, 'BrowserRestartReconcile');
  }
  await tickOnlineSession({ forceHeartbeat: true });
}

function scheduleDebouncedTick(): void {
  if (debounceTimer) clearTimeout(debounceTimer);
  debounceTimer = setTimeout(() => {
    debounceTimer = null;
    void tickOnlineSession();
  }, 500);
}

/** Install alarms + tab listeners for background online tracking. */
export function installOnlineSessionTracker(): void {
  chrome.alarms
    .create(ONLINE_HEARTBEAT_ALARM, { periodInMinutes: ONLINE_HEARTBEAT_PERIOD_MINUTES })
    .catch(() => undefined);

  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === ONLINE_HEARTBEAT_ALARM) {
      void tickOnlineSession({ forceHeartbeat: true });
    }
  });

  chrome.runtime.onStartup.addListener(() => {
    void reconcileOnlineSessionOnStartup();
  });
  chrome.runtime.onInstalled.addListener(() => {
    void reconcileOnlineSessionOnStartup();
  });

  chrome.tabs.onUpdated.addListener((_tabId, changeInfo, tab) => {
    if (changeInfo.status === 'complete' || changeInfo.url) {
      if (isSellerUsUrl(tab.url) || isSellerUsUrl(changeInfo.url)) {
        scheduleDebouncedTick();
      }
    }
  });

  chrome.tabs.onRemoved.addListener(() => {
    scheduleDebouncedTick();
  });

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local') return;
    if (changes[STORAGE_KEYS.shopInfo]) {
      scheduleDebouncedTick();
    }
  });

  // Opportunistic first tick when SW wakes.
  void tickOnlineSession({ forceHeartbeat: true });
}
