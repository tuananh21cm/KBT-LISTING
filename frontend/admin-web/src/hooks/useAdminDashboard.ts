import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { adminDashboardApi } from '../services/adminDashboardApi';
import type {
  DashboardQueryParams,
  DashboardTrendsParams,
  SellerPerformanceParams,
} from '../types/dashboard';

/** Dashboard is visited often — keep aggregates warm a bit longer. */
const DASHBOARD_STALE_MS = 120_000;

export const useAdminDashboardSummary = (
  params: DashboardQueryParams = {},
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['adminDashboardSummary', params],
    queryFn: async () => {
      const response = await adminDashboardApi.summary(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được dashboard.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useAdminDashboardTrends = (params: DashboardTrendsParams = { days: 7 }) =>
  useQuery({
    queryKey: ['adminDashboardTrends', params],
    queryFn: async () => {
      const response = await adminDashboardApi.trends(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được xu hướng dashboard.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
  });

export const useAdminDashboardOrderHeatmap = (
  params: DashboardTrendsParams = { days: 7 },
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['adminDashboardOrderHeatmap', params],
    queryFn: async () => {
      const response = await adminDashboardApi.orderHeatmap(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được heatmap đơn hàng.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useAdminDashboardOrderHourlyHeatmap = (
  params: DashboardTrendsParams = { days: 7 },
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['adminDashboardOrderHourlyHeatmap', params],
    queryFn: async () => {
      const response = await adminDashboardApi.orderHourlyHeatmap(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được heatmap đơn theo giờ.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useAdminSellerPerformance = (
  params: SellerPerformanceParams = {},
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['adminSellerPerformance', params],
    queryFn: async () => {
      const response = await adminDashboardApi.sellerPerformance(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được hiệu suất seller.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });
