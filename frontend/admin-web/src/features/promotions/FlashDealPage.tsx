import {
  ADMIN_PROMOTIONS_CONFIG,
  FlashDealPage as SharedFlashDealPage,
  PromotionsDepsProvider,
  type PromotionsDeps,
} from '@drop/promotions';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_FLASH_DEALS_PAGE_SIZE,
  useAdminFlashDeals,
  useAdminFlashDealSyncJob,
  useDeactivateAdminFlashDeal,
  useSyncAdminFlashDeals,
} from '../../hooks/useAdminFlashDeals';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useSellers } from '../../hooks/useSellers';

function useAdminFlashDealDeps(): PromotionsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useList: useAdminFlashDeals,
    useShopOptions: useAdminShopOptions,
    useSellers,
    useSync: useSyncAdminFlashDeals,
    useSyncJob: useAdminFlashDealSyncJob,
    useDeactivate: useDeactivateAdminFlashDeal,
    hasPermission,
    defaultPageSize: ADMIN_FLASH_DEALS_PAGE_SIZE,
  };
}

/** Admin Flash Deal shell — wires portal deps into shared package. */
export const FlashDealPage = () => {
  const deps = useAdminFlashDealDeps();
  return (
    <PromotionsDepsProvider value={deps}>
      <SharedFlashDealPage config={ADMIN_PROMOTIONS_CONFIG} />
    </PromotionsDepsProvider>
  );
};
