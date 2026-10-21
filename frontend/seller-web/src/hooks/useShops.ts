import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { useShopAutoPublishSchedulerPoll } from '@drop/shops';
import { LISTING_AUTO_PUBLISH_DEFAULTS } from '@drop/ui';
import { shopApi } from '../services/shopApi';
import {
  pickShopCatalogFields,
  pickShopAutoPublishFields,
  type ShopDetail,
  type ShopListItem,
  type UpdateSellerShopRequest,
  type UpdateShopCatalogProfileRequest,
  type UpdateShopAutoPublishRequest,
} from '../types/shop';

export const SHOPS_DEFAULT_PAGE_SIZE = 50;

export const useShops = () => {
  const listQueryKey = ['shops'] as const;

  const query = useQuery({
    queryKey: listQueryKey,
    queryFn: async () => {
      const response = await shopApi.list();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

  useShopAutoPublishSchedulerPoll({
    listQueryKey,
    shops: query.data ?? [],
    pollQueryKeyPrefix: ['shops'],
    fetchStatuses: async (ids) => {
      const response = await shopApi.autoPublishSchedulerStatus(ids);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data?.items ?? [];
    },
  });

  return query;
};

export const useShop = (id?: string | null, enabled = true) =>
  useQuery({
    queryKey: ['shops', id],
    queryFn: async () => {
      const response = await shopApi.getById(id!);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: enabled && !!id,
  });

export const fetchShopDetail = async (
  queryClient: QueryClient,
  id: string,
): Promise<ShopDetail> => {
  return queryClient.fetchQuery({
    queryKey: ['shops', id],
    queryFn: async () => {
      const response = await shopApi.getById(id);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được shop.');
      }
      return response.data;
    },
  });
};

export const useUpdateShop = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateSellerShopRequest }) =>
      shopApi.update(id, data),
    onSuccess: (response, variables) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật shop thành công.');
        queryClient.invalidateQueries({ queryKey: ['shops'] });
        queryClient.invalidateQueries({ queryKey: ['shops', variables.id] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateShopCatalogProfile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShopCatalogProfileRequest }) =>
      shopApi.updateCatalogProfile(id, data),
    onSuccess: (response, variables) => {
      if (!response.success || !response.data) {
        message.error(response.message || 'Không cập nhật được danh mục shop.');
        return;
      }
      message.success(response.message || 'Cập nhật danh mục shop thành công.');
      const patch = pickShopCatalogFields(response.data);
      queryClient.setQueryData<ShopListItem[]>(['shops'], (prev) =>
        prev?.map((shop) => (shop.id === variables.id ? { ...shop, ...patch } : shop)),
      );
      queryClient.setQueryData<ShopDetail>(['shops', variables.id], (prev) =>
        prev ? { ...prev, ...patch } : prev,
      );
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateShopAutoPublish = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShopAutoPublishRequest }) =>
      shopApi.updateAutoPublish(id, data),
    onSuccess: (response, variables) => {
      if (!response.success || !response.data) {
        message.error(response.message || 'Không cập nhật được auto publish.');
        return;
      }
      message.success(response.message || 'Cập nhật auto publish thành công.');
      const patch = pickShopAutoPublishFields(response.data);
      queryClient.setQueryData<ShopListItem[]>(['shops'], (prev) =>
        prev?.map((shop) => (shop.id === variables.id ? { ...shop, ...patch } : shop)),
      );
      queryClient.setQueryData<ShopDetail>(['shops', variables.id], (prev) =>
        prev ? { ...prev, ...patch } : prev,
      );
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** Seeded age brackets — long staleTime (near-static). */
export const useAgeRanges = () =>
  useQuery({
    queryKey: ['ageRanges'],
    queryFn: async () => {
      const response = await shopApi.listAgeRanges();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    staleTime: 60 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useAutoPublishSystemConfig = (enabled = true) =>
  useQuery({
    queryKey: ['autoPublishSystemConfig'],
    queryFn: async () => {
      const response = await shopApi.getAutoPublishSystemConfig!();
      if (!response.success) {
        throw new Error(response.message || 'Không tải được cấu hình auto publish.');
      }
      return response.data ?? {
        systemAutoPublishEnabled: false,
        defaultIntervalSeconds: LISTING_AUTO_PUBLISH_DEFAULTS.defaultIntervalSeconds,
        defaultMinDraftAgeMinutes: LISTING_AUTO_PUBLISH_DEFAULTS.defaultMinDraftAgeMinutes,
      };
    },
    enabled,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
