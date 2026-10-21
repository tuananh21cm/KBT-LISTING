import { useState, type ReactNode } from 'react';
import {
  Alert,
  Card,
  Col,
  Empty,
  Row,
  Space,
  Table,
} from 'antd';
import {
  ShoppingCartOutlined,
  DollarOutlined,
  FundOutlined,
  PercentageOutlined,
} from '@ant-design/icons';
import { Column, Pie } from '@ant-design/plots';
import { type Dayjs } from 'dayjs';
import { Link } from 'react-router-dom';
import {
  chartTheme,
  formatDayLabel,
  money,
  moneyCompact,
  pieSliceLabelStyle,
  sharePieLegend,
  DashboardStatusItem,
} from '@drop/dashboard';
import { PageHeader, PageContentSpin, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import { toVnCalendarDate } from '@drop/utils/datetime';
import type { DashboardOrderStatusCounts, DashboardPeriod } from '@drop/types/dashboard';
import '@drop/dashboard/dashboard.css';
import { useOrdersStatsDeps } from './OrdersStatsDepsContext';
import { ORDER_STATUS_META, type OrderStatusKey } from './orderStatusMeta';
import type { OrdersStatsPortalConfig } from './portalConfig';
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
} from './statsPeriod';

const MOBILE_CHART_HEIGHT = 200;

const buildOrderPieData = (rows: Array<{ name: string; orders: number }>) => {
  const ranked = rows
    .map((r) => ({ name: r.name, orders: Number(r.orders) || 0 }))
    .filter((r) => r.orders > 0)
    .sort((a, b) => b.orders - a.orders);

  if (ranked.length === 0) return [];
  if (ranked.length <= SELLER_PIE_TOP) return ranked;

  const top = ranked.slice(0, SELLER_PIE_TOP);
  const othersOrders = ranked.slice(SELLER_PIE_TOP).reduce((sum, r) => sum + r.orders, 0);
  return [...top, { name: 'Others', orders: othersOrders }];
};

const statusValue = (counts: DashboardOrderStatusCounts | undefined, key: OrderStatusKey) => {
  if (!counts) return 0;
  return Number(counts[key] ?? 0);
};

export type SharedOrdersStatsPageProps = {
  config: OrdersStatsPortalConfig;
};

export const OrdersStatsPage = ({ config }: SharedOrdersStatsPageProps) => {
  const { useDashboardSummary, useDashboardTrends, useThemeMode } = useOrdersStatsDeps();
  const isMobile = useIsMobile();
  /** Re-evaluate chart labels / KPI chips when theme toggles. */
  useThemeMode();
  const [range, setRange] = useState<[Dayjs, Dayjs]>(() => rangeForPeriod('month'));
  const [period, setPeriod] = useState<DashboardPeriod | 'custom'>('month');
  const kpiBlue = statsKpiTone('blue');
  const kpiGreen = statsKpiTone('green');
  const kpiRed = statsKpiTone('red');
  const negativeColor = statsNegativeColor();

  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);

  const include = config.showSellerSharePie
    ? 'kpis,orderStatus,sellers,shopsFinance'
    : 'kpis,orderStatus,shopsFinance';

  const queryParams = {
    fromDate,
    toDate,
    include,
    metrics: 'orders' as const,
  };

  const summaryQuery = useDashboardSummary(queryParams);
  const trendsQuery = useDashboardTrends({
    ...queryParams,
    grain: 'day',
    includeListed: false,
  });

  const summary = summaryQuery.data;
  const trendDays = trendsQuery.data?.days ?? [];
  const sellersToday = summary?.sellersToday ?? [];
  const shopsFinance = summary?.shopsFinance ?? [];
  const revenue = Number(summary?.today.revenue ?? 0);
  const profit = Number(summary?.today.profit ?? 0);
  const cost = Number(summary?.today.cost ?? 0);
  const profitMargin =
    summary?.today.profitMargin != null
      ? Number(summary.today.profitMargin)
      : cost > 0
        ? (profit / cost) * 100
        : null;

  const formatMoney = isMobile ? moneyCompact : money;
  const activeChartHeight = isMobile ? MOBILE_CHART_HEIGHT : chartHeight;
  const activeChartEmptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  const ordersTrendData = trendDays.map((d) => ({
    date: formatDayLabel(d.date),
    value: d.orders,
  }));

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

  const sellerOrderSource = sellersToday.map((s) => ({
    name: (s.displayName || s.userId).trim() || s.userId,
    orders: s.orders,
  }));
  const shopOrderSource = shopsFinance.map((s, i) => ({
    name: shopFinanceLabels[i] ?? s.shopName,
    orders: s.orders,
  }));

  const sellerOrdersTotal = sellerOrderSource.reduce((sum, r) => sum + (Number(r.orders) || 0), 0);
  const shopOrdersTotal = shopOrderSource.reduce((sum, r) => sum + (Number(r.orders) || 0), 0);

  const toOrderPieData = (
    source: Array<{ name: string; orders: number }>,
    total: number,
  ) =>
    buildOrderPieData(source).map((r) => {
      const pct = total > 0 ? (r.orders / total) * 100 : 0;
      return {
        name: r.name,
        orders: r.orders,
        percent: pct,
        shareLabel: `${pct.toFixed(1)}%`,
        legendLabel: isMobile
          ? `${r.name} (${pct.toFixed(0)}%)`
          : `${r.name} · ${r.orders.toLocaleString('en-US')} (${pct.toFixed(1)}%)`,
      };
    });

  const sellerOrderPieData = toOrderPieData(sellerOrderSource, sellerOrdersTotal);
  const shopOrderPieData = toOrderPieData(shopOrderSource, shopOrdersTotal);

  const statusCounts = summary?.orderStatusCounts;
  const pieTotal = ORDER_STATUS_META.filter((m) => m.inPie).reduce(
    (sum, m) => sum + statusValue(statusCounts, m.key),
    0,
  );

  const statusPieData = ORDER_STATUS_META.filter((m) => m.inPie)
    .map((m) => {
      const count = statusValue(statusCounts, m.key);
      return {
        status: m.label,
        count,
        shareLabel: pieTotal > 0 ? `${((count / pieTotal) * 100).toFixed(1)}%` : '0%',
      };
    })
    .filter((d) => d.count > 0);

  const statusTableRows = ORDER_STATUS_META.map((m) => {
    const count = statusValue(statusCounts, m.key);
    const base = pieTotal > 0 && m.inPie ? count / pieTotal : m.key === 'all' ? 1 : 0;
    return {
      key: m.key,
      label: m.label,
      count,
      share: m.key === 'all' ? null : base,
      platformOrderStatus: m.platformOrderStatus,
      tone: m.tone,
      inPie: m.inPie,
    };
  });

  const statusColumns = [
    {
      title: 'Trạng thái',
      dataIndex: 'label',
      key: 'label',
      render: (label: string, row: (typeof statusTableRows)[0]) =>
        row.platformOrderStatus ? (
          <Link to={`/orders?platformOrderStatus=${row.platformOrderStatus}`}>{label}</Link>
        ) : (
          <Link to="/orders">{label}</Link>
        ),
    },
    {
      title: isMobile ? 'Đơn' : 'Số đơn',
      dataIndex: 'count',
      key: 'count',
      align: 'right' as const,
      width: isMobile ? 64 : 100,
      render: (v: number) => v.toLocaleString('en-US'),
    },
    {
      title: isMobile ? '%' : 'Tỷ trọng',
      dataIndex: 'share',
      key: 'share',
      align: 'right' as const,
      width: isMobile ? 56 : 100,
      render: (v: number | null) => (v == null ? '—' : `${(v * 100).toFixed(1)}%`),
    },
  ];

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

  const renderOrderSharePie = (
    data: Array<{
      name: string;
      orders: number;
      percent: number;
      shareLabel: string;
      legendLabel: string;
    }>,
  ) => (
    <Pie
      height={activeChartHeight}
      data={data}
      angleField="orders"
      colorField="legendLabel"
      radius={0.9}
      innerRadius={0.55}
      legend={sharePieLegend(isMobile)}
      label={
        isMobile
          ? false
          : {
              text: 'shareLabel',
              style: pieSliceLabelStyle('md'),
            }
      }
      tooltip={{
        title: 'name',
        items: [
          {
            field: 'orders',
            name: 'Đơn hàng',
            valueFormatter: (v: number) => Number(v).toLocaleString('en-US'),
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

  return (
    <div className="dashboard-page">
      <PageHeader
        title="Đơn hàng"
        extra={
          isMobile ? undefined : (
            <Space wrap size={8} style={{ justifyContent: 'flex-end' }}>
              {periodControls}
            </Space>
          )
        }
      />

      {isMobile ? <div className="dashboard-toolbar">{periodControls}</div> : null}

      {summaryQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={
            summaryQuery.error instanceof Error
              ? summaryQuery.error.message
              : 'Lỗi tải thống kê đơn hàng'
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
              <p className="dashboard-kpi-value">
                <Link to="/orders" style={{ color: 'inherit' }}>
                  {(summary?.today.orderCount ?? 0).toLocaleString('en-US')}
                </Link>
              </p>
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
          <Col xs={12} sm={6}>
            <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
              <div className="dashboard-kpi-head">
                <span className="dashboard-kpi-label">Margin</span>
                <span
                  className="dashboard-kpi-icon"
                  style={{ background: kpiBlue.iconBg, color: kpiBlue.iconColor }}
                >
                  <PercentageOutlined />
                </span>
              </div>
              <p
                className="dashboard-kpi-value"
                style={{
                  color:
                    profitMargin != null && profitMargin < 0
                      ? negativeColor
                      : undefined,
                }}
              >
                {profitMargin != null
                  ? `${profitMargin.toLocaleString('en-US', {
                      maximumFractionDigits: 1,
                      minimumFractionDigits: 1,
                    })}%`
                  : '—'}
              </p>
            </Card>
          </Col>
        </Row>

        <div className="dashboard-section-block">
          <Card
            size="small"
            className="dashboard-section-card dashboard-status-card"
            title="Trạng thái đơn"
          >
            <div className="dashboard-status-grid is-status-workflow">
              {ORDER_STATUS_META.map((meta) => {
                const to = meta.platformOrderStatus
                  ? `/orders?platformOrderStatus=${meta.platformOrderStatus}`
                  : '/orders';
                return (
                  <DashboardStatusItem
                    key={meta.key}
                    label={meta.label}
                    value={statusValue(statusCounts, meta.key)}
                    tone={meta.tone}
                    to={to}
                  />
                );
              })}
            </div>
          </Card>
        </div>

        <div className="dashboard-section-block">
          <Card size="small" className="dashboard-section-card" title="Orders">
            {renderChartBody(
              trendsQuery.isLoading || trendsQuery.isFetching,
              ordersTrendData.length === 0,
              trendsQuery.isError ? trendsQuery.error : null,
              <Column
                height={activeChartHeight}
                data={ordersTrendData}
                xField="date"
                yField="value"
                style={{ maxWidth: isMobile ? 18 : 28, fill: '#1677ff' }}
                axis={{
                  ...(isMobile ? chartTheme.axisSm : chartTheme.axisMd),
                  x: {
                    ...(isMobile ? chartTheme.axisSm.x : chartTheme.axisMd.x),
                    labelAutoRotate: isMobile,
                    labelAutoHide: isMobile,
                  },
                }}
              />,
            )}
          </Card>
        </div>

        <div className="dashboard-section-block">
          <p className="dashboard-section-label">Tỷ trọng đơn hàng</p>
          <Row gutter={[8, 8]}>
            {config.showSellerSharePie ? (
              <Col xs={24} lg={12}>
                <Card size="small" className="dashboard-section-card" title="Seller">
                  {renderChartBody(
                    summaryQuery.isLoading,
                    sellerOrderPieData.length === 0,
                    null,
                    renderOrderSharePie(sellerOrderPieData),
                  )}
                </Card>
              </Col>
            ) : null}
            <Col xs={24} lg={config.showSellerSharePie ? 12 : 24}>
              <Card size="small" className="dashboard-section-card" title="Shop">
                {renderChartBody(
                  summaryQuery.isLoading,
                  shopOrderPieData.length === 0,
                  null,
                  renderOrderSharePie(shopOrderPieData),
                )}
              </Card>
            </Col>
          </Row>
        </div>

        <div className="dashboard-section-block">
          <p className="dashboard-section-label">Đơn hàng theo trạng thái</p>
          <Alert
            type="info"
            showIcon={!isMobile}
            className="dashboard-status-alert"
            title={
              isMobile
                ? 'Trạng thái = backlog hiện tại (không theo kỳ đã chọn).'
                : 'Số liệu trạng thái là tồn đọng hiện tại (toàn bộ backlog), không giới hạn theo khoảng thời gian đã chọn.'
            }
          />
          <Row gutter={[8, 8]}>
            <Col xs={24} lg={10}>
              <Card size="small" className="dashboard-section-card" title="Phân bố trạng thái">
                {renderChartBody(
                  summaryQuery.isLoading,
                  statusPieData.length === 0,
                  null,
                  <Pie
                    height={activeChartHeight}
                    data={statusPieData}
                    angleField="count"
                    colorField="status"
                    legend={{
                      color: {
                        title: false,
                        position: 'bottom',
                        itemMarker: 'circle',
                        itemLabelFill: chartTheme.axisMd.x.labelFill,
                        itemLabelFontSize: chartTheme.legendColorMd.itemLabelFontSize,
                        itemLabelFontFamily: chartTheme.legendColorMd.itemLabelFontFamily,
                        layout: isMobile
                          ? { justifyContent: 'center', flexWrap: true }
                          : undefined,
                      },
                    }}
                    label={
                      isMobile
                        ? false
                        : {
                            text: 'shareLabel',
                            style: pieSliceLabelStyle('md'),
                          }
                    }
                    tooltip={{
                      title: 'status',
                      items: [{ field: 'count', name: 'Đơn' }],
                    }}
                    innerRadius={0.55}
                  />,
                )}
              </Card>
            </Col>
            <Col xs={24} lg={14}>
              <Card size="small" className="dashboard-section-card" title="Chi tiết trạng thái">
                {isMobile ? (
                  <div className="dashboard-status-grid is-2">
                    {statusTableRows.map((row) => {
                      const href = row.platformOrderStatus
                        ? `/orders?platformOrderStatus=${row.platformOrderStatus}`
                        : '/orders';
                      const valueClass =
                        row.tone === 'warn'
                          ? 'dashboard-status-value is-warn'
                          : row.tone === 'danger'
                            ? 'dashboard-status-value is-danger'
                            : row.tone === 'success'
                              ? 'dashboard-status-value is-success'
                              : 'dashboard-status-value';
                      return (
                        <Link key={row.key} to={href} className="dashboard-status-item is-link">
                          <span className="dashboard-status-label">
                            {row.label}
                            {row.share != null ? ` · ${(row.share * 100).toFixed(0)}%` : null}
                          </span>
                          <span className={valueClass}>{row.count.toLocaleString('en-US')}</span>
                        </Link>
                      );
                    })}
                  </div>
                ) : (
                  <Table
                    size="small"
                    pagination={false}
                    rowKey="key"
                    dataSource={statusTableRows}
                    columns={statusColumns}
                  />
                )}
              </Card>
            </Col>
          </Row>
        </div>
      </PageContentSpin>
    </div>
  );
};
