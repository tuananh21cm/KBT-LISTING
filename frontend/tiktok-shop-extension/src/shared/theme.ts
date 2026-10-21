/** UI theme — khớp seller/admin (`light` | `dark`). */
export type ThemeMode = 'light' | 'dark';

export function normalizeTheme(value: string | null | undefined): ThemeMode {
  return value === 'dark' ? 'dark' : 'light';
}

/** Áp `data-theme` + `colorScheme` lên document (popup). */
export function applyDocumentTheme(mode: ThemeMode): void {
  if (typeof document === 'undefined') return;
  document.documentElement.setAttribute('data-theme', mode);
  document.documentElement.style.colorScheme = mode;
}
