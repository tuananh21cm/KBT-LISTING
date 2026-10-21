import {
  ADMIN_PROMOTIONS_CONFIG,
  PromotionsDepsProvider,
  ProductDiscountPage as SharedProductDiscountPage,
  type PromotionsDeps,
} from '@drop/promotions';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_PRODUCT_DISCOUNTS_PAGE_SIZE,
  useAdminProductDiscounts,
  useAdminProductDiscountSyncJob,
  useDeactivateAdminProductDiscount,
  useSyncAdminProductDiscounts,
} from '../../hooks/useAdminProductDiscounts';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useSellers } from '../../hooks/useSellers';

function useAdminPromotionsDeps(): PromotionsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useAdminProductDiscounts,
    useShopOptions: useAdminShopOptions,
    useSellers,
    useSync: useSyncAdminProductDiscounts,
    useSyncJob: useAdminProductDiscountSyncJob,
    useDeactivate: useDeactivateAdminProductDiscount,
    hasPermission,
    defaultPageSize: ADMIN_PRODUCT_DISCOUNTS_PAGE_SIZE,
  };
}

/** Admin Product Discount shell — wires portal deps into shared package. */
export const ProductDiscountPage = () => {
  const deps = useAdminPromotionsDeps();
  return (
    <PromotionsDepsProvider value={deps}>
      <SharedProductDiscountPage config={ADMIN_PROMOTIONS_CONFIG} />
    </PromotionsDepsProvider>
  );
};
