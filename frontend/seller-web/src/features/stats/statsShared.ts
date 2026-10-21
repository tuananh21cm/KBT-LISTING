/**
 * Seller stats helpers — re-export shared `@drop/stats` + `@drop/dashboard`
 * so Dashboard / future stats pages stay in sync with admin.
 */
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
  chartTheme,
  sharePieLegend,
  pieSliceLabelStyle,
} from '@drop/dashboard';
