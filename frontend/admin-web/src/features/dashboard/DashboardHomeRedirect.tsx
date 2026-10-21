import { Navigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';
import { DashboardPage } from './DashboardPage';

/** Khớp thứ tự sidebar hiện tại: nhiệm vụ ngày → listings → kênh bán → danh mục → hệ thống */
const HOME_FALLBACK_ROUTES: Array<{ permission: string; path: string }> = [
  { permission: 'admin.shop_tasks.view', path: '/shop-tasks/daily' },
  { permission: 'admin.listings.view', path: '/listings/draft' },
  { permission: 'admin.orders.view', path: '/orders' },
  { permission: 'admin.supplier-sellers.view', path: '/supplier-lists' },
  { permission: 'admin.shops.view', path: '/shops' },
  { permission: 'admin.platforms.view', path: '/platforms' },
  { permission: 'admin.categories.view', path: '/categories' },
  { permission: 'admin.product-sources.view', path: '/product-sources' },
  { permission: 'admin.users.view', path: '/users' },
  { permission: 'admin.roles.view', path: '/roles' },
  { permission: 'admin.settings.view', path: '/settings' },
];

const firstAllowedPath = (
  isAdmin: boolean,
  hasPermission: (permission: string) => boolean,
): string => {
  const fallback = HOME_FALLBACK_ROUTES.find(
    (r) => isAdmin || hasPermission(r.permission),
  );
  return fallback?.path ?? '/shops';
};

/** Home `/`: không còn Dashboard — vào nhiệm vụ ngày (hoặc module đầu tiên đủ quyền). */
export const HomeRedirect = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.roles.includes('Admin') ?? false;

  return <Navigate to={firstAllowedPath(isAdmin, hasPermission)} replace />;
};

/** `/dashboard`: trang thống kê khi có quyền; không thì về home fallback. */
export const DashboardHomeRedirect = () => {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.roles.includes('Admin') ?? false;

  if (isAdmin || hasPermission('admin.dashboard.view')) {
    return <DashboardPage />;
  }

  return <Navigate to={firstAllowedPath(isAdmin, hasPermission)} replace />;
};
