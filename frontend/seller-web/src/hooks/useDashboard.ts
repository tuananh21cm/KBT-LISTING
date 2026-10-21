import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { dashboardApi } from '../services/dashboardApi';
import type {
  DashboardQueryParams,
  DashboardTrendsParams,
  SellerPerformanceParams,
} from '../types/dashboard';

const DASHBOARD_STALE_MS = 120_000;

export const useDashboardSummary = (
  params: DashboardQueryParams = {},
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['sellerDashboardSummary', params],
    queryFn: async () => {
      const response = await dashboardApi.summary(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được dashboard.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useDashboardTrends = (params: DashboardTrendsParams = { days: 7 }) =>
  useQuery({
    queryKey: ['sellerDashboardTrends', params],
    queryFn: async () => {
      const response = await dashboardApi.trends(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được xu hướng dashboard.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
  });

export const useDashboardOrderHeatmap = (
  params: DashboardTrendsParams = { days: 7 },
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['sellerDashboardOrderHeatmap', params],
    queryFn: async () => {
      const response = await dashboardApi.orderHeatmap(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được heatmap đơn hàng.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useDashboardOrderHourlyHeatmap = (
  params: DashboardTrendsParams = { days: 7 },
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['sellerDashboardOrderHourlyHeatmap', params],
    queryFn: async () => {
      const response = await dashboardApi.orderHourlyHeatmap(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được heatmap đơn theo giờ.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });

export const useSellerPerformance = (
  params: SellerPerformanceParams = {},
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['sellerSellerPerformance', params],
    queryFn: async () => {
      const response = await dashboardApi.sellerPerformance(params);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được hiệu suất.');
      }
      return response.data!;
    },
    staleTime: DASHBOARD_STALE_MS,
    placeholderData: keepPreviousData,
    enabled: options?.enabled ?? true,
  });
