import { useEffect, useMemo, useState, type Key } from 'react';
import {
  Alert,
  Button,
  Descriptions,
  Input,
  Popconfirm,
  Popover,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { DisconnectOutlined, DeleteOutlined, LinkOutlined, ReloadOutlined, StopOutlined } from '@ant-design/icons';
import { type Dayjs } from 'dayjs';
import { Modal } from '../../components/AppModal';
import { PageContentSpin, AdminDateRangeFilter } from '@drop/ui';
import { PageHeader } from '../../components/PageHeader';
import { useIsMobile } from '../../hooks/useIsMobile';
import { useFulfilAccountOptions } from '../../hooks/useAdminFulfil';
import { useFulfilUserOptions } from '../../hooks/useAdminUsers';
import {
  useAttachSupplierPurchase,
  useBulkDeleteSupplierPurchases,
  useDeleteSupplierPurchase,
  useDetachSupplierPurchase,
  useIgnoreSupplierPurchase,
  useOrderLookup,
  useSupplierPurchases,
} from '../../hooks/useAdminSupplierPurchases';
import { useAuthStore } from '../../stores/authStore';
import {
  SUPPLIER_PURCHASE_MATCH_STATES,
  SUPPLIER_PURCHASE_SKIP_REASONS,
  type SupplierPurchase,
} from '../../types/supplierPurchase';
import {
  formatDateTimeVn,
  rangeForPresetVn,
  toUtcIsoFromTo,
} from '../../utils/datetime';
import { tablePagination } from '../../utils/pagination';
import {
  sheinSupplierStatusTag,
  sheinSupplierStatusTooltip,
} from '../../utils/sheinSupplierStatus';
import '@drop/fulfil/fulfil.css';

const { Text } = Typography;

type DatePreset = '' | 'day' | 'week' | 'month' | '30d';

const SP_DATE_PRESETS = [
  { value: 'day', label: 'Ngày' },
  { value: 'week', label: 'Tuần' },
  { value: 'month', label: 'Tháng' },
  { value: '30d', label: '30 ngày' },
] as const;

const rangeForPreset = (preset: Exclude<DatePreset, ''>): [Dayjs, Dayjs] =>
  rangeForPresetVn(preset);

const matchSpDatePreset = (value: [Dayjs | null, Dayjs | null] | null): DatePreset => {
  if (!value?.[0] || !value?.[1]) return '';
  const [from, to] = value;
  for (const preset of ['day', 'week', 'month', '30d'] as const) {
    const [pFrom, pTo] = rangeForPreset(preset);
    if (from.isSame(pFrom, 'minute') && to.isSame(pTo, 'minute')) return preset;
  }
  return '';
};

const PAGE_SIZE_OPTIONS = [10, 20, 50];
/** Desktop scroll: cột dữ liệu + Fulfill + checkbox chọn nhiều (~36px). */
const TABLE_SCROLL_X = 1584;

const MATCH_STATE_OPTIONS = Object.entries(SUPPLIER_PURCHASE_MATCH_STATES).map(([value, label]) => ({
  value,
  label,
}));

const SKIP_REASON_OPTIONS = Object.entries(SUPPLIER_PURCHASE_SKIP_REASONS).map(([value, label]) => ({
  value,
  label,
}));

/** Badge trạng thái vòng đời đơn NCC (SHEIN) — đủ chữ, khung ôm sát. */
const supplierStatusCell = (row: SupplierPurchase) => {
  const tag = sheinSupplierStatusTag(row.supplierStatus);
  if (!tag) return <Text type="secondary">—</Text>;
  const tip = sheinSupplierStatusTooltip(row.supplierStatus);
  return (
    <Tag
      color={tag.color}
      title={tip || tag.label}
      className="app-tag-compact"
      style={{ width: 'fit-content', paddingInline: 4, lineHeight: '16px', height: 18 }}
    >
      {tag.label}
    </Tag>
  );
};

const money = (value: number | null, currency: string) =>
  value === null ? '—' : `${value.toFixed(2)} ${currency}`;

const SHEIN_TRACK_BASE = 'https://us.shein.com/orders/track';

const trackingKindLabel = (kind?: string | null) => {
  if (kind === 'carrier') return 'Carrier';
  if (kind === 'warehouse') return 'Kho SHEIN';
  return null;
};

const buildSheinTrackUrl = (billno?: string | null): string | null => {
  const no = billno?.trim();
  if (!no) return null;
  return `${SHEIN_TRACK_BASE}?billno=${encodeURIComponent(no)}`;
};

const copySheinTrackUrl = async (url: string) => {
  await navigator.clipboard.writeText(url);
  message.success('Đã copy link track SHEIN');
};

const trackingCell = (row: SupplierPurchase) => {
  const code = (row.trackingNo || row.warehouseTrackingNo || '').trim();
  if (!code) return <Text type="secondary">—</Text>;
  const carrier = row.carrier?.trim();
  const kind = trackingKindLabel(row.trackingKind);
  const trackUrl = buildSheinTrackUrl(row.supplierOrderNo);
  // Ưu tiên tên hãng thật; fallback loại chặng (Kho / Carrier) khi chưa nhận diện tên.
  const tipParts = [carrier || kind, code].filter(Boolean);
  if (trackUrl) tipParts.push('Copy');

  const label = (
    <Text
      ellipsis
      className={trackUrl ? 'fulfil-link' : undefined}
      style={{
        fontSize: 12,
        maxWidth: '100%',
        display: 'inline-block',
        cursor: trackUrl ? 'pointer' : undefined,
      }}
      onClick={
        trackUrl
          ? (e) => {
              e.stopPropagation();
              void copySheinTrackUrl(trackUrl);
            }
          : undefined
      }
    >
      {code}
    </Text>
  );

  return <Tooltip title={tipParts.join(' · ')}>{label}</Tooltip>;
};

/**
 * Bằng chứng near-miss backend ghi lúc bỏ qua. Chỉ hiển thị những khoá có ý nghĩa với người xử lý:
 * pool trống hay pool đầy mà không đơn nào trùng khoá là hai việc phải làm khác nhau.
 */
const EVIDENCE_LABELS: Record<string, string> = {
  poolSize: 'Đơn trong pool',
  phoneMatches: 'Trùng điện thoại',
  zipMatches: 'Trùng mã bưu chính',
  bothMatches: 'Trùng cả hai',
  rejectedPurchasedBeforeOrder: 'Bị loại: mua trước khi đơn được đặt',
  rejectedCanceled: 'Bị loại: đơn đã huỷ/hoàn',
  phoneKey: 'Khoá điện thoại',
  zipKey: 'Khoá mã bưu chính',
  mode: 'Chế độ sync',
};

function SkipEvidence({ json }: { json: string | null }) {
  const parsed = useMemo(() => {
    if (!json) return null;
    try {
      return JSON.parse(json) as Record<string, unknown>;
    } catch {
      return null;
    }
  }, [json]);

  if (!parsed) return <Text type="secondary">Không có bằng chứng đi kèm.</Text>;

  return (
    <Descriptions size="small" column={1} bordered>
      {Object.entries(parsed).map(([key, value]) => (
        <Descriptions.Item key={key} label={EVIDENCE_LABELS[key] ?? key}>
          {value === null || value === undefined ? '—' : String(value)}
        </Descriptions.Item>
      ))}
    </Descriptions>
  );
}

/**
 * Hàng đợi đơn NCC chưa gắn.
 *
 * Khớp tự động cần điện thoại **và** mã bưu chính trùng tuyệt đối, nên luôn có phần trượt. Trước
 * đây trượt là mất luôn dấu vết — bảng cũ là bảng con của `Orders` nên không khớp đơn là không ghi
 * gì cả. Trang này là chỗ kết thúc nốt phần máy không quyết được.
 */
export function SupplierPurchasesPage() {
  const isMobile = useIsMobile();
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.orders.manage');
  const canListUsers = hasPermission('admin.users.view');

  const [matchState, setMatchState] = useState<string | undefined>('unmatched');
  const [skipReason, setSkipReason] = useState<string | undefined>();
  const [accountId, setAccountId] = useState<string | undefined>();
  const [paidByUserId, setPaidByUserId] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [datePreset, setDatePreset] = useState<DatePreset>('');
  const [pagination, setPagination] = useState({ current: 1, pageSize: 50 });
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([]);

  const [attachTarget, setAttachTarget] = useState<SupplierPurchase | null>(null);
  const [attachOrderId, setAttachOrderId] = useState<string | undefined>();
  const [attachAccountId, setAttachAccountId] = useState<string | undefined>();
  const [orderSearch, setOrderSearch] = useState('');

  const query = useMemo(() => {
    const { from, to } = toUtcIsoFromTo(range?.[0], range?.[1]);
    return {
      matchState,
      skipReason,
      fulfilAccountId: accountId,
      paidByUserId: canListUsers ? paidByUserId : undefined,
      keyword: searchApplied || undefined,
      from,
      to,
      pageCurrent: pagination.current,
      pageSize: pagination.pageSize,
    };
  }, [matchState, skipReason, accountId, canListUsers, paidByUserId, searchApplied, range, pagination]);

  const { data, isLoading, isFetching, refetch } = useSupplierPurchases(query);
  const rows = data?.items ?? [];
  const total = data?.total ?? 0;

  const { data: accounts } = useFulfilAccountOptions();
  const accountOptions = useMemo(
    () => (accounts ?? []).map((a) => ({ value: a.id, label: a.name })),
    [accounts],
  );
  const { data: users = [] } = useFulfilUserOptions(canListUsers);
  const userOptions = useMemo(
    () => (users ?? []).map((u) => ({ value: u.id, label: u.fullName?.trim() || u.userName })),
    [users],
  );

  const { data: orderMatches, isFetching: orderLookupLoading } = useOrderLookup(orderSearch);
  const orderOptions = useMemo(
    () =>
      (orderMatches ?? []).map((o) => ({
        value: o.id,
        label: `${o.platformOrderId ?? o.fourSellerOrderId} · ${o.buyerName ?? '—'} · ${o.postalCode ?? '—'}`,
      })),
    [orderMatches],
  );

  const attachMutation = useAttachSupplierPurchase();
  const detachMutation = useDetachSupplierPurchase();
  const ignoreMutation = useIgnoreSupplierPurchase();
  const deleteMutation = useDeleteSupplierPurchase();
  const bulkDeleteMutation = useBulkDeleteSupplierPurchases();

  const canBulkDelete = canManage && !isMobile;

  const resetPage = () => {
    setPagination((p) => ({ ...p, current: 1 }));
    setSelectedRowKeys([]);
  };

  useEffect(() => {
    if (isMobile) setSelectedRowKeys([]);
  }, [isMobile]);

  const handleBulkDelete = async () => {
    if (selectedRowKeys.length === 0) return;
    const ids = selectedRowKeys.map(String).filter(Boolean);
    await bulkDeleteMutation.mutateAsync(ids);
    setSelectedRowKeys([]);
  };

  const applyDateRange = (next: [Dayjs, Dayjs] | null) => {
    setRange(next);
    setDatePreset(matchSpDatePreset(next));
    resetPage();
  };

  const closeAttach = () => {
    setAttachTarget(null);
    setAttachOrderId(undefined);
    setAttachAccountId(undefined);
    setOrderSearch('');
  };

  /**
   * Lần mua chưa quy kết được tài khoản (email billing SHEIN không map) thì đây là lúc duy nhất
   * người xử lý đang nhìn đủ bằng chứng để chỉ ra nó — thiếu tài khoản thì đơn hiện đúng tiền mà
   * trống cột TK mua, và dòng sổ cái mang ghi chú "chưa rõ tài khoản mua hàng".
   */
  const attachNeedsAccount = attachTarget !== null && !attachTarget.fulfilAccountId;

  const handleAttach = async () => {
    if (!attachTarget || !attachOrderId) return;
    if (attachNeedsAccount && !attachAccountId) return;
    await attachMutation.mutateAsync({
      id: attachTarget.id,
      orderId: attachOrderId,
      fulfilAccountId: attachNeedsAccount ? attachAccountId : undefined,
    });
    closeAttach();
  };

  const copySupplierOrderUrl = async (url: string) => {
    await navigator.clipboard.writeText(url);
    message.success('Đã copy link SHEIN');
  };

  const purchaseCell = (row: SupplierPurchase) => {
    const orderNo = row.supplierOrderNo?.trim();
    const orderUrl = row.supplierOrderUrl?.trim();
    const billno =
      orderNo && orderUrl ? (
        <Text
          className="fulfil-link"
          strong
          title="Copy"
          style={{ cursor: 'pointer' }}
          onClick={(e) => {
            e.stopPropagation();
            void copySupplierOrderUrl(orderUrl);
          }}
        >
          {orderNo}
        </Text>
      ) : (
        <Text strong>{orderNo ?? 'Nhập tay'}</Text>
      );

    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 4,
          minWidth: 0,
          whiteSpace: 'nowrap',
        }}
      >
        <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>{billno}</span>
        {row.source === 'manual' ? (
          <Tag className="app-tag-compact" style={{ flexShrink: 0 }}>
            Manual
          </Tag>
        ) : null}
        {row.isMembershipOrVirtual ? (
          <Tag color="purple" className="app-tag-compact" style={{ flexShrink: 0 }}>
            Club
          </Tag>
        ) : null}
      </div>
    );
  };

  const paymentCell = (row: SupplierPurchase) =>
    row.supplierUnpaid ? (
      <Tag color="red" className="app-tag-compact">
        Unpaid
      </Tag>
    ) : (
      <Tag color="green" className="app-tag-compact">
        Paid
      </Tag>
    );

  const moneyCell = (row: SupplierPurchase) => (
    <div style={{ fontSize: 12, fontWeight: 600, lineHeight: 1.35 }}>
      {money(row.supplierTotal, row.currency)}
    </div>
  );

  const receiverCell = (row: SupplierPurchase) => {
    const name = row.receiverName?.trim() || '—';
    const hasDetail = !!(row.receiverName || row.receiverPhone || row.receiverPostalCode);
    if (!hasDetail) return <Text type="secondary">—</Text>;

    return (
      <Popover
        trigger="hover"
        placement="right"
        mouseEnterDelay={0.2}
        title="Người nhận"
        content={
          <Descriptions size="small" column={1} style={{ maxWidth: 260 }}>
            <Descriptions.Item label="Tên">{row.receiverName ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Điện thoại">{row.receiverPhone ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Mã bưu chính">{row.receiverPostalCode ?? '—'}</Descriptions.Item>
          </Descriptions>
        }
      >
        <Text
          ellipsis
          style={{ fontSize: 12, maxWidth: '100%', cursor: 'default', display: 'inline-block' }}
        >
          {name}
        </Text>
      </Popover>
    );
  };

  const paidByCell = (row: SupplierPurchase) => {
    const text = row.paidByUserName?.trim();
    if (!text) return <Text type="secondary">—</Text>;
    return (
      <Text ellipsis={{ tooltip: text }} style={{ fontSize: 12, maxWidth: 88, display: 'inline-block' }}>
        {text}
      </Text>
    );
  };

  const reasonCell = (row: SupplierPurchase) => {
    if (row.matchState === 'matched') {
      const orderLabel = String(row.platformOrderId ?? row.fourSellerOrderId ?? '—');
      const shop = row.shopName?.trim();
      return (
        <Popover
          trigger="hover"
          placement="left"
          mouseEnterDelay={0.2}
          title="Đơn sàn"
          content={
            <Descriptions size="small" column={1} style={{ maxWidth: 280 }}>
              <Descriptions.Item label="Mã đơn">{orderLabel}</Descriptions.Item>
              <Descriptions.Item label="Shop">{shop || '—'}</Descriptions.Item>
            </Descriptions>
          }
        >
          <Text
            type="secondary"
            ellipsis
            style={{ fontSize: 12, maxWidth: '100%', display: 'inline-block', cursor: 'default' }}
          >
            {shop ? `${orderLabel} · ${shop}` : orderLabel}
          </Text>
        </Popover>
      );
    }

    const label = row.skipReason
      ? SUPPLIER_PURCHASE_SKIP_REASONS[row.skipReason] ?? row.skipReason
      : 'Chưa có lý do';

    return (
      <Popover
        trigger="hover"
        placement="left"
        mouseEnterDelay={0.2}
        title="Lý do"
        content={
          <div style={{ maxWidth: 320 }}>
            <Text style={{ fontSize: 13 }}>{label}</Text>
            {row.skipReason ? (
              <div style={{ marginTop: 4 }}>
                <Text type="secondary" style={{ fontSize: 11 }}>
                  Mã: {row.skipReason}
                </Text>
              </div>
            ) : null}
            {row.skipEvidenceJson ? (
              <div style={{ marginTop: 8 }}>
                <SkipEvidence json={row.skipEvidenceJson} />
              </div>
            ) : null}
          </div>
        }
      >
        <Text
          ellipsis
          style={{ fontSize: 12, maxWidth: '100%', display: 'inline-block', cursor: 'default' }}
        >
          {label}
        </Text>
      </Popover>
    );
  };

  const actionsCell = (row: SupplierPurchase) => {
    if (!canManage) return null;
    const deleteBtn = (
      <Popconfirm
        title="Xóa lần mua này?"
        description={
          row.source === 'shein'
            ? 'Xóa khỏi hàng đợi và dòng chi sổ cái (nếu có). Lượt sync sau có thể ghi nhận lại cùng billno.'
            : 'Xóa hẳn lần mua nhập tay và dòng chi sổ cái (nếu có).'
        }
        okText="Xóa"
        okButtonProps={{ danger: true }}
        cancelText="Hủy"
        onConfirm={() => void deleteMutation.mutateAsync(row.id)}
      >
        <Button
          type="text"
          size="small"
          danger
          icon={<DeleteOutlined />}
          title="Xóa"
          loading={deleteMutation.isPending}
        />
      </Popconfirm>
    );

    return (
      <Space size={0} onClick={(e) => e.stopPropagation()}>
        {row.orderId ? (
          <Popconfirm
            title="Gỡ khỏi đơn hàng?"
            description="Giá mua và dòng chi trên sổ cái của đơn được tính lại ngay."
            okText="Gỡ"
            cancelText="Hủy"
            onConfirm={() => void detachMutation.mutateAsync(row.id)}
          >
            <Button
              type="text"
              size="small"
              icon={<DisconnectOutlined />}
              title="Gỡ khỏi đơn"
              loading={detachMutation.isPending}
            />
          </Popconfirm>
        ) : (
          <>
            <Button
              type="text"
              size="small"
              icon={<LinkOutlined />}
              title="Gắn vào đơn"
              onClick={() => setAttachTarget(row)}
            />
            <Button
              type="text"
              size="small"
              icon={<StopOutlined />}
              title={row.matchState === 'ignored' ? 'Trả về hàng đợi' : 'Bỏ qua'}
              loading={ignoreMutation.isPending}
              onClick={() =>
                void ignoreMutation.mutateAsync({
                  id: row.id,
                  ignored: row.matchState !== 'ignored',
                })
              }
            />
          </>
        )}
        {deleteBtn}
      </Space>
    );
  };

  const desktopColumns: ColumnsType<SupplierPurchase> = [
    {
      title: '#',
      key: 'index',
      width: 48,
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Tài khoản',
      key: 'account',
      width: 110,
      ellipsis: true,
      render: (_, row) => (
        <Text style={{ fontSize: 12 }} title={row.fulfilAccountName ?? undefined}>
          {row.fulfilAccountName ?? '—'}
        </Text>
      ),
    },
    {
      title: 'Đơn',
      key: 'purchase',
      width: 200,
      render: (_, row) => purchaseCell(row),
    },
    {
      title: 'Thanh toán',
      key: 'payment',
      width: 80,
      render: (_, row) => paymentCell(row),
    },
    {
      title: 'Tiền',
      key: 'money',
      width: 120,
      render: (_, row) => moneyCell(row),
    },
    {
      title: 'Người nhận',
      key: 'receiver',
      width: 160,
      render: (_, row) => receiverCell(row),
    },
    {
      title: 'Vận đơn',
      key: 'tracking',
      width: 150,
      render: (_, row) => trackingCell(row),
    },
    {
      title: (
        <Tooltip title="Người mua hàng — fulfil đã bấm Đã mua">
          <span>Fulfill</span>
        </Tooltip>
      ),
      key: 'paidBy',
      width: 100,
      ellipsis: true,
      render: (_, row) => paidByCell(row),
    },
    {
      title: 'Trạng thái',
      key: 'status',
      width: 100,
      render: (_, row) => supplierStatusCell(row),
    },
    {
      title: 'Ngày tạo',
      key: 'createdAt',
      width: 130,
      render: (_, row) => (
        <Text style={{ fontSize: 12 }}>{formatDateTimeVn(row.orderedAt ?? row.firstSeenAt)}</Text>
      ),
    },
    {
      title: 'Đơn sàn / Lý do',
      key: 'reason',
      width: 200,
      render: (_, row) => reasonCell(row),
    },
    {
      title: '',
      key: 'actions',
      width: canManage ? 108 : 40,
      fixed: 'right',
      render: (_, row) => actionsCell(row),
    },
  ];

  const mobileColumns: ColumnsType<SupplierPurchase> = [
    {
      title: 'Đơn',
      key: 'mobile',
      render: (_, row) => (
        <div className="fulfil-txn-card">
          <div className="fulfil-txn-card-top">
            <div style={{ minWidth: 0, flex: 1 }}>{purchaseCell(row)}</div>
            <span className="fulfil-txn-card-time">
              {formatDateTimeVn(row.orderedAt ?? row.firstSeenAt)}
            </span>
          </div>
          <div className="fulfil-txn-card-line" style={{ flexWrap: 'wrap', gap: 6 }}>
            {paymentCell(row)}
            {supplierStatusCell(row)}
            <Text strong style={{ fontSize: 13 }}>
              {money(row.supplierTotal, row.currency)}
            </Text>
          </div>
          <div className="fulfil-txn-card-line" style={{ whiteSpace: 'normal' }}>
            {receiverCell(row)}
          </div>
          {(row.trackingNo || row.warehouseTrackingNo) && (
            <div className="fulfil-txn-card-line">{trackingCell(row)}</div>
          )}
          {row.fulfilAccountName?.trim() ? (
            <div className="fulfil-txn-card-line">
              <Text type="secondary" style={{ fontSize: 12 }}>
                TK: {row.fulfilAccountName.trim()}
              </Text>
            </div>
          ) : null}
          {row.paidByUserName?.trim() ? (
            <div className="fulfil-txn-card-line">
              <Text type="secondary" style={{ fontSize: 12 }}>
                Fulfill: {row.paidByUserName.trim()}
              </Text>
            </div>
          ) : null}
          <div className="fulfil-txn-card-line" style={{ whiteSpace: 'normal' }}>
            {reasonCell(row)}
          </div>
          {canManage ? <div className="fulfil-txn-card-foot">{actionsCell(row)}</div> : null}
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Đơn chưa khớp"
        extra={
          <Space size={4}>
            {canBulkDelete && selectedRowKeys.length > 0 ? (
              <Popconfirm
                title={`Xóa ${selectedRowKeys.length} lần mua đã chọn?`}
                description="Xóa khỏi hàng đợi và dòng chi sổ cái (nếu có). Lượt sync sau có thể ghi nhận lại billno SHEIN."
                okText="Xóa"
                cancelText="Hủy"
                okButtonProps={{ danger: true, loading: bulkDeleteMutation.isPending }}
                onConfirm={() => void handleBulkDelete()}
              >
                <Button
                  danger
                  size="small"
                  icon={<DeleteOutlined />}
                  disabled={bulkDeleteMutation.isPending}
                  loading={bulkDeleteMutation.isPending}
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
          </Space>
        }
      />

      <Alert
        type={data && data.unmatchedCount > 0 ? 'warning' : 'info'}
        showIcon
        style={{ marginBottom: 12 }}
        title={
          data
            ? `${data.unmatchedCount} lần mua chưa gắn được đơn sàn · ${data.ignoredCount} đã bỏ qua`
            : 'Đang tải hàng đợi…'
        }
        description="Mọi mã đơn SHEIN đều được ghi nhận trước khi chạy khớp, nên một lần khớp trượt vẫn để lại dấu vết ở đây. Gắn tay xong thì giá mua và sổ cái của đơn được tính lại ngay."
      />

      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          allowClear
          placeholder="Tìm mã NCC / tên / điện thoại / mã bưu chính / mã vận đơn…"
          style={{ width: isMobile ? '100%' : 300 }}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onSearch={(v) => {
            setSearchApplied(v.trim());
            resetPage();
          }}
        />
        <div className="admin-filters-grid">
          <AdminDateRangeFilter
            variant="grid"
            allowClear
            value={range}
            presetOptions={[...SP_DATE_PRESETS]}
            presetValue={datePreset}
            onChange={applyDateRange}
            onPresetChange={(preset) => {
              const next = preset as DatePreset;
              setDatePreset(next);
              applyDateRange(next ? rangeForPreset(next) : null);
            }}
          />
          <Select
            size="small"
            allowClear
            placeholder="Trạng thái"
            style={{ width: isMobile ? '100%' : 140 }}
            options={MATCH_STATE_OPTIONS}
            value={matchState}
            onChange={(v) => {
              setMatchState(v);
              resetPage();
            }}
          />
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Lý do bỏ qua"
            style={{ width: isMobile ? '100%' : 260 }}
            options={SKIP_REASON_OPTIONS}
            value={skipReason}
            onChange={(v) => {
              setSkipReason(v);
              resetPage();
            }}
          />
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Tài khoản mua hàng"
            style={{ width: isMobile ? '100%' : 180 }}
            options={accountOptions}
            value={accountId}
            onChange={(v) => {
              setAccountId(v);
              resetPage();
            }}
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
              value={paidByUserId}
              onChange={(v) => {
                setPaidByUserId(v);
                resetPage();
              }}
            />
          ) : null}
        </div>
      </div>

      <PageContentSpin spinning={isLoading} tip="Đang tải đơn NCC…">
        <Table
          className={isMobile ? 'fulfil-txn-table fulfil-txn-mobile-table' : 'fulfil-txn-table'}
          rowKey="id"
          size="small"
          loading={false}
          columns={isMobile ? mobileColumns : desktopColumns}
          dataSource={rows}
          showHeader={!isMobile}
          rowSelection={
            canBulkDelete
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
          locale={{ emptyText: 'Không có đơn NCC khớp bộ lọc' }}
          pagination={tablePagination(total, {
            ...pagination,
            total,
            size: 'small',
            simple: isMobile,
            showSizeChanger: !isMobile,
            pageSizeOptions: PAGE_SIZE_OPTIONS.map(String),
            showTotal: isMobile ? undefined : (t) => `${t} lần mua`,
            onChange: (current, pageSize) => {
              setPagination({ current, pageSize });
              setSelectedRowKeys([]);
            },
          })}
          scroll={isMobile ? undefined : { x: TABLE_SCROLL_X }}
          tableLayout={isMobile ? 'fixed' : undefined}
          styles={
            isMobile
              ? { body: { cell: { paddingBlock: 0, paddingInline: 0 } } }
              : {
                  body: {
                    cell: {
                      paddingBlock: 6,
                      paddingInline: 8,
                      verticalAlign: 'top',
                    },
                  },
                }
          }
        />
      </PageContentSpin>

      <Modal
        title={`Gắn ${attachTarget?.supplierOrderNo ?? 'đơn NCC'} vào đơn hàng`}
        open={attachTarget !== null}
        onCancel={closeAttach}
        onOk={() => void handleAttach()}
        okText="Gắn"
        cancelText="Hủy"
        okButtonProps={{
          disabled: !attachOrderId || (attachNeedsAccount && !attachAccountId),
          loading: attachMutation.isPending,
        }}
      >
        <Space direction="vertical" style={{ width: '100%' }} size={8}>
          {attachTarget ? (
            <Descriptions size="small" column={1} bordered>
              <Descriptions.Item label="Người nhận">
                {attachTarget.receiverName ?? '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Điện thoại / mã bưu chính">
                {attachTarget.receiverPhone ?? '—'} · {attachTarget.receiverPostalCode ?? '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Tổng tiền">
                {money(attachTarget.supplierTotal, attachTarget.currency)}
              </Descriptions.Item>
              <Descriptions.Item label="Tài khoản mua hàng">
                {attachTarget.fulfilAccountName ?? (
                  <Text type="warning">Chưa xác định — chọn bên dưới</Text>
                )}
              </Descriptions.Item>
            </Descriptions>
          ) : null}
          {attachNeedsAccount ? (
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="Tài khoản mua hàng (bắt buộc)"
              style={{ width: '100%' }}
              options={accountOptions}
              value={attachAccountId}
              onChange={setAttachAccountId}
            />
          ) : null}
          <Select
            showSearch
            allowClear
            placeholder="Gõ ít nhất 3 ký tự mã đơn / tên khách để tìm"
            style={{ width: '100%' }}
            filterOption={false}
            options={orderOptions}
            value={attachOrderId}
            loading={orderLookupLoading}
            onSearch={setOrderSearch}
            onChange={setAttachOrderId}
            notFoundContent={
              orderSearch.trim().length < 3
                ? 'Gõ thêm để tìm…'
                : orderLookupLoading
                  ? 'Đang tìm…'
                  : 'Không tìm thấy đơn.'
            }
          />
        </Space>
      </Modal>
    </div>
  );
}

export default SupplierPurchasesPage;
