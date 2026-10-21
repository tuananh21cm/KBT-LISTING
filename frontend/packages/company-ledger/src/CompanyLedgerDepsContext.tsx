import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type {
  CompanyLedgerCategory,
  CompanyLedgerList,
  CompanyLedgerQuery,
  SaveCompanyLedgerEntryRequest,
} from '@drop/types/companyLedger';

export type CompanyLedgerShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  ownerId?: string | null;
  status?: string | null;
};

export type CompanyLedgerSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type SaveCompanyLedgerVars = {
  id?: string;
  body: SaveCompanyLedgerEntryRequest;
};

export type CompanyLedgerDeps = {
  useLedger: (
    query: CompanyLedgerQuery,
  ) => Pick<UseQueryResult<CompanyLedgerList>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useCategories: () => Pick<UseQueryResult<CompanyLedgerCategory[]>, 'data' | 'isLoading'>;
  useShops: () => Pick<UseQueryResult<CompanyLedgerShopOption[]>, 'data' | 'isLoading'>;
  useSellers?: () => Pick<UseQueryResult<CompanyLedgerSellerOption[]>, 'data' | 'isLoading'>;
  saveEntry?: Pick<
    UseMutationResult<unknown, Error, SaveCompanyLedgerVars>,
    'mutateAsync' | 'isPending'
  >;
  deleteEntry?: Pick<UseMutationResult<unknown, Error, string>, 'mutateAsync' | 'isPending'>;
  hasPermission: (permission: string) => boolean;
  useThemeMode?: () => string;
};

const CompanyLedgerDepsContext = createContext<CompanyLedgerDeps | null>(null);

export const CompanyLedgerDepsProvider = ({
  value,
  children,
}: {
  value: CompanyLedgerDeps;
  children: ReactNode;
}) => <CompanyLedgerDepsContext.Provider value={value}>{children}</CompanyLedgerDepsContext.Provider>;

export const useCompanyLedgerDeps = (): CompanyLedgerDeps => {
  const deps = useContext(CompanyLedgerDepsContext);
  if (!deps) {
    throw new Error('useCompanyLedgerDeps must be used within a <CompanyLedgerDepsProvider>');
  }
  return deps;
};
