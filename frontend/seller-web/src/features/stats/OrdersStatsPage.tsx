import {
  SELLER_ORDERS_STATS_CONFIG,
  OrdersStatsDepsProvider,
  OrdersStatsPage as SharedOrdersStatsPage,
  type OrdersStatsDeps,
} from '@drop/stats';
import { useDashboardSummary, useDashboardTrends } from '../../hooks/useDashboard';
import { useThemeStore } from '../../stores/themeStore';

function useSellerOrdersStatsDeps(): OrdersStatsDeps {
  return {
    useDashboardSummary,
    useDashboardTrends,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Seller Orders Stats shell — wires portal deps into shared `@drop/stats` page. */
export const OrdersStatsPage = () => {
  const deps = useSellerOrdersStatsDeps();
  return (
    <OrdersStatsDepsProvider value={deps}>
      <SharedOrdersStatsPage config={SELLER_ORDERS_STATS_CONFIG} />
    </OrdersStatsDepsProvider>
  );
};
