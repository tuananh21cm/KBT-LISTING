import { useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Card,
  Col,
  Empty,
  Popover,
  Row,
  Segmented,
  Table,
  Typography,
} from 'antd';
import {
  ShoppingCartOutlined,
  DollarOutlined,
  AccountBookOutlined,
  FundOutlined,
  BankOutlined,
  ShoppingOutlined,
  TagOutlined,
} from '@ant-design/icons';
import { Column } from '@ant-design/plots';
import { PageHeader } from '../../components/PageHeader';
import { PageContentSpin } from '../../components/PageSpin';
import {
  useDashboardOrderHeatmap,
  useDashboardOrderHourlyHeatmap,
  useDashboardSummary,
  useDashboardTrends,
} from '../../hooks/useDashboard';
import { useIsMobile } from '../../hooks/useIsMobile';
import { ORDER_STATUS_META } from '../stats/statsShared';
import {
  DASHBOARD_PERIOD_OPTIONS,
  DashboardKpiStrip,
  DashboardStatusItem,
  ListedTrendChart,
  OrderHeatmapGrid,
  OrderHourlyHeatmapGrid,
  OrdersTrendChart,
  RevenueTrendChart,
  buildOrdersTrendData,
  chartTheme,
  formatDayLabel,
  formatPeriodRange,
  money,
  moneyCompact,
  resolveDashboardParams,
  type DashboardPeriodFilter,
} from '@drop/dashboard';
import '@drop/dashboard/dashboard.css';
import type { DashboardOrderStatusCounts, DashboardTopProduct } from '../../types/dashboard';

const { Text } = Typography;

const PERIOD_COPY: Record<
  DashboardPeriodFilter,
  { sectionPeriod: string; chartOrders: string; chartRevenue: string; chartListed: string }
> = {
  day: { sectionPeriod: 'Today', chartOrders: 'Đơn hàng', chartRevenue: 'Doanh thu', chartListed: 'Listed' },
  week: { sectionPeriod: 'This week', chartOrders: 'Đơn hàng', chartRevenue: 'Doanh thu', chartListed: 'Listed' },
  thisMonth: {
    sectionPeriod: 'This month',
    chartOrders: 'Đơn hàng',
    chartRevenue: 'Doanh thu',
    chartListed: 'Listed',
  },
  last30Days: {
    sectionPeriod: 'Last 30 days',
    chartOrders: 'Đơn hàng',
    chartRevenue: 'Doanh thu',
    chartListed: 'Listed',
  },
};

export const DashboardPage = () => {
  const isMobile = useIsMobile();
  const [period, setPeriod] = useState<DashboardPeriodFilter>('last30Days');
  const [hourlyTz, setHourlyTz] = useState<'us' | 'vn'>('us');
  const copy = PERIOD_COPY[period];

  const queryParams = useMemo(() => resolveDashboardParams(period), [period]);

  const summaryQuery = useDashboardSummary(queryParams);
  // Trends are above-the-fold; fire with summary (no groupBy → aggregate days only).
  const trendsQuery = useDashboardTrends(queryParams);
  // Day heatmap (shop) below the fold.
  const heatmapQuery = useDashboardOrderHeatmap(queryParams, { enabled: summaryQuery.isFetched });
  const hourlyHeatmapQuery = useDashboardOrderHourlyHeatmap(
    { ...queryParams, timezone: hourlyTz },
    { enabled: summaryQuery.isFetched },
  );

  const summary = summaryQuery.data;
  const trendDays = trendsQuery.data?.days ?? [];
  const rangeLabel = formatPeriodRange(summary?.fromDate, summary?.toDate);
  const profit = Number(summary?.today.profit ?? 0);
  const cost = Number(summary?.today.cost ?? 0);
  const income = Number(summary?.today.income ?? 0);
  const profitMargin =
    summary?.today.profitMargin != null
      ? Number(summary.today.profitMargin)
      : cost > 0
        ? (profit / cost) * 100
        : null;
  const marginSub =
    profitMargin != null
      ? `${profitMargin.toLocaleString('en-US', {
          maximumFractionDigits: 1,
          minimumFractionDigits: 1,
          signDisplay: 'exceptZero',
        })}%`
      : null;

  const ordersColumnData = buildOrdersTrendData(trendDays, formatDayLabel);

  const revenueLineData = trendDays.map((d) => ({
    date: formatDayLabel(d.date),
    value: Number(d.revenue),
  }));

  const listedColumnData = trendDays.map((d) => ({
    date: formatDayLabel(d.date),
    listed: d.listed,
  }));

  const shopHeatmapCells = heatmapQuery.data?.shops ?? [];

  const topProducts = summary?.topProducts ?? [];
  const shopsFinance = summary?.shopsFinance ?? [];

  const renderTopProductCell = (_: unknown, row: DashboardTopProduct) => {
    const contextParts = [row.shopName, row.shopProfileName]
      .map((p) => p?.trim())
      .filter((p): p is string => Boolean(p));
    const contextLabel = contextParts.join(' • ');

    const thumb = (
      <Avatar
        shape="square"
        size={isMobile ? 32 : 36}
        src={row.imgUrl || undefined}
        icon={<ShoppingOutlined />}
        className="dashboard-product-avatar"
      />
    );

    return (
      <div className="dashboard-product-cell">
        {row.imgUrl ? (
          <Popover
            content={
              <img
                src={row.imgUrl}
                alt=""
                referrerPolicy="no-referrer"
                className="dashboard-product-preview"
              />
            }
            trigger="hover"
            placement="right"
            mouseEnterDelay={0.25}
            arrow={false}
            styles={{ container: { padding: 4 } }}
          >
            <span className="dashboard-product-avatar-wrap">{thumb}</span>
          </Popover>
        ) : (
          thumb
        )}
        <div className="dashboard-product-meta">
          <Text ellipsis={{ tooltip: row.title }} className="dashboard-product-title">
            {row.title}
          </Text>
          {row.sellerSku ? (
            <Text type="secondary" ellipsis={{ tooltip: row.sellerSku }} className="dashboard-product-sku">
              {row.sellerSku}
            </Text>
          ) : null}
          {contextLabel ? (
            <Text type="secondary" ellipsis={{ tooltip: contextLabel }} className="dashboard-product-context">
              {contextLabel}
            </Text>
          ) : null}
        </div>
      </div>
    );
  };

  const renderTopProductRank = (_: unknown, __: DashboardTopProduct, index: number) => {
    const rank = index + 1;
    const medalClass =
      rank === 1
        ? 'dashboard-rank-medal is-1'
        : rank === 2
          ? 'dashboard-rank-medal is-2'
          : rank === 3
            ? 'dashboard-rank-medal is-3'
            : 'dashboard-rank-medal';
    return <span className={medalClass}>{rank}</span>;
  };

  const topProductColumns = [
    { title: '#', key: 'rank', width: 48, render: renderTopProductRank },
    { title: 'Sản phẩm', key: 'product', ellipsis: true, render: renderTopProductCell },
    { title: 'Đã bán', dataIndex: 'quantitySold', key: 'quantitySold', width: 80, align: 'right' as const },
    { title: 'Đơn', dataIndex: 'orderCount', key: 'orderCount', width: 64, align: 'right' as const },
    {
      title: 'Doanh thu',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 110,
      align: 'right' as const,
      render: (v: number) => money(Number(v)),
    },
  ];

  const topProductColumnsMobile = [
    { title: '#', key: 'rank', width: 32, render: renderTopProductRank },
    { title: 'Sản phẩm', key: 'product', ellipsis: true, render: renderTopProductCell },
    { title: 'Đơn', dataIndex: 'orderCount', key: 'orderCount', width: 40, align: 'right' as const },
    {
      title: 'Rev',
      dataIndex: 'revenue',
      key: 'revenue',
      width: 64,
      align: 'right' as const,
      render: (v: number) => moneyCompact(Number(v)),
    },
  ];

  const chartEmptyStyle: CSSProperties = { minHeight: 220 };
  const chartHeight = 220;

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
      return `${base} (${n})`;
    });
  })();

  const financeColumnRows = shopsFinance.map((s, i) => ({
    entity: shopFinanceLabels[i] ?? s.shopName,
    revenue: Number(s.revenue) || 0,
    profit: Number(s.profit) || 0,
    orders: s.orders,
  }));

  const financeColumnData = financeColumnRows.flatMap((s) => [
    { entity: s.entity, metric: 'Doanh thu', value: s.revenue, orders: s.orders },
    { entity: s.entity, metric: 'Lợi nhuận', value: s.profit, orders: s.orders },
  ]);

  const renderChartBody = (loading: boolean, empty: boolean, error: unknown, chart: ReactNode) => (
    <PageContentSpin spinning={loading}>
      {error ? (
        <Alert type="error" showIcon title={error instanceof Error ? error.message : 'Lỗi tải biểu đồ'} />
      ) : empty ? (
        <div className="dashboard-chart-empty" style={chartEmptyStyle}>
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có dữ liệu" />
        </div>
      ) : (
        chart
      )}
    </PageContentSpin>
  );

  return (
    <div className="dashboard-page">
      <PageHeader
        title={
          <span style={{ display: 'inline-flex', alignItems: 'baseline', gap: 8, minWidth: 0 }}>
            <span>Dashboard</span>
            {summary ? (
              <Text type="secondary" style={{ fontSize: 12, fontWeight: 400 }}>
                {copy.sectionPeriod}
                {rangeLabel ? ` · ${rangeLabel}` : null}
                {` · ${summary.timezone}`}
              </Text>
            ) : null}
          </span>
        }
        extra={
          <Segmented
            size="small"
            value={period}
            options={DASHBOARD_PERIOD_OPTIONS}
            onChange={(value) => setPeriod(value as DashboardPeriodFilter)}
          />
        }
      />

      {summaryQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={summaryQuery.error instanceof Error ? summaryQuery.error.message : 'Lỗi tải dashboard'}
        />
      ) : null}

      <PageContentSpin spinning={summaryQuery.isLoading || summaryQuery.isFetching}>
        {/* 1. Period finance */}
        <div>
          <DashboardKpiStrip
            items={[
              {
                key: 'orders',
                label: 'Orders',
                value: summary?.today.orderCount ?? 0,
                icon: <ShoppingCartOutlined />,
                tone: 'blue',
                to: '/orders',
              },
              {
                key: 'revenue',
                label: 'Revenue',
                value: money(Number(summary?.today.revenue ?? 0)),
                icon: <DollarOutlined />,
                tone: 'green',
              },
              {
                key: 'cost',
                label: 'Cost',
                value: money(Number(summary?.today.cost ?? 0)),
                icon: <AccountBookOutlined />,
                tone: 'orange',
              },
              {
                key: 'coupon',
                label: 'Coupon',
                value: money(Number(summary?.today.coupon ?? 0)),
                icon: <TagOutlined />,
                tone: 'purple',
              },
              {
                key: 'profit',
                label: 'Profit',
                value: money(profit),
                sub:
                  marginSub != null ? (
                    <span
                      className={
                        profitMargin != null && profitMargin < 0
                          ? 'is-negative'
                          : profitMargin != null && profitMargin > 0
                            ? 'is-positive'
                            : undefined
                      }
                    >
                      {marginSub}
                    </span>
                  ) : null,
                icon: <FundOutlined />,
                tone: profit >= 0 ? 'green' : 'red',
              },
              {
                key: 'income',
                label: 'Income',
                value: money(income),
                icon: <BankOutlined />,
                tone: income >= 0 ? 'cyan' : 'red',
                to: '/receiving-banks/transactions',
              },
            ]}
          />
        </div>

        {/* 2. Catalog / backlog counts */}
        <div style={{ marginTop: 14 }}>
          <p className="dashboard-section-label">Tồn kho & trạng thái</p>
          <Row gutter={[10, 10]} align="stretch">
            <Col xs={24} md={12} lg={12}>
              <Card size="small" className="dashboard-section-card dashboard-status-card" title="Trạng thái đơn">
                <div className="dashboard-status-grid is-4">
                  {ORDER_STATUS_META.filter(
                    (meta) =>
                      meta.key === 'all' ||
                      meta.key === 'canceled' ||
                      meta.key === 'refund',
                  ).map((meta) => {
                    const counts = summary?.orderStatusCounts;
                    const value = Number(counts?.[meta.key as keyof DashboardOrderStatusCounts] ?? 0);
                    const to = meta.platformOrderStatus
                      ? `/orders?platformOrderStatus=${meta.platformOrderStatus}`
                      : '/orders';
                    return (
                      <DashboardStatusItem key={meta.key} label={meta.label} value={value} tone={meta.tone} to={to} />
                    );
                  })}
                  <DashboardStatusItem
                    label="Suspended"
                    value={Number(summary?.orderStatusCounts?.suspended ?? 0)}
                    tone="danger"
                    to="/shops"
                  />
                </div>
              </Card>
            </Col>
            <Col xs={24} md={12} lg={8}>
              <Card size="small" className="dashboard-section-card dashboard-status-card" title="Listings">
                <div className="dashboard-status-grid is-4">
                  <DashboardStatusItem label="Draft" value={summary?.listings.draft ?? 0} tone="warn" to="/listings" />
                  <DashboardStatusItem
                    label="Published"
                    value={summary?.listings.published ?? 0}
                    tone="success"
                    to="/listings"
                  />
                  <DashboardStatusItem label="Active" value={summary?.listings.active ?? 0} tone="success" />
                  <DashboardStatusItem label="Inactive" value={summary?.listings.inactive ?? 0} />
                </div>
              </Card>
            </Col>
            <Col xs={24} md={24} lg={4}>
              <Card size="small" className="dashboard-section-card dashboard-status-card" title="Shops">
                <div className="dashboard-status-grid is-2">
                  <DashboardStatusItem label="Active" value={summary?.shops.active ?? 0} tone="success" />
                  <DashboardStatusItem label="Suspended" value={summary?.shops.suspended ?? 0} tone="danger" />
                </div>
              </Card>
            </Col>
          </Row>
        </div>

        {/* 3. Trends — 3 equal charts */}
        <div style={{ marginTop: 14 }}>
          <p className="dashboard-section-label">Xu hướng</p>
          <Row gutter={[10, 10]}>
            <Col xs={24} lg={8}>
              <Card size="small" className="dashboard-section-card" title={copy.chartOrders}>
                {renderChartBody(
                  trendsQuery.isLoading || trendsQuery.isFetching,
                  ordersColumnData.length === 0,
                  trendsQuery.isError ? trendsQuery.error : null,
                  <OrdersTrendChart data={ordersColumnData} height={chartHeight} />,
                )}
              </Card>
            </Col>
            <Col xs={24} lg={8}>
              <Card size="small" className="dashboard-section-card" title={copy.chartRevenue}>
                {renderChartBody(
                  trendsQuery.isLoading || trendsQuery.isFetching,
                  revenueLineData.length === 0,
                  null,
                  <RevenueTrendChart data={revenueLineData} height={chartHeight} />,
                )}
              </Card>
            </Col>
            <Col xs={24} lg={8}>
              <Card size="small" className="dashboard-section-card" title={copy.chartListed}>
                {renderChartBody(
                  trendsQuery.isLoading || trendsQuery.isFetching,
                  listedColumnData.length === 0,
                  null,
                  <ListedTrendChart data={listedColumnData} height={chartHeight} />,
                )}
              </Card>
            </Col>
          </Row>
        </div>
      </PageContentSpin>

      {/* 4. Heatmaps — mỗi heatmap một hàng full-width (ô ngày/giờ không bị bóp khi 2 cột) */}
      <div>
        <p className="dashboard-section-label">Heatmap đơn hàng</p>
        <Row gutter={[10, 10]}>
          <Col span={24}>
            <Card size="small" className="dashboard-section-card is-heatmap" title="Orders theo ngày · Shop">
              <PageContentSpin spinning={heatmapQuery.isLoading || heatmapQuery.isFetching}>
                {heatmapQuery.isError ? (
                  <Alert
                    type="error"
                    showIcon
                    title={heatmapQuery.error instanceof Error ? heatmapQuery.error.message : 'Lỗi tải heatmap'}
                  />
                ) : (
                  <OrderHeatmapGrid cells={shopHeatmapCells} sortByProfileName />
                )}
              </PageContentSpin>
            </Card>
          </Col>
          <Col span={24}>
            <Card
              size="small"
              className="dashboard-section-card is-heatmap"
              title="Orders theo giờ"
              extra={
                <Segmented
                  size="small"
                  value={hourlyTz}
                  options={[
                    { label: 'US', value: 'us' },
                    { label: 'VN', value: 'vn' },
                  ]}
                  onChange={(value) => setHourlyTz(value as 'us' | 'vn')}
                />
              }
            >
              <PageContentSpin spinning={hourlyHeatmapQuery.isLoading || hourlyHeatmapQuery.isFetching}>
                {hourlyHeatmapQuery.isError ? (
                  <Alert
                    type="error"
                    showIcon
                    title={
                      hourlyHeatmapQuery.error instanceof Error
                        ? hourlyHeatmapQuery.error.message
                        : 'Lỗi tải heatmap theo giờ'
                    }
                  />
                ) : (
                  <OrderHourlyHeatmapGrid totals={hourlyHeatmapQuery.data?.totals ?? []} />
                )}
              </PageContentSpin>
            </Card>
          </Col>
        </Row>
      </div>

      {/* 5. Revenue / Profit by shop */}
      <div>
        <p className="dashboard-section-label">Doanh thu & lợi nhuận theo shop</p>
        <Card size="small" className="dashboard-section-card" title="Doanh thu & lợi nhuận theo shop">
          {renderChartBody(
            summaryQuery.isLoading || summaryQuery.isFetching,
            financeColumnData.length === 0,
            summaryQuery.error,
            <Column
              height={320}
              data={financeColumnData}
              xField="entity"
              yField="value"
              colorField="metric"
              group
              label={false}
              marginBottom={28}
              scale={{ color: { range: ['#52c41a', '#1677ff'] } }}
              style={{ maxWidth: 22, radiusTopLeft: 4, radiusTopRight: 4 }}
              axis={{
                ...chartTheme.axis,
                x: {
                  ...chartTheme.axis.x,
                  title: false,
                  labelAutoRotate: false,
                  labelAutoEllipsis: true,
                  labelAutoHide: true,
                },
                y: {
                  ...chartTheme.axis.y,
                  label: false,
                  tick: false,
                  gridStrokeOpacity: 1,
                },
              }}
              legend={{
                color: {
                  position: 'top',
                  layout: { justifyContent: 'flex-end' },
                  ...chartTheme.legendColor,
                },
              }}
              tooltip={{
                shared: true,
                title: (d: { entity?: string }) => d?.entity ?? '',
                items: [
                  (d: { metric?: string; value?: number }) => ({
                    name: d.metric ?? '',
                    value: money(Number(d.value) || 0),
                  }),
                ],
              }}
              interaction={{
                elementHighlight: { background: true },
                tooltip: { shared: true },
              }}
            />,
          )}
        </Card>
      </div>

      {/* 6. Top products */}
      <div>
        <p className="dashboard-section-label">Sản phẩm bán chạy</p>
        <Card size="small" className="dashboard-section-card" title="Top 10 trong kỳ">
          <Table<DashboardTopProduct>
            size="small"
            rowKey="productKey"
            loading={summaryQuery.isLoading || summaryQuery.isFetching}
            columns={isMobile ? topProductColumnsMobile : topProductColumns}
            dataSource={topProducts}
            pagination={false}
            className={isMobile ? 'dashboard-seller-table-mobile' : undefined}
            locale={{
              emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có sản phẩm bán trong kỳ" />,
            }}
          />
        </Card>
      </div>
    </div>
  );
};
