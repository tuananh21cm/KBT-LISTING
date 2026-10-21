import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { ThemeMode } from './antdTheme';

export function applyDocumentTheme(mode: ThemeMode) {
  if (typeof document === 'undefined') return;
  document.documentElement.setAttribute('data-theme', mode);
  document.documentElement.style.colorScheme = mode;
}

function readCachedMode(storageKey: string): ThemeMode {
  if (typeof window === 'undefined') return 'light';
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return 'light';
    const parsed = JSON.parse(raw) as { state?: { mode?: string } };
    return parsed.state?.mode === 'dark' ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

export type ThemeStore = {
  mode: ThemeMode;
  setMode: (mode: ThemeMode) => void;
  hydrateFromUser: (theme: string | null | undefined) => void;
  toggleMode: () => ThemeMode;
};

/**
 * Factory so each portal can use its own localStorage key
 * (`admin-theme` / `seller-theme`) without forking store logic.
 */
export function createThemeStore(storageKey: string) {
  const initialMode = readCachedMode(storageKey);

  const useThemeStore = create<ThemeStore>()(
    persist(
      (set, get) => ({
        mode: initialMode,
        setMode: (mode) => {
          applyDocumentTheme(mode);
          set({ mode });
        },
        hydrateFromUser: (theme) => {
          // Keep local preference when API omits theme (avoid wiping post-logout cache).
          if (theme !== 'dark' && theme !== 'light') {
            applyDocumentTheme(get().mode);
            return;
          }
          const next: ThemeMode = theme;
          if (get().mode === next) {
            applyDocumentTheme(next);
            return;
          }
          applyDocumentTheme(next);
          set({ mode: next });
        },
        toggleMode: () => {
          const next: ThemeMode = get().mode === 'dark' ? 'light' : 'dark';
          applyDocumentTheme(next);
          set({ mode: next });
          return next;
        },
      }),
      {
        name: storageKey,
        storage: createJSONStorage(() => localStorage),
        partialize: (state) => ({ mode: state.mode }),
        onRehydrateStorage: () => (state) => {
          applyDocumentTheme(state?.mode === 'dark' ? 'dark' : 'light');
        },
      },
    ),
  );

  /** Apply cached theme ASAP (before React mount) to reduce FOUC. */
  if (typeof window !== 'undefined') {
    applyDocumentTheme(initialMode);
  }

  return useThemeStore;
}
