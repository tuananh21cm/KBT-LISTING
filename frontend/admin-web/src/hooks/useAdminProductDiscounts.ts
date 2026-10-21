import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import type {
  ProductDiscountListParams,
  ProductDiscountSyncRequest,
} from '@drop/types/productDiscount';
import { adminPromotionsApi } from '../services/adminPromotionsApi';

export const ADMIN_PRODUCT_DISCOUNTS_PAGE_SIZE = 50;

export const useAdminProductDiscounts = (params: ProductDiscountListParams) =>
  useQuery({
    queryKey: ['adminProductDiscounts', params],
    queryFn: async () => {
      const response = await adminPromotionsApi.list(params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được Product Discount.');
      }
      return response.data;
    },
    refetchOnWindowFocus: false,
  });

export const useSyncAdminProductDiscounts = () =>
  useMutation({
    mutationFn: async (data?: ProductDiscountSyncRequest) => {
      const response = await adminPromotionsApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ Product Discount thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

export const useAdminProductDiscountSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['adminProductDiscountSyncJob', jobId],
    queryFn: async () => {
      const response = await adminPromotionsApi.syncStatus(jobId!);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không đọc được tiến độ đồng bộ.');
      }
      return response.data;
    },
    enabled: !!jobId,
    refetchInterval: (query) =>
      query.state.data && query.state.data.state !== 'running' ? false : 2000,
    refetchOnWindowFocus: false,
    gcTime: 0,
    retry: false,
  });

export const useDeactivateAdminProductDiscount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const response = await adminPromotionsApi.deactivate(id);
      if (!response.success) {
        throw new Error(response.message || 'Deactivate Product Discount thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã deactivate Product Discount.');
      void queryClient.invalidateQueries({ queryKey: ['adminProductDiscounts'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};