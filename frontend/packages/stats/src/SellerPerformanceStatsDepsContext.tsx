import { createContext, useContext, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import type { SellerPerformance, SellerPerformanceParams } from '@drop/types/dashboard';

/**
 * Hooks/data the shared Seller-Performance Stats page needs, without app-specific imports.
 * Each portal (admin/seller) provides its own React Query hook + theme subscription.
 */
export type SellerPerformanceStatsDeps = {
  useSellerPerformance: (
    params: SellerPerformanceParams,
    options?: { enabled?: boolean },
  ) => Pick<
    UseQueryResult<SellerPerformance>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  >;
  /** Subscribe so chart axis/legend/KPI chips re-evaluate when theme toggles. */
  useThemeMode: () => string;
};

const SellerPerformanceStatsDepsContext = createContext<SellerPerformanceStatsDeps | null>(null);

export const SellerPerformanceStatsDepsProvider = ({
  value,
  children,
}: {
  value: SellerPerformanceStatsDeps;
  children: ReactNode;
}) => (
  <SellerPerformanceStatsDepsContext.Provider value={value}>
    {children}
  </SellerPerformanceStatsDepsContext.Provider>
);

export const useSellerPerformanceStatsDeps = (): SellerPerformanceStatsDeps => {
  const deps = useContext(SellerPerformanceStatsDepsContext);
  if (!deps) {
    throw new Error(
      'useSellerPerformanceStatsDeps must be used within an <SellerPerformanceStatsDepsProvider>',
    );
  }
  return deps;
};
