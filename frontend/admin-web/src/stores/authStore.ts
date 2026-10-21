import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { AuthResponse, CurrentUser } from '../types/auth';

export const AUTH_STORAGE_KEY = 'admin-auth';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: string | null;
  user: CurrentUser | null;
  setAuth: (auth: AuthResponse) => void;
  setUser: (user: CurrentUser) => void;
  clearAuth: () => void;
  hasPermission: (permission: string) => boolean;
  canAccessAdminPortal: () => boolean;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      user: null,
      setAuth: (auth) =>
        set({
          accessToken: auth.accessToken,
          refreshToken: auth.refreshToken,
          expiresAt: auth.expiresAt,
        }),
      setUser: (user) => set({ user }),
      clearAuth: () =>
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          user: null,
        }),
      hasPermission: (permission) => get().user?.permissions.includes(permission) ?? false,
      canAccessAdminPortal: () => {
        const { user } = get();
        if (!user) return false;
        if (user.roles.includes('Admin')) return true;
        return user.permissions.some((p) => p.startsWith('admin.') && p.endsWith('.view'));
      },
    }),
    {
      name: AUTH_STORAGE_KEY,
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        expiresAt: state.expiresAt,
        user: state.user,
      }),
    },
  ),
);

/** Keep in-memory tokens in sync when another tab rotates/refreshes auth. */
if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key !== AUTH_STORAGE_KEY) return;

    if (event.newValue === null) {
      useAuthStore.setState({
        accessToken: null,
        refreshToken: null,
        expiresAt: null,
        user: null,
      });
      return;
    }

    try {
      const parsed = JSON.parse(event.newValue) as {
        state?: {
          accessToken?: string | null;
          refreshToken?: string | null;
          expiresAt?: string | null;
          user?: CurrentUser | null;
        };
      };
      const next = parsed.state;
      if (!next) return;

      useAuthStore.setState({
        accessToken: next.accessToken ?? null,
        refreshToken: next.refreshToken ?? null,
        expiresAt: next.expiresAt ?? null,
        user: next.user ?? null,
      });
    } catch {
      // ignore malformed persist payload
    }
  });
}
