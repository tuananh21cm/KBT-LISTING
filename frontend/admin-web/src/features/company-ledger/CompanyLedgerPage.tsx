import {
  ADMIN_COMPANY_LEDGER_CONFIG,
  CompanyLedgerDepsProvider,
  CompanyLedgerPage as SharedCompanyLedgerPage,
  type CompanyLedgerDeps,
} from '@drop/company-ledger';
import '@drop/company-ledger/companyLedger.css';
import { useAuthStore } from '../../stores/authStore';
import { useThemeStore } from '../../stores/themeStore';
import { useSellers } from '../../hooks/useSellers';
import {
  useAdminCompanyLedger,
  useAdminCompanyLedgerCategories,
  useAdminCompanyLedgerShops,
  useDeleteCompanyLedgerEntry,
  useSaveCompanyLedgerEntry,
} from '../../hooks/useAdminCompanyLedger';

function useAdminCompanyLedgerDeps(): CompanyLedgerDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const saveEntry = useSaveCompanyLedgerEntry();
  const deleteEntry = useDeleteCompanyLedgerEntry();

  return {
    useLedger: useAdminCompanyLedger,
    useCategories: useAdminCompanyLedgerCategories,
    useShops: useAdminCompanyLedgerShops,
    useSellers,
    saveEntry,
    deleteEntry,
    hasPermission,
    useThemeMode: () => useThemeStore((s) => s.mode),
  };
}

/** Admin shell for the shared company ledger page. */
export const CompanyLedgerPage = () => {
  const deps = useAdminCompanyLedgerDeps();
  return (
    <CompanyLedgerDepsProvider value={deps}>
      <SharedCompanyLedgerPage config={ADMIN_COMPANY_LEDGER_CONFIG} />
    </CompanyLedgerDepsProvider>
  );
};
