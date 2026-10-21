import { createContext, useContext, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import type {
  DashboardQueryParams,
  DashboardSummary,
  DashboardTrends,
  DashboardTrendsParams,
} from '@drop/types/dashboard';

/**
 * Hooks/data the shared Revenue Stats page needs, without importing anything app-specific.
 * Each portal (admin/seller) provides its own React Query hooks + theme subscription.
 */
export type RevenueStatsDeps = {
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

const RevenueStatsDepsContext = createContext<RevenueStatsDeps | null>(null);

export const RevenueStatsDepsProvider = ({
  value,
  children,
}: {
  value: RevenueStatsDeps;
  children: ReactNode;
}) => <RevenueStatsDepsContext.Provider value={value}>{children}</RevenueStatsDepsContext.Provider>;

export const useRevenueStatsDeps = (): RevenueStatsDeps => {
  const deps = useContext(RevenueStatsDepsContext);
  if (!deps) {
    throw new Error('useRevenueStatsDeps must be used within an <RevenueStatsDepsProvider>');
  }
  return deps;
};
