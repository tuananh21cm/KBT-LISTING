import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { useShopAutoPublishSchedulerPoll } from '@drop/shops';
import { LISTING_AUTO_PUBLISH_DEFAULTS } from '@drop/ui';
import { adminShopApi } from '../services/adminShopApi';
import { getMutationErrorMessage } from '../utils/apiError';
import {
  pickShopCatalogFields,
  pickShopAutoPublishFields,
  type CreateShopRequest,
  type ShopDetail,
  type ShopListItem,
  type UpdateAdminShopRequest,
  type UpdateShopCatalogProfileRequest,
  type UpdateShopAutoPublishRequest,
} from '../types/shop';

export const ADMIN_SHOPS_DEFAULT_PAGE_SIZE = 50;

/** Slim id+name options for filter dropdowns. */
export const useAdminShopOptions = (enabled = true) =>
  useQuery({
    queryKey: ['adminShopOptions'],
    queryFn: async () => {
      const response = await adminShopApi.getOptions!();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useAdminShops = () => {
  const listQueryKey = ['adminShops'] as const;

  const query = useQuery({
    queryKey: listQueryKey,
    queryFn: async () => {
      const response = await adminShopApi.list();
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
    pollQueryKeyPrefix: ['adminShops'],
    fetchStatuses: async (ids) => {
      const response = await adminShopApi.autoPublishSchedulerStatus(ids);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data?.items ?? [];
    },
  });

  return query;
};

export const useAdminShop = (id?: string | null, enabled = true) =>
  useQuery({
    queryKey: ['adminShop', id],
    queryFn: async () => {
      const response = await adminShopApi.getById(id!);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: enabled && !!id,
  });

/** Prefetch / fetch full shop detail (edit / view). */
export const fetchAdminShopDetail = async (
  queryClient: QueryClient,
  id: string,
): Promise<ShopDetail> => {
  return queryClient.fetchQuery({
    queryKey: ['adminShop', id],
    queryFn: async () => {
      const response = await adminShopApi.getById(id);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được shop.');
      }
      return response.data;
    },
  });
};

export const useCreateAdminShop = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateShopRequest) => adminShopApi.create!(data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Tạo shop thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateAdminShop = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateAdminShopRequest }) =>
      adminShopApi.update(id, data),
    onSuccess: (response, variables) => {
      if (response.success) {
        message.success(response.message || 'Cập nhật shop thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
        queryClient.invalidateQueries({ queryKey: ['adminShop', variables.id] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useUpdateAdminShopCatalogProfile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShopCatalogProfileRequest }) =>
      adminShopApi.updateCatalogProfile(id, data),
    onSuccess: (response, variables) => {
      if (!response.success || !response.data) {
        message.error(response.message || 'Không cập nhật được danh mục shop.');
        return;
      }
      message.success(response.message || 'Cập nhật danh mục shop thành công.');
      const patch = pickShopCatalogFields(response.data);
      queryClient.setQueryData<ShopListItem[]>(['adminShops'], (prev) =>
        prev?.map((shop) => (shop.id === variables.id ? { ...shop, ...patch } : shop)),
      );
      queryClient.setQueryData<ShopDetail>(['adminShop', variables.id], (prev) =>
        prev ? { ...prev, ...patch } : prev,
      );
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useUpdateAdminShopAutoPublish = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShopAutoPublishRequest }) =>
      adminShopApi.updateAutoPublish(id, data),
    onSuccess: (response, variables) => {
      if (!response.success || !response.data) {
        message.error(response.message || 'Không cập nhật được auto publish.');
        return;
      }
      message.success(response.message || 'Cập nhật auto publish thành công.');
      const patch = pickShopAutoPublishFields(response.data);
      queryClient.setQueryData<ShopListItem[]>(['adminShops'], (prev) =>
        prev?.map((shop) => (shop.id === variables.id ? { ...shop, ...patch } : shop)),
      );
      queryClient.setQueryData<ShopDetail>(['adminShop', variables.id], (prev) =>
        prev ? { ...prev, ...patch } : prev,
      );
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** Seeded age brackets — long staleTime (near-static). */
export const useAdminAgeRanges = () =>
  useQuery({
    queryKey: ['adminAgeRanges'],
    queryFn: async () => {
      const response = await adminShopApi.listAgeRanges();
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    staleTime: 60 * 60_000,
    refetchOnWindowFocus: false,
  });

export const useDeleteAdminShop = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminShopApi.delete!(id),
    onSuccess: (response, id) => {
      if (response.success) {
        message.success(response.message || 'Xóa shop thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
        queryClient.removeQueries({ queryKey: ['adminShop', id] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useReassignAdminShopOwnedData = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminShopApi.reassignOwnedData!(id),
    onSuccess: (response, id) => {
      if (response.success) {
        const counts = response.data;
        const detail = counts
          ? ` · đơn ${counts.ordersTransferred}, gộp trùng ${counts.ordersMerged ?? 0}, listing ${counts.listingsTransferred}, gộp shell ${counts.listingsCollapsed ?? 0}`
          : '';
        message.success(
          (response.message || 'Đã đồng bộ ownership.') + (counts?.hasChanges ? detail : ''),
        );
        queryClient.invalidateQueries({ queryKey: ['adminShop', id] });
        queryClient.invalidateQueries({ queryKey: ['adminShopMappingHealth'] });
        queryClient.invalidateQueries({ queryKey: ['adminOrders'] });
        queryClient.invalidateQueries({ queryKey: ['adminListings'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useReassignAllAdminShopOwnedData = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => adminShopApi.reassignAllOwnedData!(),
    onSuccess: (response) => {
      if (response.success) {
        const counts = response.data;
        const detail = counts
          ? ` · ${counts.shopsProcessed ?? 0} shop · đơn ${counts.ordersTransferred}, listing ${counts.listingsTransferred}, gộp shell ${counts.listingsCollapsed ?? 0}`
          : '';
        message.success(
          (response.message || 'Đã đồng bộ ownership tất cả shop.') +
            (counts?.hasChanges ? detail : ''),
        );
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopMappingHealth'] });
        queryClient.invalidateQueries({ queryKey: ['adminOrders'] });
        queryClient.invalidateQueries({ queryKey: ['adminListings'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => message.error(getMutationErrorMessage(error)),
  });
};

export const useAdminShopMappingHealth = (enabled = true) =>
  useQuery({
    queryKey: ['adminShopMappingHealth'],
    queryFn: async () => {
      const response = await adminShopApi.getMappingHealth!();
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được mapping health.');
      }
      return response.data;
    },
    enabled: enabled && Boolean(adminShopApi.getMappingHealth),
    staleTime: 60_000,
  });

export const useReorderAdminShops = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (orderedIds: string[]) => adminShopApi.reorder!({ orderedIds }),
    onMutate: async (orderedIds) => {
      await queryClient.cancelQueries({ queryKey: ['adminShops'] });
      const previous = queryClient.getQueryData<ShopListItem[]>(['adminShops']);
      if (previous) {
        const byId = new Map(previous.map((shop) => [shop.id, shop]));
        const next: ShopListItem[] = [];
        orderedIds.forEach((id, index) => {
          const shop = byId.get(id);
          if (shop) next.push({ ...shop, sortOrder: index });
        });
        queryClient.setQueryData<ShopListItem[]>(['adminShops'], next);
      }
      return { previous };
    },
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Sắp xếp shop thành công.');
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
      } else {
        message.error(response.message);
        queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
      }
    },
    onError: (error: Error, _orderedIds, context) => {
      if (context?.previous) {
        queryClient.setQueryData(['adminShops'], context.previous);
      }
      message.error(error.message);
      queryClient.invalidateQueries({ queryKey: ['adminShops'] });
      queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
    },
  });
};

export const useAdminAutoPublishSystemConfig = (enabled = true) =>
  useQuery({
    queryKey: ['adminAutoPublishSystemConfig'],
    queryFn: async () => {
      const response = await adminShopApi.getAutoPublishSystemConfig!();
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

/** Writes the unique shop purchase cost. Null or 0 deletes the ledger row. */
export const useSetShopAcquisitionCost = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ shopId, amount }: { shopId: string; amount: number | null }) => {
      const response = await adminShopApi.setAcquisitionCost!(shopId, { amount });
      if (!response.success) throw new Error(response.message || 'Không lưu được giá mua shop.');
      return response.data;
    },
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['companyLedger'] });
      void queryClient.invalidateQueries({ queryKey: ['adminShopFinance'] });
      void queryClient.invalidateQueries({ queryKey: ['adminShop', variables.shopId] });
    },
  });
};
