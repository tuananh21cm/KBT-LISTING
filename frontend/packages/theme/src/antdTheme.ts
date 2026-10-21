import { theme, type ThemeConfig } from 'antd';

/** Shared with ConfigProvider + chart labels (Inter 12 — project standard). */
export const APP_FONT_FAMILY =
  "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif" as const;
export const APP_FONT_SIZE = 12;

const sharedToken = {
  fontFamily: APP_FONT_FAMILY,
  fontSize: APP_FONT_SIZE,
  borderRadius: 6,
  colorPrimary: '#1677ff',
} as const;

const sharedComponentSizes = {
  Button: { controlHeight: 24, paddingInline: 12 },
  Input: { controlHeight: 24 },
  Select: { controlHeight: 24 },
  Table: { cellPaddingBlock: 8, cellPaddingInline: 8 },
  Pagination: { itemSize: 24 },
  Form: { itemMarginBottom: 16 },
} as const;

/** Classic Ant dark sider used in light app mode. */
const LIGHT_SIDER = '#001529';

/** Flat dark surfaces (Cursor / OpenAI-platform style). */
const DARK_BASE = '#212121';
const DARK_CONTAINER = '#2f2f2f';
const DARK_ELEVATED = '#3a3a3a';
const DARK_SIDER = '#1a1a1a';
const DARK_BORDER = '#424242';
const DARK_BORDER_SECONDARY = '#383838';

export type ThemeMode = 'light' | 'dark';

export const lightTheme: ThemeConfig = {
  algorithm: theme.defaultAlgorithm,
  token: {
    ...sharedToken,
  },
  components: {
    ...sharedComponentSizes,
    Menu: {
      itemHeight: 36,
      darkItemBg: LIGHT_SIDER,
      darkSubMenuItemBg: LIGHT_SIDER,
      subMenuItemBg: LIGHT_SIDER,
    },
    Layout: { siderBg: LIGHT_SIDER, headerBg: '#ffffff' },
  },
};

export const darkTheme: ThemeConfig = {
  algorithm: theme.darkAlgorithm,
  token: {
    ...sharedToken,
    colorBgBase: DARK_BASE,
    colorBgContainer: DARK_CONTAINER,
    colorBgElevated: DARK_ELEVATED,
    colorBgLayout: DARK_BASE,
    colorBorder: DARK_BORDER,
    colorBorderSecondary: DARK_BORDER_SECONDARY,
    colorText: 'rgba(255,255,255,0.88)',
    colorTextSecondary: 'rgba(255,255,255,0.65)',
    colorTextTertiary: 'rgba(255,255,255,0.45)',
    colorTextQuaternary: 'rgba(255,255,255,0.25)',
  },
  components: {
    ...sharedComponentSizes,
    Menu: {
      itemHeight: 36,
      darkItemBg: DARK_SIDER,
      darkSubMenuItemBg: DARK_SIDER,
      subMenuItemBg: DARK_SIDER,
      darkItemSelectedBg: 'rgba(22, 119, 255, 0.25)',
      darkItemHoverBg: 'rgba(255, 255, 255, 0.08)',
    },
    Layout: { siderBg: DARK_SIDER, headerBg: DARK_CONTAINER },
  },
};

/** @deprecated Prefer lightTheme / darkTheme via theme store mode. */
export const antdTheme = lightTheme;

export const siderBgForMode = (mode: ThemeMode): string =>
  mode === 'dark' ? DARK_SIDER : LIGHT_SIDER;
