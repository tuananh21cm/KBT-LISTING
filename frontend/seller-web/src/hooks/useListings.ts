import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query';
import { message } from 'antd';
import { useEffect } from 'react';
import {
  applyAutoPublishRetryToListPage,
  getApiErrorDetails,
  useListingAutoPublishQueuePoll,
} from '@drop/listings';
import { listingApi } from '../services/listingApi';
import type {
  BindListingCategoryRequest,
  ListingAnalyticsParams,
  ListingDelistSuggestionParams,
  ListingDetail,
  ListingAutoPublishQueueStatusItem,
  ListingListItem,
  ListingListPage,
  ListingListParams,
  ListingSourceUrlResult,
  ListingSyncRequest,
  MapListingSpecificsRequest,
  RewriteListingDescriptionRequest,
  UpdateListingDescriptionPrepPayload,
  UpdateListingPayload,
} from '../types/listing';
import {
  notifyListingsChanged,
  subscribeListingsChanged,
} from '../utils/listingCrossTabSync';

/** Push mutations surface errors via ListingEditPage modal — no toast here. */
const pushMutationErrorMode = 'silent' as const;

/**
 * Invalidate list cache in this tab and ping sibling tabs (edit opens in window.open).
 * @param refetchType `'active'` = refetch mounted list queries; `'none'` = mark stale only
 *   (edit tab is closing — sibling list tabs still refetch via BroadcastChannel).
 */
export function invalidateListingsCache(
  queryClient: QueryClient,
  options?: { refetchType?: 'active' | 'none' | 'all'; reason?: string },
): void {
  const refetchType = options?.refetchType ?? 'active';
  void queryClient.invalidateQueries({
    queryKey: ['listings'],
    refetchType,
  });
  notifyListingsChanged(options?.reason);
}

export const useListings = (params: ListingListParams) => {
  const queryClient = useQueryClient();

  // Edit tab has a separate QueryClient — listen for Save Draft / Publish / … elsewhere.
  useEffect(
    () =>
      subscribeListingsChanged(() => {
        void queryClient.invalidateQueries({ queryKey: ['listings'] });
      }),
    [queryClient],
  );

  const listQueryKey = ['listings', params] as const;

  const query = useQuery({
    queryKey: listQueryKey,
    queryFn: async () => {
      const response = await listingApi.list(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return (
        response.data ?? {
          items: [],
          total: 0,
          pageCurrent: params.pageCurrent ?? 1,
          pageSize: params.pageSize ?? 50,
          statusCounts: {
            all: 0,
            draft: 0,
            published: 0,
            active: 0,
            inactive: 0,
            removed: 0,
            incomplete: 0,
            suspended: 0,
            private: 0,
            pending: 0,
            reject: 0,
            unsaleable: 0,
          },
        }
      );
    },
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    // Backup when BroadcastChannel missed: switch back to list tab → refetch if stale.
    refetchOnWindowFocus: true,
  });

  useListingAutoPublishQueuePoll({
    listQueryKey,
    listParams: params,
    items: query.data?.items ?? [],
    pollQueryKeyPrefix: ['listings'],
    fetchStatuses: async (ids) => {
      const response = await listingApi.autoPublishQueueStatus(ids);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data?.items ?? [];
    },
  });

  return query;
};
/** Compass product metrics của một ngày snapshot (extension TikTok Shop đẩy lên). */
export const useListingAnalytics = (params: ListingAnalyticsParams) =>
  useQuery({
    queryKey: ['listingAnalytics', params],
    queryFn: async () => {
      const response = await listingApi.analytics(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return (
        response.data ?? {
          items: [],
          total: 0,
          pageCurrent: params.pageCurrent ?? 1,
          pageSize: params.pageSize ?? 50,
          totals: {
            products: 0,
            gmv: 0,
            orders: 0,
            impressions: 0,
            clicks: 0,
            addToCartUnits: 0,
          },
        }
      );
    },
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });

export const useListingAnalyticsDates = (params?: { shopId?: string; ownerUserId?: string }) =>
  useQuery({
    queryKey: ['listingAnalyticsDates', params ?? {}],
    queryFn: async () => {
      const response = await listingApi.analyticsDates(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? { dates: [], latest: null };
    },
    staleTime: 60_000,
  });

/** Listing yếu nên hủy để nhả slot — chỉ gọi khi panel mở (`enabled`). */
export const useListingDelistSuggestions = (
  params: ListingDelistSuggestionParams,
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['listingDelistSuggestions', params],
    queryFn: async () => {
      const response = await listingApi.analyticsDelistSuggestions(params);
      if (!response.success) {
        throw new Error(response.message);
      }
      return (
        response.data ?? {
          currency: 'USD',
          scanned: 0,
          skippedWinners: 0,
          skippedTooNew: 0,
          skippedHealthy: 0,
          delistCount: 0,
          reviewCount: 0,
          remainingSlots: 0,
          recommendedDelistCount: 0,
          warnings: [],
          benchmarks: {
            medianCtr: 0,
            medianAtcRate: 0,
            medianOrderRate: 0,
            medianImpressions: 0,
            gmv: 0,
            windowDays: 7,
            trendWindowDays: 0,
            minTrackedDays: 0,
            minListedDays: 7,
          },
          items: [],
          keywords: [],
        }
      );
    },
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });

export const useListingDetail = (id: string | null) =>
  useQuery({
    queryKey: ['listing', id],
    queryFn: async () => {
      if (!id) {
        return null;
      }
      // Local DB snapshot only — FE hydrates immediately, then background refresh=true.
      const response = await listingApi.getById(id, { refresh: false });
      if (!response.success) {
        throw new Error(response.message);
      }
      // Drop cross-tab prefetch once we have a live response.
      clearPrefetchedListingDetail(id);
      return response.data;
    },
    enabled: !!id,
    // Re-open / remount should paint from cache; soft refetch keeps data fresh.
    staleTime: 60_000,
    gcTime: 5 * 60_000,
    refetchOnMount: true,
    refetchOnWindowFocus: false,
    // New-tab Edit: ListingsPage may have stashed detail in sessionStorage.
    initialData: () => (id ? readPrefetchedListingDetail(id) : undefined),
    initialDataUpdatedAt: () => (id ? readPrefetchedListingDetailAt(id) : undefined),
  });

const PREFETCH_KEY = (id: string) => `listing-detail-prefetch:${id}`;

type PrefetchEnvelope = { at: number; data: unknown };

function readPrefetchEnvelope(id: string): PrefetchEnvelope | null {
  try {
    const raw = sessionStorage.getItem(PREFETCH_KEY(id));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PrefetchEnvelope;
    if (!parsed?.data || typeof parsed.at !== 'number') return null;
    // Ignore stale stashes (>2 min).
    if (Date.now() - parsed.at > 120_000) {
      sessionStorage.removeItem(PREFETCH_KEY(id));
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function readPrefetchedListingDetail(id: string): ListingDetail | undefined {
  return readPrefetchEnvelope(id)?.data as ListingDetail | undefined;
}

export function readPrefetchedListingDetailAt(id: string): number | undefined {
  return readPrefetchEnvelope(id)?.at;
}

export function clearPrefetchedListingDetail(id: string) {
  try {
    sessionStorage.removeItem(PREFETCH_KEY(id));
  } catch {
    /* ignore */
  }
}

/** Stash detail for a new edit tab (sessionStorage survives window.open same-origin). */
export function stashPrefetchedListingDetail(id: string, data: ListingDetail) {
  try {
    sessionStorage.setItem(PREFETCH_KEY(id), JSON.stringify({ at: Date.now(), data }));
  } catch {
    /* quota / private mode */
  }
}

/** Warm React Query cache before navigating to edit (same-tab). */
export const prefetchListingDetail = async (queryClient: QueryClient, id: string) => {
  if (!id) return;
  await queryClient.prefetchQuery({
    queryKey: ['listing', id],
    queryFn: async () => {
      const response = await listingApi.getById(id, { refresh: false });
      if (!response.success) {
        throw new Error(response.message);
      }
      if (response.data) {
        stashPrefetchedListingDetail(id, response.data);
      }
      return response.data;
    },
    staleTime: 60_000,
  });
};

/** Best-effort 4Seller per-id refresh after local hydrate. */
export const refreshListingFromFourSeller = async (id: string) => {
  const response = await listingApi.getById(id, { refresh: true });
  if (!response.success) {
    throw new Error(response.message || 'Refresh 4Seller thất bại.');
  }
  return response.data;
};

/** Sync one listing on 4Seller then return refreshed local detail (no toast). */
export const syncOneListingFromFourSeller = async (id: string) => {
  const response = await listingApi.syncOne(id);
  if (!response.success) {
    throw new Error(response.message || 'Đồng bộ listing thất bại.');
  }
  return response.data;
};

/** Shop-scoped brands + warehouses (owner cookie). */
export const useListingShopMetadata = (listingId: string | null, shopId: string | null | undefined) =>
  useQuery({
    queryKey: ['listingShopMetadata', listingId, shopId],
    queryFn: async () => {
      if (!listingId || !shopId) return null;
      const response = await listingApi.getShopMetadata(listingId, shopId);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được Brand / Warehouse từ 4Seller.');
      }
      return response.data ?? null;
    },
    enabled: !!listingId && !!shopId,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

/** Debounced brand suggest — pass trimmed brandName (empty disables). */
export const useListingBrandSuggest = (
  listingId: string | null,
  shopId: string | null | undefined,
  brandName: string,
) =>
  useQuery({
    queryKey: ['listingBrandSuggest', listingId, shopId, brandName],
    queryFn: async () => {
      if (!listingId || !shopId || !brandName.trim()) return [];
      const response = await listingApi.suggestBrands(listingId, shopId, brandName.trim());
      if (!response.success) {
        throw new Error(response.message || 'Gợi ý brand thất bại.');
      }
      return response.data ?? [];
    },
    enabled: !!listingId && !!shopId && brandName.trim().length >= 1,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });

/**
 * Starts a sync job. The pull itself runs server-side — result toasts come from the status poll,
 * so this only reports a failure to enqueue.
 */
export const useSyncListings = () =>
  useMutation({
    mutationFn: async (data: ListingSyncRequest = {}) => {
      const response = await listingApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ listing thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

/** Polls a sync job every 2s until it finishes. */
export const useListingSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['listingSyncJob', jobId],
    queryFn: async () => {
      const response = await listingApi.syncStatus(jobId!);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không đọc được tiến độ đồng bộ.');
      }
      return response.data;
    },
    enabled: !!jobId,
    refetchInterval: (query) =>
      query.state.data && query.state.data.state !== 'running' ? false : 2000,
    refetchOnWindowFocus: false,
    // A finished job would otherwise be served from cache when the same id is polled again.
    gcTime: 0,
    retry: false,
  });

export const useUpdateListingLocal = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateListingPayload }) => {
      const response = await listingApi.update(id, data);
      if (!response.success) {
        throw new Error(response.message || 'Cập nhật listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã lưu listing local.');
      invalidateListingsCache(queryClient, { reason: 'update-local' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useSaveDraftListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateListingPayload }) => {
      const response = await listingApi.saveDraft(id, data);
      if (!response.success) {
        const err = new Error(response.message || 'Save Draft thất bại.') as Error & {
          apiErrors?: string[];
        };
        err.apiErrors = response.errors ?? [];
        throw err;
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Save Draft thành công.');
      // Edit tab closes next — do NOT invalidate adminListing (would refetch GET detail and abort).
      // Sibling list tabs refetch via BroadcastChannel.
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'save-draft',
      });
    },
    onError: (error: Error & { apiErrors?: string[] }) => {
      if (pushMutationErrorMode === 'silent') return;
      message.error(error.message);
    },
  });
};

export const usePublishListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateListingPayload }) => {
      const response = await listingApi.publish(id, data);
      if (!response.success) {
        const err = new Error(response.message || 'Publish thất bại.') as Error & {
          apiErrors?: string[];
        };
        err.apiErrors = response.errors ?? [];
        throw err;
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã gửi publish, đang chờ duyệt.');
      // Sibling Draft/Published list tabs refetch (this edit tab is closing).
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'publish',
      });
    },
    onError: (error: Error & { apiErrors?: string[] }) => {
      if (pushMutationErrorMode === 'silent') return;
      message.error(error.message);
    },
  });
};

export const useUpdateListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateListingPayload }) => {
      const response = await listingApi.pushUpdate(id, data);
      if (!response.success) {
        const err = new Error(response.message || 'Update thất bại.') as Error & {
          apiErrors?: string[];
        };
        err.apiErrors = response.errors ?? [];
        throw err;
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Update thành công.');
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'update',
      });
    },
    onError: (error: Error & { apiErrors?: string[] }) => {
      if (pushMutationErrorMode === 'silent') return;
      message.error(error.message);
    },
  });
};

export const useDeactivateListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await listingApi.deactivate(id);
      if (!response.success) {
        throw new Error(response.message || 'Deactivate thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Deactivate thành công.');
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'deactivate',
      });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useActivateListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await listingApi.activate(id);
      if (!response.success) {
        throw new Error(response.message || 'Activate thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Activate thành công.');
      invalidateListingsCache(queryClient, { reason: 'activate' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** Duplicate listing: 4Seller copy → upsert local draft → return new detail. */
export const useDuplicateListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await listingApi.duplicate(id);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Duplicate listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã tạo bản sao listing.');
      if (response.data) {
        queryClient.setQueryData(['listing', response.data.id], response.data);
      }
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'duplicate',
      });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** Pull full listing detail from 4Seller (GET ?refresh=true) for one card action. */
export const useSyncOneListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await listingApi.getById(id, { refresh: true });
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã đồng bộ listing từ 4Seller.');
      if (response.data) {
        queryClient.setQueryData(['listing', response.data.id], response.data);
      }
      invalidateListingsCache(queryClient, { reason: 'sync-one' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: unknown) => {
      const { message: msg, errors } = getApiErrorDetails(error, 'Đồng bộ listing thất bại.');
      message.error(errors.length > 0 ? `${msg} — ${errors.join(' | ')}` : msg);
    },
  });
};

export const useDeleteListing = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await listingApi.deleteById(id);
      if (!response.success) {
        throw new Error(response.message || 'Xóa listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã xóa listing.');
      invalidateListingsCache(queryClient, { reason: 'delete' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteListingsByFilter = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (params: ListingListParams) => {
      const response = await listingApi.deleteByFilter(params);
      if (!response.success) {
        throw new Error(response.message || 'Xóa listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(
        response.message || `Đã xóa ${response.data?.deletedCount ?? 0} listing.`,
      );
      invalidateListingsCache(queryClient, { reason: 'delete-filter' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useBulkDeleteListings = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (ids: string[]) => {
      const response = await listingApi.bulkDelete(ids);
      if (!response.success) {
        throw new Error(response.message || 'Xóa listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(
        response.message || `Đã xóa ${response.data?.deletedCount ?? 0} listing.`,
      );
      invalidateListingsCache(queryClient, { reason: 'bulk-delete' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useBulkDeactivateListings = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (ids: string[]) => {
      const response = await listingApi.bulkDeactivate(ids);
      if (!response.success) {
        throw new Error(response.message || 'Deactivate listing thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(
        response.message ||
          `Đã deactivate ${response.data?.deactivatedCount ?? 0} listing.`,
      );
      invalidateListingsCache(queryClient, { reason: 'bulk-deactivate' });
      void queryClient.invalidateQueries({ queryKey: ['listing'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** AI rewrite description — applies to form only; no listing invalidate. */
export const useRewriteListingDescription = () =>
  useMutation({
    mutationFn: async (data: RewriteListingDescriptionRequest) => {
      const response = await listingApi.rewriteDescription(data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'AI viết lại mô tả thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

function mergeListingSourceUrl<T extends ListingListItem>(
  item: T,
  data: ListingSourceUrlResult,
): T {
  return {
    ...item,
    sourceUrl: data.sourceUrl,
    reviewCount: data.reviewCount ?? item.reviewCount,
    averageRating: data.averageRating ?? item.averageRating,
    storeLogo: data.storeLogo ?? item.storeLogo,
    storeTitle: data.storeTitle ?? item.storeTitle,
    storeUrl: data.storeUrl ?? item.storeUrl,
    shopRank: data.shopRank ?? item.shopRank,
    storeAttention: data.storeAttention ?? item.storeAttention,
    shippingDays: data.shippingDays ?? item.shippingDays,
    shippingDayPercent: data.shippingDayPercent ?? item.shippingDayPercent,
    sourceStock: data.sourceStock ?? item.sourceStock,
    isLocalProduct: data.isLocalProduct ?? item.isLocalProduct,
    isSupportQuickShip: data.isSupportQuickShip ?? item.isSupportQuickShip,
    sellerPlatform: data.sellerPlatform ?? item.sellerPlatform,
    storeCode: data.storeCode ?? item.storeCode,
    hasSizeChart: data.hasSizeChart ?? item.hasSizeChart,
  };
}

/** Local-only description + Rewrite/Insert Images flags — silent (caller toasts). */
export const useSaveListingDescriptionPrep = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      data,
    }: {
      id: string;
      data: UpdateListingDescriptionPrepPayload;
    }) => {
      const response = await listingApi.saveDescriptionPrep(id, data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Lưu mô tả local thất bại.');
      }
      return response.data;
    },
    onSuccess: (data, variables) => {
      queryClient.setQueryData(
        ['listing', variables.id],
        (prev: ListingDetail | undefined) =>
          prev
            ? {
                ...prev,
                descriptionHtml: data.descriptionHtml ?? variables.data.descriptionHtml,
                descriptionRewritten: data.descriptionRewritten,
                descriptionImagesInserted: data.descriptionImagesInserted,
              }
            : prev,
      );
    },
  });
};

/** Local-only supplier source URL — patches list cards without a full refetch. */
export const useUpdateListingSourceUrl = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, sourceUrl }: { id: string; sourceUrl: string }) => {
      const response = await listingApi.updateSourceUrl(id, { sourceUrl });
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Lưu URL nguồn thất bại.');
      }
      return response.data;
    },
    onSuccess: (data, variables) => {
      message.success('Đã lưu URL nguồn.');
      queryClient.setQueriesData(
        { queryKey: ['listings'] },
        (prev: ListingListPage | undefined) => {
          if (!prev?.items) return prev;
          return {
            ...prev,
            items: prev.items.map((item) =>
              item.id === data.listingId ? mergeListingSourceUrl(item, data) : item,
            ),
          };
        },
      );
      queryClient.setQueryData(
        ['listing', variables.id],
        (prev: ListingDetail | undefined) =>
          prev
            ? { ...mergeListingSourceUrl(prev, data), listingSourceUrl: data.sourceUrl }
            : prev,
      );
      invalidateListingsCache(queryClient, {
        refetchType: 'none',
        reason: 'source-url',
      });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

/** Map description → category Specifics — suggestion only; no listing invalidate. */
export const useMapListingSpecifics = (listingId: string | undefined) =>
  useMutation({
    mutationFn: async (data: MapListingSpecificsRequest) => {
      if (!listingId) {
        throw new Error('Thiếu listing id.');
      }
      const response = await listingApi.mapSpecifics(listingId, data);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Auto map Specifics thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

/** Bind recent-use category onto draft listing (batch-binging-category). */
export const useBindListingCategory = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      data,
    }: {
      id: string;
      data: BindListingCategoryRequest;
    }) => {
      const response = await listingApi.bindCategory(id, data);
      if (!response.success) {
        throw new Error(response.message || 'Gắn category thất bại.');
      }
      return response;
    },
    onSuccess: (response, variables) => {
      message.success(response.message || 'Đã gắn category cho listing.');
      invalidateListingsCache(queryClient, { reason: 'bind-category' });
      void queryClient.invalidateQueries({ queryKey: ['listing', variables.id] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useListingAutoPublishRuns = (
  listingId: string | null | undefined,
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ['listingAutoPublishRuns', listingId],
    queryFn: async () => {
      const response = await listingApi.listAutoPublishRuns(listingId!);
      if (!response.success) {
        throw new Error(response.message || 'Không tải được log auto publish.');
      }
      return response.data ?? [];
    },
    enabled: (options?.enabled ?? true) && !!listingId,
    staleTime: 10_000,
    refetchInterval: (query) => {
      const latest = query.state.data?.[0];
      const status = latest?.status?.trim().toLowerCase();
      return status === 'running' ? 2_000 : false;
    },
  });

export const useRetryListingAutoPublish = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (listingId: string) => {
      const response = await listingApi.retryAutoPublish(listingId);
      if (!response.success) {
        throw new Error(response.message || 'Không retry được auto publish.');
      }
      let patches: ListingAutoPublishQueueStatusItem[] = [];
      try {
        const status = await listingApi.autoPublishQueueStatus([listingId]);
        if (status.success) {
          patches = status.data?.items ?? [];
        }
      } catch {
        // Retry đã xong — badge sẽ cập nhật khi poll in-flight hoặc Tải lại.
      }
      return { response, patches };
    },
    onSuccess: ({ response, patches }, listingId) => {
      message.success(response.message || 'Đã cho phép auto publish lại.');
      queryClient.setQueriesData<ListingListPage>({ queryKey: ['listings'] }, (page) =>
        applyAutoPublishRetryToListPage(page, listingId, patches),
      );
      invalidateListingsCache(queryClient, { reason: 'auto-publish-retry', refetchType: 'none' });
      void queryClient.invalidateQueries({ queryKey: ['listingAutoPublishRuns', listingId] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};