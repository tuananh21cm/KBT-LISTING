import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Avatar, Button, Input, Select, Table, Typography } from 'antd';
import { ReloadOutlined, ShopOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import type { SupplierSellerListItem } from '@drop/types/supplierSeller';
import { PageHeader, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import {
  disabledFutureVnDate,
  endOfVnDay,
  nowVn,
  rangeForPresetVn,
  startOfVnDay,
  toVnCalendarDate,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { SupplierListsPortalConfig } from './portalConfig';
import { useSupplierListsDeps } from './SupplierListsDepsContext';
import './supplierLists.css';

const { Text } = Typography;

type SupplierListDatePreset = '7d' | '30d' | 'month';

const SUPPLIER_LIST_PRESET_OPTIONS = [
  { value: '7d', label: '7 ngày' },
  { value: '30d', label: '30 ngày' },
  { value: 'month', label: 'Tháng này' },
] as const;

const supplierListPresetRange = (preset: SupplierListDatePreset): [Dayjs, Dayjs] => {
  if (preset === '7d') {
    return [nowVn().subtract(6, 'day').startOf('day'), nowVn().endOf('day')];
  }
  if (preset === '30d') return rangeForPresetVn('30d');
  return [nowVn().startOf('month'), nowVn().endOf('day')];
};

const matchSupplierListPreset = (value: [Dayjs, Dayjs]): SupplierListDatePreset | '' => {
  for (const preset of ['7d', '30d', 'month'] as const) {
    const [pFrom, pTo] = supplierListPresetRange(preset);
    if (value[0].isSame(pFrom, 'minute') && value[1].isSame(pTo, 'minute')) return preset;
  }
  return '';
};

const rangePresets = (): { label: string; value: [Dayjs, Dayjs] }[] =>
  SUPPLIER_LIST_PRESET_OPTIONS.map((option) => ({
    label: option.label,
    value: supplierListPresetRange(option.value),
  }));

const formatMoney = (value: number) =>
  value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });

const rangeFromParams = (params: URLSearchParams): [Dayjs, Dayjs] => {
  const from = params.get('fromDate');
  const to = params.get('toDate');
  if (from && to) return [startOfVnDay(from), endOfVnDay(to)];
  return rangeForPresetVn('30d');
};

export type SharedSupplierListsPageProps = {
  config: SupplierListsPortalConfig;
};

export const SupplierListsPage = ({ config }: SharedSupplierListsPageProps) => {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const isMobile = useIsMobile();
  const deps = useSupplierListsDeps();

  const search = searchParams.get('search')?.trim() || undefined;
  const platform = searchParams.get('platform')?.trim() || undefined;
  const ownerUserId = config.showSellerFilter
    ? searchParams.get('ownerUserId')?.trim() || undefined
    : undefined;
  const pageCurrent = Math.max(1, Number(searchParams.get('page') ?? '1') || 1);
  const pageSize = Math.max(
    1,
    Number(searchParams.get('pageSize') ?? String(deps.defaultPageSize)) || deps.defaultPageSize,
  );
  const range = rangeFromParams(searchParams);
  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);

  const [searchInput, setSearchInput] = useState(search ?? '');
  const [platformInput, setPlatformInput] = useState(platform ?? '');

  const patchParams = (patch: Record<string, string | undefined>, replace = true) => {
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(patch)) {
      if (value == null || value === '') next.delete(key);
      else next.set(key, value);
    }
    setSearchParams(next, { replace });
  };

  useEffect(() => {
    setSearchInput(search ?? '');
  }, [search]);

  useEffect(() => {
    setPlatformInput(platform ?? '');
  }, [platform]);

  useEffect(() => {
    if (searchParams.get('fromDate') && searchParams.get('toDate')) return;
    patchParams({ fromDate, toDate });
  }, [searchParams, fromDate, toDate]);

  const sellersQuery = deps.useSellers?.(config.showSellerFilter);
  const listQuery = deps.useList({
    pageCurrent,
    pageSize,
    search,
    platform,
    ownerUserId,
    fromDate,
    toDate,
  });

  useEffect(() => {
    const data = listQuery.data;
    if (!data) return;
    const patch: Record<string, string | undefined> = {};
    if (data.pageCurrent !== pageCurrent) patch.page = String(data.pageCurrent);
    if (data.pageSize !== pageSize) patch.pageSize = String(data.pageSize);
    if (data.fromDate && data.fromDate !== fromDate) patch.fromDate = data.fromDate;
    if (data.toDate && data.toDate !== toDate) patch.toDate = data.toDate;
    if (Object.keys(patch).length > 0) patchParams(patch);
  }, [listQuery.data, pageCurrent, pageSize, fromDate, toDate]);

  const sellerOptions = useMemo(
    () =>
      (sellersQuery?.data ?? []).map((s) => ({
        value: s.id,
        label: s.fullName || s.userName || s.id,
      })),
    [sellersQuery?.data],
  );

  const applyPlatform = (value: string) => {
    const next = value.trim();
    setPlatformInput(next);
    if ((next || undefined) !== platform) {
      patchParams({ platform: next || undefined, page: '1' });
    }
  };

  const columns: ColumnsType<SupplierSellerListItem> = [
    {
      title: 'Shop NCC',
      key: 'shop',
      render: (_, row) => (
        <div className="supplier-lists-shop">
          <Avatar src={row.storeLogo || undefined} icon={<ShopOutlined />} />
          <div className="supplier-lists-shop-meta">
            {row.storeUrl ? (
              <Typography.Link
                className="supplier-lists-shop-title"
                href={row.storeUrl}
                target="_blank"
                rel="noreferrer"
                onClick={(e) => e.stopPropagation()}
              >
                {row.storeTitle || row.storeCode}
              </Typography.Link>
            ) : (
              <span className="supplier-lists-shop-title">{row.storeTitle || row.storeCode}</span>
            )}
            <span className="supplier-lists-shop-code">
              {row.platform} · {row.storeCode}
            </span>
          </div>
        </div>
      ),
    },
    {
      title: 'Rank',
      dataIndex: 'shopRank',
      width: 80,
      render: (v: number | null | undefined) => (v == null ? '—' : v),
    },
    {
      title: 'Follow',
      dataIndex: 'storeAttention',
      width: 90,
      render: (v: number | null | undefined) => (v == null ? '—' : v.toLocaleString('en-US')),
    },
    {
      title: 'SLA',
      key: 'sla',
      width: 110,
      render: (_, row) =>
        row.shippingDays == null && row.shippingDayPercent == null
          ? '—'
          : `${row.shippingDays ?? '—'}d / ${row.shippingDayPercent ?? '—'}%`,
    },
    {
      title: 'SP',
      dataIndex: 'productCount',
      width: 70,
      align: 'right',
    },
    {
      title: 'Listing',
      dataIndex: 'listingCount',
      width: 80,
      align: 'right',
    },
    {
      title: 'Đơn',
      key: 'orderCount',
      dataIndex: 'orderCount',
      width: 70,
      align: 'right',
    },
    {
      title: 'Doanh thu',
      key: 'itemRevenue',
      dataIndex: 'itemRevenue',
      width: 120,
      align: 'right',
      render: (v: number) => formatMoney(v ?? 0),
    },
  ];

  const mobileKeys = new Set(['shop', 'orderCount', 'itemRevenue']);
  const visibleColumns = isMobile ? columns.filter((c) => mobileKeys.has(String(c.key))) : columns;
  const total = listQuery.data?.total ?? 0;

  if (!deps.hasPermission(config.viewPermission)) {
    return <Alert type="error" showIcon title="Bạn không có quyền xem Supplier Lists." />;
  }

  return (
    <div>
      <PageHeader
        title="Supplier Lists"
        extra={
          <Button icon={<ReloadOutlined />} onClick={() => void listQuery.refetch()} loading={listQuery.isFetching}>
            Tải lại
          </Button>
        }
      />
      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          allowClear
          placeholder="Tên shop / store code"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          onSearch={(value) => {
            const next = value.trim();
            setSearchInput(next);
            patchParams({ search: next || undefined, page: '1' });
          }}
          style={{ width: isMobile ? '100%' : 240 }}
        />
        <div className="admin-filters-grid">
          <Input.Search
            allowClear
            placeholder="Platform (SHEIN…)"
            value={platformInput}
            onChange={(e) => setPlatformInput(e.target.value)}
            onSearch={applyPlatform}
            onBlur={() => applyPlatform(platformInput)}
            style={{ width: isMobile ? '100%' : 160 }}
          />
          {config.showSellerFilter ? (
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              loading={sellersQuery?.isLoading}
              options={sellerOptions}
              value={ownerUserId}
              onChange={(value) => patchParams({ ownerUserId: value, page: '1' })}
              style={{ width: isMobile ? '100%' : 200 }}
            />
          ) : null}
          <AdminDateRangeFilter
            variant="grid"
            value={range}
            presets={rangePresets()}
            presetOptions={[...SUPPLIER_LIST_PRESET_OPTIONS]}
            presetValue={matchSupplierListPreset(range)}
            disabledDate={disabledFutureVnDate}
            onChange={(next) => {
              if (!next) return;
              patchParams({
                fromDate: toVnCalendarDate(next[0]),
                toDate: toVnCalendarDate(next[1]),
                page: '1',
              });
            }}
            onPresetChange={(preset) => {
              const nextRange = supplierListPresetRange(preset as SupplierListDatePreset);
              patchParams({
                fromDate: toVnCalendarDate(nextRange[0]),
                toDate: toVnCalendarDate(nextRange[1]),
                page: '1',
              });
            }}
          />
        </div>
      </div>
      {listQuery.isError ? (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          title={listQuery.error instanceof Error ? listQuery.error.message : 'Không tải được danh sách NCC.'}
        />
      ) : null}
      <Table<SupplierSellerListItem>
        rowKey="id"
        size="small"
        loading={listQuery.isLoading || listQuery.isFetching}
        columns={visibleColumns}
        dataSource={listQuery.data?.items ?? []}
        onRow={(row) => ({
          onClick: () => {
            const next = new URLSearchParams(searchParams);
            next.set('fromDate', fromDate);
            next.set('toDate', toDate);
            next.delete('productPage');
            next.delete('productPageSize');
            navigate(`/supplier-lists/${row.id}?${next.toString()}`);
          },
          style: { cursor: 'pointer' },
        })}
        pagination={tablePagination(total, {
          current: pageCurrent,
          pageSize,
          total,
          showSizeChanger: !isMobile,
          onChange: (page, size) => {
            patchParams({
              page: String(page),
              pageSize: String(size),
            });
          },
        })}
      />
      {listQuery.data && total === 0 ? (
        <Text type="secondary">Chưa có shop NCC phù hợp bộ lọc.</Text>
      ) : null}
    </div>
  );
};
