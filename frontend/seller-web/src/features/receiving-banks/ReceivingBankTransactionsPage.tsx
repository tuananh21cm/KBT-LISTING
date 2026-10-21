import {
  SELLER_RECEIVING_BANKS_TX_CONFIG,
  ReceivingBanksDepsProvider,
  ReceivingBankTransactionsPage as SharedReceivingBankTransactionsPage,
  type ReceivingBanksDeps,
} from '@drop/receiving-banks';
import { useAuthStore } from '../../stores/authStore';
import {
  useCreateReceivingBankTransaction,
  useDeleteReceivingBankTransaction,
  useReceivingBankAccountOptions,
  useReceivingBankTransactions,
  useUpdateReceivingBankTransaction,
} from '../../hooks/useReceivingBanks';
import { useShops } from '../../hooks/useShops';
import { usePlatformOptions } from '../../hooks/usePlatforms';

function useSellerReceivingBanksDeps(): ReceivingBanksDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useTransactions: useReceivingBankTransactions,
    useAccountOptions: useReceivingBankAccountOptions,
    useShopOptions: useShops,
    usePlatformOptions,
    useCreateTransaction: useCreateReceivingBankTransaction,
    useUpdateTransaction: useUpdateReceivingBankTransaction,
    useDeleteTransaction: useDeleteReceivingBankTransaction,
    hasPermission,
  };
}

/** Seller Lịch sử tiền về — create Pending Credit; edit/delete own Pending only. */
export const ReceivingBankTransactionsPage = () => {
  const deps = useSellerReceivingBanksDeps();
  return (
    <ReceivingBanksDepsProvider value={deps}>
      <SharedReceivingBankTransactionsPage config={SELLER_RECEIVING_BANKS_TX_CONFIG} />
    </ReceivingBanksDepsProvider>
  );
};
