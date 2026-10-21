import {
  SELLER_COMPANY_LEDGER_CONFIG,
  CompanyLedgerDepsProvider,
  CompanyLedgerPage as SharedCompanyLedgerPage,
  type CompanyLedgerDeps,
} from '@drop/company-ledger';
import '@drop/company-ledger/companyLedger.css';
import { useAuthStore } from '../../stores/authStore';
import { useThemeStore } from '../../stores/themeStore';
import {
  useCompanyLedger,
  useCompanyLedgerCategories,
  useCompanyLedgerShops,
} from '../../hooks/useCompanyLedger';

function useSellerCompanyLedgerDeps(): CompanyLedgerDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  return {
    useLedger: useCompanyLedger,
    useCategories: useCompanyLedgerCategories,
    useShops: useCompanyLedgerShops,
    hasPermission,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Seller shell — read-only rows assigned to this seller or their shops. */
export const CompanyLedgerPage = () => {
  const deps = useSellerCompanyLedgerDeps();
  return (
    <CompanyLedgerDepsProvider value={deps}>
      <SharedCompanyLedgerPage config={SELLER_COMPANY_LEDGER_CONFIG} />
    </CompanyLedgerDepsProvider>
  );
};
