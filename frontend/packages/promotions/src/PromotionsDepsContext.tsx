import { createContext, useContext, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type {
  ProductDiscountListPage,
  ProductDiscountListParams,
  ProductDiscountSyncJobStatus,
  ProductDiscountSyncRequest,
} from '@drop/types/productDiscount';

export type PromotionsShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  ownerId?: string;
};

export type PromotionsSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type PromotionsDeps = {
  useList: (
    params: ProductDiscountListParams,
  ) => Pick<UseQueryResult<ProductDiscountListPage>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useShopOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<PromotionsShopOption[]>, 'data' | 'isLoading'>;
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<PromotionsSellerOption[]>, 'data' | 'isLoading'>;
  useSync: () => Pick<
    UseMutationResult<
      ApiResponse<ProductDiscountSyncJobStatus>,
      unknown,
      ProductDiscountSyncRequest | undefined
    >,
    'mutateAsync' | 'isPending'
  >;
  useSyncJob: (
    jobId: string | null,
  ) => Pick<UseQueryResult<ProductDiscountSyncJobStatus>, 'data'>;
  useDeactivate: () => Pick<
    UseMutationResult<ApiResponse<unknown>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  hasPermission: (permission: string) => boolean;
  defaultPageSize: number;
};

const PromotionsDepsContext = createContext<PromotionsDeps | null>(null);

export const PromotionsDepsProvider = ({
  value,
  children,
}: {
  value: PromotionsDeps;
  children: ReactNode;
}) => (
  <PromotionsDepsContext.Provider value={value}>{children}</PromotionsDepsContext.Provider>
);

export const usePromotionsDeps = (): PromotionsDeps => {
  const ctx = useContext(PromotionsDepsContext);
  if (!ctx) {
    throw new Error('usePromotionsDeps must be used within PromotionsDepsProvider');
  }
  return ctx;
};
