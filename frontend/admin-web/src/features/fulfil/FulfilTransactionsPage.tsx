import {
  ADMIN_FULFIL_TX_CONFIG,
  FulfilDepsProvider,
  FulfilTransactionsPage as SharedFulfilTransactionsPage,
  type FulfilDeps,
} from '@drop/fulfil';
import { useAuthStore } from '../../stores/authStore';
import {
  useBulkDeleteFulfilTransactions,
  useCreateFulfilTransaction,
  useDeleteFulfilTransaction,
  useFulfilAccountOptions,
  useFulfilCardOptions,
  useFulfilTransactions,
  useUpdateFulfilTransaction,
} from '../../hooks/useAdminFulfil';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useFulfilUserOptions } from '../../hooks/useAdminUsers';
import { useAdminOrderPreview } from '../../hooks/useAdminOrders';
import {
  useOrderLookup,
  useReassignPurchaseOrder,
} from '../../hooks/useAdminSupplierPurchases';
import { useSellers } from '../../hooks/useSellers';

function useAdminFulfilDeps(): FulfilDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const currentUserId = useAuthStore((s) => s.user?.id);

  return {
    useTransactions: useFulfilTransactions,
    useAccountOptions: useFulfilAccountOptions,
    useCardOptions: useFulfilCardOptions,
    useCreateTransaction: useCreateFulfilTransaction,
    useUpdateTransaction: useUpdateFulfilTransaction,
    useDeleteTransaction: useDeleteFulfilTransaction,
    useBulkDeleteTransactions: useBulkDeleteFulfilTransactions,
    useShopOptions: useAdminShopOptions,
    useSellers,
    // Chỉ role Admin / Fulfil mới đi đơn — options id/tên (active, cache 5′), không kéo full user.
    useUsers: useFulfilUserOptions,
    useOrderLookup,
    useReassignPurchaseOrder,
    useOrderPreview: useAdminOrderPreview,
    hasPermission,
    currentUserId,
  };
}

/** Admin Lịch sử mua hàng — wires portal deps into shared page. */
export const FulfilTransactionsPage = () => {
  const deps = useAdminFulfilDeps();
  return (
    <FulfilDepsProvider value={deps}>
      <SharedFulfilTransactionsPage config={ADMIN_FULFIL_TX_CONFIG} />
    </FulfilDepsProvider>
  );
};
