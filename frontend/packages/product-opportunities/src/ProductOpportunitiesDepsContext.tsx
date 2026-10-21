import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type {
  ProductOpportunityBulkDeleteResult,
  ProductOpportunityListPage,
  ProductOpportunityListParams,
  ProductOpportunitySavedResult,
  ShopKeywordResearch,
} from '@drop/types/productOpportunity';

export type ProductOpportunitiesShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  ownerId?: string;
  bodySizeCode?: string | null;
};

export type ProductOpportunitiesSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type ProductOpportunitiesDeps = {
  useList: (
    params: ProductOpportunityListParams,
  ) => Pick<UseQueryResult<ProductOpportunityListPage>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useShopOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ProductOpportunitiesShopOption[]>, 'data' | 'isLoading'>;
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ProductOpportunitiesSellerOption[]>, 'data' | 'isLoading'>;
  useBulkDelete: () => Pick<
    UseMutationResult<
      ApiResponse<ProductOpportunityBulkDeleteResult>,
      unknown,
      { ids: string[]; shopId?: string }
    >,
    'mutateAsync' | 'isPending'
  >;
  useSetSaved: () => Pick<
    UseMutationResult<
      ApiResponse<ProductOpportunitySavedResult>,
      unknown,
      { id: string; saved: boolean; shopId?: string; keyword?: string }
    >,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useShopResearch: () => Pick<
    UseMutationResult<ApiResponse<ShopKeywordResearch>, unknown, string>,
    'mutateAsync' | 'isPending'
  >;
  useGetShopResearch: (
    shopId?: string,
    enabled?: boolean,
  ) => Pick<
    UseQueryResult<ShopKeywordResearch | null>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'isSuccess' | 'error'
  >;
  hasPermission: (permission: string) => boolean;
  defaultPageSize: number;
};

const ProductOpportunitiesDepsContext = createContext<ProductOpportunitiesDeps | null>(null);

export const ProductOpportunitiesDepsProvider = ({
  value,
  children,
}: {
  value: ProductOpportunitiesDeps;
  children: ReactNode;
}) => (
  <ProductOpportunitiesDepsContext.Provider value={value}>
    {children}
  </ProductOpportunitiesDepsContext.Provider>
);

export const useProductOpportunitiesDeps = (): ProductOpportunitiesDeps => {
  const deps = useContext(ProductOpportunitiesDepsContext);
  if (!deps) {
    throw new Error(
      'useProductOpportunitiesDeps must be used within a <ProductOpportunitiesDepsProvider>',
    );
  }
  return deps;
};
