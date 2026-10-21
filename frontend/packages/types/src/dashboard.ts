export type DashboardPeriod = 'day' | 'week' | 'month';

export type DashboardTrendGrain = 'day' | 'week' | 'month' | 'year';

export type DashboardChartType = 'column' | 'line' | 'area';

export type DashboardTrendGroupBy = 'seller' | 'shop';

export interface DashboardQueryParams {
  ownerUserId?: string;
  shopId?: string;
  platform?: string;
  /** day | week | month — period KPIs + seller ranking */
  period?: DashboardPeriod;
  /** Inclusive yyyy-MM-dd — with toDate overrides period */
  fromDate?: string;
  /** Inclusive yyyy-MM-dd */
  toDate?: string;
  /**
   * Comma-separated summary sections to compute (omit = full snapshot).
   * kpis | cardSpend (deprecated) | orderStatus | listings | shops | sellers | shopsFinance | topProducts
   */
  include?: string;
  /**
   * Slim metric set:
   * - `orders` → sellers / shopsFinance / trends skip revenue·cost·Items (sort by orders)
   * - `revenue` → trends skip cost·Items (keep revenue + orders; for /stats/revenue)
   * Omit = full finance metrics.
   */
  metrics?: 'orders' | 'revenue';
}

export interface DashboardTrendsParams extends DashboardQueryParams {
  /** Lookback days when period is day (default 7). Ignored for week/month or custom range. */
  days?: number;
  /** day | week | month | year bucket size */
  grain?: DashboardTrendGrain;
  /** Break down series by seller or shop (top 8 + Others) */
  groupBy?: DashboardTrendGroupBy;
  /** Day heatmap only: fetch seller OR shop matrix (omit = both, slower) */
  dimension?: DashboardTrendGroupBy;
  /** Hourly heatmap: us (America/Los_Angeles, default) | vn (Asia/Ho_Chi_Minh) */
  timezone?: 'us' | 'vn';
  /** When false, skip listed scans (charts page). Default true. */
  includeListed?: boolean;
}

export interface DashboardToday {
  date: string;
  /** Period orders: Active shop with receiving bank, excluding CANCELLED / REFUND (same set as revenue). */
  orderCount: number;
  /** Seller net for the same order set as orderCount. */
  revenue: number;
  /**
   * Net fulfil purchase spend in period (FulfilTransactions: Debit − Credit),
   * same formula as Lịch sử mua hàng / /admin/fulfil/transactions/stats.
   */
  cost: number;
  /**
   * Σ FulfilTransactions.CouponAmount in the same window/filters as `cost`
   * (CostUpdate coupon snapshot; Manual null → 0). Not subtracted from cost —
   * same meaning as the Coupon column on Lịch sử mua hàng.
   */
  coupon?: number;
  /** revenue − cost */
  profit: number;
  /** profit / cost × 100 (markup on fulfil Cost); null when cost is 0 */
  profitMargin?: number | null;
  /**
   * Net receiving-bank income in period (Confirmed: Credit − Debit),
   * same formula as /admin/receiving-bank-accounts/transactions/stats.
   */
  income?: number;
  /** @deprecated Alias of `cost` — prefer `cost`. */
  cardSpend?: number;
  listedCount: number;
}

export interface DashboardOrderStatusCounts {
  all: number;
  pendingProcess: number;
  purchased: number;
  /** Chờ lấy (AWAITING_COLLECTION_GROUP) */
  awaitingCollection: number;
  /** Đang giao (IN_TRANSIT) */
  inTransit: number;
  delivered: number;
  canceled: number;
  refund: number;
  /** USPS last-mile chưa xử lý (optional — list/dashboard) */
  uspsUnprocessed?: number;
  /**
   * Orders whose shop is not Active and/or has no receiving bank card
   * (excluded from KPI Orders/Revenue).
   */
  suspended?: number;
  /** @deprecated Prefer pendingProcess */
  onHold?: number;
  /** @deprecated Prefer pendingProcess */
  awaitingShipment?: number;
}

export interface DashboardListings {
  draft: number;
  published: number;
  syncErrorCount: number;
  active: number;
  inactive: number;
  pending: number;
}

export interface DashboardShops {
  total: number;
  active: number;
  inactive: number;
  suspended: number;
  connected: number;
}

export interface DashboardProducts {
  total: number;
  draft: number;
  active: number;
  archived: number;
}

export interface DashboardSellerRank {
  userId: string;
  displayName: string;
  listed: number;
  orders: number;
  revenue: number;
  /** Order SupplierTotal sum in period */
  cost: number;
  /** revenue − cost */
  profit: number;
  /** profit / revenue × 100; null when revenue is 0 */
  profitMargin?: number | null;
}

export interface DashboardShopFinance {
  shopId?: string | null;
  shopName: string;
  shopProfileName?: string | null;
  sellerName?: string | null;
  orders: number;
  revenue: number;
  cost: number;
  profit: number;
}

export interface DashboardTopProduct {
  productKey: string;
  sellerSku?: string | null;
  title: string;
  imgUrl?: string | null;
  /** Primary seller (highest qty for this product key) */
  sellerName?: string | null;
  shopName?: string | null;
  shopProfileName?: string | null;
  quantitySold: number;
  orderCount: number;
  revenue: number;
}

export interface DashboardSummary {
  timezone: string;
  period: DashboardPeriod;
  fromDate: string;
  toDate: string;
  today: DashboardToday;
  orderStatusCounts: DashboardOrderStatusCounts;
  listings: DashboardListings;
  shops: DashboardShops;
  products: DashboardProducts;
  sellersToday: DashboardSellerRank[];
  shopsFinance: DashboardShopFinance[];
  topProducts: DashboardTopProduct[];
}

export interface DashboardTrendDay {
  date: string;
  /** All-status order count */
  orders: number;
  /** Exclude CANCELLED / REFUND */
  ordersOk?: number;
  /** CANCELLED / CANCELED (+ legacy canceled) */
  ordersCanceled?: number;
  /** REFUND / REFUNDED */
  ordersRefund?: number;
  revenue: number;
  /** Order total cost in bucket */
  cost?: number;
  /** revenue − cost */
  profit?: number;
  listed: number;
}

export interface DashboardTrendSeriesPoint {
  date: string;
  entityId: string;
  entityName: string;
  orders: number;
  revenue: number;
  cost: number;
  profit: number;
}

export interface DashboardTrends {
  timezone: string;
  /** day | week | month | year */
  grain?: DashboardTrendGrain;
  fromDate?: string;
  toDate?: string;
  /** seller | shop when breakdown requested */
  groupBy?: DashboardTrendGroupBy | null;
  days: DashboardTrendDay[];
  /** Per-entity points when groupBy is set */
  series?: DashboardTrendSeriesPoint[];
}

export interface DashboardHeatmapCell {
  date: string;
  entityId: string;
  entityName: string;
  orders: number;
  /** Shop heatmap: seller id for grouping */
  groupId?: string | null;
  groupName?: string | null;
  /** Shop cells: TikTok publish_limit.published_num (synced). */
  publishLimitPublished?: number | null;
  /** Shop cells: TikTok publish_limit.total_limit (synced). */
  publishLimitTotal?: number | null;
}

export interface DashboardOrderHeatmap {
  timezone: string;
  period: DashboardPeriod;
  fromDate: string;
  toDate: string;
  sellers: DashboardHeatmapCell[];
  shops: DashboardHeatmapCell[];
}

/** One cell: orders at hour 0–23, aggregated over the selected range. */
export interface DashboardHourlyHeatmapCell {
  entityId: string;
  entityName: string;
  hour: number;
  orders: number;
}

/** Hour order-count matrix: totals (all sellers) and/or per-seller rows. */
export interface DashboardOrderHourlyHeatmap {
  timezone: string;
  period: DashboardPeriod;
  fromDate: string;
  toDate: string;
  totals: DashboardHourlyHeatmapCell[];
  sellers: DashboardHourlyHeatmapCell[];
}

/** Query for seller performance (same period / from–to as summary). */
export type SellerPerformanceParams = DashboardQueryParams;

export interface SellerPerformanceTotals {
  dayCount: number;
  /** Unique VN days with ≥1 operational shop (Σ across sellers). */
  sellerOperationalDays: number;
  activeSellerCount: number;
  sellersWithOrders: number;
  /** Current Active shops snapshot (informational). */
  activeShopCount: number;
  /** Current Deactivated shops snapshot (informational). */
  deactivatedShopCount: number;
  /** Current Suspended shops snapshot (informational). */
  suspendedShopCount: number;
  /** Current total shops (all statuses) in scope. */
  shopCount: number;
  /** Shops with ≥1 operational day in the period. */
  operationalShopCount: number;
  /** Legacy alias of operationalShopCount. */
  participatingShopCount?: number;
  /** Σ operational shop-days. */
  shopDays: number;
  /** Period orders with non-null ShopId. */
  eligibleShopOrderCount: number;
  /** Period orders with null ShopId. */
  unassignedOrderCount: number;
  /** unassignedOrderCount / orderCount × 100 */
  unassignedOrderRate?: number | null;
  orderCount: number;
  revenue: number;
  cost: number;
  profit: number;
  profitMargin?: number | null;
  averageRevenuePerOrder?: number | null;
  averageProfitPerOrder?: number | null;
  /** orderCount / dayCount */
  averageOrdersPerCalendarDay: number;
  /** orderCount / sellerOperationalDays */
  averageOrdersPerOperationalDay?: number | null;
  /** eligibleShopOrderCount / operationalShopCount (period total) */
  averageOrdersPerOperationalShop?: number | null;
  /** eligibleShopOrderCount / shopDays */
  averageOrdersPerShopDay?: number | null;
  /** @deprecated alias of averageOrdersPerCalendarDay */
  averageOrdersPerDay?: number;
  /** @deprecated alias of averageOrdersPerOperationalShop */
  averageOrdersPerActiveShop?: number | null;
  /** @deprecated alias of averageOrdersPerShopDay */
  averageOrdersPerActiveShopPerDay?: number | null;
}

export interface SellerPerformanceRow {
  userId: string;
  displayName: string;
  orders: number;
  activeShopCount: number;
  deactivatedShopCount: number;
  suspendedShopCount: number;
  /** Current total shops for this seller (all statuses). */
  shopCount: number;
  sellerOperationalDays: number;
  operationalShopCount: number;
  participatingShopCount?: number;
  shopDays: number;
  eligibleShopOrderCount: number;
  unassignedOrderCount: number;
  unassignedOrderRate?: number | null;
  revenue: number;
  cost: number;
  profit: number;
  profitMargin?: number | null;
  averageRevenuePerOrder?: number | null;
  averageProfitPerOrder?: number | null;
  averageOrdersPerCalendarDay: number;
  averageOrdersPerOperationalDay?: number | null;
  averageOrdersPerOperationalShop?: number | null;
  averageOrdersPerShopDay?: number | null;
  /** @deprecated */
  averageOrdersPerDay?: number;
  /** @deprecated */
  averageOrdersPerActiveShop?: number | null;
  /** @deprecated */
  averageOrdersPerActiveShopPerDay?: number | null;
}

export interface SellerPerformanceDay {
  date: string;
  orders: number;
  eligibleShopOrders: number;
  operationalShopCount: number;
  ordersPerDay: number;
  ordersPerShop?: number | null;
}

export interface SellerPerformance {
  timezone: string;
  period: DashboardPeriod;
  fromDate: string;
  toDate: string;
  totals: SellerPerformanceTotals;
  /** Daily series for all sellers in scope, or one seller when ownerUserId is set. */
  days: SellerPerformanceDay[];
  sellers: SellerPerformanceRow[];
}

