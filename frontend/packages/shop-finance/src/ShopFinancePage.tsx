import { useMemo, useState, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Input,
  Segmented,
  Select,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import {
  BankOutlined,
  CarOutlined,
  ClockCircleOutlined,
  DollarOutlined,
  PauseCircleOutlined,
  ReloadOutlined,
  SafetyOutlined,
  ShopOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import { Link } from 'react-router-dom';
import { DashboardKpiStrip, money, moneyCompact, type DashboardKpiItem } from '@drop/dashboard';
import '@drop/dashboard/dashboard.css';
import { PageContentSpin, PageHeader, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import {
  DATE_FORMAT,
  disabledFutureVnDate,
  formatDateTimeVn,
  nowVn,
  rangeForPresetVn,
  toUtcIsoFromTo,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { ShopFinanceItem, ShopFinanceQuery, ShopFinanceSummary } from '@drop/types/shopFinance';
import { SHOP_STATUSES, SHOP_STATUS_LABELS } from '@drop/types/shop';
import { useShopFinanceDeps } from './ShopFinanceDepsContext';
import type { ShopFinancePortalConfig } from './portalConfig';
import { ShopFinanceTxnDrawer } from './ShopFinanceTxnDrawer';
import './shopFinance.css';

const { Text } = Typography;

type PeriodKey = 'all' | 'day' | 'week' | 'month' | 'custom';

const PERIOD_OPTIONS: { label: string; value: Exclude<PeriodKey, 'custom'> }[] = [
  { label: 'Tất cả', value: 'all' },
  { label: 'Ngày', value: 'day' },
  { label: 'Tuần', value: 'week' },
  { label: 'Tháng', value: 'month' },
];

const STATUS_OPTIONS = [
  ...SHOP_STATUSES.map((value) => ({ value, label: SHOP_STATUS_LABELS[value] ?? value })),
  { value: 'all', label: 'Tất cả trạng thái' },
];

const EMPTY_SUMMARY: ShopFinanceSummary = {
  shopCount: 0,
  shopsWithoutBankCount: 0,
  onHoldAmount: 0,
  onHoldInTransitAmount: 0,
  onHoldAwaitingSettlementAmount: 0,
  onHoldReserveAmount: 0,
  moneyReceived: 0,
  pendingConfirmationAmount: 0,
  acquisitionCostTotal: 0,
};

const rangeForPeriod = (period: 'day' | 'week' | 'month'): [Dayjs, Dayjs] => {
  const [start, end] = rangeForPresetVn(period);
  return [start.startOf('day'), end.startOf('day')];
};

const matchPeriod = (range: [Dayjs, Dayjs] | null): PeriodKey => {
  if (!range) return 'all';
  for (const period of ['day', 'week', 'month'] as const) {
    const [a, b] = rangeForPeriod(period);
    if (range[0].isSame(a, 'day') && range[1].isSame(b, 'day')) return period;
  }
  return 'custom';
};

const RANGE_PRESETS: { label: string; value: [Dayjs, Dayjs] }[] = [
  { label: '7 ngày', value: [nowVn().subtract(6, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '30 ngày', value: [nowVn().subtract(29, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: '90 ngày', value: [nowVn().subtract(89, 'day').startOf('day'), nowVn().startOf('day')] },
  { label: 'Năm nay', value: [nowVn().startOf('year'), nowVn().startOf('day')] },
];

const formatUsd = (value?: number | null, compact = false) => {
  const n = Number(value);
  if (!Number.isFinite(n)) return '—';
  return compact ? moneyCompact(n) : money(n);
};

const MoneyCell = ({ value, compact }: { value?: number | null; compact?: boolean }) => {
  const n = Number(value) || 0;
  return (
    <span className={`shop-finance-money${n === 0 ? ' is-zero' : ''}`}>
      {formatUsd(n, compact)}
    </span>
  );
};

const shopSubline = (record: ShopFinanceItem, showOwner: boolean) => {
  const owner = showOwner ? record.ownerFullName || record.ownerUserName : null;
  return [record.profileName, owner].filter(Boolean).join(' · ');
};

/** Keep the end of a long shop name; CSS ellipsizes the head so DELI stays on one line. */
const SHOP_NAME_TAIL = 14;

const ShopNameText = ({ name }: { name: string }) => {
  const text = name.trim() || name;
  if (text.length <= SHOP_NAME_TAIL + 8) return text;
  return (
    <>
      <span className="shop-finance-name-head">{text.slice(0, -SHOP_NAME_TAIL)}</span>
      <span className="shop-finance-name-tail">{text.slice(-SHOP_NAME_TAIL)}</span>
    </>
  );
};

const MetricChip = ({
  label,
  value,
  tone,
  onClick,
}: {
  label: string;
  value?: number | null;
  tone?: 'received' | 'hold';
  onClick?: (e: { preventDefault: () => void; stopPropagation: () => void }) => void;
}) => (
  <span
    className={`shop-finance-card-metric${tone ? ` is-${tone}` : ''}${onClick ? ' is-clickable' : ''}`}
    role={onClick ? 'button' : undefined}
    tabIndex={onClick ? 0 : undefined}
    onClick={onClick}
    onKeyDown={
      onClick
        ? (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              onClick(e);
            }
          }
        : undefined
    }
  >
    <span className="shop-finance-card-metric-label">{label}</span>
    <MoneyCell value={value} compact />
  </span>
);

const deliBadge = (deli?: number | null) => {
  if (deli == null) return null;
  const unit = deli === 1 ? 'day' : 'days';
  return (
    <Tooltip title={`Hạn thanh toán: ${deli} ngày (TTS bill_finish_period_in_days)`}>
      <span className="shop-finance-deli">
        {deli} {unit}
      </span>
    </Tooltip>
  );
};

export type SharedShopFinancePageProps = {
  config: ShopFinancePortalConfig;
};

export const ShopFinancePage = ({ config }: SharedShopFinancePageProps) => {
  const deps = useShopFinanceDeps();
  deps.useThemeMode?.();
  const isMobile = useIsMobile();

  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null);
  const [period, setPeriod] = useState<PeriodKey>('all');
  const [status, setStatus] = useState('Active');
  const [ownerUserId, setOwnerUserId] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState({ current: 1, pageSize: 100 });
  const [ledgerShop, setLedgerShop] = useState<ShopFinanceItem | null>(null);

  const query = useMemo((): ShopFinanceQuery => {
    const { from, to } = toUtcIsoFromTo(range?.[0], range?.[1]);
    return {
      status,
      from,
      to,
      ownerUserId: config.showSellerFilter ? ownerUserId : undefined,
    };
  }, [range, status, ownerUserId, config.showSellerFilter]);

  const { data, isLoading, isFetching, refetch } = deps.useFinance(query);
  const { data: sellers = [] } = deps.useSellers?.(config.showSellerFilter) ?? { data: [] };

  const summary = data?.summary ?? EMPTY_SUMMARY;
  const items = data?.items ?? [];

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((row) => {
      const hay = [row.name, row.profileName, row.ownerFullName, row.ownerUserName]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return hay.includes(q);
    });
  }, [items, search]);

  const onPeriodChange = (next: PeriodKey) => {
    setPeriod(next);
    setPage((p) => ({ ...p, current: 1 }));
    if (next === 'all') {
      setRange(null);
      return;
    }
    if (next === 'custom') return;
    setRange(rangeForPeriod(next));
  };

  const onRangeChange = (next: [Dayjs | null, Dayjs | null] | null) => {
    if (!next?.[0] || !next[1]) {
      setRange(null);
      setPeriod('all');
      return;
    }
    const clamped: [Dayjs, Dayjs] = [next[0].startOf('day'), next[1].startOf('day')];
    setRange(clamped);
    setPeriod(matchPeriod(clamped));
    setPage((p) => ({ ...p, current: 1 }));
  };

  const txPath = deps.transactionsPath;

  const kpiItems = useMemo((): DashboardKpiItem[] => {
    const compact = isMobile;
    const mainCol = compact ? { xs: 12, sm: 12, flex: '1 1 calc(50% - 5px)' } : undefined;
    const thirdCol = compact ? { xs: 8, sm: 8, flex: '1 1 calc(33.333% - 7px)' } : undefined;
    const halfCol = compact ? { xs: 12, sm: 12, flex: '1 1 calc(50% - 5px)' } : undefined;
    const usd = (n: number) => formatUsd(n, compact);

    const cards: DashboardKpiItem[] = [
      {
        key: 'received',
        label: period === 'all' ? 'Tiền đã về' : compact ? 'Đã về (kỳ)' : 'Tiền đã về (kỳ)',
        value: usd(summary.moneyReceived),
        icon: <DollarOutlined />,
        tone: 'green',
        to: txPath,
        colProps: mainCol,
      },
      {
        key: 'acquisition',
        label: compact ? 'Giá mua' : 'Tổng giá mua',
        value: usd(summary.acquisitionCostTotal),
        icon: <ShopOutlined />,
        tone: 'purple',
        colProps: mainCol,
      },
      {
        key: 'onHold',
        label: 'On hold',
        value: usd(summary.onHoldAmount),
        icon: <PauseCircleOutlined />,
        tone: 'orange',
        colProps: mainCol,
      },
      {
        key: 'inTransit',
        label: compact ? 'Chờ GH' : 'Chờ giao hàng',
        value: usd(summary.onHoldInTransitAmount),
        icon: <CarOutlined />,
        tone: 'blue',
        variant: compact ? 'compact' : 'primary',
        colProps: thirdCol,
      },
      {
        key: 'awaiting',
        label: compact ? 'Sắp TT' : 'Sắp thanh toán',
        value: usd(summary.onHoldAwaitingSettlementAmount),
        icon: <ClockCircleOutlined />,
        tone: 'cyan',
        variant: compact ? 'compact' : 'primary',
        colProps: thirdCol,
      },
      {
        key: 'reserve',
        label: 'Reserve',
        value: usd(summary.onHoldReserveAmount),
        icon: <SafetyOutlined />,
        tone: 'purple',
        variant: compact ? 'compact' : 'primary',
        colProps: thirdCol,
      },
    ];
    if (config.showPendingConfirmation) {
      cards.push({
        key: 'pending',
        label: compact ? 'Chờ duyệt' : 'Chờ duyệt',
        value: usd(summary.pendingConfirmationAmount),
        icon: <ClockCircleOutlined />,
        tone: 'orange',
        variant: compact ? 'compact' : 'primary',
        colProps: halfCol,
      });
    }
    if (config.showShopsWithoutBank) {
      cards.push({
        key: 'noBank',
        label: compact ? 'Chưa thẻ' : 'Chưa gắn thẻ',
        value: summary.shopsWithoutBankCount,
        icon: <BankOutlined />,
        tone: summary.shopsWithoutBankCount > 0 ? 'red' : 'green',
        variant: compact ? 'compact' : 'primary',
        colProps: halfCol,
      });
    } else {
      cards.push({
        key: 'shops',
        label: 'Số shop',
        value: summary.shopCount,
        icon: <ShopOutlined />,
        tone: 'blue',
        variant: compact ? 'compact' : 'primary',
        colProps: halfCol,
      });
    }
    return cards;
  }, [summary, period, config.showPendingConfirmation, config.showShopsWithoutBank, txPath, isMobile]);

  const columns = useMemo((): ColumnsType<ShopFinanceItem> => {
    const shopLink = (shopId: string) => `${txPath}?shopId=${encodeURIComponent(shopId)}`;
    const cols: ColumnsType<ShopFinanceItem> = [
      {
        title: '#',
        key: 'index',
        width: 44,
        align: 'center',
        render: (_, __, index) => (page.current - 1) * page.pageSize + index + 1,
      },
      {
        title: 'Shop',
        key: 'shop',
        width: 280,
        onHeaderCell: () => ({ className: 'shop-finance-shop-th' }),
        onCell: () => ({ className: 'shop-finance-shop-td' }),
        render: (_, record) => {
          const sub = shopSubline(record, config.showOwnerColumn);
          return (
            <div className="shop-finance-shop-cell">
              <Avatar src={record.avatarUrl || undefined} size={32} shape="square">
                {(record.profileName || record.name || '?').charAt(0).toUpperCase()}
              </Avatar>
              <div className="shop-finance-shop-meta">
                <span className="shop-finance-shop-name">
                  <Link to={shopLink(record.shopId)} className="shop-finance-shop-name-link" title={record.name}>
                    <ShopNameText name={record.name} />
                  </Link>
                  {deliBadge(record.deli)}
                </span>
                {sub ? (
                  <span className="shop-finance-shop-profile" title={sub}>
                    {sub}
                  </span>
                ) : null}
              </div>
            </div>
          );
        },
      },
    ];

    cols.push(
        {
          title: 'Paid',
          showSorterTooltip: { title: 'Tiền đã về' },
          dataIndex: 'moneyReceived',
          width: 120,
          align: 'right',
          sorter: (a, b) => a.moneyReceived - b.moneyReceived,
          render: (v: number, record) => (
            <button
              type="button"
              className="shop-finance-ledger-link"
              title="Xem lịch sử tiền về của shop"
              onClick={() => setLedgerShop(record)}
            >
              <MoneyCell value={v} />
            </button>
          ),
        },
        {
          title: 'Giá mua',
          dataIndex: 'acquisitionCost',
          width: 120,
          align: 'right',
          sorter: (a, b) => (a.acquisitionCost ?? 0) - (b.acquisitionCost ?? 0),
          render: (value: number | null | undefined) => <MoneyCell value={value} />,
        },
      {
        title: 'On hold',
        dataIndex: 'onHoldAmount',
        width: 120,
        align: 'right',
        defaultSortOrder: 'descend',
        sorter: (a, b) => (a.onHoldAmount ?? 0) - (b.onHoldAmount ?? 0),
        render: (v: number | null | undefined) => <MoneyCell value={v} />,
      },
      {
        title: 'In transit',
        showSorterTooltip: { title: 'Chờ giao hàng' },
        dataIndex: 'onHoldInTransitAmount',
        width: 110,
        align: 'right',
        sorter: (a, b) => (a.onHoldInTransitAmount ?? 0) - (b.onHoldInTransitAmount ?? 0),
        render: (v: number | null | undefined) => <MoneyCell value={v} />,
      },
      {
        title: 'Delivered',
        showSorterTooltip: { title: 'Sắp thanh toán' },
        dataIndex: 'onHoldAwaitingSettlementAmount',
        width: 110,
        align: 'right',
        sorter: (a, b) =>
          (a.onHoldAwaitingSettlementAmount ?? 0) - (b.onHoldAwaitingSettlementAmount ?? 0),
        render: (v: number | null | undefined) => <MoneyCell value={v} />,
      },
      {
        title: 'Reserve',
        showSorterTooltip: { title: 'Tiền giữ lại' },
        dataIndex: 'onHoldReserveAmount',
        width: 110,
        align: 'right',
        sorter: (a, b) => (a.onHoldReserveAmount ?? 0) - (b.onHoldReserveAmount ?? 0),
        render: (v: number | null | undefined) => <MoneyCell value={v} />,
      },
    );

    if (config.showPendingConfirmation) {
      cols.push({
        title: 'Processing',
        showSorterTooltip: { title: 'Đang xử lý' },
        dataIndex: 'pendingConfirmationAmount',
        width: 110,
        align: 'right',
        sorter: (a, b) => a.pendingConfirmationAmount - b.pendingConfirmationAmount,
        render: (v: number) => <MoneyCell value={v} />,
      });
    }

    cols.push(
      {
        title: 'Thẻ',
        key: 'bank',
        width: 100,
        render: (_, record) =>
          record.receivingBankAccountId ? (
            <Link to={shopLink(record.shopId)}>
              <Tag color="green">Đã gắn</Tag>
            </Link>
          ) : (
            <Tag>Chưa gắn</Tag>
          ),
      },
      {
        title: 'Sync time',
        dataIndex: 'tikTokProfileSyncedAt',
        width: 130,
        render: (v: string | null | undefined) =>
          v ? (
            <Tooltip title={formatDateTimeVn(v)}>
              <Text type="secondary">{formatDateTimeVn(v, DATE_FORMAT)}</Text>
            </Tooltip>
          ) : (
            <Text type="secondary">—</Text>
          ),
      },
    );

    return cols;
  }, [config.showOwnerColumn, config.showPendingConfirmation, txPath, page.current, page.pageSize]);

  const mobileColumns = useMemo((): ColumnsType<ShopFinanceItem> => {
    const shopLink = (shopId: string) => `${txPath}?shopId=${encodeURIComponent(shopId)}`;
    return [
      {
        title: 'Shop',
        key: 'mobile',
        render: (_, record) => {
          const sub = shopSubline(record, config.showOwnerColumn);
          return (
            <Link to={shopLink(record.shopId)} className="shop-finance-card">
              <div className="shop-finance-card-head">
                <Avatar src={record.avatarUrl || undefined} size={32} shape="square">
                  {(record.profileName || record.name || '?').charAt(0).toUpperCase()}
                </Avatar>
                <div className="shop-finance-shop-meta">
                  <span className="shop-finance-shop-name">
                    <span className="shop-finance-shop-name-text" title={record.name}>
                      <ShopNameText name={record.name} />
                    </span>
                    {deliBadge(record.deli)}
                    <Tag
                      color={record.receivingBankAccountId ? 'green' : undefined}
                      className="shop-finance-card-bank"
                    >
                      {record.receivingBankAccountId ? 'Đã gắn' : 'Chưa thẻ'}
                    </Tag>
                  </span>
                  {sub ? <span className="shop-finance-shop-profile">{sub}</span> : null}
                </div>
              </div>
              <div className="shop-finance-card-grid">
                <MetricChip
                  label="Đã về"
                  value={record.moneyReceived}
                  tone="received"
                  onClick={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    setLedgerShop(record);
                  }}
                />
                <MetricChip label="Giá mua" value={record.acquisitionCost} />
                <MetricChip label="On hold" value={record.onHoldAmount} tone="hold" />
                <MetricChip label="Chờ GH" value={record.onHoldInTransitAmount} />
                <MetricChip label="Sắp TT" value={record.onHoldAwaitingSettlementAmount} />
                <MetricChip label="Reserve" value={record.onHoldReserveAmount} />
                {config.showPendingConfirmation ? (
                  <MetricChip label="Chờ duyệt" value={record.pendingConfirmationAmount} />
                ) : null}
              </div>
            </Link>
          );
        },
      },
    ];
  }, [config.showOwnerColumn, config.showPendingConfirmation, txPath]);

  const sellerOptions = useMemo(
    () =>
      sellers.map((s) => ({
        value: s.id,
        label: `${s.fullName || s.userName || s.id}${s.userName && s.fullName ? ` (${s.userName})` : ''}`,
      })),
    [sellers],
  );

  const reloadBtn = (
    <Tooltip title="Làm mới">
      <ReloadOutlined
        onClick={() => {
          void refetch();
        }}
        spin={isFetching}
        style={{ cursor: 'pointer' }}
      />
    </Tooltip>
  );

  const extra: ReactNode = reloadBtn;

  return (
    <div className={`shop-finance-page${isMobile ? ' is-mobile' : ''}`}>
      <PageHeader
        title="Tài chính shop"
        extra={extra}
        description={
          isMobile ? undefined : (
            <Alert
              type="info"
              showIcon
              title="On hold / DELI / Chờ GH / Sắp TT / Reserve là snapshot TikTok Finance. Tiền đã về lấy từ sổ thẻ nhận tiền (Confirmed, Credit − Debit)."
            />
          )
        }
      />
      <div className="shop-finance-toolbar">
          <Segmented
            size="small"
            block={isMobile}
            value={period === 'custom' ? undefined : period}
            options={PERIOD_OPTIONS}
            onChange={(v) => onPeriodChange(v as PeriodKey)}
          />
          <div className="admin-filters">
            <Input.Search
              className="admin-filters-search"
              size="small"
              allowClear
              placeholder="Tìm shop"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage((p) => ({ ...p, current: 1 }));
              }}
            />
            <div className="admin-filters-grid">
              {config.showSellerFilter ? (
                <Select
                  size="small"
                  allowClear
                  placeholder="Seller"
                  options={sellerOptions}
                  value={ownerUserId}
                  onChange={(v) => {
                    setOwnerUserId(v);
                    setPage((p) => ({ ...p, current: 1 }));
                  }}
                  showSearch
                  optionFilterProp="label"
                />
              ) : null}
              <Select
                size="small"
                options={STATUS_OPTIONS}
                value={status}
                onChange={(v) => {
                  setStatus(v);
                  setPage((p) => ({ ...p, current: 1 }));
                }}
              />
              <AdminDateRangeFilter
                variant="grid"
                allowClear
                value={range}
                presets={RANGE_PRESETS}
                disabledDate={disabledFutureVnDate}
                onChange={onRangeChange}
              />
            </div>
          </div>
        </div>
      <DashboardKpiStrip items={kpiItems} />
      <PageContentSpin spinning={isLoading}>
        <Table<ShopFinanceItem>
          className={isMobile ? 'shop-finance-mobile-table' : undefined}
          size="small"
          rowKey="shopId"
          columns={isMobile ? mobileColumns : columns}
          dataSource={filtered}
          showHeader={!isMobile}
          scroll={isMobile ? undefined : { x: 1260 }}
          pagination={tablePagination(filtered.length, {
            current: page.current,
            pageSize: page.pageSize,
            size: 'small',
            simple: isMobile,
            showSizeChanger: !isMobile,
            pageSizeOptions: ['20', '50', '100'],
            onChange: (current, pageSize) => setPage({ current, pageSize }),
          })}
        />
      </PageContentSpin>
      <ShopFinanceTxnDrawer
        shop={ledgerShop}
        from={query.from}
        to={query.to}
        periodLabel={
          range ? `${range[0].format(DATE_FORMAT)} – ${range[1].format(DATE_FORMAT)}` : undefined
        }
        transactionsPath={txPath}
        onClose={() => setLedgerShop(null)}
      />
    </div>
  );
};
