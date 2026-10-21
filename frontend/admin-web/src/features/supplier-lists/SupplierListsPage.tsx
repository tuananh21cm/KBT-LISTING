import {
  ADMIN_SUPPLIER_LISTS_CONFIG,
  SupplierListsDepsProvider,
  SupplierListsPage as SharedSupplierListsPage,
  type SupplierListsDeps,
} from '@drop/supplier-lists';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_SUPPLIER_LISTS_DEFAULT_PAGE_SIZE,
  useAdminSupplierSeller,
  useAdminSupplierSellers,
} from '../../hooks/useAdminSupplierSellers';
import { useSellers } from '../../hooks/useSellers';

function useAdminSupplierListsDeps(): SupplierListsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  return {
    useList: useAdminSupplierSellers,
    useDetail: useAdminSupplierSeller,
    useSellers,
    hasPermission,
    defaultPageSize: ADMIN_SUPPLIER_LISTS_DEFAULT_PAGE_SIZE,
  };
}

export const SupplierListsPage = () => {
  const deps = useAdminSupplierListsDeps();
  return (
    <SupplierListsDepsProvider value={deps}>
      <SharedSupplierListsPage config={ADMIN_SUPPLIER_LISTS_CONFIG} />
    </SupplierListsDepsProvider>
  );
};
