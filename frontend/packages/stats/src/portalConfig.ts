/** Feature flags that differentiate admin vs seller Orders Stats UI. */
export type OrdersStatsPortalConfig = {
  portal: 'admin' | 'seller';
  /** Cross-seller order-share pie (admin only — seller is scoped to self). */
  showSellerSharePie: boolean;
};

export const ADMIN_ORDERS_STATS_CONFIG: OrdersStatsPortalConfig = {
  portal: 'admin',
  showSellerSharePie: true,
};

export const SELLER_ORDERS_STATS_CONFIG: OrdersStatsPortalConfig = {
  portal: 'seller',
  showSellerSharePie: false,
};

/** Feature flags that differentiate admin vs seller Revenue Stats UI. */
export type RevenueStatsPortalConfig = {
  portal: 'admin' | 'seller';
  /**
   * Cross-seller revenue pie + Seller|Shop dim toggle + seller ranking table
   * (admin only — seller is scoped to self, shop-only).
   */
  showSellerSharePie: boolean;
};

export const ADMIN_REVENUE_STATS_CONFIG: RevenueStatsPortalConfig = {
  portal: 'admin',
  showSellerSharePie: true,
};

export const SELLER_REVENUE_STATS_CONFIG: RevenueStatsPortalConfig = {
  portal: 'seller',
  showSellerSharePie: false,
};

/** Feature flags that differentiate admin vs seller Seller-Performance Stats UI. */
export type SellerPerformanceStatsPortalConfig = {
  portal: 'admin' | 'seller';
  pageTitle: string;
  /** Cross-seller volume/intensity charts + ranking table (admin only). */
  showCrossSellerRanking: boolean;
  /** Seller picker on the growth chart (admin only). */
  showSellerFilter: boolean;
  /** Active-seller count KPI (admin only — seller is always self). */
  showSellerCountKpi: boolean;
};

export const ADMIN_SELLER_PERFORMANCE_STATS_CONFIG: SellerPerformanceStatsPortalConfig = {
  portal: 'admin',
  pageTitle: 'Hiệu suất Seller',
  showCrossSellerRanking: true,
  showSellerFilter: true,
  showSellerCountKpi: true,
};

export const SELLER_PERFORMANCE_STATS_CONFIG: SellerPerformanceStatsPortalConfig = {
  portal: 'seller',
  pageTitle: 'Hiệu suất',
  showCrossSellerRanking: false,
  showSellerFilter: false,
  showSellerCountKpi: false,
};
