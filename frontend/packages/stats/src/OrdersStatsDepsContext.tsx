import { createContext, useContext, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import type {
  DashboardQueryParams,
  DashboardSummary,
  DashboardTrends,
  DashboardTrendsParams,
} from '@drop/types/dashboard';

/**
 * Hooks/data the shared Orders Stats page needs, without importing anything app-specific.
 * Each portal (admin/seller) provides its own React Query hooks + theme subscription.
 */
export type OrdersStatsDeps = {
  useDashboardSummary: (
    params: DashboardQueryParams,
    options?: { enabled?: boolean },
  ) => Pick<
    UseQueryResult<DashboardSummary>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  >;
  useDashboardTrends: (
    params: DashboardTrendsParams,
  ) => Pick<
    UseQueryResult<DashboardTrends>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  >;
  /** Subscribe so chart axis/legend/KPI chips re-evaluate when theme toggles. */
  useThemeMode: () => string;
};

const OrdersStatsDepsContext = createContext<OrdersStatsDeps | null>(null);

export const OrdersStatsDepsProvider = ({
  value,
  children,
}: {
  value: OrdersStatsDeps;
  children: ReactNode;
}) => <OrdersStatsDepsContext.Provider value={value}>{children}</OrdersStatsDepsContext.Provider>;

export const useOrdersStatsDeps = (): OrdersStatsDeps => {
  const deps = useContext(OrdersStatsDepsContext);
  if (!deps) {
    throw new Error('useOrdersStatsDeps must be used within an <OrdersStatsDepsProvider>');
  }
  return deps;
};
