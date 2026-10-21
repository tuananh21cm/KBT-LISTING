import {
  ADMIN_SHOP_FINANCE_CONFIG,
  ShopFinanceDepsProvider,
  ShopFinancePage as SharedShopFinancePage,
  type ShopFinanceDeps,
} from '@drop/shop-finance';
import { useAuthStore } from '../../stores/authStore';
import { useThemeStore } from '../../stores/themeStore';
import { useAdminShopFinance } from '../../hooks/useAdminShopFinance';
import { useReceivingBankTransactions } from '../../hooks/useAdminReceivingBanks';
import { useSellers } from '../../hooks/useSellers';

function useAdminShopFinanceDeps(): ShopFinanceDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useFinance: useAdminShopFinance,
    useSellers,
    useThemeMode: () => useThemeStore((s) => s.mode),
    hasPermission,
    transactionsPath: '/receiving-banks/transactions',
    useTransactions: useReceivingBankTransactions,
  };
}

/** Admin Shop Finance shell — wires portal deps into shared `@drop/shop-finance`. */
export const ShopFinancePage = () => {
  const deps = useAdminShopFinanceDeps();
  return (
    <ShopFinanceDepsProvider value={deps}>
      <SharedShopFinancePage config={ADMIN_SHOP_FINANCE_CONFIG} />
    </ShopFinanceDepsProvider>
  );
};
