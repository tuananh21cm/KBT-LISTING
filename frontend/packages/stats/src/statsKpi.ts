import { isDarkTheme } from '@drop/theme';

/** KPI icon chip — light surfaces vs translucent chips on dark cards. */
export type StatsKpiTone = 'blue' | 'green' | 'cyan' | 'orange' | 'red' | 'purple';

const KPI_TONES_LIGHT: Record<StatsKpiTone, { iconBg: string; iconColor: string }> = {
  blue: { iconBg: '#e6f4ff', iconColor: '#1677ff' },
  green: { iconBg: '#f6ffed', iconColor: '#52c41a' },
  cyan: { iconBg: '#e6fffb', iconColor: '#13c2c2' },
  orange: { iconBg: '#fff7e6', iconColor: '#fa8c16' },
  red: { iconBg: '#fff1f0', iconColor: '#f5222d' },
  purple: { iconBg: '#f9f0ff', iconColor: '#722ed1' },
};

const KPI_TONES_DARK: Record<StatsKpiTone, { iconBg: string; iconColor: string }> = {
  blue: { iconBg: 'rgba(22, 119, 255, 0.2)', iconColor: '#69b1ff' },
  green: { iconBg: 'rgba(82, 196, 26, 0.2)', iconColor: '#95de64' },
  cyan: { iconBg: 'rgba(19, 194, 194, 0.2)', iconColor: '#5cdbd3' },
  orange: { iconBg: 'rgba(250, 140, 22, 0.2)', iconColor: '#ffc069' },
  red: { iconBg: 'rgba(245, 34, 45, 0.2)', iconColor: '#ff7875' },
  purple: { iconBg: 'rgba(114, 46, 209, 0.2)', iconColor: '#b37feb' },
};

export const statsKpiTone = (tone: StatsKpiTone) =>
  (isDarkTheme() ? KPI_TONES_DARK : KPI_TONES_LIGHT)[tone];

/** Negative money / margin / debit (chi tiêu) on KPI values. */
export const statsNegativeColor = () => (isDarkTheme() ? '#ff7875' : '#cf1322');

/** Positive money / credit (nạp) on KPI values. */
export const statsPositiveColor = () => (isDarkTheme() ? '#95de64' : '#389e0d');
