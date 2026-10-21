import {
  ADMIN_RECEIVING_BANKS_TX_CONFIG,
  ReceivingBanksDepsProvider,
  ReceivingBankTransactionsPage as SharedReceivingBankTransactionsPage,
  type ReceivingBanksDeps,
} from '@drop/receiving-banks';
import { useAuthStore } from '../../stores/authStore';
import {
  useConfirmReceivingBankTransaction,
  useCreateReceivingBankTransaction,
  useDeleteReceivingBankTransaction,
  useReceivingBankAccountOptions,
  useReceivingBankTransactions,
  useRejectReceivingBankTransaction,
  useSetReceivingBankConfirmationStatus,
  useUpdateReceivingBankTransaction,
} from '../../hooks/useAdminReceivingBanks';
import { useAdminShops } from '../../hooks/useAdminShops';
import { usePlatformOptions } from '../../hooks/useAdminPlatforms';
import { useSellers } from '../../hooks/useSellers';

function useAdminReceivingBanksDeps(): ReceivingBanksDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useTransactions: useReceivingBankTransactions,
    useAccountOptions: useReceivingBankAccountOptions,
    useShopOptions: useAdminShops,
    useSellers,
    usePlatformOptions,
    useCreateTransaction: useCreateReceivingBankTransaction,
    useUpdateTransaction: useUpdateReceivingBankTransaction,
    useDeleteTransaction: useDeleteReceivingBankTransaction,
    useConfirmTransaction: useConfirmReceivingBankTransaction,
    useRejectTransaction: useRejectReceivingBankTransaction,
    useSetConfirmationStatus: useSetReceivingBankConfirmationStatus,
    hasPermission,
  };
}

/** Admin Receiving Bank Transactions shell — wires portal deps into shared page. */
export const ReceivingBankTransactionsPage = () => {
  const deps = useAdminReceivingBanksDeps();
  return (
    <ReceivingBanksDepsProvider value={deps}>
      <SharedReceivingBankTransactionsPage config={ADMIN_RECEIVING_BANKS_TX_CONFIG} />
    </ReceivingBanksDepsProvider>
  );
};
