import { useState, type ReactNode } from 'react';
import {
  Alert,
  Card,
  Col,
  Empty,
  Row,
  Segmented,
  Table,
  Typography,
} from 'antd';
import {
  DollarOutlined,
  FundOutlined,
  ShoppingCartOutlined,
  AccountBookOutlined,
} from '@ant-design/icons';
import { Column, Pie } from '@ant-design/plots';
import { type Dayjs } from 'dayjs';
import {
  chartTheme,
  formatDayLabel,
  money,
  moneyCompact,
  pieSliceLabelStyle,
  sharePieLegend,
  shortMoney,
} from '@drop/dashboard';
import { PageHeader, PageContentSpin, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import { toVnCalendarDate } from '@drop/utils/datetime';
import type {
  DashboardPeriod,
  DashboardSellerRank,
  DashboardShopFinance,
} from '@drop/types/dashboard';
import '@drop/dashboard/dashboard.css';
import { DayHeatmapStrip } from './DayHeatmapStrip';
import { useRevenueStatsDeps } from './RevenueStatsDepsContext';
import type { RevenueStatsPortalConfig } from './portalConfig';
import { statsKpiTone, statsNegativeColor } from './statsKpi';
import {
  PERIOD_OPTIONS,
  RANGE_PRESETS,
  SELLER_PIE_TOP,
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

type FinanceDim = 'seller' | 'shop';

const buildRevenuePieData = (rows: Array<{ name: string; revenue: number }>) => {
  const ranked = rows
    .map((r) => ({ name: r.name, revenue: Number(r.revenue) || 0 }))
    .filter((r) => r.revenue > 0)
    .sort((a, b) => b.revenue - a.revenue);

  if (ranked.length === 0) return [];
  if (ranked.length <= SELLER_PIE_TOP) return ranked;

  const top = ranked.slice(0, SELLER_PIE_TOP);
  const othersRevenue = ranked.slice(SELLER_PIE_TOP).reduce((sum, r) => sum + r.revenue, 0);
  return [...top, { name: 'Others', revenue: othersRevenue }];
};

export type SharedRevenueStatsPageProps = {
  config: RevenueStatsPortalConfig;
};

export const RevenueStatsPage = ({ config }: SharedRevenueStatsPageProps) => {
  const { useDashboardSummary, useDashboardTrends, useThemeMode } = useRevenueStatsDeps();
  const isMobile = useIsMobile();
  /** Re-evaluate chart labels / KPI chips when theme toggles. */
  useThemeMode();
  const [range, setRange] = useState<[Dayjs, Dayjs]>(() => rangeForPeriod('month'));
  const [period, setPeriod] = useState<DashboardPeriod | 'custom'>('month');
  const [financeDim, setFinanceDim] = useState<FinanceDim>(
    config.showSellerSharePie ? 'seller' : 'shop',
  );
  const kpiBlue = statsKpiTone('blue');
  const kpiGreen = statsKpiTone('green');
  const kpiOrange = statsKpiTone('orange');
  const kpiRed = statsKpiTone('red');
  const negativeColor = statsNegativeColor();

  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);

  const include = config.showSellerSharePie
    ? 'kpis,sellers,shopsFinance'
    : 'kpis,shopsFinance';

  const queryParams = {
    fromDate,
    toDate,
    // Only sections this page renders — skip cardSpend / orderStatus / listings / shops / topProducts.
    include,
  };
  const summaryQuery = useDashboardSummary(queryParams);
  const trendsQuery = useDashboardTrends({
    ...queryParams,
    grain: 'day',
    // Heatmap + revenue column only need orders/revenue (skip listing scan + Items/cost joins).
    includeListed: false,
    metrics: 'revenue',
  });
  const summary = summaryQuery.data;
  const trendDays = trendsQuery.data?.days ?? [];

  const sellersToday = summary?.sellersToday ?? [];
  const shopsFinance = summary?.shopsFinance ?? [];
  const activeFinanceDim: FinanceDim = config.showSellerSharePie ? financeDim : 'shop';

  const revenueTrendData = trendDays.map((d) => ({
    date: formatDayLabel(d.date),
    value: Number(d.revenue) || 0,
    orders: d.orders,
  }));

  const revenue = Number(summary?.today.revenue ?? 0);
  const cost = Number(summary?.today.cost ?? 0);
  const profit = Number(summary?.today.profit ?? 0);
  const orderCount = summary?.today.orderCount ?? 0;

  const formatMoney = isMobile ? moneyCompact : money;
  const activeChartHeight = isMobile ? MOBILE_CHART_HEIGHT : chartHeight;
  const activeChartEmptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  const shopFinanceLabels = (() => {
    const counts = new Map<string, number>();
    for (const s of shopsFinance) {
      const base = (s.shopName || 'Unassigned').trim() || 'Unassigned';
      counts.set(base, (counts.get(base) ?? 0) + 1);
    }
    const seen = new Map<string, number>();
    return shopsFinance.map((s) => {
      const base = (s.shopName || 'Unassigned').trim() || 'Unassigned';
      const n = (seen.get(base) ?? 0) + 1;
      seen.set(base, n);
      if ((counts.get(base) ?? 0) <= 1) return base;
      const seller = s.sellerName?.trim();
      if (seller && config.showSellerSharePie) return `${base} · ${seller}`;
      return `${base} (${n})`;
    });
  })();

  const sellerPieSource = sellersToday.map((s) => ({
    name: (s.displayName || s.userId).trim() || s.userId,
    revenue: Number(s.revenue) || 0,
  }));
  const shopPieSource = shopsFinance.map((s, i) => ({
    name: shopFinanceLabels[i] ?? s.shopName,
    revenue: Number(s.revenue) || 0,
  }));

  const sellerRevenueTotal = sellerPieSource.reduce((sum, r) => sum + r.revenue, 0);
  const shopRevenueTotal = shopPieSource.reduce((sum, r) => sum + r.revenue, 0);

  const toPieData = (
    source: Array<{ name: string; revenue: number }>,
    total: number,
  ) =>
    buildRevenuePieData(source).map((r) => {
      const pct = total > 0 ? (r.revenue / total) * 100 : 0;
      return {
        name: r.name,
        revenue: r.revenue,
        percent: pct,
        shareLabel: `${pct.toFixed(1)}%`,
        legendLabel: isMobile
          ? `${r.name} (${pct.toFixed(0)}%)`
          : `${r.name} · ${money(r.revenue)} (${pct.toFixed(1)}%)`,
      };
    });

  const sellerPieData = toPieData(sellerPieSource, sellerRevenueTotal);
  const shopPieData = toPieData(shopPieSource, shopRevenueTotal);

  const financeColumnRows =
    activeFinanceDim === 'seller'
      ? sellersToday.map((s) => ({
          entity: (s.displayName || s.userId).trim() || s.userId,
          revenue: Number(s.revenue) || 0,
          profit: Number(s.profit) || 0,
          cost: Number(s.cost) || 0,
          orders: s.orders,
        }))
      : shopsFinance.map((s, i) => ({
          entity: shopFinanceLabels[i] ?? s.shopName,
          revenue: Number(s.revenue) || 0,
          profit: Number(s.profit) || 0,
          cost: Number(s.cost) || 0,
          orders: s.orders,
        }));

  const financeColumnData = financeColumnRows.flatMap((s) => [
    { entity: s.entity, metric: 'Revenue', value: s.revenue, orders: s.orders },
    { entity: s.entity, metric: 'Profit', value: s.profit, orders: s.orders },
  ]);

  const sellerColumns = [
    {
      title: '#',
      key: 'rank',
      width: 48,
      render: (_: unknown, __: DashboardSellerRank, index: number) => (
        <span className={renderRankMedal(index)}>{index + 1}</span>
      ),
    },
    {
      title: 'Seller',
      dataIndex: 'displayName',
      key: 'displayName',
      ellipsis: true,
    },
    {
      title: 'Orders',
      dataIndex: 'orders',
      key: 'orders',
      width: 72,
      align: 'right' as const,
    },
    {
      title: 'Revenue',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 110,
      align: 'right' as const,
      render: (v: number) => money(Number(v)),
    },
    {
      title: 'Cost',
      dataIndex: 'cost',
      key: 'cost',
      width: 100,
      align: 'right' as const,
      render: (v: number) => money(Number(v)),
    },
    {
      title: 'Profit',
      dataIndex: 'profit',
      key: 'profit',
      width: 110,
      align: 'right' as const,
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
      title: 'Margin',
      dataIndex: 'profitMargin',
      key: 'profitMargin',
      width: 72,
      align: 'right' as const,
      render: (v: number | null | undefined) => {
        if (v == null) return '—';
        const margin = Number(v);
        return (
          <Text type={margin < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
            {`${margin.toFixed(1)}%`}
          </Text>
        );
      },
    },
    {
      title: 'Share',
      key: 'share',
      width: 72,
      align: 'right' as const,
      render: (_: unknown, row: DashboardSellerRank) => {
        if (sellerRevenueTotal <= 0) return '—';
        return `${(((Number(row.revenue) || 0) / sellerRevenueTotal) * 100).toFixed(1)}%`;
      },
    },
  ];

  const sellerColumnsMobile = [
    {
      title: '#',
      key: 'rank',
      width: 36,
      render: (_: unknown, __: DashboardSellerRank, index: number) => (
        <span className={renderRankMedal(index)}>{index + 1}</span>
      ),
    },
    {
      title: 'Seller',
      dataIndex: 'displayName',
      key: 'displayName',
      ellipsis: true,
    },
    {
      title: 'Đơn',
      dataIndex: 'orders',
      key: 'orders',
      width: 44,
      align: 'right' as const,
    },
    {
      title: 'Rev',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 64,
      align: 'right' as const,
      render: (v: number) => moneyCompact(Number(v)),
    },
    {
      title: 'Cost',
      dataIndex: 'cost',
      key: 'cost',
      width: 64,
      align: 'right' as const,
      render: (v: number) => moneyCompact(Number(v)),
    },
    {
      title: 'Profit',
      dataIndex: 'profit',
      key: 'profit',
      width: 68,
      align: 'right' as const,
      render: (v: number) => {
        const rowProfit = Number(v) || 0;
        return (
          <Text type={rowProfit < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
            {moneyCompact(rowProfit)}
          </Text>
        );
      },
    },
  ];

  const shopColumns = [
    {
      title: '#',
      key: 'rank',
      width: 48,
      render: (_: unknown, __: DashboardShopFinance, index: number) => (
        <span className={renderRankMedal(index)}>{index + 1}</span>
      ),
    },
    {
      title: 'Shop',
      key: 'shop',
      ellipsis: true,
      render: (_: unknown, row: DashboardShopFinance, index: number) => (
        <div>
          <div>{shopFinanceLabels[index] ?? row.shopName}</div>
          {row.shopProfileName ? (
            <Text type="secondary" style={{ fontSize: 12 }}>
              {row.shopProfileName}
            </Text>
          ) : null}
        </div>
      ),
    },
    ...(config.showSellerSharePie
      ? [
          {
            title: 'Seller',
            dataIndex: 'sellerName',
            key: 'sellerName',
            width: 120,
            ellipsis: true,
            render: (v: string | null | undefined) => v || '—',
          },
        ]
      : []),
    {
      title: 'Orders',
      dataIndex: 'orders',
      key: 'orders',
      width: 72,
      align: 'right' as const,
    },
    {
      title: 'Revenue',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 110,
      align: 'right' as const,
      render: (v: number) => money(Number(v)),
    },
    {
      title: 'Cost',
      dataIndex: 'cost',
      key: 'cost',
      width: 100,
      align: 'right' as const,
      render: (v: number) => money(Number(v)),
    },
    {
      title: 'Profit',
      dataIndex: 'profit',
      key: 'profit',
      width: 110,
      align: 'right' as const,
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
      title: 'Margin',
      key: 'margin',
      width: 72,
      align: 'right' as const,
      render: (_: unknown, row: DashboardShopFinance) => {
        const rev = Number(row.revenue) || 0;
        if (rev <= 0) return '—';
        const margin = ((Number(row.profit) || 0) / rev) * 100;
        return (
          <Text type={margin < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
            {`${margin.toFixed(1)}%`}
          </Text>
        );
      },
    },
    {
      title: 'Share',
      key: 'share',
      width: 72,
      align: 'right' as const,
      render: (_: unknown, row: DashboardShopFinance) => {
        if (shopRevenueTotal <= 0) return '—';
        return `${(((Number(row.revenue) || 0) / shopRevenueTotal) * 100).toFixed(1)}%`;
      },
    },
  ];

  const shopColumnsMobile = [
    {
      title: '#',
      key: 'rank',
      width: 36,
      render: (_: unknown, __: DashboardShopFinance, index: number) => (
        <span className={renderRankMedal(index)}>{index + 1}</span>
      ),
    },
    {
      title: 'Shop',
      key: 'shop',
      ellipsis: true,
      render: (_: unknown, row: DashboardShopFinance, index: number) =>
        shopFinanceLabels[index] ?? row.shopName,
    },
    {
      title: 'Đơn',
      dataIndex: 'orders',
      key: 'orders',
      width: 44,
      align: 'right' as const,
    },
    {
      title: 'Rev',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 64,
      align: 'right' as const,
      render: (v: number) => moneyCompact(Number(v)),
    },
    {
      title: 'Cost',
      dataIndex: 'cost',
      key: 'cost',
      width: 64,
      align: 'right' as const,
      render: (v: number) => moneyCompact(Number(v)),
    },
    {
      title: 'Profit',
      dataIndex: 'profit',
      key: 'profit',
      width: 68,
      align: 'right' as const,
      render: (v: number) => {
        const rowProfit = Number(v) || 0;
        return (
          <Text type={rowProfit < 0 ? 'danger' : undefined} style={{ fontSize: 12 }}>
            {moneyCompact(rowProfit)}
          </Text>
        );
      },
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

  const renderRevenueSharePie = (
    data: Array<{
      name: string;
      revenue: number;
      percent: number;
      shareLabel: string;
      legendLabel: string;
    }>,
  ) => (
    <Pie
      height={activeChartHeight}
      data={data}
      angleField="revenue"
      colorField="legendLabel"
      radius={0.9}
      innerRadius={0.55}
      legend={sharePieLegend(isMobile)}
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
            field: 'revenue',
            name: 'Doanh thu',
            valueFormatter: (v: number) => money(Number(v)),
          },
          {
            field: 'percent',
            name: 'Tỷ trọng',
            valueFormatter: (v: number) => `${Number(v).toFixed(1)}%`,
          },
        ],
      }}
    />
  );

  const onPeriodChange = (value: string | number) => {
    const next = value as DashboardPeriod;
    setPeriod(next);
    setRange(rangeForPeriod(next));
  };

  const periodControls = (
    <>
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
      {config.showSellerSharePie ? (
        <Segmented
          size="small"
          block={isMobile}
          value={financeDim}
          options={[
            { label: 'Seller', value: 'seller' },
            { label: 'Shop', value: 'shop' },
          ]}
          onChange={(value) => setFinanceDim(value as FinanceDim)}
        />
      ) : null}
    </>
  );

  return (
    <div className="dashboard-page">
      <PageHeader
        title="Doanh thu"
        extra={isMobile ? undefined : <div className="dashboard-toolbar is-inline">{periodControls}</div>}
      />

      {isMobile ? <div className="dashboard-toolbar">{periodControls}</div> : null}

      {summaryQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={
            summaryQuery.error instanceof Error
              ? summaryQuery.error.message
              : 'Lỗi tải thống kê doanh số'
          }
        />
      ) : null}

      <PageContentSpin spinning={summaryQuery.isLoading || summaryQuery.isFetching}>
        <Row gutter={[8, 8]}>
          <Col xs={12} sm={6}>
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
            </Card>
          </Col>
          <Col xs={12} sm={6}>
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
          <Col xs={12} sm={6}>
            <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
              <div className="dashboard-kpi-head">
                <span className="dashboard-kpi-label">Cost</span>
                <span
                  className="dashboard-kpi-icon"
                  style={{ background: kpiOrange.iconBg, color: kpiOrange.iconColor }}
                >
                  <AccountBookOutlined />
                </span>
              </div>
              <p className="dashboard-kpi-value">{formatMoney(cost)}</p>
            </Card>
          </Col>
          <Col xs={12} sm={6}>
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
        </Row>

        <div className="dashboard-section-block">
          <p className="dashboard-section-label">Revenue heatmap</p>
          <Card size="small" className="dashboard-section-card" title="Revenue by day">
            {renderChartBody(
              trendsQuery.isLoading || trendsQuery.isFetching,
              trendDays.length === 0,
              trendsQuery.isError ? trendsQuery.error : null,
              <DayHeatmapStrip
                days={trendDays.map((d) => ({
                  date: d.date,
                  value: Number(d.revenue) || 0,
                }))}
                formatValue={money}
                formatCellValue={shortMoney}
                maxDaysForCellValue={21}
              />,
            )}
          </Card>
        </div>

        <div className="dashboard-section-block">
          <Card size="small" className="dashboard-section-card" title="Doanh thu">
            {renderChartBody(
              trendsQuery.isLoading || trendsQuery.isFetching,
              revenueTrendData.length === 0,
              trendsQuery.isError ? trendsQuery.error : null,
              <Column
                height={activeChartHeight}
                data={revenueTrendData}
                xField="date"
                yField="value"
                style={{ maxWidth: isMobile ? 18 : 28, fill: '#52c41a' }}
                axis={{
                  ...(isMobile ? chartTheme.axisSm : chartTheme.axisMd),
                  x: {
                    ...(isMobile ? chartTheme.axisSm.x : chartTheme.axisMd.x),
                    labelAutoRotate: isMobile,
                    labelAutoHide: isMobile,
                  },
                  y: {
                    ...(isMobile ? chartTheme.axisSm.y : chartTheme.axisMd.y),
                    labelFormatter: (v: number) => shortMoney(Number(v)),
                  },
                }}
                tooltip={{
                  items: [
                    {
                      field: 'value',
                      name: 'Doanh thu',
                      valueFormatter: (v: number) => money(Number(v)),
                    },
                    { field: 'orders', name: 'Orders' },
                  ],
                }}
              />,
            )}
          </Card>
        </div>

        <div className="dashboard-section-block">
          <p className="dashboard-section-label">Tỷ trọng doanh thu</p>
          <Row gutter={[8, 8]}>
            {config.showSellerSharePie ? (
              <Col xs={24} lg={12}>
                <Card size="small" className="dashboard-section-card" title="Seller">
                  {renderChartBody(
                    summaryQuery.isLoading,
                    sellerPieData.length === 0,
                    null,
                    renderRevenueSharePie(sellerPieData),
                  )}
                </Card>
              </Col>
            ) : null}
            <Col xs={24} lg={config.showSellerSharePie ? 12 : 24}>
              <Card size="small" className="dashboard-section-card" title="Shop">
                {renderChartBody(
                  summaryQuery.isLoading,
                  shopPieData.length === 0,
                  null,
                  renderRevenueSharePie(shopPieData),
                )}
              </Card>
            </Col>
          </Row>
        </div>

        <Alert
          type="info"
          showIcon={!isMobile}
          className="dashboard-status-alert"
          style={{ marginTop: 12 }}
          title={
            isMobile
              ? activeFinanceDim === 'seller'
                ? 'Top seller theo API (tối đa ~20).'
                : 'Top shop theo API (tối đa ~12).'
              : activeFinanceDim === 'seller'
                ? 'Bảng xếp hạng seller hiển thị top theo API (tối đa ~20).'
                : 'Doanh số shop hiển thị top theo API (tối đa ~12).'
          }
        />

        <div className="dashboard-section-block">
          <Card
            size="small"
            className="dashboard-section-card"
            title={
              activeFinanceDim === 'seller'
                ? 'Revenue & profit by seller'
                : 'Revenue & profit by shop'
            }
          >
            {renderChartBody(
              summaryQuery.isLoading,
              financeColumnData.length === 0,
              null,
              <Column
                height={activeChartHeight}
                data={financeColumnData}
                xField="entity"
                yField="value"
                colorField="metric"
                group
                style={{ maxWidth: isMobile ? 12 : 18 }}
                axis={{
                  ...chartTheme.axisSm,
                  x: {
                    ...chartTheme.axisSm.x,
                    labelAutoRotate: true,
                    labelAutoHide: isMobile,
                  },
                }}
                legend={{
                  color: {
                    position: 'top',
                    ...chartTheme.legendColorSm,
                  },
                }}
                scale={{
                  color: {
                    domain: ['Revenue', 'Profit'],
                    range: ['#1677ff', '#52c41a'],
                  },
                }}
                tooltip={{
                  items: [
                    { field: 'value', name: 'Value' },
                    { field: 'orders', name: 'Orders' },
                  ],
                }}
              />,
            )}
          </Card>
        </div>

        <div className="dashboard-section-block">
          <Card
            size="small"
            className="dashboard-section-card"
            title={activeFinanceDim === 'seller' ? 'Xếp hạng seller' : 'Xếp hạng shop'}
          >
            {activeFinanceDim === 'seller' ? (
              <Table
                size="small"
                pagination={false}
                rowKey="userId"
                dataSource={sellersToday}
                columns={isMobile ? sellerColumnsMobile : sellerColumns}
                scroll={{ x: isMobile ? 420 : 900 }}
                className={isMobile ? 'dashboard-seller-table-mobile' : undefined}
                locale={{ emptyText: 'Chưa có dữ liệu' }}
              />
            ) : (
              <Table
                size="small"
                pagination={false}
                rowKey={(row) => row.shopId ?? `${row.shopName}-${row.sellerName}`}
                dataSource={shopsFinance}
                columns={isMobile ? shopColumnsMobile : shopColumns}
                scroll={{ x: isMobile ? 420 : config.showSellerSharePie ? 960 : 840 }}
                className={isMobile ? 'dashboard-seller-table-mobile' : undefined}
                locale={{ emptyText: 'Chưa có dữ liệu' }}
              />
            )}
          </Card>
        </div>
      </PageContentSpin>
    </div>
  );
};
