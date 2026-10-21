import { Navigate, useSearchParams } from 'react-router-dom';

/**
 * `/shop-tasks` → `/shop-tasks/daily|optimize|settings`, maps legacy `?tab=` and keeps other query.
 */
export const ShopTasksIndexRedirect = () => {
  const [searchParams] = useSearchParams();
  const tab = searchParams.get('tab');
  const section = tab === 'optimize' || tab === 'settings' ? tab : 'daily';
  const next = new URLSearchParams(searchParams);
  next.delete('tab');
  const qs = next.toString();
  return <Navigate to={`/shop-tasks/${section}${qs ? `?${qs}` : ''}`} replace />;
};
