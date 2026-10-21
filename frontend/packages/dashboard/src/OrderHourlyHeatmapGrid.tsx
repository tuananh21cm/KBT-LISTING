import { useState, type MouseEvent } from 'react';
import { Empty } from 'antd';
import type { DashboardHourlyHeatmapCell } from '@drop/types/dashboard';
import { abbreviatePersonName, formatHourLabel } from './format';
import { heatmapCellColor, heatmapCellTextColor, heatmapRamp } from './heatmap';
import { useHeatmapScrollToEnd } from './useHeatmapScrollToEnd';

const HOURS_0_23 = Array.from({ length: 24 }, (_, h) => h);

/**
 * Hour (0–23) order-count matrix. `sellers` is optional per-entity breakdown (admin); when
 * omitted only the `totals` ribbon renders (seller portal — single-seller scope).
 * Trailing column repeats abbreviated seller names so mobile scroll-to-end still identifies rows.
 */
export const OrderHourlyHeatmapGrid = ({
  sellers = [],
  totals,
}: {
  sellers?: DashboardHourlyHeatmapCell[];
  totals: DashboardHourlyHeatmapCell[];
}) => {
  const [hoveredRowId, setHoveredRowId] = useState<string | null>(null);
  const showEndLabels = sellers.length > 0;
  const scrollRef = useHeatmapScrollToEnd(
    `${sellers.length}:${totals.length}:${sellers[0]?.entityId ?? ''}:${totals[0]?.hour ?? ''}`,
  );

  if (sellers.length === 0 && totals.length === 0) {
    return (
      <div className="dashboard-chart-empty" style={{ minHeight: 120 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
      </div>
    );
  }

  const entities: { id: string; name: string }[] = [];
  const seen = new Set<string>();
  for (const c of sellers) {
    if (seen.has(c.entityId)) continue;
    seen.add(c.entityId);
    entities.push({ id: c.entityId, name: c.entityName });
  }

  const countByKey = new Map<string, number>();
  let maxSellerOrders = 0;
  for (const c of sellers) {
    countByKey.set(`${c.entityId}|${c.hour}`, c.orders);
    if (c.orders > maxSellerOrders) maxSellerOrders = c.orders;
  }

  const totalsByHour = new Map<number, number>();
  let maxTotalOrders = 0;
  for (const c of totals) {
    totalsByHour.set(c.hour, c.orders);
    if (c.orders > maxTotalOrders) maxTotalOrders = c.orders;
  }

  const HEATMAP_RAMP = heatmapRamp();
  const totalRowId = '__all__';

  const rowHoverProps = (rowId: string) => ({
    'data-heatmap-row': rowId,
    onMouseEnter: () => setHoveredRowId(rowId),
    onMouseLeave: (e: MouseEvent<HTMLElement>) => {
      const next = e.relatedTarget;
      if (next instanceof Element && next.closest(`[data-heatmap-row="${CSS.escape(rowId)}"]`)) {
        return;
      }
      setHoveredRowId((cur: string | null) => (cur === rowId ? null : cur));
    },
  });

  const hoverClass = (rowId: string) => (hoveredRowId === rowId ? ' is-row-hover' : '');

  const renderHourCells = (
    rowKey: string,
    label: string,
    getOrders: (hour: number) => number,
    maxOrders: number,
    isTotal: boolean,
  ) =>
    HOURS_0_23.map((hour) => {
      const orders = getOrders(hour);
      return (
        <div
          key={`${rowKey}|${hour}`}
          className={`dashboard-heatmap-cell${isTotal ? ' is-total' : ''}${hoverClass(rowKey)}`}
          style={{
            backgroundColor: heatmapCellColor(orders, maxOrders),
            color: heatmapCellTextColor(orders, maxOrders),
          }}
          title={`${label} · ${formatHourLabel(hour)}:00–${formatHourLabel(hour)}:59: ${orders} đơn`}
          {...rowHoverProps(rowKey)}
        >
          {orders > 0 ? orders : ''}
        </div>
      );
    });

  const renderEndLabel = (rowId: string, fullName: string, shortName: string) => (
    <div
      className={`dashboard-heatmap-end-label${rowId === totalRowId ? ' is-total' : ''}${hoverClass(rowId)}`}
      title={fullName}
      {...rowHoverProps(rowId)}
    >
      {shortName}
    </div>
  );

  const endCol = showEndLabels ? ' minmax(18px, 24px)' : '';

  return (
    <div className="dashboard-heatmap-scroll" ref={scrollRef}>
      <div
        className={`dashboard-heatmap-grid is-hourly${showEndLabels ? ' has-end-labels' : ''}`}
        style={{
          gridTemplateColumns: `minmax(56px, 80px) repeat(24, minmax(12px, 1fr))${endCol}`,
        }}
      >
        <div className="dashboard-heatmap-corner" />
        {HOURS_0_23.map((hour) => (
          <div key={hour} className="dashboard-heatmap-col-label" title={`${formatHourLabel(hour)}:00`}>
            {hour % 3 === 0 ? formatHourLabel(hour) : ''}
          </div>
        ))}
        {showEndLabels ? <div className="dashboard-heatmap-corner is-end" /> : null}

        {entities.map((entity) => (
          <div key={entity.id} className="dashboard-heatmap-row">
            <div
              className={`dashboard-heatmap-row-label${hoverClass(entity.id)}`}
              title={entity.name}
              {...rowHoverProps(entity.id)}
            >
              {entity.name}
            </div>
            {renderHourCells(
              entity.id,
              entity.name,
              (hour) => countByKey.get(`${entity.id}|${hour}`) ?? 0,
              maxSellerOrders,
              false,
            )}
            {showEndLabels
              ? renderEndLabel(entity.id, entity.name, abbreviatePersonName(entity.name))
              : null}
          </div>
        ))}

        {totals.length > 0 ? (
          <div className="dashboard-heatmap-row is-total">
            <div
              className={`dashboard-heatmap-row-label is-total${hoverClass(totalRowId)}`}
              title="Tổng"
              {...rowHoverProps(totalRowId)}
            >
              Tổng
            </div>
            {renderHourCells(
              totalRowId,
              'Tổng',
              (hour) => totalsByHour.get(hour) ?? 0,
              maxTotalOrders,
              true,
            )}
            {showEndLabels ? renderEndLabel(totalRowId, 'Tổng', abbreviatePersonName('Tổng')) : null}
          </div>
        ) : null}
      </div>
      <div className="dashboard-heatmap-legend">
        <span>0</span>
        {HEATMAP_RAMP.map((color) => (
          <span
            key={color}
            className="dashboard-heatmap-legend-swatch"
            style={{ background: color }}
          />
        ))}
        <span>Max</span>
      </div>
    </div>
  );
};
