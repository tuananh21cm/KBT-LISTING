import {
  ADMIN_SELLER_PERFORMANCE_STATS_CONFIG,
  SellerPerformanceStatsDepsProvider,
  SellerPerformanceStatsPage as SharedSellerPerformanceStatsPage,
  type SellerPerformanceStatsDeps,
} from '@drop/stats';
import { useAdminSellerPerformance } from '../../hooks/useAdminDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useAdminSellerPerformanceStatsDeps(): SellerPerformanceStatsDeps {
  return {
    useSellerPerformance: useAdminSellerPerformance,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Admin Seller-Performance Stats shell — wires portal deps into shared `@drop/stats` page. */
export const SellerPerformanceStatsPage = () => {
  const deps = useAdminSellerPerformanceStatsDeps();
  return (
    <SellerPerformanceStatsDepsProvider value={deps}>
      <SharedSellerPerformanceStatsPage config={ADMIN_SELLER_PERFORMANCE_STATS_CONFIG} />
    </SellerPerformanceStatsDepsProvider>
  );
};
