import type { CSSProperties } from 'react';
import type { Dayjs } from 'dayjs';
import type { DashboardPeriod } from '@drop/types/dashboard';
import { dayjs, disabledFutureVnDate, nowVn, startOfVnDay } from '@drop/utils/datetime';

export const PERIOD_OPTIONS: { label: string; value: DashboardPeriod }[] = [
  { label: 'Ngày', value: 'day' },
  { label: 'Tuần', value: 'week' },
  { label: 'Tháng', value: 'month' },
];

export const PERIOD_SECTION: Record<DashboardPeriod, string> = {
  day: 'Kỳ này · hôm nay',
  week: 'Kỳ này · tuần',
  month: 'Kỳ này · tháng',
};

export const RANGE_PRESETS: { label: string; value: [Dayjs, Dayjs] }[] = [
  { label: '7 ngày', value: [nowVn().subtract(6, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '30 ngày', value: [nowVn().subtract(29, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '90 ngày', value: [nowVn().subtract(89, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: 'Năm nay', value: [nowVn().startOf('year'), nowVn().startOf('day')] },
];

const startOfIsoWeek = (d: Dayjs) => {
  const dow = d.day();
  const fromMon = dow === 0 ? 6 : dow - 1;
  return d.subtract(fromMon, 'day').startOf('day');
};

export const rangeForPeriod = (period: DashboardPeriod): [Dayjs, Dayjs] => {
  const end = nowVn().startOf('day');
  if (period === 'day') return [end, end];
  if (period === 'week') return [startOfIsoWeek(end), end];
  return [end.startOf('month'), end];
};

export const matchPeriod = (range: [Dayjs, Dayjs]): DashboardPeriod | 'custom' => {
  const [start, end] = range;
  for (const period of ['day', 'week', 'month'] as DashboardPeriod[]) {
    const [a, b] = rangeForPeriod(period);
    if (start.isSame(a, 'day') && end.isSame(b, 'day')) return period;
  }
  return 'custom';
};

export const clampDateRange = (start: Dayjs, end: Dayjs, maxDays = 366): [Dayjs, Dayjs] => {
  let s = startOfVnDay(start);
  let e = startOfVnDay(end);
  if (e.isBefore(s)) [s, e] = [e, s];
  const today = nowVn().startOf('day');
  if (e.isAfter(today, 'day')) e = today;
  if (e.diff(s, 'day') + 1 > maxDays) s = e.subtract(maxDays - 1, 'day');
  return [s, e];
};

export const disabledFutureDate = (current: Dayjs) => disabledFutureVnDate(current);

export const chartEmptyStyle: CSSProperties = { minHeight: 220 };
export const chartHeight = 260;

/** Full pie slices: top N + Others (rates stay complete). */
export const SELLER_PIE_TOP = 8;

export const renderRankMedal = (index: number) => {
  const rank = index + 1;
  const medalClass =
    rank === 1
      ? 'dashboard-rank-medal is-1'
      : rank === 2
        ? 'dashboard-rank-medal is-2'
        : rank === 3
          ? 'dashboard-rank-medal is-3'
          : 'dashboard-rank-medal';
  return medalClass;
};

export { dayjs };
