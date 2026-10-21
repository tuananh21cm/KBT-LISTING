/**
 * Cross-tab sync for seller listing list (prep for edit-in-new-tab later).
 */

const CHANNEL = 'seller-listings-sync';
const STORAGE_KEY = 'seller-listings-invalidate-at';

export type ListingsChangedPayload = {
  at: number;
  reason?: string;
};

/** Notify sibling tabs that listing list data changed (same origin). */
export function notifyListingsChanged(reason?: string): void {
  const payload: ListingsChangedPayload = { at: Date.now(), reason };
  try {
    const bc = new BroadcastChannel(CHANNEL);
    bc.postMessage(payload);
    bc.close();
  } catch {
    /* BroadcastChannel unavailable */
  }
  try {
    localStorage.setItem(STORAGE_KEY, String(payload.at));
  } catch {
    /* private mode / quota */
  }
}

/** Subscribe to listing-list invalidation from other tabs. Returns unsubscribe. */
export function subscribeListingsChanged(
  onChange: (payload?: ListingsChangedPayload) => void,
): () => void {
  let bc: BroadcastChannel | null = null;
  try {
    bc = new BroadcastChannel(CHANNEL);
    bc.onmessage = (event: MessageEvent<ListingsChangedPayload>) => {
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
