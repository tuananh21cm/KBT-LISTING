import { useMemo, useState, type ReactNode } from 'react';
import {
  Alert,
  Card,
  Col,
  Empty,
  Row,
  Segmented,
  Select,
  Space,
} from 'antd';
import {
  ShoppingCartOutlined,
  DollarOutlined,
  FundOutlined,
} from '@ant-design/icons';
import { Area, Column, Line, Pie } from '@ant-design/plots';
import { type Dayjs } from 'dayjs';
import { AdminDateRangeFilter } from '@drop/ui';
import { PageHeader } from '../../components/PageHeader';
import { PageContentSpin } from '../../components/PageSpin';
import { useIsMobile } from '../../hooks/useIsMobile';
import {
  useAdminDashboardSummary,
  useAdminDashboardTrends,
} from '../../hooks/useAdminDashboard';
import { useAdminShops } from '../../hooks/useAdminShops';
import { useSellers } from '../../hooks/useSellers';
import { useThemeStore } from '../../stores/themeStore';
import type {
  DashboardChartType,
  DashboardTrendGrain,
  DashboardTrendGroupBy,
} from '../../types/dashboard';
import '@drop/dashboard/dashboard.css';
import { nowVn, toVnCalendarDate, disabledFutureVnDate, startOfVnDay } from '../../utils/datetime';
import {
  SELLER_PIE_TOP,
  chartEmptyStyle,
  chartTheme,
  money,
  moneyCompact,
  pieSliceLabelStyle,
  statsKpiTone,
  statsNegativeColor,
} from './statsShared';

const CHART_HEIGHT = 280;
const MOBILE_CHART_HEIGHT = 200;

type ViewMode = 'total' | DashboardTrendGroupBy;

const GRAIN_OPTIONS: { label: string; value: DashboardTrendGrain }[] = [
  { label: 'Ngày', value: 'day' },
  { label: 'Tuần', value: 'week' },
  { label: 'Tháng', value: 'month' },
  { label: 'Năm', value: 'year' },
];

const CHART_TYPE_OPTIONS: { label: string; value: DashboardChartType }[] = [
  { label: 'Cột', value: 'column' },
  { label: 'Đường', value: 'line' },
  { label: 'Area', value: 'area' },
];

const VIEW_OPTIONS: { label: string; value: ViewMode }[] = [
  { label: 'Tổng', value: 'total' },
  { label: 'Seller', value: 'seller' },
  { label: 'Shop', value: 'shop' },
];

const RANGE_PRESETS: { label: string; value: [Dayjs, Dayjs] }[] = [
  { label: '7 ngày', value: [nowVn().subtract(6, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '30 ngày', value: [nowVn().subtract(29, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '90 ngày', value: [nowVn().subtract(89, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: 'Năm nay', value: [nowVn().startOf('year'), nowVn().startOf('day')] },
  {
    label: '12 tháng',
    value: [nowVn().subtract(11, 'month').startOf('month'), nowVn().startOf('day')],
  },
];

const formatBucketLabel = (isoDate: string, grain: DashboardTrendGrain): string => {
  const parts = isoDate.split('-');
  if (parts.length !== 3) return isoDate;
  const [y = '', m = '', d = ''] = parts;
  if (grain === 'year') return y;
  if (grain === 'month') return `${m}-${y}`;
  if (grain === 'week') return `T${d}-${m}`;
  return `${d}-${m}`;
};

const maxSpanDays = (grain: DashboardTrendGrain) => {
  switch (grain) {
    case 'week':
      return 730;
    case 'month':
      return 1825;
    case 'year':
      return 3650;
    default:
      return 366;
  }
};

type MetricKey = 'orders' | 'revenue' | 'profit';
type ChartRow = { label: string; value: number; entity?: string };

type SharePieRow = {
  name: string;
  value: number;
  percent: number;
  shareLabel: string;
  legendLabel: string;
};

const buildTopShareRows = (rows: Array<{ name: string; value: number }>) => {
  const ranked = rows
    .map((r) => ({ name: r.name, value: Number(r.value) || 0 }))
    .filter((r) => r.value > 0)
    .sort((a, b) => b.value - a.value);

  if (ranked.length === 0) return [];
  if (ranked.length <= SELLER_PIE_TOP) return ranked;

  const top = ranked.slice(0, SELLER_PIE_TOP);
  const others = ranked.slice(SELLER_PIE_TOP).reduce((sum, r) => sum + r.value, 0);
  return [...top, { name: 'Others', value: others }];
};

const toSharePieData = (
  source: Array<{ name: string; value: number }>,
  total: number,
  isMobile: boolean,
  formatValue: (v: number) => string,
): SharePieRow[] =>
  buildTopShareRows(source).map((r) => {
    const pct = total > 0 ? (r.value / total) * 100 : 0;
    return {
      name: r.name,
      value: r.value,
      percent: pct,
      shareLabel: `${pct.toFixed(1)}%`,
      legendLabel: isMobile
        ? `${r.name} (${pct.toFixed(0)}%)`
        : `${r.name} · ${formatValue(r.value)} (${pct.toFixed(1)}%)`,
    };
  });

interface MetricChartProps {
  title: string;
  metricLabel: string;
  loading: boolean;
  error: unknown;
  data: ChartRow[];
  chartType: DashboardChartType;
  color: string;
  multiSeries: boolean;
  isMobile: boolean;
  valueFormatter?: (v: number) => string;
}

interface SharePieChartProps {
  title: string;
  loading: boolean;
  error: unknown;
  data: SharePieRow[];
  valueLabel: string;
  isMobile: boolean;
  valueFormatter: (v: number) => string;
}

const MetricChart = ({
  title,
  metricLabel,
  loading,
  error,
  data,
  chartType,
  color,
  multiSeries,
  isMobile,
  valueFormatter,
}: MetricChartProps) => {
  const fmt = valueFormatter ?? ((v: number) => v.toLocaleString('en-US'));
  const empty = data.length === 0;
  const height = isMobile ? MOBILE_CHART_HEIGHT : CHART_HEIGHT;
  const emptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  const common = {
    height,
    data,
    xField: 'label',
    yField: 'value',
    axis: {
      ...(isMobile ? chartTheme.axisSm : chartTheme.axisMd),
      x: {
        ...(isMobile ? chartTheme.axisSm.x : chartTheme.axisMd.x),
        labelAutoRotate: isMobile,
        labelAutoHide: isMobile,
      },
    },
    tooltip: {
      items: [
        {
          field: 'value',
          name: metricLabel,
          valueFormatter: (v: number) => fmt(Number(v)),
        },
      ],
    },
  } as const;

  const chart: ReactNode = (() => {
    if (multiSeries) {
      const seriesProps = {
        ...common,
        colorField: 'entity',
        legend: {
          color: {
            position: 'bottom' as const,
            ...(isMobile ? chartTheme.legendColorSm : chartTheme.legendColorMd),
            layout: isMobile
              ? { justifyContent: 'center', flexWrap: true }
              : undefined,
          },
        },
      };
      if (chartType === 'column') {
        return <Column {...seriesProps} group style={{ maxWidth: isMobile ? 10 : 16 }} />;
      }
      if (chartType === 'area') {
        return <Area {...seriesProps} style={{ fillOpacity: 0.22, lineWidth: 2 }} />;
      }
      return (
        <Line
          {...seriesProps}
          style={{ lineWidth: 2 }}
          point={{ size: isMobile ? 1.5 : 2, shape: 'circle' }}
        />
      );
    }

    if (chartType === 'line') {
      return (
        <Line
          {...common}
          style={{ lineWidth: 2, stroke: color }}
          point={{
            size: isMobile ? 2 : 3,
            shape: 'circle',
            style: { fill: color },
          }}
        />
      );
    }
    if (chartType === 'area') {
      return (
        <Area
          {...common}
          style={{ fill: color, fillOpacity: 0.28, stroke: color, lineWidth: 2 }}
        />
      );
    }
    return <Column {...common} style={{ maxWidth: isMobile ? 22 : 36, fill: color }} />;
  })();

  return (
    <Card size="small" className="dashboard-section-card" title={title}>
      <PageContentSpin spinning={loading}>
        {error ? (
          <Alert
            type="error"
            showIcon
            title={error instanceof Error ? error.message : 'Lỗi tải biểu đồ'}
          />
        ) : empty ? (
          <div className="dashboard-chart-empty" style={emptyStyle}>
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
          </div>
        ) : (
          chart
        )}
      </PageContentSpin>
    </Card>
  );
};

const SharePieChart = ({
  title,
  loading,
  error,
  data,
  valueLabel,
  isMobile,
  valueFormatter,
}: SharePieChartProps) => {
  const empty = data.length === 0;
  const height = isMobile ? MOBILE_CHART_HEIGHT : CHART_HEIGHT;
  const emptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  return (
    <Card size="small" className="dashboard-section-card" title={title}>
      <PageContentSpin spinning={loading}>
        {error ? (
          <Alert
            type="error"
            showIcon
            title={error instanceof Error ? error.message : 'Lỗi tải biểu đồ'}
          />
        ) : empty ? (
          <div className="dashboard-chart-empty" style={emptyStyle}>
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
          </div>
        ) : (
          <Pie
            height={height}
            data={data}
            angleField="value"
            colorField="legendLabel"
            radius={0.9}
            innerRadius={0.55}
            legend={false}
            label={
              isMobile
                ? false
                : {
                    text: 'shareLabel',
                    style: pieSliceLabelStyle(),
                  }
            }
            tooltip={{
              title: 'name',
              items: [
                {
                  field: 'value',
                  name: valueLabel,
                  valueFormatter: (v: number) => valueFormatter(Number(v)),
                },
                {
                  field: 'percent',
                  name: 'Tỷ trọng',
                  valueFormatter: (v: number) => `${Number(v).toFixed(1)}%`,
                },
              ],
            }}
          />
        )}
      </PageContentSpin>
    </Card>
  );
};

export const ChartsStatsPage = () => {
  const isMobile = useIsMobile();
  /** Re-evaluate chart labels / KPI chips when theme toggles. */
  useThemeStore((s) => s.mode);
  const kpiBlue = statsKpiTone('blue');
  const kpiGreen = statsKpiTone('green');
  const kpiRed = statsKpiTone('red');
  const negativeColor = statsNegativeColor();

  const [range, setRange] = useState<[Dayjs, Dayjs]>([
    nowVn().subtract(29, 'day').startOf('day'),
    nowVn().startOf('day'),
  ]);
  const [grain, setGrain] = useState<DashboardTrendGrain>('day');
  const [chartType, setChartType] = useState<DashboardChartType>('column');
  const [viewMode, setViewMode] = useState<ViewMode>('total');
  const [ownerUserId, setOwnerUserId] = useState<string | undefined>();
  const [shopId, setShopId] = useState<string | undefined>();

  const { data: sellers = [] } = useSellers();
  const { data: shops = [] } = useAdminShops();

  const sellerOptions = useMemo(
    () =>
      sellers.map((s) => ({
        value: s.id,
        label: s.fullName?.trim() || s.userName || s.email || s.id,
      })),
    [sellers],
  );

  const shopOptions = useMemo(
    () =>
      shops
        .filter((s) => {
          if (s.status !== 'Active') return false;
          if (ownerUserId && s.ownerId !== ownerUserId) return false;
          return true;
        })
        .map((s) => {
          const shopName = s.name?.trim() || 'Shop';
          const profile = s.profileName?.trim();
          return {
            value: s.id,
            label: profile ? `${profile} (${shopName})` : shopName,
          };
        }),
    [shops, ownerUserId],
  );

  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);
  const groupBy = viewMode === 'total' ? undefined : viewMode;
  // Seller/Shop view: pie derives from trends.series → skip summary entirely.
  // Total view: only need sellers ranking for share pies (no KPIs / listings / top products).
  const needShareSummary = viewMode === 'total';

  const summaryQuery = useAdminDashboardSummary(
    {
      fromDate,
      toDate,
      ownerUserId,
      shopId,
      include: 'sellers',
    },
    { enabled: needShareSummary },
  );

  const trendsQuery = useAdminDashboardTrends({
    fromDate,
    toDate,
    grain,
    groupBy,
    ownerUserId,
    shopId,
    includeListed: false,
  });

  const days = trendsQuery.data?.days ?? [];
  const series = trendsQuery.data?.series ?? [];
  const activeGrain = (trendsQuery.data?.grain as DashboardTrendGrain | undefined) ?? grain;
  const multiSeries = Boolean(groupBy) && series.length > 0;

  const sellersToday = summaryQuery.data?.sellersToday ?? [];

  /** Aggregate trends.series into pie rows (Seller/Shop view). */
  const seriesShareSource = useMemo(() => {
    if (!multiSeries) return { orders: [] as Array<{ name: string; value: number }>, revenue: [] as Array<{ name: string; value: number }> };
    const ordersBy = new Map<string, number>();
    const revenueBy = new Map<string, number>();
    for (const p of series) {
      const name = (p.entityName || p.entityId || 'Unknown').trim() || 'Unknown';
      ordersBy.set(name, (ordersBy.get(name) ?? 0) + (Number(p.orders) || 0));
      revenueBy.set(name, (revenueBy.get(name) ?? 0) + (Number(p.revenue) || 0));
    }
    return {
      orders: [...ordersBy.entries()].map(([name, value]) => ({ name, value })),
      revenue: [...revenueBy.entries()].map(([name, value]) => ({ name, value })),
    };
  }, [multiSeries, series]);

  const sellerOrderSource = useMemo(
    () =>
      sellersToday.map((s) => ({
        name: (s.displayName || s.userId).trim() || s.userId,
        value: s.orders,
      })),
    [sellersToday],
  );
  const sellerRevenueSource = useMemo(
    () =>
      sellersToday.map((s) => ({
        name: (s.displayName || s.userId).trim() || s.userId,
        value: Number(s.revenue) || 0,
      })),
    [sellersToday],
  );

  const orderShareSource = multiSeries ? seriesShareSource.orders : sellerOrderSource;
  const revenueShareSource = multiSeries ? seriesShareSource.revenue : sellerRevenueSource;

  const sellerOrdersTotal = orderShareSource.reduce((sum, r) => sum + (Number(r.value) || 0), 0);
  const sellerRevenueTotal = revenueShareSource.reduce((sum, r) => sum + (Number(r.value) || 0), 0);

  const orderSharePieData = toSharePieData(
    orderShareSource,
    sellerOrdersTotal,
    isMobile,
    (v) => v.toLocaleString('en-US'),
  );
  const revenueSharePieData = toSharePieData(revenueShareSource, sellerRevenueTotal, isMobile, money);

  const shareTitle = viewMode === 'shop' ? 'Shop' : 'Seller';

  const metricValue = (
    metric: MetricKey,
    point: { orders: number; revenue: number; cost?: number; profit?: number },
  ) => {
    if (metric === 'orders') return point.orders;
    if (metric === 'revenue') return Number(point.revenue) || 0;
    return Number(
      point.profit ?? (Number(point.revenue) || 0) - (Number(point.cost) || 0),
    );
  };

  const buildSeries = (metric: MetricKey): ChartRow[] => {
    if (multiSeries) {
      return series.map((p) => ({
        label: formatBucketLabel(p.date, activeGrain),
        entity: p.entityName,
        value: metricValue(metric, p),
      }));
    }
    return days.map((d) => ({
      label: formatBucketLabel(d.date, activeGrain),
      value: metricValue(metric, d),
    }));
  };

  const ordersData = buildSeries('orders');
  const revenueData = buildSeries('revenue');
  const profitData = buildSeries('profit');

  const totalOrders = days.reduce((s, d) => s + d.orders, 0);
  const totalRevenue = days.reduce((s, d) => s + (Number(d.revenue) || 0), 0);
  const totalProfit = days.reduce(
    (s, d) =>
      s + (Number(d.profit ?? (Number(d.revenue) || 0) - (Number(d.cost) || 0)) || 0),
    0,
  );

  const formatMoney = isMobile ? moneyCompact : money;

  const disabledDate = (current: Dayjs) => disabledFutureVnDate(current);

  const onRangeChange = (values: [Dayjs | null, Dayjs | null] | null) => {
    if (!values?.[0] || !values[1]) return;
    let start = startOfVnDay(values[0]);
    let end = startOfVnDay(values[1]);
    if (end.isBefore(start)) {
      [start, end] = [end, start];
    }
    const max = maxSpanDays(grain);
    if (end.diff(start, 'day') + 1 > max) {
      start = end.subtract(max - 1, 'day');
    }
    setRange([start, end]);
  };

  const chartLoading = trendsQuery.isLoading || trendsQuery.isFetching;
  const shareLoading = needShareSummary
    ? summaryQuery.isLoading || summaryQuery.isFetching
    : chartLoading;
  const shareError = needShareSummary
    ? summaryQuery.isError
      ? summaryQuery.error
      : null
    : trendsQuery.isError
      ? trendsQuery.error
      : null;

  const filterControls = (
    <>
      <AdminDateRangeFilter
        variant="toolbar"
        value={range}
        allowClear={false}
        disabledDate={disabledDate}
        presets={RANGE_PRESETS}
        onChange={(values) => {
          if (!values) return;
          onRangeChange(values);
        }}
      />
      <Segmented
        size="small"
        block={isMobile}
        value={viewMode}
        options={VIEW_OPTIONS}
        onChange={(value) => {
          const next = value as ViewMode;
          setViewMode(next);
          if (next !== 'total' && chartType === 'column') {
            setChartType('line');
          }
        }}
      />
      {isMobile ? (
        <div className="dashboard-toolbar-row">
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Seller"
            value={ownerUserId}
            options={sellerOptions}
            onChange={(v) => {
              setOwnerUserId(v);
              setShopId(undefined);
            }}
            style={{ width: '100%' }}
          />
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            value={shopId}
            options={shopOptions}
            onChange={(v) => setShopId(v)}
            style={{ width: '100%' }}
          />
        </div>
      ) : (
        <>
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Seller"
            value={ownerUserId}
            options={sellerOptions}
            onChange={(v) => {
              setOwnerUserId(v);
              setShopId(undefined);
            }}
            style={{ width: 120 }}
          />
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            value={shopId}
            options={shopOptions}
            onChange={(v) => setShopId(v)}
            style={{ width: 130 }}
          />
        </>
      )}
      <Segmented
        size="small"
        block={isMobile}
        value={grain}
        options={GRAIN_OPTIONS}
        onChange={(value) => {
          const next = value as DashboardTrendGrain;
          setGrain(next);
          const max = maxSpanDays(next);
          if (range[1].diff(range[0], 'day') + 1 > max) {
            setRange([range[1].subtract(max - 1, 'day'), range[1]]);
          }
        }}
      />
      {isMobile ? (
        <Segmented
          size="small"
          block
          value={chartType}
          options={CHART_TYPE_OPTIONS}
          onChange={(v) => setChartType(v as DashboardChartType)}
        />
      ) : (
        <Select
          size="small"
          value={chartType}
          options={CHART_TYPE_OPTIONS}
          onChange={(v) => setChartType(v)}
          style={{ width: 88 }}
        />
      )}
    </>
  );

  return (
    <div className="dashboard-page">
      <PageHeader
        title="Tổng hợp"
        extra={
          isMobile ? undefined : (
            <div className="dashboard-toolbar is-inline">{filterControls}</div>
          )
        }
      />

      {isMobile ? <div className="dashboard-toolbar">{filterControls}</div> : null}

      {trendsQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={
            trendsQuery.error instanceof Error
              ? trendsQuery.error.message
              : 'Lỗi tải dữ liệu biểu đồ'
          }
        />
      ) : null}

      {needShareSummary && summaryQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={
            summaryQuery.error instanceof Error
              ? summaryQuery.error.message
              : 'Lỗi tải tỷ trọng Seller'
          }
          style={{ marginTop: trendsQuery.isError ? 8 : 0 }}
        />
      ) : null}

      <Row gutter={[8, 8]}>
        <Col xs={12} sm={8}>
          <Card size="small" className="dashboard-kpi-card is-compact">
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Orders</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiBlue.iconBg, color: kpiBlue.iconColor }}
              >
                <ShoppingCartOutlined />
              </span>
            </div>
            <p className="dashboard-kpi-value">{totalOrders.toLocaleString('en-US')}</p>
          </Card>
        </Col>
        <Col xs={12} sm={8}>
          <Card size="small" className="dashboard-kpi-card is-compact">
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Revenue</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiGreen.iconBg, color: kpiGreen.iconColor }}
              >
                <DollarOutlined />
              </span>
            </div>
            <p className="dashboard-kpi-value">{formatMoney(totalRevenue)}</p>
          </Card>
        </Col>
        <Col xs={12} sm={8}>
          <Card size="small" className="dashboard-kpi-card is-compact">
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Profit</span>
              <span
                className="dashboard-kpi-icon"
                style={{
                  background: (totalProfit >= 0 ? kpiGreen : kpiRed).iconBg,
                  color: (totalProfit >= 0 ? kpiGreen : kpiRed).iconColor,
                }}
              >
                <FundOutlined />
              </span>
            </div>
            <p
              className="dashboard-kpi-value"
              style={{ color: totalProfit < 0 ? negativeColor : undefined }}
            >
              {formatMoney(totalProfit)}
            </p>
          </Card>
        </Col>
      </Row>

      <Space orientation="vertical" size={isMobile ? 10 : 14} style={{ width: '100%' }}>
        <Row gutter={[8, 8]}>
          <Col xs={24} lg={16}>
            <MetricChart
              title="Orders"
              metricLabel="Orders"
              loading={chartLoading}
              error={trendsQuery.isError ? trendsQuery.error : null}
              data={ordersData}
              chartType={chartType}
              color="#1677ff"
              multiSeries={multiSeries}
              isMobile={isMobile}
            />
          </Col>
          <Col xs={24} lg={8}>
            <SharePieChart
              title={shareTitle}
              loading={shareLoading}
              error={shareError}
              data={orderSharePieData}
              valueLabel="Đơn hàng"
              isMobile={isMobile}
              valueFormatter={(v) => v.toLocaleString('en-US')}
            />
          </Col>
        </Row>

        <Row gutter={[8, 8]}>
          <Col xs={24} lg={16}>
            <MetricChart
              title="Revenue"
              metricLabel="Revenue"
              loading={chartLoading}
              error={null}
              data={revenueData}
              chartType={chartType}
              color="#52c41a"
              multiSeries={multiSeries}
              isMobile={isMobile}
              valueFormatter={money}
            />
          </Col>
          <Col xs={24} lg={8}>
            <SharePieChart
              title={shareTitle}
              loading={shareLoading}
              error={null}
              data={revenueSharePieData}
              valueLabel="Doanh thu"
              isMobile={isMobile}
              valueFormatter={money}
            />
          </Col>
        </Row>

        <MetricChart
          title="Profit"
          metricLabel="Profit"
          loading={chartLoading}
          error={null}
          data={profitData}
          chartType={chartType}
          color="#722ed1"
          multiSeries={multiSeries}
          isMobile={isMobile}
          valueFormatter={money}
        />
      </Space>
    </div>
  );
};
