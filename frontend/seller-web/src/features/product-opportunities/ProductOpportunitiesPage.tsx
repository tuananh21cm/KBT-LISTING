import {
  SELLER_PRODUCT_OPPORTUNITIES_CONFIG,
  ProductOpportunitiesDepsProvider,
  ProductOpportunitiesPage as SharedProductOpportunitiesPage,
  type ProductOpportunitiesDeps,
} from '@drop/product-opportunities';
import { useAuthStore } from '../../stores/authStore';
import {
  PRODUCT_OPPORTUNITIES_PAGE_SIZE,
  useProductOpportunities,
  useProductOpportunityShopOptions,
  useBulkDeleteProductOpportunities,
  useSetSavedProductOpportunity,
  useShopKeywordResearch,
  useGetShopKeywordResearch,
} from '../../hooks/useProductOpportunities';

function useSellerProductOpportunitiesDeps(): ProductOpportunitiesDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useProductOpportunities,
    useShopOptions: useProductOpportunityShopOptions,
    useBulkDelete: useBulkDeleteProductOpportunities,
    useSetSaved: useSetSavedProductOpportunity,
    useShopResearch: useShopKeywordResearch,
    useGetShopResearch: useGetShopKeywordResearch,
    hasPermission,
    defaultPageSize: PRODUCT_OPPORTUNITIES_PAGE_SIZE,
  };
}

/** Seller Product opportunities shell — wires portal deps into shared package. */
export const ProductOpportunitiesPage = () => {
  const deps = useSellerProductOpportunitiesDeps();
  return (
    <ProductOpportunitiesDepsProvider value={deps}>
      <SharedProductOpportunitiesPage config={SELLER_PRODUCT_OPPORTUNITIES_CONFIG} />
    </ProductOpportunitiesDepsProvider>
  );
};
