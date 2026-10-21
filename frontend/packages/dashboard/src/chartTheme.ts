import { isDarkTheme } from './theme';

/** Axis / legend text scale by chart density. */
export type ChartTextScale = 'sm' | 'md' | 'lg';

/** Shared with ConfigProvider + chart labels (Inter — project standard). */
export const CHART_FONT_FAMILY =
  "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif" as const;

const CHART_FONT_SIZE: Record<ChartTextScale, number> = {
  sm: 10, // compact cards (~220px), dense ticks
  md: 11, // medium pie / half-width
  lg: 12, // full-width / default page text
};

const buildAxis = (scale: ChartTextScale) => {
  const dark = isDarkTheme();
  const label = {
    labelFontSize: CHART_FONT_SIZE[scale],
    labelFontFamily: CHART_FONT_FAMILY,
    labelOpacity: 1,
    labelFill: dark ? 'rgba(255,255,255,0.65)' : 'rgba(0,0,0,0.45)',
  } as const;
  return {
    x: { ...label },
    y: { ...label, gridStroke: dark ? '#424242' : '#f0f0f0' },
  };
};

const buildLegendColor = (scale: ChartTextScale) => {
  const dark = isDarkTheme();
  return {
    itemLabelFill: dark ? 'rgba(255,255,255,0.65)' : 'rgba(0,0,0,0.45)',
    itemLabelFontSize: CHART_FONT_SIZE[scale],
    itemLabelFontFamily: CHART_FONT_FAMILY,
  };
};

/**
 * Chart typography tied to project font; pick scale by chart size:
 * - sm: small trend cards
 * - md: pie / mid panels
 * - lg: full-width charts (default page 12px)
 */
export const chartTheme = {
  get axis() {
    return buildAxis('lg');
  },
  get axisSm() {
    return buildAxis('sm');
  },
  get axisMd() {
    return buildAxis('md');
  },
  get legendColor() {
    return buildLegendColor('lg');
  },
  get legendColorSm() {
    return buildLegendColor('sm');
  },
  get legendColorMd() {
    return buildLegendColor('md');
  },
};

/** Slice % label — Inter + scale (default md for pie panels). */
export const pieSliceLabelStyle = (scale: ChartTextScale = 'md') => ({
  fontSize: CHART_FONT_SIZE[scale],
  fontFamily: CHART_FONT_FAMILY,
  fill: isDarkTheme() ? 'rgba(255,255,255,0.65)' : 'rgba(0,0,0,0.45)',
});

/**
 * Bottom pie legend (built-in SVG). Do **not** use G2 `legend.color.render` HTML
 * customizers under `@ant-design/plots` + React 19 — HTML path races
 * `.legend-html` mount (`addEventListener` on null) and DOM `createElement`
 * is mis-detected as React JSX by plots `processConfig` / `createNode`.
 */
export const sharePieLegend = (isMobile: boolean) => ({
  color: {
    position: 'bottom' as const,
    rowPadding: 4,
    ...(isMobile ? chartTheme.legendColorSm : chartTheme.legendColorMd),
    layout: isMobile ? { justifyContent: 'center' as const, flexWrap: true } : undefined,
  },
});
