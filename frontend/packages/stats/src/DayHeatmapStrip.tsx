import { Empty } from 'antd';
import { formatDayLabel, formatHeatmapDayLabel, isDarkTheme } from '@drop/dashboard';

const HEATMAP_POS_LIGHT = ['#f0f0f0', '#bae0ff', '#69b1ff', '#4096ff', '#1677ff', '#0958d9'] as const;
const HEATMAP_NEG_LIGHT = ['#f0f0f0', '#ffccc7', '#ffa39e', '#ff7875', '#ff4d4f', '#cf1322'] as const;
/** GitHub-blue contribution ramp on dark surfaces (empty → bright). */
const HEATMAP_POS_DARK = ['#2a2a2a', '#0a3069', '#0550ae', '#0969da', '#218bff', '#58a6ff'] as const;
const HEATMAP_NEG_DARK = ['#2a2a2a', '#5c1a1a', '#8b1e1e', '#c93434', '#f85149', '#ff7b72'] as const;

const intensityColor = (absValue: number, maxAbs: number, palette: readonly string[]) => {
  if (absValue <= 0 || maxAbs <= 0) return palette[0];
  const t = absValue / maxAbs;
  if (t <= 0.2) return palette[1];
  if (t <= 0.4) return palette[2];
  if (t <= 0.6) return palette[3];
  if (t <= 0.8) return palette[4];
  return palette[5];
};

const heatmapCellColor = (value: number, maxAbs: number, dark: boolean) =>
  intensityColor(
    Math.abs(value),
    maxAbs,
    value < 0
      ? dark
        ? HEATMAP_NEG_DARK
        : HEATMAP_NEG_LIGHT
      : dark
        ? HEATMAP_POS_DARK
        : HEATMAP_POS_LIGHT,
  );

const heatmapTextColor = (value: number, maxAbs: number, dark: boolean) => {
  const abs = Math.abs(value);
  if (abs <= 0 || maxAbs <= 0) return 'transparent';
  const t = abs / maxAbs;
  if (dark) return t > 0 ? 'rgba(255,255,255,0.92)' : 'transparent';
  return t >= 0.45 ? '#ffffff' : '#1f1f1f';
};

export type DayHeatmapPoint = {
  date: string;
  value: number;
};

type DayHeatmapStripProps = {
  days: DayHeatmapPoint[];
  /** Tooltip unit suffix, e.g. "orders" */
  unitLabel?: string;
  /** Tooltip value format */
  formatValue?: (value: number) => string;
  /** In-cell value format; defaults to formatValue */
  formatCellValue?: (value: number) => string;
  /** Hide in-cell numbers when more days than this (default 45) */
  maxDaysForCellValue?: number;
};

/**
 * Single-row day strip heatmap (intensity by |value|; red when negative).
 * Parent should subscribe to theme so colors re-evaluate on toggle.
 */
export const DayHeatmapStrip = ({
  days,
  unitLabel = '',
  formatValue = (v) => String(v),
  formatCellValue,
  maxDaysForCellValue = 45,
}: DayHeatmapStripProps) => {
  const dark = isDarkTheme();
  const posPalette = dark ? HEATMAP_POS_DARK : HEATMAP_POS_LIGHT;
  const negPalette = dark ? HEATMAP_NEG_DARK : HEATMAP_NEG_LIGHT;

  if (days.length === 0) {
    return (
      <div className="dashboard-chart-empty" style={{ minHeight: 72 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
      </div>
    );
  }

  const maxAbs = days.reduce((max, d) => Math.max(max, Math.abs(d.value)), 0);
  const showCount = days.length <= maxDaysForCellValue;
  const cellFormat = formatCellValue ?? formatValue;
  const hasNegative = days.some((d) => d.value < 0);

  return (
    <div className="dashboard-heatmap-strip">
      <div className="dashboard-heatmap-scroll">
        <div
          className="dashboard-heatmap-strip-grid"
          style={{
            gridTemplateColumns: `repeat(${days.length}, minmax(22px, 1fr))`,
          }}
        >
          {days.map((d) => (
            <div key={d.date} className="dashboard-heatmap-col-label" title={d.date}>
              {formatHeatmapDayLabel(d.date)}
            </div>
          ))}
          {days.map((d) => {
            const tip = formatValue(d.value);
            const unitSuffix = unitLabel ? ` ${unitLabel}` : '';
            return (
              <div
                key={`${d.date}-cell`}
                className="dashboard-heatmap-strip-cell"
                style={{
                  background: heatmapCellColor(d.value, maxAbs, dark),
                  color: heatmapTextColor(d.value, maxAbs, dark),
                }}
                title={`${formatDayLabel(d.date)}: ${tip}${unitSuffix}`}
              >
                {showCount && d.value !== 0 ? cellFormat(d.value) : ''}
              </div>
            );
          })}
        </div>
      </div>
      <div className="dashboard-heatmap-legend">
        {hasNegative ? (
          <>
            <span>Loss</span>
            {[...negPalette].reverse().map((color) => (
              <span
                key={`neg-${color}`}
                className="dashboard-heatmap-legend-swatch"
                style={{ background: color }}
              />
            ))}
            <span>0</span>
            {posPalette.map((color) => (
              <span
                key={`pos-${color}`}
                className="dashboard-heatmap-legend-swatch"
                style={{ background: color }}
              />
            ))}
            <span>Profit</span>
          </>
        ) : (
          <>
            <span>0</span>
            {posPalette.map((color) => (
              <span
                key={color}
                className="dashboard-heatmap-legend-swatch"
                style={{ background: color }}
              />
            ))}
            <span>Max</span>
          </>
        )}
      </div>
    </div>
  );
};
