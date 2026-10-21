import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { adminProductOpportunityApi } from '../services/adminProductOpportunityApi';
import type {
  ProductOpportunityListPage,
  ProductOpportunityListParams,
} from '@drop/types/productOpportunity';

const EMPTY_PAGE = {
  items: [],
  total: 0,
  pageCurrent: 1,
  pageSize: 50,
  lastSyncedAt: null,
  leadTagOptions: [],
};

export const ADMIN_PRODUCT_OPPORTUNITIES_PAGE_SIZE = 50;

const patchSavedInCache = (
  old: ProductOpportunityListPage | undefined,
  id: string,
  saved: boolean,
  shopId?: string,
): ProductOpportunityListPage | undefined => {
  if (!old) return old;
  const items = old.items
    .map((item) =>
      item.id === id && (!shopId || item.shopId === shopId)
        ? { ...item, isSaved: saved }
        : item,
    )
    .filter((item) => item.isSaved || !!item.leadId);
  return {
    ...old,
    items,
    total: Math.max(0, old.total - (old.items.length - items.length)),
  };
};

export const useAdminProductOpportunities = (params: ProductOpportunityListParams) =>
  useQuery({
    queryKey: ['adminProductOpportunities', params],
    queryFn: async () => {
      const response = await adminProductOpportunityApi.list(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? EMPTY_PAGE;
    },
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

export const useBulkDeleteAdminProductOpportunities = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ ids, shopId }: { ids: string[]; shopId?: string }) => {
      const response = await adminProductOpportunityApi.bulkDelete(ids, shopId);
      if (!response.success) {
        throw new Error(response.message || 'Xóa keyword thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || `Đã xóa ${response.data?.deleted ?? 0} keyword.`);
      void queryClient.invalidateQueries({ queryKey: ['adminProductOpportunities'] });
      void queryClient.invalidateQueries({ queryKey: ['adminShopKeywordResearch'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useSetSavedAdminProductOpportunity = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      saved,
      shopId,
      keyword,
    }: {
      id: string;
      saved: boolean;
      shopId?: string;
      keyword?: string;
    }) => {
      const response =
        shopId && keyword
          ? await adminProductOpportunityApi.setSavedKeyword(shopId, keyword, saved)
          : await adminProductOpportunityApi.setSaved(id, saved, shopId);
      if (!response.success) {
        throw new Error(response.message || 'Lưu keyword thất bại.');
      }
      return response;
    },
    onSuccess: (response, vars) => {
      const saved = response.data?.saved ?? vars.saved;
      queryClient.setQueriesData<ProductOpportunityListPage>(
        { queryKey: ['adminProductOpportunities'] },
        (old) => patchSavedInCache(old, vars.id, saved, vars.shopId),
      );
      void queryClient.invalidateQueries({ queryKey: ['adminProductOpportunities'] });
      void queryClient.invalidateQueries({ queryKey: ['adminShopKeywordResearch'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useAdminShopKeywordResearch = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (shopId: string) => {
      const response = await adminProductOpportunityApi.shopResearch(shopId);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Phân tích shop thất bại.');
      }
      return response;
    },
    onSuccess: (_response, shopId) => {
      void queryClient.invalidateQueries({ queryKey: ['adminShopKeywordResearch', shopId] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useGetAdminShopKeywordResearch = (shopId?: string, enabled = true) =>
  useQuery({
    queryKey: ['adminShopKeywordResearch', shopId],
    queryFn: async () => {
      const response = await adminProductOpportunityApi.getShopResearch(shopId!);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được phân tích shop.');
      }
      return response.data ?? null;
    },
    enabled: enabled && !!shopId,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
