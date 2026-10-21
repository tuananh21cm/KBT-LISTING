import { useQueryClient } from '@tanstack/react-query';
import {
  ADMIN_SHOPS_CONFIG,
  ShopsDepsProvider,
  ShopsPage as SharedShopsPage,
  type CategorySearchHit,
  type ShopsDeps,
} from '@drop/shops';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_SHOPS_DEFAULT_PAGE_SIZE,
  fetchAdminShopDetail,
  useAdminShop,
  useAdminShops,
  useCreateAdminShop,
  useDeleteAdminShop,
  useReassignAdminShopOwnedData,
  useReassignAllAdminShopOwnedData,
  useAdminShopMappingHealth,
  useReorderAdminShops,
  useUpdateAdminShop,
  useUpdateAdminShopCatalogProfile,
  useUpdateAdminShopAutoPublish,
  useAdminAgeRanges,
  useAdminAutoPublishSystemConfig,
  useSetShopAcquisitionCost,
} from '../../hooks/useAdminShops';
import { usePlatformOptions } from '../../hooks/useAdminPlatforms';
import {
  useReceivingBankAccount,
  useReceivingBankAccountOptions,
} from '../../hooks/useAdminReceivingBanks';
import { useSellers } from '../../hooks/useSellers';
import { CategoryCascader } from '../categories/CategoryCascader';
import { adminCategoryApi } from '../../services/adminCategoryApi';
import { adminShopApi } from '../../services/adminShopApi';
import type { Category } from '../../types/category';
import { getMutationErrorMessage } from '../../utils/apiError';

const CATEGORY_SEARCH_TAKE = 80;

const toCategorySearchHits = (rows: Category[]): CategorySearchHit[] =>
  rows.map((c) => {
    const pathIds = (c.path ?? '')
      .split('/')
      .map((part) => part.trim())
      .filter(Boolean);
    if (pathIds.length === 0 && c.externalId) pathIds.push(c.externalId);
    return {
      id: c.externalId,
      name: c.name,
      pathLabel: c.pathLabel,
      pathIds,
      isLeaf: c.isLeaf,
      level: c.level,
    };
  });

function useAdminShopsDeps(): ShopsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const setAcquisitionCost = useSetShopAcquisitionCost();
  const queryClient = useQueryClient();

  return {
    useShopsList: useAdminShops,
    useShopDetail: useAdminShop,
    fetchShopDetail: fetchAdminShopDetail,
    useCreateShop: useCreateAdminShop,
    useUpdateShop: useUpdateAdminShop as ShopsDeps['useUpdateShop'],
    useUpdateShopCatalogProfile: useUpdateAdminShopCatalogProfile,
    useUpdateShopAutoPublish: useUpdateAdminShopAutoPublish,
    useAgeRanges: useAdminAgeRanges,
    useDeleteShop: useDeleteAdminShop,
    useReassignOwnedData: useReassignAdminShopOwnedData,
    useReassignAllOwnedData: useReassignAllAdminShopOwnedData,
    useMappingHealth: useAdminShopMappingHealth,
    useReorderShops: useReorderAdminShops,
    useAutoPublishSystemConfig: useAdminAutoPublishSystemConfig,
    usePlatformOptions,
    useSellers,
    useReceivingBankOptions: useReceivingBankAccountOptions,
    useReceivingBankDetail: useReceivingBankAccount,
    CategoryCascader,
    searchCategories: async ({ site, search }) => {
      const response = await adminCategoryApi.getList({
        site,
        search,
        activeOnly: true,
        take: CATEGORY_SEARCH_TAKE,
      });
      if (!response.success) {
        throw new Error(response.message || 'Không tìm được category.');
      }
      return toCategorySearchHits(response.data ?? []);
    },
    recommendCategory: async ({ site, title }) => {
      try {
        const response = await adminCategoryApi.recommend({ site, title });
        if (!response.success || !response.data) {
          throw new Error(response.message || 'AI không gợi ý được category.');
        }
        const data = response.data;
        return {
          id: data.categoryId,
          name: data.categoryName,
          pathLabel: data.nodePath,
          pathIds: data.pathIds?.length ? data.pathIds : [data.categoryId],
          isLeaf: data.isLeaf,
        };
      } catch (error) {
        throw new Error(getMutationErrorMessage(error));
      }
    },
    hasPermission,
    setAcquisitionCost,
    importShops: {
      create: (data) => adminShopApi.create!(data),
      update: (id, data) => adminShopApi.update(id, data),
      invalidate: async () => {
        await queryClient.invalidateQueries({ queryKey: ['adminShops'] });
        await queryClient.invalidateQueries({ queryKey: ['adminShopOptions'] });
        // Import writes bypass useUpdateAdminShop, so drop per-shop detail too.
        await queryClient.invalidateQueries({ queryKey: ['adminShop'] });
      },
    },
    defaultPageSize: ADMIN_SHOPS_DEFAULT_PAGE_SIZE,
  };
}

/** Admin Shops shell — wires portal deps into shared `@drop/shops` page. */
export const ShopsPage = () => {
  const deps = useAdminShopsDeps();
  return (
    <ShopsDepsProvider value={deps}>
      <SharedShopsPage config={ADMIN_SHOPS_CONFIG} />
    </ShopsDepsProvider>
  );
};
