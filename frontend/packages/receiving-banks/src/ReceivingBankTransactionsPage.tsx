import { useMemo, useState, type ReactNode } from 'react';
import {
  Button,
  DatePicker,
  Dropdown,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  type MenuProps,
} from 'antd';
import {
  CheckOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  PlusOutlined,
  ReloadOutlined,
  SwapOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { type Dayjs } from 'dayjs';
import { Modal, PageHeader, PlatformAvatar, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import type {
  ReceivingBankTransaction,
  TikTokPayoutOrderDetail,
} from '@drop/types/receivingBank';
import {
  formatReceivingBankOptionLabel,
  isReceivingBankPayoutCardMismatch,
  RECEIVING_BANK_CONFIRMATION,
  receivingBankConfirmationColor,
  receivingBankConfirmationLabel,
} from '@drop/types/receivingBank';
import { SHOP_STATUS_COLORS, SHOP_STATUS_LABELS } from '@drop/types/shop';
import {
  DATETIME_FORMAT,
  DATETIME_FORMAT_SHORT,
  DATE_FORMAT,
  dayjs,
  formatDateTimeVn,
  nowVn,
  parseToVnDayjs,
  parseUtcInstant,
  rangeForPresetVn,
  toUtcIsoFromTo,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { ReceivingBanksPortalConfig } from './portalConfig';
import {
  formatReceivingBanksShopLabel,
  useReceivingBanksDeps,
} from './ReceivingBanksDepsContext';
import './receivingBanks.css';

const { Text } = Typography;

type DatePreset = '' | 'day' | 'week' | 'month' | '30d';

const RB_DATE_PRESETS = [
  { value: 'day', label: 'Ngày' },
  { value: 'week', label: 'Tuần' },
  { value: 'month', label: 'Tháng' },
  { value: '30d', label: '30 ngày' },
] as const;

const rangeForPreset = (preset: Exclude<DatePreset, ''>): [Dayjs, Dayjs] =>
  rangeForPresetVn(preset);

const matchRbDatePreset = (value: [Dayjs | null, Dayjs | null] | null): DatePreset => {
  if (!value?.[0] || !value?.[1]) return '';
  const [from, to] = value;
  for (const preset of ['day', 'week', 'month', '30d'] as const) {
    const [pFrom, pTo] = rangeForPreset(preset);
    if (from.isSame(pFrom, 'minute') && to.isSame(pTo, 'minute')) return preset;
  }
  return '';
};

/** TikTok Shop US finance wall-clock (statement date). */
const TTS_STATEMENT_TZ = 'America/Los_Angeles';

interface TxnFormValues {
  shopId: string;
  direction: 'Debit' | 'Credit';
  amount: number;
  currency?: string;
  externalTransactionId?: string;
  note?: string;
  occurredAt: Dayjs;
}

const DIRECTION_OPTIONS = [
  { value: 'Credit' as const, label: 'Tiền về' },
  { value: 'Debit' as const, label: 'Điều chỉnh trừ' },
];

const formatTxnAmount = (amount: number, direction?: string) => {
  const abs = Math.abs(Number(amount) || 0).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  const isCredit = direction !== 'Debit';
  return { isCredit, text: `${isCredit ? '+' : '-'}$${abs}` };
};

const AmountText = ({ amount, direction }: { amount: number; direction?: string }) => {
  const { isCredit, text } = formatTxnAmount(amount, direction);
  return (
    <Text
      strong
      className={isCredit ? 'rb-amount-credit' : 'rb-amount-debit'}
      style={{
        fontVariantNumeric: 'tabular-nums',
        whiteSpace: 'nowrap',
      }}
    >
      {text}
    </Text>
  );
};

const ConfirmationTag = ({ status, compact }: { status?: string; compact?: boolean }) => (
  <Tag color={receivingBankConfirmationColor(status)} className={compact ? 'rb-tag-compact' : undefined}>
    {receivingBankConfirmationLabel(status)}
  </Tag>
);

const PAYOUT_MISMATCH_HINT = 'Đuôi thẻ TTS không khớp thẻ hệ thống.';

const payoutMismatchHint = (row: ReceivingBankTransaction) =>
  isReceivingBankPayoutCardMismatch(row) ? PAYOUT_MISMATCH_HINT : undefined;

const SourceTag = ({ source, compact }: { source?: string; compact?: boolean }) => {
  const className = compact ? 'rb-tag-compact' : undefined;
  if (source === 'SettlementPaid') {
    return (
      <Tag color="green" className={className}>
        {compact ? 'Đã TT' : 'Đã thanh toán'}
      </Tag>
    );
  }
  if (source === 'TikTokPayout') {
    return (
      <Tag color="cyan" className={className}>
        {compact ? 'TTS' : 'TTS Payout'}
      </Tag>
    );
  }
  return <Tag className={className}>Thủ công</Tag>;
};

/** Ghi chú TTS sync thường lặp shop + payout ID + mask thẻ — đã hiện ở header/chân thẻ. */
const isRedundantPayoutNote = (note: string, row: ReceivingBankTransaction) => {
  const id = row.externalTransactionId?.trim();
  const hasCardMask = /\bcard\s+\*{4,}\d+/i.test(note);
  if (id && note.includes(id) && (hasCardMask || /payout id/i.test(note) || /^tiktok payout\b/i.test(note))) {
    return true;
  }
  if (hasCardMask) {
    return note.replace(/\s*·?\s*card\s+\*{4,}\d+/i, '').trim().length === 0;
  }
  return false;
};

const formatMoneyCell = (v?: number | null) => {
  if (v == null || !Number.isFinite(Number(v))) return '—';
  return `$${Math.abs(Number(v)).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
};

/** Statement date from TikTok `bill_period` (Pacific), not VN settlement event time. */
const formatStatementDate = (raw?: string | null) => {
  if (!raw) return '—';
  const n = Number(raw);
  const d =
    Number.isFinite(n) && n > 0
      ? dayjs(n > 1e12 ? n : n * 1000)
      : parseUtcInstant(raw);
  if (!d.isValid()) return '—';
  return d.tz(TTS_STATEMENT_TZ).format(DATE_FORMAT);
};

const receivingBankTxnSearchHaystack = (row: ReceivingBankTransaction) => {
  const sourceLabel =
    row.source === 'SettlementPaid'
      ? 'Đã thanh toán SettlementPaid'
      : row.source === 'TikTokPayout'
        ? 'TTS Payout TikTokPayout'
        : 'Thủ công Manual';
  const { text: amountText } = formatTxnAmount(row.amount, row.direction);
  const payoutOrderIds = (row.payoutOrders ?? []).map((o) => o.tradeOrderId);
  return [
    row.sourceName,
    row.receivingBankAccountNumberMasked,
    row.receivingBankAccountNumber,
    row.receivingBankAccountName,
    row.sourceAccountName,
    row.accountHolderName,
    row.shopProfileName,
    row.shopName,
    row.shopOwnerName,
    row.externalTransactionId,
    row.note,
    row.platformOrderId,
    row.currency,
    row.source,
    sourceLabel,
    receivingBankConfirmationLabel(row.confirmationStatus),
    String(row.amount),
    amountText,
    formatDateTimeVn(row.occurredAt),
    ...payoutOrderIds,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
};

const matchesReceivingBankTxnSearch = (row: ReceivingBankTransaction, keyword: string) =>
  receivingBankTxnSearchHaystack(row).includes(keyword.trim().toLowerCase());

export type SharedReceivingBankTransactionsPageProps = {
  config: ReceivingBanksPortalConfig;
};

export const ReceivingBankTransactionsPage = ({
  config,
}: SharedReceivingBankTransactionsPageProps) => {
  const deps = useReceivingBanksDeps();
  const canManage =
    config.enableManage && deps.hasPermission(config.managePermission);
  const canConfirm =
    config.enableConfirm && deps.hasPermission(config.managePermission);
  const isSeller = config.portal === 'seller';
  const isMobile = useIsMobile();

  const [filterCardId, setFilterCardId] = useState<string | undefined>();
  const [filterSellerId, setFilterSellerId] = useState<string | undefined>();
  const [filterShopId, setFilterShopId] = useState<string | undefined>(() => {
    try {
      return new URLSearchParams(window.location.search).get('shopId') || undefined;
    } catch {
      return undefined;
    }
  });
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [datePreset, setDatePreset] = useState<DatePreset>('');
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');
  const [pagination, setPagination] = useState({ current: 1, pageSize: 50 });

  const showSellerFilter = config.showSellerColumn;

  const query = useMemo(() => {
    const { from, to } = toUtcIsoFromTo(range?.[0], range?.[1]);
    return {
      receivingBankAccountId: filterCardId,
      shopId: filterShopId,
      ownerUserId: showSellerFilter ? filterSellerId : undefined,
      from,
      to,
    };
  }, [filterCardId, filterShopId, filterSellerId, range, showSellerFilter]);

  const { data: rows = [], isLoading, isFetching, refetch } = deps.useTransactions(query);

  const filteredRows = useMemo(() => {
    const kw = searchApplied.trim();
    if (!kw) return rows;
    return rows.filter((row) => matchesReceivingBankTxnSearch(row, kw));
  }, [rows, searchApplied]);
  const { data: cardOptions = [] } = deps.useAccountOptions(true);
  const { data: shops = [] } = deps.useShopOptions();
  const { data: sellers = [] } = deps.useSellers?.() ?? { data: [] };
  const { data: platforms = [] } = deps.usePlatformOptions(true);

  const createTxn = deps.useCreateTransaction?.();
  const updateTxn = deps.useUpdateTransaction?.();
  const deleteTxn = deps.useDeleteTransaction?.();
  const setStatus = deps.useSetConfirmationStatus?.();

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ReceivingBankTransaction | null>(null);
  const [detailTxn, setDetailTxn] = useState<ReceivingBankTransaction | null>(null);
  const [form] = Form.useForm<TxnFormValues>();

  const cardFilterOptions = useMemo(
    () =>
      cardOptions.map((c) => ({
        value: c.id,
        label: formatReceivingBankOptionLabel(c),
      })),
    [cardOptions],
  );

  const sellerFilterOptions = useMemo(
    () =>
      sellers.map((s) => ({
        value: s.id,
        label: s.fullName?.trim() || s.userName || s.email || s.id,
      })),
    [sellers],
  );

  const allShopOptions = useMemo(() => {
    const cardById = new Map(cardOptions.map((c) => [c.id, c]));
    return shops.map((s) => {
      const card = s.receivingBankAccountId ? cardById.get(s.receivingBankAccountId) : undefined;
      return {
        value: s.id,
        ownerId: s.ownerId ?? undefined,
        label: formatReceivingBanksShopLabel({
          ...s,
          accountNumber: card?.accountNumber,
        }),
        platformCode: s.platformCode,
        status: s.status,
      };
    });
  }, [shops, cardOptions]);

  const shopFilterOptions = useMemo(() => {
    if (!showSellerFilter || !filterSellerId) return allShopOptions;
    return allShopOptions.filter((s) => s.ownerId === filterSellerId);
  }, [allShopOptions, filterSellerId, showSellerFilter]);

  const renderShopOption = (
    platformCode?: string | null,
    label?: ReactNode,
    status?: string | null,
  ) => (
    <span className="rb-shop-option">
      <span className="rb-shop-option-avatar">
        <PlatformAvatar platformCode={platformCode} platforms={platforms} size={16} />
      </span>
      <span className="rb-shop-option-label">{label}</span>
      {status ? (
        <Tag
          color={SHOP_STATUS_COLORS[status] ?? 'default'}
          className="rb-shop-option-status"
        >
          {SHOP_STATUS_LABELS[status] ?? status}
        </Tag>
      ) : null}
    </span>
  );

  const canEditRow = (row: ReceivingBankTransaction) => {
    if (!canManage) return false;
    if (isSeller) {
      return row.source === 'Manual' && row.confirmationStatus === 'Pending';
    }
    // Admin: TikTokPayout note-only; SettlementPaid/Manual editable.
    return true;
  };

  const canDeleteRow = (row: ReceivingBankTransaction) => {
    if (!canManage || !deleteTxn) return false;
    if (isSeller) {
      return row.source === 'Manual' && row.confirmationStatus === 'Pending';
    }
    return true;
  };

  const sellerLockedReason = (row: ReceivingBankTransaction) => {
    if (row.confirmationStatus === 'Confirmed') {
      return 'Đã duyệt — chỉ Admin được sửa/xóa';
    }
    if (row.confirmationStatus === 'Rejected') {
      return 'Không duyệt — chỉ Admin được sửa/xóa';
    }
    if (row.source === 'SettlementPaid') {
      return 'Giao dịch từ Đã thanh toán — chỉ Admin được sửa/xóa';
    }
    if (row.source === 'TikTokPayout') {
      return 'Giao dịch sync TTS Payout — chỉ Admin được sửa/xóa';
    }
    return 'Không thể sửa/xóa';
  };

  const isTikTokPayout = (row: ReceivingBankTransaction | null) =>
    row?.source === 'TikTokPayout';

  const isSettlementPaid = (row: ReceivingBankTransaction | null) =>
    row?.source === 'SettlementPaid';

  const payoutLockedFields = isTikTokPayout(editing);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({
      shopId: filterShopId,
      direction: 'Credit',
      currency: 'USD',
      occurredAt: nowVn(),
      amount: 0,
    });
    setModalOpen(true);
  };

  const openEdit = (row: ReceivingBankTransaction) => {
    setEditing(row);
    form.setFieldsValue({
      shopId: row.shopId ?? undefined,
      direction: row.direction === 'Debit' ? 'Debit' : 'Credit',
      amount: row.amount,
      currency: row.currency,
      externalTransactionId: row.externalTransactionId ?? undefined,
      note: row.note ?? undefined,
      occurredAt: parseToVnDayjs(row.occurredAt) ?? nowVn(),
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    if (!createTxn || !updateTxn) return;
    const values = await form.validateFields();
    const payload = {
      shopId: values.shopId,
      direction: (isSeller ? 'Credit' : values.direction || 'Credit') as 'Debit' | 'Credit',
      amount: values.amount,
      currency: values.currency || 'USD',
      externalTransactionId: values.externalTransactionId?.trim() || null,
      note: values.note || null,
      occurredAt: values.occurredAt.toISOString(),
    };
    if (editing) {
      await updateTxn.mutateAsync({
        id: editing.id,
        data: { ...payload, orderId: editing.orderId },
      });
    } else {
      await createTxn.mutateAsync(payload);
    }
    setModalOpen(false);
  };

  const renderActions = (record: ReceivingBankTransaction) => {
    const editEnabled = canEditRow(record);
    const deleteEnabled = canDeleteRow(record);
    const canSwitchStatus =
      canConfirm &&
      setStatus &&
      (record.source === 'Manual' || record.source === 'TikTokPayout');
    const currentStatus =
      record.confirmationStatus === RECEIVING_BANK_CONFIRMATION.Pending
      || record.confirmationStatus === RECEIVING_BANK_CONFIRMATION.Rejected
        ? record.confirmationStatus
        : RECEIVING_BANK_CONFIRMATION.Confirmed;
    const statusLabel = (status: typeof currentStatus) => (
      <span className={`rb-txn-status-item is-${receivingBankConfirmationColor(status)}`}>
        <CheckOutlined className={currentStatus === status ? 'is-on' : 'is-off'} />
        {receivingBankConfirmationLabel(status)}
      </span>
    );
    const statusItems: MenuProps['items'] = [
      { key: RECEIVING_BANK_CONFIRMATION.Pending, label: statusLabel(RECEIVING_BANK_CONFIRMATION.Pending) },
      { key: RECEIVING_BANK_CONFIRMATION.Confirmed, label: statusLabel(RECEIVING_BANK_CONFIRMATION.Confirmed) },
      { key: RECEIVING_BANK_CONFIRMATION.Rejected, label: statusLabel(RECEIVING_BANK_CONFIRMATION.Rejected) },
    ];
    const lockedHint = isSeller && (!editEnabled || !deleteEnabled) ? sellerLockedReason(record) : undefined;
    const showDetail = record.source === 'TikTokPayout';

    if (!canManage && !canConfirm && !showDetail) return null;

    return (
      <Space size={0} className="rb-txn-actions" onClick={(e) => e.stopPropagation()}>
        {showDetail ? (
          <Button
            type="text"
            size="small"
            icon={<EyeOutlined />}
            title="Chi tiết payout"
            onClick={() => setDetailTxn(record)}
          />
        ) : null}
        {canSwitchStatus ? (
          <Dropdown
            trigger={['click']}
            overlayClassName="rb-txn-status-menu"
            menu={{
              items: statusItems,
              onClick: ({ key, domEvent }) => {
                domEvent.stopPropagation();
                if (key === currentStatus) return;
                setStatus?.mutate({
                  id: record.id,
                  confirmationStatus: key as 'Pending' | 'Confirmed' | 'Rejected',
                });
              },
            }}
          >
            <Button
              type="text"
              size="small"
              icon={<SwapOutlined />}
              title={`Đổi trạng thái · ${receivingBankConfirmationLabel(currentStatus)}`}
              className={`rb-txn-status-swap is-${receivingBankConfirmationColor(currentStatus)}`}
            />
          </Dropdown>
        ) : null}
        {canManage ? (
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            title={editEnabled ? 'Sửa' : lockedHint}
            disabled={!editEnabled}
            onClick={() => {
              if (editEnabled) openEdit(record);
            }}
          />
        ) : null}
        {canManage && deleteTxn ? (
          <Popconfirm
            title={
              record.source === 'SettlementPaid'
                ? 'Xóa giao dịch và đánh dấu đơn Chưa thanh toán?'
                : record.source === 'TikTokPayout'
                  ? 'Xóa TTS Payout và gỡ Đã thanh toán các đơn gắn payout này?'
                  : 'Xóa giao dịch này?'
            }
            disabled={!deleteEnabled}
            onConfirm={() => deleteTxn.mutate(record.id)}
          >
            <Button
              type="text"
              size="small"
              danger
              icon={<DeleteOutlined />}
              title={deleteEnabled ? 'Xóa' : lockedHint}
              disabled={!deleteEnabled}
            />
          </Popconfirm>
        ) : null}
      </Space>
    );
  };

  const desktopColumns: ColumnsType<ReceivingBankTransaction> = [
    {
      title: '#',
      key: 'index',
      width: 44,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Thời gian',
      dataIndex: 'occurredAt',
      key: 'occurredAt',
      width: 122,
      render: (v: string) => formatDateTimeVn(v),
    },
    {
      title: 'Số tiền',
      dataIndex: 'amount',
      key: 'amount',
      width: 84,
      align: 'right',
      render: (amount: number, row) => <AmountText amount={amount} direction={row.direction} />,
    },
    {
      title: 'Thẻ',
      key: 'card',
      width: 150,
      ellipsis: true,
      onCell: () => ({ style: { whiteSpace: 'nowrap', maxWidth: 150 } }),
      render: (_, row) => {
        const source = row.sourceName?.trim() || null;
        const masked =
          row.receivingBankAccountNumberMasked || row.receivingBankAccountNumber || null;
        const label = [source, masked].filter(Boolean).join(' ') || '—';
        const mismatchHint = payoutMismatchHint(row);
        return (
          <Text
            ellipsis={{ tooltip: mismatchHint ? `${label} · ${mismatchHint}` : label !== '—' ? label : undefined }}
            type={mismatchHint ? 'warning' : undefined}
            style={{ fontWeight: 500, fontVariantNumeric: 'tabular-nums', maxWidth: '100%' }}
          >
            {label}
          </Text>
        );
      },
    },
    ...(config.showSellerColumn
      ? [
          {
            title: 'Seller',
            dataIndex: 'shopOwnerName',
            key: 'shopOwnerName',
            width: 96,
            ellipsis: true,
            render: (v: string | undefined) => (
              <Text ellipsis={{ tooltip: v || undefined }} style={{ maxWidth: '100%' }}>
                {v || '—'}
              </Text>
            ),
          } satisfies ColumnsType<ReceivingBankTransaction>[number],
        ]
      : []),
    {
      title: 'Shop',
      key: 'shop',
      width: 140,
      ellipsis: true,
      render: (_, row) => {
        const label = [row.shopProfileName?.trim(), row.shopName?.trim()].filter(Boolean).join(' - ');
        return (
          <Text ellipsis={{ tooltip: label || undefined }} style={{ maxWidth: '100%' }}>
            {label || '—'}
          </Text>
        );
      },
    },
    {
      title: 'Loại',
      dataIndex: 'source',
      key: 'source',
      width: 100,
      render: (source: string) => <SourceTag source={source} />,
    },
    {
      title: 'Transaction',
      dataIndex: 'externalTransactionId',
      key: 'externalTransactionId',
      width: 112,
      ellipsis: true,
      render: (v) => (
        <Text
          ellipsis={{ tooltip: v || undefined }}
          style={{ maxWidth: '100%', fontVariantNumeric: 'tabular-nums' }}
        >
          {v || '—'}
        </Text>
      ),
    },
    {
      title: 'Ghi chú',
      dataIndex: 'note',
      key: 'note',
      width: 260,
      ellipsis: true,
      render: (v, row) => {
        const mismatchHint = payoutMismatchHint(row);
        return (
          <Text
            ellipsis={{
              tooltip: mismatchHint ? `${v || '—'} · ${mismatchHint}` : v || undefined,
            }}
            type={mismatchHint ? 'warning' : undefined}
            style={{ maxWidth: '100%' }}
          >
            {v || '—'}
          </Text>
        );
      },
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 120,
      fixed: 'right' as const,
      render: (_: unknown, record: ReceivingBankTransaction) => renderActions(record),
    },
  ];

  const resetPage = () => setPagination((p) => ({ ...p, current: 1 }));

  const applyDateRange = (next: [Dayjs, Dayjs] | null) => {
    setRange(next);
    setDatePreset(matchRbDatePreset(next));
    resetPage();
  };

  const mobileColumns: ColumnsType<ReceivingBankTransaction> = [
    {
      title: 'Giao dịch',
      key: 'mobile',
      render: (_, row) => {
        const source = row.sourceName?.trim() || null;
        const masked = row.receivingBankAccountNumberMasked || row.receivingBankAccountNumber || null;
        const cardLabel = [source, masked].filter(Boolean).join(' ') || null;
        const shopLabel = [row.shopProfileName?.trim(), row.shopName?.trim()].filter(Boolean).join(' · ');
        const shopLine = [shopLabel || null, config.showSellerColumn ? row.shopOwnerName?.trim() || null : null]
          .filter(Boolean)
          .join(' · ');
        const txnId = row.externalTransactionId?.trim() || null;
        const metaLine = [cardLabel, txnId].filter(Boolean).join(' · ') || null;
        const note = row.note?.trim() || null;
        const extraNote = note && !isRedundantPayoutNote(note, row) ? note : null;
        const mismatchHint = payoutMismatchHint(row);

        return (
          <div className={`rb-txn-card${isReceivingBankPayoutCardMismatch(row) ? ' is-mismatch' : ''}`}>
            <div className="rb-txn-card-top">
              <AmountText amount={row.amount} direction={row.direction} />
              {shopLine ? (
                <Text ellipsis={{ tooltip: shopLine }} className="rb-txn-card-shop">
                  {shopLine}
                </Text>
              ) : null}
              <Text type="secondary" className="rb-txn-card-time">
                {formatDateTimeVn(row.occurredAt, DATETIME_FORMAT_SHORT)}
              </Text>
            </div>
            {extraNote ? (
              <Text type="secondary" ellipsis={{ tooltip: extraNote }} className="rb-txn-card-note">
                {extraNote}
              </Text>
            ) : null}
            <div className="rb-txn-card-foot">
              <div className="rb-txn-card-info">
                {metaLine ? (
                  <Text
                    ellipsis={{ tooltip: mismatchHint ? `${metaLine} · ${mismatchHint}` : metaLine }}
                    type={mismatchHint ? 'warning' : undefined}
                    className="rb-txn-card-meta"
                  >
                    {metaLine}
                  </Text>
                ) : null}
                <span className="rb-txn-card-tags">
                  <SourceTag source={row.source} compact />
                  <ConfirmationTag status={row.confirmationStatus} compact />
                </span>
              </div>
              {renderActions(row)}
            </div>
          </div>
        );
      },
    },
  ];

  return (
    <div className={isMobile ? 'rb-txn-page is-mobile' : 'rb-txn-page'}>
      <PageHeader
        title="Lịch sử tiền về"
        extra={
          <Space size={4}>
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
        <Input.Search
          className="admin-filters-search"
          size="small"
          allowClear
          placeholder="Thẻ · shop · seller · transaction · ghi chú · số tiền"
          style={{ width: isMobile ? '100%' : 320 }}
          value={search}
          onChange={(e) => {
            const next = e.target.value;
            setSearch(next);
            if (!next.trim()) {
              setSearchApplied('');
              resetPage();
            }
          }}
          onSearch={(value) => {
            setSearchApplied(value.trim());
            resetPage();
          }}
        />
        <div className="admin-filters-grid">
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Thẻ nhận tiền"
            style={{ width: isMobile ? '100%' : 220 }}
            options={cardFilterOptions}
            value={filterCardId}
            onChange={(v) => {
              setFilterCardId(v);
              resetPage();
            }}
          />
          {showSellerFilter ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 180 }}
              options={sellerFilterOptions}
              value={filterSellerId}
              onChange={(v) => {
                setFilterSellerId(v);
                if (v && filterShopId) {
                  const shop = shops.find((s) => s.id === filterShopId);
                  if (!shop?.ownerId || shop.ownerId !== v) {
                    setFilterShopId(undefined);
                  }
                }
                resetPage();
              }}
            />
          ) : null}
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            className="rb-shop-select"
            classNames={{ popup: { root: 'rb-shop-select-dropdown' } }}
            style={{ width: isMobile ? '100%' : 220 }}
            options={shopFilterOptions}
            value={filterShopId}
            onChange={(v) => {
              setFilterShopId(v);
              resetPage();
            }}
            optionRender={(option) =>
              renderShopOption(
                (option.data as { platformCode?: string; status?: string }).platformCode,
                option.data.label,
                (option.data as { status?: string }).status,
              )
            }
            labelRender={(props) => {
              const hit = shopFilterOptions.find((o) => o.value === props.value);
              return renderShopOption(
                hit?.platformCode,
                props.label,
                isMobile ? null : hit?.status,
              );
            }}
          />
          <AdminDateRangeFilter
            variant="grid"
            allowClear
            value={range}
            presetOptions={[...RB_DATE_PRESETS]}
            presetValue={datePreset}
            onChange={applyDateRange}
            onPresetChange={(preset) => {
              const next = preset as DatePreset;
              setDatePreset(next);
              applyDateRange(next ? rangeForPreset(next) : null);
            }}
          />
        </div>
      </div>

      <Table
        className={isMobile ? 'rb-txn-mobile-table' : undefined}
        rowKey="id"
        size="small"
        loading={isLoading}
        columns={isMobile ? mobileColumns : desktopColumns}
        dataSource={filteredRows}
        showHeader={!isMobile}
        rowClassName={(row) =>
          isReceivingBankPayoutCardMismatch(row) ? 'rb-txn-row-mismatch' : ''
        }
        onRow={(row) =>
          isReceivingBankPayoutCardMismatch(row)
            ? { title: PAYOUT_MISMATCH_HINT }
            : {}
        }
        pagination={tablePagination(filteredRows.length, {
          ...pagination,
          size: 'small',
          simple: isMobile,
          showSizeChanger: !isMobile,
          onChange: (current, pageSize) => setPagination({ current, pageSize }),
        })}
        scroll={isMobile ? undefined : { x: config.showSellerColumn ? 1320 : 1220 }}
        tableLayout={isMobile ? 'fixed' : undefined}
        styles={
          isMobile ? { body: { cell: { paddingBlock: 0, paddingInline: 0 } } } : undefined
        }
      />

      {canManage ? (
        <Modal
          title={editing ? 'Sửa giao dịch' : isSeller ? 'Thêm tiền về' : 'Thêm giao dịch thủ công'}
          open={modalOpen}
          onCancel={() => setModalOpen(false)}
          onOk={() => void handleSubmit()}
          okText="Lưu"
          cancelText="Hủy"
          confirmLoading={!!(createTxn?.isPending || updateTxn?.isPending)}
          destroyOnClose
          width={isMobile ? '100%' : 520}
          centered={!isMobile}
          className={`rb-edit-modal${isMobile ? ' is-mobile' : ''}`}
          styles={isMobile ? { body: { maxHeight: '70vh', overflowY: 'auto' } } : undefined}
        >
          {editing?.source === 'SettlementPaid' ? (
            <Text type="secondary" className="rb-edit-hint">
              Giao dịch tự động từ Đã thanh toán — có thể sửa số tiền / ghi chú / thời gian.
            </Text>
          ) : null}
          {editing?.source === 'TikTokPayout' ? (
            <Text type="secondary" className="rb-edit-hint">
              Giao dịch sync TTS Payout — chỉ sửa ghi chú. Số tiền / Transaction giữ từ TikTok.
            </Text>
          ) : null}
          {isSeller && !editing ? (
            <Text type="secondary" className="rb-edit-hint">
              {isMobile
                ? 'Chờ duyệt — Admin duyệt mới tính vào thống kê.'
                : 'Giao dịch sẽ ở trạng thái Chờ duyệt — Admin duyệt mới tính vào thống kê tiền về.'}
            </Text>
          ) : null}
          <Form form={form} layout="vertical" requiredMark={false} size="small">
            <Form.Item
              name="shopId"
              label="Shop"
              rules={[{ required: true, message: 'Chọn shop' }]}
              extra={isMobile ? undefined : 'Thẻ nhận tiền lấy từ shop đã gắn.'}
            >
              <Select
                size="middle"
                showSearch
                optionFilterProp="label"
                options={allShopOptions}
                disabled={isSettlementPaid(editing) || payoutLockedFields}
                className="rb-shop-select"
                classNames={{ popup: { root: 'rb-shop-select-dropdown' } }}
                optionRender={(option) =>
                  renderShopOption(
                    (option.data as { platformCode?: string; status?: string }).platformCode,
                    option.data.label,
                    (option.data as { status?: string }).status,
                  )
                }
                labelRender={(props) => {
                  const hit = allShopOptions.find((o) => o.value === props.value);
                  return renderShopOption(
                    hit?.platformCode,
                    props.label,
                    isMobile ? null : hit?.status,
                  );
                }}
              />
            </Form.Item>
            {!isSeller ? (
              <Form.Item
                name="direction"
                label="Loại giao dịch"
                rules={[{ required: true, message: 'Chọn loại giao dịch' }]}
              >
                <Select
                  options={DIRECTION_OPTIONS}
                  disabled={isSettlementPaid(editing) || payoutLockedFields}
                />
              </Form.Item>
            ) : null}
            <div className="rb-form-grid">
              <Form.Item
                name="amount"
                label="Số tiền về"
                rules={[{ required: true, message: 'Nhập số tiền' }]}
              >
                <InputNumber
                  min={0}
                  step={0.01}
                  precision={2}
                  style={{ width: '100%' }}
                  prefix="$"
                  disabled={payoutLockedFields}
                />
              </Form.Item>
              <Form.Item name="currency" label="Currency">
                <Input disabled={payoutLockedFields} />
              </Form.Item>
            </div>
            <div className="rb-form-grid">
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
                  disabled={payoutLockedFields}
                />
              </Form.Item>
              <Form.Item
                name="externalTransactionId"
                label="Transaction"
                rules={[{ max: 200, message: 'Tối đa 200 ký tự' }]}
              >
                <Input
                  placeholder="Payout / transfer id"
                  allowClear
                  disabled={payoutLockedFields}
                />
              </Form.Item>
            </div>
            <Form.Item name="note" label="Ghi chú">
              <Input.TextArea rows={2} />
            </Form.Item>
          </Form>
        </Modal>
      ) : null}

      <Modal
        title={
          detailTxn
            ? isMobile
              ? detailTxn.externalTransactionId || 'Chi tiết payout'
              : `Chi tiết TTS Payout${detailTxn.externalTransactionId ? ` · ${detailTxn.externalTransactionId}` : ''}`
            : 'Chi tiết TTS Payout'
        }
        open={!!detailTxn}
        onCancel={() => setDetailTxn(null)}
        footer={
          <Button type="primary" block={isMobile} onClick={() => setDetailTxn(null)}>
            Đóng
          </Button>
        }
        destroyOnClose
        width={isMobile ? '100%' : 720}
        centered={!isMobile}
        className={isMobile ? 'rb-payout-modal is-mobile' : undefined}
        styles={isMobile ? { body: { maxHeight: '70vh', overflowY: 'auto' } } : undefined}
      >
        {detailTxn ? (
          <PayoutOrdersDetail
            txn={detailTxn}
            orders={detailTxn.payoutOrders ?? []}
            compact={isMobile}
          />
        ) : null}
      </Modal>
    </div>
  );
};

function PayoutOrdersDetail({
  txn,
  orders,
  compact,
}: {
  txn: ReceivingBankTransaction;
  orders: TikTokPayoutOrderDetail[];
  compact?: boolean;
}) {
  if (!orders.length) {
    return (
      <Text type="secondary">
        Chưa có chi tiết đơn. Bấm Sync paid lại trên extension để lấy breakdown settlement / earning /
        fees.
      </Text>
    );
  }

  const sumSettlement = orders.reduce((s, o) => s + (Number(o.settlementAmount) || 0), 0);
  const sumEarning = orders.reduce((s, o) => s + (Number(o.earningAmount) || 0), 0);
  const sumFees = orders.reduce((s, o) => s + (Number(o.fees) || 0), 0);

  if (compact) {
    return (
      <div className="rb-payout-mobile">
        <div className="rb-payout-mobile-sum">
          <span>
            Bank <AmountText amount={txn.amount} direction={txn.direction} />
          </span>
          <span className="rb-payout-mobile-count">{orders.length} đơn</span>
        </div>
        <div className="rb-payout-mobile-totals">
          <span>
            <span className="rb-payout-metric-label">Settlement</span>
            <strong>{formatMoneyCell(sumSettlement)}</strong>
          </span>
          <span>
            <span className="rb-payout-metric-label">Giữ lại</span>
            <strong>{formatMoneyCell(sumEarning)}</strong>
          </span>
          <span>
            <span className="rb-payout-metric-label">Fees</span>
            <strong>{formatMoneyCell(sumFees)}</strong>
          </span>
        </div>
        {orders.map((o) => (
          <div key={o.tradeOrderId} className="rb-payout-order-card">
            <div className="rb-payout-order-head">
              <Text code className="rb-payout-order-id">
                {o.tradeOrderId}
              </Text>
              <Text type="secondary" className="rb-payout-order-date">
                {formatStatementDate(o.settlementTime)}
              </Text>
            </div>
            <div className="rb-payout-order-metrics">
              <span>
                <span className="rb-payout-metric-label">Settlement</span>
                {formatMoneyCell(o.settlementAmount)}
              </span>
              <span>
                <span className="rb-payout-metric-label">Giữ lại</span>
                {formatMoneyCell(o.earningAmount)}
              </span>
              <span>
                <span className="rb-payout-metric-label">Fees</span>
                {formatMoneyCell(o.fees)}
              </span>
            </div>
          </div>
        ))}
      </div>
    );
  }

  return (
    <div>
      <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
        Tiền bank (payout): <AmountText amount={txn.amount} direction={txn.direction} /> · {orders.length}{' '}
        đơn
      </Text>
      <Table
        size="small"
        rowKey={(r) => r.tradeOrderId}
        pagination={false}
        dataSource={orders}
        columns={[
          {
            title: 'Đơn TTS',
            dataIndex: 'tradeOrderId',
            key: 'tradeOrderId',
            ellipsis: true,
            render: (v: string) => (
              <Text code style={{ fontSize: 12 }}>
                {v}
              </Text>
            ),
          },
          {
            title: 'Settlement',
            dataIndex: 'settlementAmount',
            key: 'settlementAmount',
            width: 100,
            align: 'right' as const,
            render: (v: number | null | undefined) => formatMoneyCell(v),
          },
          {
            title: 'Giữ lại',
            dataIndex: 'earningAmount',
            key: 'earningAmount',
            width: 100,
            align: 'right' as const,
            render: (v: number | null | undefined) => formatMoneyCell(v),
          },
          {
            title: 'Fees',
            dataIndex: 'fees',
            key: 'fees',
            width: 90,
            align: 'right' as const,
            render: (v: number | null | undefined) => formatMoneyCell(v),
          },
          {
            title: 'Statement date',
            dataIndex: 'settlementTime',
            key: 'settlementTime',
            width: 120,
            render: (v: string | null | undefined) => (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {formatStatementDate(v)}
              </Text>
            ),
          },
        ]}
        summary={() => (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0}>
              <Text strong>Tổng</Text>
            </Table.Summary.Cell>
            <Table.Summary.Cell index={1} align="right">
              <Text strong>{formatMoneyCell(sumSettlement)}</Text>
            </Table.Summary.Cell>
            <Table.Summary.Cell index={2} align="right">
              <Text strong>{formatMoneyCell(sumEarning)}</Text>
            </Table.Summary.Cell>
            <Table.Summary.Cell index={3} align="right">
              <Text strong>{formatMoneyCell(sumFees)}</Text>
            </Table.Summary.Cell>
            <Table.Summary.Cell index={4} />
          </Table.Summary.Row>
        )}
      />
    </div>
  );
}
