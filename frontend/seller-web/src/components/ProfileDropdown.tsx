import { Avatar, Dropdown, type MenuProps } from 'antd';
import { KeyOutlined, LogoutOutlined, UserOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { useLogout } from '../hooks/useAuth';

export const ProfileDropdown = () => {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logoutMutation = useLogout();

  const items: MenuProps['items'] = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: 'Hồ sơ',
      onClick: () => navigate('/profile'),
    },
    {
      key: 'change-password',
      icon: <KeyOutlined />,
      label: 'Đổi mật khẩu',
      onClick: () => navigate('/change-password'),
    },
    { type: 'divider' },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Đăng xuất',
      danger: true,
      onClick: () => logoutMutation.mutate(),
    },
  ];

  const displayName = user?.fullName || user?.username || 'User';

  return (
    <Dropdown menu={{ items }} placement="bottomRight" trigger={['click']}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
        <Avatar size="small" icon={<UserOutlined />} style={{ backgroundColor: '#1677ff' }} />
        <span style={{ fontSize: 12, fontWeight: 500 }}>{displayName}</span>
      </div>
    </Dropdown>
  );
};
