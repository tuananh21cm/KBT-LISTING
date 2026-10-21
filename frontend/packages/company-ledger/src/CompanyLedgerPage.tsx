import { useMemo, useState, type ReactNode } from 'react';
import {
  Button,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Select,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { DashboardKpiStrip, money, type DashboardKpiItem } from '@drop/dashboard';
import '@drop/dashboard/dashboard.css';
import { AdminDateRangeFilter, PageContentSpin, PageHeader, useIsMobile } from '@drop/ui';
import {
  DATE_FORMAT,
  disabledFutureVnDate,
  formatDateTimeVn,
  nowVn,
  rangeForPresetVn,
  startOfVnDay,
  toUtcIsoFromTo,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import {
  COMPANY_LEDGER_DIRECTIONS,
  type CompanyLedgerEntry,
  type CompanyLedgerQuery,
  type SaveCompanyLedgerEntryRequest,
} from '@drop/types/companyLedger';
import { SHOP_STATUS_COLORS, SHOP_STATUS_LABELS } from '@drop/types/shop';
import { useCompanyLedgerDeps } from './CompanyLedgerDepsContext';
import type { CompanyLedgerPortalConfig } from './portalConfig';
import './companyLedger.css';

const UNASSIGNED = '__none__';

const formatMoney = (amount: number, currency = 'USD') => {
  if (currency === 'VND') {
    return `${Math.round(amount).toLocaleString('vi-VN')} ₫`;
  }
  return money(amount);
};

const personLabel = (fullName?: string | null, userName?: string | null) => {
  if (fullName && userName && fullName !== userName) return `${fullName} (${userName})`;
  return fullName || userName || '—';
};

const shopLabel = (shop: { name: string; profileName?: string | null }) =>
  shop.profileName ? `${shop.profileName} (${shop.name})` : shop.name;

const renderShopOption = (label?: ReactNode, status?: string | null) => (
  <span className="company-ledger-shop-option">
    <span className="company-ledger-shop-option-label">{label}</span>
    {status ? (
      <Tag color={SHOP_STATUS_COLORS[status] ?? 'default'} className="company-ledger-shop-option-status">
        {SHOP_STATUS_LABELS[status] ?? status}
      </Tag>
    ) : null}
  </span>
);

type FormValues = {
  direction: string;
  categoryCode: string;
  amount: number;
  currency: string;
  title: string;
  note?: string;
  occurredAt: Dayjs;
  shopId?: string;
  ownerUserId?: string;
};

export type SharedCompanyLedgerPageProps = {
  config: CompanyLedgerPortalConfig;
};

export const CompanyLedgerPage = ({ config }: SharedCompanyLedgerPageProps) => {
  const deps = useCompanyLedgerDeps();
  deps.useThemeMode?.();
  const isMobile = useIsMobile();
  const canManage = config.enableManage && deps.hasPermission(config.managePermission);

  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(() => rangeForPresetVn('30d'));
  const [direction, setDirection] = useState<string | undefined>();
  const [categoryCode, setCategoryCode] = useState<string | undefined>();
  const [ownerFilter, setOwnerFilter] = useState<string | undefined>();
  const [shopFilter, setShopFilter] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState({ current: 1, pageSize: 50 });
  const [editing, setEditing] = useState<CompanyLedgerEntry | null | undefined>(undefined);
  const [form] = Form.useForm<FormValues>();

  const query = useMemo((): CompanyLedgerQuery => {
    const { from, to } = toUtcIsoFromTo(range?.[0], range?.[1]);
    return {
      from,
      to,
      direction,
      categoryCode,
      search: search.trim() || undefined,
      ownerUnassigned: ownerFilter === UNASSIGNED ? true : undefined,
      ownerUserId: ownerFilter && ownerFilter !== UNASSIGNED ? ownerFilter : undefined,
      shopUnassigned: shopFilter === UNASSIGNED ? true : undefined,
      shopId: shopFilter && shopFilter !== UNASSIGNED ? shopFilter : undefined,
    };
  }, [range, direction, categoryCode, search, ownerFilter, shopFilter]);

  const { data, isLoading, isFetching, refetch } = deps.useLedger(query);
  const { data: categories = [] } = deps.useCategories();
  const { data: shops = [] } = deps.useShops();
  const { data: sellers = [] } = deps.useSellers?.() ?? { data: [] };

  const items = data?.items ?? [];
  const summary = data?.summary;
  const categoryName = useMemo(() => {
    const map = new Map(categories.map((c) => [c.code, c.name]));
    return (code: string) => map.get(code) ?? code;
  }, [categories]);

  const watchedCategory = Form.useWatch('categoryCode', form);
  const watchedShop = Form.useWatch('shopId', form);
  const watchedOwner = Form.useWatch('ownerUserId', form);
  const lockDirection = watchedCategory === 'SHOP_ACQUISITION' && !!watchedShop;

  const kpiItems = useMemo((): DashboardKpiItem[] => {
    const buckets = summary?.byCurrency?.length
      ? summary.byCurrency
      : [{ currency: 'USD', creditTotal: 0, debitTotal: 0, net: 0 }];
    const cards: DashboardKpiItem[] = [];
    for (const bucket of buckets) {
      const suffix = buckets.length > 1 ? ` ${bucket.currency}` : '';
      cards.push(
        {
          key: `credit-${bucket.currency}`,
          label: `Thu${suffix}`,
          value: formatMoney(bucket.creditTotal, bucket.currency),
          tone: 'green',
          icon: <PlusOutlined />,
        },
        {
          key: `debit-${bucket.currency}`,
          label: `Chi${suffix}`,
          value: formatMoney(bucket.debitTotal, bucket.currency),
          tone: 'red',
          icon: <PlusOutlined />,
        },
        {
          key: `net-${bucket.currency}`,
          label: `Thu − Chi${suffix}`,
          value: formatMoney(bucket.net, bucket.currency),
          tone: bucket.net >= 0 ? 'blue' : 'orange',
          icon: <PlusOutlined />,
        },
      );
    }
    return cards;
  }, [summary]);

  const openCreate = () => {
    setEditing(null);
    form.setFieldsValue({
      direction: 'Debit',
      categoryCode: 'PROXY',
      currency: 'USD',
      amount: undefined,
      title: '',
      note: '',
      occurredAt: nowVn().startOf('day'),
      shopId: undefined,
      ownerUserId: undefined,
    });
  };

  const openEdit = (row: CompanyLedgerEntry) => {
    setEditing(row);
    form.setFieldsValue({
      direction: row.direction,
      categoryCode: row.categoryCode,
      currency: row.currency,
      amount: row.amount,
      title: row.title,
      note: row.note ?? '',
      occurredAt: dayjs(row.occurredAt),
      shopId: row.shopId ?? undefined,
      ownerUserId: row.ownerUserId ?? undefined,
    });
  };

  const onShopPicked = (shopId?: string) => {
    if (!shopId) return;
    if (form.getFieldValue('categoryCode') === 'SHOP_ACQUISITION') {
      form.setFieldValue('currency', 'USD');
    }
    if (!config.showSellerField) return;
    const ownerId = shops.find((s) => s.id === shopId)?.ownerId;
    if (ownerId) form.setFieldValue('ownerUserId', ownerId);
  };

  const onSellerPicked = (sellerId?: string) => {
    const shopId = form.getFieldValue('shopId') as string | undefined;
    if (!sellerId || !shopId) return;
    const shop = shops.find((s) => s.id === shopId);
    if (shop?.ownerId && shop.ownerId !== sellerId) {
      form.setFieldValue('shopId', undefined);
    }
  };

  const submit = async () => {
    const values = await form.validateFields();
    const shopId = values.shopId || null;
    const body: SaveCompanyLedgerEntryRequest = {
      direction:
        values.categoryCode === 'SHOP_ACQUISITION' && shopId ? 'Debit' : values.direction,
      categoryCode: values.categoryCode,
      amount: values.amount,
      currency: values.categoryCode === 'SHOP_ACQUISITION' && shopId ? 'USD' : values.currency,
      title: values.title?.trim() ?? '',
      note: values.note?.trim() || null,
      occurredAt: startOfVnDay(values.occurredAt).toISOString(),
      shopId,
      ownerUserId: config.showSellerField ? values.ownerUserId || null : undefined,
    };
    try {
      await deps.saveEntry?.mutateAsync({ id: editing?.id, body });
      message.success(editing ? 'Đã cập nhật khoản thu/chi.' : 'Đã lưu khoản thu/chi.');
      setEditing(undefined);
    } catch (error) {
      message.error(error instanceof Error ? error.message : 'Không lưu được khoản thu/chi.');
    }
  };

  const columns = useMemo((): ColumnsType<CompanyLedgerEntry> => {
    const cols: ColumnsType<CompanyLedgerEntry> = [
      {
        title: '#',
        key: 'index',
        width: 44,
        align: 'center',
        render: (_, __, index) => (page.current - 1) * page.pageSize + index + 1,
      },
      {
        title: 'Ngày',
        dataIndex: 'occurredAt',
        width: 110,
        render: (value: string) => (
          <Tooltip title={formatDateTimeVn(value)}>
            <span>{formatDateTimeVn(value, DATE_FORMAT)}</span>
          </Tooltip>
        ),
      },
      {
        title: 'Loại',
        dataIndex: 'direction',
        width: 64,
        render: (value: string) => (
          <Tag color={value === 'Credit' ? 'green' : 'red'}>
            {COMPANY_LEDGER_DIRECTIONS[value as 'Debit' | 'Credit'] ?? value}
          </Tag>
        ),
      },
      {
        title: 'Khoản',
        key: 'title',
        ellipsis: true,
        render: (_, row) => (
          <span className="company-ledger-khoan">
            <span className="company-ledger-khoan-title">{row.title.trim() ? row.title : '—'}</span>
            <span className="company-ledger-empty">{categoryName(row.categoryCode)}</span>
          </span>
        ),
      },
      {
        title: 'Số tiền',
        dataIndex: 'amount',
        width: 120,
        align: 'right',
        render: (value: number, row) => (
          <span className={row.direction === 'Credit' ? 'company-ledger-credit' : 'company-ledger-debit'}>
            {formatMoney(value, row.currency)}
          </span>
        ),
      },
    ];
    if (config.showSellerField) {
      cols.push({
        title: 'Seller',
        key: 'seller',
        width: 160,
        ellipsis: true,
        render: (_, row) =>
          row.ownerUserId ? (
            personLabel(row.ownerFullName, row.ownerUserName)
          ) : (
            <span className="company-ledger-empty">—</span>
          ),
      });
    }
    cols.push({
      title: 'Shop',
      key: 'shop',
      width: 160,
      ellipsis: true,
      render: (_, row) =>
        row.shopName ? row.shopName : <span className="company-ledger-empty">—</span>,
    });
    if (canManage) {
      cols.push({
        title: '',
        key: 'actions',
        width: 72,
        align: 'right',
        render: (_, row) => (
          <>
            <Button
              type="text"
              size="small"
              icon={<EditOutlined />}
              aria-label="Sửa"
              onClick={() => openEdit(row)}
            />
            <Popconfirm
              title="Xóa khoản này?"
              okText="Xóa"
              cancelText="Hủy"
              onConfirm={() => {
                void deps.deleteEntry
                  ?.mutateAsync(row.id)
                  .then(() => message.success('Đã xóa khoản thu/chi.'))
                  .catch((error: unknown) => {
                    message.error(error instanceof Error ? error.message : 'Không xóa được.');
                  });
              }}
            >
              <Button type="text" size="small" icon={<DeleteOutlined />} aria-label="Xóa" />
            </Popconfirm>
          </>
        ),
      });
    }
    return cols;
  }, [canManage, categoryName, config.showSellerField, deps.deleteEntry, page]);

  const sellerOptions = [
    { value: UNASSIGNED, label: 'Chưa gán seller' },
    ...sellers.map((s) => ({
      value: s.id,
      label: personLabel(s.fullName, s.userName),
    })),
  ];
  const shopOptions = [
    { value: UNASSIGNED, label: 'Chưa gán shop' },
    ...shops.map((s) => ({
      value: s.id,
      label: s.profileName ? `${s.profileName} (${s.name})` : s.name,
    })),
  ];
  const formShopOptions = useMemo(() => {
    const visible = watchedOwner
      ? shops.filter((shop) => shop.ownerId === watchedOwner || shop.id === watchedShop)
      : shops;
    return visible.map((shop) => ({
      value: shop.id,
      label: shopLabel(shop),
      status: shop.status,
    }));
  }, [shops, watchedOwner, watchedShop]);
  const formSellerOptions = sellers.map((s) => ({
    value: s.id,
    label: personLabel(s.fullName, s.userName),
  }));

  return (
    <div className={`company-ledger-page${isMobile ? ' is-mobile' : ''}`}>
      <PageHeader
        title="Sổ cái hệ thống"
        extra={
          <>
            {canManage ? (
              <Button icon={<PlusOutlined />} onClick={openCreate}>
                Thêm
              </Button>
            ) : null}
            <Tooltip title="Tải lại">
              <Button
                icon={<ReloadOutlined spin={isFetching} />}
                aria-label="Tải lại"
                onClick={() => {
                  void refetch();
                }}
              />
            </Tooltip>
          </>
        }
      />
      <DashboardKpiStrip items={kpiItems} />
      <div className="company-ledger-toolbar">
        <div className="admin-filters">
          <Input.Search
            className="admin-filters-search"
            size="small"
            allowClear
            placeholder="Tìm nội dung"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage((p) => ({ ...p, current: 1 }));
            }}
          />
          <div className="admin-filters-grid">
            <AdminDateRangeFilter
              value={range}
              onChange={(next) => {
                setRange(next ? [next[0].startOf('day'), next[1].startOf('day')] : null);
                setPage((p) => ({ ...p, current: 1 }));
              }}
              disabledDate={disabledFutureVnDate}
            />
            <Select
              size="small"
              allowClear
              placeholder="Loại khoản"
              options={categories.map((c) => ({ value: c.code, label: c.name }))}
              value={categoryCode}
              onChange={(value) => {
                setCategoryCode(value);
                setPage((p) => ({ ...p, current: 1 }));
              }}
            />
            <Select
              size="small"
              allowClear
              placeholder="Thu / Chi"
              options={[
                { value: 'Credit', label: 'Thu' },
                { value: 'Debit', label: 'Chi' },
              ]}
              value={direction}
              onChange={(value) => {
                setDirection(value);
                setPage((p) => ({ ...p, current: 1 }));
              }}
            />
            {config.showSellerFilter ? (
              <Select
                size="small"
                allowClear
                showSearch
                optionFilterProp="label"
                placeholder="Seller"
                options={sellerOptions}
                value={ownerFilter}
                onChange={(value) => {
                  setOwnerFilter(value);
                  setPage((p) => ({ ...p, current: 1 }));
                }}
              />
            ) : null}
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Shop"
              options={shopOptions}
              value={shopFilter}
              onChange={(value) => {
                setShopFilter(value);
                setPage((p) => ({ ...p, current: 1 }));
              }}
            />
          </div>
        </div>
      </div>
      <PageContentSpin spinning={isLoading}>
        <Table
          size="small"
          rowKey="id"
          columns={columns}
          dataSource={items}
          scroll={{ x: 720 }}
          pagination={tablePagination(items.length, {
            ...page,
            size: 'small',
            className: 'app-list-pagination',
            onChange: (current, pageSize) => setPage({ current, pageSize }),
          })}
        />
      </PageContentSpin>
      <Modal
        className="company-ledger-modal"
        title={editing ? 'Sửa khoản thu/chi' : 'Thêm khoản thu/chi'}
        open={editing !== undefined}
        width={isMobile ? 'calc(100vw - 16px)' : 560}
        centered
        okText="Lưu"
        cancelText="Hủy"
        confirmLoading={deps.saveEntry?.isPending}
        onCancel={() => setEditing(undefined)}
        onOk={() => {
          void submit();
        }}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" requiredMark={false} className="company-ledger-form">
          <div className="company-ledger-form-row">
            <Form.Item name="categoryCode" label="Khoản" rules={[{ required: true, message: 'Chọn khoản' }]}>
              <Select
                options={categories.map((c) => ({ value: c.code, label: c.name }))}
                onChange={(code: string) => {
                  const category = categories.find((c) => c.code === code);
                  const shopId = form.getFieldValue('shopId');
                  if (code === 'SHOP_ACQUISITION' && shopId) {
                    form.setFieldValue('currency', 'USD');
                    return;
                  }
                  if (category) form.setFieldValue('direction', category.defaultDirection);
                }}
              />
            </Form.Item>
            {lockDirection ? (
              <Form.Item label="Thu / Chi">
                <div className="company-ledger-lock">Chi · USD</div>
              </Form.Item>
            ) : (
              <Form.Item
                name="direction"
                label="Thu / Chi"
                className="company-ledger-direction"
                rules={[{ required: true }]}
              >
                <Radio.Group optionType="button" buttonStyle="solid">
                  <Radio.Button value="Debit">Chi</Radio.Button>
                  <Radio.Button value="Credit">Thu</Radio.Button>
                </Radio.Group>
              </Form.Item>
            )}
          </div>
          <div className="company-ledger-form-row company-ledger-form-money">
            <Form.Item name="amount" label="Số tiền" rules={[{ required: true, message: 'Nhập số tiền' }]}>
              <InputNumber min={0.01} style={{ width: '100%' }} />
            </Form.Item>
            {lockDirection ? null : (
              <Form.Item name="currency" label="Tiền tệ" rules={[{ required: true }]}>
                <Select
                  options={[
                    { value: 'USD', label: 'USD' },
                    { value: 'VND', label: 'VND' },
                  ]}
                />
              </Form.Item>
            )}
            <Form.Item name="occurredAt" label="Ngày" rules={[{ required: true, message: 'Chọn ngày' }]}>
              <DatePicker style={{ width: '100%' }} format={DATE_FORMAT} disabledDate={disabledFutureVnDate} />
            </Form.Item>
          </div>
          <Form.Item name="title" label="Nội dung">
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="note" label="Ghi chú">
            <Input.TextArea maxLength={2000} autoSize={{ minRows: 1, maxRows: 2 }} />
          </Form.Item>
          <div className={config.showSellerField ? 'company-ledger-form-row company-ledger-form-assign' : undefined}>
            {config.showSellerField ? (
              <Form.Item name="ownerUserId" label="Seller">
                <Select
                  allowClear
                  showSearch
                  optionFilterProp="label"
                  placeholder="Không gán seller"
                  options={formSellerOptions}
                  onChange={(value) => onSellerPicked(value)}
                />
              </Form.Item>
            ) : null}
            <Form.Item name="shopId" label="Shop">
              <Select
                allowClear
                showSearch
                optionFilterProp="label"
                placeholder={watchedOwner ? 'Shop của seller' : 'Không gán shop'}
                options={formShopOptions}
                className="company-ledger-shop-select"
                classNames={{ popup: { root: 'company-ledger-shop-dropdown' } }}
                optionRender={(option) =>
                  renderShopOption(option.data.label, (option.data as { status?: string }).status)
                }
                labelRender={(props) => {
                  const hit = formShopOptions.find((option) => option.value === props.value);
                  return renderShopOption(props.label, hit?.status);
                }}
                onChange={(value) => onShopPicked(value)}
              />
            </Form.Item>
          </div>
        </Form>
      </Modal>
    </div>
  );
};
