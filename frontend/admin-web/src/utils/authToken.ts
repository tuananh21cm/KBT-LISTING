/** Refresh slightly before access JWT expires to avoid 401 races. */
const REFRESH_SKEW_MS = 60_000;

/** True when access token is missing expiry info that is past / within skew. */
export function shouldRefreshAccessToken(expiresAt: string | null | undefined): boolean {
  if (!expiresAt) return false;
  const expiresMs = Date.parse(expiresAt);
  if (Number.isNaN(expiresMs)) return false;
  return Date.now() >= expiresMs - REFRESH_SKEW_MS;
}

type PersistedAuthSlice = {
  accessToken?: string | null;
  refreshToken?: string | null;
  expiresAt?: string | null;
  user?: unknown;
};

/** Read latest auth slice written by zustand persist (other tabs). */
export function readPersistedAuth(storageKey: string): PersistedAuthSlice | null {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { state?: PersistedAuthSlice };
    return parsed.state ?? null;
  } catch {
    return null;
  }
}
