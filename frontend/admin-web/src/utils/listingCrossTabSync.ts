/**
 * Cross-tab sync for admin listing list.
 * Edit opens in a new window (separate React Query cache) — BroadcastChannel (+ localStorage
 * fallback) lets the list tab invalidate / refetch after Save Draft / Publish / … elsewhere.
 */

const CHANNEL = 'admin-listings-sync';
const STORAGE_KEY = 'admin-listings-invalidate-at';

export type AdminListingsChangedPayload = {
  at: number;
  reason?: string;
};

/** Notify sibling tabs that listing list data changed (same origin). */
export function notifyAdminListingsChanged(reason?: string): void {
  const payload: AdminListingsChangedPayload = { at: Date.now(), reason };
  try {
    const bc = new BroadcastChannel(CHANNEL);
    bc.postMessage(payload);
    bc.close();
  } catch {
    /* BroadcastChannel unavailable */
  }
  try {
    // storage event fires in *other* tabs only (not the sender).
    localStorage.setItem(STORAGE_KEY, String(payload.at));
  } catch {
    /* private mode / quota */
  }
}

/** Subscribe to listing-list invalidation from other tabs. Returns unsubscribe. */
export function subscribeAdminListingsChanged(
  onChange: (payload?: AdminListingsChangedPayload) => void,
): () => void {
  let bc: BroadcastChannel | null = null;
  try {
    bc = new BroadcastChannel(CHANNEL);
    bc.onmessage = (event: MessageEvent<AdminListingsChangedPayload>) => {
      onChange(event.data);
    };
  } catch {
    bc = null;
  }

  const onStorage = (event: StorageEvent) => {
    if (event.key !== STORAGE_KEY || event.newValue == null) return;
    const at = Number(event.newValue);
    onChange({ at: Number.isFinite(at) ? at : Date.now() });
  };
  window.addEventListener('storage', onStorage);

  return () => {
    try {
      bc?.close();
    } catch {
      /* ignore */
    }
    window.removeEventListener('storage', onStorage);
  };
}
