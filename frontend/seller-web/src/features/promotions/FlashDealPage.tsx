import {
  FlashDealPage as SharedFlashDealPage,
  PromotionsDepsProvider,
  SELLER_PROMOTIONS_CONFIG,
  type PromotionsDeps,
} from '@drop/promotions';
import { useAuthStore } from '../../stores/authStore';
import {
  FLASH_DEALS_PAGE_SIZE,
  useDeactivateFlashDeal,
  useFlashDealShopOptions,
  useFlashDealSyncJob,
  useFlashDeals,
  useSyncFlashDeals,
} from '../../hooks/useFlashDeals';

function useSellerFlashDealDeps(): PromotionsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useFlashDeals,
    useShopOptions: useFlashDealShopOptions,
    useSync: useSyncFlashDeals,
    useSyncJob: useFlashDealSyncJob,
    useDeactivate: useDeactivateFlashDeal,
    hasPermission,
    defaultPageSize: FLASH_DEALS_PAGE_SIZE,
  };
}

/** Seller Flash Deal shell — wires portal deps into shared package. */
export const FlashDealPage = () => {
  const deps = useSellerFlashDealDeps();
  return (
    <PromotionsDepsProvider value={deps}>
      <SharedFlashDealPage config={SELLER_PROMOTIONS_CONFIG} />
    </PromotionsDepsProvider>
  );
};
