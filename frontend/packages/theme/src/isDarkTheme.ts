/** Read current UI theme from `document` (set by themeStore / index.html bootstrap). */
export const isDarkTheme = (): boolean =>
  typeof document !== 'undefined' &&
  document.documentElement.getAttribute('data-theme') === 'dark';
