import { useMemo, useState, type ReactNode } from 'react';
import {
  Alert,
  Card,
  Col,
  Empty,
  Input,
  Row,
  Segmented,
  Select,
  Table,
  Tooltip,
  Typography,
} from 'antd';
import {
  CalendarOutlined,
  DollarOutlined,
  FundOutlined,
  QuestionCircleOutlined,
  ShoppingCartOutlined,
  TeamOutlined,
  RiseOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { Column, Line } from '@ant-design/plots';
import dayjs, { type Dayjs } from 'dayjs';
import {
  chartTheme,
  money,
  moneyCompact,
  pieSliceLabelStyle,
  shortMoney,
} from '@drop/dashboard';
import { PageHeader, PageContentSpin, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import { tablePagination } from '@drop/utils/pagination';
import { toVnCalendarDate } from '@drop/utils/datetime';
import type { DashboardPeriod, SellerPerformanceRow } from '@drop/types/dashboard';
import '@drop/dashboard/dashboard.css';
import { useSellerPerformanceStatsDeps } from './SellerPerformanceStatsDepsContext';
import type { SellerPerformanceStatsPortalConfig } from './portalConfig';
import { statsKpiTone, statsNegativeColor } from './statsKpi';
import {
  PERIOD_OPTIONS,
  RANGE_PRESETS,
  chartEmptyStyle,
  chartHeight,
  clampDateRange,
  disabledFutureDate,
  matchPeriod,
  rangeForPeriod,
  renderRankMedal,
} from './statsPeriod';

const { Text } = Typography;

const MOBILE_CHART_HEIGHT = 200;
const CHART_TOP_N = 12;
const ALL_SELLERS_VALUE = '__all__';

type VolumeMetric = 'orders' | 'ordersPerCalendarDay' | 'profit' | 'revenue' | 'avgProfit' | 'avgRevenue';
type IntensityMetric = 'ordersPerOperationalDay' | 'ordersPerShopDay';
type GrowthMetric = 'ordersPerDay' | 'ordersPerShop';
type GrowthView = 'day' | 'week' | 'month' | 'year';

const VOLUME_METRIC_OPTIONS: { label: string; value: VolumeMetric }[] = [
  { label: 'Profit/đơn', value: 'avgProfit' },
  { label: 'Rev/đơn', value: 'avgRevenue' },
  { label: 'Đơn/ngày lịch', value: 'ordersPerCalendarDay' },
  { label: 'Đơn', value: 'orders' },
  { label: 'Profit', value: 'profit' },
  { label: 'Revenue', value: 'revenue' },
];

const INTENSITY_METRIC_OPTIONS: { label: string; value: IntensityMetric }[] = [
  { label: 'Đơn/Ngày', value: 'ordersPerOperationalDay' },
  { label: 'Đơn/Shop', value: 'ordersPerShopDay' },
];

const GROWTH_VIEW_OPTIONS: { label: string; value: GrowthView }[] = [
  { label: 'Ngày', value: 'day' },
  { label: 'Tuần', value: 'week' },
  { label: 'Tháng', value: 'month' },
  { label: 'Năm', value: 'year' },
];

const growthViewBucket = (date: string, view: GrowthView) => {
  const value = dayjs(date);
  if (view === 'year') return value.format('YYYY');
  if (view === 'month') return value.format('YYYY-MM');
  if (view === 'week') {
    const daysFromMonday = value.day() === 0 ? 6 : value.day() - 1;
    return value.subtract(daysFromMonday, 'day').format('YYYY-MM-DD');
  }
  return date;
};

const volumeMetricValue = (row: SellerPerformanceRow, metric: VolumeMetric): number => {
  switch (metric) {
    case 'avgProfit':
      return Number(row.averageProfitPerOrder) || 0;
    case 'avgRevenue':
      return Number(row.averageRevenuePerOrder) || 0;
    case 'orders':
      return row.orders;
    case 'ordersPerCalendarDay':
      return Number(row.averageOrdersPerCalendarDay ?? row.averageOrdersPerDay) || 0;
    case 'profit':
      return Number(row.profit) || 0;
    case 'revenue':
      return Number(row.revenue) || 0;
  }
};

const intensityMetricValue = (
  row: SellerPerformanceRow,
  metric: IntensityMetric,
): number | null => {
  const raw =
    metric === 'ordersPerOperationalDay'
      ? row.averageOrdersPerOperationalDay
      : (row.averageOrdersPerShopDay ?? row.averageOrdersPerActiveShopPerDay);
  if (raw == null) return null;
  return Number(raw);
};

const formatRate = (value: number | null | undefined) => {
  if (value == null) return '—';
  return Number(value).toFixed(2);
};

const formatAvg = (value: number | null | undefined, compact: boolean) => {
  if (value == null) return '—';
  return compact ? moneyCompact(Number(value)) : money(Number(value));
};

const formatMargin = (value: number | null | undefined) => {
  if (value == null) return '—';
  const margin = Number(value);
  return (
    <Text type={margin < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
      {`${margin.toFixed(1)}%`}
    </Text>
  );
};

const HintTitle = ({ label, hint }: { label: string; hint: string }) => (
  <Tooltip title={hint}>
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 3,
        whiteSpace: 'nowrap',
      }}
    >
      {label}
      <QuestionCircleOutlined style={{ fontSize: 11, flex: 'none' }} />
    </span>
  </Tooltip>
);

const compactDecimal = (value: number) => {
  if (value === 0) return '';
  return value.toLocaleString('en-US', {
    minimumFractionDigits: 0,
    maximumFractionDigits: Math.abs(value) < 1 ? 2 : 1,
  });
};

export type SharedSellerPerformanceStatsPageProps = {
  config: SellerPerformanceStatsPortalConfig;
};

export const SellerPerformanceStatsPage = ({
  config,
}: SharedSellerPerformanceStatsPageProps) => {
  const { useSellerPerformance, useThemeMode } = useSellerPerformanceStatsDeps();
  const isMobile = useIsMobile();
  /** Re-evaluate chart labels / KPI chips when theme toggles. */
  useThemeMode();
  const [range, setRange] = useState<[Dayjs, Dayjs]>(() => rangeForPeriod('month'));
  const [period, setPeriod] = useState<DashboardPeriod | 'custom'>('month');
  const [volumeMetric, setVolumeMetric] = useState<VolumeMetric>('avgProfit');
  const [intensityMetric, setIntensityMetric] =
    useState<IntensityMetric>('ordersPerShopDay');
  const [growthMetric, setGrowthMetric] = useState<GrowthMetric>('ordersPerDay');
  const [growthView, setGrowthView] = useState<GrowthView>('day');
  const [selectedSellerId, setSelectedSellerId] = useState<string>(ALL_SELLERS_VALUE);
  const [search, setSearch] = useState('');
  const kpiPurple = statsKpiTone('purple');
  const kpiBlue = statsKpiTone('blue');
  const kpiGreen = statsKpiTone('green');
  const kpiOrange = statsKpiTone('orange');
  const kpiRed = statsKpiTone('red');
  const negativeColor = statsNegativeColor();
  const chartLabelStyle = pieSliceLabelStyle(isMobile ? 'sm' : 'md');

  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);

  const query = useSellerPerformance({ fromDate, toDate });
  const data = query.data;
  const sellers = data?.sellers ?? [];
  const totals = data?.totals;
  const effectiveSellerId = (() => {
    if (!config.showSellerFilter) return ALL_SELLERS_VALUE;
    if (
      selectedSellerId === ALL_SELLERS_VALUE ||
      sellers.some((s) => s.userId === selectedSellerId)
    ) {
      return selectedSellerId;
    }
    return ALL_SELLERS_VALUE;
  })();
  const isAllSellers = effectiveSellerId === ALL_SELLERS_VALUE;
  const growthQuery = useSellerPerformance(
    {
      fromDate,
      toDate,
      ownerUserId: isAllSellers ? undefined : effectiveSellerId,
    },
    { enabled: config.showSellerFilter && !isAllSellers },
  );
  const growthDays = isAllSellers ? (data?.days ?? []) : (growthQuery.data?.days ?? []);
  const selectedSeller = isAllSellers
    ? undefined
    : sellers.find((s) => s.userId === effectiveSellerId);

  const formatMoney = isMobile ? moneyCompact : money;
  const activeChartHeight = isMobile ? MOBILE_CHART_HEIGHT : chartHeight;
  const activeChartEmptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  const filteredSellers = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return sellers;
    return sellers.filter((s) => {
      const name = (s.displayName || '').toLowerCase();
      return name.includes(q) || s.userId.toLowerCase().includes(q);
    });
  }, [sellers, search]);

  const volumeChartData = useMemo(() => {
    const ranked = [...sellers]
      .filter((s) => s.orders > 0 || volumeMetricValue(s, volumeMetric) !== 0)
      .sort(
        (a, b) => volumeMetricValue(b, volumeMetric) - volumeMetricValue(a, volumeMetric),
      )
      .slice(0, CHART_TOP_N);

    return ranked.map((s) => {
      const value = volumeMetricValue(s, volumeMetric);
      return {
        seller: (s.displayName || s.userId).trim() || s.userId,
        value,
        valueLabel:
          value === 0
            ? ''
            : volumeMetric === 'orders'
              ? value.toLocaleString('en-US')
              : volumeMetric === 'ordersPerCalendarDay'
                ? compactDecimal(value)
                : shortMoney(value),
        orders: s.orders,
      };
    });
  }, [sellers, volumeMetric]);

  const intensityChartData = useMemo(() => {
    const ranked = [...sellers]
      .map((s) => ({ s, value: intensityMetricValue(s, intensityMetric) }))
      .filter((x) => x.value != null && (x.s.orders > 0 || (x.s.shopDays ?? 0) > 0))
      .sort((a, b) => (b.value ?? 0) - (a.value ?? 0))
      .slice(0, CHART_TOP_N);

    return ranked.map(({ s, value }) => ({
      seller: (s.displayName || s.userId).trim() || s.userId,
      value: value ?? 0,
      valueLabel: value == null ? '' : compactDecimal(value),
      orders: s.orders,
      eligibleOrders: s.eligibleShopOrderCount ?? 0,
      operationalDays: s.sellerOperationalDays ?? 0,
      shops: s.operationalShopCount ?? s.participatingShopCount ?? 0,
      shopDays: s.shopDays ?? 0,
    }));
  }, [sellers, intensityMetric]);

  const growthChartData = useMemo(() => {
    const buckets = new Map<
      string,
      {
        date: string;
        calendarDays: number;
        orders: number;
        eligibleOrders: number;
        shopDays: number;
      }
    >();

    growthDays.forEach((day) => {
      const key = growthViewBucket(day.date, growthView);
      const bucket = buckets.get(key) ?? {
        date: key,
        calendarDays: 0,
        orders: 0,
        eligibleOrders: 0,
        shopDays: 0,
      };
      bucket.calendarDays += 1;
      bucket.orders += day.orders;
      bucket.eligibleOrders += day.eligibleShopOrders;
      bucket.shopDays += day.operationalShopCount;
      buckets.set(key, bucket);
    });

    return [...buckets.values()]
      .sort((a, b) => a.date.localeCompare(b.date))
      .map((bucket) => ({
        ...bucket,
        value:
          growthMetric === 'ordersPerDay'
            ? bucket.calendarDays > 0
              ? Number((bucket.orders / bucket.calendarDays).toFixed(2))
              : null
            : bucket.shopDays > 0
              ? Number((bucket.eligibleOrders / bucket.shopDays).toFixed(2))
              : null,
      }));
  }, [growthDays, growthMetric, growthView]);

  const orderCount = totals?.orderCount ?? 0;
  const revenue = Number(totals?.revenue ?? 0);
  const profit = Number(totals?.profit ?? 0);
  const avgProfit = totals?.averageProfitPerOrder;
  const activeSellerCount = totals?.activeSellerCount ?? 0;
  const sellersWithOrders = totals?.sellersWithOrders ?? 0;
  const unassignedRate = totals?.unassignedOrderRate;
  const avgOperationalDay =
    totals?.averageOrdersPerOperationalDay ?? null;
  const avgShopDay =
    totals?.averageOrdersPerShopDay ?? totals?.averageOrdersPerActiveShopPerDay ?? null;

  const volumeMetricLabel =
    VOLUME_METRIC_OPTIONS.find((o) => o.value === volumeMetric)?.label ?? volumeMetric;
  const intensityMetricLabel =
    INTENSITY_METRIC_OPTIONS.find((o) => o.value === intensityMetric)?.label ??
    intensityMetric;
  const sellerOptions = [
    { value: ALL_SELLERS_VALUE, label: 'Tất cả seller' },
    ...sellers.map((seller) => ({
      value: seller.userId,
      label: seller.displayName || seller.userId,
    })),
  ];

  const growthSellerSelect = config.showSellerFilter ? (
    <Select
      className={isMobile ? 'seller-performance-header-select' : undefined}
      size="small"
      showSearch
      value={effectiveSellerId}
      placeholder="Chọn seller"
      optionFilterProp="label"
      options={sellerOptions}
      onChange={setSelectedSellerId}
      style={{ width: isMobile ? 132 : 190 }}
    />
  ) : null;

  const growthMetricControls = (
    <>
      <Segmented
        size="small"
        block={isMobile}
        value={growthView}
        options={GROWTH_VIEW_OPTIONS}
        onChange={(value) => setGrowthView(value as GrowthView)}
      />
      <Segmented
        size="small"
        block={isMobile}
        value={growthMetric}
        options={[
          {
            label: 'Đơn/Ngày',
            value: 'ordersPerDay',
          },
          {
            label: 'Đơn/Shop',
            value: 'ordersPerShop',
          },
        ]}
        onChange={(value) => setGrowthMetric(value as GrowthMetric)}
      />
    </>
  );

  const growthControls = (
    <div className={`seller-performance-growth-controls${isMobile ? ' is-mobile' : ''}`}>
      {growthSellerSelect}
      {growthMetricControls}
    </div>
  );

  const columns = isMobile
    ? [
        {
          title: '#',
          key: 'rank',
          width: 28,
          render: (_: unknown, __: SellerPerformanceRow, index: number) => (
            <span className={renderRankMedal(index)}>{index + 1}</span>
          ),
        },
        {
          title: 'Seller',
          dataIndex: 'displayName',
          key: 'displayName',
          width: 74,
          ellipsis: true,
        },
        {
          title: 'Đơn',
          dataIndex: 'orders',
          key: 'orders',
          width: 36,
          align: 'right' as const,
          defaultSortOrder: 'descend' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            a.orders - b.orders || Number(a.revenue) - Number(b.revenue),
        },
        {
          title: (
            <HintTitle
              label="Đ/ng"
              hint="Đơn ÷ ngày hoạt động (union ngày có ≥1 shop vận hành)."
            />
          ),
          key: 'averageOrdersPerOperationalDay',
          width: 56,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerOperationalDay) || 0) -
            (Number(b.averageOrdersPerOperationalDay) || 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(row.averageOrdersPerOperationalDay),
        },
        {
          title: (
            <HintTitle
              label="Đ/S-ng"
              hint="Đơn có ShopId ÷ shop-day vận hành trong kỳ."
            />
          ),
          key: 'averageOrdersPerShopDay',
          width: 64,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerShopDay ?? a.averageOrdersPerActiveShopPerDay) || 0) -
            (Number(b.averageOrdersPerShopDay ?? b.averageOrdersPerActiveShopPerDay) || 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(row.averageOrdersPerShopDay ?? row.averageOrdersPerActiveShopPerDay),
        },
        {
          title: 'Profit',
          dataIndex: 'profit',
          key: 'profit',
          width: 55,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            Number(a.profit) - Number(b.profit),
          render: (v: number) => {
            const rowProfit = Number(v) || 0;
            return (
              <Text type={rowProfit < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
                {moneyCompact(rowProfit)}
              </Text>
            );
          },
        },
      ]
    : [
        {
          title: '#',
          key: 'rank',
          width: 48,
          render: (_: unknown, __: SellerPerformanceRow, index: number) => (
            <span className={renderRankMedal(index)}>{index + 1}</span>
          ),
        },
        {
          title: 'Seller',
          dataIndex: 'displayName',
          key: 'displayName',
          width: 150,
          ellipsis: true,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (a.displayName || '').localeCompare(b.displayName || '', undefined, {
              sensitivity: 'base',
            }),
        },
        {
          title: 'Orders',
          dataIndex: 'orders',
          key: 'orders',
          width: 72,
          align: 'right' as const,
          defaultSortOrder: 'descend' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            a.orders - b.orders || Number(a.revenue) - Number(b.revenue),
        },
        {
          title: (
            <HintTitle
              label="Ngày"
              hint="Số ngày VN unique có ≥1 shop đang vận hành trong kỳ."
            />
          ),
          dataIndex: 'sellerOperationalDays',
          key: 'sellerOperationalDays',
          width: 72,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (a.sellerOperationalDays ?? 0) - (b.sellerOperationalDays ?? 0),
        },
        {
          title: (
            <HintTitle
              label="Đ/ngày"
              hint="Orders ÷ ngày hoạt động. Chỉ số seller/ngày chính để so sánh công bằng."
            />
          ),
          key: 'averageOrdersPerOperationalDay',
          width: 88,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerOperationalDay) || 0) -
            (Number(b.averageOrdersPerOperationalDay) || 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(row.averageOrdersPerOperationalDay),
        },
        {
          title: (
            <HintTitle
              label="Shop"
              hint="Số shop có ≥1 ngày vận hành giao với kỳ (không phải snapshot Active)."
            />
          ),
          dataIndex: 'operationalShopCount',
          key: 'operationalShopCount',
          width: 72,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (a.operationalShopCount ?? a.participatingShopCount ?? 0) -
            (b.operationalShopCount ?? b.participatingShopCount ?? 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            row.operationalShopCount ?? row.participatingShopCount ?? 0,
        },
        {
          title: (
            <HintTitle
              label="S-ngày"
              hint="Σ ngày mỗi shop vận hành trong kỳ (Active→Inactive/Suspended; lịch sử backfill từ đơn đầu)."
            />
          ),
          dataIndex: 'shopDays',
          key: 'shopDays',
          width: 78,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (a.shopDays ?? 0) - (b.shopDays ?? 0),
        },
        {
          title: (
            <HintTitle
              label="Đ/S-ngày"
              hint="Đơn có ShopId ÷ shop-day. Chỉ số năng suất shop chính."
            />
          ),
          key: 'averageOrdersPerShopDay',
          width: 94,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerShopDay ?? a.averageOrdersPerActiveShopPerDay) || 0) -
            (Number(b.averageOrdersPerShopDay ?? b.averageOrdersPerActiveShopPerDay) || 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(row.averageOrdersPerShopDay ?? row.averageOrdersPerActiveShopPerDay),
        },
        {
          title: (
            <HintTitle
              label="Đ/ng lịch"
              hint="Tổng đơn ÷ số ngày lịch đã chọn (quy mô tuyệt đối, không prorate shop)."
            />
          ),
          key: 'averageOrdersPerCalendarDay',
          width: 92,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerCalendarDay ?? a.averageOrdersPerDay) || 0) -
            (Number(b.averageOrdersPerCalendarDay ?? b.averageOrdersPerDay) || 0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(row.averageOrdersPerCalendarDay ?? row.averageOrdersPerDay),
        },
        {
          title: (
            <HintTitle
              label="Đ/shop"
              hint="Đơn có ShopId ÷ số shop vận hành trong kỳ — tổng theo kỳ, phụ thuộc độ dài kỳ."
            />
          ),
          key: 'averageOrdersPerOperationalShop',
          width: 84,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageOrdersPerOperationalShop ?? a.averageOrdersPerActiveShop) ||
              0) -
            (Number(b.averageOrdersPerOperationalShop ?? b.averageOrdersPerActiveShop) ||
              0),
          render: (_: unknown, row: SellerPerformanceRow) =>
            formatRate(
              row.averageOrdersPerOperationalShop ?? row.averageOrdersPerActiveShop,
            ),
        },
        {
          title: 'Revenue',
          dataIndex: 'revenue',
          key: 'revenue',
          width: 100,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            Number(a.revenue) - Number(b.revenue),
          render: (v: number) => money(Number(v)),
        },
        {
          title: 'Profit',
          dataIndex: 'profit',
          key: 'profit',
          width: 100,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            Number(a.profit) - Number(b.profit),
          render: (v: number) => {
            const rowProfit = Number(v) || 0;
            return (
              <Text type={rowProfit < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
                {money(rowProfit)}
              </Text>
            );
          },
        },
        {
          title: 'P/đơn',
          dataIndex: 'averageProfitPerOrder',
          key: 'averageProfitPerOrder',
          width: 90,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.averageProfitPerOrder) || 0) - (Number(b.averageProfitPerOrder) || 0),
          render: (v: number | null | undefined) => formatAvg(v, false),
        },
        {
          title: 'Margin',
          dataIndex: 'profitMargin',
          key: 'profitMargin',
          width: 72,
          align: 'right' as const,
          sorter: (a: SellerPerformanceRow, b: SellerPerformanceRow) =>
            (Number(a.profitMargin) || 0) - (Number(b.profitMargin) || 0),
          render: (v: number | null | undefined) => formatMargin(v),
        },
      ];

  const renderChartBody = (
    loading: boolean,
    empty: boolean,
    error: unknown,
    chart: ReactNode,
  ) => (
    <PageContentSpin spinning={loading}>
      {error ? (
        <Alert
          type="error"
          showIcon
          title={error instanceof Error ? error.message : 'Lỗi tải biểu đồ'}
        />
      ) : empty ? (
        <div className="dashboard-chart-empty" style={activeChartEmptyStyle}>
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
        </div>
      ) : (
        chart
      )}
    </PageContentSpin>
  );

  const onPeriodChange = (value: string | number) => {
    const next = value as DashboardPeriod;
    setPeriod(next);
    setRange(rangeForPeriod(next));
  };

  const periodControls = (
    <AdminDateRangeFilter
      variant="toolbar"
      value={range}
      allowClear={false}
      disabledDate={disabledFutureDate}
      presets={RANGE_PRESETS}
      presetOptions={PERIOD_OPTIONS}
      presetValue={period === 'custom' ? '' : period}
      onChange={(values) => {
        if (!values) return;
        const next = clampDateRange(values[0], values[1]);
        setRange(next);
        setPeriod(matchPeriod(next));
      }}
      onPresetChange={(value) => onPeriodChange(value)}
    />
  );

  return (
    <div className="dashboard-page seller-performance-page">
      <PageHeader
        title={config.pageTitle}
        extra={isMobile ? undefined : <div className="dashboard-toolbar is-inline">{periodControls}</div>}
      />

      {isMobile ? <div className="dashboard-toolbar">{periodControls}</div> : null}

      {query.isError ? (
        <Alert
          type="error"
          showIcon
          title={
            query.error instanceof Error
              ? query.error.message
              : 'Lỗi tải thống kê hiệu suất seller'
          }
        />
      ) : null}

      <PageContentSpin spinning={query.isLoading || query.isFetching}>
        <div className="seller-performance-kpi-grid">
          <Row gutter={[8, 8]}>
            {config.showSellerCountKpi ? (
              <Col xs={12} sm={12} md={6}>
                <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                  <div className="dashboard-kpi-head">
                    <span className="dashboard-kpi-label">Seller</span>
                    <span
                      className="dashboard-kpi-icon"
                      style={{ background: kpiPurple.iconBg, color: kpiPurple.iconColor }}
                    >
                      <TeamOutlined />
                    </span>
                  </div>
                  <p className="dashboard-kpi-value">{activeSellerCount.toLocaleString('en-US')}</p>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    {sellersWithOrders} có đơn
                  </Text>
                </Card>
              </Col>
            ) : null}
            <Col xs={12} sm={config.showSellerCountKpi ? 12 : 8} md={config.showSellerCountKpi ? 6 : 8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Orders</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{ background: kpiBlue.iconBg, color: kpiBlue.iconColor }}
                  >
                    <ShoppingCartOutlined />
                  </span>
                </div>
                <p className="dashboard-kpi-value">{orderCount.toLocaleString('en-US')}</p>
                {unassignedRate != null && Number(unassignedRate) > 0 ? (
                  <Text type="warning" style={{ fontSize: 12 }}>
                    {Number(unassignedRate).toFixed(1)}% thiếu shop
                  </Text>
                ) : null}
              </Card>
            </Col>
            <Col xs={12} sm={config.showSellerCountKpi ? 12 : 8} md={config.showSellerCountKpi ? 6 : 8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Đơn/Ngày</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{ background: kpiPurple.iconBg, color: kpiPurple.iconColor }}
                  >
                    <ThunderboltOutlined />
                  </span>
                </div>
                <p className="dashboard-kpi-value">{formatRate(avgOperationalDay)}</p>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {totals?.sellerOperationalDays ?? 0} ngày HĐ
                </Text>
              </Card>
            </Col>
            <Col xs={12} sm={config.showSellerCountKpi ? 12 : 8} md={config.showSellerCountKpi ? 6 : 8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Đơn/Shop</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{ background: kpiBlue.iconBg, color: kpiBlue.iconColor }}
                  >
                    <CalendarOutlined />
                  </span>
                </div>
                <p className="dashboard-kpi-value">{formatRate(avgShopDay)}</p>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {totals?.shopDays ?? 0} shop-day
                </Text>
              </Card>
            </Col>
          </Row>

          <Row gutter={[8, 8]}>
            <Col xs={12} sm={8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Revenue</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{ background: kpiGreen.iconBg, color: kpiGreen.iconColor }}
                  >
                    <DollarOutlined />
                  </span>
                </div>
                <p className="dashboard-kpi-value">{formatMoney(revenue)}</p>
              </Card>
            </Col>
            <Col xs={12} sm={8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Profit</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{
                      background: (profit >= 0 ? kpiGreen : kpiRed).iconBg,
                      color: (profit >= 0 ? kpiGreen : kpiRed).iconColor,
                    }}
                  >
                    <FundOutlined />
                  </span>
                </div>
                <p
                  className="dashboard-kpi-value"
                  style={{ color: profit < 0 ? negativeColor : undefined }}
                >
                  {formatMoney(profit)}
                </p>
              </Card>
            </Col>
            <Col xs={24} sm={8}>
              <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
                <div className="dashboard-kpi-head">
                  <span className="dashboard-kpi-label">Profit/đơn</span>
                  <span
                    className="dashboard-kpi-icon"
                    style={{ background: kpiOrange.iconBg, color: kpiOrange.iconColor }}
                  >
                    <RiseOutlined />
                  </span>
                </div>
                <p
                  className="dashboard-kpi-value"
                  style={{
                    color:
                      avgProfit != null && Number(avgProfit) < 0 ? negativeColor : undefined,
                  }}
                >
                  {formatAvg(avgProfit, isMobile)}
                </p>
              </Card>
            </Col>
          </Row>
        </div>

        {config.showCrossSellerRanking ? (
        <div className="dashboard-section-block">
          <Row gutter={[8, 8]} align="stretch">
            <Col xs={24} lg={12}>
              <Card
                size="small"
                className="dashboard-section-card"
                style={{ height: '100%' }}
                title="Quy mô seller"
                extra={
                  isMobile ? (
                    <Select
                      className="seller-performance-header-select"
                      size="small"
                      value={volumeMetric}
                      options={VOLUME_METRIC_OPTIONS}
                      onChange={(value) => setVolumeMetric(value as VolumeMetric)}
                      aria-label="Chỉ số quy mô seller"
                    />
                  ) : (
                    <Segmented
                      size="small"
                      value={volumeMetric}
                      options={VOLUME_METRIC_OPTIONS}
                      onChange={(value) => setVolumeMetric(value as VolumeMetric)}
                    />
                  )
                }
              >
                {renderChartBody(
                  query.isLoading || query.isFetching,
                  volumeChartData.length === 0,
                  query.isError ? query.error : null,
                  <Column
                    height={activeChartHeight}
                    marginTop={30}
                    data={volumeChartData}
                    xField="seller"
                    yField="value"
                    style={{
                      maxWidth: isMobile ? 12 : 18,
                      fill:
                        volumeMetric === 'avgProfit' || volumeMetric === 'profit'
                          ? '#389e0d'
                          : volumeMetric === 'ordersPerCalendarDay'
                            ? '#531dab'
                            : '#0958d9',
                    }}
                    label={{
                      text: 'valueLabel',
                      position: 'top',
                      style: chartLabelStyle,
                    }}
                    axis={{
                      ...chartTheme.axis,
                      x: {
                        ...chartTheme.axis.x,
                        labelFontSize: isMobile ? 9 : 10,
                        labelAutoRotate: true,
                        labelAutoHide: isMobile,
                      },
                      y: {
                        ...chartTheme.axis.y,
                        labelFormatter: (v: number) =>
                          volumeMetric === 'orders' ||
                          volumeMetric === 'ordersPerCalendarDay'
                            ? Number(v).toFixed(
                                volumeMetric === 'ordersPerCalendarDay' ? 1 : 0,
                              )
                            : shortMoney(Number(v)),
                      },
                    }}
                    tooltip={{
                      items: [
                        {
                          field: 'value',
                          name: volumeMetricLabel,
                          valueFormatter: (v: number) =>
                            volumeMetric === 'orders'
                              ? String(v)
                              : volumeMetric === 'ordersPerCalendarDay'
                                ? Number(v).toFixed(2)
                                : money(Number(v)),
                        },
                        { field: 'orders', name: 'Orders' },
                      ],
                    }}
                  />,
                )}
              </Card>
            </Col>

            <Col xs={24} lg={12}>
              <Card
                size="small"
                className="dashboard-section-card"
                style={{ height: '100%' }}
                title="Hiệu suất vận hành"
                extra={
                  isMobile ? (
                    <Segmented
                      className="seller-performance-header-segmented"
                      size="small"
                      value={intensityMetric}
                      options={[
                        { label: 'Đơn/Ngày', value: 'ordersPerOperationalDay' },
                        { label: 'Đơn/Shop', value: 'ordersPerShopDay' },
                      ]}
                      onChange={(value) => setIntensityMetric(value as IntensityMetric)}
                    />
                  ) : (
                    <Segmented
                      size="small"
                      value={intensityMetric}
                      options={INTENSITY_METRIC_OPTIONS}
                      onChange={(value) => setIntensityMetric(value as IntensityMetric)}
                    />
                  )
                }
              >
                {renderChartBody(
                  query.isLoading || query.isFetching,
                  intensityChartData.length === 0,
                  query.isError ? query.error : null,
                  <Column
                    height={activeChartHeight}
                    marginTop={28}
                    data={intensityChartData}
                    xField="seller"
                    yField="value"
                    style={{
                      maxWidth: isMobile ? 12 : 18,
                      fill:
                        intensityMetric === 'ordersPerShopDay' ? '#1677ff' : '#722ed1',
                    }}
                    label={{
                      text: 'valueLabel',
                      position: 'top',
                      style: chartLabelStyle,
                    }}
                    axis={{
                      ...chartTheme.axis,
                      x: {
                        ...chartTheme.axis.x,
                        labelFontSize: isMobile ? 9 : 10,
                        labelAutoRotate: true,
                        labelAutoHide: isMobile,
                      },
                      y: {
                        ...chartTheme.axis.y,
                        labelFormatter: (v: number) => Number(v).toFixed(1),
                      },
                    }}
                    tooltip={{
                      items: [
                        {
                          field: 'value',
                          name: intensityMetricLabel,
                          valueFormatter: (v: number) => Number(v).toFixed(2),
                        },
                        { field: 'orders', name: 'Tổng đơn' },
                        { field: 'eligibleOrders', name: 'Đơn có shop' },
                        { field: 'operationalDays', name: 'Ngày HĐ' },
                        { field: 'shops', name: 'Shop vận hành' },
                        { field: 'shopDays', name: 'Shop-day' },
                      ],
                    }}
                  />,
                )}
              </Card>
            </Col>
          </Row>
        </div>
        ) : null}

        <div className="dashboard-section-block">
          <Card
            size="small"
            className="dashboard-section-card"
            title={
              isMobile || !config.showSellerFilter
                ? 'Tăng trưởng'
                : `Tăng trưởng — ${
                    isAllSellers ? 'Tất cả hệ thống' : selectedSeller?.displayName || 'Chọn seller'
                  }`
            }
            extra={isMobile ? growthSellerSelect : growthControls}
          >
            {isMobile ? (
              <div className="seller-performance-growth-controls is-mobile">
                {growthMetricControls}
              </div>
            ) : null}
            {renderChartBody(
              isAllSellers
                ? query.isLoading || query.isFetching
                : growthQuery.isLoading || growthQuery.isFetching,
              growthChartData.length === 0 ||
                growthChartData.every((point) => point.value == null),
              isAllSellers
                ? query.isError
                  ? query.error
                  : null
                : growthQuery.isError
                  ? growthQuery.error
                  : null,
              <Line
                height={activeChartHeight}
                data={growthChartData}
                xField="date"
                yField="value"
                style={{
                  lineWidth: 2,
                  stroke: growthMetric === 'ordersPerDay' ? '#722ed1' : '#1677ff',
                }}
                point={{
                  size: 3,
                  shape: 'circle',
                  style: {
                    fill: growthMetric === 'ordersPerDay' ? '#722ed1' : '#1677ff',
                  },
                }}
                axis={{
                  ...chartTheme.axis,
                  x: {
                    ...chartTheme.axis.x,
                    labelFontSize: isMobile ? 9 : 10,
                    labelAutoRotate: true,
                    labelAutoHide: true,
                  },
                  y: {
                    ...chartTheme.axis.y,
                    labelFormatter: (v: number) => Number(v).toFixed(1),
                  },
                }}
                tooltip={{
                  items: [
                    {
                      field: 'value',
                      name: growthMetric === 'ordersPerDay' ? 'Đơn/ngày' : 'Đơn/shop',
                      valueFormatter: (v: number) => Number(v).toFixed(2),
                    },
                    { field: 'orders', name: 'Tổng đơn' },
                    { field: 'eligibleOrders', name: 'Đơn có shop' },
                    { field: 'calendarDays', name: 'Số ngày' },
                    {
                      field: 'shopDays',
                      name: growthView === 'day' ? 'Shop vận hành' : 'Shop-day',
                    },
                  ],
                }}
              />,
            )}
          </Card>
        </div>

        <Alert
          type="info"
          showIcon={!isMobile}
          className="dashboard-status-alert"
          style={{ marginTop: 12 }}
          title={
            isMobile
              ? 'Đơn/ngày HĐ & đơn/shop-ngày dùng lịch sử vận hành shop (Active→Inactive).'
              : `Kỳ ${totals?.dayCount ?? 0} ngày lịch · ${totals?.sellerOperationalDays ?? 0} ngày HĐ (Σ) · ${totals?.shopDays ?? 0} shop-day. Đơn/ngày hoạt động = đơn ÷ ngày có ≥1 shop vận hành; đơn/shop-ngày = đơn có ShopId ÷ shop-day. Shop bắt đầu từ Active (hoặc ngày đơn đầu khi backfill), kết thúc khi Inactive/Suspended/đổi owner. Đơn/ngày lịch và đơn/shop (kỳ) chỉ là quy mô phụ. Đơn thiếu ShopId không vào chỉ số theo shop${
                  unassignedRate != null && Number(unassignedRate) > 0
                    ? ` (hiện ${Number(unassignedRate).toFixed(1)}%).`
                    : '.'
                }`
          }
        />

        {config.showCrossSellerRanking ? (
        <div className="dashboard-section-block">
          <Card
            size="small"
            className="dashboard-section-card"
            title="Bảng hiệu suất seller"
            extra={
              <Input.Search
                size="small"
                allowClear
                placeholder="Tìm seller…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                style={{ width: isMobile ? 140 : 200 }}
              />
            }
          >
            <Table
              size="small"
              pagination={tablePagination(filteredSellers.length, {
                pageSize: 50,
                size: 'small',
                showSizeChanger: false,
              })}
              rowKey="userId"
              dataSource={filteredSellers}
              columns={columns}
              scroll={isMobile ? undefined : { x: 1500 }}
              tableLayout={isMobile ? 'fixed' : 'auto'}
              className={isMobile ? 'dashboard-seller-table-mobile' : undefined}
              locale={{ emptyText: 'Chưa có dữ liệu' }}
            />
          </Card>
        </div>
        ) : null}
      </PageContentSpin>
    </div>
  );
};
