import { Button, Form, Input, Typography } from 'antd';
import { LockOutlined, UserOutlined } from '@ant-design/icons';
import { Navigate } from 'react-router-dom';
import { LoginCard } from '@drop/ui';
import { ThemeToggle } from '../../components/ThemeToggle';
import { BRAND_NAME } from '../../constants/brand';
import { useCurrentUser, useLogin, useLogout } from '../../hooks/useAuth';
import { useAuthStore } from '../../stores/authStore';
import type { LoginRequest } from '../../types/auth';

const { Text } = Typography;

export const LoginPage = () => {
  const accessToken = useAuthStore((s) => s.accessToken);
  const canAccessSellerPortal = useAuthStore((s) => s.canAccessSellerPortal);
  const loginMutation = useLogin();
  const logoutMutation = useLogout();
  const { isLoading } = useCurrentUser();
  const [form] = Form.useForm<LoginRequest>();

  if (accessToken && !isLoading && canAccessSellerPortal()) {
    return <Navigate to="/" replace />;
  }

  const onFinish = (values: LoginRequest) => {
    loginMutation.mutate(values);
  };

  return (
    <div className="login-page-shell">
      <div className="login-page-theme-toggle">
        <ThemeToggle />
      </div>
      <LoginCard
        title={BRAND_NAME}
        subtitle="Đăng nhập vào hệ thống"
        loading={!!accessToken && isLoading}
        sessionWarn={
          accessToken && !canAccessSellerPortal() ? (
            <div className="login-page-session-warn">
              <Text type="secondary" className="login-page-session-warn-text">
                Phiên hiện tại không có quyền seller. Đăng xuất để đăng nhập lại.
              </Text>
              <Button size="small" onClick={() => logoutMutation.mutate()} loading={logoutMutation.isPending}>
                Đăng xuất
              </Button>
            </div>
          ) : null
        }
      >
        <Form form={form} layout="vertical" onFinish={onFinish} size="small" requiredMark={false}>
          <Form.Item
            name="username"
            label="Tên đăng nhập"
            rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập' }]}
          >
            <Input prefix={<UserOutlined />} placeholder="Nhập tên đăng nhập" autoComplete="username" />
          </Form.Item>
          <Form.Item
            name="password"
            label="Mật khẩu"
            rules={[{ required: true, message: 'Vui lòng nhập mật khẩu' }]}
          >
            <Input.Password
              prefix={<LockOutlined />}
              placeholder="Nhập mật khẩu"
              autoComplete="current-password"
            />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Button type="primary" htmlType="submit" block loading={loginMutation.isPending}>
              Đăng nhập
            </Button>
          </Form.Item>
        </Form>
      </LoginCard>
    </div>
  );
};
