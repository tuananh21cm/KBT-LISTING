import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Button, Dropdown, Input, Modal, Result, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { MenuProps } from 'antd';
import { MenuOutlined, ReloadOutlined, SyncOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { ProductDiscountListItem } from '@drop/types/productDiscount';
import { PageContentSpin, PageHeader, StatusDot, useIsMobile } from '@drop/ui';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { PromotionsPortalConfig } from './portalConfig';
import { usePromotionsDeps } from './PromotionsDepsContext';
import './promotions.css';

const { Text } = Typography;

const TYPE_OPTIONS = [
  { value: 'DIRECT_DISCOUNT', label: 'Direct discount' },
  { value: 'FIXED_PRICE', label: 'Fixed price' },
];

export type PromotionActivityVariant = 'product-discount' | 'flash-deal';

const VARIANT_META: Record<
  PromotionActivityVariant,
  {
    title: string;
    forbiddenSubtitle: string;
    syncFail: string;
    syncOk: string;
    showTypeFilter: boolean;
  }
> = {
  'product-discount': {
    title: 'Product Discount',
    forbiddenSubtitle: 'Bạn không thể xem Product Discount.',
    syncFail: 'Đồng bộ Product Discount thất bại.',
    syncOk: 'Đồng bộ Product Discount thành công.',
    showTypeFilter: true,
  },
  'flash-deal': {
    title: 'Flash Deal',
    forbiddenSubtitle: 'Bạn không thể xem Flash Deal.',
    syncFail: 'Đồng bộ Flash Deal thất bại.',
    syncOk: 'Đồng bộ Flash Deal thành công.',
    showTypeFilter: false,
  },
};

const statusColor = (status: string): string => {
  const s = status.toUpperCase();
  if (s === 'ONGOING') return 'var(--app-success, #52c41a)';
  if (s === 'DEACTIVATED') return 'var(--app-warning, #faad14)';
  if (s === 'EXPIRED') return 'var(--app-text-tertiary, #8c8c8c)';
  return 'var(--app-text-tertiary, #8c8c8c)';
};

const formatDiscountType = (type: string) => {
  if (type === 'DIRECT_DISCOUNT') return 'Direct';
  if (type === 'FIXED_PRICE') return 'Fixed';
  if (type === 'FLASHSALE') return 'Flash sale';
  return type || '—';
};

export type SharedProductDiscountPageProps = {
  config: PromotionsPortalConfig;
  variant?: PromotionActivityVariant;
};

/** Shared promotion activity list + manual sync (admin & seller). */
export const ProductDiscountPage = ({
  config,
  variant = 'product-discount',
}: SharedProductDiscountPageProps) => {
  const meta = VARIANT_META[variant];
  const deps = usePromotionsDeps();
  const isMobile = useIsMobile();
  const canView = deps.hasPermission(config.viewPermission);
  const canManage = deps.hasPermission(config.managePermission);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(deps.defaultPageSize);
  const [shopId, setShopId] = useState<string | undefined>();
  const [ownerUserId, setOwnerUserId] = useState<string | undefined>();
  const [discountType, setDiscountType] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');

  const listParams = useMemo(
    () => ({
      page,
      pageSize,
      shopId,
      ownerUserId: config.showSellerFilter ? ownerUserId : undefined,
      discountType: meta.showTypeFilter ? discountType : undefined,
      search: searchApplied || undefined,
    }),
    [
      page,
      pageSize,
      shopId,
      ownerUserId,
      discountType,
      searchApplied,
      config.showSellerFilter,
      meta.showTypeFilter,
    ],
  );

  const { data, isLoading, isFetching, refetch } = deps.useList(listParams);
  const shopsQuery = deps.useShopOptions(canView);
  const sellersQuery = deps.useSellers?.(config.showSellerFilter && canView);
  const syncMutation = deps.useSync();
  const deactivateMutation = deps.useDeactivate();
  const deactivateAsync = deactivateMutation.mutateAsync;
  const deactivatePending = deactivateMutation.isPending;
  const deactivateVariables = deactivateMutation.variables;
  const [syncJobId, setSyncJobId] = useState<string | null>(null);
  const syncJob = deps.useSyncJob(syncJobId).data;

  const syncRunning =
    syncMutation.isPending || (!!syncJobId && (!syncJob || syncJob.state === 'running'));

  const handledSyncJobRef = useRef<string | null>(null);
  useEffect(() => {
    if (!syncJob || syncJob.state === 'running') return;
    if (handledSyncJobRef.current === syncJob.jobId) return;
    handledSyncJobRef.current = syncJob.jobId;

    if (syncJob.state === 'failed') {
      message.error(syncJob.error || meta.syncFail);
    } else {
      const problems = syncJob.sellers.filter((s) => s.status === 'failed');
      if (problems.length > 0) {
        const detail = problems
          .map((s) => `${s.ownerName?.trim() || s.ownerUserId}: ${s.message ?? 'lỗi'}`)
          .join(' | ');
        message.warning(`${syncJob.message || 'Đồng bộ xong có lỗi.'} — ${detail}`, 8);
      } else {
        message.success(syncJob.message || meta.syncOk);
      }
    }

    setSyncJobId(null);
    void refetch();
  }, [syncJob, refetch, meta.syncFail, meta.syncOk]);

  const handleSync = async () => {
    if (!canManage) return;
    try {
      const response = await syncMutation.mutateAsync(
        config.showSellerFilter && ownerUserId ? { ownerUserId } : undefined,
      );
      const jobId = response.data?.jobId;
      if (!jobId) {
        message.error('Không nhận được jobId đồng bộ.');
        return;
      }
      handledSyncJobRef.current = null;
      setSyncJobId(jobId);
    } catch {
      /* toasted in mutation */
    }
  };

  const handleDeactivate = useCallback(
    (row: ProductDiscountListItem) => {
      if (!canManage) return;
      Modal.confirm({
        title: 'Deactivate activity?',
        content: `Ngừng khuyến mãi «${row.activityName || row.fourSellerActivityId}» trên 4Seller?`,
        okText: 'Deactivate',
        okButtonProps: { danger: true },
        cancelText: 'Hủy',
        onOk: () => deactivateAsync(row.id),
      });
    },
    [canManage, deactivateAsync],
  );

  const shopOptions = useMemo(
    () =>
      (shopsQuery.data ?? [])
        .filter((s) => !ownerUserId || s.ownerId === ownerUserId)
        .map((s) => ({
          value: s.id,
          label: s.profileName ? `${s.name} (${s.profileName})` : s.name,
        })),
    [shopsQuery.data, ownerUserId],
  );

  const sellerOptions = useMemo(
    () =>
      (sellersQuery?.data ?? []).map((s) => ({
        value: s.id,
        label: s.fullName?.trim() || s.userName || s.id,
      })),
    [sellersQuery?.data],
  );

  const columns: ColumnsType<ProductDiscountListItem> = useMemo(() => {
    const cols: ColumnsType<ProductDiscountListItem> = [
      {
        title: '#',
        key: 'index',
        width: 48,
        render: (_: unknown, __: ProductDiscountListItem, index: number) =>
          (page - 1) * pageSize + index + 1,
      },
      {
        title: 'Activity',
        dataIndex: 'activityName',
        key: 'activityName',
        ellipsis: true,
        render: (name: string, row) => (
          <Space direction="vertical" size={0}>
            <Text className="app-list-line--primary">{name || '—'}</Text>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {row.platformActivityId || row.fourSellerActivityId}
            </Text>
          </Space>
        ),
      },
      {
        title: 'Shop',
        dataIndex: 'shopName',
        key: 'shopName',
        width: isMobile ? 120 : 180,
        ellipsis: true,
        render: (name: string, row) => (
          <Space direction="vertical" size={0}>
            <Text>{name || '—'}</Text>
            {config.showOwnerColumn && row.ownerName ? (
              <Text type="secondary" style={{ fontSize: 11 }}>
                {row.ownerName}
              </Text>
            ) : null}
          </Space>
        ),
      },
      {
        title: 'Type',
        dataIndex: 'discountType',
        key: 'discountType',
        width: 90,
        render: (type: string) => <Tag className="app-tag-compact">{formatDiscountType(type)}</Tag>,
      },
      {
        title: 'Status',
        dataIndex: 'promotionStatus',
        key: 'promotionStatus',
        width: 120,
        render: (status: string) => (
          <Space size={6}>
            <StatusDot color={statusColor(status)} label={status} />
            <Text style={{ fontSize: 12 }}>{status || '—'}</Text>
          </Space>
        ),
      },
      {
        title: 'Thời gian',
        key: 'range',
        width: isMobile ? 140 : 200,
        render: (_, row) => (
          <Space direction="vertical" size={0}>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {formatDateTimeVn(row.beginTimeUtc)}
            </Text>
            <Text type="secondary" style={{ fontSize: 11 }}>
              → {formatDateTimeVn(row.endTimeUtc)}
            </Text>
          </Space>
        ),
      },
      {
        title: 'SP',
        key: 'products',
        width: 72,
        align: 'right',
        render: (_, row) => (
          <Text>
            {row.productCount}
            {row.productFailCount > 0 ? (
              <Text type="danger" style={{ fontSize: 11 }}>
                {' '}
                /{row.productFailCount}
              </Text>
            ) : null}
          </Text>
        ),
      },
      {
        title: 'Lỗi',
        dataIndex: 'errorMessage',
        key: 'errorMessage',
        ellipsis: true,
        render: (msg?: string | null) =>
          msg ? (
            <Text type="danger" style={{ fontSize: 11 }} title={msg}>
              {msg}
            </Text>
          ) : (
            <Text type="secondary">—</Text>
          ),
      },
    ];

    if (canManage) {
      cols.push({
        title: 'Hành động',
        key: 'actions',
        width: 56,
        align: 'center',
        fixed: isMobile ? undefined : 'right',
        render: (_, row) => {
          const ongoing = row.promotionStatus?.toUpperCase() === 'ONGOING';
          const loading = deactivatePending && deactivateVariables === row.id;
          const items: MenuProps['items'] = [];
          if (ongoing) {
            items.push({
              key: 'deactivate',
              label: 'Deactivate',
              danger: true,
              disabled: deactivatePending,
              onClick: () => handleDeactivate(row),
            });
          }
          if (items.length === 0) {
            return <Text type="secondary">—</Text>;
          }
          return (
            <Dropdown menu={{ items, style: { minWidth: 140 } }} trigger={['click']}>
              <Button
                type="text"
                size="small"
                icon={<MenuOutlined />}
                aria-label="Hành động"
                loading={loading}
                disabled={deactivatePending && !loading}
                onClick={(e) => e.stopPropagation()}
              />
            </Dropdown>
          );
        },
      });
    }

    return cols;
  }, [
    canManage,
    config.showOwnerColumn,
    deactivatePending,
    deactivateVariables,
    handleDeactivate,
    isMobile,
    page,
    pageSize,
  ]);

  if (!canView) {
    return <Result status="403" title="Không có quyền" subTitle={meta.forbiddenSubtitle} />;
  }

  return (
    <div className="promotions-page">
      <PageHeader
        title={meta.title}
        extra={
          <Space size={8} wrap>
            <Button
              icon={<ReloadOutlined />}
              onClick={() => void refetch()}
              loading={isFetching && !syncRunning}
              aria-label="Tải lại"
            >
              {isMobile ? null : 'Tải lại'}
            </Button>
            {canManage ? (
              <Button
                type="primary"
                icon={<SyncOutlined spin={syncRunning} />}
                onClick={() => void handleSync()}
                loading={syncRunning}
              >
                Đồng bộ
              </Button>
            ) : null}
          </Space>
        }
      />

      <div className="admin-filters admin-filters-grid">
        <Input.Search
          className="admin-filters-search"
          allowClear
          placeholder="Tìm theo tên activity"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onSearch={(v) => {
            setSearchApplied(v.trim());
            setPage(1);
          }}
        />
        {config.showSellerFilter ? (
          <Select
            allowClear
            placeholder="Seller"
            options={sellerOptions}
            value={ownerUserId}
            onChange={(v) => {
              setOwnerUserId(v);
              setShopId(undefined);
              setPage(1);
            }}
            showSearch
            optionFilterProp="label"
            loading={sellersQuery?.isLoading}
            style={{ minWidth: 140 }}
          />
        ) : null}
        <Select
          allowClear
          placeholder="Shop"
          options={shopOptions}
          value={shopId}
          onChange={(v) => {
            setShopId(v);
            setPage(1);
          }}
          showSearch
          optionFilterProp="label"
          loading={shopsQuery.isLoading}
          style={{ minWidth: 280 }}
        />
        {meta.showTypeFilter ? (
          <Select
            allowClear
            placeholder="Type"
            options={TYPE_OPTIONS}
            value={discountType}
            onChange={(v) => {
              setDiscountType(v);
              setPage(1);
            }}
            style={{ minWidth: 130 }}
          />
        ) : null}
      </div>

      {isLoading && !data ? (
        <PageContentSpin />
      ) : (
        <Table<ProductDiscountListItem>
          rowKey="id"
          size="small"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isFetching}
          pagination={tablePagination(data?.total ?? 0, {
            current: page,
            pageSize,
            size: 'small',
            className: 'app-list-pagination',
            showSizeChanger: true,
            onChange: (p, ps) => {
              setPage(p);
              setPageSize(ps);
            },
          })}
          scroll={{ x: isMobile ? 1000 : undefined }}
        />
      )}
    </div>
  );
};

export type SharedFlashDealPageProps = {
  config: PromotionsPortalConfig;
};

/** Shared Flash Deal list — same UI as Product Discount, wired to flash-deal APIs via deps. */
export const FlashDealPage = ({ config }: SharedFlashDealPageProps) => (
  <ProductDiscountPage config={config} variant="flash-deal" />
);
