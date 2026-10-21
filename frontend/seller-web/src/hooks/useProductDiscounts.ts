import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import type { PromotionsShopOption } from '@drop/promotions';
import type {
  ProductDiscountListParams,
  ProductDiscountSyncRequest,
} from '@drop/types/productDiscount';
import { promotionsApi } from '../services/promotionsApi';
import { shopApi } from '../services/shopApi';

export const PRODUCT_DISCOUNTS_PAGE_SIZE = 50;

export const useProductDiscounts = (params: ProductDiscountListParams) =>
  useQuery({
    queryKey: ['productDiscounts', params],
    queryFn: async () => {
      const response = await promotionsApi.list(params);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được Product Discount.');
      }
      return response.data;
    },
    refetchOnWindowFocus: false,
  });

export const useProductDiscountShopOptions = (enabled = true) =>
  useQuery({
    queryKey: ['shops'],
    queryFn: async () => {
      const response = await shopApi.list();
      if (!response.success) {
        throw new Error(response.message);
      }
      return (response.data ?? []).map(
        (s): PromotionsShopOption => ({
          id: s.id,
          name: s.name,
          profileName: s.profileName,
        }),
      );
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useSyncProductDiscounts = () =>
  useMutation({
    mutationFn: async (data?: ProductDiscountSyncRequest) => {
      const response = await promotionsApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ Product Discount thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

export const useProductDiscountSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['productDiscountSyncJob', jobId],
    queryFn: async () => {
      const response = await promotionsApi.syncStatus(jobId!);
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

export const useDeactivateProductDiscount = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const response = await promotionsApi.deactivate(id);
      if (!response.success) {
        throw new Error(response.message || 'Deactivate Product Discount thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã deactivate Product Discount.');
      void queryClient.invalidateQueries({ queryKey: ['productDiscounts'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};