/** Short trend/axis label: `MM-DD` from an ISO `yyyy-MM-dd` date. */
export const formatDayLabel = (isoDate: string) => {
  const parts = isoDate.split('-');
  if (parts.length !== 3) return isoDate;
  return `${parts[2]}-${parts[1]}`;
};

/** Heatmap col header: day-of-month only (cells are 22px; period ≤ 1 month). */
export const formatHeatmapDayLabel = (isoDate: string) => {
  const parts = isoDate.split('-');
  if (parts.length !== 3) return isoDate;
  return String(Number(parts[2]));
};

/** Display date: `DD-MM-YYYY` from an ISO `yyyy-MM-dd` date. */
export const formatDateLabel = (isoDate: string) => {
  const parts = isoDate.split('-');
  if (parts.length !== 3) return isoDate;
  return `${parts[2]}-${parts[1]}-${parts[0]}`;
};

/** Header range label: single date, or `from – to` when the period spans multiple days. */
export const formatPeriodRange = (fromDate?: string, toDate?: string) => {
  if (!fromDate || !toDate) return null;
  if (fromDate === toDate) return formatDateLabel(toDate);
  return `${formatDateLabel(fromDate)} – ${formatDateLabel(toDate)}`;
};

/** Zero-padded hour label for the 0–23 hourly heatmap axis. */
export const formatHourLabel = (hour: number) => String(hour).padStart(2, '0');

const HEATMAP_LABEL_KEEP = new Set(['Others', 'Unassigned']);

/**
 * Heatmap end-column: first letter of the last word only — e.g. `Đắc Định` → `Đ`,
 * `Minh Nguyễn` → `N`, `Nguyễn Văn An` → `A`, `Tổng` → `T`. Full name stays in `title`.
 */
export const abbreviatePersonName = (name: string) => {
  const trimmed = name.trim().replace(/\s+/g, ' ');
  if (!trimmed) return name;
  if (HEATMAP_LABEL_KEEP.has(trimmed)) return trimmed;
  const parts = trimmed.split(' ');
  const last = parts[parts.length - 1]!;
  const ch = [...last][0];
  return ch ? ch.toLocaleUpperCase('vi-VN') : trimmed;
};
