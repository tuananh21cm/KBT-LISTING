import {
  SELLER_SUPPLIER_LISTS_CONFIG,
  SupplierListsDepsProvider,
  SupplierSellerDetailPage as SharedSupplierSellerDetailPage,
  type SupplierListsDeps,
} from '@drop/supplier-lists';
import { useAuthStore } from '../../stores/authStore';
import {
  SUPPLIER_LISTS_DEFAULT_PAGE_SIZE,
  useSupplierSeller,
  useSupplierSellers,
} from '../../hooks/useSupplierSellers';

function useSellerSupplierListsDeps(): SupplierListsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  return {
    useList: useSupplierSellers,
    useDetail: useSupplierSeller,
    hasPermission,
    defaultPageSize: SUPPLIER_LISTS_DEFAULT_PAGE_SIZE,
  };
}

export const SupplierSellerDetailPage = () => {
  const deps = useSellerSupplierListsDeps();
  return (
    <SupplierListsDepsProvider value={deps}>
      <SharedSupplierSellerDetailPage config={SELLER_SUPPLIER_LISTS_CONFIG} />
    </SupplierListsDepsProvider>
  );
};
