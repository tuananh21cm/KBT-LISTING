/** Setting keys for USPS auto track-scan (must match backend UspsScanAutoSettingKeys). */
export const USPS_SCAN_AUTO_SETTING_KEYS = {
  autoEnabled: 'order.usps_scan.auto_enabled',
  delayHours: 'order.usps_scan.delay_hours',
  chunkSize: 'order.usps_scan.chunk_size',
  intervalMinutes: 'order.usps_scan.interval_minutes',
  windowEnabled: 'order.usps_scan.window_enabled',
  weekdayStart: 'order.usps_scan.weekday_start',
  weekdayEnd: 'order.usps_scan.weekday_end',
  saturdayEnabled: 'order.usps_scan.saturday_enabled',
  saturdayStart: 'order.usps_scan.saturday_start',
  saturdayEnd: 'order.usps_scan.saturday_end',
  cutoffMarginMinutes: 'order.usps_scan.cutoff_margin_minutes',
} as const;

export const USPS_SCAN_AUTO_DEFAULTS = {
  autoEnabled: false,
  delayHours: 2,
  chunkSize: 10,
  intervalMinutes: 10,
  windowEnabled: true,
  weekdayStart: '08:00',
  weekdayEnd: '17:00',
  saturdayEnabled: true,
  saturdayStart: '08:00',
  saturdayEnd: '12:00',
  cutoffMarginMinutes: 30,
} as const;

/** Minimum track age since TrackingAddedAt; the US window decides *when* to push. */
export const USPS_SCAN_AUTO_DELAY_HOURS = { min: 1, max: 168, default: 2 } as const;
/** 0 = send all eligible in one PushAsync (provider max may still split). */
export const USPS_SCAN_AUTO_CHUNK_SIZE = { min: 0, max: 100, default: 10 } as const;
export const USPS_SCAN_AUTO_INTERVAL_MINUTES = { min: 1, max: 1440, default: 10 } as const;
/** Stop pushing N minutes before the window closes so the last batch still gets scanned. */
export const USPS_SCAN_AUTO_CUTOFF_MARGIN_MINUTES = { min: 0, max: 240, default: 30 } as const;

/**
 * Business-hours anchor. Window clocks are stored as US Eastern wall time so they keep the same
 * business meaning across DST; VN times shown in the UI are derived, never stored.
 */
export const USPS_SCAN_WINDOW_TZ = 'America/New_York';

export function parseUspsScanAutoEnabled(raw: string | undefined | null): boolean {
  return String(raw ?? '').trim().toLowerCase() === 'true';
}

/** Boolean settings default to `true` when unseeded (window gate, Saturday). */
export function parseUspsScanFlagOn(raw: string | undefined | null, fallback: boolean): boolean {
  const value = String(raw ?? '').trim().toLowerCase();
  if (value === 'true') return true;
  if (value === 'false') return false;
  return fallback;
}

/**
 * Normalizes a window clock to `HH:mm`, mirroring backend `UspsScanWindow.ParseClock` so the UI
 * never shows a different window than the job actually uses.
 */
export function parseUspsScanClock(raw: string | undefined | null, fallback: string): string {
  const value = String(raw ?? '').trim();
  if (value === '') return fallback;

  const match = /^(\d{1,2})(?::(\d{1,2}))?$/.exec(value);
  if (!match) return fallback;

  const hour = Number(match[1]);
  const minute = match[2] == null ? 0 : Number(match[2]);
  if (hour > 23 || minute > 59) return fallback;

  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
}

/** Clamps a numeric setting the same way the backend reader does. */
export function parseUspsScanNumber(
  raw: string | undefined | null,
  range: { min: number; max: number; default: number },
): number {
  const parsed = Number(String(raw ?? '').trim());
  if (!Number.isInteger(parsed)) return range.default;
  return Math.min(range.max, Math.max(range.min, parsed));
}
