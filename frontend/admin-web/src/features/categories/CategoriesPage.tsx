import { useMemo, useState } from 'react';
import {
  Button,
  Card,
  Input,
  Select,
  Table,
  Tag,
  Tooltip,
  Typography,
  Alert,
} from 'antd';
import { SyncOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useAdminCategories, useSyncCategories } from '../../hooks/useAdminCategories';
import { useAdminShops } from '../../hooks/useAdminShops';
import { useAuthStore } from '../../stores/authStore';
import { PageHeader } from '../../components/PageHeader';
import { useIsMobile } from '../../hooks/useIsMobile';
import { SHOP_REGIONS } from '../../types/shop';
import { formatDateTimeVn } from '../../utils/datetime';
import type { Category } from '../../types/category';
import { tablePagination } from '../../utils/pagination';
import './categories.css';

const { Text } = Typography;

const MOBILE_CATEGORY_KEYS = new Set(['name', 'isLeaf']);

export const CategoriesPage = () => {
  const isMobile = useIsMobile();
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const canManage = hasPermission('admin.categories.manage');

  const { data: shops = [] } = useAdminShops();
  const hasConnectedShop = useMemo(
    () =>
      shops.some(
        (s) =>
          s.fourSellerConnectionStatus === 'Connected' ||
          (s.fourSellerShopId && /^\d+$/.test(s.fourSellerShopId)),
      ),
    [shops],
  );

  const [site, setSite] = useState('US');
  const [search, setSearch] = useState('');
  const [searchApplied, setSearchApplied] = useState('');
  const [pagination, setPagination] = useState({ current: 1, pageSize: 20 });

  const { data: categories = [], isLoading, isFetching, refetch } = useAdminCategories(site, searchApplied);
  const syncCategories = useSyncCategories();

  const handleSync = async () => {
    await syncCategories.mutateAsync({ site });
    await refetch();
  };

  const columns: ColumnsType<Category> = [
    {
      title: '#',
      key: 'index',
      width: 56,
      align: 'center',
      render: (_, __, index) => (pagination.current - 1) * pagination.pageSize + index + 1,
    },
    {
      title: 'External ID',
      dataIndex: 'externalId',
      key: 'externalId',
      width: 140,
    },
    {
      title: 'Tên',
      dataIndex: 'name',
      key: 'name',
      ellipsis: true,
      render: (name: string, record) =>
        isMobile ? (
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 500, lineHeight: 1.35 }}>{name}</div>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {record.externalId}
              {record.level != null ? ` · L${record.level}` : ''}
              {record.parentExternalId ? ` · ↑${record.parentExternalId}` : ''}
            </Text>
          </div>
        ) : (
          name
        ),
    },
    {
      title: 'Parent ID',
      dataIndex: 'parentExternalId',
      key: 'parentExternalId',
      width: 120,
      render: (v) => v || '—',
    },
    {
      title: 'Level',
      dataIndex: 'level',
      key: 'level',
      width: 70,
      align: 'center',
    },
    {
      title: 'Leaf',
      dataIndex: 'isLeaf',
      key: 'isLeaf',
      width: isMobile ? 72 : 80,
      render: (leaf: boolean) => (
        <Tag color={leaf ? 'green' : 'default'}>
          {isMobile ? (leaf ? 'Leaf' : 'P') : leaf ? 'Leaf' : 'Parent'}
        </Tag>
      ),
    },
    {
      title: 'Path',
      dataIndex: 'path',
      key: 'path',
      ellipsis: true,
      width: 220,
    },
    {
      title: 'Synced',
      dataIndex: 'lastSyncedAt',
      key: 'lastSyncedAt',
      width: 170,
      render: (v?: string) => formatDateTimeVn(v),
    },
  ];

  const visibleColumns = isMobile
    ? columns.filter((col) => MOBILE_CATEGORY_KEYS.has(String(col.key)))
    : columns;

  return (
    <div>
      <PageHeader
        title={isMobile ? 'Category' : 'Quản lý Category TikTok'}
        description={
          isMobile ? undefined : (
            <Text type="secondary">
              Catalog dùng chung toàn hệ thống theo Site. Sync Category dùng bất kỳ cookie 4Seller
              logged_in trong hệ thống (không bắt buộc cookie của admin đang đăng nhập), rồi lấy shop
              TikTok Connected trên 4Seller theo site.
            </Text>
          )
        }
        extra={
          canManage ? (
            <Tooltip title="Sync Category">
              <Button
                type="primary"
                icon={<SyncOutlined />}
                loading={syncCategories.isPending}
                onClick={handleSync}
                aria-label="Sync Category"
              >
                {isMobile ? null : 'Sync Category'}
              </Button>
            </Tooltip>
          ) : undefined
        }
      />

      <div className="admin-filters">
        <Input.Search
          className="admin-filters-search"
          placeholder="Tìm theo tên / ID"
          allowClear
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onSearch={(v) => {
            setSearchApplied(v.trim());
            setPagination((p) => ({ ...p, current: 1 }));
          }}
        />
        {isMobile ? (
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              width: '100%',
            }}
          >
            <Select
              style={{ flex: 1, minWidth: 0 }}
              value={site}
              onChange={(value) => {
                setSite(value);
                setPagination((p) => ({ ...p, current: 1 }));
              }}
              options={SHOP_REGIONS.map((r) => ({
                label: r.value,
                value: r.value,
                title: r.label,
              }))}
              optionRender={(option) => (
                <span>
                  <Text strong>{option.data.value}</Text>
                  <Text type="secondary" style={{ marginLeft: 8, fontSize: 12 }}>
                    {String(option.data.title ?? '').replace(/^[A-Z]{2}\s*-\s*/, '')}
                  </Text>
                </span>
              )}
            />
            <Button
              icon={<ReloadOutlined />}
              onClick={() => void refetch()}
              loading={isFetching}
              aria-label="Tải lại"
              style={{ flexShrink: 0 }}
            />
          </div>
        ) : (
          <>
            <div className="admin-filters-grid">
              <Select
                style={{ width: 200 }}
                value={site}
                onChange={(value) => {
                  setSite(value);
                  setPagination((p) => ({ ...p, current: 1 }));
                }}
                options={SHOP_REGIONS.map((r) => ({ label: r.label, value: r.value }))}
              />
            </div>
            <div className="admin-filters-actions">
              <Button
                icon={<ReloadOutlined />}
                onClick={() => void refetch()}
                loading={isFetching}
              >
                Tải lại
              </Button>
            </div>
          </>
        )}
      </div>

      {!isMobile ? (
        <Card size="small">
          <Alert
            className="cat-alert"
            type="info"
            showIcon
            title="Sync: cookie logged_in bất kỳ → get-list 4Seller → shop TikTok Connected theo Site → get-category-list. Cần Check login tại Quản lý Người dùng nếu chưa có account logged_in."
          />
          {!hasConnectedShop && (
            <Alert
              className="cat-alert"
              style={{ marginTop: 12 }}
              type="info"
              showIcon
              title="Gợi ý: nên có shop local đã kết nối 4Seller. Sync sẽ lấy shopId từ danh sách shop Connected trên 4Seller theo Site."
            />
          )}

          {syncCategories.data?.data && (
            <Alert
              className="cat-alert"
              style={{ marginTop: 12 }}
              type="success"
              showIcon
              title={`Lần sync gần nhất (${syncCategories.data.data.site}): ${syncCategories.data.data.syncedCount} node (mới ${syncCategories.data.data.createdCount}, cập nhật ${syncCategories.data.data.updatedCount}, ẩn ${syncCategories.data.data.deactivatedCount})`}
            />
          )}
        </Card>
      ) : (
        <>
          {!hasConnectedShop ? (
            <Alert
              className="cat-alert"
              style={{ marginBottom: 12 }}
              type="info"
              showIcon
              title="Nên có shop Connected 4Seller theo Site để sync."
            />
          ) : null}
          {syncCategories.data?.data ? (
            <Alert
              className="cat-alert"
              style={{ marginBottom: 12 }}
              type="success"
              showIcon
              title={`Sync ${syncCategories.data.data.site}: ${syncCategories.data.data.syncedCount} node`}
            />
          ) : null}
        </>
      )}

      <div className="admin-table-wrap">
        <Table
          style={{ marginTop: isMobile ? 0 : 16 }}
          rowKey="id"
          loading={isLoading || isFetching}
          columns={visibleColumns}
          dataSource={categories}
          pagination={tablePagination(categories.length, {
            size: 'small',
            simple: isMobile,
            ...pagination,
            total: categories.length,
            showSizeChanger: !isMobile,
            showTotal: isMobile ? undefined : (total) => `${total} category`,
            onChange: (current, pageSize) => setPagination({ current, pageSize }),
          })}
          size="small"
          scroll={isMobile ? undefined : { x: 1000 }}
        />
      </div>
    </div>
  );
};
