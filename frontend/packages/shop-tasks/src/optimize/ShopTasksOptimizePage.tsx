import { Button, Result, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { PageHeader, useIsMobile } from '@drop/ui';
import type { ShopTasksPortalConfig } from '../portalConfig';
import { useShopTasksDeps } from '../ShopTasksDepsContext';
import { ShopTaskBoard, useShopTaskBoardQueryMeta } from '../ShopTaskBoard';
import '../shopTasks.css';

const { Text } = Typography;

export type ShopTasksOptimizePageProps = {
  config: ShopTasksPortalConfig;
};

/** Trang `/shop-tasks/optimize` — Tối ưu listing. */
export const ShopTasksOptimizePage = ({ config }: ShopTasksOptimizePageProps) => {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();
  const canView = deps.hasPermission(config.viewPermission);
  const meta = useShopTaskBoardQueryMeta(config);

  if (!canView) {
    return (
      <Result
        status="403"
        title="Không có quyền"
        subTitle="Tài khoản không được xem Nhiệm vụ ngày."
      />
    );
  }

  return (
    <div className={isMobile ? 'shop-task-page is-mobile' : 'shop-task-page'}>
      <PageHeader
        title="Tối ưu listing"
        extra={
          <Button
            icon={<ReloadOutlined />}
            loading={meta.isFetching}
            onClick={meta.refetch}
            aria-label="Tải lại"
          />
        }
        description={
          <Text type="secondary" style={{ fontSize: 12 }}>
            {`Ngày ${meta.date} (giờ VN) · mọi shop Active.`}
          </Text>
        }
      />
      <ShopTaskBoard config={config} mode="optimize" />
    </div>
  );
};
