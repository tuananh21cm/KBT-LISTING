import {
  SELLER_SHOP_FINANCE_CONFIG,
  ShopFinanceDepsProvider,
  ShopFinancePage as SharedShopFinancePage,
  type ShopFinanceDeps,
} from '@drop/shop-finance';
import { useAuthStore } from '../../stores/authStore';
import { useThemeStore } from '../../stores/themeStore';
import { useShopFinance } from '../../hooks/useShopFinance';
import { useReceivingBankTransactions } from '../../hooks/useReceivingBanks';

function useSellerShopFinanceDeps(): ShopFinanceDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useFinance: useShopFinance,
    useThemeMode: () => useThemeStore((s) => s.mode),
    hasPermission,
    transactionsPath: '/receiving-banks/transactions',
    useTransactions: useReceivingBankTransactions,
  };
}

/** Seller Shop Finance shell — wires portal deps into shared `@drop/shop-finance`. */
export const ShopFinancePage = () => {
  const deps = useSellerShopFinanceDeps();
  return (
    <ShopFinanceDepsProvider value={deps}>
      <SharedShopFinancePage config={SELLER_SHOP_FINANCE_CONFIG} />
    </ShopFinanceDepsProvider>
  );
};
