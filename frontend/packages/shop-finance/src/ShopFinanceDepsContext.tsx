import { createContext, useContext, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import type { ReceivingBankTransaction, ReceivingBankTransactionQuery } from '@drop/types/receivingBank';
import type { ShopFinance, ShopFinanceQuery } from '@drop/types/shopFinance';

/** Admin-only seller option for the owner filter. */
export type ShopFinanceSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

/**
 * Hooks/data the shared Shop Finance page needs, without importing anything app-specific.
 */
export type ShopFinanceDeps = {
  useFinance: (
    params: ShopFinanceQuery,
  ) => Pick<UseQueryResult<ShopFinance | null | undefined>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  /** Admin only — seller dropdown. */
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ShopFinanceSellerOption[]>, 'data' | 'isLoading'>;
  /** Subscribe so KPI chips re-evaluate when theme toggles. */
  useThemeMode?: () => string;
  hasPermission: (permission: string) => boolean;
  /** Client route for lịch sử tiền về (appended `?shopId=`). */
  transactionsPath: string;
  /** Ledger rows for the shop money-received drawer. */
  useTransactions: (
    query: ReceivingBankTransactionQuery,
    enabled?: boolean,
  ) => Pick<UseQueryResult<ReceivingBankTransaction[]>, 'data' | 'isLoading' | 'isFetching'>;
};

const ShopFinanceDepsContext = createContext<ShopFinanceDeps | null>(null);

export const ShopFinanceDepsProvider = ({
  value,
  children,
}: {
  value: ShopFinanceDeps;
  children: ReactNode;
}) => <ShopFinanceDepsContext.Provider value={value}>{children}</ShopFinanceDepsContext.Provider>;

export const useShopFinanceDeps = (): ShopFinanceDeps => {
  const deps = useContext(ShopFinanceDepsContext);
  if (!deps) {
    throw new Error('useShopFinanceDeps must be used within a <ShopFinanceDepsProvider>');
  }
  return deps;
};
