import { APP_FONT_FAMILY, APP_FONT_SIZE } from '../../theme/antdTheme';
import { isDarkTheme } from '../../theme/isDarkTheme';

// Shared stats helpers — change in `@drop/stats` updates both portals.
export {
  ORDER_STATUS_META,
  PERIOD_OPTIONS,
  PERIOD_SECTION,
  RANGE_PRESETS,
  rangeForPeriod,
  matchPeriod,
  clampDateRange,
  disabledFutureDate,
  chartEmptyStyle,
  chartHeight,
  SELLER_PIE_TOP,
  renderRankMedal,
  statsKpiTone,
  statsNegativeColor,
  statsPositiveColor,
  dayjs,
  type OrderStatusKey,
  type OrderStatusMeta,
  type StatsKpiTone,
} from '@drop/stats';

export {
  money,
  moneyCompact,
  shortMoney,
  formatDayLabel,
  formatDateLabel,
  formatPeriodRange,
} from '@drop/dashboard';

/** Axis / legend text scale by chart density (Inter family from antdTheme). */
export type ChartTextScale = 'sm' | 'md' | 'lg';

const CHART_FONT_SIZE: Record<ChartTextScale, number> = {
  sm: 10,
  md: 11,
  lg: APP_FONT_SIZE,
};

const buildAxis = (scale: ChartTextScale) => {
  const dark = isDarkTheme();
  const label = {
    labelFontSize: CHART_FONT_SIZE[scale],
    labelFontFamily: APP_FONT_FAMILY,
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
    itemLabelFontFamily: APP_FONT_FAMILY,
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

/**
 * Bottom pie legend (built-in SVG). Do **not** use G2 `legend.color.render` HTML
 * customizers under `@ant-design/plots` + React 19.
 */
export const sharePieLegend = (isMobile: boolean) => ({
  color: {
    position: 'bottom' as const,
    rowPadding: 4,
    ...(isMobile ? chartTheme.legendColorSm : chartTheme.legendColorMd),
    layout: isMobile ? { justifyContent: 'center' as const, flexWrap: true } : undefined,
  },
});

/** Slice % label — Inter + scale (default md for pie panels). */
export const pieSliceLabelStyle = (scale: ChartTextScale = 'md') => ({
  fontSize: CHART_FONT_SIZE[scale],
  fontFamily: APP_FONT_FAMILY,
  fill: isDarkTheme() ? 'rgba(255,255,255,0.65)' : 'rgba(0,0,0,0.45)',
});
