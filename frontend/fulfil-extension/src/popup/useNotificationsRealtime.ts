import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { STORAGE_KEYS, notificationsHubUrl, getOrCreateSessionId } from '../shared/config';
import type { AuthState } from '../shared/types';

export type OrderClaimChangedItem = {
  orderId: string;
  state: string;
  claimedByUserId?: string | null;
  claimedByName?: string | null;
  claimedAt?: string | null;
  expiresAt?: string | null;
  sessionCount?: number;
  sessionIds?: string[];
  startedAt?: string | null;
};

export type OrderClaimChangedPayload = {
  items: OrderClaimChangedItem[];
  serverTime?: string;
};

function normalizeClaimChanged(raw: unknown): OrderClaimChangedPayload | null {
  if (!raw || typeof raw !== 'object') return null;
  const obj = raw as Record<string, unknown>;
  const itemsRaw = (obj.items ?? obj.Items) as unknown;
  if (!Array.isArray(itemsRaw)) return null;
  const items: OrderClaimChangedItem[] = itemsRaw
    .filter((x): x is Record<string, unknown> => !!x && typeof x === 'object')
    .map((x) => ({
      orderId: String(x.orderId ?? x.OrderId ?? ''),
      state: String(x.state ?? x.State ?? ''),
      claimedByUserId: (x.claimedByUserId ?? x.ClaimedByUserId) as string | null | undefined,
      claimedByName: (x.claimedByName ?? x.ClaimedByName) as string | null | undefined,
      claimedAt: (x.claimedAt ?? x.ClaimedAt) as string | null | undefined,
      expiresAt: (x.expiresAt ?? x.ExpiresAt) as string | null | undefined,
      sessionCount: Number(x.sessionCount ?? x.SessionCount ?? 0),
      sessionIds: Array.isArray(x.sessionIds ?? x.SessionIds)
        ? ((x.sessionIds ?? x.SessionIds) as unknown[]).map(String)
        : [],
      startedAt: (x.startedAt ?? x.StartedAt) as string | null | undefined,
    }))
    .filter((x) => !!x.orderId);
  const serverTime = (obj.serverTime ?? obj.ServerTime) as string | undefined;
  return { items, serverTime };
}

async function readAccessToken(): Promise<string | null> {
  try {
    const raw = await chrome.storage.local.get(STORAGE_KEYS.auth);
    const auth = raw[STORAGE_KEYS.auth] as AuthState | undefined;
    return auth?.accessToken?.trim() || null;
  } catch {
    return null;
  }
}

export type UseNotificationsRealtimeOpts = {
  enabled: boolean;
  onClaimChanged?: (payload: OrderClaimChangedPayload) => void;
  onConnectionChange?: (connected: boolean) => void;
};

/**
 * SignalR `/hubs/notifications` khi side panel đã đăng nhập.
 * Chỉ sống trong lifecycle React (không giữ trong service worker MV3).
 * Chỉ lắng nghe `orderClaimChanged` (hàng đợi fulfil) — không còn chat/thông báo trong app.
 */
export function useNotificationsRealtime(opts: UseNotificationsRealtimeOpts) {
  const { enabled, onClaimChanged, onConnectionChange } = opts;
  const onClaimChangedRef = useRef(onClaimChanged);
  onClaimChangedRef.current = onClaimChanged;
  const onConnectionChangeRef = useRef(onConnectionChange);
  onConnectionChangeRef.current = onConnectionChange;

  useEffect(() => {
    if (!enabled) {
      onConnectionChangeRef.current?.(false);
      return;
    }

    let disposed = false;
    let connection: signalR.HubConnection | null = null;

    const start = async () => {
      const token = await readAccessToken();
      if (!token || disposed) return;

      const sessionId = await getOrCreateSessionId();
      if (disposed) return;

      const hubUrl = `${notificationsHubUrl()}?fulfilSession=${encodeURIComponent(sessionId)}`;
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: async () => (await readAccessToken()) || token,
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      if (disposed) {
        void conn.stop().catch(() => undefined);
        return;
      }

      connection = conn;

      conn.on('orderClaimChanged', (raw: unknown) => {
        if (disposed) return;
        const payload = normalizeClaimChanged(raw);
        if (!payload || payload.items.length === 0) return;
        onClaimChangedRef.current?.(payload);
      });

      // Hub broadcasts presence to all clients; register so SignalR does not warn.
      conn.on('presenceChanged', () => undefined);

      conn.onreconnected(() => {
        if (!disposed) onConnectionChangeRef.current?.(true);
      });
      conn.onclose(() => {
        if (!disposed) onConnectionChangeRef.current?.(false);
      });
      conn.onreconnecting(() => {
        if (!disposed) onConnectionChangeRef.current?.(false);
      });

      try {
        await conn.start();
        if (disposed) {
          void conn.stop().catch(() => undefined);
          if (connection === conn) connection = null;
          return;
        }
        onConnectionChangeRef.current?.(true);
      } catch {
        if (connection === conn) connection = null;
        onConnectionChangeRef.current?.(false);
        // Fallback poll ở App xử lý khi hub không kết nối được.
      }
    };

    void start();

    return () => {
      disposed = true;
      onConnectionChangeRef.current?.(false);
      const conn = connection;
      connection = null;
      if (conn) {
        void conn.stop().catch(() => undefined);
      }
    };
  }, [enabled]);
}
