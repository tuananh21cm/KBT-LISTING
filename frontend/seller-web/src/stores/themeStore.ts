import { createThemeStore } from '@drop/theme';

export const THEME_STORAGE_KEY = 'seller-theme';

export const useThemeStore = createThemeStore(THEME_STORAGE_KEY);
