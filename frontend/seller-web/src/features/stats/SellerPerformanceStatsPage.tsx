import {
  SELLER_PERFORMANCE_STATS_CONFIG,
  SellerPerformanceStatsDepsProvider,
  SellerPerformanceStatsPage as SharedSellerPerformanceStatsPage,
  type SellerPerformanceStatsDeps,
} from '@drop/stats';
import { useSellerPerformance } from '../../hooks/useDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useSellerSellerPerformanceStatsDeps(): SellerPerformanceStatsDeps {
  return {
    useSellerPerformance,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Seller Performance Stats shell — wires portal deps into shared `@drop/stats` page. */
export const SellerPerformanceStatsPage = () => {
  const deps = useSellerSellerPerformanceStatsDeps();
  return (
    <SellerPerformanceStatsDepsProvider value={deps}>
      <SharedSellerPerformanceStatsPage config={SELLER_PERFORMANCE_STATS_CONFIG} />
    </SellerPerformanceStatsDepsProvider>
  );
};
