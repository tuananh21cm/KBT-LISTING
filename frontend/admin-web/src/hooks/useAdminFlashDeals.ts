import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import type {
  ProductDiscountListParams,
  ProductDiscountSyncRequest,
} from '@drop/types/productDiscount';
import { adminFlashDealsApi } from '../services/adminFlashDealsApi';

export const ADMIN_FLASH_DEALS_PAGE_SIZE = 50;

export const useAdminFlashDeals = (params: ProductDiscountListParams) =>
  useQuery({
    queryKey: ['adminFlashDeals', params],
    queryFn: async () => {
      const response = await adminFlashDealsApi.list(params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được Flash Deal.');
      }
      return response.data;
    },
    refetchOnWindowFocus: false,
  });

export const useSyncAdminFlashDeals = () =>
  useMutation({
    mutationFn: async (data?: ProductDiscountSyncRequest) => {
      const response = await adminFlashDealsApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ Flash Deal thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

export const useAdminFlashDealSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['adminFlashDealSyncJob', jobId],
    queryFn: async () => {
      const response = await adminFlashDealsApi.syncStatus(jobId!);
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

export const useDeactivateAdminFlashDeal = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const response = await adminFlashDealsApi.deactivate(id);
      if (!response.success) {
        throw new Error(response.message || 'Deactivate Flash Deal thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã deactivate Flash Deal.');
      void queryClient.invalidateQueries({ queryKey: ['adminFlashDeals'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};
