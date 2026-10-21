import {
  SELLER_FULFIL_TX_CONFIG,
  FulfilDepsProvider,
  FulfilTransactionsPage as SharedFulfilTransactionsPage,
  type FulfilDeps,
} from '@drop/fulfil';
import { useAuthStore } from '../../stores/authStore';
import {
  useCreateFulfilTransaction,
  useDeleteFulfilTransaction,
  useFulfilAccountOptions,
  useFulfilCardOptions,
  useFulfilTransactions,
  useOrderPreview,
  useUpdateFulfilTransaction,
} from '../../hooks/useFulfil';
import { useShops } from '../../hooks/useShops';

function useSellerFulfilDeps(): FulfilDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const currentUserId = useAuthStore((s) => s.user?.id);

  return {
    useTransactions: useFulfilTransactions,
    useAccountOptions: useFulfilAccountOptions,
    useCardOptions: useFulfilCardOptions,
    useCreateTransaction: useCreateFulfilTransaction,
    useUpdateTransaction: useUpdateFulfilTransaction,
    useDeleteTransaction: useDeleteFulfilTransaction,
    useShopOptions: useShops,
    useOrderPreview,
    hasPermission,
    currentUserId,
  };
}

/** Seller Lịch sử mua hàng — Manual CRUD; CostUpdate view-only; scoped OwnerId. */
export const FulfilTransactionsPage = () => {
  const deps = useSellerFulfilDeps();
  return (
    <FulfilDepsProvider value={deps}>
      <SharedFulfilTransactionsPage config={SELLER_FULFIL_TX_CONFIG} />
    </FulfilDepsProvider>
  );
};
