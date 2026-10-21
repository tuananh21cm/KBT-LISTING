import type { ListingsDeps } from '@drop/listings';
import { adminListingApi } from '../../services/adminListingApi';
import {
  useAdminListings,
  useAdminListingAnalytics,
  useAdminListingAnalyticsDates,
  useAdminListingDelistSuggestions,
  useAdminListingDetail,
  useActivateListing,
  useDeactivateListing,
  useDeleteAdminListing,
  useDeleteAdminListingsByFilter,
  useBulkDeleteAdminListings,
  useBulkDeactivateAdminListings,
  useDuplicateListing,
  useSyncAdminListings,
  useAdminListingSyncJob,
  useSyncOneAdminListing,
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
  prefetchAdminListingDetail,
  invalidateAdminListingsCache,
  refreshAdminListingFromFourSeller,
  syncOneAdminListingFromFourSeller,
} from '../../hooks/useAdminListings';
import { usePlatformOptions } from '../../hooks/useAdminPlatforms';
import { useAdminShops } from '../../hooks/useAdminShops';
import { useRecommendCategory } from '../../hooks/useAdminCategories';
import { useSellers } from '../../hooks/useSellers';
import { useAuthStore } from '../../stores/authStore';
import { useAdminSettings } from '../../hooks/useAdminSettings';
import { useProductSourceOptions } from '../../hooks/useAdminProductSources';
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

const ADMIN_LISTINGS_PAGE_SIZE = 25;

/** Wires admin-web hooks/APIs into the shared `@drop/listings` package. */
export function useAdminListingsDeps(): ListingsDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    hasPermission,
    defaultPageSize: ADMIN_LISTINGS_PAGE_SIZE,

    useListingsList: useAdminListings,
    useListingAnalytics: useAdminListingAnalytics,
    useListingAnalyticsDates: useAdminListingAnalyticsDates,
    useListingDelistSuggestions: useAdminListingDelistSuggestions,
    useSyncListings: useSyncAdminListings,
    useListingSyncJob: useAdminListingSyncJob,
    useSyncOneListing: useSyncOneAdminListing,
    useDeleteListing: useDeleteAdminListing,
    useDeleteListingsByFilter: useDeleteAdminListingsByFilter,
    useBulkDeleteListings: useBulkDeleteAdminListings,
    useBulkDeactivateListings: useBulkDeactivateAdminListings,
    useActivateListing,
    useDeactivateListing,
    useDuplicateListing,
    usePlatformOptions,
    useShopOptions: useAdminShops,
    useSellers,
    useAdminSettings,
    prefetchListingDetail: prefetchAdminListingDetail,
    useBindListingCategory,
    useUpdateListingSourceUrl,
    getMoveSource: adminListingApi.getMoveSource,
    getRecentCategories: adminListingApi.getRecentCategories,

    useListingDetail: (id) => useAdminListingDetail(id ?? null),
    useSaveDraftListing,
    usePublishListing,
    useUpdateListing,
    useListingShopMetadata: (listingId, shopId) =>
      useListingShopMetadata(listingId ?? null, shopId),
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
      cosUpload: adminListingApi.cosUpload,
      generateSizeChart: adminListingApi.generateSizeChart,
      saveDraft: adminListingApi.saveDraft,
      pushUpdate: adminListingApi.pushUpdate,
      update: adminListingApi.update,
      mapSpecifics: adminListingApi.mapSpecifics,
      getRewriteConfig: adminListingApi.getRewriteConfig,
      suggestBrands: adminListingApi.suggestBrands,
      listAutoPublishRuns: adminListingApi.listAutoPublishRuns,
      retryAutoPublish: adminListingApi.retryAutoPublish,
    },

    invalidateListingsCache: (queryClient) => invalidateAdminListingsCache(queryClient),
    listingDetailQueryKey: (id) => ['adminListing', id],
    listingDetailQueryKeyPrefix: ['adminListing'],
    refreshListingFromFourSeller: refreshAdminListingFromFourSeller,
    syncOneListingFromFourSeller: syncOneAdminListingFromFourSeller,
  };
}
