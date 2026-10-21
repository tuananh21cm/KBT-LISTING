import { useMemo, useState, type CSSProperties } from 'react';
import {
  Alert,
  Button,
  Descriptions,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
  theme,
} from 'antd';
import { CloudDownloadOutlined, CopyOutlined, LinkOutlined, ReloadOutlined, SyncOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { formatDateTimeVn, tablePagination } from '@drop/utils';
import { Modal, NAV_LABEL, PageHeader, PlatformAvatar, useIsMobile } from '@drop/ui';
import type {
  FourSellerConnectionStatus,
  FourSellerShopCheckResponse,
} from '@drop/types/fourSeller';
import type { PlatformOption } from '@drop/types/platform';
import type { ShopListItem } from '@drop/types/shop';
import { useFourSellerDeps } from './FourSellerDepsContext';
import type { FourSellerPortalConfig } from './portalConfig';
import './fourseller.css';

const { Paragraph, Text } = Typography;

/** Mobile: shop identity + connect action only. */
const MOBILE_COLUMN_KEYS = new Set(['index', 'name', 'fourSellerConnectionStatus']);

interface ShopFilters {
  search: string;
  ownerId?: string;
  platformId?: string;
  region?: string;
  fourSellerStatus?: FourSellerConnectionStatus;
}

const defaultFilters: ShopFilters = { search: '' };

const fourSellerStatusOptions = [
  { label: 'Đã kết nối', value: 'Connected' as const },
  { label: 'Chưa kết nối', value: 'NotConnected' as const },
];

const resolveConnectionStatus = (shop: ShopListItem): FourSellerConnectionStatus =>
  shop.fourSellerConnectionStatus ?? (shop.fourSellerShopId ? 'Connected' : 'NotConnected');

const matchesSearch = (shop: ShopListItem, search: string) => {
  const keyword = search.trim().toLowerCase();
  if (!keyword) return true;

  const haystack = [
    shop.name,
    shop.profileName,
    shop.ownerFullName,
    shop.ownerUserName,
    shop.externalShopId,
    shop.fourSellerShopId,
    shop.platformName,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();

  return haystack.includes(keyword);
};

const matchByLabels: Record<string, string> = {
  platformShopName: 'tên shop (platformShopName)',
  sellingPartnerId: 'Selling Partner ID',
  fourSellerId: '4Seller ID',
};

const compactBtnStyle = {
  fontSize: 11,
  height: 20,
  paddingInline: 6,
  lineHeight: '18px',
} as const;

const tableCellRow: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  maxWidth: '100%',
  height: 22,
  lineHeight: '22px',
};

const shopTypeTag = (shopType?: string) => {
  if (!shopType) return null;
  const label = shopType === 'Llc' ? 'LLC' : shopType === 'Per' ? 'PER' : shopType;
  return (
    <Tag
      color={shopType === 'Llc' ? 'blue' : 'default'}
      style={{
        margin: 0,
        flexShrink: 0,
        fontSize: 10,
        lineHeight: '16px',
        padding: '0 4px',
        height: 16,
        borderRadius: 4,
      }}
    >
      {label}
    </Tag>
  );
};

const shopDeliBadge = (deli?: number | null) => {
  if (deli == null) return null;
  const unit = deli === 1 ? 'day' : 'days';
  return (
    <Tooltip title={`Hạn thanh toán: ${deli} ngày`}>
      <span className="fsc-deli-badge" aria-label={`Hạn thanh toán ${deli} ngày`}>
        {deli} {unit}
      </span>
    </Tooltip>
  );
};

const renderProfileCell = (shop: ShopListItem, platforms: PlatformOption[]) => (
  <span style={tableCellRow}>
    <span style={{ display: 'inline-flex', lineHeight: 0, flexShrink: 0 }}>
      <PlatformAvatar platformCode={shop.platformCode} platforms={platforms} size={16} />
    </span>
    <span
      style={{
        minWidth: 0,
        flex: 1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        lineHeight: '22px',
      }}
      title={shop.profileName || undefined}
    >
      {shop.profileName || '—'}
    </span>
  </span>
);

const renderShopCell = (shop: ShopListItem) => (
  <span style={tableCellRow}>
    {shopTypeTag(shop.shopType)}
    <span
      style={{
        minWidth: 0,
        flex: 1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        lineHeight: '22px',
      }}
      title={shop.name}
    >
      {shop.name}
    </span>
    {shopDeliBadge(shop.deli)}
  </span>
);

export type SharedFourSellerConnectPageProps = {
  config: FourSellerPortalConfig;
};

export const FourSellerConnectPage = ({ config }: SharedFourSellerConnectPageProps) => {
  const isMobile = useIsMobile();
  const { token } = theme.useToken();
  const deps = useFourSellerDeps();
  const { data: shops = [], isLoading, isFetching, refetch } = deps.useShopsList();
  const { data: platforms = [], isLoading: platformsLoading } = deps.usePlatformOptions();
  const sellersQuery = deps.useSellers?.(config.showSellerFilter);
  const sellers = sellersQuery?.data ?? [];
  const sellersLoading = sellersQuery?.isLoading ?? false;
  const authRedirect = deps.useAuthRedirect();
  const checkConnection = deps.useCheckConnection();
  const syncShops = deps.useSyncShops();
  const syncFromCrm = deps.useSyncFromCrm?.();
  const canManage = deps.hasPermission(config.managePermission);

  const [filters, setFilters] = useState<ShopFilters>(defaultFilters);
  const [modalOpen, setModalOpen] = useState(false);
  const [authorizeUrl, setAuthorizeUrl] = useState('');
  const [connectingShopId, setConnectingShopId] = useState<string | null>(null);
  const [modalShop, setModalShop] = useState<ShopListItem | null>(null);
  const [checkResult, setCheckResult] = useState<FourSellerShopCheckResponse | null>(null);
  const [pagination, setPagination] = useState({ current: 1, pageSize: 100 });

  const sellerOptions = useMemo(
    () =>
      sellers.map((seller) => ({
        label: `${seller.fullName} (${seller.userName})`,
        value: seller.id,
      })),
    [sellers],
  );

  const platformOptions = useMemo(
    () =>
      platforms.map((platform) => ({
        label: platform.name,
        value: platform.id,
      })),
    [platforms],
  );

  const connectableShops = useMemo(
    () => shops.filter((shop) => shop.status === 'Active'),
    [shops],
  );

  const regionOptions = useMemo(() => {
    const regions = [...new Set(connectableShops.map((shop) => shop.region).filter(Boolean))].sort();
    return regions.map((region) => ({ label: region, value: region }));
  }, [connectableShops]);

  const filteredShops = useMemo(
    () =>
      connectableShops.filter((shop) => {
        if (!matchesSearch(shop, filters.search)) return false;
        if (filters.ownerId && shop.ownerId !== filters.ownerId) return false;
        if (filters.platformId && shop.platformId !== filters.platformId) return false;
        if (filters.region && shop.region !== filters.region) return false;
        if (
          filters.fourSellerStatus &&
          resolveConnectionStatus(shop) !== filters.fourSellerStatus
        ) {
          return false;
        }
        return true;
      }),
    [connectableShops, filters],
  );

  const hasActiveFilters =
    !!filters.search ||
    !!filters.ownerId ||
    !!filters.platformId ||
    !!filters.region ||
    !!filters.fourSellerStatus;

  const updateFilters = (patch: Partial<ShopFilters>) => {
    setFilters((prev) => ({ ...prev, ...patch }));
    setPagination((prev) => ({ ...prev, current: 1 }));
  };

  const resetFilters = () => {
    setFilters(defaultFilters);
    setPagination((prev) => ({ ...prev, current: 1 }));
  };

  const handleConnect = async (shop: ShopListItem) => {
    setConnectingShopId(shop.id);
    try {
      const url = await authRedirect.mutateAsync(shop.id);
      setAuthorizeUrl(url);
      setModalShop(shop);
      setCheckResult(null);
      setModalOpen(true);
    } catch {
      // Error handled in mutation onError
    } finally {
      setConnectingShopId(null);
    }
  };

  const handleCheckConnection = async () => {
    if (!modalShop) return;
    try {
      const result = await checkConnection.mutateAsync(modalShop.id);
      setCheckResult(result);
    } catch {
      // Error handled in mutation onError
    }
  };

  const handleCopyUrl = async () => {
    await navigator.clipboard.writeText(authorizeUrl);
    message.success('Đã copy link xác thực');
  };

  const columns: ColumnsType<ShopListItem> = [
    {
      title: '#',
      key: 'index',
      width: isMobile ? 40 : 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'Profile',
      dataIndex: 'profileName',
      key: 'profileName',
      fixed: isMobile ? undefined : 'left',
      width: 160,
      ellipsis: true,
      render: (_, record) => renderProfileCell(record, platforms),
    },
    {
      title: 'Shop',
      dataIndex: 'name',
      key: 'name',
      fixed: isMobile ? undefined : 'left',
      width: isMobile ? undefined : 240,
      ellipsis: true,
      render: (_, record) =>
        isMobile ? (
          <div style={{ minWidth: 0 }}>
            {renderShopCell(record)}
            <div
              style={{
                marginTop: 2,
                fontSize: 11,
                color: token.colorTextTertiary,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
              title={[record.platformName, record.region].filter(Boolean).join(' · ')}
            >
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, maxWidth: '100%' }}>
                <PlatformAvatar platformCode={record.platformCode} platforms={platforms} size={12} />
                <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {[record.platformName, record.region].filter(Boolean).join(' · ') || '—'}
                </span>
              </span>
            </div>
          </div>
        ) : (
          renderShopCell(record)
        ),
    },
    ...(config.showOwnerColumn
      ? [
          {
            title: 'Seller',
            key: 'seller',
            width: 180,
            ellipsis: true,
            render: (_: unknown, record: ShopListItem) =>
              `${record.ownerFullName} (${record.ownerUserName})`,
          } satisfies ColumnsType<ShopListItem>[number],
        ]
      : []),
    {
      title: 'Platform',
      key: 'platform',
      width: 160,
      ellipsis: true,
      render: (_, record) =>
        record.region ? `${record.platformName} (${record.region})` : record.platformName,
    },
    {
      title: '4Seller ID',
      dataIndex: 'fourSellerShopId',
      key: 'fourSellerShopId',
      width: 120,
      ellipsis: true,
      render: (v?: string) => v || '—',
    },
    {
      title: '4Seller',
      key: 'fourSellerConnectionStatus',
      width: isMobile ? 88 : 110,
      render: (_, record) => {
        const status = resolveConnectionStatus(record);
        const isConnecting = authRedirect.isPending && connectingShopId === record.id;

        if (status === 'Connected') {
          if (canManage && config.allowReconnectWhenConnected) {
            return (
              <Button
                size="small"
                color="green"
                variant="solid"
                icon={<LinkOutlined style={{ fontSize: 10 }} />}
                loading={isConnecting}
                style={compactBtnStyle}
                onClick={() => void handleConnect(record)}
              >
                {isMobile ? 'OK' : 'Cập nhật'}
              </Button>
            );
          }
          return (
            <Button
              size="small"
              color="green"
              variant="solid"
              style={{ ...compactBtnStyle, cursor: 'default', pointerEvents: 'none' }}
            >
              {isMobile ? 'OK' : 'Đã kết nối'}
            </Button>
          );
        }

        if (canManage) {
          return (
            <Button
              size="small"
              color="orange"
              variant="solid"
              icon={<LinkOutlined style={{ fontSize: 10 }} />}
              loading={isConnecting}
              style={compactBtnStyle}
              onClick={() => void handleConnect(record)}
            >
              Kết nối
            </Button>
          );
        }

        return (
          <Button size="small" disabled style={compactBtnStyle}>
            {isMobile ? '—' : 'Chưa kết nối'}
          </Button>
        );
      },
    },
    {
      title: 'Ngày kết nối',
      dataIndex: 'fourSellerConnectedAt',
      key: 'fourSellerConnectedAt',
      width: 140,
      render: (v?: string) => formatDateTimeVn(v),
    },
  ];

  const visibleColumns = !isMobile
    ? columns
    : columns.filter((col) => MOBILE_COLUMN_KEYS.has(String(col.key)));

  return (
    <div>
      <PageHeader
        title={NAV_LABEL.fourseller}
        extra={
          canManage ? (
            <Space>
              {syncFromCrm ? (
                <Button
                  icon={<CloudDownloadOutlined />}
                  loading={syncFromCrm.isPending}
                  onClick={() => void syncFromCrm.mutateAsync()}
                >
                  {isMobile ? null : 'Đồng bộ từ CRM'}
                </Button>
              ) : null}
              <Button
                type="primary"
                icon={<SyncOutlined />}
                loading={syncShops.isPending}
                onClick={() => void syncShops.mutateAsync()}
              >
                {isMobile ? null : 'Đồng bộ'}
              </Button>
            </Space>
          ) : undefined
        }
      />

      {config.showCookieHint && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title={
            <>
              Cần cấu hình <Text strong>4Seller Cookie</Text> tại Hồ sơ cá nhân trước khi đồng bộ /
              kết nối. Đồng bộ kiểm tra login 4Seller trước rồi mới cập nhật shop.
            </>
          }
        />
      )}

      <div className="admin-filters">
        <Input
          className="admin-filters-search"
          allowClear
          placeholder={config.showSellerFilter ? 'Tìm shop, seller, ID...' : 'Tìm shop, ID...'}
          value={filters.search}
          onChange={(e) => updateFilters({ search: e.target.value })}
        />
        <div className="admin-filters-grid">
          {config.showSellerFilter && (
            <Select
              allowClear
              showSearch
              placeholder="Seller"
              style={{ width: 200 }}
              options={sellerOptions}
              loading={sellersLoading}
              value={filters.ownerId}
              optionFilterProp="label"
              onChange={(value) => updateFilters({ ownerId: value })}
            />
          )}
          <Select
            allowClear
            showSearch
            placeholder="Platform"
            style={{ width: 120 }}
            options={platformOptions}
            loading={platformsLoading}
            value={filters.platformId}
            optionFilterProp="label"
            onChange={(value) => updateFilters({ platformId: value })}
          />
          <Select
            allowClear
            placeholder="Region"
            style={{ width: 100 }}
            options={regionOptions}
            value={filters.region}
            onChange={(value) => updateFilters({ region: value })}
          />
          <Select
            allowClear
            placeholder="4Seller"
            style={{ width: 150 }}
            options={fourSellerStatusOptions}
            value={filters.fourSellerStatus}
            onChange={(value) => updateFilters({ fourSellerStatus: value })}
          />
        </div>
        <div className="admin-filters-actions">
          {hasActiveFilters && (
            <Button icon={<ReloadOutlined />} onClick={resetFilters}>
              {isMobile ? 'Xóa lọc' : 'Xóa bộ lọc'}
            </Button>
          )}
          <Button icon={<ReloadOutlined />} onClick={() => void refetch()} loading={isFetching}>
            {isMobile ? null : 'Tải lại'}
          </Button>
        </div>
      </div>

      <Table
        rowKey="id"
        columns={visibleColumns}
        dataSource={filteredShops}
        loading={isLoading || isFetching}
        size="small"
        scroll={{ x: isMobile ? true : config.showOwnerColumn ? 1300 : 1120 }}
        styles={{ body: { cell: { whiteSpace: isMobile ? 'normal' : 'nowrap' } } }}
        pagination={tablePagination(filteredShops.length, {
          pageSize: pagination.pageSize,
          current: pagination.current,
          showSizeChanger: !isMobile,
          simple: isMobile,
          showTotal: isMobile ? undefined : (total) => `${total} shop`,
          onChange: (page, pageSize) => setPagination({ current: page, pageSize }),
        })}
      />

      <Modal
        title="Link xác thực 4Seller"
        open={modalOpen}
        onCancel={() => {
          setModalOpen(false);
          setCheckResult(null);
        }}
        className="fsc-connect-modal"
        destroyOnHidden
        centered={!isMobile}
        footer={[
          <Button
            key="check"
            loading={checkConnection.isPending}
            onClick={() => void handleCheckConnection()}
          >
            Kiểm tra
          </Button>,
          <Button key="close" onClick={() => setModalOpen(false)}>
            Đóng
          </Button>,
          <Button key="copy" type="primary" icon={<CopyOutlined />} onClick={() => void handleCopyUrl()}>
            Copy link
          </Button>,
        ]}
        width={isMobile ? '100%' : 640}
        styles={{
          body: {
            paddingTop: 8,
            ...(isMobile
              ? { padding: '12px 16px', maxHeight: '70vh', overflowY: 'auto' as const }
              : {}),
          },
        }}
      >
        <Paragraph className="fsc-connect-hint">
          Mở link bên dưới trong trình duyệt để hoàn tất OAuth. Sau đó bấm <Text strong>Kiểm tra</Text>{' '}
          để xác nhận shop <Text strong>{modalShop?.name ?? ''}</Text> đã kết nối trên 4Seller.
        </Paragraph>
        <Space.Compact style={{ width: '100%', marginBottom: 16 }}>
          <Input value={authorizeUrl} readOnly />
          <Button icon={<CopyOutlined />} onClick={() => void handleCopyUrl()} />
        </Space.Compact>
        {checkResult && (
          <>
            <Alert
              className="fsc-connect-alert"
              type={checkResult.isConnected ? 'success' : 'warning'}
              showIcon
              title={
                checkResult.isConnected
                  ? checkResult.shopSynced
                    ? 'Shop đã kết nối — đã cập nhật trạng thái và ngày kết nối'
                    : 'Shop đã kết nối trên 4Seller'
                  : checkResult.matchedShop
                    ? `Shop tìm thấy nhưng trạng thái: ${checkResult.matchedShop.statusDesc}`
                    : 'Chưa tìm thấy shop trên 4Seller'
              }
              description={
                checkResult.matchedShop
                  ? undefined
                  : 'Thử hoàn tất OAuth rồi kiểm tra lại, hoặc đối chiếu tên shop / Selling Partner ID.'
              }
            />
            {checkResult.matchedShop && (
              <Descriptions
                className="fsc-check-descriptions"
                bordered
                size="small"
                column={1}
                styles={
                  isMobile
                    ? { label: { width: 110, padding: '6px 8px' }, content: { padding: '6px 8px' } }
                    : { label: { width: 150 } }
                }
                items={[
                  ...(checkResult.matchedBy
                    ? [
                        {
                          key: 'matchedBy',
                          label: 'Khớp theo',
                          children: matchByLabels[checkResult.matchedBy] ?? checkResult.matchedBy,
                        },
                      ]
                    : []),
                  { key: 'id', label: '4Seller ID', children: checkResult.matchedShop.id },
                  {
                    key: 'name',
                    label: 'Tên shop',
                    children: checkResult.matchedShop.platformShopName || '—',
                  },
                  {
                    key: 'spId',
                    label: 'Selling Partner ID',
                    children: checkResult.matchedShop.sellingPartnerId || '—',
                  },
                  {
                    key: 'status',
                    label: 'Trạng thái',
                    children: checkResult.matchedShop.statusDesc || '—',
                  },
                  ...(checkResult.shopSynced && checkResult.fourSellerConnectedAt
                    ? [
                        {
                          key: 'connectedAt',
                          label: 'Ngày kết nối',
                          children: formatDateTimeVn(checkResult.fourSellerConnectedAt),
                        },
                      ]
                    : []),
                  ...(!checkResult.shopSynced && checkResult.matchedShop.createTime
                    ? [
                        {
                          key: 'createTime',
                          label: 'Ngày tạo 4Seller',
                          children: checkResult.matchedShop.createTime,
                        },
                      ]
                    : []),
                ]}
              />
            )}
          </>
        )}
      </Modal>
    </div>
  );
};
