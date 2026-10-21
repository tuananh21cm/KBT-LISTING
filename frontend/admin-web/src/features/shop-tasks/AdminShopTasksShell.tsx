import { useMemo, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  ADMIN_SHOP_TASKS_CONFIG,
  ShopTasksDepsProvider,
  type ShopTasksDeps,
  type ShopTasksPortalConfig,
} from '@drop/shop-tasks';
import { useAuthStore } from '../../stores/authStore';
import { useSellers } from '../../hooks/useSellers';
import {
  useAdminListingDelistSuggestions,
  useDeactivateListing,
  useDeleteAdminListing,
} from '../../hooks/useAdminListings';
import {
  useAdminShopTaskBoard,
  useAdminShopTaskDefinitions,
  useDeleteAdminShopTaskDefinition,
  useMarkAdminHealthRecordHandled,
  useReorderAdminShopTaskDefinitions,
  useSaveAdminShopTaskDefinition,
} from '../../hooks/useAdminShopTasks';

export const adminShopTasksConfig: ShopTasksPortalConfig = ADMIN_SHOP_TASKS_CONFIG;

/** Wraps shared shop-tasks pages with admin deps + portal config. */
export const AdminShopTasksShell = ({ children }: { children: ReactNode }) => {
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const navigate = useNavigate();

  const deps = useMemo<ShopTasksDeps>(
    () => ({
      useTaskBoard: useAdminShopTaskBoard,
      useOwners: () => useSellers(),
      useTaskDefinitions: useAdminShopTaskDefinitions,
      useSaveTaskDefinition: useSaveAdminShopTaskDefinition,
      useDeleteTaskDefinition: useDeleteAdminShopTaskDefinition,
      useReorderTaskDefinitions: useReorderAdminShopTaskDefinitions,
      useMarkHealthRecordHandled: useMarkAdminHealthRecordHandled,
      useListingDelistSuggestions: useAdminListingDelistSuggestions,
      useDeactivateListing,
      useDeleteListing: useDeleteAdminListing,
      hasPermission,
      navigate: (path) => navigate(path),
    }),
    [hasPermission, navigate],
  );

  return <ShopTasksDepsProvider value={deps}>{children}</ShopTasksDepsProvider>;
};
