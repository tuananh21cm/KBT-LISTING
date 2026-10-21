import { createContext, useContext, type ComponentType, type ReactNode } from 'react';
import type { QueryClient, UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type { PlatformOption } from '@drop/types/platform';
import type {
  CreateShopRequest,
  ShopDetail,
  ShopListItem,
  UpdateAdminShopRequest,
  UpdateSellerShopRequest,
  UpdateShopCatalogProfileRequest,
  ShopCatalogProfile,
  AgeRange,
  AutoPublishSystemConfig,
  UpdateShopAutoPublishRequest,
  ShopAutoPublish,
  ShopOwnershipTransferResult,
  ShopMappingHealth,
} from '@drop/types/shop';

/** Admin-only seller option for create/edit form. */
export type ShopsSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

/** Slim receiving-bank option for the form select (admin). */
export type ShopsReceivingBankOption = {
  id: string;
  name?: string;
  accountHolderName?: string;
  accountNumber?: string;
  sourceName?: string;
  sourceAccountName?: string;
  currency?: string;
};

/** Receiving-bank detail for the card modal (admin). */
export type ShopsReceivingBankDetail = {
  id: string;
  name?: string;
  accountHolderName?: string;
  accountNumber?: string;
  sourceName?: string;
  sourceAccountName?: string;
  currency?: string;
  bankName?: string;
  bankLocation?: string;
  accountType?: string;
  swiftBic?: string;
  routingNumber?: string;
  bankAddress?: string;
};

export type CategoryCascaderProps = {
  site?: string;
  value?: string[] | string[][];
  multiple?: boolean;
  /** `panel` = menus always open (category picker modal). */
  variant?: 'select' | 'panel';
  onChange?: (
    path: string[] | string[][] | undefined,
    selected?:
      | { id: string; name: string; pathLabel?: string }
      | Array<{ id: string; name: string; pathLabel?: string }>
      | null,
  ) => void;
  disabled?: boolean;
  placeholder?: string;
};

export type CategorySearchHit = {
  id: string;
  name: string;
  pathLabel?: string;
  pathIds: string[];
  isLeaf?: boolean;
  level?: number;
};

/**
 * Hooks/data the shared Shops page needs, without importing anything app-specific.
 * Each portal provides its own implementation backed by its React Query hooks.
 */
export type ShopsDeps = {
  useShopsList: () => Pick<UseQueryResult<ShopListItem[]>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  useShopDetail: (
    id?: string | null,
    enabled?: boolean,
  ) => Pick<
    UseQueryResult<ShopDetail | null | undefined>,
    'data' | 'isLoading' | 'isFetching'
  >;
  fetchShopDetail: (queryClient: QueryClient, id: string) => Promise<ShopDetail>;
  useCreateShop?: () => Pick<
    UseMutationResult<ApiResponse<ShopDetail>, unknown, CreateShopRequest>,
    'mutateAsync' | 'isPending'
  >;
  useUpdateShop: () => Pick<
    UseMutationResult<
      ApiResponse<ShopDetail>,
      unknown,
      { id: string; data: UpdateAdminShopRequest | UpdateSellerShopRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateShopCatalogProfile: () => Pick<
    UseMutationResult<
      ApiResponse<ShopCatalogProfile>,
      unknown,
      { id: string; data: UpdateShopCatalogProfileRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateShopAutoPublish: () => Pick<
    UseMutationResult<
      ApiResponse<ShopAutoPublish>,
      unknown,
      { id: string; data: UpdateShopAutoPublishRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Seeded age brackets for catalog Select (long-lived cache). */
  useAgeRanges: () => Pick<UseQueryResult<AgeRange[]>, 'data' | 'isLoading'>;
  useDeleteShop?: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutate' | 'isPending'
  >;
  /** Admin only — backfill Orders/Listings OwnerId onto current shop owner. */
  useReassignOwnedData?: () => Pick<
    UseMutationResult<ApiResponse<ShopOwnershipTransferResult>, unknown, string>,
    'mutateAsync' | 'isPending'
  >;
  /** Admin only — backfill ownership for all mismatched shops. */
  useReassignAllOwnedData?: () => Pick<
    UseMutationResult<ApiResponse<ShopOwnershipTransferResult>, unknown, void>,
    'mutateAsync' | 'isPending'
  >;
  /** Admin only — orders with null ShopId (reconnect / transfer gaps). */
  useMappingHealth?: () => Pick<
    UseQueryResult<ShopMappingHealth>,
    'data' | 'isLoading' | 'isFetching' | 'refetch'
  >;
  useReorderShops?: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string[]>,
    'mutateAsync' | 'isPending'
  >;
  usePlatformOptions: () => Pick<UseQueryResult<PlatformOption[]>, 'data' | 'isLoading'>;
  /** Admin only — create/edit owner select. */
  useSellers?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ShopsSellerOption[]>, 'data' | 'isLoading'>;
  /**
   * Admin only — bulk Excel import. Raw calls instead of the mutation hooks so a
   * 50-row run reports once at the end rather than firing one toast per row;
   * `invalidate` refreshes list + option caches after the loop.
   */
  importShops?: {
    create: (data: CreateShopRequest) => Promise<ApiResponse<ShopDetail>>;
    update: (id: string, data: UpdateAdminShopRequest) => Promise<ApiResponse<ShopDetail>>;
    invalidate: () => Promise<void>;
  };
  /** Admin only — receiving bank select options. */
  useReceivingBankOptions?: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ShopsReceivingBankOption[]>, 'data' | 'isLoading'>;
  /** Admin only — card detail modal. */
  useReceivingBankDetail?: (
    id?: string | null,
    enabled?: boolean,
  ) => Pick<
    UseQueryResult<ShopsReceivingBankDetail | null | undefined>,
    'data' | 'isLoading' | 'isFetching'
  >;
  CategoryCascader: ComponentType<CategoryCascaderProps>;
  searchCategories: (params: { site: string; search: string }) => Promise<CategorySearchHit[]>;
  /** 4Seller AI: gợi ý 1 category từ từ khóa (title). */
  recommendCategory: (params: { site: string; title: string }) => Promise<CategorySearchHit>;
  hasPermission: (permission: string) => boolean;
  /** Admin only — write the unique shop purchase cost on the company ledger. */
  setAcquisitionCost?: Pick<
    UseMutationResult<unknown, Error, { shopId: string; amount: number | null }>,
    'mutateAsync' | 'isPending'
  >;
  defaultPageSize: number;
  /** System gate for per-shop auto publish switch (admin + seller). */
  useAutoPublishSystemConfig?: () => Pick<
    UseQueryResult<AutoPublishSystemConfig>,
    'data' | 'isLoading'
  >;
};

const ShopsDepsContext = createContext<ShopsDeps | null>(null);

export const ShopsDepsProvider = ({
  value,
  children,
}: {
  value: ShopsDeps;
  children: ReactNode;
}) => <ShopsDepsContext.Provider value={value}>{children}</ShopsDepsContext.Provider>;

export const useShopsDeps = (): ShopsDeps => {
  const deps = useContext(ShopsDepsContext);
  if (!deps) {
    throw new Error('useShopsDeps must be used within a <ShopsDepsProvider>');
  }
  return deps;
};
