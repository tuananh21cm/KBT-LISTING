import {
  ADMIN_PRODUCT_OPPORTUNITIES_CONFIG,
  ProductOpportunitiesDepsProvider,
  ProductOpportunitiesPage as SharedProductOpportunitiesPage,
  type ProductOpportunitiesDeps,
} from '@drop/product-opportunities';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_PRODUCT_OPPORTUNITIES_PAGE_SIZE,
  useAdminProductOpportunities,
  useAdminShopKeywordResearch,
  useGetAdminShopKeywordResearch,
  useBulkDeleteAdminProductOpportunities,
  useSetSavedAdminProductOpportunity,
} from '../../hooks/useAdminProductOpportunities';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useSellers } from '../../hooks/useSellers';

function useAdminProductOpportunitiesDeps(): ProductOpportunitiesDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useAdminProductOpportunities,
    useShopOptions: useAdminShopOptions,
    useSellers,
    useBulkDelete: useBulkDeleteAdminProductOpportunities,
    useSetSaved: useSetSavedAdminProductOpportunity,
    useShopResearch: useAdminShopKeywordResearch,
    useGetShopResearch: useGetAdminShopKeywordResearch,
    hasPermission,
    defaultPageSize: ADMIN_PRODUCT_OPPORTUNITIES_PAGE_SIZE,
  };
}

/** Admin Product opportunities shell — wires portal deps into shared package. */
export const ProductOpportunitiesPage = () => {
  const deps = useAdminProductOpportunitiesDeps();
  return (
    <ProductOpportunitiesDepsProvider value={deps}>
      <SharedProductOpportunitiesPage config={ADMIN_PRODUCT_OPPORTUNITIES_CONFIG} />
    </ProductOpportunitiesDepsProvider>
  );
};
