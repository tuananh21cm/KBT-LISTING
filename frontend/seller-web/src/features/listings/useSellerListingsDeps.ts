import type { ListingsDeps } from '@drop/listings';
import { listingApi } from '../../services/listingApi';
import {
  useListings,
  useListingAnalytics,
  useListingAnalyticsDates,
  useListingDelistSuggestions,
  useListingDetail,
  useActivateListing,
  useDeactivateListing,
  useDeleteListing,
  useDeleteListingsByFilter,
  useBulkDeleteListings,
  useBulkDeactivateListings,
  useDuplicateListing,
  useSyncListings,
  useListingSyncJob,
  useSyncOneListing,
  useSaveDraftListing,
  usePublishListing,
  useUpdateListing,
  useListingShopMetadata,
  useRewriteListingDescription,
  useSaveListingDescriptionPrep,
  useUpdateListingSourceUrl,
  useMapListingSpecifics,
  useBindListingCategory,
  useListingAutoPublishRuns,
  useRetryListingAutoPublish,
  prefetchListingDetail,
  invalidateListingsCache,
  refreshListingFromFourSeller,
  syncOneListingFromFourSeller,
} from '../../hooks/useListings';
import { usePlatformOptions } from '../../hooks/usePlatforms';
import { useShops } from '../../hooks/useShops';
import { useRecommendCategory } from '../../hooks/useCategories';
import { useAuthStore } from '../../stores/authStore';
import { useProductSourceOptions } from '../../hooks/useProducts';
import {
  useShippingTemplates,
  useCreateShippingTemplate,
  useUpdateShippingTemplate,
  useDeleteShippingTemplate,
} from '../../hooks/useShippingTemplates';
import {
  usePricingTemplates,
  useCreatePricingTemplate,
  useUpdatePricingTemplate,
  useDeletePricingTemplate,
} from '../../hooks/usePricingTemplates';
import { useFiles, useUploadFile, useDeleteFile } from '../../hooks/useFiles';
import { CategoryCascader } from '../categories/CategoryCascader';

const SELLER_LISTINGS_PAGE_SIZE = 25;

/** Wires seller-web hooks/APIs into the shared `@drop/listings` package. */
export function useSellerListingsDeps(): ListingsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    hasPermission,
    defaultPageSize: SELLER_LISTINGS_PAGE_SIZE,

    useListingsList: useListings,
    useListingAnalytics,
    useListingAnalyticsDates,
    useListingDelistSuggestions,
    useSyncListings,
    useListingSyncJob,
    useSyncOneListing,
    useDeleteListing,
    useDeleteListingsByFilter,
    useBulkDeleteListings,
    useBulkDeactivateListings,
    useActivateListing,
    useDeactivateListing,
    useDuplicateListing,
    usePlatformOptions,
    useShopOptions: useShops,
    prefetchListingDetail,
    useBindListingCategory,
    useUpdateListingSourceUrl,
    getMoveSource: listingApi.getMoveSource,
    getRecentCategories: listingApi.getRecentCategories,

    useListingDetail: (id) => useListingDetail(id ?? null),
    useSaveDraftListing,
    usePublishListing,
    useUpdateListing,
    useListingShopMetadata: (listingId, shopId) => useListingShopMetadata(listingId ?? null, shopId),
    useRecommendCategory,
    useRewriteListingDescription,
    useSaveListingDescriptionPrep,
    useMapListingSpecifics,
    useListingAutoPublishRuns,
    useRetryListingAutoPublish,
    useProductSourceOptions,
    useShippingTemplates,
    useCreateShippingTemplate,
    useUpdateShippingTemplate,
    useDeleteShippingTemplate,
    usePricingTemplates,
    useCreatePricingTemplate,
    useUpdatePricingTemplate,
    useDeletePricingTemplate,

    useFiles: ({ enabled } = {}) => useFiles(enabled ?? true),
    useUploadFile,
    useDeleteFile,

    CategoryCascader: CategoryCascader as ListingsDeps['CategoryCascader'],

    listingApi: {
      cosUpload: listingApi.cosUpload,
      generateSizeChart: listingApi.generateSizeChart,
      saveDraft: listingApi.saveDraft,
      pushUpdate: listingApi.pushUpdate,
      update: listingApi.update,
      mapSpecifics: listingApi.mapSpecifics,
      getRewriteConfig: listingApi.getRewriteConfig,
      suggestBrands: listingApi.suggestBrands,
      listAutoPublishRuns: listingApi.listAutoPublishRuns,
      retryAutoPublish: listingApi.retryAutoPublish,
    },

    invalidateListingsCache: (queryClient) => invalidateListingsCache(queryClient),
    listingDetailQueryKey: (id) => ['listing', id],
    listingDetailQueryKeyPrefix: ['listing'],
    refreshListingFromFourSeller,
    syncOneListingFromFourSeller,
  };
}
