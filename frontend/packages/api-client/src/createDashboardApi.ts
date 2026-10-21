import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  DashboardQueryParams,
  DashboardSummary,
  DashboardTrends,
  DashboardTrendsParams,
  DashboardOrderHeatmap,
  DashboardOrderHourlyHeatmap,
  SellerPerformance,
  SellerPerformanceParams,
} from '@drop/types/dashboard';

export type DashboardApiBasePath = '/dashboard' | '/admin/dashboard';

export type DashboardApi = {
  summary: (params?: DashboardQueryParams) => Promise<ApiResponse<DashboardSummary>>;
  trends: (params?: DashboardTrendsParams) => Promise<ApiResponse<DashboardTrends>>;
  orderHeatmap: (params?: DashboardTrendsParams) => Promise<ApiResponse<DashboardOrderHeatmap>>;
  orderHourlyHeatmap: (
    params?: DashboardTrendsParams,
  ) => Promise<ApiResponse<DashboardOrderHourlyHeatmap>>;
  sellerPerformance: (
    params?: SellerPerformanceParams,
  ) => Promise<ApiResponse<SellerPerformance>>;
};

/**
 * Shared dashboard API. Admin: `/admin/dashboard`. Seller: `/dashboard`
 * (seller-performance is JWT-scoped on seller).
 */
export function createDashboardApi(
  http: AxiosInstance,
  opts: { basePath: DashboardApiBasePath },
): DashboardApi {
  const { basePath } = opts;

  return {
    summary: async (params: DashboardQueryParams = {}) => {
      const response = await http.get<ApiResponse<DashboardSummary>>(`${basePath}/summary`, {
        params,
      });
      return response.data;
    },

    trends: async (params: DashboardTrendsParams = {}) => {
      const hasCustomRange = Boolean(params.fromDate && params.toDate);
      const response = await http.get<ApiResponse<DashboardTrends>>(`${basePath}/trends`, {
        params: hasCustomRange ? params : { period: 'day', days: 7, ...params },
      });
      return response.data;
    },

    orderHeatmap: async (params: DashboardTrendsParams = {}) => {
      const response = await http.get<ApiResponse<DashboardOrderHeatmap>>(
        `${basePath}/order-heatmap`,
        { params: { period: 'day', days: 7, ...params } },
      );
      return response.data;
    },

    orderHourlyHeatmap: async (params: DashboardTrendsParams = {}) => {
      const response = await http.get<ApiResponse<DashboardOrderHourlyHeatmap>>(
        `${basePath}/order-hourly-heatmap`,
        { params: { period: 'day', days: 7, ...params } },
      );
      return response.data;
    },

    sellerPerformance: async (params: SellerPerformanceParams = {}) => {
      const response = await http.get<ApiResponse<SellerPerformance>>(
        `${basePath}/seller-performance`,
        { params },
      );
      return response.data;
    },
  };
}
