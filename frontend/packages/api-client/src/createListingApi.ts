import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  BindListingCategoryRequest,
  ListingAnalyticsDates,
  ListingAnalyticsPage,
  ListingAnalyticsParams,
  ListingBrandOption,
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
  ListingStatusCounts,
  ListingSyncJobStatus,
  ListingSyncRequest,
  MapListingSpecificsRequest,
  MapListingSpecificsResult,
  RewriteListingDescriptionRequest,
  RewriteListingDescriptionResult,
  ListingRewriteConfig,
  ListingSourceUrlResult,
  ListingAutoPublishRun,
  UpdateListingDescriptionPrepPayload,
  UpdateListingPayload,
  UpdateListingSourceUrlPayload,
  ListingAutoPublishQueueStatusBatch,
} from '@drop/types/listing';

export type ListingApiBasePath = '/listings' | '/admin/listings';

export type ListingApi = {
  list: (params?: ListingListParams) => Promise<ApiResponse<ListingListPage>>;
  statusCounts: (params?: ListingListParams) => Promise<ApiResponse<ListingStatusCounts>>;
  /** Compass product metrics của một ngày snapshot (extension đẩy lên). */
  analytics: (params?: ListingAnalyticsParams) => Promise<ApiResponse<ListingAnalyticsPage>>;
  /** Các ngày snapshot có dữ liệu trong scope (date picker). */
  analyticsDates: (params?: {
    shopId?: string;
    ownerUserId?: string;
  }) => Promise<ApiResponse<ListingAnalyticsDates>>;
  /** Listing yếu nên hủy để nhả slot + keyword nên list thay vào (chỉ đọc). */
  analyticsDelistSuggestions: (
    params?: ListingDelistSuggestionParams,
  ) => Promise<ApiResponse<ListingDelistSuggestions>>;
  getById: (id: string, options?: { refresh?: boolean }) => Promise<ApiResponse<ListingDetail>>;
  getShopMetadata: (listingId: string, shopId: string) => Promise<ApiResponse<ListingShopMetadata>>;
  suggestBrands: (
    listingId: string,
    shopId: string,
    brandName: string,
  ) => Promise<ApiResponse<ListingBrandOption[]>>;
  /** Queues the pull and resolves as soon as the server accepts it (HTTP 202). */
  sync: (data?: ListingSyncRequest) => Promise<ApiResponse<ListingSyncJobStatus>>;
  syncStatus: (jobId: string) => Promise<ApiResponse<ListingSyncJobStatus>>;
  update: (id: string, data: UpdateListingPayload) => Promise<ApiResponse<ListingDetail>>;
  saveDescriptionPrep: (
    id: string,
    data: UpdateListingDescriptionPrepPayload,
  ) => Promise<ApiResponse<ListingDescriptionPrepResult>>;
  updateSourceUrl: (
    id: string,
    data: UpdateListingSourceUrlPayload,
  ) => Promise<ApiResponse<ListingSourceUrlResult>>;
  saveDraft: (id: string, data?: UpdateListingPayload) => Promise<ApiResponse<ListingPushResult>>;
  publish: (id: string, data?: UpdateListingPayload) => Promise<ApiResponse<ListingPushResult>>;
  pushUpdate: (id: string, data?: UpdateListingPayload) => Promise<ApiResponse<ListingPushResult>>;
  deactivate: (id: string) => Promise<ApiResponse<ListingPushResult>>;
  activate: (id: string) => Promise<ApiResponse<ListingPushResult>>;
  duplicate: (id: string) => Promise<ApiResponse<ListingDetail>>;
  syncOne: (id: string) => Promise<ApiResponse<ListingDetail>>;
  deleteById: (id: string) => Promise<ApiResponse<null>>;
  deleteByFilter: (params?: ListingListParams) => Promise<ApiResponse<ListingDeleteResult>>;
  bulkDelete: (ids: string[]) => Promise<ApiResponse<ListingDeleteResult>>;
  /** Bulk Hủy list: 4Seller batch-deactivate Active only; skipped returned in data. */
  bulkDeactivate: (ids: string[]) => Promise<ApiResponse<ListingDeactivateResult>>;
  rewriteDescription: (
    data: RewriteListingDescriptionRequest,
  ) => Promise<ApiResponse<RewriteListingDescriptionResult>>;
  getRewriteConfig: () => Promise<ApiResponse<ListingRewriteConfig>>;
  mapSpecifics: (
    listingId: string,
    data: MapListingSpecificsRequest,
  ) => Promise<ApiResponse<MapListingSpecificsResult>>;
  getMoveSource: (id: string) => Promise<ApiResponse<ListingMoveSource>>;
  getRecentCategories: (
    id: string,
    params?: { shopId?: number; site?: string },
  ) => Promise<ApiResponse<ListingRecentCategory[]>>;
  bindCategory: (id: string, data: BindListingCategoryRequest) => Promise<ApiResponse<null>>;
  cosUpload: (
    listingId: string,
    file: File,
    referenceType?: string,
  ) => Promise<ApiResponse<ListingCosUploadResult>>;
  generateSizeChart: (
    listingId: string,
  ) => Promise<ApiResponse<ListingCosUploadResult>>;
  listAutoPublishRuns: (
    listingId: string,
    take?: number,
  ) => Promise<ApiResponse<ListingAutoPublishRun[]>>;
  getAutoPublishRun: (
    listingId: string,
    runId: string,
  ) => Promise<ApiResponse<ListingAutoPublishRun>>;
  retryAutoPublish: (listingId: string) => Promise<ApiResponse<null>>;
  /** Lightweight draft auto publish queue badges for background list poll. */
  autoPublishQueueStatus: (ids: string[]) => Promise<ApiResponse<ListingAutoPublishQueueStatusBatch>>;
};

/**
 * Shared listing API client. Admin uses `/admin/listings`, seller uses `/listings`.
 * Ownership / permissions are enforced server-side from the JWT.
 */
export function createListingApi(
  http: AxiosInstance,
  opts: { basePath: ListingApiBasePath },
): ListingApi {
  const { basePath } = opts;

  return {
    list: async (params: ListingListParams = {}) => {
      const response = await http.get<ApiResponse<ListingListPage>>(basePath, { params });
      return response.data;
    },

    analytics: async (params: ListingAnalyticsParams = {}) => {
      const response = await http.get<ApiResponse<ListingAnalyticsPage>>(`${basePath}/analytics`, {
        params,
      });
      return response.data;
    },

    analyticsDates: async (params: { shopId?: string; ownerUserId?: string } = {}) => {
      const response = await http.get<ApiResponse<ListingAnalyticsDates>>(
        `${basePath}/analytics/dates`,
        { params },
      );
      return response.data;
    },

    analyticsDelistSuggestions: async (params: ListingDelistSuggestionParams = {}) => {
      const response = await http.get<ApiResponse<ListingDelistSuggestions>>(
        `${basePath}/analytics/delist-suggestions`,
        { params },
      );
      return response.data;
    },

    statusCounts: async (params: ListingListParams = {}) => {
      const { pageCurrent: _p, pageSize: _s, siteStatus: _st, ...filters } = params;
      const response = await http.get<ApiResponse<ListingStatusCounts>>(`${basePath}/status-counts`, {
        params: filters,
      });
      return response.data;
    },

    getById: async (id, options) => {
      const response = await http.get<ApiResponse<ListingDetail>>(`${basePath}/${id}`, {
        params: options?.refresh ? { refresh: true } : undefined,
        timeout: options?.refresh ? 120_000 : undefined,
      });
      return response.data;
    },

    getShopMetadata: async (listingId, shopId) => {
      const response = await http.get<ApiResponse<ListingShopMetadata>>(
        `${basePath}/${listingId}/shop-metadata`,
        { params: { shopId }, timeout: 60_000 },
      );
      return response.data;
    },

    suggestBrands: async (listingId, shopId, brandName) => {
      const response = await http.get<ApiResponse<ListingBrandOption[]>>(
        `${basePath}/${listingId}/brand-suggest`,
        { params: { shopId, brandName }, timeout: 60_000 },
      );
      return response.data;
    },

    // Returns 202 with a job id: the pull runs server-side, the client polls syncStatus.
    sync: async (data: ListingSyncRequest = {}) => {
      const response = await http.post<ApiResponse<ListingSyncJobStatus>>(`${basePath}/sync`, data);
      return response.data;
    },

    syncStatus: async (jobId: string) => {
      const response = await http.get<ApiResponse<ListingSyncJobStatus>>(
        `${basePath}/sync/status/${jobId}`,
      );
      return response.data;
    },

    update: async (id, data) => {
      const response = await http.put<ApiResponse<ListingDetail>>(`${basePath}/${id}`, data);
      return response.data;
    },

    saveDescriptionPrep: async (id, data) => {
      const response = await http.put<ApiResponse<ListingDescriptionPrepResult>>(
        `${basePath}/${id}/description-prep`,
        data,
      );
      return response.data;
    },

    updateSourceUrl: async (id, data) => {
      const response = await http.put<ApiResponse<ListingSourceUrlResult>>(
        `${basePath}/${id}/source-url`,
        data,
      );
      return response.data;
    },

    saveDraft: async (id, data) => {
      const response = await http.post<ApiResponse<ListingPushResult>>(
        `${basePath}/${id}/save-draft`,
        data ?? null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    publish: async (id, data) => {
      const response = await http.post<ApiResponse<ListingPushResult>>(
        `${basePath}/${id}/publish`,
        data ?? null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    pushUpdate: async (id, data) => {
      const response = await http.post<ApiResponse<ListingPushResult>>(
        `${basePath}/${id}/update`,
        data ?? null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    deactivate: async (id) => {
      const response = await http.post<ApiResponse<ListingPushResult>>(
        `${basePath}/${id}/deactivate`,
        null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    activate: async (id) => {
      const response = await http.post<ApiResponse<ListingPushResult>>(
        `${basePath}/${id}/activate`,
        null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    duplicate: async (id) => {
      const response = await http.post<ApiResponse<ListingDetail>>(`${basePath}/${id}/duplicate`, null, {
        timeout: 120_000,
      });
      return response.data;
    },

    syncOne: async (id) => {
      const response = await http.post<ApiResponse<ListingDetail>>(`${basePath}/${id}/sync-one`, null, {
        timeout: 120_000,
      });
      return response.data;
    },

    deleteById: async (id) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/${id}`, {
        timeout: 120_000,
      });
      return response.data;
    },

    deleteByFilter: async (params: ListingListParams = {}) => {
      const { pageCurrent: _pageCurrent, pageSize: _pageSize, ...filters } = params;
      const response = await http.delete<ApiResponse<ListingDeleteResult>>(basePath, {
        params: filters,
        timeout: 300_000,
      });
      return response.data;
    },

    bulkDelete: async (ids) => {
      const response = await http.post<ApiResponse<ListingDeleteResult>>(
        `${basePath}/bulk-delete`,
        { ids },
        { timeout: 300_000 },
      );
      return response.data;
    },

    bulkDeactivate: async (ids) => {
      const response = await http.post<ApiResponse<ListingDeactivateResult>>(
        `${basePath}/bulk-deactivate`,
        { ids },
        { timeout: 300_000 },
      );
      return response.data;
    },

    rewriteDescription: async (data) => {
      const response = await http.post<ApiResponse<RewriteListingDescriptionResult>>(
        `${basePath}/rewrite-description`,
        data,
        { timeout: 120_000 },
      );
      return response.data;
    },

    getRewriteConfig: async () => {
      const response = await http.get<ApiResponse<ListingRewriteConfig>>(
        `${basePath}/rewrite-config`,
      );
      return response.data;
    },

    mapSpecifics: async (listingId, data) => {
      const response = await http.post<ApiResponse<MapListingSpecificsResult>>(
        `${basePath}/${listingId}/map-specifics`,
        data,
        { timeout: 120_000 },
      );
      return response.data;
    },

    getMoveSource: async (id) => {
      const response = await http.get<ApiResponse<ListingMoveSource>>(
        `${basePath}/${id}/move-source`,
        { timeout: 60_000 },
      );
      return response.data;
    },

    getRecentCategories: async (id, params) => {
      const response = await http.get<ApiResponse<ListingRecentCategory[]>>(
        `${basePath}/${id}/recent-categories`,
        { params, timeout: 60_000 },
      );
      return response.data;
    },

    bindCategory: async (id, data) => {
      const response = await http.post<ApiResponse<null>>(`${basePath}/${id}/bind-category`, data, {
        timeout: 60_000,
      });
      return response.data;
    },

    cosUpload: async (listingId, file, referenceType) => {
      const formData = new FormData();
      formData.append('file', file);
      if (referenceType?.trim()) {
        formData.append('referenceType', referenceType.trim());
      }
      const response = await http.post<ApiResponse<ListingCosUploadResult>>(
        `${basePath}/${listingId}/cos-upload`,
        formData,
        {
          headers: { 'Content-Type': 'multipart/form-data' },
          timeout: 120_000,
        },
      );
      return response.data;
    },
    generateSizeChart: async (listingId) => {
      const response = await http.post<ApiResponse<ListingCosUploadResult>>(
        `${basePath}/${listingId}/generate-size-chart`,
        null,
        { timeout: 120_000 },
      );
      return response.data;
    },

    listAutoPublishRuns: async (listingId, take = 20) => {
      const response = await http.get<ApiResponse<ListingAutoPublishRun[]>>(
        `${basePath}/${listingId}/auto-publish/runs`,
        { params: { take } },
      );
      return response.data;
    },

    getAutoPublishRun: async (listingId, runId) => {
      const response = await http.get<ApiResponse<ListingAutoPublishRun>>(
        `${basePath}/${listingId}/auto-publish/runs/${runId}`,
      );
      return response.data;
    },

    retryAutoPublish: async (listingId) => {
      const response = await http.post<ApiResponse<null>>(
        `${basePath}/${listingId}/auto-publish/retry`,
      );
      return response.data;
    },

    autoPublishQueueStatus: async (ids) => {
      const response = await http.get<ApiResponse<ListingAutoPublishQueueStatusBatch>>(
        `${basePath}/auto-publish-queue-status`,
        {
          params: { ids },
          paramsSerializer: {
            indexes: null,
          },
        },
      );
      return response.data;
    },
  };
}
