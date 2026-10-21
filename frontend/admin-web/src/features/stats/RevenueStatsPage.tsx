import {
  ADMIN_REVENUE_STATS_CONFIG,
  RevenueStatsDepsProvider,
  RevenueStatsPage as SharedRevenueStatsPage,
  type RevenueStatsDeps,
} from '@drop/stats';
import {
  useAdminDashboardSummary,
  useAdminDashboardTrends,
} from '../../hooks/useAdminDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useAdminRevenueStatsDeps(): RevenueStatsDeps {
  return {
    useDashboardSummary: useAdminDashboardSummary,
    useDashboardTrends: useAdminDashboardTrends,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Admin Revenue Stats shell — wires portal deps into shared `@drop/stats` page. */
export const RevenueStatsPage = () => {
  const deps = useAdminRevenueStatsDeps();
  return (
    <RevenueStatsDepsProvider value={deps}>
      <SharedRevenueStatsPage config={ADMIN_REVENUE_STATS_CONFIG} />
    </RevenueStatsDepsProvider>
  );
};
