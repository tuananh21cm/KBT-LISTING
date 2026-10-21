import { useEffect, type ReactNode } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Avatar,
  Button,
  Card,
  Col,
  Row,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import {
  ArrowLeftOutlined,
  DollarOutlined,
  ReloadOutlined,
  ShopOutlined,
  ShoppingCartOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import type { SupplierSellerListing, SupplierSellerProduct } from '@drop/types/supplierSeller';
import { PageContentSpin, PageHeader, AdminDateRangeFilter, useIsMobile } from '@drop/ui';
import '@drop/dashboard/dashboard.css';
import {
  clampVnDateRange,
  disabledFutureVnDate,
  endOfVnDay,
  rangeForPresetVn,
  startOfVnDay,
  toVnCalendarDate,
} from '@drop/utils/datetime';
import { tablePagination } from '@drop/utils/pagination';
import type { SupplierListsPortalConfig } from './portalConfig';
import { useSupplierListsDeps } from './SupplierListsDepsContext';
import './supplierLists.css';

const { Text, Title } = Typography;

const applyDetailDateRange = (
  next: [Dayjs, Dayjs] | null,
  searchParams: URLSearchParams,
  setSearchParams: (next: URLSearchParams, opts?: { replace?: boolean }) => void,
) => {
  const clamped = clampVnDateRange(next);
  if (!clamped) return;
  const params = new URLSearchParams(searchParams);
  params.set('fromDate', toVnCalendarDate(clamped[0]));
  params.set('toDate', toVnCalendarDate(clamped[1]));
  params.set('productPage', '1');
  setSearchParams(params, { replace: true });
};

const formatMoney = (value: number) =>
  value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });

const firstImage = (src?: string | null) =>
  src?.split('|').map((part) => part.trim()).find(Boolean);

const rangeFromParams = (params: URLSearchParams): [Dayjs, Dayjs] => {
  const from = params.get('fromDate');
  const to = params.get('toDate');
  if (from && to) return [startOfVnDay(from), endOfVnDay(to)];
  return rangeForPresetVn('30d');
};

export type SharedSupplierSellerDetailPageProps = {
  config: SupplierListsPortalConfig;
};

export const SupplierSellerDetailPage = ({ config }: SharedSupplierSellerDetailPageProps) => {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const deps = useSupplierListsDeps();

  const range = rangeFromParams(searchParams);
  const fromDate = toVnCalendarDate(range[0]);
  const toDate = toVnCalendarDate(range[1]);
  const ownerUserId = config.showSellerFilter
    ? searchParams.get('ownerUserId')?.trim() || undefined
    : undefined;
  const productPageCurrent = Math.max(1, Number(searchParams.get('productPage') ?? '1') || 1);
  const productPageSize = Math.max(1, Number(searchParams.get('productPageSize') ?? '20') || 20);

  const detailQuery = deps.useDetail(
    id,
    {
      fromDate,
      toDate,
      ownerUserId,
      productPageCurrent,
      productPageSize,
    },
    !!id,
  );

  const detail = detailQuery.data;

  useEffect(() => {
    if (!searchParams.get('fromDate') || !searchParams.get('toDate')) {
      const next = new URLSearchParams(searchParams);
      next.set('fromDate', fromDate);
      next.set('toDate', toDate);
      setSearchParams(next, { replace: true });
    }
  }, [searchParams, fromDate, toDate, setSearchParams]);

  useEffect(() => {
    if (!detail) return;
    const patch: Record<string, string> = {};
    if (detail.productPageCurrent !== productPageCurrent) {
      patch.productPage = String(detail.productPageCurrent);
    }
    if (detail.productPageSize !== productPageSize) {
      patch.productPageSize = String(detail.productPageSize);
    }
    if (detail.fromDate && detail.fromDate !== fromDate) patch.fromDate = detail.fromDate;
    if (detail.toDate && detail.toDate !== toDate) patch.toDate = detail.toDate;
    if (Object.keys(patch).length === 0) return;
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(patch)) next.set(key, value);
    setSearchParams(next, { replace: true });
  }, [detail, productPageCurrent, productPageSize, fromDate, toDate, searchParams, setSearchParams]);

  const productColumns: ColumnsType<SupplierSellerProduct> = [
    {
      title: 'Sản phẩm nguồn',
      key: 'source',
      render: (_, row) => (
        <Space direction="vertical" size={2}>
          {row.sourceUrl ? (
            <Typography.Link href={row.sourceUrl} target="_blank" rel="noreferrer">
              {row.sourceUrlKey}
            </Typography.Link>
          ) : (
            <Text>{row.sourceUrlKey}</Text>
          )}
          <Space size={4} wrap>
            {row.isLocalProduct ? <Tag color="blue">Local</Tag> : null}
            {row.isSupportQuickShip ? <Tag color="green">Ship nhanh</Tag> : null}
            {row.averageRating != null ? <Tag>★ {row.averageRating}</Tag> : null}
            {row.reviewCount != null ? <Tag>{row.reviewCount.toLocaleString('en-US')} review</Tag> : null}
            {row.stock != null ? <Tag>Stock {row.stock}</Tag> : null}
          </Space>
        </Space>
      ),
    },
    {
      title: 'Listings',
      key: 'listings',
      render: (_, row) => (
        <Space direction="vertical" size={6} style={{ width: '100%' }}>
          {row.listings.map((listing) => (
            <ListingLine key={listing.id} listing={listing} showOwner={config.showOwnerName && !ownerUserId} />
          ))}
          {row.listings.length === 0 ? <Text type="secondary">Chưa gắn listing</Text> : null}
        </Space>
      ),
    },
  ];

  const backToList = () => {
    const next = new URLSearchParams(searchParams);
    next.delete('productPage');
    next.delete('productPageSize');
    navigate(`/supplier-lists?${next.toString()}`);
  };

  if (!deps.hasPermission(config.viewPermission)) {
    return <Alert type="error" showIcon title="Bạn không có quyền xem Supplier Lists." />;
  }

  return (
    <div>
      <PageHeader
        title={detail?.storeTitle || detail?.storeCode || 'Chi tiết NCC'}
        leading={<Button type="text" icon={<ArrowLeftOutlined />} onClick={backToList} />}
        extra={
          isMobile ? undefined : (
            <Space wrap>
              <AdminDateRangeFilter
                variant="inline"
                value={range}
                disabledDate={disabledFutureVnDate}
                onChange={(next) => applyDetailDateRange(next, searchParams, setSearchParams)}
              />
              <Button
                icon={<ReloadOutlined />}
                loading={detailQuery.isFetching}
                onClick={() => void detailQuery.refetch()}
              >
                Tải lại
              </Button>
            </Space>
          )
        }
      />

      {isMobile ? (
        <div className="dashboard-toolbar" style={{ marginBottom: 12 }}>
          <AdminDateRangeFilter
            variant="toolbar"
            value={range}
            disabledDate={disabledFutureVnDate}
            onChange={(next) => applyDetailDateRange(next, searchParams, setSearchParams)}
          />
          <Button
            block
            icon={<ReloadOutlined />}
            loading={detailQuery.isFetching}
            onClick={() => void detailQuery.refetch()}
          >
            Tải lại
          </Button>
        </div>
      ) : null}

      {detailQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={detailQuery.error instanceof Error ? detailQuery.error.message : 'Không tải được shop NCC.'}
        />
      ) : null}

      <PageContentSpin spinning={detailQuery.isLoading || detailQuery.isFetching}>
        {detail ? (
          <>
            <Space align="start" size={12} style={{ marginBottom: 16 }}>
              <Avatar size={56} src={detail.storeLogo || undefined} icon={<ShopOutlined />} />
              <div>
                <Title level={5} style={{ margin: 0 }}>
                  {detail.storeUrl ? (
                    <Typography.Link href={detail.storeUrl} target="_blank" rel="noreferrer">
                      {detail.storeTitle || detail.storeCode}
                    </Typography.Link>
                  ) : (
                    detail.storeTitle || detail.storeCode
                  )}
                </Title>
                <Text type="secondary">
                  {detail.platform} · {detail.storeCode}
                  {detail.shopRank != null ? ` · Rank ${detail.shopRank}` : ''}
                  {detail.storeAttention != null
                    ? ` · ${detail.storeAttention.toLocaleString('en-US')} follow`
                    : ''}
                  {detail.shippingDays != null || detail.shippingDayPercent != null
                    ? ` · SLA ${detail.shippingDays ?? '—'}d / ${detail.shippingDayPercent ?? '—'}%`
                    : ''}
                </Text>
              </div>
            </Space>

            <Row gutter={[8, 8]} className="supplier-lists-kpi-row">
              <Col xs={12} sm={8} md={4}>
                <KpiCard label="Đơn" value={detail.orderCount.toLocaleString('en-US')} icon={<ShoppingCartOutlined />} />
              </Col>
              <Col xs={12} sm={8} md={4}>
                <KpiCard label="SL" value={detail.itemQty.toLocaleString('en-US')} icon={<UnorderedListOutlined />} />
              </Col>
              <Col xs={12} sm={8} md={5}>
                <KpiCard label="Doanh thu" value={formatMoney(detail.itemRevenue)} icon={<DollarOutlined />} />
              </Col>
              <Col xs={12} sm={8} md={5}>
                <KpiCard label="Sản phẩm" value={detail.productCount.toLocaleString('en-US')} icon={<ShopOutlined />} />
              </Col>
              <Col xs={12} sm={8} md={6}>
                <KpiCard
                  label="Listing"
                  value={detail.listingCount.toLocaleString('en-US')}
                  icon={<UnorderedListOutlined />}
                />
              </Col>
            </Row>

            <div className="supplier-lists-product-head">
              <Title level={5} style={{ margin: 0 }}>
                Sản phẩm liên kết
              </Title>
              <Text type="secondary">{detail.productTotal.toLocaleString('en-US')} SP</Text>
            </div>
            <Table<SupplierSellerProduct>
              rowKey="id"
              size="small"
              columns={productColumns}
              dataSource={detail.products}
              pagination={tablePagination(detail.productTotal, {
                current: productPageCurrent,
                pageSize: productPageSize,
                total: detail.productTotal,
                showSizeChanger: !isMobile,
                onChange: (page, size) => {
                  const next = new URLSearchParams(searchParams);
                  next.set('productPage', String(page));
                  next.set('productPageSize', String(size));
                  setSearchParams(next, { replace: true });
                },
              })}
            />
          </>
        ) : null}
      </PageContentSpin>
    </div>
  );
};

const KpiCard = ({ label, value, icon }: { label: string; value: string; icon: ReactNode }) => (
  <Card size="small" className="dashboard-kpi-card">
    <div className="dashboard-kpi-head">
      <span className="dashboard-kpi-label">{label}</span>
      <span className="dashboard-kpi-icon">{icon}</span>
    </div>
    <p className="dashboard-kpi-value">{value}</p>
  </Card>
);

const ListingLine = ({
  listing,
  showOwner,
}: {
  listing: SupplierSellerListing;
  showOwner: boolean;
}) => {
  const thumb = firstImage(listing.mainImage);
  return (
    <div className="supplier-lists-listing-row">
      {thumb ? <img className="supplier-lists-listing-thumb" src={thumb} alt="" /> : null}
      <div className="supplier-lists-listing-meta">
        <Link to={`/listings/${listing.id}/edit`}>{listing.productName || listing.id}</Link>
        <Text type="secondary">
          {listing.pageType}
          {listing.siteStatus ? ` · ${listing.siteStatus}` : ''}
          {listing.shopName ? ` · ${listing.shopName}` : ''}
          {showOwner && listing.ownerName ? ` · ${listing.ownerName}` : ''}
        </Text>
      </div>
    </div>
  );
};
