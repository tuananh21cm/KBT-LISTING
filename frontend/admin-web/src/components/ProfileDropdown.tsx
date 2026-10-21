import { Avatar, Dropdown, type MenuProps } from 'antd';
import { LogoutOutlined, UserOutlined } from '@ant-design/icons';
import { useAuthStore } from '../stores/authStore';
import { useLogout } from '../hooks/useAuth';

export const ProfileDropdown = () => {
  const user = useAuthStore((s) => s.user);
  const logoutMutation = useLogout();

  const items: MenuProps['items'] = [
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Đăng xuất',
      danger: true,
      onClick: () => logoutMutation.mutate(),
    },
  ];

  const displayName = user?.fullName || user?.username || 'Admin';

  return (
    <Dropdown menu={{ items }} placement="bottomRight" trigger={['click']}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
        <Avatar size="small" icon={<UserOutlined />} style={{ backgroundColor: '#1677ff' }} />
        <span style={{ fontSize: 12, fontWeight: 500 }}>{displayName}</span>
      </div>
    </Dropdown>
  );
};
