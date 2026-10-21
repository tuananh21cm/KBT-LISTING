import { createContext, useContext, type ComponentType, type ReactNode } from 'react';
import type { QueryClient, UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type { PlatformOption } from '@drop/types/platform';
import type {
  CreatePricingTemplateRequest,
  PricingTemplate,
} from '@drop/types/pricingTemplate';
import type {
  CreateShippingTemplateRequest,
  ShippingTemplate,
  UpdateShippingTemplateRequest,
} from '@drop/types/shippingTemplate';
import type {
  BindListingCategoryRequest,
  ListingAnalyticsDates,
  ListingAnalyticsPage,
  ListingAnalyticsParams,
  ListingCosUploadResult,
  ListingDeleteResult,
  ListingDeactivateResult,
  ListingDelistSuggestionParams,
  ListingDelistSuggestions,
  ListingDescriptionPrepResult,
  ListingDetail,
  ListingListPage,
  ListingListParams,
  ListingMoveSource,
  ListingPushResult,
  ListingRecentCategory,
  ListingShopMetadata,
  ListingSourceUrlResult,
  ListingSyncJobStatus,
  ListingSyncRequest,
  MapListingSpecificsRequest,
  MapListingSpecificsResult,
  RewriteListingDescriptionRequest,
  RewriteListingDescriptionResult,
  ListingRewriteConfig,
  ListingAutoPublishRun,
  UpdateListingDescriptionPrepPayload,
  UpdateListingPayload,
} from '@drop/types/listing';
import type { ProductSourceOption, UserFile } from './localTypes';

/** Slim shop shape for filter dropdowns — admin adds `ownerId` for seller-scoped filtering. */
export type ListingsShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  platformCode?: string | null;
  status?: string;
  ownerId?: string;
  region?: string | null;
  fourSellerShopId?: number | string | null;
  avatarUrl?: string | null;
};

export type ListingsSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

export type ListingsAdminSettingOption = {
  key: string;
  value: string;
};

export type CategoryCascaderProps = {
  site?: string;
  value?: string[];
  onChange?: (
    path: string[] | undefined,
    selected?: { id: string; name: string } | null,
  ) => void;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  style?: React.CSSProperties;
};

export type RecommendCategoryRequest = {
  site: string;
  title: string;
  ownerUserId?: string;
  fourSellerShopId?: number;
};

export type RecommendCategoryResult = {
  categoryId: string;
  categoryName: string;
  nodePath?: string;
  pathIds: string[];
  isLeaf: boolean;
  site: string;
  /** False khi category không có trong catalog đã sync, bị vô hiệu hoá, hoặc không phải leaf. */
  isValidated?: boolean;
  validationWarning?: string | null;
  categoryPath?: string | null;
  /** AI trả về leaf này nhưng Settings blocked-remap đã thay bằng categoryId. */
  replacedFromCategoryId?: string | null;
  replacedFromCategoryName?: string | null;
  replacementReason?: string | null;
};

/**
 * Hooks/data the shared Listings pages need, without importing anything app-specific.
 * Each portal (admin/seller) provides its own implementation.
 */
export type ListingsDeps = {
  hasPermission: (permission: string) => boolean;
  defaultPageSize: number;

  // ── list ──
  useListingsList: (
    params: ListingListParams,
  ) => Pick<UseQueryResult<ListingListPage>, 'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'> & {
    refetch: () => Promise<unknown>;
  };
  // ── analytics (Compass snapshot theo ngày) ──
  useListingAnalytics: (
    params: ListingAnalyticsParams,
  ) => Pick<
    UseQueryResult<ListingAnalyticsPage>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  > & {
    refetch: () => Promise<unknown>;
  };
  useListingAnalyticsDates: (params?: {
    shopId?: string;
    ownerUserId?: string;
  }) => Pick<UseQueryResult<ListingAnalyticsDates>, 'data' | 'isLoading'>;
  /** Lazy: chỉ fetch khi panel «Đề xuất hủy list» mở. */
  useListingDelistSuggestions: (
    params: ListingDelistSuggestionParams,
    options?: { enabled?: boolean },
  ) => Pick<
    UseQueryResult<ListingDelistSuggestions>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error'
  > & {
    refetch: () => Promise<unknown>;
  };

  /** Starts the sync job; resolves as soon as the server accepts it (HTTP 202). */
  useSyncListings: () => Pick<
    UseMutationResult<ApiResponse<ListingSyncJobStatus>, unknown, ListingSyncRequest>,
    'mutateAsync' | 'isPending'
  >;
  /** Polls a started job until it reaches a terminal state. Pass null to stop polling. */
  useListingSyncJob: (jobId: string | null) => { data?: ListingSyncJobStatus };
  useSyncOneListing: () => Pick<
    UseMutationResult<ApiResponse<ListingDetail>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useDeleteListing: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useDeleteListingsByFilter: () => Pick<
    UseMutationResult<ApiResponse<ListingDeleteResult>, unknown, ListingListParams>,
    'mutateAsync' | 'isPending'
  >;
  useBulkDeleteListings: () => Pick<
    UseMutationResult<ApiResponse<ListingDeleteResult>, unknown, string[]>,
    'mutateAsync' | 'isPending'
  >;
  useBulkDeactivateListings: () => Pick<
    UseMutationResult<ApiResponse<ListingDeactivateResult>, unknown, string[]>,
    'mutateAsync' | 'isPending'
  >;
  useActivateListing: () => Pick<
    UseMutationResult<ApiResponse<ListingPushResult | ListingDetail>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useDeactivateListing: () => Pick<
    UseMutationResult<ApiResponse<ListingPushResult | ListingDetail>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  useDuplicateListing: () => Pick<
    UseMutationResult<ApiResponse<ListingDetail>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  usePlatformOptions: () => Pick<UseQueryResult<PlatformOption[]>, 'data'>;
  useShopOptions: () => Pick<UseQueryResult<ListingsShopOption[]>, 'data' | 'isLoading'>;
  useSellers?: () => Pick<UseQueryResult<ListingsSellerOption[]>, 'data'>;
  useAdminSettings?: (options?: {
    enabled?: boolean;
  }) => Pick<UseQueryResult<ListingsAdminSettingOption[]>, 'data'>;
  prefetchListingDetail: (queryClient: QueryClient, id: string) => void | Promise<unknown>;
  useBindListingCategory: () => Pick<
    UseMutationResult<
      ApiResponse<null>,
      unknown,
      { id: string; data: BindListingCategoryRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateListingSourceUrl: () => Pick<
    UseMutationResult<
      ListingSourceUrlResult,
      unknown,
      { id: string; sourceUrl: string }
    >,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  getMoveSource: (id: string) => Promise<ApiResponse<ListingMoveSource>>;
  getRecentCategories: (
    id: string,
    params?: { shopId?: number; site?: string },
  ) => Promise<ApiResponse<ListingRecentCategory[]>>;

  // ── edit ──
  useListingDetail: (
    id: string | undefined,
    options?: { refresh?: boolean },
  ) => Pick<
    UseQueryResult<ListingDetail | null | undefined>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error' | 'refetch'
  >;
  useSaveDraftListing: () => Pick<
    UseMutationResult<
      ApiResponse<ListingPushResult>,
      unknown,
      { id: string; data: UpdateListingPayload }
    >,
    'mutateAsync' | 'isPending'
  >;
  usePublishListing: () => Pick<
    UseMutationResult<
      ApiResponse<ListingPushResult>,
      unknown,
      { id: string; data: UpdateListingPayload }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateListing: () => Pick<
    UseMutationResult<
      ApiResponse<ListingDetail | ListingPushResult>,
      unknown,
      { id: string; data: UpdateListingPayload }
    >,
    'mutateAsync' | 'isPending'
  >;
  useListingShopMetadata: (
    listingId: string | undefined,
    shopId: string | null | undefined,
  ) => Pick<
    UseQueryResult<ListingShopMetadata | null | undefined>,
    'data' | 'isFetching' | 'isError' | 'error' | 'refetch'
  >;
  useRecommendCategory: () => Pick<
    UseMutationResult<ApiResponse<RecommendCategoryResult>, unknown, RecommendCategoryRequest>,
    'mutateAsync' | 'isPending'
  >;
  useRewriteListingDescription: () => Pick<
    UseMutationResult<
      ApiResponse<RewriteListingDescriptionResult>,
      unknown,
      RewriteListingDescriptionRequest
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Local-only description prep save — returns unwrapped result (caller doesn't inspect success/data). */
  useSaveListingDescriptionPrep: () => Pick<
    UseMutationResult<
      ListingDescriptionPrepResult,
      unknown,
      { id: string; data: UpdateListingDescriptionPrepPayload }
    >,
    'mutateAsync' | 'isPending'
  >;
  useMapListingSpecifics: (listingId?: string) => Pick<
    UseMutationResult<ApiResponse<MapListingSpecificsResult>, unknown, MapListingSpecificsRequest>,
    'mutateAsync' | 'isPending'
  >;
  useProductSourceOptions: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ProductSourceOption[]>, 'data' | 'isLoading'>;
  useShippingTemplates: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<ShippingTemplate[]>, 'data' | 'isLoading' | 'refetch'>;
  useCreateShippingTemplate: () => Pick<
    UseMutationResult<ApiResponse<ShippingTemplate>, unknown, CreateShippingTemplateRequest>,
    'mutateAsync' | 'isPending'
  >;
  useUpdateShippingTemplate: () => Pick<
    UseMutationResult<
      ApiResponse<ShippingTemplate>,
      unknown,
      { id: string; data: UpdateShippingTemplateRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useDeleteShippingTemplate: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutateAsync' | 'isPending'
  >;
  usePricingTemplates: (
    enabled?: boolean,
  ) => Pick<UseQueryResult<PricingTemplate[]>, 'data' | 'isLoading' | 'refetch'>;
  /** Batch pricing modal manages templates via these (create/edit/delete/set-default). */
  useCreatePricingTemplate: () => Pick<
    UseMutationResult<ApiResponse<PricingTemplate>, unknown, CreatePricingTemplateRequest>,
    'mutateAsync' | 'isPending'
  >;
  useUpdatePricingTemplate: () => Pick<
    UseMutationResult<
      ApiResponse<PricingTemplate>,
      unknown,
      { id: string; data: CreatePricingTemplateRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useDeletePricingTemplate: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutateAsync' | 'isPending'
  >;

  /** App-owned file library hooks for AddProductImageModal. */
  useFiles: (params?: {
    search?: string;
    pageCurrent?: number;
    pageSize?: number;
    enabled?: boolean;
  }) => Pick<
    UseQueryResult<{ items: UserFile[]; total: number } | UserFile[]>,
    'data' | 'isLoading' | 'isFetching' | 'isError' | 'error' | 'refetch'
  >;
  useUploadFile: () => Pick<
    UseMutationResult<
      ApiResponse<UserFile>,
      unknown,
      { file: File; linkedEntityType?: string; linkedEntityId?: string; metadata?: string }
    >,
    'mutateAsync' | 'isPending'
  >;
  useDeleteFile: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutateAsync' | 'isPending'
  >;

  CategoryCascader: ComponentType<CategoryCascaderProps>;

  listingApi: {
    cosUpload: (
      listingId: string,
      file: File,
      referenceType?: string,
    ) => Promise<ApiResponse<ListingCosUploadResult>>;
    generateSizeChart: (
      listingId: string,
    ) => Promise<ApiResponse<ListingCosUploadResult>>;
    saveDraft: (
      id: string,
      data?: UpdateListingPayload,
    ) => Promise<ApiResponse<ListingPushResult>>;
    pushUpdate: (
      id: string,
      data?: UpdateListingPayload,
    ) => Promise<ApiResponse<ListingPushResult>>;
    update: (
      id: string,
      data: UpdateListingPayload,
    ) => Promise<ApiResponse<ListingDetail>>;
    mapSpecifics: (
      listingId: string,
      data: MapListingSpecificsRequest,
    ) => Promise<ApiResponse<MapListingSpecificsResult>>;
    getRewriteConfig: () => Promise<ApiResponse<ListingRewriteConfig>>;
    suggestBrands?: (
      listingId: string,
      shopId: string,
      brandName: string,
    ) => Promise<ApiResponse<unknown>>;
    listAutoPublishRuns: (
      listingId: string,
      take?: number,
    ) => Promise<ApiResponse<ListingAutoPublishRun[]>>;
    retryAutoPublish: (listingId: string) => Promise<ApiResponse<null>>;
  };

  /** Auto publish run history for timeline modal. */
  useListingAutoPublishRuns?: (
    listingId: string | null | undefined,
    options?: { enabled?: boolean },
  ) => Pick<
    UseQueryResult<ListingAutoPublishRun[]>,
    'data' | 'isLoading' | 'isFetching' | 'refetch'
  >;
  /** Kết quả mutate không dùng ở page (app tự xử lý cache/patch badge) nên để `unknown`. */
  useRetryListingAutoPublish?: () => Pick<
    UseMutationResult<unknown, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;

  /** Invalidate listing caches after mutations that bypass hooks. */
  invalidateListingsCache: (queryClient: QueryClient) => void;
  /** React-query key for a single listing detail — app-specific (e.g. ['adminListing', id] vs ['listing', id]). */
  listingDetailQueryKey: (id: string) => readonly unknown[];
  /** Prefix key (no id) to broadly invalidate/refetch all listing-detail queries after a mutation. */
  listingDetailQueryKeyPrefix: readonly unknown[];
  /** Imperative refresh of one listing from 4Seller (edit page) — returns refreshed detail (unwrapped) or throws. */
  refreshListingFromFourSeller?: (id: string) => Promise<ListingDetail | null | undefined>;
  syncOneListingFromFourSeller?: (id: string) => Promise<ListingDetail | null | undefined>;
};

const ListingsDepsContext = createContext<ListingsDeps | null>(null);

export const ListingsDepsProvider = ({
  value,
  children,
}: {
  value: ListingsDeps;
  children: ReactNode;
}) => <ListingsDepsContext.Provider value={value}>{children}</ListingsDepsContext.Provider>;

export const useListingsDeps = (): ListingsDeps => {
  const deps = useContext(ListingsDepsContext);
  if (!deps) {
    throw new Error('useListingsDeps must be used within an <ListingsDepsProvider>');
  }
  return deps;
};
