import { createContext, useContext, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import type {
  SupplierSellerDetail,
  SupplierSellerListPage,
  SupplierSellerListParams,
} from '@drop/types/supplierSeller';

export type SupplierListsSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type SupplierListsDeps = {
  useList: (
    params: SupplierSellerListParams,
  ) => Pick<
    UseQueryResult<SupplierSellerListPage>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  > & {
    refetch: () => Promise<unknown>;
  };
  useDetail: (
    id: string | undefined,
    params: SupplierSellerListParams,
    enabled?: boolean,
  ) => Pick<
    UseQueryResult<SupplierSellerDetail | undefined>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  > & { refetch: () => Promise<unknown> };
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<SupplierListsSellerOption[]>, 'data' | 'isLoading'>;
  hasPermission: (permission: string) => boolean;
  defaultPageSize: number;
};

const SupplierListsDepsContext = createContext<SupplierListsDeps | null>(null);

export const SupplierListsDepsProvider = ({
  value,
  children,
}: {
  value: SupplierListsDeps;
  children: ReactNode;
}) => <SupplierListsDepsContext.Provider value={value}>{children}</SupplierListsDepsContext.Provider>;

export const useSupplierListsDeps = (): SupplierListsDeps => {
  const deps = useContext(SupplierListsDepsContext);
  if (!deps) {
    throw new Error('useSupplierListsDeps must be used within a <SupplierListsDepsProvider>');
  }
  return deps;
};
