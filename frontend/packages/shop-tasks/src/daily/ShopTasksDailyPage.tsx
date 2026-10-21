import { Button, Result, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { PageHeader, useIsMobile } from '@drop/ui';
import type { ShopTasksPortalConfig } from '../portalConfig';
import { useShopTasksDeps } from '../ShopTasksDepsContext';
import { ShopTaskBoard, useShopTaskBoardQueryMeta } from '../ShopTaskBoard';
import '../shopTasks.css';

const { Text } = Typography;

export type ShopTasksDailyPageProps = {
  config: ShopTasksPortalConfig;
};

/** Trang `/shop-tasks/daily` — Tương tác. */
export const ShopTasksDailyPage = ({ config }: ShopTasksDailyPageProps) => {
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
        title="Tương tác"
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
            {isMobile
              ? `Ngày ${meta.date} (giờ VN)`
              : `Ngày ${meta.date} (giờ VN) · trạng thái suy từ log sync extension, publish limit của shop và kết quả truy cập URL.`}
          </Text>
        }
      />
      <ShopTaskBoard config={config} mode="daily" />
    </div>
  );
};
