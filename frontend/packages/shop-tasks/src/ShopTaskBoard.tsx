import { useEffect, useMemo, useState, type MouseEvent, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert,
  Avatar,
  DatePicker,
  Empty,
  Input,
  Popover,
  Progress,
  Select,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import {
  CheckCircleOutlined,
  EyeOutlined,
  PlusCircleOutlined,
  ScissorOutlined,
  ShopOutlined,
  ShoppingOutlined,
  UnorderedListOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import type { ColumnsType, TableProps } from 'antd/es/table';
import dayjs from 'dayjs';
import {
  DashboardKpiStrip,
  dashboardKpiTone,
  type DashboardKpiItem,
  type DashboardKpiTone,
} from '@drop/dashboard';
import '@drop/dashboard/dashboard.css';
import type { ShopTaskItem, ShopTaskShop } from '@drop/types/shopTask';
import { SHOP_STATUS_COLORS, SHOP_STATUS_LABELS } from '@drop/types/shop';
import { PageContentSpin, ShopScoreBadges, useIsMobile } from '@drop/ui';
import {
  APP_TZ,
  DATETIME_FORMAT_SHORT,
  formatDateTimeVn,
  nowVn,
  parseUtcInstant,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { ShopTasksPortalConfig } from './portalConfig';
import { useShopTasksDeps } from './ShopTasksDepsContext';
import { ShopTaskDrawer } from './ShopTaskDrawer';
import { ShopHealthIndicators, type HealthTabKey } from './ShopHealthIndicators';
import { ShopHealthRecordsModal } from './ShopHealthRecordsModal';
import { ShopDelistListingsModal } from './ShopDelistListingsModal';
import { statusMeta, taskShortLabel, isDailyMissionTask, delistChipTone, delistChipLabel } from './shopTaskStatus';
import { ownerLabel, shopTitleLabel } from './shopTaskLabels';
import './shopTasks.css';

const { Text } = Typography;

const kpiPercentSuffix = (percent: number, tone: DashboardKpiTone) => (
  <span style={{ color: dashboardKpiTone(tone).iconColor }}>{percent}%</span>
);

/** `đã list / giới hạn` — số đã list đậm, phần giới hạn nhỏ hơn + xám để khỏi lẫn. */
const kpiRatioValue = (current: number, total: number) => {
  const fmt = (n: number) => n.toLocaleString('en-US');
  return (
    <span className="shop-task-kpi-ratio">
      <span className="shop-task-kpi-ratio-main">{fmt(current)}</span>
      <span className="shop-task-kpi-ratio-rest">/{fmt(total)}</span>
    </span>
  );
};

/**
 * Quy đổi số listing → «shop tương đương» theo trung bình đã list/shop.
 * Ví dụ: 454 cần list ÷ 95 listing/shop ≈ lãng phí 4.8 shop.
 */
const kpiWasteShopSub = (listingCount: number, avgListingsPerShop: number) => {
  if (listingCount <= 0 || avgListingsPerShop <= 0) return undefined;
  const wasteShops = listingCount / avgListingsPerShop;
  if (wasteShops < 0.1) return undefined;
  return (
    <span className="is-negative shop-task-kpi-waste">
      Lãng phí {wasteShops.toFixed(1)} shop
    </span>
  );
};

const DATE_FORMAT = 'YYYY-MM-DD';
const DEFAULT_PAGE_SIZE = 50;

type OptimizeSortKey =
  | 'analyticsGmv'
  | 'analyticsImpressions'
  | 'analyticsCtr'
  | 'analyticsClicks'
  | 'analyticsAtc'
  | 'analyticsOrders';

type OptimizeSortState = {
  key: OptimizeSortKey;
  order: 'ascend' | 'descend';
};

const DEFAULT_OPTIMIZE_SORT: OptimizeSortState = {
  key: 'analyticsImpressions',
  order: 'descend',
};

const OPTIMIZE_SORT_KEYS: OptimizeSortKey[] = [
  'analyticsGmv',
  'analyticsImpressions',
  'analyticsCtr',
  'analyticsClicks',
  'analyticsAtc',
  'analyticsOrders',
];

const optimizeSortValue = (row: ShopTaskShop, key: OptimizeSortKey): number | null | undefined => {
  switch (key) {
    case 'analyticsGmv':
      return row.analyticsGmv;
    case 'analyticsImpressions':
      return row.analyticsImpressions;
    case 'analyticsCtr':
      return row.analyticsCtr;
    case 'analyticsClicks':
      return row.analyticsClicks;
    case 'analyticsAtc':
      return row.analyticsAddToCartUnits;
    case 'analyticsOrders':
      return row.analyticsOrders;
  }
};

export type ShopTaskBoardMode = 'daily' | 'optimize';

const taskProgressPercent = (completedCount: number, totalCount: number): number =>
  totalCount > 0 ? Math.round((completedCount / totalCount) * 100) : 0;

const slotProgressPercent = (published: number, total: number): number =>
  total > 0 ? Math.min(100, Math.round((published / total) * 100)) : 0;

const formatInt = (value?: number | null): string =>
  value == null ? '—' : Math.round(value).toLocaleString('en-US');

const formatRate = (value?: number | null): string =>
  value == null ? '—' : `${(value * 100).toFixed(2)}%`;

const formatMoney = (value?: number | null, currency?: string | null): string => {
  if (value == null) return '—';
  const code = currency?.trim() || 'USD';
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: code,
      minimumFractionDigits: 0,
      maximumFractionDigits: value < 100 ? 2 : 0,
    }).format(value);
  } catch {
    return `${value.toLocaleString('en-US')} ${code}`;
  }
};

/** Null/undefined xếp thấp nhất (cuối khi sort desc metric). */
const compareNullableNumber = (a?: number | null, b?: number | null): number =>
  (a ?? Number.NEGATIVE_INFINITY) - (b ?? Number.NEGATIVE_INFINITY);

/** Số ngày lịch VN kể từ lần sync Compass; null khi chưa có dữ liệu. */
const analyticsSyncAgeDays = (syncedAt?: string | null): number | null => {
  if (syncedAt == null || syncedAt === '') return null;
  const synced = parseUtcInstant(syncedAt);
  if (!synced.isValid()) return null;
  return nowVn().startOf('day').diff(synced.tz(APP_TZ).startOf('day'), 'day');
};

type SyncTone = 'fresh' | 'stale' | 'old';

const syncTone = (ageDays: number | null): SyncTone | null => {
  if (ageDays == null) return null;
  if (ageDays <= 1) return 'fresh';
  if (ageDays === 2) return 'stale';
  return 'old';
};

const AnalyticsSyncCell = ({ syncedAt }: { syncedAt?: string | null }) => {
  const ageDays = analyticsSyncAgeDays(syncedAt);
  const tone = syncTone(ageDays);
  if (tone == null) {
    return (
      <Text type="secondary" style={{ fontSize: 11 }}>
        —
      </Text>
    );
  }

  return (
    <Tooltip title={formatDateTimeVn(syncedAt)}>
      <span className={`shop-task-sync is-${tone}`}>
        {formatDateTimeVn(syncedAt, DATETIME_FORMAT_SHORT)} ({ageDays}d)
      </span>
    </Tooltip>
  );
};

/** Format board-day online seconds as `2h 15m` / `45m` / `—`. */
const formatTodayOnline = (seconds?: number | null): string => {
  if (seconds == null || seconds <= 0) return '—';
  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  if (hours <= 0) return `${minutes}m`;
  if (minutes <= 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
};

const OnlineCell = ({ seconds }: { seconds?: number | null }) => (
  <Tooltip title="Tổng thời gian online trong ngày board (VN) — tab seller-us">
    <Text style={{ fontVariantNumeric: 'tabular-nums' }}>{formatTodayOnline(seconds)}</Text>
  </Tooltip>
);

const AnalyticsMetricCell = ({ children }: { children: ReactNode }) => (
  <span className="shop-task-analytics-metric">{children}</span>
);

type ProgressTone = 'danger' | 'warning' | 'primary' | 'success';

/** <30% nguy hiểm · 30–90% cảnh báo · >90% bình thường · 100% xong. */
const progressTone = (percent: number): ProgressTone => {
  if (percent >= 100) return 'success';
  if (percent > 90) return 'primary';
  if (percent >= 30) return 'warning';
  return 'danger';
};

const progressStrokeColor = (percent: number): string => {
  switch (progressTone(percent)) {
    case 'success':
      return 'var(--ant-color-success, #52c41a)';
    case 'primary':
      return 'var(--ant-color-primary, #1677ff)';
    case 'warning':
      return 'var(--ant-color-warning, #faad14)';
    default:
      return 'var(--ant-color-error, #ff4d4f)';
  }
};

const ShopTaskMetricProgress = ({
  percent,
  format,
}: {
  percent: number;
  format: (value: number | undefined) => ReactNode;
}) => (
  <div className={`shop-task-progress is-tone-${progressTone(percent)}`}>
    <Progress
      percent={percent}
      size="small"
      showInfo
      format={format}
      strokeColor={progressStrokeColor(percent)}
    />
  </div>
);

const TaskChip = ({
  task,
  delistCount,
  reviewCount,
  onOpenDelist,
}: {
  task: ShopTaskItem;
  delistCount?: number | null;
  reviewCount?: number | null;
  onOpenDelist?: (event: { stopPropagation: () => void; preventDefault: () => void }) => void;
}) => {
  const meta = statusMeta(task.status);
  const isDelistTask = task.key === 'delist_cleanup';
  const isDelist = isDelistTask && onOpenDelist != null;
  const resolvedDelist = delistCount ?? task.current;
  const resolvedReview = reviewCount;
  const chipTone = isDelistTask
    ? delistChipTone(resolvedDelist, resolvedReview)
    : meta.tone;
  const chipIcon =
    isDelistTask && ((resolvedDelist ?? 0) > 0 || (resolvedReview ?? 0) > 0)
      ? statusMeta('attention').icon
      : meta.icon;
  const chipLabel = isDelistTask
    ? delistChipLabel(resolvedDelist, resolvedReview)
    : taskShortLabel(task);

  return (
    <Tooltip title={`${meta.label}: ${task.title} — ${task.detail}`}>
      <Tag
        color={chipTone}
        icon={chipIcon}
        className={`app-tag-compact shop-task-chip${isDelist ? ' is-clickable' : ''}`}
        role={isDelist ? 'button' : undefined}
        tabIndex={isDelist ? 0 : undefined}
        onClick={
          isDelist
            ? (event) => {
                event.stopPropagation();
                onOpenDelist(event);
              }
            : undefined
        }
        onKeyDown={
          isDelist
            ? (event) => {
                if (event.key !== 'Enter' && event.key !== ' ') return;
                event.preventDefault();
                event.stopPropagation();
                onOpenDelist(event);
              }
            : undefined
        }
      >
        {chipLabel}
      </Tag>
    </Tooltip>
  );
};

const boardTasksForMode = (tasks: ShopTaskItem[], mode: ShopTaskBoardMode): ShopTaskItem[] =>
  tasks.filter((task) =>
    mode === 'optimize' ? task.group === 'optimize' : isDailyMissionTask(task),
  );

const completedTaskCount = (tasks: ShopTaskItem[]): number =>
  tasks.filter((task) => task.status === 'done').length;

/** Ô cột Nhiệm vụ: `x/y` gọn; hover (desktop) / tap (mobile) mở panel chip chi tiết. */
const TaskChipsSummary = ({
  tasks,
  delistCount,
  reviewCount,
  onOpenDelist,
  interaction,
}: {
  tasks: ShopTaskItem[];
  delistCount?: number | null;
  reviewCount?: number | null;
  onOpenDelist?: (event: { stopPropagation: () => void; preventDefault: () => void }) => void;
  interaction: 'hover' | 'click';
}) => {
  const total = tasks.length;
  if (total === 0) {
    return (
      <Text type="secondary" style={{ fontSize: 11 }}>
        —
      </Text>
    );
  }

  // Optimize chỉ có `delist_cleanup`: backend vẫn `done` khi còn slot trống nên `1/1`
  // che mất «Cần hủy N · Cần xem xét M» — hiện chip trực tiếp, click mở modal.
  const soleDelist = total === 1 && tasks[0]?.key === 'delist_cleanup' ? tasks[0] : null;
  if (soleDelist) {
    return (
      <TaskChip
        task={soleDelist}
        delistCount={delistCount}
        reviewCount={reviewCount}
        onOpenDelist={onOpenDelist}
      />
    );
  }

  const completed = completedTaskCount(tasks);
  const percent = taskProgressPercent(completed, total);
  const tone = progressTone(percent);

  const panel = (
    <div
      className="shop-task-chips-panel"
      onClick={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
    >
      <div className="shop-task-chips-panel-head">
        {completed}/{total} hoàn thành
      </div>
      <div className="shop-task-chips">
        {tasks.map((task) => (
          <TaskChip
            key={`${task.key}-${task.definitionId ?? ''}`}
            task={task}
            delistCount={delistCount}
            reviewCount={reviewCount}
            onOpenDelist={onOpenDelist}
          />
        ))}
      </div>
    </div>
  );

  return (
    <Popover
      content={panel}
      trigger={interaction}
      placement="leftTop"
      mouseEnterDelay={0.2}
      mouseLeaveDelay={0.12}
      arrow={false}
      rootClassName="shop-task-chips-popover"
      destroyOnHidden
    >
      <span
        className={`shop-task-tasks-summary is-tone-${tone}`}
        role="button"
        tabIndex={0}
        aria-label={`${completed} trên ${total} nhiệm vụ hoàn thành`}
        onClick={(event) => {
          // Hover: chặn mở drawer khi bấm vào số. Click: Popover cần nhận click.
          if (interaction === 'hover') event.stopPropagation();
        }}
      >
        <span className="shop-task-tasks-summary-main">{completed}</span>
        <span className="shop-task-tasks-summary-rest">/{total}</span>
      </span>
    </Popover>
  );
};

const formatPromoRemainingHours = (endTimeUtc: string): string => {
  const end = parseUtcInstant(endTimeUtc);
  if (!end.isValid()) return 'Có';
  const hoursLeft = end.diff(dayjs(), 'hour', true);
  if (hoursLeft <= 0) return '0h';
  if (hoursLeft < 1) return '<1h';
  return `${Math.ceil(hoursLeft)}h`;
};

const PromotionRunningCell = ({
  active,
  beginTimeUtc,
  endTimeUtc,
}: {
  active?: boolean;
  beginTimeUtc?: string | null;
  endTimeUtc?: string | null;
}) => {
  if (!active) {
    return (
      <Text type="secondary" style={{ fontSize: 11 }}>
        —
      </Text>
    );
  }

  const label = endTimeUtc ? formatPromoRemainingHours(endTimeUtc) : 'Có';
  const tag = (
    <Tag color="success" className="app-tag-compact">
      {label}
    </Tag>
  );

  if (!beginTimeUtc && !endTimeUtc) {
    return tag;
  }

  return (
    <Tooltip
      title={
        <div>
          <div>Bắt đầu: {formatDateTimeVn(beginTimeUtc)}</div>
          <div>Kết thúc: {formatDateTimeVn(endTimeUtc)}</div>
        </div>
      }
    >
      {tag}
    </Tooltip>
  );
};

/** Tag trạng thái ops — chỉ hiện khi shop không Active. */
const ShopOpsStatusTag = ({ status }: { status?: string | null }) => {
  const value = (status || '').trim();
  if (!value || value === 'Active') return null;
  return (
    <Tag
      color={SHOP_STATUS_COLORS[value] ?? 'default'}
      className="app-tag-compact shop-task-shop-status-tag"
    >
      {SHOP_STATUS_LABELS[value] ?? value}
    </Tag>
  );
};

const ShopIdentity = ({
  row,
  showOwner,
  onOpenHealth,
  onClick,
}: {
  row: ShopTaskShop;
  showOwner: boolean;
  onOpenHealth: (tab: HealthTabKey, event: MouseEvent) => void;
  onClick?: (event: MouseEvent) => void;
}) => (
  <div className="shop-task-cell" onClick={onClick}>
    <ShopHealthIndicators
      variant="avatar"
      summary={row.healthSummary}
      onOpen={onOpenHealth}
    >
      <Avatar
        className="shop-task-shop-avatar"
        src={row.avatarUrl || undefined}
        shape="square"
        size={34}
        icon={<ShopOutlined />}
        alt={row.shopName}
      />
    </ShopHealthIndicators>
    <div className="shop-task-shop-body">
      <div className="app-list-line app-list-line--primary shop-task-shop-title">
        <span>{shopTitleLabel(row, showOwner)}</span>
        <ShopOpsStatusTag status={row.shopStatus} />
      </div>
      <ShopScoreBadges scores={row} detail="full" className="shop-task-shop-scores" />
    </div>
  </div>
);

const ShopTaskMobileCard = ({
  row,
  mode,
  showOwner,
  onOpenHealth,
  onOpenDelist,
}: {
  row: ShopTaskShop;
  mode: ShopTaskBoardMode;
  showOwner: boolean;
  onOpenHealth: (tab: HealthTabKey, event: MouseEvent) => void;
  onOpenDelist?: (
    shop: ShopTaskShop,
    event: { stopPropagation: () => void; preventDefault: () => void },
  ) => void;
}) => {
  const published = row.publishLimitPublished ?? 0;
  const slotTotal = row.publishLimitTotal;
  const slotPercent = slotTotal == null ? 0 : slotProgressPercent(published, slotTotal);
  const tasks = boardTasksForMode(row.tasks, mode);

  return (
    <div className="shop-task-mobile-card">
      <div className="shop-task-mobile-card-head">
        <ShopIdentity row={row} showOwner={showOwner} onOpenHealth={onOpenHealth} />
      </div>
      <div className={`shop-task-mobile-metrics${mode === 'optimize' ? ' is-optimize' : ''}`}>
        <div className="shop-task-mobile-metric">
          <span className="shop-task-mobile-metric-label">Sync</span>
          <AnalyticsSyncCell syncedAt={row.analyticsSyncedAt} />
        </div>
        <div className="shop-task-mobile-metric">
          <span className="shop-task-mobile-metric-label">Online</span>
          <OnlineCell seconds={row.todayOnlineSeconds} />
        </div>
        {mode === 'optimize' ? (
          <>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">GMV</span>
              <AnalyticsMetricCell>
                {formatMoney(row.analyticsGmv, row.analyticsCurrency)}
              </AnalyticsMetricCell>
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">Impressions</span>
              <AnalyticsMetricCell>{formatInt(row.analyticsImpressions)}</AnalyticsMetricCell>
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">CTR</span>
              <AnalyticsMetricCell>{formatRate(row.analyticsCtr)}</AnalyticsMetricCell>
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">Clicks</span>
              <AnalyticsMetricCell>{formatInt(row.analyticsClicks)}</AnalyticsMetricCell>
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label" title="Add-to-cart">
                ATC
              </span>
              <AnalyticsMetricCell>{formatInt(row.analyticsAddToCartUnits)}</AnalyticsMetricCell>
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">Orders</span>
              <AnalyticsMetricCell>{formatInt(row.analyticsOrders)}</AnalyticsMetricCell>
            </div>
          </>
        ) : (
          <div className="shop-task-mobile-metric">
            <span className="shop-task-mobile-metric-label">Tiến độ</span>
            <ShopTaskMetricProgress
              percent={taskProgressPercent(row.completedCount, row.totalCount)}
              format={(value) => `${value}%`}
            />
          </div>
        )}
        <div className="shop-task-mobile-metric">
          <span className="shop-task-mobile-metric-label">Listing</span>
          {slotTotal == null ? (
            <Text type="secondary" style={{ fontSize: 11 }}>
              —
            </Text>
          ) : (
            <ShopTaskMetricProgress
              percent={slotPercent}
              format={(value) => `${value}% (${published}/${slotTotal})`}
            />
          )}
        </div>
        {mode === 'optimize' ? null : (
          <>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">PD</span>
              <PromotionRunningCell
                active={row.hasProductDiscount}
                beginTimeUtc={row.productDiscountBeginTimeUtc}
                endTimeUtc={row.productDiscountEndTimeUtc}
              />
            </div>
            <div className="shop-task-mobile-metric">
              <span className="shop-task-mobile-metric-label">Flash</span>
              <PromotionRunningCell
                active={row.hasFlashDeal}
                beginTimeUtc={row.flashDealBeginTimeUtc}
                endTimeUtc={row.flashDealEndTimeUtc}
              />
            </div>
            <div className="shop-task-mobile-metric is-order">
              <span className="shop-task-mobile-metric-label">Order</span>
              <Tooltip
                title={
                  <div className="shop-task-order-tooltip">
                    <div>Đã giao: {row.deliveredOrderCount ?? 0}</div>
                    <div>Refund: {row.refundOrderCount ?? 0}</div>
                  </div>
                }
              >
                <span className="shop-task-order-total">
                  {(row.deliveredOrderCount ?? 0) + (row.refundOrderCount ?? 0)}
                </span>
              </Tooltip>
            </div>
          </>
        )}
      </div>
      {tasks.length > 0 ? (
        <div className="shop-task-mobile-chips">
          <TaskChipsSummary
            tasks={tasks}
            delistCount={row.delistCount}
            reviewCount={row.reviewCount}
            onOpenDelist={
              onOpenDelist ? (event) => onOpenDelist(row, event) : undefined
            }
            interaction="click"
          />
        </div>
      ) : null}
    </div>
  );
};

export type ShopTaskBoardProps = {
  config: ShopTasksPortalConfig;
  mode: ShopTaskBoardMode;
};

/**
 * Bảng nhiệm vụ theo shop — dùng chung cho trang Tương tác (daily) và Tối ưu listing (optimize).
 */
export const ShopTaskBoard = ({ config, mode }: ShopTaskBoardProps) => {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();
  const [searchParams, setSearchParams] = useSearchParams();

  const date = searchParams.get('date') || dayjs().format(DATE_FORMAT);
  const ownerId = config.showOwnerFilter ? searchParams.get('ownerId') || undefined : undefined;
  const search = searchParams.get('q')?.trim() || undefined;

  const [searchInput, setSearchInput] = useState(search ?? '');
  useEffect(() => {
    setSearchInput(search ?? '');
  }, [search]);

  const [selected, setSelected] = useState<ShopTaskShop | null>(null);
  const [healthModal, setHealthModal] = useState<{
    shop: ShopTaskShop;
    tab: HealthTabKey;
  } | null>(null);
  const [delistShop, setDelistShop] = useState<ShopTaskShop | null>(null);
  const [markingRecordId, setMarkingRecordId] = useState<string | null>(null);
  const markHealthHandledMutation = deps.useMarkHealthRecordHandled?.();
  const canManageListings = deps.hasPermission(config.listingsManagePermission);
  const [pagination, setPagination] = useState({ current: 1, pageSize: DEFAULT_PAGE_SIZE });
  const [optimizeSort, setOptimizeSort] = useState<OptimizeSortState>(DEFAULT_OPTIMIZE_SORT);

  useEffect(() => {
    setPagination((prev) => ({ ...prev, current: 1 }));
  }, [mode, date, ownerId, search]);

  useEffect(() => {
    if (mode === 'optimize') setOptimizeSort(DEFAULT_OPTIMIZE_SORT);
  }, [mode]);

  const patchParams = (patch: Record<string, string | undefined>) => {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.delete('tab');
        for (const [key, value] of Object.entries(patch)) {
          if (value == null || value === '') next.delete(key);
          else next.set(key, value);
        }
        return next;
      },
      { replace: true },
    );
  };

  const boardQuery = deps.useTaskBoard({ date, ownerId, search });
  const ownersQuery = config.showOwnerFilter ? deps.useOwners?.() : undefined;

  const board = boardQuery.data;
  const shops = useMemo(() => board?.shops ?? [], [board]);

  const visibleShops = useMemo(() => {
    if (mode !== 'optimize') return shops;

    const factor = optimizeSort.order === 'ascend' ? 1 : -1;
    return [...shops].sort(
      (a, b) =>
        factor *
        compareNullableNumber(
          optimizeSortValue(a, optimizeSort.key),
          optimizeSortValue(b, optimizeSort.key),
        ),
    );
  }, [shops, mode, optimizeSort]);

  const optimizeSortOrderFor = (key: OptimizeSortKey) =>
    optimizeSort.key === key ? optimizeSort.order : null;

  useEffect(() => {
    if (!selected) return;
    const fresh = shops.find((s) => s.shopId === selected.shopId);
    if (fresh && fresh !== selected) setSelected(fresh);
    if (!fresh) setSelected(null);
  }, [shops, selected]);

  const summary = board?.summary;

  const kpiItems = useMemo((): DashboardKpiItem[] => {
    if (!summary) return [];

    const colProps = { xs: 8, sm: 8, flex: '1 1 160px' };
    const compact = isMobile;

    if (mode === 'optimize') {
      const optimizeColProps = { xs: 12, sm: 6, flex: '1 1 160px' };
      const published = summary.publishLimitPublishedSum ?? 0;
      const limit = summary.publishLimitTotalSum ?? 0;
      const needList = Math.max(0, limit - published);
      const fillPercent = limit > 0 ? Math.round((published / limit) * 100) : 0;
      const emptyPercent = limit > 0 ? Math.round((needList / limit) * 100) : 0;
      const delist = summary.delistCountSum ?? 0;
      const review = summary.reviewCountSum ?? 0;
      const delistPercent = published > 0 ? Math.round((delist / published) * 100) : 0;
      const reviewPercent = published > 0 ? Math.round((review / published) * 100) : 0;
      const avgListingsPerShop =
        summary.shopCount > 0 && published > 0 ? published / summary.shopCount : 0;
      const fmt = (n: number) => n.toLocaleString('en-US');
      const listingTone: DashboardKpiTone = 'blue';
      const needListTone: DashboardKpiTone = needList > 0 ? 'orange' : 'green';
      const delistTone: DashboardKpiTone = delist > 0 ? 'red' : 'green';
      const reviewTone: DashboardKpiTone = review > 0 ? 'orange' : 'green';

      return [
        {
          key: 'listing',
          label: compact ? 'Listing' : 'Đã list / giới hạn',
          value: limit > 0 ? kpiRatioValue(published, limit) : fmt(published),
          suffix: limit > 0 ? kpiPercentSuffix(fillPercent, listingTone) : undefined,
          icon: <ShoppingOutlined />,
          tone: listingTone,
          colProps: optimizeColProps,
        },
        {
          key: 'needList',
          label: 'Cần list',
          value: fmt(needList),
          sub: kpiWasteShopSub(needList, avgListingsPerShop),
          suffix: limit > 0 ? kpiPercentSuffix(emptyPercent, needListTone) : undefined,
          icon: <PlusCircleOutlined />,
          tone: needListTone,
          colProps: optimizeColProps,
        },
        {
          key: 'delist',
          label: compact ? 'Cần hủy' : 'Cần hủy',
          value: fmt(delist),
          sub: kpiWasteShopSub(delist, avgListingsPerShop),
          suffix: published > 0 ? kpiPercentSuffix(delistPercent, delistTone) : undefined,
          icon: <ScissorOutlined />,
          tone: delistTone,
          colProps: optimizeColProps,
        },
        {
          key: 'review',
          label: compact ? 'Xem xét' : 'Cần xem xét',
          value: fmt(review),
          suffix: published > 0 ? kpiPercentSuffix(reviewPercent, reviewTone) : undefined,
          icon: <EyeOutlined />,
          tone: reviewTone,
          colProps: optimizeColProps,
        },
      ];
    }

    return [
      {
        key: 'shops',
        label: compact ? 'Shop xong' : 'Shop xong tương tác',
        value: kpiRatioValue(summary.completedShopCount, summary.shopCount),
        icon: <CheckCircleOutlined />,
        tone: 'green',
        colProps,
      },
      {
        key: 'tasks',
        label: compact ? 'Nhiệm vụ' : 'Nhiệm vụ tương tác',
        value: kpiRatioValue(summary.completedTaskCount, summary.totalTaskCount),
        icon: <UnorderedListOutlined />,
        tone: 'blue',
        colProps,
      },
      {
        key: 'attention',
        label: compact ? 'Cần xử lý' : 'Cần xử lý ngay',
        value: summary.attentionTaskCount.toLocaleString('en-US'),
        icon: <WarningOutlined />,
        tone: summary.attentionTaskCount > 0 ? 'orange' : 'green',
        colProps,
      },
    ];
  }, [mode, summary, isMobile]);

  const openHealthModal = (shop: ShopTaskShop, tab: HealthTabKey, event: MouseEvent) => {
    event.stopPropagation();
    event.preventDefault();
    setSelected(null);
    setHealthModal({ shop, tab });
  };

  const openDelistModal = (
    shop: ShopTaskShop,
    event: { stopPropagation: () => void; preventDefault: () => void },
  ) => {
    event.stopPropagation();
    event.preventDefault();
    setSelected(null);
    setDelistShop(shop);
  };

  const handleMarkHealthRecordHandled = async (recordId: string) => {
    if (!healthModal || !markHealthHandledMutation) return;
    setMarkingRecordId(recordId);
    try {
      await markHealthHandledMutation.mutateAsync({
        shopId: healthModal.shop.shopId,
        recordId,
      });
      const result = await boardQuery.refetch();
      const freshShop = result.data?.shops.find((s) => s.shopId === healthModal.shop.shopId);
      if (!freshShop?.healthSummary) {
        setHealthModal(null);
        return;
      }
      setHealthModal({ ...healthModal, shop: freshShop });
    } finally {
      setMarkingRecordId(null);
    }
  };

  const handleRowClick = (shop: ShopTaskShop, event: MouseEvent) => {
    const target = event.target as HTMLElement;
    if (isMobile) {
      if (
        target.closest(
          '.shop-health-indicators, button, a, .shop-task-chip.is-clickable, .shop-task-tasks-summary',
        )
      ) {
        return;
      }
      setSelected(shop);
      return;
    }
    if (
      target.closest(
        '.shop-task-shop-col, .shop-task-cell, .shop-health-indicators, button, a, .ant-tag, .shop-task-tasks-summary',
      )
    ) {
      return;
    }
    setSelected(shop);
  };

  const desktopColumns: ColumnsType<ShopTaskShop> = [
    {
      title: '#',
      key: 'index',
      width: 44,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Shop',
      key: 'shop',
      className: 'shop-task-shop-col',
      onCell: () => ({
        className: 'shop-task-shop-col',
        onClick: (event) => event.stopPropagation(),
      }),
      render: (_, row) => (
        <ShopIdentity
          row={row}
          showOwner={config.showOwnerFilter}
          onOpenHealth={(tab, event) => openHealthModal(row, tab, event)}
          onClick={(event) => event.stopPropagation()}
        />
      ),
    },
    {
      title: 'Sync',
      key: 'analyticsSync',
      width: 130,
      render: (_: unknown, row: ShopTaskShop) => (
        <AnalyticsSyncCell syncedAt={row.analyticsSyncedAt} />
      ),
    },
    {
      title: 'Online',
      key: 'todayOnline',
      width: 88,
      align: 'right' as const,
      render: (_: unknown, row: ShopTaskShop) => (
        <OnlineCell seconds={row.todayOnlineSeconds} />
      ),
    },
    ...(mode === 'optimize'
      ? ([
          {
            title: 'GMV',
            key: 'analyticsGmv',
            width: 100,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsGmv'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>
                {formatMoney(row.analyticsGmv, row.analyticsCurrency)}
              </AnalyticsMetricCell>
            ),
          },
          {
            title: 'Impressions',
            key: 'analyticsImpressions',
            width: 100,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsImpressions'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>{formatInt(row.analyticsImpressions)}</AnalyticsMetricCell>
            ),
          },
          {
            title: 'CTR',
            key: 'analyticsCtr',
            width: 80,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsCtr'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>{formatRate(row.analyticsCtr)}</AnalyticsMetricCell>
            ),
          },
          {
            title: 'Clicks',
            key: 'analyticsClicks',
            width: 90,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsClicks'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>{formatInt(row.analyticsClicks)}</AnalyticsMetricCell>
            ),
          },
          {
            title: (
              <Tooltip title="Add-to-cart">
                <span>ATC</span>
              </Tooltip>
            ),
            key: 'analyticsAtc',
            width: 72,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsAtc'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>{formatInt(row.analyticsAddToCartUnits)}</AnalyticsMetricCell>
            ),
          },
          {
            title: 'Orders',
            key: 'analyticsOrders',
            width: 80,
            align: 'right' as const,
            sorter: true,
            sortOrder: optimizeSortOrderFor('analyticsOrders'),
            render: (_: unknown, row: ShopTaskShop) => (
              <AnalyticsMetricCell>{formatInt(row.analyticsOrders)}</AnalyticsMetricCell>
            ),
          },
        ] satisfies ColumnsType<ShopTaskShop>)
      : ([
          {
            title: 'Tiến độ',
            key: 'progress',
            width: 130,
            render: (_: unknown, row: ShopTaskShop) => {
              const percent = taskProgressPercent(row.completedCount, row.totalCount);
              return <ShopTaskMetricProgress percent={percent} format={(value) => `${value}%`} />;
            },
          },
        ] satisfies ColumnsType<ShopTaskShop>)),
    {
      title: 'Listing',
      key: 'slots',
      width: 160,
      render: (_, row) => {
        if (row.publishLimitTotal == null) {
          return (
            <Text type="secondary" style={{ fontSize: 11 }}>
              —
            </Text>
          );
        }

        const published = row.publishLimitPublished ?? 0;
        const total = row.publishLimitTotal;
        const percent = slotProgressPercent(published, total);

        return (
          <ShopTaskMetricProgress
            percent={percent}
            format={(value) => `${value}% (${published}/${total})`}
          />
        );
      },
    },
    ...(mode === 'optimize'
      ? []
      : ([
          {
            title: 'Discount',
            key: 'hasProductDiscount',
            width: 130,
            align: 'center' as const,
            render: (_: unknown, row: ShopTaskShop) => (
              <PromotionRunningCell
                active={row.hasProductDiscount}
                beginTimeUtc={row.productDiscountBeginTimeUtc}
                endTimeUtc={row.productDiscountEndTimeUtc}
              />
            ),
          },
          {
            title: 'Flash',
            key: 'hasFlashDeal',
            width: 110,
            align: 'center' as const,
            render: (_: unknown, row: ShopTaskShop) => (
              <PromotionRunningCell
                active={row.hasFlashDeal}
                beginTimeUtc={row.flashDealBeginTimeUtc}
                endTimeUtc={row.flashDealEndTimeUtc}
              />
            ),
          },
          {
            title: 'Order',
            key: 'workflowOrders',
            width: 64,
            align: 'center' as const,
            render: (_: unknown, row: ShopTaskShop) => {
              const delivered = row.deliveredOrderCount ?? 0;
              const refund = row.refundOrderCount ?? 0;
              const total = delivered + refund;

              return (
                <Tooltip
                  title={
                    <div className="shop-task-order-tooltip">
                      <div>Đã giao: {delivered}</div>
                      <div>Refund: {refund}</div>
                    </div>
                  }
                >
                  <span className="shop-task-order-total">{total}</span>
                </Tooltip>
              );
            },
          },
        ] satisfies ColumnsType<ShopTaskShop>)),
    {
      title: 'Nhiệm vụ',
      key: 'tasks',
      // Optimize hiện chip «Cần hủy N · Cần xem xét M» — cần rộng hơn `x/y`.
      width: mode === 'optimize' ? 200 : 88,
      align: 'center' as const,
      render: (_, row) => (
        <TaskChipsSummary
          tasks={boardTasksForMode(row.tasks, mode)}
          delistCount={row.delistCount}
          reviewCount={row.reviewCount}
          onOpenDelist={(event) => openDelistModal(row, event)}
          interaction="hover"
        />
      ),
    },
  ];

  const mobileColumns: ColumnsType<ShopTaskShop> = [
    {
      title: 'Shop',
      key: 'mobile',
      render: (_, row) => (
        <ShopTaskMobileCard
          row={row}
          mode={mode}
          showOwner={config.showOwnerFilter}
          onOpenHealth={(tab, event) => openHealthModal(row, tab, event)}
          onOpenDelist={openDelistModal}
        />
      ),
    },
  ];

  const columns = isMobile ? mobileColumns : desktopColumns;

  const handleTableChange: TableProps<ShopTaskShop>['onChange'] = (nextPagination, _f, sorter, extra) => {
    if (extra.action === 'paginate') {
      setPagination({
        current: nextPagination.current ?? 1,
        pageSize: nextPagination.pageSize ?? DEFAULT_PAGE_SIZE,
      });
      return;
    }
    if (extra.action === 'sort' && mode === 'optimize') {
      const active = Array.isArray(sorter) ? sorter[0] : sorter;
      const key = typeof active?.columnKey === 'string' ? active.columnKey : '';
      if (key && active?.order && OPTIMIZE_SORT_KEYS.includes(key as OptimizeSortKey)) {
        setOptimizeSort({ key: key as OptimizeSortKey, order: active.order });
      } else {
        setOptimizeSort(DEFAULT_OPTIMIZE_SORT);
      }
      setPagination((prev) => ({ ...prev, current: 1 }));
    }
  };

  return (
    <>
      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          size="small"
          allowClear
          placeholder="Tìm shop / TikTok seller id"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          onSearch={(value) => patchParams({ q: value.trim() || undefined })}
        />
        <div className="admin-filters-grid">
          {config.showOwnerFilter ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 150 }}
              value={ownerId}
              loading={ownersQuery?.isLoading}
              options={(ownersQuery?.data ?? []).map((owner) => ({
                value: owner.id,
                label: ownerLabel(owner.fullName, owner.userName),
              }))}
              onChange={(value) => patchParams({ ownerId: value })}
            />
          ) : null}
          <DatePicker
            size="small"
            allowClear={false}
            style={{ width: isMobile ? '100%' : 140 }}
            value={dayjs(date, DATE_FORMAT)}
            format={DATE_FORMAT}
            disabledDate={(value) => value.isAfter(dayjs(), 'day')}
            onChange={(value) =>
              patchParams({ date: value ? value.format(DATE_FORMAT) : undefined })
            }
          />
        </div>
      </div>

      {(board?.warnings ?? []).map((warning) => (
        <Alert key={warning} type="warning" showIcon message={warning} className="shop-task-alert" />
      ))}

      {kpiItems.length > 0 ? (
        <div className="shop-task-kpi-strip">
          <DashboardKpiStrip items={kpiItems} />
        </div>
      ) : null}

      <PageContentSpin spinning={boardQuery.isLoading || boardQuery.isFetching}>
        <Table
          className={isMobile ? 'shop-task-mobile-table' : undefined}
          size="small"
          rowKey="shopId"
          columns={columns}
          dataSource={visibleShops}
          showHeader={!isMobile}
          showSorterTooltip={false}
          scroll={isMobile ? undefined : { x: mode === 'optimize' ? 1300 : 1100 }}
          onRow={(row) => ({ onClick: (event) => handleRowClick(row, event) })}
          rowClassName="shop-task-table-row"
          tableLayout={isMobile ? 'fixed' : undefined}
          styles={
            isMobile ? { body: { cell: { paddingBlock: 0, paddingInline: 0 } } } : undefined
          }
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Chưa có shop Active nào khớp bộ lọc."
              />
            ),
          }}
          pagination={tablePagination(visibleShops.length, {
            ...pagination,
            size: 'small',
            simple: isMobile,
            showSizeChanger: !isMobile,
            pageSizeOptions: ['20', '50', '100'],
          })}
          onChange={handleTableChange}
        />
      </PageContentSpin>

      <ShopTaskDrawer shop={selected} config={config} onClose={() => setSelected(null)} />
      <ShopHealthRecordsModal
        open={healthModal != null}
        shopName={
          healthModal
            ? shopTitleLabel(healthModal.shop, config.showOwnerFilter)
            : undefined
        }
        summary={healthModal?.shop.healthSummary ?? null}
        initialTab={healthModal?.tab ?? 'violation'}
        markingRecordId={markingRecordId}
        onMarkHandled={
          markHealthHandledMutation ? handleMarkHealthRecordHandled : undefined
        }
        onClose={() => setHealthModal(null)}
      />
      <ShopDelistListingsModal
        open={delistShop != null}
        shopId={delistShop?.shopId ?? null}
        shopName={
          delistShop ? shopTitleLabel(delistShop, config.showOwnerFilter) : undefined
        }
        canManage={canManageListings}
        onClose={() => setDelistShop(null)}
        onChanged={() => void boardQuery.refetch()}
      />
    </>
  );
};

/** Hook nhẹ cho PageHeader: date + reload từ cùng query board. */
export const useShopTaskBoardQueryMeta = (config: ShopTasksPortalConfig) => {
  const deps = useShopTasksDeps();
  const [searchParams] = useSearchParams();
  const date = searchParams.get('date') || dayjs().format(DATE_FORMAT);
  const ownerId = config.showOwnerFilter ? searchParams.get('ownerId') || undefined : undefined;
  const search = searchParams.get('q')?.trim() || undefined;
  const boardQuery = deps.useTaskBoard({ date, ownerId, search });
  return {
    date: boardQuery.data?.date ?? date,
    isFetching: boardQuery.isFetching,
    isLoading: boardQuery.isLoading,
    refetch: () => void boardQuery.refetch(),
  };
};
