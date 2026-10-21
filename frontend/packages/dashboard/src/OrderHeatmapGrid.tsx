import { useState, type MouseEvent } from 'react';
import { Empty } from 'antd';
import type { DashboardHeatmapCell } from '@drop/types/dashboard';
import { compareProfileNames } from '@drop/utils/profileNameSort';
import { abbreviatePersonName, formatDayLabel, formatHeatmapDayLabel } from './format';
import { heatmapCellColor, heatmapCellTextColor, heatmapRamp } from './heatmap';
import { useHeatmapScrollToEnd } from './useHeatmapScrollToEnd';

const OTHERS_ENTITY_ID = '__others__';
const OTHERS_GROUP_ID = '__others_group__';

type HeatmapEntity = {
  id: string;
  name: string;
  groupId?: string | null;
  groupName?: string | null;
  publishLimitPublished?: number | null;
  publishLimitTotal?: number | null;
};

type SellerGroup = {
  groupId: string;
  groupName: string;
  shops: HeatmapEntity[];
};

/** Same ascending profile sort as Shops "Theo tên Profile"; Others always last. */
const compareHeatmapShop = (a: HeatmapEntity, b: HeatmapEntity) => {
  if (a.id === OTHERS_ENTITY_ID) return 1;
  if (b.id === OTHERS_ENTITY_ID) return -1;
  const byProfile = compareProfileNames(a.name, b.name);
  if (byProfile !== 0) return byProfile;
  return a.id.localeCompare(b.id);
};

const compareSellerGroup = (a: SellerGroup, b: SellerGroup) => {
  if (a.groupId === OTHERS_GROUP_ID) return 1;
  if (b.groupId === OTHERS_GROUP_ID) return -1;
  const byName = a.groupName.localeCompare(b.groupName, 'vi');
  if (byName !== 0) return byName;
  return a.groupId.localeCompare(b.groupId);
};

const branchOf = (index: number, total: number) => {
  if (total <= 1) return '\u2514\u2500'; // └─
  if (index === 0) return '\u250C\u2500'; // ┌─
  if (index === total - 1) return '\u2514\u2500'; // └─
  return '\u251C\u2500'; // ├─
};

const formatListingLabel = (published?: number | null, total?: number | null) => {
  if (published == null && total == null) return null;
  if (published != null && total != null) {
    return `(${published.toLocaleString('en-US')}/${total.toLocaleString('en-US')})`;
  }
  if (published != null) return `(${published.toLocaleString('en-US')})`;
  return null;
};

/**
 * CSS heatmap: day × entity matrix + totals row.
 * When cells carry `groupId`/`groupName` (admin: shops grouped by seller), rows render as a
 * seller → shop tree; otherwise rows are flat (seller portal: shops only, no grouping).
 *
 * @param endAbbreviatedLabels — seller matrix: thêm cột cuối với `abbreviatePersonName`
 *   để khi cuộn ngang về phải vẫn nhận ra hàng (mobile).
 * @param sortByProfileName — shop matrix: sắp xếp tên shop giống Shops "Theo tên Profile" (asc).
 * @param size — `comfortable` phóng ô/chữ (dashboard seller day cạnh chart cao hơn).
 */
export const OrderHeatmapGrid = ({
  cells,
  endAbbreviatedLabels = false,
  sortByProfileName = false,
  size = 'default',
}: {
  cells: DashboardHeatmapCell[];
  endAbbreviatedLabels?: boolean;
  sortByProfileName?: boolean;
  size?: 'default' | 'comfortable';
}) => {
  const [hoveredRowId, setHoveredRowId] = useState<string | null>(null);
  const scrollRef = useHeatmapScrollToEnd(
    `${cells.length}:${cells[0]?.entityId ?? ''}:${cells[0]?.date ?? ''}:${cells[cells.length - 1]?.date ?? ''}`,
  );
  const isComfortable = size === 'comfortable';

  if (cells.length === 0) {
    return (
      <div className="dashboard-chart-empty" style={{ minHeight: isComfortable ? 220 : 160 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
      </div>
    );
  }

  const dates = [...new Set(cells.map((c) => c.date))].sort();

  const entities: HeatmapEntity[] = [];
  const seen = new Set<string>();
  for (const c of cells) {
    if (seen.has(c.entityId)) continue;
    seen.add(c.entityId);
    entities.push({
      id: c.entityId,
      name: c.entityName,
      groupId: c.groupId,
      groupName: c.groupName,
      publishLimitPublished: c.publishLimitPublished,
      publishLimitTotal: c.publishLimitTotal,
    });
  }

  const hasGroups = entities.some((e) => !!e.groupId && !!e.groupName);
  /** End short-name column only on flat seller matrix (not shop tree). */
  const showEndLabels = endAbbreviatedLabels && !hasGroups;

  let orderedEntities = entities;
  let groups: SellerGroup[] = [];
  if (hasGroups) {
    const byGroup = new Map<string, SellerGroup>();
    for (const entity of entities) {
      const gid = entity.groupId || '__none__';
      let group = byGroup.get(gid);
      if (!group) {
        group = {
          groupId: gid,
          groupName: entity.groupName || 'Seller',
          shops: [],
        };
        byGroup.set(gid, group);
      }
      group.shops.push(entity);
    }
    groups = [...byGroup.values()].map((group) => ({
      ...group,
      shops: sortByProfileName
        ? [...group.shops].sort(compareHeatmapShop)
        : group.shops,
    }));
    if (sortByProfileName) {
      groups.sort(compareSellerGroup);
    }
  } else if (sortByProfileName) {
    orderedEntities = [...entities].sort(compareHeatmapShop);
  }

  const countByKey = new Map<string, number>();
  const totalsByDate = new Map<string, number>();
  let maxOrders = 0;
  let maxTotalOrders = 0;
  for (const c of cells) {
    countByKey.set(`${c.entityId}|${c.date}`, c.orders);
    if (c.orders > maxOrders) maxOrders = c.orders;
    const dayTotal = (totalsByDate.get(c.date) ?? 0) + c.orders;
    totalsByDate.set(c.date, dayTotal);
    if (dayTotal > maxTotalOrders) maxTotalOrders = dayTotal;
  }

  const HEATMAP_RAMP = heatmapRamp();
  const totalRowId = '__total__';

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

  const renderListingBadge = (entity: HeatmapEntity) => {
    const label = formatListingLabel(entity.publishLimitPublished, entity.publishLimitTotal);
    if (!label) return null;
    return (
      <span
        className="dashboard-heatmap-shop-listing"
        title={`Listing active: ${label} (TikTok publish_limit)`}
      >
        {label}
      </span>
    );
  };

  const renderEndLabel = (rowId: string, fullName: string, shortName: string) => (
    <div
      className={`dashboard-heatmap-end-label${hoverClass(rowId)}`}
      title={fullName}
      {...rowHoverProps(rowId)}
    >
      {shortName}
    </div>
  );

  const renderCells = (entity: HeatmapEntity) =>
    dates.map((date) => {
      const orders = countByKey.get(`${entity.id}|${date}`) ?? 0;
      return (
        <div
          key={`${entity.id}|${date}`}
          className={`dashboard-heatmap-cell${hoverClass(entity.id)}`}
          style={{
            backgroundColor: heatmapCellColor(orders, maxOrders),
            color: heatmapCellTextColor(orders, maxOrders),
          }}
          title={`${entity.groupName ? `${entity.groupName} · ` : ''}${entity.name} · ${formatDayLabel(date)}: ${orders} orders`}
          {...rowHoverProps(entity.id)}
        >
          {orders > 0 ? orders : ''}
        </div>
      );
    });

  const renderTotalCells = () =>
    dates.map((date) => {
      const orders = totalsByDate.get(date) ?? 0;
      return (
        <div
          key={`${totalRowId}|${date}`}
          className={`dashboard-heatmap-cell is-total${hoverClass(totalRowId)}`}
          style={{
            backgroundColor: heatmapCellColor(orders, maxTotalOrders),
            color: heatmapCellTextColor(orders, maxTotalOrders),
          }}
          title={`Tổng · ${formatDayLabel(date)}: ${orders} orders`}
          {...rowHoverProps(totalRowId)}
        >
          {orders > 0 ? orders : ''}
        </div>
      );
    });

  const shopLabelTitle = (shop: HeatmapEntity) => {
    const listing = formatListingLabel(shop.publishLimitPublished, shop.publishLimitTotal);
    return listing ? `${shop.name} · Listing ${listing}` : shop.name;
  };

  const endCol = showEndLabels
    ? isComfortable
      ? ' minmax(24px, 32px)'
      : ' minmax(18px, 24px)'
    : '';
  const dayColMin = isComfortable ? 22 : 14;
  const flatLabelCol = isComfortable ? 'minmax(72px, 110px)' : 'minmax(64px, 96px)';
  const groupCols = isComfortable
    ? `minmax(64px, 88px) minmax(110px, 160px) repeat(${dates.length}, minmax(${dayColMin}px, 1fr))`
    : `minmax(56px, 72px) minmax(96px, 140px) repeat(${dates.length}, minmax(${dayColMin}px, 1fr))`;

  return (
    <div className="dashboard-heatmap-scroll" ref={scrollRef}>
      <div
        className={`dashboard-heatmap-grid${hasGroups ? ' is-grouped' : ''}${showEndLabels ? ' has-end-labels' : ''}${isComfortable ? ' is-comfortable' : ''}`}
        style={{
          gridTemplateColumns: hasGroups
            ? groupCols
            : `${flatLabelCol} repeat(${dates.length}, minmax(${dayColMin}px, 1fr))${endCol}`,
        }}
      >
        <div className="dashboard-heatmap-corner" />
        {hasGroups ? <div className="dashboard-heatmap-corner" /> : null}
        {dates.map((date) => (
          <div key={date} className="dashboard-heatmap-col-label" title={date}>
            {formatHeatmapDayLabel(date)}
          </div>
        ))}
        {showEndLabels ? <div className="dashboard-heatmap-corner is-end" /> : null}

        {hasGroups
          ? groups.flatMap((group) =>
              group.shops.map((shop, index) => {
                const isFirst = index === 0;
                const isLast = index === group.shops.length - 1;
                const isOnly = group.shops.length === 1;
                return (
                  <div key={`${group.groupId}:${shop.id}`} className="dashboard-heatmap-row">
                    {isFirst ? (
                      <div
                        className="dashboard-heatmap-seller-label"
                        style={{ gridRow: `span ${group.shops.length}` }}
                        title={group.groupName}
                      >
                        {group.groupName}
                      </div>
                    ) : null}
                    <div
                      className={`dashboard-heatmap-shop-label${isFirst ? ' is-first' : ''}${isLast ? ' is-last' : ''}${isOnly ? ' is-only' : ''}${hoverClass(shop.id)}`}
                      title={shopLabelTitle(shop)}
                      {...rowHoverProps(shop.id)}
                    >
                      <span className="dashboard-heatmap-branch" aria-hidden>
                        {branchOf(index, group.shops.length)}
                      </span>
                      <span className="dashboard-heatmap-shop-name">{shop.name}</span>
                      {renderListingBadge(shop)}
                    </div>
                    {renderCells(shop)}
                  </div>
                );
              }),
            )
          : orderedEntities.map((entity) => (
              <div key={entity.id} className="dashboard-heatmap-row">
                <div
                  className={`dashboard-heatmap-row-label${hoverClass(entity.id)}`}
                  title={entity.name}
                  {...rowHoverProps(entity.id)}
                >
                  <span className="dashboard-heatmap-shop-name">{entity.name}</span>
                  {renderListingBadge(entity)}
                </div>
                {renderCells(entity)}
                {showEndLabels
                  ? renderEndLabel(entity.id, entity.name, abbreviatePersonName(entity.name))
                  : null}
              </div>
            ))}

        <div className="dashboard-heatmap-row is-total">
          {hasGroups ? (
            <div
              className={`dashboard-heatmap-row-label is-total${hoverClass(totalRowId)}`}
              style={{ gridColumn: 'span 2' }}
              title="Tổng"
              {...rowHoverProps(totalRowId)}
            >
              Tổng
            </div>
          ) : (
            <div
              className={`dashboard-heatmap-row-label is-total${hoverClass(totalRowId)}`}
              title="Tổng"
              {...rowHoverProps(totalRowId)}
            >
              Tổng
            </div>
          )}
          {renderTotalCells()}
          {showEndLabels ? renderEndLabel(totalRowId, 'Tổng', abbreviatePersonName('Tổng')) : null}
        </div>
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
