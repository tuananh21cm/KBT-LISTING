import {
  SELLER_PROMOTIONS_CONFIG,
  PromotionsDepsProvider,
  ProductDiscountPage as SharedProductDiscountPage,
  type PromotionsDeps,
} from '@drop/promotions';
import { useAuthStore } from '../../stores/authStore';
import {
  PRODUCT_DISCOUNTS_PAGE_SIZE,
  useDeactivateProductDiscount,
  useProductDiscountShopOptions,
  useProductDiscountSyncJob,
  useProductDiscounts,
  useSyncProductDiscounts,
} from '../../hooks/useProductDiscounts';

function useSellerPromotionsDeps(): PromotionsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useProductDiscounts,
    useShopOptions: useProductDiscountShopOptions,
    useSync: useSyncProductDiscounts,
    useSyncJob: useProductDiscountSyncJob,
    useDeactivate: useDeactivateProductDiscount,
    hasPermission,
    defaultPageSize: PRODUCT_DISCOUNTS_PAGE_SIZE,
  };
}

/** Seller Product Discount shell — wires portal deps into shared package. */
export const ProductDiscountPage = () => {
  const deps = useSellerPromotionsDeps();
  return (
    <PromotionsDepsProvider value={deps}>
      <SharedProductDiscountPage config={SELLER_PROMOTIONS_CONFIG} />
    </PromotionsDepsProvider>
  );
};
