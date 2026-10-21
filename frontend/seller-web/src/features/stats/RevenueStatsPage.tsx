import {
  SELLER_REVENUE_STATS_CONFIG,
  RevenueStatsDepsProvider,
  RevenueStatsPage as SharedRevenueStatsPage,
  type RevenueStatsDeps,
} from '@drop/stats';
import { useDashboardSummary, useDashboardTrends } from '../../hooks/useDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useSellerRevenueStatsDeps(): RevenueStatsDeps {
  return {
    useDashboardSummary,
    useDashboardTrends,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Seller Revenue Stats shell — wires portal deps into shared `@drop/stats` page. */
export const RevenueStatsPage = () => {
  const deps = useSellerRevenueStatsDeps();
  return (
    <RevenueStatsDepsProvider value={deps}>
      <SharedRevenueStatsPage config={SELLER_REVENUE_STATS_CONFIG} />
    </RevenueStatsDepsProvider>
  );
};
