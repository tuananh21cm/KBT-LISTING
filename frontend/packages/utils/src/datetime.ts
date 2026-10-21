import dayjs, { type Dayjs } from 'dayjs';
import isoWeek from 'dayjs/plugin/isoWeek';
import timezone from 'dayjs/plugin/timezone';
import utc from 'dayjs/plugin/utc';

dayjs.extend(utc);
dayjs.extend(timezone);
dayjs.extend(isoWeek);

/** Canonical business / display timezone. */
export const APP_TZ = 'Asia/Ho_Chi_Minh';

/** Canonical UI date/datetime patterns (Vietnam display). */
export const DATE_FORMAT = 'DD-MM-YYYY';
export const DATE_FORMAT_SHORT = 'DD-MM';
export const DATETIME_FORMAT = 'DD-MM-YYYY HH:mm';
export const DATETIME_FORMAT_SHORT = 'DD-MM HH:mm';
export const DATETIME_FORMAT_SECONDS = 'DD-MM-YYYY HH:mm:ss';

dayjs.tz.setDefault(APP_TZ);

export type DatePreset = 'day' | 'week' | 'month' | '7d' | '30d';

/**
 * API/DB instants are UTC. Strings without `Z`/offset (e.g. `2026-07-20T07:05:36`) must be
 * treated as UTC — browser-local parse would under-shift by the operator's offset (often VN +7).
 */
const NAIVE_UTC_DATETIME_RE =
  /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/;

/** Parse an absolute instant; naive datetime strings = UTC. */
export const parseUtcInstant = (value: string | number | Date | Dayjs): Dayjs => {
  if (typeof value === 'string') {
    const t = value.trim();
    if (NAIVE_UTC_DATETIME_RE.test(t)) {
      return dayjs.utc(t.includes('T') ? t : t.replace(' ', 'T'));
    }
  }
  return dayjs(value);
};

/** Current instant in Vietnam. */
export const nowVn = (): Dayjs => dayjs().tz(APP_TZ);

/** Start of Vietnam calendar day (00:00:00). Keeps picker wall-clock as VN date. */
export const startOfVnDay = (value?: string | number | Date | Dayjs | null): Dayjs =>
  dayjs(value).tz(APP_TZ, true).startOf('day');

/** End of Vietnam calendar day (23:59:59.999). Keeps picker wall-clock as VN date. */
export const endOfVnDay = (value?: string | number | Date | Dayjs | null): Dayjs =>
  dayjs(value).tz(APP_TZ, true).endOf('day');

/** Format an absolute instant for UI display in Vietnam (`dd-MM-yyyy HH:mm` by default). */
export const formatDateTimeVn = (
  value?: string | number | Date | Dayjs | null,
  pattern = DATETIME_FORMAT,
): string => {
  if (value == null || value === '') return '—';
  const d = parseUtcInstant(value);
  if (!d.isValid()) return '—';
  return d.tz(APP_TZ).format(pattern);
};

/** Format a calendar date only (`dd-MM-yyyy`). */
export const formatDateVn = (
  value?: string | number | Date | Dayjs | null,
  pattern = DATE_FORMAT,
): string => formatDateTimeVn(value, pattern);

/**
 * @deprecated Use {@link formatDateTimeVn} — same canonical `dd-MM-yyyy HH:mm` format.
 */
export const formatListingDateTimeVn = (
  value?: string | number | Date | Dayjs | null,
): string => formatDateTimeVn(value);

/** Vietnam calendar date key (yyyy-MM-dd) for dashboard APIs. */
export const toVnCalendarDate = (value?: string | number | Date | Dayjs | null): string =>
  dayjs(value).tz(APP_TZ, true).format('YYYY-MM-DD');

/** VN day bounds → UTC ISO pair for listing/order/txn APIs. */
export const toUtcIsoRange = (
  from?: Dayjs | null,
  to?: Dayjs | null,
): { dateFrom?: string; dateTo?: string } => ({
  dateFrom: from ? startOfVnDay(from).toISOString() : undefined,
  dateTo: to ? endOfVnDay(to).toISOString() : undefined,
});

/** VN day bounds as ISO `from`/`to` (card spending / bank stats). */
export const toUtcIsoFromTo = (
  from?: Dayjs | null,
  to?: Dayjs | null,
): { from?: string; to?: string } => ({
  from: from ? startOfVnDay(from).toISOString() : undefined,
  to: to ? endOfVnDay(to).toISOString() : undefined,
});

/** Preset ranges in Vietnam calendar (day / iso-week / month / rolling 7 or 30 days). */
export const rangeForPresetVn = (preset: DatePreset): [Dayjs, Dayjs] => {
  const now = nowVn();
  if (preset === 'day') {
    return [now.startOf('day'), now.endOf('day')];
  }
  if (preset === 'week') {
    return [now.startOf('isoWeek'), now.endOf('isoWeek')];
  }
  if (preset === '7d') {
    // 7 ngày tính cả hôm nay.
    return [now.subtract(6, 'day').startOf('day'), now.endOf('day')];
  }
  if (preset === '30d') {
    // 30 ngày tính cả hôm nay.
    return [now.subtract(29, 'day').startOf('day'), now.endOf('day')];
  }
  return [now.startOf('month'), now.endOf('month')];
};

/** Clamp a picker range to Vietnam day bounds (inclusive). */
export const clampVnDateRange = (range: [Dayjs, Dayjs] | null): [Dayjs, Dayjs] | null => {
  if (!range?.[0] || !range?.[1]) return null;
  return [startOfVnDay(range[0]), endOfVnDay(range[1])];
};

/** Disable future Vietnam calendar days in RangePicker. */
export const disabledFutureVnDate = (current: Dayjs): boolean =>
  !!current && current.tz(APP_TZ).startOf('day').isAfter(nowVn().startOf('day'));

/** Parse API ISO / Date into a Dayjs anchored in Vietnam (for DatePicker value). */
export const parseToVnDayjs = (value?: string | number | Date | null): Dayjs | undefined => {
  if (value == null || value === '') return undefined;
  const d = parseUtcInstant(value);
  return d.isValid() ? d.tz(APP_TZ) : undefined;
};

export { dayjs };
