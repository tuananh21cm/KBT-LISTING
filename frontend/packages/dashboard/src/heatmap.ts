import { isDarkTheme } from './theme';

/** GitHub-style contribution ramp shared by day/hour heatmaps. */
export const heatmapRamp = () =>
  isDarkTheme()
    ? (['#2a2a2a', '#0a3069', '#0550ae', '#0969da', '#218bff', '#58a6ff'] as const)
    : (['#f0f0f0', '#bae0ff', '#69b1ff', '#4096ff', '#1677ff', '#0958d9'] as const);

export const heatmapCellLevel = (orders: number, maxOrders: number) => {
  if (orders <= 0 || maxOrders <= 0) return 0;
  const t = orders / maxOrders;
  if (t <= 0.2) return 1;
  if (t <= 0.4) return 2;
  if (t <= 0.6) return 3;
  if (t <= 0.8) return 4;
  return 5;
};

export const heatmapCellColor = (orders: number, maxOrders: number) =>
  heatmapRamp()[heatmapCellLevel(orders, maxOrders)];

export const heatmapCellTextColor = (orders: number, maxOrders: number) => {
  const level = heatmapCellLevel(orders, maxOrders);
  if (level === 0) return 'transparent';
  if (isDarkTheme()) return level >= 1 ? 'rgba(255,255,255,0.92)' : 'transparent';
  return level >= 3 ? '#ffffff' : '#1f1f1f';
};
