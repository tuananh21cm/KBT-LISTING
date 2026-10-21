import {
  SELLER_SHOPS_CONFIG,
  ShopsDepsProvider,
  ShopsPage as SharedShopsPage,
  type CategorySearchHit,
  type ShopsDeps,
} from '@drop/shops';
import { useAuthStore } from '../../stores/authStore';
import {
  SHOPS_DEFAULT_PAGE_SIZE,
  fetchShopDetail,
  useShop,
  useShops,
  useUpdateShop,
  useUpdateShopCatalogProfile,
  useUpdateShopAutoPublish,
  useAgeRanges,
  useAutoPublishSystemConfig,
} from '../../hooks/useShops';
import { usePlatformOptions } from '../../hooks/usePlatforms';
import { CategoryCascader } from '../categories/CategoryCascader';
import { categoryApi } from '../../services/categoryApi';
import type { Category } from '../../types/category';

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

function useSellerShopsDeps(): ShopsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useShopsList: useShops,
    useShopDetail: useShop,
    fetchShopDetail,
    useUpdateShop: useUpdateShop as ShopsDeps['useUpdateShop'],
    useUpdateShopCatalogProfile,
    useUpdateShopAutoPublish,
    useAgeRanges,
    useAutoPublishSystemConfig,
    usePlatformOptions,
    CategoryCascader,
    searchCategories: async ({ site, search }) => {
      const response = await categoryApi.getList({
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
        const response = await categoryApi.recommend({ site, title });
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
        const axiosError = error as { response?: { data?: { message?: string } } };
        throw new Error(
          axiosError.response?.data?.message ||
            (error instanceof Error ? error.message : 'AI không gợi ý được category.'),
        );
      }
    },
    hasPermission,
    defaultPageSize: SHOPS_DEFAULT_PAGE_SIZE,
  };
}

/** Seller Shops shell — wires portal deps into shared `@drop/shops` page. */
export const ShopsPage = () => {
  const deps = useSellerShopsDeps();
  return (
    <ShopsDepsProvider value={deps}>
      <SharedShopsPage config={SELLER_SHOPS_CONFIG} />
    </ShopsDepsProvider>
  );
};
