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
  Table,
  Typography,
} from 'antd';
import {
  CreditCardOutlined,
  DollarOutlined,
  FallOutlined,
  RiseOutlined,
  ShoppingCartOutlined,
} from '@ant-design/icons';
import { Column, Pie } from '@ant-design/plots';
import { type Dayjs } from 'dayjs';
import { Link } from 'react-router-dom';
import { AdminDateRangeFilter } from '@drop/ui';
import { PageHeader } from '../../components/PageHeader';
import { PageContentSpinCenter } from '../../components/PageSpin';
import { useIsMobile } from '../../hooks/useIsMobile';
import {
  useFulfilAccountOptions,
  useFulfilCardOptions,
  useFulfilSpendingStats,
} from '../../hooks/useAdminFulfil';
import { useReceivingCardSources } from '../../hooks/useAdminReceivingBanks';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useFulfilUserOptions } from '../../hooks/useAdminUsers';
import { useAuthStore } from '../../stores/authStore';
import { useThemeStore } from '../../stores/themeStore';
import type { DashboardPeriod } from '../../types/dashboard';
import type { FulfilSpendingBreakdownItem } from '../../types/fulfil';
import { formatFulfilCardOptionLabel } from '../../types/fulfil';
import '@drop/dashboard/dashboard.css';
import { toUtcIsoFromTo } from '../../utils/datetime';
import {
  PERIOD_OPTIONS,
  RANGE_PRESETS,
  SELLER_PIE_TOP,
  chartEmptyStyle,
  chartHeight,
  chartTheme,
  clampDateRange,
  disabledFutureDate,
  formatDayLabel,
  matchPeriod,
  money,
  moneyCompact,
  rangeForPeriod,
  renderRankMedal,
  sharePieLegend,
  pieSliceLabelStyle,
  statsKpiTone,
  statsNegativeColor,
  statsPositiveColor,
} from './statsShared';

const { Text } = Typography;

const MOBILE_CHART_HEIGHT = 200;

type BreakdownDim = 'card' | 'account' | 'shop' | 'paidBy';

const DIM_OPTIONS: { label: string; value: BreakdownDim }[] = [
  { label: 'Thẻ', value: 'card' },
  { label: 'Tài khoản', value: 'account' },
  { label: 'Shop', value: 'shop' },
  { label: 'Người đi đơn', value: 'paidBy' },
];

/** Pie uses debit (chi tiêu thực) — credits are shown in KPI, not as pie slices. */
const buildPieData = (rows: FulfilSpendingBreakdownItem[]) => {
  const ranked = rows
    .map((r) => ({
      name: r.extra ? `${r.name} (${r.extra})` : r.name,
      amount: Number(r.debitAmount ?? (r.amount > 0 ? r.amount : 0)) || 0,
    }))
    .filter((r) => r.amount > 0)
    .sort((a, b) => b.amount - a.amount);

  if (ranked.length === 0) return [];
  if (ranked.length <= SELLER_PIE_TOP) return ranked;

  const top = ranked.slice(0, SELLER_PIE_TOP);
  const othersAmount = ranked.slice(SELLER_PIE_TOP).reduce((sum, r) => sum + r.amount, 0);
  return [...top, { name: 'Others', amount: othersAmount }];
};

/** Spending tone: positive = debit (red), negative = credit (green). */
const moneyTone = (value: number, debit: string, credit: string) => {
  if (value > 0) return debit;
  if (value < 0) return credit;
  return undefined;
};

export const CardSpendingStatsPage = () => {
  const isMobile = useIsMobile();
  /** Re-evaluate chart labels / KPI chips when theme toggles. */
  useThemeStore((s) => s.mode);
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canListUsers = hasPermission('admin.users.view');
  const kpiOrange = statsKpiTone('orange');
  const kpiRed = statsKpiTone('red');
  const kpiGreen = statsKpiTone('green');
  const kpiBlue = statsKpiTone('blue');
  const debitColor = statsNegativeColor();
  const creditColor = statsPositiveColor();
  const [range, setRange] = useState<[Dayjs, Dayjs]>(() => rangeForPeriod('month'));
  const [period, setPeriod] = useState<DashboardPeriod | 'custom'>('month');
  const [breakdownDim, setBreakdownDim] = useState<BreakdownDim>('card');

  const [filterAccountId, setFilterAccountId] = useState<string | undefined>();
  const [filterSourceId, setFilterSourceId] = useState<string | undefined>();
  const [filterCardId, setFilterCardId] = useState<string | undefined>();
  const [filterShopId, setFilterShopId] = useState<string | undefined>();
  const [filterPaidByUserId, setFilterPaidByUserId] = useState<string | undefined>();

  const query = useMemo(
    () => {
      const { from, to } = toUtcIsoFromTo(range[0], range[1]);
      return {
        accountId: filterAccountId,
        sourceId: filterSourceId,
        cardId: filterCardId,
        shopId: filterShopId,
        paidByUserId: filterPaidByUserId,
        from,
        to,
        breakdown: breakdownDim,
      };
    },
    [
      filterAccountId,
      filterSourceId,
      filterCardId,
      filterShopId,
      filterPaidByUserId,
      range,
      breakdownDim,
    ],
  );

  const statsQuery = useFulfilSpendingStats(query);
  const summary = statsQuery.data;

  const { data: accountOptions = [] } = useFulfilAccountOptions(true);
  const { data: sources = [] } = useReceivingCardSources();
  // Card filter independent of account (same as /fulfil/transactions) — one card ↔ many accounts.
  const { data: cardOptions = [] } = useFulfilCardOptions(undefined, true);
  const { data: shops = [] } = useAdminShopOptions();
  const { data: users = [] } = useFulfilUserOptions(canListUsers);

  const formatMoney = isMobile ? moneyCompact : money;
  const activeChartHeight = isMobile ? MOBILE_CHART_HEIGHT : chartHeight;
  const activeChartEmptyStyle = isMobile
    ? { ...chartEmptyStyle, minHeight: MOBILE_CHART_HEIGHT }
    : chartEmptyStyle;

  const totalAmount = Number(summary?.totalAmount ?? 0);
  const debitAmount = Number(summary?.debitAmount ?? 0);
  const creditAmount = Number(summary?.creditAmount ?? 0);
  const txnCount = summary?.transactionCount ?? 0;
  const orderCount = summary?.orderCount ?? 0;
  const cardCount = summary?.cardCount ?? 0;
  const accountCount = summary?.accountCount ?? 0;

  const breakdownRows: FulfilSpendingBreakdownItem[] = (() => {
    if (!summary) return [];
    if (breakdownDim === 'card') return summary.byCard ?? [];
    if (breakdownDim === 'account') return summary.byAccount ?? [];
    if (breakdownDim === 'shop') return summary.byShop ?? [];
    return summary.byPaidBy ?? [];
  })();

  const pieSource = buildPieData(breakdownRows);
  const pieTotal = pieSource.reduce((sum, r) => sum + r.amount, 0);
  const pieData = pieSource.map((r) => ({
    name: r.name,
    amount: r.amount,
    shareLabel: pieTotal > 0 ? `${((r.amount / pieTotal) * 100).toFixed(1)}%` : '0%',
    legendLabel: isMobile ? r.name : `${r.name} (${money(r.amount)})`,
  }));

  const trendData = (summary?.days ?? []).map((d) => ({
    date: formatDayLabel(d.date),
    amount: Number(d.amount) || 0,
  }));

  const accountFilterOptions = useMemo(
    () => accountOptions.map((a) => ({ value: a.id, label: a.name })),
    [accountOptions],
  );

  const sourceFilterOptions = useMemo(
    () =>
      sources
        .filter((s) => s.isActive)
        .map((s) => ({ value: s.id, label: s.name })),
    [sources],
  );

  const cardFilterOptions = useMemo(
    () =>
      cardOptions
        .filter((c) => !filterSourceId || c.sourceId === filterSourceId)
        .map((c) => ({
          value: c.id,
          label: formatFulfilCardOptionLabel(c, { preferredSuffix: false, compact: true }),
        })),
    [cardOptions, filterSourceId],
  );

  const shopOptions = useMemo(
    () => shops.map((s) => ({ value: s.id, label: s.name })),
    [shops],
  );

  const userOptions = useMemo(
    () => users.map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName })),
    [users],
  );

  const tableRows = breakdownRows.map((r, index) => ({
    key: r.id ?? `row-${index}`,
    rank: index + 1,
    name: r.name,
    extra: r.extra,
    amount: Number(r.amount) || 0,
    debitAmount: Number(r.debitAmount ?? 0) || 0,
    creditAmount: Number(r.creditAmount ?? 0) || 0,
    transactionCount: r.transactionCount,
    orderCount: r.orderCount ?? 0,
    share: debitAmount > 0 ? (Number(r.debitAmount ?? 0) || 0) / debitAmount : 0,
  }));

  const nameTitle =
    breakdownDim === 'card'
      ? 'Thẻ'
      : breakdownDim === 'account'
        ? 'Tài khoản'
        : breakdownDim === 'shop'
          ? 'Shop'
          : 'Người đi đơn';

  // Compact columns so the table fits beside the pie without horizontal scroll.
  const tableColumns = [
    {
      title: '#',
      dataIndex: 'rank',
      key: 'rank',
      width: 36,
      render: (_: number, __: (typeof tableRows)[0], index: number) => (
        <span className={renderRankMedal(index)}>{index + 1}</span>
      ),
    },
    {
      title: nameTitle,
      dataIndex: 'name',
      key: 'name',
      ellipsis: { showTitle: true },
      render: (name: string, row: (typeof tableRows)[0]) => {
        const tip = row.extra ? `${name} · ${row.extra}` : name;
        return (
          <span title={tip} style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            <span style={{ fontWeight: 500 }}>{name}</span>
            {row.extra ? (
              <Text type="secondary" style={{ marginLeft: 4, fontSize: 12 }}>
                {row.extra}
              </Text>
            ) : null}
          </span>
        );
      },
    },
    {
      title: 'GD',
      dataIndex: 'transactionCount',
      key: 'transactionCount',
      align: 'right' as const,
      width: 44,
      render: (v: number) => v.toLocaleString('en-US'),
    },
    {
      title: 'Đơn',
      dataIndex: 'orderCount',
      key: 'orderCount',
      align: 'right' as const,
      width: 44,
      render: (v: number) => v.toLocaleString('en-US'),
    },
    {
      title: 'Chi tiêu',
      dataIndex: 'amount',
      key: 'amount',
      align: 'right' as const,
      width: isMobile ? 72 : 88,
      render: (v: number, row: (typeof tableRows)[0]) => (
        <span
          title={`Trừ ${money(row.debitAmount)} · Nạp ${money(row.creditAmount)}`}
          style={{
            color: moneyTone(v, debitColor, creditColor),
            fontVariantNumeric: 'tabular-nums',
            whiteSpace: 'nowrap',
          }}
        >
          {moneyCompact(v)}
        </span>
      ),
    },
    {
      title: '%',
      dataIndex: 'share',
      key: 'share',
      align: 'right' as const,
      width: 48,
      render: (v: number) => `${(v * 100).toFixed(0)}%`,
    },
  ];

  const onPeriodChange = (value: string | number) => {
    const next = value as DashboardPeriod;
    setPeriod(next);
    setRange(rangeForPeriod(next));
  };

  const filterSelects = (
    <>
      <Select
        size="small"
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder="Nguồn thẻ"
        value={filterSourceId}
        options={sourceFilterOptions}
        onChange={(v) => {
          setFilterSourceId(v);
          if (v && filterCardId) {
            const stillValid = cardOptions.some((c) => c.id === filterCardId && c.sourceId === v);
            if (!stillValid) setFilterCardId(undefined);
          }
        }}
        style={{ width: isMobile ? '100%' : 130 }}
      />
      <Select
        size="small"
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder="Thẻ"
        value={filterCardId}
        options={cardFilterOptions}
        onChange={(v) => setFilterCardId(v)}
        style={{ width: isMobile ? '100%' : 160 }}
      />
      <Select
        size="small"
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder="Tài khoản"
        value={filterAccountId}
        options={accountFilterOptions}
        onChange={(v) => setFilterAccountId(v)}
        style={{ width: isMobile ? '100%' : 140 }}
      />
      <Select
        size="small"
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder="Shop"
        value={filterShopId}
        options={shopOptions}
        onChange={(v) => setFilterShopId(v)}
        style={{ width: isMobile ? '100%' : 140 }}
      />
      <Select
        size="small"
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder="Người đi đơn"
        value={filterPaidByUserId}
        options={userOptions}
        onChange={(v) => setFilterPaidByUserId(v)}
        style={{ width: isMobile ? '100%' : 140 }}
      />
    </>
  );

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
  ) => {
    if (loading) {
      return <PageContentSpinCenter minHeight={activeChartHeight} />;
    }
    if (error) {
      return (
        <Alert
          type="error"
          showIcon
          title={error instanceof Error ? error.message : 'Không tải được dữ liệu.'}
        />
      );
    }
    if (empty) {
      return (
        <div
          className="dashboard-chart-empty"
          style={{ ...activeChartEmptyStyle, display: 'grid', placeItems: 'center' }}
        >
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có giao dịch" />
        </div>
      );
    }
    return chart;
  };

  return (
    <div className="dashboard-page">
      <PageHeader
        title="Thẻ thanh toán"
        extra={
          isMobile ? undefined : (
            <Space wrap size={8}>
              {filterSelects}
              {periodControls}
            </Space>
          )
        }
      />

      {isMobile ? (
        <div className="dashboard-toolbar" style={{ marginBottom: 12 }}>
          <div className="dashboard-toolbar-row">{filterSelects}</div>
          <div className="dashboard-toolbar-row">{periodControls}</div>
        </div>
      ) : null}

      <Row gutter={[12, 12]} style={{ marginBottom: 12 }}>
        <Col xs={12} sm={6}>
          <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Chi tiêu ròng</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiOrange.iconBg, color: kpiOrange.iconColor }}
              >
                <DollarOutlined />
              </span>
            </div>
            <p
              className="dashboard-kpi-value"
              style={{ color: moneyTone(totalAmount, debitColor, creditColor) }}
            >
              {formatMoney(totalAmount)}
            </p>
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Trừ tiền</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiRed.iconBg, color: kpiRed.iconColor }}
              >
                <FallOutlined />
              </span>
            </div>
            <p className="dashboard-kpi-value" style={{ color: debitAmount > 0 ? debitColor : undefined }}>
              {formatMoney(debitAmount)}
            </p>
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Nạp tiền</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiGreen.iconBg, color: kpiGreen.iconColor }}
              >
                <RiseOutlined />
              </span>
            </div>
            <p
              className="dashboard-kpi-value"
              style={{ color: creditAmount > 0 ? creditColor : undefined }}
            >
              {formatMoney(creditAmount)}
            </p>
          </Card>
        </Col>
        <Col xs={12} sm={6}>
          <Card size="small" className={`dashboard-kpi-card is-primary${isMobile ? ' is-compact' : ''}`}>
            <div className="dashboard-kpi-head">
              <span className="dashboard-kpi-label">Đơn / GD</span>
              <span
                className="dashboard-kpi-icon"
                style={{ background: kpiBlue.iconBg, color: kpiBlue.iconColor }}
              >
                <ShoppingCartOutlined />
              </span>
            </div>
            <p className="dashboard-kpi-value">
              <Link to="/fulfil/transactions" style={{ color: 'inherit' }}>
                {orderCount.toLocaleString('en-US')}
                <Text type="secondary" style={{ fontSize: 12, fontWeight: 400, marginLeft: 6 }}>
                  / {txnCount.toLocaleString('en-US')}
                </Text>
              </Link>
            </p>
            <Text type="secondary" style={{ display: 'block', margin: '2px 0 0', fontSize: 11 }}>
              {cardCount} thẻ · {accountCount} TK
              <CreditCardOutlined style={{ marginLeft: 6 }} />
            </Text>
          </Card>
        </Col>
      </Row>

      <div className="dashboard-section-block">
        <Card size="small" className="dashboard-section-card" title="Chi tiêu ròng theo ngày">
          {renderChartBody(
            statsQuery.isLoading || statsQuery.isFetching,
            trendData.length === 0,
            statsQuery.isError ? statsQuery.error : null,
            <Column
              data={trendData}
              xField="date"
              yField="amount"
              height={activeChartHeight}
              axis={chartTheme.axis}
              tooltip={{
                items: [
                  {
                    channel: 'y',
                    name: 'Ròng',
                    valueFormatter: (v: number) => money(Number(v) || 0),
                  },
                ],
              }}
              style={{ maxWidth: 48 }}
            />,
          )}
        </Card>
      </div>

      <div className="dashboard-section-block">
        <Card
          size="small"
          className="dashboard-section-card"
          title="Phân bổ chi tiêu"
          extra={
            <Segmented
              size="small"
              value={breakdownDim}
              options={DIM_OPTIONS}
              onChange={(v) => setBreakdownDim(v as BreakdownDim)}
            />
          }
        >
          <Row gutter={[12, 12]}>
            <Col xs={24} lg={10}>
              {renderChartBody(
                statsQuery.isLoading || statsQuery.isFetching,
                pieData.length === 0,
                statsQuery.isError ? statsQuery.error : null,
                <Pie
                  data={pieData}
                  angleField="amount"
                  colorField="legendLabel"
                  radius={0.9}
                  innerRadius={0.55}
                  height={activeChartHeight}
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
                    items: [
                      {
                        channel: 'y',
                        name: 'Trừ tiền',
                        valueFormatter: (v: number) => money(Number(v) || 0),
                      },
                    ],
                  }}
                />,
              )}
              {!isMobile && pieData.length > 0 ? (
                <Text type="secondary" style={{ display: 'block', margin: '4px 0 0', fontSize: 11 }}>
                  Pie &amp; % theo trừ tiền. Hover số tiền để xem trừ/nạp.
                </Text>
              ) : null}
            </Col>
            <Col xs={24} lg={14}>
              <Table
                size="small"
                rowKey="key"
                loading={statsQuery.isLoading || statsQuery.isFetching}
                pagination={false}
                dataSource={tableRows}
                columns={tableColumns}
                tableLayout="fixed"
              />
            </Col>
          </Row>
        </Card>
      </div>

      {summary && (txnCount > 0 || debitAmount > 0 || creditAmount > 0) ? (
        <div className="dashboard-section-block">
          <Card size="small" className="dashboard-section-card" title="Theo nguồn">
            <Space size="large" wrap>
              <span>
                Cập nhật phí:{' '}
                <strong style={{ color: moneyTone(Number(summary.costUpdateAmount) || 0, debitColor, creditColor) }}>
                  {formatMoney(Number(summary.costUpdateAmount) || 0)}
                </strong>
              </span>
              <span>
                Thủ công:{' '}
                <strong style={{ color: moneyTone(Number(summary.manualAmount) || 0, debitColor, creditColor) }}>
                  {formatMoney(Number(summary.manualAmount) || 0)}
                </strong>
              </span>
              <span>
                Trừ:{' '}
                <strong style={{ color: debitColor }}>{formatMoney(debitAmount)}</strong>
              </span>
              <span>
                Nạp:{' '}
                <strong style={{ color: creditColor }}>{formatMoney(creditAmount)}</strong>
              </span>
            </Space>
          </Card>
        </div>
      ) : null}
    </div>
  );
};
