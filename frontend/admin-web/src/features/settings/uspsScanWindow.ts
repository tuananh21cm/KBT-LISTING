import dayjs, { type Dayjs } from 'dayjs';
// Side effect: extends dayjs with the utc + timezone plugins used below.
import { APP_TZ } from '../../utils/datetime';
import { USPS_SCAN_WINDOW_TZ } from '../../constants/uspsScanAutoSettings';

/** Window clocks as stored in Settings (US Eastern wall time, `HH:mm`). */
export type UspsScanWindowConfig = {
  enabled: boolean;
  weekdayStart: string;
  weekdayEnd: string;
  saturdayEnabled: boolean;
  saturdayStart: string;
  saturdayEnd: string;
  cutoffMarginMinutes: number;
};

export type UspsScanWindowStatus = {
  isOpen: boolean;
  text: string;
};

/** Mirrors backend UspsScanWindow.LookaheadDays. */
const LOOKAHEAD_DAYS = 8;

const VN_DOW_LABELS = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'] as const;

/** Builds an instant from an Eastern date + `HH:mm` wall clock. */
function easternClockAt(easternDate: Dayjs, clock: string): Dayjs {
  return dayjs.tz(`${easternDate.format('YYYY-MM-DD')} ${clock}`, USPS_SCAN_WINDOW_TZ);
}

/**
 * Open/close instants for one Eastern day, cutoff margin already applied.
 * Sunday is always closed; Saturday follows its own toggle and clocks.
 */
function resolveEasternWindow(
  easternDay: Dayjs,
  config: UspsScanWindowConfig,
): { open: Dayjs; close: Dayjs } | null {
  const dayOfWeek = easternDay.day();
  if (dayOfWeek === 0) return null;

  const isSaturday = dayOfWeek === 6;
  if (isSaturday && !config.saturdayEnabled) return null;

  const open = easternClockAt(easternDay, isSaturday ? config.saturdayStart : config.weekdayStart);
  const rawClose = easternClockAt(easternDay, isSaturday ? config.saturdayEnd : config.weekdayEnd);
  if (!open.isValid() || !rawClose.isValid()) return null;

  const close = rawClose.subtract(Math.max(0, config.cutoffMarginMinutes), 'minute');
  // Margin wider than the window (or close <= open) leaves nothing to push into.
  return close.isAfter(open) ? { open, close } : null;
}

/** Next instant the window opens, or `null` when no day in the next week qualifies. */
function nextOpenInstant(now: Dayjs, config: UspsScanWindowConfig): Dayjs | null {
  const easternNow = now.tz(USPS_SCAN_WINDOW_TZ);

  for (let offset = 0; offset < LOOKAHEAD_DAYS; offset += 1) {
    const window = resolveEasternWindow(easternNow.add(offset, 'day'), config);
    if (!window) continue;

    if (offset === 0) {
      if (!easternNow.isBefore(window.close)) continue;
      if (!easternNow.isBefore(window.open)) return easternNow;
    }

    return window.open;
  }

  return null;
}

/** `19:00 hôm nay` / `20:00 ngày mai` / `20:00 T7 04/10` in Vietnam wall time. */
export function formatVnMoment(instant: Dayjs, now: Dayjs = dayjs()): string {
  const vn = instant.tz(APP_TZ);
  const clock = vn.format('HH:mm');
  const dayShift = vn.startOf('day').diff(now.tz(APP_TZ).startOf('day'), 'day');

  if (dayShift === 0) return `${clock} hôm nay`;
  if (dayShift === 1) return `${clock} ngày mai`;
  return `${clock} ${VN_DOW_LABELS[vn.day()]} ${vn.format('DD/MM')}`;
}

/**
 * Vietnam equivalent of an Eastern `HH:mm` range, e.g. `20:00 → 05:00 (+1)`.
 * Evaluated on today's Eastern date so the label follows US daylight saving automatically.
 */
export function etRangeToVnLabel(
  startClock: string,
  endClock: string,
  now: Dayjs = dayjs(),
): string | null {
  const easternToday = now.tz(USPS_SCAN_WINDOW_TZ);
  const start = easternClockAt(easternToday, startClock);
  const end = easternClockAt(easternToday, endClock);
  if (!start.isValid() || !end.isValid()) return null;

  const startVn = start.tz(APP_TZ);
  const endVn = end.tz(APP_TZ);
  const dayShift = endVn.startOf('day').diff(startVn.startOf('day'), 'day');
  const suffix = dayShift > 0 ? ` (+${dayShift})` : '';

  return `${startVn.format('HH:mm')} → ${endVn.format('HH:mm')}${suffix}`;
}

/** One-line status for the Settings card, in Vietnam wall time. */
export function describeUspsScanWindow(
  config: UspsScanWindowConfig,
  now: Dayjs = dayjs(),
): UspsScanWindowStatus {
  if (!config.enabled) {
    return { isOpen: true, text: 'Đang đẩy suốt ngày đêm — gate giờ làm việc US đang tắt.' };
  }

  const easternNow = now.tz(USPS_SCAN_WINDOW_TZ);
  const today = resolveEasternWindow(easternNow, config);
  if (today && !easternNow.isBefore(today.open) && easternNow.isBefore(today.close)) {
    return {
      isOpen: true,
      text: `Đang trong giờ làm việc US · ngừng đẩy lúc ${formatVnMoment(today.close, now)}`,
    };
  }

  const nextOpen = nextOpenInstant(now, config);
  if (!nextOpen) {
    return {
      isOpen: false,
      text: 'Không có khung giờ nào mở — kiểm tra lại giờ mở/đóng và đệm đóng cửa.',
    };
  }

  return {
    isOpen: false,
    text: `Ngoài giờ làm việc US · mở lại ${formatVnMoment(nextOpen, now)}`,
  };
}
