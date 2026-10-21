import { Button, Drawer, Empty, Progress, Tag, Typography } from 'antd';
import {
  ExportOutlined,
  ScissorOutlined,
  ShoppingOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import type { ShopTaskItem, ShopTaskShop } from '@drop/types/shopTask';
import { useIsMobile } from '@drop/ui';
import { formatDateTimeVn } from '@drop/utils/datetime';
import type { ShopTasksPortalConfig } from './portalConfig';
import { useShopTasksDeps } from './ShopTasksDepsContext';
import { isDailyMissionTask, statusMeta } from './shopTaskStatus';

const { Text } = Typography;

const shopLabel = (shopName: string, profileName?: string | null): string => {
  const profile = profileName?.trim();
  const name = shopName?.trim() || 'Shop';
  return profile ? `${profile} (${name})` : name;
};

type TaskAction = { label: string; icon: React.ReactNode; path: string };

/**
 * Hành động gợi ý cho từng nhiệm vụ. Nhiệm vụ đồng bộ không có link nội bộ — nó phải làm trên
 * extension, nên chỉ hướng dẫn bằng chữ.
 */
const resolveAction = (
  task: ShopTaskItem,
  shop: ShopTaskShop,
  config: ShopTasksPortalConfig,
): TaskAction | null => {
  switch (task.key) {
    case 'listing_fill':
      return {
        label: 'Mở danh sách Draft',
        icon: <ShoppingOutlined />,
        path: `${config.listingsDraftPath}?shopId=${shop.shopId}`,
      };
    case 'delist_cleanup':
      return {
        label: 'Xem đề xuất hủy list',
        icon: <ScissorOutlined />,
        path: `${config.listingsAnalyticsPath}?shopId=${shop.shopId}&delist=1`,
      };
    default:
      return null;
  }
};

const TaskRow = ({
  task,
  shop,
  config,
  onNavigate,
}: {
  task: ShopTaskItem;
  shop: ShopTaskShop;
  config: ShopTasksPortalConfig;
  onNavigate: (path: string) => void;
}) => {
  const meta = statusMeta(task.status);
  const action = resolveAction(task, shop, config);

  return (
    <div className={`shop-task-row is-${task.status}`}>
      <div className="shop-task-row-head">
        <span className="shop-task-row-title">{task.title}</span>
        <Tag color={meta.tone} icon={meta.icon} className="app-tag-compact">
          {meta.label}
        </Tag>
      </div>
      <div className="shop-task-row-detail">{task.detail}</div>
      <div className="shop-task-row-foot">
        {task.current != null && task.target != null ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            {task.current}/{task.target}
          </Text>
        ) : null}
        {task.completedAt ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            {formatDateTimeVn(task.completedAt)}
          </Text>
        ) : null}
        {task.url ? (
          <a href={task.url} target="_blank" rel="noreferrer" className="shop-task-row-link">
            <ExportOutlined /> Mở URL
          </a>
        ) : null}
        {action ? (
          <Button type="link" size="small" icon={action.icon} onClick={() => onNavigate(action.path)}>
            {action.label}
          </Button>
        ) : null}
        {task.key === 'sync_extension' && task.status !== 'done' ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            <SyncOutlined /> Mở extension KBT Seller → bấm Sync
          </Text>
        ) : null}
        {task.key === 'url_visit' && task.status !== 'done' ? (
          <Text type="secondary" style={{ fontSize: 11 }}>
            Tự đánh dấu xong khi seller mở trang này (extension đang bật)
          </Text>
        ) : null}
      </div>
    </div>
  );
};

export type ShopTaskDrawerProps = {
  shop: ShopTaskShop | null;
  config: ShopTasksPortalConfig;
  onClose: () => void;
};

/** Chi tiết từng nhiệm vụ của một shop kèm lối tắt sang trang xử lý tương ứng. */
export function ShopTaskDrawer({ shop, config, onClose }: ShopTaskDrawerProps) {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();

  const daily = shop?.tasks.filter(isDailyMissionTask) ?? [];
  const optimize = shop?.tasks.filter((t) => t.group === 'optimize') ?? [];
  const percent = shop && shop.totalCount > 0
    ? Math.round((shop.completedCount / shop.totalCount) * 100)
    : 0;

  const go = (path: string) => {
    onClose();
    deps.navigate(path);
  };

  return (
    <Drawer
      open={!!shop}
      onClose={onClose}
      size={isMobile ? '100%' : 520}
      title={shop ? shopLabel(shop.shopName, shop.profileName) : 'Nhiệm vụ shop'}
      className={isMobile ? 'shop-task-drawer is-mobile' : 'shop-task-drawer'}
      styles={
        isMobile
          ? { body: { padding: '12px 12px 16px' } }
          : undefined
      }
    >
      {shop ? (
        <>
          <div className="shop-task-drawer-summary">
            <Progress percent={percent} size="small" status={percent === 100 ? 'success' : 'active'} />
            <div className="shop-task-drawer-meta">
              <Text type="secondary" style={{ fontSize: 11 }}>
                {shop.completedCount}/{shop.totalCount} nhiệm vụ hàng ngày
              </Text>
              {config.showOwnerFilter ? (
                <Text type="secondary" style={{ fontSize: 11 }}>
                  Seller: {shop.ownerFullName || shop.ownerUserName}
                </Text>
              ) : null}
              <Text type="secondary" style={{ fontSize: 11 }}>
                Slot:{' '}
                {shop.publishLimitTotal == null
                  ? '—'
                  : `${shop.publishLimitPublished ?? 0}/${shop.publishLimitTotal} · trống ${shop.remainingSlots ?? 0}`}
              </Text>
            </div>
          </div>

          <div className="shop-task-drawer-section">Nhiệm vụ hàng ngày</div>
          {daily.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có nhiệm vụ." />
          ) : (
            daily.map((task) => (
              <TaskRow
                key={`${task.key}-${task.definitionId ?? ''}`}
                task={task}
                shop={shop}
                config={config}
                onNavigate={go}
              />
            ))
          )}

          {optimize.length > 0 ? (
            <>
              <div className="shop-task-drawer-section">Tối ưu listing</div>
              {optimize.map((task) => (
                <TaskRow
                  key={`${task.key}-${task.definitionId ?? ''}`}
                  task={task}
                  shop={shop}
                  config={config}
                  onNavigate={go}
                />
              ))}
            </>
          ) : null}
        </>
      ) : null}
    </Drawer>
  );
}
