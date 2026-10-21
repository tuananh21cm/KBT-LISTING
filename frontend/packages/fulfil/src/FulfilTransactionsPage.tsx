import { useEffect, useMemo, useState, type Key, type MouseEvent, type ReactNode } from 'react';
import {
  Button,
  DatePicker,
  Descriptions,
  Form,
  Input,
  InputNumber,
  message,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import {
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  LockOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SwapOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { type Dayjs } from 'dayjs';
import { AdminDateRangeFilter, Modal, PageHeader, useIsMobile } from '@drop/ui';
import { EMPTY_FULFIL_TX_SUMMARY, formatFulfilCardOptionLabel, type FulfilTransaction } from '@drop/types/fulfil';
import {
  DATETIME_FORMAT,
  DATETIME_FORMAT_SHORT,
  formatDateTimeVn,
  nowVn,
  parseToVnDayjs,
  rangeForPresetVn,
  toUtcIsoFromTo,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import {
  sheinSupplierStatusTag,
  sheinSupplierStatusTooltip,
} from '@drop/utils/sheinSupplierStatus';
import type { FulfilPortalConfig } from './portalConfig';
import { useFulfilDeps } from './FulfilDepsContext';
import { OrderIdWithHoverPreview } from './OrderHoverPreviewPanel';
import './fulfil.css';

const { Text } = Typography;

type DatePreset = '' | 'day' | 'week' | 'month' | '30d';

const FULFIL_DATE_PRESETS = [
  { value: 'day', label: 'Ngày' },
  { value: 'week', label: 'Tuần' },
  { value: 'month', label: 'Tháng' },
  { value: '30d', label: '30 ngày' },
] as const;

const rangeForPreset = (preset: Exclude<DatePreset, ''>): [Dayjs, Dayjs] =>
  rangeForPresetVn(preset);

const matchFulfilDatePreset = (value: [Dayjs | null, Dayjs | null] | null): DatePreset => {
  if (!value?.[0] || !value?.[1]) return '';
  const [from, to] = value;
  for (const preset of ['day', 'week', 'month', '30d'] as const) {
    const [pFrom, pTo] = rangeForPreset(preset);
    if (from.isSame(pFrom, 'minute') && to.isSame(pTo, 'minute')) return preset;
  }
  return '';
};

const cardSourceLabel = (row: FulfilTransaction) =>
  row.fulfilCardSourceAccountName
    ? `${row.fulfilCardSourceName} · ${row.fulfilCardSourceAccountName}`
    : row.fulfilCardSourceName || row.fulfilCardName || null;

const displayOrderId = (row: FulfilTransaction) =>
  row.platformOrderId || (row.fourSellerOrderId != null ? String(row.fourSellerOrderId) : '');

/**
 * Billno để preview chỉ hiện số của đúng lần quẹt thẻ này. Dòng cấp đơn (chưa gắn billno)
 * mượn `supplierOrderNo` của đơn, nên phải bỏ qua — nó trả tiền cho tổng, không cho 1 billno.
 */
const focusBillno = (row: FulfilTransaction) =>
  row.orderSupplierLinkId ? row.supplierOrderNo : null;

/** Khoá gộp dòng theo đơn; giao dịch chưa gắn đơn nội bộ vẫn gộp được theo mã sàn. */
const orderGroupKey = (row: FulfilTransaction) => {
  if (row.orderId) return row.orderId;
  const platformId = displayOrderId(row);
  return platformId ? `p:${platformId}` : '';
};

const ORDER_GROUP_COLOR_COUNT = 4;

/**
 * Một đơn joint shipment tách thành nhiều lần quẹt thẻ theo billno, nằm rải trong bảng.
 * Tô cùng nền cho các dòng cùng đơn để soát nhanh; đơn chỉ có một dòng thì để nền mặc định.
 * Màu cấp phát tuần tự theo thứ tự dòng nên hai nhóm cạnh nhau không bao giờ trùng màu.
 */
const buildOrderGroupColors = (rows: FulfilTransaction[]) => {
  const counts = new Map<string, number>();
  for (const row of rows) {
    const key = orderGroupKey(row);
    if (!key) continue;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  const colorByGroup = new Map<string, number>();
  for (const row of rows) {
    const key = orderGroupKey(row);
    if (!key || colorByGroup.has(key) || (counts.get(key) ?? 0) < 2) continue;
    colorByGroup.set(key, colorByGroup.size % ORDER_GROUP_COLOR_COUNT);
  }
  return colorByGroup;
};

const SHEIN_ORDER_DETAIL_BASE = 'https://us.shein.com/user/orders/detail';

/** SHEIN detail URL to copy (stored shein URL or fallback like OrdersPage). */
const resolveSheinSupplierCopyUrl = (row: {
  supplierOrderNo?: string | null;
  supplierOrderUrl?: string | null;
}): string | null => {
  const orderNo = row.supplierOrderNo?.trim();
  if (!orderNo) return null;
  const stored = row.supplierOrderUrl?.trim();
  if (stored && !/shein\.com/i.test(stored)) return null;
  return `${SHEIN_ORDER_DETAIL_BASE}/${encodeURIComponent(orderNo)}`;
};

const copySheinSupplierUrl = async (row: FulfilTransaction) => {
  const url = resolveSheinSupplierCopyUrl(row);
  if (!url) return;
  await navigator.clipboard.writeText(url);
  message.success('Đã copy link SHEIN');
};

const SupplierSheinStatusTag = ({
  row,
  compact,
}: {
  row: FulfilTransaction;
  compact?: boolean;
}) => {
  const tag = sheinSupplierStatusTag(row.supplierStatus, row.supplierStatusRaw);
  if (!tag) return null;
  const tip = sheinSupplierStatusTooltip(row.supplierStatus, row.supplierStatusRaw);
  return (
    <Tag color={tag.color} title={tip || tag.label} className="fulfil-txn-tag">
      {compact ? tag.shortLabel : tag.label}
    </Tag>
  );
};

/** Nguồn giao dịch — nhãn ngắn để giữ bảng 1 dòng. */
const SourceTag = ({ source }: { source: string }) =>
  source === 'CostUpdate' ? (
    <Tag color="purple" className="fulfil-txn-tag" title="Cập nhật phí">
      Phí
    </Tag>
  ) : (
    <Tag className="fulfil-txn-tag" title="Thủ công">
      Tay
    </Tag>
  );

/**
 * Lần mua của dòng chi này do người xử lý tự chọn đơn — đồng bộ SHEIN không được tự chuyển nó nữa.
 * Cần thấy ngay trên bảng: nó giải thích vì sao một billno "sai phone/zip" vẫn nằm yên ở đơn này.
 */
const ManualLinkMark = ({ row }: { row: FulfilTransaction }) => {
  if (!row.manualLinkedAt) return null;
  return (
    <Tooltip
      title={`Đã gắn tay ${formatDateTimeVn(row.manualLinkedAt)} — đồng bộ SHEIN không tự chuyển đơn nữa`}
    >
      <LockOutlined className="fulfil-txn-muted" />
    </Tooltip>
  );
};

const EllipsisText = ({
  value,
  maxWidth,
  className,
}: {
  value?: string | null;
  maxWidth?: number;
  className?: string;
}) => {
  const text = value?.trim();
  if (!text) return <Text type="secondary">—</Text>;
  return (
    <Text
      ellipsis={{ tooltip: text }}
      className={className}
      style={maxWidth ? { maxWidth } : undefined}
    >
      {text}
    </Text>
  );
};

const SupplierOrderCell = ({ row }: { row: FulfilTransaction }) => {
  const status = <SupplierSheinStatusTag row={row} compact />;
  const orderNo = row.supplierOrderNo?.trim();
  if (!orderNo) return status ?? <Text type="secondary">—</Text>;

  const sheinUrl = resolveSheinSupplierCopyUrl(row);
  const billno = sheinUrl ? (
    <Text
      className="fulfil-link fulfil-txn-id"
      ellipsis={{ tooltip: `Copy: ${sheinUrl}` }}
      onClick={(e) => {
        e.stopPropagation();
        void copySheinSupplierUrl(row);
      }}
    >
      {orderNo}
    </Text>
  ) : (
    <Text className="fulfil-txn-id" ellipsis={{ tooltip: orderNo }}>
      {orderNo}
    </Text>
  );

  return (
    <div className="fulfil-txn-inline">
      {billno}
      {status}
    </div>
  );
};

const trackingKindLabel = (kind?: string | null) => {
  const k = (kind || '').toLowerCase();
  if (k === 'carrier') return 'Carrier';
  if (k === 'warehouse') return 'Kho SHEIN';
  return null;
};

const copyTrackingNo = async (trackingNo: string) => {
  await navigator.clipboard.writeText(trackingNo);
  message.success('Đã copy mã vận đơn');
};

/** Chỉ hiện tên carrier; click tag → copy mã vận đơn (không hiện mã trên bảng). */
const CarrierTag = ({ row }: { row: FulfilTransaction }) => {
  const trackingNo = row.trackingNo?.trim();
  const carrier = row.carrier?.trim();
  const kind = (row.trackingKind || '').toLowerCase();
  const kindTip = trackingKindLabel(row.trackingKind);

  // Có mã last-mile / tên hãng → hiện carrier (click copy).
  if (carrier || (trackingNo && kind === 'carrier')) {
    const tip = trackingNo
      ? `Copy mã: ${trackingNo}`
      : [carrier, kindTip].filter(Boolean).join(' · ');

    const onCopy = (e: MouseEvent) => {
      e.stopPropagation();
      if (!trackingNo) {
        message.info('Chưa có mã vận đơn');
        return;
      }
      void copyTrackingNo(trackingNo);
    };

    return (
      <Tag
        className="fulfil-txn-tag fulfil-txn-carrier-copy"
        color="blue"
        style={{ margin: 0, cursor: trackingNo ? 'pointer' : 'default' }}
        title={tip}
        onClick={onCopy}
      >
        {carrier || kindTip || 'Track'}
      </Tag>
    );
  }

  // Có mã kho SHEIN (C…/N…) — chưa phải last-mile.
  if (trackingNo && kind === 'warehouse') {
    return (
      <Tag
        className="fulfil-txn-tag"
        style={{ margin: 0 }}
        title={`Kho SHEIN · ${trackingNo}`}
      >
        Kho
      </Tag>
    );
  }

  // Có mã nhưng chưa phân loại / chưa có tên hãng — vẫn cho copy.
  if (trackingNo) {
    return (
      <Tag
        className="fulfil-txn-tag fulfil-txn-carrier-copy"
        style={{ margin: 0, cursor: 'pointer' }}
        title={`Copy mã: ${trackingNo}`}
        onClick={(e) => {
          e.stopPropagation();
          void copyTrackingNo(trackingNo);
        }}
      >
        Track
      </Tag>
    );
  }

  // Đã gắn đơn nhưng chưa có track last-mile.
  if (row.orderId || displayOrderId(row)) {
    return (
      <Tag className="fulfil-txn-tag fulfil-txn-await-track" style={{ margin: 0 }} title="Chưa có mã carrier">
        Chờ
      </Tag>
    );
  }

  return null;
};

const hasMoney = (value?: number | null) =>
  value != null && !Number.isNaN(Number(value)) && Math.abs(Number(value)) > 0;

/** Breakdown gross NCC — null = chưa có; $0 vẫn hiện (vd. Tax = 0). Ship dùng hasMoney. */
const hasBreakdownValue = (value?: number | null) =>
  value != null && !Number.isNaN(Number(value));

const rowPurchaseCost = (row: FulfilTransaction) => {
  if (row.purchaseCostAmount != null) return row.purchaseCostAmount;
  if (row.source !== 'CostUpdate') return null;
  const grossNcc =
    (Number(row.amount) || 0) +
    (Number(row.walletAmount) || 0) +
    (Number(row.couponAmount) || 0) +
    (Number(row.membershipAmount) || 0) +
    (Number(row.pointsAmount) || 0);
  return grossNcc + (Number(row.buyTrackAmount) || 0) + (Number(row.thankCardAmount) || 0);
};

/** Carrier gắn trước mã đơn sàn — click tag copy mã vận đơn. */
const OrderCell = ({
  row,
  maxWidth,
}: {
  row: FulfilTransaction;
  maxWidth?: number;
}) => {
  const id = displayOrderId(row);
  const carrier = <CarrierTag row={row} />;
  if (!id && !carrier) return <Text type="secondary">—</Text>;
  return (
    <div className="fulfil-txn-inline fulfil-txn-order-cell">
      {carrier}
      {id ? (
        <OrderIdWithHoverPreview
          displayId={id}
          orderId={row.orderId}
          maxWidth={maxWidth}
          focusSupplierOrderNo={focusBillno(row)}
        />
      ) : null}
    </div>
  );
};

interface TxnFormValues {
  fulfilAccountId?: string;
  fulfilCardId?: string;
  direction: 'Debit' | 'Credit';
  amount: number;
  currency?: string;
  note?: string;
  paidByUserId?: string;
  occurredAt: Dayjs;
}

const DIRECTION_OPTIONS = [
  { value: 'Debit' as const, label: 'Trừ tiền' },
  { value: 'Credit' as const, label: 'Nạp tiền' },
];

const formatTxnAmount = (amount: number, direction?: string) => {
  const abs = Math.abs(Number(amount) || 0).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  const isCredit = direction === 'Credit';
  return { isCredit, text: `${isCredit ? '+' : '-'}$${abs}` };
};

const AmountText = ({ amount, direction }: { amount: number; direction?: string }) => {
  const { isCredit, text } = formatTxnAmount(amount, direction);
  return (
    <Text
      strong
      className={[
        isCredit ? 'fulfil-amount-credit' : 'fulfil-amount-debit',
        'fulfil-txn-amount',
      ].join(' ')}
    >
      {text}
    </Text>
  );
};

/** Cột Thẻ / số tiền gắn đơn — hover (desktop) hoặc tap (mobile) mở preview Mua hàng như mã đơn. */
const AmountWithOrderPreview = ({ row }: { row: FulfilTransaction }) => {
  const amountEl = <AmountText amount={row.amount} direction={row.direction} />;
  if (!row.orderId) return amountEl;
  return (
    <OrderIdWithHoverPreview
      displayId={displayOrderId(row) || String(row.orderId)}
      orderId={row.orderId}
      focusSupplierOrderNo={focusBillno(row)}
    >
      <span className="fulfil-order-preview-trigger fulfil-txn-amount-preview">{amountEl}</span>
    </OrderIdWithHoverPreview>
  );
};

const formatMoneyUsd = (value: number) =>
  `$${Math.abs(Number(value) || 0).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;

const TxnStatCard = ({
  label,
  title,
  children,
}: {
  label: string;
  title: string;
  children: ReactNode;
}) => (
  <div className="fulfil-txn-stat" title={title}>
    <span className="fulfil-txn-stat-label">{label}</span>
    <span className="fulfil-txn-stat-value">{children}</span>
  </div>
);

/** Plain money cell; optional signed coloring for profit; discount = coupon style. */
const MoneyCell = ({
  value,
  signed,
  discount,
  empty = '—',
}: {
  value?: number | null;
  /** When set, color by sign (profit). */
  signed?: boolean;
  /** Coupon / giảm giá — cùng màu preview (`#c44a01`) + prefix `-`. */
  discount?: boolean;
  empty?: string;
}) => {
  if (value == null || Number.isNaN(Number(value))) {
    return <Text type="secondary">{empty}</Text>;
  }
  const n = Number(value);
  if (discount) {
    const abs = Math.abs(n);
    if (abs <= 0) {
      return <Text className="fulfil-txn-amount fulfil-txn-amount-muted">-$0.00</Text>;
    }
    return (
      <Text className="fulfil-txn-amount fulfil-txn-discount">-{formatMoneyUsd(abs)}</Text>
    );
  }
  const text = signed && n < 0 ? `-${formatMoneyUsd(n)}` : formatMoneyUsd(n);
  if (!signed) {
    return <Text className="fulfil-txn-amount">{text}</Text>;
  }
  return (
    <Text
      strong
      className={[
        n >= 0 ? 'fulfil-amount-credit' : 'fulfil-amount-debit',
        'fulfil-txn-amount',
      ].join(' ')}
    >
      {n > 0 ? `+${text}` : text}
    </Text>
  );
};

export type SharedFulfilTransactionsPageProps = {
  config: FulfilPortalConfig;
};

export const FulfilTransactionsPage = ({ config }: SharedFulfilTransactionsPageProps) => {
  const deps = useFulfilDeps();
  const {
    hasPermission,
    currentUserId,
    useTransactions,
    useAccountOptions,
    useCardOptions,
    useCreateTransaction,
    useUpdateTransaction,
    useDeleteTransaction,
    useBulkDeleteTransactions,
    useShopOptions,
    useSellers,
    useUsers,
    useOrderLookup,
    useReassignPurchaseOrder,
  } = deps;
  const canManage = config.enableManage && hasPermission(config.managePermission);
  const canMutateCostUpdate = canManage && config.enableCostUpdateMutate;
  const canBulkDelete = canManage && config.enableBulkDelete && !!useBulkDeleteTransactions;
  const canReassign =
    config.enableReassignOrder
    && hasPermission(config.reassignPermission)
    && !!useOrderLookup
    && !!useReassignPurchaseOrder;
  const isMobile = useIsMobile();

  const [filterAccountId, setFilterAccountId] = useState<string | undefined>();
  const [filterShopId, setFilterShopId] = useState<string | undefined>();
  const [filterSellerId, setFilterSellerId] = useState<string | undefined>();
  const [filterPaidByUserId, setFilterPaidByUserId] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(
    () => rangeForPresetVn('30d'),
  );
  const [datePreset, setDatePreset] = useState<DatePreset>('30d');
  const [appliedAccountId, setAppliedAccountId] = useState<string | undefined>();
  const [appliedShopId, setAppliedShopId] = useState<string | undefined>();
  const [appliedSellerId, setAppliedSellerId] = useState<string | undefined>();
  const [appliedPaidByUserId, setAppliedPaidByUserId] = useState<string | undefined>();
  const [appliedRange, setAppliedRange] = useState<[Dayjs | null, Dayjs | null] | null>(
    () => rangeForPresetVn('30d'),
  );
  const [pagination, setPagination] = useState({ current: 1, pageSize: 25 });
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([]);
  const [activeRowId, setActiveRowId] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [reassigning, setReassigning] = useState<FulfilTransaction | null>(null);
  const [reassignOrderId, setReassignOrderId] = useState<string | undefined>();
  const [orderSearch, setOrderSearch] = useState('');
  const [editing, setEditing] = useState<FulfilTransaction | null>(null);
  const [viewing, setViewing] = useState<FulfilTransaction | null>(null);
  const [form] = Form.useForm<TxnFormValues>();

  const query = useMemo(
    () => {
      const { from, to } = toUtcIsoFromTo(appliedRange?.[0], appliedRange?.[1]);
      return {
        accountId: appliedAccountId,
        shopId: appliedShopId,
        sellerId: config.showSellerColumn ? appliedSellerId : undefined,
        paidByUserId: config.showPaidByFilter ? appliedPaidByUserId : undefined,
        keyword: searchApplied || undefined,
        from,
        to,
        pageCurrent: pagination.current,
        pageSize: pagination.pageSize,
      };
    },
    [
      appliedAccountId,
      appliedShopId,
      appliedSellerId,
      appliedPaidByUserId,
      searchApplied,
      appliedRange,
      pagination.current,
      pagination.pageSize,
      config.showSellerColumn,
      config.showPaidByFilter,
    ],
  );

  const { data, isLoading, isFetching, refetch } = useTransactions(query);
  const rows = data?.items ?? [];
  const total = data?.total ?? 0;
  const summary = data?.summary ?? EMPTY_FULFIL_TX_SUMMARY;
  const orderGroupColors = useMemo(() => buildOrderGroupColors(rows), [rows]);
  const { data: accountOptions = [] } = useAccountOptions(true);
  const { data: shops = [] } = useShopOptions();
  const { data: sellers = [] } = useSellers?.() ?? { data: [] };
  const [formAccountId, setFormAccountId] = useState<string | undefined>();
  const { data: accountFilteredCards = [] } = useCardOptions(formAccountId, !!formAccountId);
  const { data: allCardOptions = [] } = useCardOptions(undefined, true);
  // No account → all cards (manual txn may be for other services); with account → linked cards only.
  const cardOptions = formAccountId ? accountFilteredCards : allCardOptions;
  // Danh sách người fulfil dùng cho cả bộ lọc và form, nên không gắn với quyền sửa sổ cái.
  const canListUsers = config.showPaidByFilter && hasPermission(config.userListPermission);
  const { data: users = [] } = useUsers?.(canListUsers) ?? { data: [] };

  const createTxn = useCreateTransaction?.();
  const updateTxn = useUpdateTransaction?.();
  const deleteTxn = useDeleteTransaction?.();
  const bulkDeleteTxn = useBulkDeleteTransactions?.();
  const reassignTxn = useReassignPurchaseOrder?.();
  const { data: orderMatches, isFetching: orderLookupLoading } =
    useOrderLookup?.(orderSearch) ?? { data: [], isFetching: false };
  const orderLookupOptions = useMemo(
    () =>
      (orderMatches ?? [])
        // Chọn lại chính đơn đang giữ thì không phải là chuyển đơn — bỏ khỏi danh sách để
        // không ai bấm nhầm rồi tưởng đã xử lý xong.
        .filter((o) => o.id !== reassigning?.orderId)
        .map((o) => ({
          value: o.id,
          label: `${o.platformOrderId ?? o.fourSellerOrderId ?? o.id} · ${o.buyerName ?? '—'} · ${o.postalCode ?? '—'}`,
        })),
    [orderMatches, reassigning?.orderId],
  );

  const canEditRow = (row: FulfilTransaction) =>
    canManage && (row.source !== 'CostUpdate' || canMutateCostUpdate);

  useEffect(() => {
    setSelectedRowKeys([]);
    setActiveRowId(null);
  }, [
    appliedAccountId,
    appliedShopId,
    appliedSellerId,
    appliedPaidByUserId,
    searchApplied,
    appliedRange,
  ]);

  useEffect(() => {
    if (isMobile) setSelectedRowKeys([]);
  }, [isMobile]);

  const handleBulkDelete = async () => {
    if (!bulkDeleteTxn || selectedRowKeys.length === 0) return;
    const ids = selectedRowKeys.map(String);
    await bulkDeleteTxn.mutateAsync(ids);
    setSelectedRowKeys([]);
  };

  const applyDateRangeDraft = (next: [Dayjs, Dayjs] | null) => {
    setRange(next);
    setDatePreset(matchFulfilDatePreset(next));
  };

  /** Tab Ngày/Tuần/Tháng/30 ngày — áp dụng khoảng ngày ngay và load luôn. */
  const applyDatePreset = (preset: Exclude<DatePreset, ''>) => {
    const next = rangeForPreset(preset);
    setDatePreset(preset);
    setRange(next);
    setAppliedRange(next);
    setPagination((p) => ({ ...p, current: 1 }));
  };

  /** Seller / Shop / TK / Fulfil / RangePicker tùy chọn + keyword — chỉ load khi bấm Tìm kiếm. */
  const applyManualFilters = () => {
    setAppliedAccountId(filterAccountId);
    setAppliedShopId(filterShopId);
    setAppliedSellerId(filterSellerId);
    setAppliedPaidByUserId(filterPaidByUserId);
    setSearchApplied(search.trim());
    setAppliedRange(range);
    setPagination((p) => ({ ...p, current: 1 }));
  };
  const userOptions = useMemo(
    () => users.map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName })),
    [users],
  );

  /**
   * Dropdown chỉ liệt kê người fulfil đang hoạt động. Dòng cũ có thể gắn người đã bị khóa hoặc
   * ngoài nhóm đó — vẫn phải hiện tên trên form sửa, nếu không Select sẽ phơi GUID ra.
   */
  const paidByFormOptions = useMemo(() => {
    const id = editing?.paidByUserId;
    if (!id || userOptions.some((o) => o.value === id)) return userOptions;
    return [
      ...userOptions,
      { value: id, label: editing?.paidByUserName?.trim() || 'Người dùng cũ' },
    ];
  }, [userOptions, editing?.paidByUserId, editing?.paidByUserName]);

  const accountFilterOptions = useMemo(
    () => accountOptions.map((a) => ({ value: a.id, label: a.name })),
    [accountOptions],
  );

  const sellerFilterOptions = useMemo(
    () =>
      sellers.map((s) => ({
        value: s.id,
        label: s.fullName?.trim() || s.userName || s.email || s.id,
      })),
    [sellers],
  );

  const shopFilterOptions = useMemo(
    () =>
      shops
        .filter((shop) => {
          if (shop.status && shop.status !== 'Active') return false;
          if (config.showSellerColumn && filterSellerId && shop.ownerId !== filterSellerId) {
            return false;
          }
          return true;
        })
        .map((shop) => {
          const profile = shop.profileName?.trim();
          const shopName = shop.name?.trim() || 'Shop';
          return {
            value: shop.id,
            label: profile ? `${profile} (${shopName})` : shopName,
          };
        }),
    [shops, filterSellerId, config.showSellerColumn],
  );

  useEffect(() => {
    if (!modalOpen) return;
    const accountId = form.getFieldValue('fulfilAccountId') as string | undefined;
    setFormAccountId(accountId);
  }, [modalOpen, form]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setFormAccountId(filterAccountId);
    const defaultCardId = filterAccountId
      ? accountOptions.find((a) => a.id === filterAccountId)?.activeCardId ?? undefined
      : undefined;
    form.setFieldsValue({
      fulfilAccountId: filterAccountId,
      fulfilCardId: defaultCardId,
      direction: 'Debit',
      currency: 'USD',
      paidByUserId: currentUserId ?? undefined,
      occurredAt: nowVn(),
      amount: 0,
    });
    setModalOpen(true);
  };

  const openEdit = (row: FulfilTransaction) => {
    setEditing(row);
    setFormAccountId(row.fulfilAccountId ?? undefined);
    form.setFieldsValue({
      fulfilAccountId: row.fulfilAccountId ?? undefined,
      fulfilCardId: row.fulfilCardId ?? undefined,
      direction: row.direction === 'Credit' ? 'Credit' : 'Debit',
      amount: row.amount,
      currency: row.currency,
      note: row.note ?? undefined,
      paidByUserId: row.paidByUserId ?? undefined,
      occurredAt: parseToVnDayjs(row.occurredAt) ?? nowVn(),
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    const payload = {
      fulfilAccountId: values.fulfilAccountId || null,
      fulfilCardId: values.fulfilCardId || null,
      direction: values.direction || 'Debit',
      amount: values.amount,
      currency: values.currency || 'USD',
      note: values.note || null,
      paidByUserId: values.paidByUserId || null,
      occurredAt: values.occurredAt.toISOString(),
    };
    if (editing) {
      await updateTxn?.mutateAsync({
        id: editing.id,
        data: { ...payload, orderId: editing.orderId },
      });
    } else {
      await createTxn?.mutateAsync(payload);
    }
    setModalOpen(false);
  };

  const openReassign = (row: FulfilTransaction) => {
    setReassigning(row);
    setReassignOrderId(undefined);
    setOrderSearch('');
  };

  const closeReassign = () => {
    setReassigning(null);
    setReassignOrderId(undefined);
    setOrderSearch('');
  };

  /**
   * Dòng Cập nhật phí là bản dẫn xuất của một lần mua NCC, nên chuyển đơn = gắn lại lần mua đó:
   * giá mua và dòng chi của cả hai đơn được dựng lại trong cùng một transaction. Dòng nhập tay
   * không có lần mua nào phía sau nên chỉ cần trỏ lại `orderId`.
   */
  const handleReassign = async () => {
    if (!reassigning || !reassignOrderId) return;
    if (reassigning.orderSupplierLinkId) {
      await reassignTxn?.mutateAsync({
        purchaseId: reassigning.orderSupplierLinkId,
        orderId: reassignOrderId,
      });
    } else {
      await updateTxn?.mutateAsync({
        id: reassigning.id,
        data: {
          fulfilAccountId: reassigning.fulfilAccountId ?? null,
          fulfilCardId: reassigning.fulfilCardId ?? null,
          orderId: reassignOrderId,
          amount: reassigning.amount,
          direction: reassigning.direction,
          currency: reassigning.currency,
          note: reassigning.note ?? null,
          paidByUserId: reassigning.paidByUserId ?? null,
          occurredAt: reassigning.occurredAt,
        },
      });
    }
    closeReassign();
  };

  const renderActions = (record: FulfilTransaction) => {
    const rowEditable = canEditRow(record);
    // Dòng Cập nhật phí mất dấu lần mua (dữ liệu cũ) thì không có gì để gắn lại — đổi mỗi `orderId`
    // của dòng tiền sẽ làm sổ cái lệch khỏi giá mua trên đơn.
    const reassignBlocked = record.source === 'CostUpdate' && !record.orderSupplierLinkId;
    return (
      <Space size={0} className="fulfil-txn-actions" onClick={(e) => e.stopPropagation()}>
        <Button
          type="text"
          size="small"
          icon={<EyeOutlined />}
          title="Xem chi tiết"
          onClick={() => {
            setActiveRowId(record.id);
            setViewing(record);
          }}
        />
        {canReassign ? (
          <Button
            type="text"
            size="small"
            icon={<SwapOutlined />}
            title={
              reassignBlocked
                ? 'Dòng này không gắn lần mua NCC nào — không chuyển đơn được'
                : 'Chuyển sang đơn khác'
            }
            disabled={reassignBlocked}
            onClick={() => openReassign(record)}
          />
        ) : null}
        {rowEditable ? (
          <>
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              title="Sửa"
              onClick={() => openEdit(record)}
            />
            <Popconfirm
              title={
                record.source === 'CostUpdate'
                  ? 'Xóa giao dịch và gỡ tài khoản/thẻ/người xử lý đã gắn trên đơn?'
                  : 'Xóa giao dịch này?'
              }
              onConfirm={() => deleteTxn?.mutate(record.id)}
            >
              <Button type="text" size="small" danger icon={<DeleteOutlined />} title="Xóa" />
            </Popconfirm>
          </>
        ) : null}
      </Space>
    );
  };

  const desktopColumns: ColumnsType<FulfilTransaction> = [
    {
      title: '#',
      key: 'index',
      width: 40,
      align: 'center',
      className: 'fulfil-txn-col-index',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Thời gian',
      dataIndex: 'occurredAt',
      key: 'occurredAt',
      width: 136,
      render: (v: string) => (
        <span className="fulfil-txn-mono fulfil-txn-muted">{formatDateTimeVn(v)}</span>
      ),
    },
    {
      title: 'Account',
      dataIndex: 'fulfilAccountName',
      key: 'fulfilAccountName',
      width: 76,
      ellipsis: true,
      render: (v: string | null | undefined) => <EllipsisText value={v} maxWidth={64} />,
    },
    {
      title: (
        <Tooltip title="Phần thẻ / totalPrice hóa đơn — đối chiếu sao kê thẻ">
          <span>Thẻ</span>
        </Tooltip>
      ),
      dataIndex: 'amount',
      key: 'amount',
      width: 80,
      align: 'right',
      render: (_, row) => <AmountWithOrderPreview row={row} />,
    },
    {
      title: (
        <Tooltip title="Cost = giá mua gộp NCC (Subtotal + Ship + Tax …) + BuyTrack + ThankCard — chưa trừ coupon/ví/club/points">
          <span>Cost</span>
        </Tooltip>
      ),
      key: 'purchaseCostAmount',
      width: 88,
      align: 'right',
      render: (_, row) => {
        if (row.source !== 'CostUpdate' && row.purchaseCostAmount == null) {
          return <Text type="secondary">—</Text>;
        }
        const value = rowPurchaseCost(row);
        return <MoneyCell value={value} />;
      },
    },
    {
      title: (
        <Tooltip title="Profit = Revenue − totalCost (1 lần / đơn). Nhiều dòng cùng mã đơn vẫn hiện cùng số — KPI header chỉ cộng một lần">
          <span>Profit</span>
        </Tooltip>
      ),
      dataIndex: 'profitAmount',
      key: 'profitAmount',
      width: 92,
      align: 'right',
      render: (v: number | null | undefined) => <MoneyCell value={v} signed />,
    },
    {
      title: (
        <Tooltip title="Phí BuyTrack (USPS scan) cấp đơn — từ Orders.buyTrackAmount; không phải quẹt thẻ">
          <span>Track</span>
        </Tooltip>
      ),
      dataIndex: 'buyTrackAmount',
      key: 'buyTrackAmount',
      width: 80,
      align: 'right',
      render: (v: number | null | undefined) =>
        hasMoney(v) ? <MoneyCell value={v} /> : <Text type="secondary">—</Text>,
    },
    {
      title: (
        <Tooltip title="Phí ThankCard cấp đơn — từ Orders.thankCardAmount; không phải quẹt thẻ">
          <span>Thank</span>
        </Tooltip>
      ),
      dataIndex: 'thankCardAmount',
      key: 'thankCardAmount',
      width: 88,
      align: 'right',
      render: (v: number | null | undefined) =>
        hasMoney(v) ? <MoneyCell value={v} /> : <Text type="secondary">—</Text>,
    },
    {
      title: (
        <Tooltip title="Phí ship lần mua NCC — từ breakdown SupplierPurchase; đã gồm trong Cost gộp">
          <span>Ship</span>
        </Tooltip>
      ),
      dataIndex: 'supplierShippingFee',
      key: 'supplierShippingFee',
      width: 80,
      align: 'right',
      render: (v: number | null | undefined) =>
        hasMoney(v) ? <MoneyCell value={v} /> : <Text type="secondary">—</Text>,
    },
    {
      title: (
        <Tooltip title="Thuế lần mua NCC — từ breakdown SupplierPurchase; đã gồm trong Cost gộp">
          <span>Tax</span>
        </Tooltip>
      ),
      dataIndex: 'supplierTax',
      key: 'supplierTax',
      width: 80,
      align: 'right',
      render: (v: number | null | undefined) =>
        hasBreakdownValue(v) ? <MoneyCell value={v} /> : <Text type="secondary">—</Text>,
    },
    {
      title: 'Coupon',
      dataIndex: 'couponAmount',
      key: 'couponAmount',
      width: 88,
      align: 'right',
      render: (v: number | null | undefined) => <MoneyCell value={v} discount />,
    },
    {
      title: (
        <Tooltip title="Thanh toán ví Shein (cashback) — hiển thị riêng; đã gồm trong Cost gộp">
          <span>Wallet</span>
        </Tooltip>
      ),
      dataIndex: 'walletAmount',
      key: 'walletAmount',
      width: 88,
      align: 'right',
      // Ví/Club: style số phụ; hiển thị tách — đã gồm trong Cost gộp khi fallback ledger.
      render: (v: number | null | undefined) => <MoneyCell value={v} discount />,
    },
    {
      title: 'Club',
      dataIndex: 'membershipAmount',
      key: 'membershipAmount',
      width: 80,
      align: 'right',
      render: (v: number | null | undefined) => <MoneyCell value={v} discount />,
    },
    {
      title: 'Points',
      dataIndex: 'pointsAmount',
      key: 'pointsAmount',
      width: 80,
      align: 'right',
      render: (v: number | null | undefined) => <MoneyCell value={v} discount />,
    },
    {
      title: 'Revenue',
      dataIndex: 'revenueAmount',
      key: 'revenueAmount',
      width: 92,
      align: 'right',
      render: (v: number | null | undefined) => <MoneyCell value={v} />,
    },
    {
      title: 'Đơn hàng',
      // Carrier + mã đơn sàn 19 chữ số — đủ chỗ để không cắt đuôi mã.
      key: 'order',
      width: 220,
      ellipsis: true,
      render: (_, row) => <OrderCell row={row} maxWidth={148} />,
    },
    {
      title: 'Mã NCC',
      dataIndex: 'supplierOrderNo',
      key: 'supplierOrderNo',
      width: 200,
      ellipsis: true,
      render: (_, row) => <SupplierOrderCell row={row} />,
    },
    ...(config.showSellerColumn
      ? [
          {
            title: 'Seller',
            dataIndex: 'sellerName',
            key: 'sellerName',
            width: 100,
            ellipsis: true,
            render: (v: string | null | undefined) => <EllipsisText value={v} maxWidth={88} />,
          } satisfies ColumnsType<FulfilTransaction>[number],
        ]
      : []),
    {
      title: (
        <Tooltip title="Người mua hàng — fulfil đã bấm Đã mua">
          <span>Fulfill</span>
        </Tooltip>
      ),
      key: 'paidBy',
      width: 100,
      ellipsis: true,
      render: (_, row) => (
        <EllipsisText value={row.paidByUserName} maxWidth={88} />
      ),
    },
    {
      title: '',
      dataIndex: 'source',
      key: 'source',
      width: 66,
      align: 'center',
      render: (source: string, row) => (
        <Space size={2}>
          <SourceTag source={source} />
          <ManualLinkMark row={row} />
        </Space>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: canManage ? 88 : 36,
      fixed: 'right',
      render: (_, record) => renderActions(record),
    },
  ];

  const mobileColumns: ColumnsType<FulfilTransaction> = [
    {
      title: 'Giao dịch',
      key: 'mobile',
      render: (_, row) => {
        const groupColor = orderGroupColors.get(orderGroupKey(row));
        const extras = [
          { key: 'BT', title: 'Track (BuyTrack USPS)', value: row.buyTrackAmount, discount: false },
          { key: 'TK', title: 'Thank (ThankCard)', value: row.thankCardAmount, discount: false },
          { key: 'Sh', title: 'Ship', value: row.supplierShippingFee, discount: false },
          { key: 'Tx', title: 'Tax', value: row.supplierTax, discount: false, breakdown: true },
          { key: 'Cp', title: 'Coupon', value: row.couponAmount, discount: true },
          { key: 'W', title: 'Wallet', value: row.walletAmount, discount: true },
          { key: 'Cl', title: 'Club', value: row.membershipAmount, discount: true },
          { key: 'Pt', title: 'Points', value: row.pointsAmount, discount: true },
        ].filter((item) =>
          'breakdown' in item && item.breakdown ? hasBreakdownValue(item.value) : hasMoney(item.value),
        );
        const cost = rowPurchaseCost(row);
        const showPnl = cost != null || row.revenueAmount != null || row.profitAmount != null;
        const account = row.fulfilAccountName?.trim() || null;
        const seller = config.showSellerColumn ? row.sellerName?.trim() || null : null;
        const hasOrder = !!(displayOrderId(row) || row.orderId || row.trackingNo || row.carrier);
        const hasSupplier = !!(
          row.supplierOrderNo?.trim() ||
          sheinSupplierStatusTag(row.supplierStatus, row.supplierStatusRaw)
        );

        return (
          <div
            className={[
              'fulfil-txn-card',
              groupColor === undefined ? '' : `fulfil-txn-group-${groupColor}`,
            ]
              .filter(Boolean)
              .join(' ')}
          >
            <div className="fulfil-txn-card-top">
              <AmountWithOrderPreview row={row} />
              {account ? (
                <EllipsisText value={account} className="fulfil-txn-card-account" />
              ) : null}
              <span className="fulfil-txn-card-time">
                {formatDateTimeVn(row.occurredAt, DATETIME_FORMAT_SHORT)}
              </span>
            </div>
            {hasOrder || hasSupplier ? (
              <div className="fulfil-txn-card-line">
                {hasOrder ? <OrderCell row={row} /> : null}
                {hasSupplier ? <SupplierOrderCell row={row} /> : null}
              </div>
            ) : null}
            {showPnl ? (
              <div className="fulfil-txn-card-pnl">
                <span title="Cost = giá mua gộp NCC + BuyTrack + ThankCard">
                  <span className="fulfil-txn-card-pnl-label">Cost</span>
                  <MoneyCell value={cost} />
                </span>
                <span title="Revenue">
                  <span className="fulfil-txn-card-pnl-label">Rev</span>
                  <MoneyCell value={row.revenueAmount} />
                </span>
                <span title="Profit = Revenue − Cost">
                  <span className="fulfil-txn-card-pnl-label">Pft</span>
                  <MoneyCell value={row.profitAmount} signed />
                </span>
              </div>
            ) : null}
            {extras.length > 0 ? (
              <div className="fulfil-txn-card-extras">
                {extras.map((item) => (
                  <span key={item.key} title={item.title}>
                    {item.key} <MoneyCell value={item.value} discount={item.discount} />
                  </span>
                ))}
              </div>
            ) : null}
            <div className="fulfil-txn-card-foot">
              <span className="fulfil-txn-card-tags">
                <SourceTag source={row.source} />
                <ManualLinkMark row={row} />
                {seller ? (
                  <EllipsisText value={seller} className="fulfil-txn-card-seller" />
                ) : null}
              </span>
              {renderActions(row)}
            </div>
          </div>
        );
      },
    },
  ];

  return (
    <div className={isMobile ? 'fulfil-txn-page is-mobile' : 'fulfil-txn-page'}>
      <PageHeader
        title="Lịch sử mua hàng"
        extra={
          <Space size={4}>
            {!isMobile && canBulkDelete && selectedRowKeys.length > 0 && bulkDeleteTxn ? (
              <Popconfirm
                title={`Xóa ${selectedRowKeys.length} giao dịch đã chọn?`}
                description="Giao dịch Cập nhật phí sẽ gỡ tài khoản/thẻ/người xử lý đã gắn trên đơn."
                okText="Xóa"
                cancelText="Hủy"
                okButtonProps={{ danger: true, loading: bulkDeleteTxn.isPending }}
                onConfirm={() => void handleBulkDelete()}
              >
                <Button
                  danger
                  size="small"
                  icon={<DeleteOutlined />}
                  disabled={bulkDeleteTxn.isPending}
                  loading={bulkDeleteTxn.isPending}
                  aria-label={`Xóa ${selectedRowKeys.length}`}
                >
                  Xóa ({selectedRowKeys.length})
                </Button>
              </Popconfirm>
            ) : null}
            <Button
              size="small"
              icon={<ReloadOutlined />}
              onClick={() => void refetch()}
              loading={isFetching}
            >
              {isMobile ? null : 'Làm mới'}
            </Button>
            {canManage ? (
              <Button type="primary" size="small" icon={<PlusOutlined />} onClick={openCreate}>
                {isMobile ? null : 'Thêm giao dịch'}
              </Button>
            ) : null}
          </Space>
        }
      />

      <div className="admin-filters">
        {!isMobile ? (
          <Input
            className="admin-filters-search"
            size="small"
            allowClear
            placeholder="Tìm mã đơn / NCC / track / USPS·FedEx / seller / thẻ…"
            style={{ width: 280 }}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onPressEnter={applyManualFilters}
          />
        ) : null}
        <div className="admin-filters-grid">
          {isMobile ? (
            <Input
              className="admin-filters-search"
              size="small"
              allowClear
              placeholder="Tìm mã đơn / NCC / track…"
              style={{ width: '100%' }}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onPressEnter={applyManualFilters}
            />
          ) : null}
          {config.showSellerColumn ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 140 }}
              options={sellerFilterOptions}
              value={filterSellerId}
              onChange={(v) => {
                setFilterSellerId(v);
                setFilterShopId(undefined);
              }}
            />
          ) : null}
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            style={{ width: isMobile ? '100%' : 200 }}
            options={shopFilterOptions}
            value={filterShopId}
            onChange={setFilterShopId}
          />
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Tài khoản mua hàng"
            style={{ width: isMobile ? '100%' : 160 }}
            options={accountFilterOptions}
            value={filterAccountId}
            onChange={setFilterAccountId}
          />
          {canListUsers ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Người fulfil"
              title="Lọc theo người đã bấm Đã mua (cột Fulfill)"
              style={{ width: isMobile ? '100%' : 150 }}
              options={userOptions}
              value={filterPaidByUserId}
              onChange={setFilterPaidByUserId}
            />
          ) : null}
          <AdminDateRangeFilter
            variant="grid"
            value={range}
            desktopWidth={220}
            presetOptions={[...FULFIL_DATE_PRESETS]}
            presetValue={datePreset}
            presetSpanClassName="admin-filters-span admin-filters-end"
            onChange={applyDateRangeDraft}
            onPresetChange={(preset) => {
              if (!preset) return;
              applyDatePreset(preset as Exclude<DatePreset, ''>);
            }}
            betweenRangeAndPreset={
              <Button
                type="primary"
                size="small"
                block={isMobile}
                icon={<SearchOutlined />}
                onClick={applyManualFilters}
                loading={isFetching}
              >
                Tìm kiếm
              </Button>
            }
          />
        </div>
      </div>

      <div className="fulfil-txn-stats">
        <TxnStatCard label="Đơn" title="Số đơn khác nhau trong bộ lọc (không tính giao dịch thủ công chưa gắn đơn)">
          {summary.orderCount.toLocaleString()}
        </TxnStatCard>
        <TxnStatCard
          label="Thẻ"
          title="Σ cột Thẻ — tiền trừ trên thẻ (Debit − Credit), khớp totalPrice hóa đơn / sao kê thẻ"
        >
          <MoneyCell value={summary.cardAmount} signed={summary.cardAmount < 0} empty="$0.00" />
        </TxnStatCard>
        <TxnStatCard label="Cost" title="Σ Cost (giá mua gộp NCC + BuyTrack/ThankCard 1 lần/đơn) các dòng Cập nhật phí khớp bộ lọc">
          <MoneyCell value={summary.costAmount} empty="$0.00" />
        </TxnStatCard>
        <TxnStatCard
          label="Track"
          title="Σ BuyTrack (USPS scan) 1 lần/đơn trong bộ lọc — từ Orders.buyTrackAmount; không phải quẹt thẻ"
        >
          <MoneyCell value={summary.buyTrackAmount} empty="$0.00" />
        </TxnStatCard>
        <TxnStatCard
          label="Thank"
          title="Σ ThankCard 1 lần/đơn trong bộ lọc — từ Orders.thankCardAmount; không phải quẹt thẻ"
        >
          <MoneyCell value={summary.thankCardAmount} empty="$0.00" />
        </TxnStatCard>
        <TxnStatCard label="Coupon" title="Σ Coupon snapshot trong bộ lọc — không trừ khỏi Cost">
          <MoneyCell value={summary.couponAmount} discount />
        </TxnStatCard>
        <TxnStatCard label="Wallet" title="Σ ví Shein (cashback) — kênh thanh toán, đã gồm trong Cost gộp">
          <MoneyCell value={summary.walletAmount} discount />
        </TxnStatCard>
        <TxnStatCard label="Club" title="Σ giảm membership / Club — hiển thị riêng, Cost gộp không trừ">
          <MoneyCell value={summary.membershipAmount} discount />
        </TxnStatCard>
        <TxnStatCard label="Points" title="Σ giảm SHEIN Points trong bộ lọc">
          <MoneyCell value={summary.pointsAmount} discount />
        </TxnStatCard>
        <TxnStatCard
          label="Profit"
          title="Tổng Profit 1 lần / đơn trong bộ lọc (Revenue − totalCost). Đơn joint shipment / nhiều tài khoản Shein vẫn chỉ cộng một lần — không cộng trùng các dòng ledger cùng OrderId"
        >
          <MoneyCell value={summary.profitAmount} signed />
        </TxnStatCard>
      </div>

      <Table
        className={isMobile ? 'fulfil-txn-table fulfil-txn-mobile-table' : 'fulfil-txn-table'}
        rowKey="id"
        size="small"
        loading={isLoading}
        columns={isMobile ? mobileColumns : desktopColumns}
        dataSource={rows}
        showHeader={!isMobile}
        rowSelection={
          !isMobile && canBulkDelete
            ? {
                selectedRowKeys,
                onChange: (keys) => setSelectedRowKeys(keys),
                columnWidth: 36,
                getCheckboxProps: () => ({
                  onClick: (e) => e.stopPropagation(),
                }),
              }
            : undefined
        }
        onRow={
          isMobile
            ? undefined
            : (record) => ({
                onClick: () => setActiveRowId(record.id),
                style: { cursor: 'pointer' },
              })
        }
        rowClassName={
          isMobile
            ? undefined
            : (record) => {
                const groupColor = orderGroupColors.get(orderGroupKey(record));
                return [
                  groupColor === undefined ? '' : `fulfil-txn-group-${groupColor}`,
                  record.id === activeRowId ? 'fulfil-txn-row-active' : '',
                ]
                  .filter(Boolean)
                  .join(' ');
              }
        }
        pagination={tablePagination(total, {
          ...pagination,
          total,
          size: 'small',
          simple: isMobile,
          showSizeChanger: !isMobile,
          onChange: (current, pageSize) => setPagination({ current, pageSize }),
        })}
        scroll={isMobile ? undefined : { x: 1480 }}
        tableLayout={isMobile ? 'fixed' : undefined}
        styles={
          isMobile
            ? { body: { cell: { paddingBlock: 0, paddingInline: 0 } } }
            : { body: { cell: { whiteSpace: 'nowrap', paddingBlock: 6, paddingInline: 8 } } }
        }
      />

      <Modal
        title={
          viewing
            ? isMobile
              ? formatDateTimeVn(viewing.occurredAt)
              : `Giao dịch · ${formatDateTimeVn(viewing.occurredAt)}`
            : 'Chi tiết giao dịch'
        }
        open={!!viewing}
        onCancel={() => setViewing(null)}
        footer={
          <div
            style={{
              display: 'flex',
              flexDirection: isMobile ? 'column-reverse' : 'row',
              flexWrap: 'wrap',
              gap: 8,
              justifyContent: isMobile ? 'stretch' : 'flex-end',
            }}
          >
            <Button block={isMobile} onClick={() => setViewing(null)}>
              Đóng
            </Button>
            {canManage && viewing && canEditRow(viewing) ? (
              <Button
                block={isMobile}
                type="primary"
                icon={<EditOutlined />}
                onClick={() => {
                  const row = viewing;
                  setViewing(null);
                  openEdit(row);
                }}
              >
                Sửa
              </Button>
            ) : null}
          </div>
        }
        width={isMobile ? '100%' : 480}
        centered={!isMobile}
        styles={isMobile ? { body: { paddingTop: 8, maxHeight: '70vh', overflowY: 'auto' } } : undefined}
        destroyOnClose
      >
        {viewing ? (
          <Descriptions
            className="fulfil-view-descriptions"
            bordered
            size="small"
            column={1}
            styles={{
              label: isMobile
                ? { width: 108, padding: '6px 8px', fontSize: 12 }
                : { width: 140 },
              ...(isMobile ? { content: { padding: '6px 8px', fontSize: 12 } } : {}),
            }}
          >
            <Descriptions.Item label="Số tiền">
              <AmountText amount={viewing.amount} direction={viewing.direction} />
              {viewing.currency ? (
                <Text type="secondary" style={{ marginLeft: 6 }}>
                  {viewing.currency}
                </Text>
              ) : null}
            </Descriptions.Item>
            <Descriptions.Item label="Thời gian">
              {formatDateTimeVn(viewing.occurredAt)}
            </Descriptions.Item>
            <Descriptions.Item label="Đơn hàng">
              <OrderCell row={viewing} maxWidth={220} />
            </Descriptions.Item>
            <Descriptions.Item label="Supplier order no">
              {viewing.supplierOrderNo ? (
                <Space size={8} wrap>
                  {resolveSheinSupplierCopyUrl(viewing) ? (
                    <Text
                      className="fulfil-link"
                      style={{ fontVariantNumeric: 'tabular-nums' }}
                      onClick={() => void copySheinSupplierUrl(viewing)}
                    >
                      {viewing.supplierOrderNo}
                    </Text>
                  ) : (
                    viewing.supplierOrderNo
                  )}
                  <SupplierSheinStatusTag row={viewing} />
                </Space>
              ) : sheinSupplierStatusTag(
                  viewing.supplierStatus,
                  viewing.supplierStatusRaw,
                ) ? (
                <SupplierSheinStatusTag row={viewing} />
              ) : (
                '—'
              )}
            </Descriptions.Item>
            <Descriptions.Item label="Trạng thái SHEIN">
              {(() => {
                const tag = sheinSupplierStatusTag(
                  viewing.supplierStatus,
                  viewing.supplierStatusRaw,
                );
                if (!tag) return viewing.supplierStatusRaw || viewing.supplierStatus || '—';
                const tip = sheinSupplierStatusTooltip(
                  viewing.supplierStatus,
                  viewing.supplierStatusRaw,
                );
                return (
                  <Space size={6}>
                    <Tag color={tag.color} style={{ margin: 0 }}>
                      {tag.label}
                    </Tag>
                    {tip ? (
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        {tip}
                      </Text>
                    ) : null}
                  </Space>
                );
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="Vận đơn">
              {viewing.trackingNo || viewing.carrier ? (
                <Space size={8} wrap>
                  {viewing.carrier ? (
                    <Tag
                      color={
                        (viewing.trackingKind || '').toLowerCase() === 'carrier'
                          ? 'blue'
                          : undefined
                      }
                      style={{ margin: 0 }}
                    >
                      {viewing.carrier}
                      {trackingKindLabel(viewing.trackingKind)
                        ? ` · ${trackingKindLabel(viewing.trackingKind)}`
                        : ''}
                    </Tag>
                  ) : trackingKindLabel(viewing.trackingKind) ? (
                    <Text type="secondary">{trackingKindLabel(viewing.trackingKind)}</Text>
                  ) : null}
                  {viewing.trackingNo ? (
                    <Text
                      className="fulfil-link"
                      style={{ fontVariantNumeric: 'tabular-nums' }}
                      onClick={() => void copyTrackingNo(viewing.trackingNo!.trim())}
                    >
                      {viewing.trackingNo}
                    </Text>
                  ) : null}
                </Space>
              ) : (
                '—'
              )}
            </Descriptions.Item>
            {config.showSellerColumn ? (
              <Descriptions.Item label="Seller">{viewing.sellerName || '—'}</Descriptions.Item>
            ) : null}
            <Descriptions.Item label="Tài khoản mua hàng">
              {viewing.fulfilAccountName || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Nguồn thẻ">
              {cardSourceLabel(viewing) || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Số thẻ">
              <Text
                style={{
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
                  fontVariantNumeric: 'tabular-nums',
                }}
              >
                {viewing.fulfilCardNumberMasked || '—'}
              </Text>
              {viewing.fulfilCardCurrency ? (
                <Tag style={{ marginInlineStart: 6, marginInlineEnd: 0 }}>
                  {viewing.fulfilCardCurrency}
                </Tag>
              ) : null}
            </Descriptions.Item>
            <Descriptions.Item label="Người tạo">
              {viewing.createdByUserName || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Người mua hàng">
              {viewing.paidByUserName || '—'}
            </Descriptions.Item>
            <Descriptions.Item label="Nguồn">
              {viewing.source === 'CostUpdate' ? (
                <Tag color="purple" style={{ marginInlineEnd: 0 }}>
                  Cập nhật phí
                </Tag>
              ) : (
                <Tag style={{ marginInlineEnd: 0 }}>Thủ công</Tag>
              )}
            </Descriptions.Item>
            {viewing.manualLinkedAt ? (
              <Descriptions.Item label="Gắn tay">
                {formatDateTimeVn(viewing.manualLinkedAt)} — đồng bộ SHEIN không tự chuyển đơn nữa
              </Descriptions.Item>
            ) : null}
            <Descriptions.Item label="Ghi chú">{viewing.note || '—'}</Descriptions.Item>
          </Descriptions>
        ) : null}
      </Modal>

      <Modal
        title={`Chuyển ${reassigning?.supplierOrderNo?.trim() || 'giao dịch'} sang đơn khác`}
        open={reassigning !== null}
        onCancel={closeReassign}
        onOk={() => void handleReassign()}
        okText="Chuyển"
        cancelText="Hủy"
        okButtonProps={{
          disabled: !reassignOrderId,
          loading: reassignTxn?.isPending || updateTxn?.isPending,
        }}
      >
        <Space direction="vertical" style={{ width: '100%' }} size={8}>
          {reassigning ? (
            <Descriptions size="small" column={1} bordered>
              <Descriptions.Item label="Đơn hiện tại">
                {displayOrderId(reassigning) || '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Số tiền">
                <MoneyCell value={reassigning.amount} />
              </Descriptions.Item>
              <Descriptions.Item label="Thời gian">
                {formatDateTimeVn(reassigning.occurredAt)}
              </Descriptions.Item>
            </Descriptions>
          ) : null}
          <Select
            showSearch
            allowClear
            placeholder="Gõ ít nhất 3 ký tự mã đơn / tên khách / SĐT để tìm"
            style={{ width: '100%' }}
            filterOption={false}
            options={orderLookupOptions}
            value={reassignOrderId}
            loading={orderLookupLoading}
            onSearch={setOrderSearch}
            onChange={setReassignOrderId}
            notFoundContent={
              orderSearch.trim().length < 3
                ? 'Gõ thêm để tìm…'
                : orderLookupLoading
                  ? 'Đang tìm…'
                  : 'Không tìm thấy đơn.'
            }
          />
          <Text type="secondary" style={{ fontSize: 12 }}>
            {reassigning?.orderSupplierLinkId
              ? 'Lần mua NCC chuyển sang đơn mới: giá mua và dòng chi của cả hai đơn được tính lại'
                + ' ngay, và lượt đồng bộ SHEIN sau không tự chuyển billno này nữa.'
              : 'Dòng nhập tay chỉ đổi đơn được gắn — không ảnh hưởng giá mua của đơn nào.'}
          </Text>
        </Space>
      </Modal>

      <Modal
        title={editing ? 'Sửa giao dịch' : 'Thêm giao dịch thủ công'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={() => void handleSubmit()}
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={!!(createTxn?.isPending || updateTxn?.isPending)}
        destroyOnClose
        width={isMobile ? '100%' : 520}
        centered={!isMobile}
        className="fulfil-edit-modal"
        styles={isMobile ? { body: { paddingTop: 8, maxHeight: '70vh', overflowY: 'auto' } } : undefined}
      >
        {editing?.source === 'CostUpdate' ? (
          <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
            Giao dịch tự động từ Cập nhật phí — có thể sửa số tiền / ghi chú / thẻ.
          </Text>
        ) : null}
        <Form
          form={form}
          layout="vertical"
          requiredMark={false}
          size="small"
          onValuesChange={(changed) => {
            if ('fulfilAccountId' in changed) {
              setFormAccountId(changed.fulfilAccountId);
              const selected = accountOptions.find((a) => a.id === changed.fulfilAccountId);
              form.setFieldValue('fulfilCardId', selected?.activeCardId ?? undefined);
            }
          }}
        >
          <Form.Item
            name="fulfilAccountId"
            label="Tài khoản mua hàng"
            extra={isMobile ? undefined : 'Không bắt buộc — chọn tài khoản sẽ tự gắn thẻ đang dùng'}
          >
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Không gắn tài khoản"
              options={accountOptions.map((a) => ({ value: a.id, label: a.name }))}
            />
          </Form.Item>
          <Form.Item
            name="fulfilCardId"
            label="Thẻ thanh toán"
            extra={
              isMobile
                ? undefined
                : 'Mặc định = thẻ đang dùng của tài khoản; có thể đổi hoặc bỏ trống'
            }
          >
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Không gắn thẻ"
              options={cardOptions.map((c) => ({
                value: c.id,
                label: formatFulfilCardOptionLabel(c),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="direction"
            label="Loại giao dịch"
            rules={[{ required: true, message: 'Chọn loại giao dịch' }]}
          >
            <Select options={DIRECTION_OPTIONS} disabled={editing?.source === 'CostUpdate'} />
          </Form.Item>
          <div className="fulfil-form-grid">
            <Form.Item
              name="amount"
              label="Số tiền"
              rules={[{ required: true, message: 'Nhập số tiền' }]}
            >
              <InputNumber min={0} step={0.01} precision={2} style={{ width: '100%' }} prefix="$" />
            </Form.Item>
            <Form.Item name="currency" label="Currency">
              <Input />
            </Form.Item>
          </div>
          {config.showPaidByFilter ? (
            <Form.Item
              name="paidByUserId"
              label="Người mua hàng"
              extra={isMobile ? undefined : 'Fulfil đã đi đơn / bấm Đã mua — gán doanh số cho người này'}
            >
              <Select allowClear showSearch optionFilterProp="label" options={paidByFormOptions} />
            </Form.Item>
          ) : null}
          <Form.Item
            name="occurredAt"
            label="Thời gian"
            rules={[{ required: true, message: 'Chọn thời gian' }]}
          >
            <DatePicker
              showTime
              style={{ width: '100%' }}
              format={DATETIME_FORMAT}
              inputReadOnly={isMobile}
            />
          </Form.Item>
          <Form.Item name="note" label="Ghi chú">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};
