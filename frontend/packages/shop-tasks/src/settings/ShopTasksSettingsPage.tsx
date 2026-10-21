import { useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Result, Typography } from 'antd';
import { PageHeader, useIsMobile } from '@drop/ui';
import type { ShopTasksPortalConfig } from '../portalConfig';
import { useShopTasksDeps } from '../ShopTasksDepsContext';
import { TaskDefinitionsPanel } from '../TaskDefinitionsPanel';
import '../shopTasks.css';

const { Text } = Typography;

export type ShopTasksSettingsPageProps = {
  config: ShopTasksPortalConfig;
};

/** Trang `/shop-tasks/settings` — Thiết lập (admin CRUD nhiệm vụ URL). */
export const ShopTasksSettingsPage = ({ config }: ShopTasksSettingsPageProps) => {
  const deps = useShopTasksDeps();
  const isMobile = useIsMobile();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const canView = deps.hasPermission(config.viewPermission);

  useEffect(() => {
    if (config.enableDefinitionCrud) return;
    const qs = searchParams.toString();
    navigate(`/shop-tasks/daily${qs ? `?${qs}` : ''}`, { replace: true });
  }, [config.enableDefinitionCrud, searchParams, navigate]);

  if (!canView) {
    return (
      <Result
        status="403"
        title="Không có quyền"
        subTitle="Tài khoản không được xem Nhiệm vụ ngày."
      />
    );
  }

  if (!config.enableDefinitionCrud) {
    return null;
  }

  return (
    <div className={isMobile ? 'shop-task-page is-mobile' : 'shop-task-page'}>
      <PageHeader
        title="Thiết lập"
        description={
          <Text type="secondary" style={{ fontSize: 12 }}>
            {isMobile
              ? 'Định nghĩa nhiệm vụ URL giao seller.'
              : 'Định nghĩa nhiệm vụ truy cập URL giao cho seller (extension quan sát khi seller mở trang).'}
          </Text>
        }
      />
      <TaskDefinitionsPanel config={config} active />
    </div>
  );
};
