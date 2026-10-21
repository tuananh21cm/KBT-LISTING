import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button, Empty, Input, Result, Select, Table, Tooltip, Typography } from 'antd';
import { CaretDownOutlined, CaretUpOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { ListingAnalyticsParams, ListingAnalyticsRow } from '@drop/types/listing';
import { PageContentSpin, PageHeader, useIsMobile } from '@drop/ui';
import { formatDateTimeVn } from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { ListingsPortalConfig } from './portalConfig';
import { useListingsDeps } from './ListingsDepsContext';
import { formatInt, formatMoney, formatRate } from './listingAnalyticsFormat';
import { ListingAnalyticsProductCell } from './ListingAnalyticsProductCell';
import { ListingAnalyticsTotalsBar } from './ListingAnalyticsTotalsBar';
import { ListingDelistSuggestions } from './ListingDelistSuggestions';
import './listings.css';
import './listingAnalytics.css';

const { Text } = Typography;

type SortKey = NonNullable<ListingAnalyticsParams['sort']>;

const SORT_KEYS: SortKey[] = ['gmv', 'orders', 'impressions', 'clicks', 'ctr', 'atc', 'name'];
const ANALYTICS_DEFAULT_PAGE_SIZE = 100;

/** Khớp cột Sản phẩm Listings (`minmax(260px, 2.4fr)`); 360px cho tên/meta đủ chỗ khi viewport rộng. */
const ANALYTICS_PRODUCT_COL_MIN = 260;
const ANALYTICS_PRODUCT_COL_WIDTH = 360;
const ANALYTICS_METRIC_COL_WIDTHS = {
  gmv: 110,
  orders: 90,
  impressions: 110,
  clicks: 100,
  ctr: 90,
  atc: 110,
} as const;
const ANALYTICS_TABLE_SCROLL_X =
  ANALYTICS_PRODUCT_COL_WIDTH +
  ANALYTICS_METRIC_COL_WIDTHS.gmv +
  ANALYTICS_METRIC_COL_WIDTHS.orders +
  ANALYTICS_METRIC_COL_WIDTHS.impressions +
  ANALYTICS_METRIC_COL_WIDTHS.clicks +
  ANALYTICS_METRIC_COL_WIDTHS.ctr +
  ANALYTICS_METRIC_COL_WIDTHS.atc;

type Delta = { dir: 'up' | 'down' | 'flat'; label: string };

const parseDelta = (ratio?: number | null): Delta | null => {
  if (ratio == null || !Number.isFinite(ratio)) return null;
  const percent = ratio * 100;
  const rounded = Math.abs(percent) >= 100 ? Math.round(percent) : Math.round(percent * 10) / 10;
  if (rounded === 0) return { dir: 'flat', label: '0%' };
  const label = `${Math.abs(rounded).toLocaleString('en-US')}%`;
  return { dir: rounded > 0 ? 'up' : 'down', label };
};

const MetricCell = ({ main, ratio }: { main: string; ratio?: number | null }) => {
  const delta = parseDelta(ratio);
  return (
    <div className="listing-analytics-metric">
      <div>{main}</div>
      {delta ? (
        <div className={`listing-analytics-delta is-${delta.dir}`}>
          {delta.dir === 'up' ? <CaretUpOutlined /> : null}
          {delta.dir === 'down' ? <CaretDownOutlined /> : null}
          <span>{delta.label}</span>
        </div>
      ) : null}
    </div>
  );
};

const shopLabel = (name: string, profileName?: string | null): string =>
  profileName?.trim() ? `${name} · ${profileName}` : name;

const sellerLabel = (fullName?: string | null, userName?: string | null): string =>
  fullName?.trim() || userName?.trim() || '—';

export type ListingAnalyticsPageProps = {
  config: ListingsPortalConfig;
};

/**
 * Compass "Product analysis" snapshot của một ngày: GMV / traffic / add-to-cart từng product
 * kèm % so kỳ trước. Dữ liệu do extension TikTok Shop đẩy lên (một bản ghi / product / ngày VN).
 */
export const ListingAnalyticsPage = ({ config }: ListingAnalyticsPageProps) => {
  const deps = useListingsDeps();
  const isMobile = useIsMobile();
  const [searchParams, setSearchParams] = useSearchParams();
  const canView = deps.hasPermission(config.viewPermission);

  const pageCurrent = Math.max(1, Number(searchParams.get('page') || 1) || 1);
  const pageSize =
    Math.max(1, Number(searchParams.get('pageSize') || ANALYTICS_DEFAULT_PAGE_SIZE)) ||
    ANALYTICS_DEFAULT_PAGE_SIZE;
  const search = searchParams.get('q')?.trim() || undefined;
  const shopId = searchParams.get('shopId') || undefined;
  const ownerId = config.showSellerFilter ? searchParams.get('ownerId') || undefined : undefined;
  const date = searchParams.get('date') || undefined;
  /** Điều hướng từ nhiệm vụ «Hủy list rác để lấy slot» mở sẵn panel đề xuất. */
  const openDelist = searchParams.get('delist') === '1';
  const sortParam = searchParams.get('sort') || undefined;
  const sort = SORT_KEYS.includes(sortParam as SortKey) ? (sortParam as SortKey) : undefined;
  const sortDir = searchParams.get('dir') === 'asc' ? 'asc' : searchParams.get('dir') === 'desc' ? 'desc' : undefined;

  const [searchInput, setSearchInput] = useState(search ?? '');
  useEffect(() => {
    setSearchInput(search ?? '');
  }, [search]);

  const patchParams = (patch: Record<string, string | undefined>) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      for (const [key, value] of Object.entries(patch)) {
        if (value == null || value === '') next.delete(key);
        else next.set(key, value);
      }
      return next;
    }, { replace: true });
  };

  const params: ListingAnalyticsParams = {
    pageCurrent,
    pageSize,
    date,
    shopId,
    ownerUserId: ownerId,
    search,
    sort,
    sortDir,
  };

  const listQuery = deps.useListingAnalytics(params);
  const datesQuery = deps.useListingAnalyticsDates({ shopId, ownerUserId: ownerId });
  const shopsQuery = deps.useShopOptions();
  const sellersQuery = config.showSellerFilter ? deps.useSellers?.() : undefined;

  const page = listQuery.data;
  const items = page?.items ?? [];
  const total = page?.total ?? 0;
  const totals = page?.totals;
  const currency = items[0]?.currency ?? 'USD';

  const shopOptions = useMemo(() => {
    const all = shopsQuery.data ?? [];
    if (!ownerId) return all;
    return all.filter((s) => !s.ownerId || s.ownerId === ownerId);
  }, [shopsQuery.data, ownerId]);

  const dateOptions = useMemo(() => {
    const dates = datesQuery.data?.dates ?? [];
    return dates.map((d) => ({ value: d, label: d }));
  }, [datesQuery.data]);

  /** Giữ mũi tên sort khớp URL (mặc định server sort GMV giảm dần). */
  const sortOrderFor = (key: SortKey) => {
    const activeKey = sort ?? 'gmv';
    if (activeKey !== key) return null;
    return (sortDir ?? 'desc') === 'asc' ? ('ascend' as const) : ('descend' as const);
  };

  const columns: ColumnsType<ListingAnalyticsRow> = [
    {
      title: 'Product',
      key: 'name',
      sorter: true,
      sortOrder: sortOrderFor('name'),
      width: isMobile ? 220 : ANALYTICS_PRODUCT_COL_WIDTH,
      ellipsis: true,
      onCell: () => ({
        style: {
          minWidth: isMobile ? 200 : ANALYTICS_PRODUCT_COL_MIN,
          width: isMobile ? 220 : ANALYTICS_PRODUCT_COL_WIDTH,
        },
      }),
      render: (_: unknown, row, rowIndex) => (
        <ListingAnalyticsProductCell
          row={row}
          index={(pageCurrent - 1) * pageSize + rowIndex + 1}
          showOwnerName={config.showOwnerName}
        />
      ),
    },
    {
      title: 'GMV',
      key: 'gmv',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('gmv'),
      width: ANALYTICS_METRIC_COL_WIDTHS.gmv,
      render: (_: unknown, row) => (
        <MetricCell main={formatMoney(row.gmv, row.currency)} ratio={row.gmvRatio} />
      ),
    },
    {
      title: 'Impressions',
      key: 'impressions',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('impressions'),
      width: ANALYTICS_METRIC_COL_WIDTHS.impressions,
      render: (_: unknown, row) => (
        <MetricCell main={formatInt(row.impressions)} ratio={row.impressionsRatio} />
      ),
    },
    {
      title: 'CTR',
      key: 'ctr',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('ctr'),
      width: ANALYTICS_METRIC_COL_WIDTHS.ctr,
      render: (_: unknown, row) => <MetricCell main={formatRate(row.ctr)} ratio={row.ctrRatio} />,
    },
    {
      title: 'Clicks',
      key: 'clicks',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('clicks'),
      width: ANALYTICS_METRIC_COL_WIDTHS.clicks,
      render: (_: unknown, row) => (
        <MetricCell main={formatInt(row.clicks)} ratio={row.clicksRatio} />
      ),
    },
    {
      title: 'Add-to-cart',
      key: 'atc',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('atc'),
      width: ANALYTICS_METRIC_COL_WIDTHS.atc,
      render: (_: unknown, row) => (
        <MetricCell main={formatInt(row.addToCartUnits)} ratio={row.addToCartUnitsRatio} />
      ),
    },
    {
      title: 'Orders',
      key: 'orders',
      align: 'right',
      sorter: true,
      sortOrder: sortOrderFor('orders'),
      width: ANALYTICS_METRIC_COL_WIDTHS.orders,
      render: (_: unknown, row) => (
        <MetricCell main={formatInt(row.orders)} ratio={row.ordersRatio} />
      ),
    },
  ];

  const handleTableChange: TableProps<ListingAnalyticsRow>['onChange'] = (
    pagination,
    _f,
    sorter,
    extra,
  ) => {
    if (extra.action === 'paginate') {
      patchParams({
        page: String(pagination.current ?? 1),
        pageSize: String(pagination.pageSize ?? pageSize),
      });
      return;
    }
    if (extra.action !== 'sort') return;
    const active = Array.isArray(sorter) ? sorter[0] : sorter;
    const key = typeof active?.columnKey === 'string' ? active.columnKey : '';
    if (!key || !active?.order || !SORT_KEYS.includes(key as SortKey)) {
      patchParams({ sort: undefined, dir: undefined, page: undefined });
      return;
    }
    patchParams({
      sort: key,
      dir: active.order === 'ascend' ? 'asc' : 'desc',
      page: undefined,
    });
  };

  if (!canView) {
    return (
      <Result
        status="403"
        title="Không có quyền"
        subTitle="Tài khoản không được xem Listing analytics."
      />
    );
  }

  const rangeLabel =
    page?.rangeStart && page?.rangeEnd ? `${page.rangeStart} → ${page.rangeEnd}` : null;

  return (
    <div className={`listing-analytics-page${isMobile ? ' is-mobile' : ''}`}>
      <PageHeader
        title="Listing analytics"
        extra={
          <Button
            icon={<ReloadOutlined />}
            loading={listQuery.isFetching}
            onClick={() => void listQuery.refetch()}
            aria-label="Tải lại"
          />
        }
        description={
          <Text type="secondary" style={{ fontSize: 12 }}>
            {page?.snapshotDate
              ? `Snapshot ${page.snapshotDate}${rangeLabel ? ` · Compass ${rangeLabel} (last 7 days)` : ''}${
                  page.lastSyncedAt ? ` · sync ${formatDateTimeVn(page.lastSyncedAt)}` : ''
                } · mỗi dòng = tổng 7 ngày, không phải số 1 ngày · % so kỳ 7 ngày trước`
              : 'Chưa có dữ liệu — bấm Analytics trên extension TikTok Shop để lấy Compass Product analysis.'}
          </Text>
        }
      />
      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          size="small"
          allowClear
          placeholder="Tìm product / product id"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          onSearch={(value) => patchParams({ q: value.trim() || undefined, page: undefined })}
        />
        <div className="admin-filters-grid">
          {config.showSellerFilter ? (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Seller"
              style={{ width: isMobile ? '100%' : 150 }}
              value={ownerId}
              options={(sellersQuery?.data ?? []).map((s) => ({
                value: s.id,
                label: sellerLabel(s.fullName, s.userName),
              }))}
              onChange={(value) =>
                patchParams({ ownerId: value, shopId: undefined, page: undefined })
              }
            />
          ) : null}
          <Select
            size="small"
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Shop"
            style={{ width: isMobile ? '100%' : 220 }}
            popupMatchSelectWidth={isMobile ? true : 280}
            value={shopId}
            loading={shopsQuery.isLoading}
            options={shopOptions.map((s) => ({
              value: s.id,
              label: shopLabel(s.name, s.profileName),
            }))}
            onChange={(value) => patchParams({ shopId: value, page: undefined })}
          />
          <Select
            size="small"
            allowClear
            placeholder="Ngày snapshot"
            style={{ width: isMobile ? '100%' : 140 }}
            value={date ?? page?.snapshotDate ?? undefined}
            loading={datesQuery.isLoading}
            options={dateOptions}
            onChange={(value) => patchParams({ date: value, page: undefined })}
          />
        </div>
      </div>
      {totals && total > 0 ? (
        <ListingAnalyticsTotalsBar
          totals={totals}
          currency={currency}
          isMobile={isMobile}
          footerExtra={
            <ListingDelistSuggestions
              params={{
                date: date ?? page?.snapshotDate ?? undefined,
                shopId,
                ownerUserId: ownerId,
              }}
              canManage={deps.hasPermission(config.managePermission)}
              defaultOpen={openDelist}
            />
          }
        />
      ) : null}
      <PageContentSpin spinning={listQuery.isLoading}>
        <Table
          size="small"
          rowKey="id"
          columns={columns}
          dataSource={items}
          tableLayout="fixed"
          scroll={{ x: isMobile ? 360 : ANALYTICS_TABLE_SCROLL_X }}
          onChange={handleTableChange}
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={
                  <Tooltip title="Extension TikTok Shop → nút Analytics (mỗi ngày ghi đè 1 bản ghi / product)">
                    <span>Chưa có snapshot nào cho bộ lọc này.</span>
                  </Tooltip>
                }
              />
            ),
          }}
          pagination={tablePagination(total, {
            current: pageCurrent,
            pageSize,
            total,
            size: 'small',
            simple: isMobile,
            showSizeChanger: !isMobile,
            pageSizeOptions: ['20', '50', '100'],
          })}
        />
      </PageContentSpin>
    </div>
  );
};
