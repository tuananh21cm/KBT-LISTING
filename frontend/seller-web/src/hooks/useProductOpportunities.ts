import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { productOpportunityApi } from '../services/productOpportunityApi';
import { shopApi } from '../services/shopApi';
import type {
  ProductOpportunityListPage,
  ProductOpportunityListParams,
} from '@drop/types/productOpportunity';
import type { ProductOpportunitiesShopOption } from '@drop/product-opportunities';

const EMPTY_PAGE = {
  items: [],
  total: 0,
  pageCurrent: 1,
  pageSize: 50,
  lastSyncedAt: null,
  leadTagOptions: [],
};

export const PRODUCT_OPPORTUNITIES_PAGE_SIZE = 50;

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

export const useProductOpportunities = (params: ProductOpportunityListParams) =>
  useQuery({
    queryKey: ['productOpportunities', params],
    queryFn: async () => {
      const response = await productOpportunityApi.list(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? EMPTY_PAGE;
    },
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

export const useBulkDeleteProductOpportunities = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ ids, shopId }: { ids: string[]; shopId?: string }) => {
      const response = await productOpportunityApi.bulkDelete(ids, shopId);
      if (!response.success) {
        throw new Error(response.message || 'Xóa keyword thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || `Đã xóa ${response.data?.deleted ?? 0} keyword.`);
      void queryClient.invalidateQueries({ queryKey: ['productOpportunities'] });
      void queryClient.invalidateQueries({ queryKey: ['shopKeywordResearch'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useSetSavedProductOpportunity = () => {
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
          ? await productOpportunityApi.setSavedKeyword(shopId, keyword, saved)
          : await productOpportunityApi.setSaved(id, saved, shopId);
      if (!response.success) {
        throw new Error(response.message || 'Lưu keyword thất bại.');
      }
      return response;
    },
    onSuccess: (response, vars) => {
      const saved = response.data?.saved ?? vars.saved;
      queryClient.setQueriesData<ProductOpportunityListPage>(
        { queryKey: ['productOpportunities'] },
        (old) => patchSavedInCache(old, vars.id, saved, vars.shopId),
      );
      void queryClient.invalidateQueries({ queryKey: ['productOpportunities'] });
      void queryClient.invalidateQueries({ queryKey: ['shopKeywordResearch'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useProductOpportunityShopOptions = (enabled = true) =>
  useQuery({
    queryKey: ['shops'],
    queryFn: async () => {
      const response = await shopApi.list();
      if (!response.success) {
        throw new Error(response.message);
      }
      return (response.data ?? []).map(
        (s): ProductOpportunitiesShopOption => ({
          id: s.id,
          name: s.name,
          profileName: s.profileName,
          bodySizeCode: s.bodySizeCode,
        }),
      );
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useShopKeywordResearch = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (shopId: string) => {
      const response = await productOpportunityApi.shopResearch(shopId);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Phân tích shop thất bại.');
      }
      return response;
    },
    onSuccess: (_response, shopId) => {
      void queryClient.invalidateQueries({ queryKey: ['shopKeywordResearch', shopId] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useGetShopKeywordResearch = (shopId?: string, enabled = true) =>
  useQuery({
    queryKey: ['shopKeywordResearch', shopId],
    queryFn: async () => {
      const response = await productOpportunityApi.getShopResearch(shopId!);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được phân tích shop.');
      }
      return response.data ?? null;
    },
    enabled: enabled && !!shopId,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
