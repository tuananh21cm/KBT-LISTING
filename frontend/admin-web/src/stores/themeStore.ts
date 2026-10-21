import { createThemeStore } from '@drop/theme';

export const THEME_STORAGE_KEY = 'admin-theme';

export const useThemeStore = createThemeStore(THEME_STORAGE_KEY);
