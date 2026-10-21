import { Navigate, useLocation } from 'react-router-dom';
import { Button, Result, Spin } from 'antd';
import { useCurrentUser, useLogout } from '../hooks/useAuth';
import { useAuthStore } from '../stores/authStore';

interface ProtectedRouteProps {
  children: React.ReactNode;
}

export const ProtectedRoute = ({ children }: ProtectedRouteProps) => {
  const accessToken = useAuthStore((s) => s.accessToken);
  const canAccessSellerPortal = useAuthStore((s) => s.canAccessSellerPortal);
  const location = useLocation();
  const { isLoading, isError } = useCurrentUser();
  const logoutMutation = useLogout();

  if (!accessToken) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (isLoading) {
    return (
      <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <Spin size="large" />
      </div>
    );
  }

  if (isError || !canAccessSellerPortal()) {
    return (
      <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16 }}>
        <Result
          status="403"
          title="Không có quyền truy cập"
          subTitle="Tài khoản này không có quyền sử dụng Seller Portal. Hãy đăng xuất và thử tài khoản khác."
          extra={
            <Button type="primary" loading={logoutMutation.isPending} onClick={() => logoutMutation.mutate()}>
              Đăng xuất
            </Button>
          }
        />
      </div>
    );
  }

  return <>{children}</>;
};
