import {
  ADMIN_ORDERS_STATS_CONFIG,
  OrdersStatsDepsProvider,
  OrdersStatsPage as SharedOrdersStatsPage,
  type OrdersStatsDeps,
} from '@drop/stats';
import {
  useAdminDashboardSummary,
  useAdminDashboardTrends,
} from '../../hooks/useAdminDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useAdminOrdersStatsDeps(): OrdersStatsDeps {
  return {
    useDashboardSummary: useAdminDashboardSummary,
    useDashboardTrends: useAdminDashboardTrends,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Admin Orders Stats shell — wires portal deps into shared `@drop/stats` page. */
export const OrdersStatsPage = () => {
  const deps = useAdminOrdersStatsDeps();
  return (
    <OrdersStatsDepsProvider value={deps}>
      <SharedOrdersStatsPage config={ADMIN_ORDERS_STATS_CONFIG} />
    </OrdersStatsDepsProvider>
  );
};
