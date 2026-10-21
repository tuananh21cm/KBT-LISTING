import type {
  DashboardPeriod,
  DashboardQueryParams,
  DashboardTrendsParams,
} from '@drop/types/dashboard';
import { nowVn, toVnCalendarDate } from '@drop/utils/datetime';

/** UI period filter (maps to API period or fromDate/toDate). */
export type DashboardPeriodFilter = 'day' | 'week' | 'thisMonth' | 'last30Days';

export const DASHBOARD_PERIOD_OPTIONS: { label: string; value: DashboardPeriodFilter }[] = [
  { label: 'Day', value: 'day' },
  { label: 'Week', value: 'week' },
  { label: 'Month', value: 'thisMonth' },
  { label: '30 Days', value: 'last30Days' },
];

/** Map UI filter → API query (period presets or custom fromDate/toDate). */
export const resolveDashboardParams = (
  filter: DashboardPeriodFilter,
): DashboardQueryParams & Pick<DashboardTrendsParams, 'days' | 'grain'> => {
  const today = nowVn().startOf('day');

  if (filter === 'day') {
    return { period: 'day' satisfies DashboardPeriod, days: 7, grain: 'day' };
  }
  if (filter === 'week') {
    return { period: 'week' satisfies DashboardPeriod, grain: 'day' };
  }
  if (filter === 'thisMonth') {
    return { period: 'month' satisfies DashboardPeriod, grain: 'day' };
  }

  // Inclusive rolling window: today and the previous 29 VN calendar days.
  const start = today.subtract(29, 'day');
  return {
    fromDate: toVnCalendarDate(start),
    toDate: toVnCalendarDate(today),
    grain: 'day',
  };
};
