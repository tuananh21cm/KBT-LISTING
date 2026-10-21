import { useMemo, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  SELLER_SHOP_TASKS_CONFIG,
  ShopTasksDepsProvider,
  type ShopTasksDeps,
  type ShopTasksPortalConfig,
} from '@drop/shop-tasks';
import { useAuthStore } from '../../stores/authStore';
import {
  useDeactivateListing,
  useDeleteListing,
  useListingDelistSuggestions,
} from '../../hooks/useListings';
import { useMarkShopHealthRecordHandled, useShopTaskBoard } from '../../hooks/useShopTasks';

export const sellerShopTasksConfig: ShopTasksPortalConfig = SELLER_SHOP_TASKS_CONFIG;

/** Wraps shared shop-tasks pages with seller deps + portal config. */
export const SellerShopTasksShell = ({ children }: { children: ReactNode }) => {
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const navigate = useNavigate();

  const deps = useMemo<ShopTasksDeps>(
    () => ({
      useTaskBoard: useShopTaskBoard,
      useMarkHealthRecordHandled: useMarkShopHealthRecordHandled,
      useListingDelistSuggestions,
      useDeactivateListing,
      useDeleteListing,
      hasPermission,
      navigate: (path) => navigate(path),
    }),
    [hasPermission, navigate],
  );

  return <ShopTasksDepsProvider value={deps}>{children}</ShopTasksDepsProvider>;
};
