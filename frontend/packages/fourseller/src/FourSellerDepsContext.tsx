import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { FourSellerShopCheckResponse } from '@drop/types/fourSeller';
import type { PlatformOption } from '@drop/types/platform';
import type { ShopListItem } from '@drop/types/shop';

/** Admin-only seller option for filter dropdown. */
export type FourSellerSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

/**
 * Hooks/data the shared 4Seller connect page needs, without importing anything app-specific.
 * Each portal provides its own implementation backed by its React Query hooks.
 */
export type FourSellerDeps = {
  useShopsList: () => Pick<UseQueryResult<ShopListItem[]>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  usePlatformOptions: () => Pick<UseQueryResult<PlatformOption[]>, 'data' | 'isLoading'>;
  /** Admin only — seller filter. */
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<FourSellerSellerOption[]>, 'data' | 'isLoading'>;
  useAuthRedirect: () => Pick<
    UseMutationResult<string, unknown, string>,
    'mutateAsync' | 'isPending'
  >;
  useCheckConnection: () => Pick<
    UseMutationResult<FourSellerShopCheckResponse, unknown, string>,
    'mutateAsync' | 'isPending'
  >;
  useSyncShops: () => Pick<UseMutationResult<unknown, unknown, void>, 'mutateAsync' | 'isPending'>;
  hasPermission: (permission: string) => boolean;
};

const FourSellerDepsContext = createContext<FourSellerDeps | null>(null);

export const FourSellerDepsProvider = ({
  value,
  children,
}: {
  value: FourSellerDeps;
  children: ReactNode;
}) => <FourSellerDepsContext.Provider value={value}>{children}</FourSellerDepsContext.Provider>;

export const useFourSellerDeps = (): FourSellerDeps => {
  const deps = useContext(FourSellerDepsContext);
  if (!deps) {
    throw new Error('useFourSellerDeps must be used within a <FourSellerDepsProvider>');
  }
  return deps;
};
