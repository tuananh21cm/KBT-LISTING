import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { ORDER_QUERY_KEYS, invalidateOrdersCaches } from '@drop/api-client';
import { adminOrderApi } from '../services/adminOrderApi';
import { adminShopApi } from '../services/adminShopApi';
import { adminPlatformApi } from '../services/adminPlatformApi';
import { adminUserApi } from '../services/adminUserApi';
import { rangeForPresetVn, toUtcIsoRange } from '../utils/datetime';
import type {
  MarkShipRequest,
  OrderListParams,
  OrderListPage,
  OrderAfterSalesSyncRequest,
  OrderSyncRequest,
  UpdateOrderCostRequest,
  UpdateOrderMilestonesRequest,
  UpdateOrderNotesRequest,
  UpdateOrderMessageRequest,
  UpdateOrderSettlementRequest,
  UpdateOrderUspsProcessedRequest,
  PushUspsScanRequest,
  UpdateOrderThankCardProcessedRequest,
  UpdateOrderRefundProcessedRequest,
} from '../types/order';

/** Default list page size (Orders UI). */
export const ADMIN_ORDERS_DEFAULT_PAGE_SIZE = 50;

/** Query params matching OrdersPage first paint (30 ngày + page 1). */
export const getDefaultAdminOrdersListParams = (): OrderListParams => {
  const [from, to] = rangeForPresetVn('30d');
  const { dateFrom, dateTo } = toUtcIsoRange(from, to);
  return {
    pageCurrent: 1,
    pageSize: ADMIN_ORDERS_DEFAULT_PAGE_SIZE,
    platformOrderStatus: undefined,
    ownerUserId: undefined,
    platform: undefined,
    shopId: undefined,
    search: undefined,
    dateFrom,
    dateTo,
  };
};

const emptyOrderListPage = (params: OrderListParams): OrderListPage => ({
  items: [],
  total: 0,
  pageCurrent: params.pageCurrent ?? 1,
  pageSize: params.pageSize ?? ADMIN_ORDERS_DEFAULT_PAGE_SIZE,
});

const fetchAdminOrdersPage = async (params: OrderListParams): Promise<OrderListPage> => {
  const response = await adminOrderApi.list(params);
  if (!response.success) {
    throw new Error(response.message);
  }
  return response.data ?? emptyOrderListPage(params);
};

/**
 * Cache key for tab counts: drops paging and the selected tab because the counts scope
 * ignores them, so paging or switching tabs reuses one cached result.
 */
const toStatusCountsScope = (params: OrderListParams): OrderListParams => {
  const {
    pageCurrent: _pageCurrent,
    pageSize: _pageSize,
    platformOrderStatus: _platformOrderStatus,
    pendingFilter: _pendingFilter,
    ...scope
  } = params;
  return scope;
};

const fetchAdminOrderStatusCounts = async (params: OrderListParams) => {
  const response = await adminOrderApi.statusCounts(params);
  if (!response.success) {
    throw new Error(response.message);
  }
  return response.data ?? null;
};

/**
 * Warm Orders page caches on menu hover so click shows shell + (often) data immediately.
 */
export const prefetchOrdersPageData = (queryClient: QueryClient): void => {
  const params = getDefaultAdminOrdersListParams();
  void queryClient.prefetchQuery({
    queryKey: [ORDER_QUERY_KEYS.adminList, params],
    queryFn: () => fetchAdminOrdersPage(params),
    staleTime: 30_000,
  });
  void queryClient.prefetchQuery({
    queryKey: [ORDER_QUERY_KEYS.adminStatusCounts, toStatusCountsScope(params)],
    queryFn: () => fetchAdminOrderStatusCounts(params),
    staleTime: 30_000,
  });
  void queryClient.prefetchQuery({
    queryKey: ['adminShopOptions'],
    queryFn: async () => {
      const response = await adminShopApi.getOptions!();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
  });
  void queryClient.prefetchQuery({
    queryKey: ['platformOptions'],
    queryFn: async () => {
      const response = await adminPlatformApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
  });
  void queryClient.prefetchQuery({
    queryKey: ['sellers'],
    queryFn: async () => {
      const response = await adminUserApi.getSellers();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
  });
};

export const useAdminOrders = (params: OrderListParams) =>
  useQuery({
    queryKey: [ORDER_QUERY_KEYS.adminList, params],
    queryFn: () => fetchAdminOrdersPage(params),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    /** Mỗi lần vào trang refetch → Spin hiện (vẫn giữ data cũ dưới overlay nếu đã cache). */
    refetchOnMount: 'always',
  });

/**
 * Tab counts, fetched separately from the list so paging never recomputes them.
 * Always invalidated alongside the list via `invalidateOrdersCaches`.
 */
export const useAdminOrderStatusCounts = (params: OrderListParams) =>
  useQuery({
    queryKey: [ORDER_QUERY_KEYS.adminStatusCounts, toStatusCountsScope(params)],
    queryFn: () => fetchAdminOrderStatusCounts(params),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

export const useAdminOrderDetail = (id: string | null) =>
  useQuery({
    queryKey: ['adminOrder', id],
    queryFn: async () => {
      if (!id) {
        return null;
      }
      const response = await adminOrderApi.getById(id);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: !!id,
  });

/** Lazy hover preview — only fetches while `enabled` (popover open). */
export const useAdminOrderPreview = (id: string | null, enabled: boolean) =>
  useQuery({
    queryKey: ['adminOrderPreview', id],
    queryFn: async () => {
      if (!id) {
        return null;
      }
      const response = await adminOrderApi.getPreview(id);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: enabled && !!id,
    staleTime: 60_000,
  });

export const useShippingServices = (
  ownerUserId: string | null | undefined,
  platform: string | null | undefined,
  enabled: boolean,
) =>
  useQuery({
    queryKey: ['adminOrderShippingServices', ownerUserId, platform],
    queryFn: async () => {
      if (!ownerUserId) {
        return [];
      }
      const response = await adminOrderApi.listShippingServices(ownerUserId, platform);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled: enabled && !!ownerUserId,
    staleTime: 5 * 60 * 1000,
  });

/**
 * Starts an order sync job. The pull itself runs server-side — result toasts come from the
 * status poll, so this only reports a failure to enqueue.
 */
export const useSyncAdminOrders = () =>
  useMutation({
    mutationFn: async (data: OrderSyncRequest = {}) => {
      const response = await adminOrderApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ đơn hàng thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

/** Polls an order sync job every 2s until it finishes. */
export const useAdminOrderSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['adminOrderSyncJob', jobId],
    queryFn: async () => {
      const response = await adminOrderApi.syncStatus(jobId!);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không đọc được tiến độ đồng bộ.');
      }
      return response.data;
    },
    enabled: !!jobId,
    refetchInterval: (query) =>
      query.state.data && query.state.data.state !== 'running' ? false : 2000,
    refetchOnWindowFocus: false,
    gcTime: 0,
    retry: false,
  });

/**
 * Runs the after-sales (return / refund) sync now. Separate from `useSyncAdminOrders` because it
 * is the only flow allowed to set or release REFUND — the order sync just latches what it wrote.
 */
export const useSyncAdminAfterSales = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (data: OrderAfterSalesSyncRequest = {}) => {
      const response = await adminOrderApi.syncAfterSales!(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ đơn sau bán thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      const result = response.data;
      if (result?.skipped) {
        message.warning(response.message || 'Đồng bộ đang chạy. Thử lại sau.');
        return;
      }

      const failures = result?.failedSellers ?? [];
      const failureDetail = failures
        .map((f) => `${f.ownerName?.trim() || f.ownerUserId}: ${f.message}`)
        .join(' | ');

      if (failures.length > 0) {
        message.warning(
          `${response.message || 'Đồng bộ sau bán xong có lỗi.'}${failureDetail ? ` — ${failureDetail}` : ''}`,
          8,
        );
      } else {
        message.success(response.message || 'Đồng bộ đơn sau bán thành công.');
      }
      invalidateOrdersCaches(queryClient, 'admin');
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const response = await adminOrderApi.deleteById!(id);
      if (!response.success) {
        throw new Error(response.message || 'Xóa đơn hàng thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || 'Đã xóa đơn hàng local.');
      invalidateOrdersCaches(queryClient, 'admin');
      queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteAdminOrdersByFilter = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (params: OrderListParams) => {
      const response = await adminOrderApi.deleteByFilter!(params);
      if (!response.success) {
        throw new Error(response.message || 'Xóa đơn hàng thất bại.');
      }
      return response;
    },
    onSuccess: (response) => {
      message.success(response.message || `Đã xóa ${response.data?.deletedCount ?? 0} đơn hàng local.`);
      invalidateOrdersCaches(queryClient, 'admin');
      queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useMarkShipOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: MarkShipRequest }) => adminOrderApi.markShip(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Mark Shipped thành công.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Mark Shipped thất bại.'),
      );
      // Manual Mark Ship persists autoMarkShipError on supplier link before returning 4xx.
      invalidateOrdersCaches(queryClient, 'admin');
      queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
    },
  });
};

/** Sync track — toast chi tiết do OrdersPage xử lý; hook chỉ invalidate cache. */
export const useSyncTrackOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminOrderApi.syncTrack(id),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      }
    },
    onError: () => {
      invalidateOrdersCaches(queryClient, 'admin');
      queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
    },
  });
};

/** Sync trạng thái sàn — toast do OrdersPage; invalidate list/detail. */
export const useSyncPlatformStatus = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => adminOrderApi.syncPlatformStatus(id),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      }
    },
    onError: () => {
      invalidateOrdersCaches(queryClient, 'admin');
      queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
    },
  });
};

export const useUpdateOrderCost = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderCostRequest }) =>
      adminOrderApi.updateCost(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật phí đơn hàng.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật phí thất bại.'),
      );
    },
  });
};

export const useUpdateOrderMilestones = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderMilestonesRequest }) =>
      adminOrderApi.updateMilestones(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật mốc fulfil.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật mốc fulfil thất bại.'),
      );
    },
  });
};

export const useUpdateOrderSettlement = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderSettlementRequest }) =>
      adminOrderApi.updateSettlement(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái thanh toán.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['receivingBankIncomingStats'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật thanh toán thất bại.'),
      );
    },
  });
};

export const useUpdateOrderUspsProcessed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderUspsProcessedRequest }) =>
      adminOrderApi.updateUspsProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái USPS.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật USPS thất bại.'),
      );
    },
  });
};

export const usePushUspsScan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: PushUspsScanRequest) => adminOrderApi.pushUspsScan(data),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      }
    },
  });
};

export const useUpdateOrderThankCardProcessed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderThankCardProcessedRequest }) =>
      adminOrderApi.updateThankCardProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật ThankCard.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilSpendingStats'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật ThankCard thất bại.'),
      );
    },
  });
};

export const useUpdateOrderRefundProcessed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderRefundProcessedRequest }) =>
      adminOrderApi.updateRefundProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái Refund.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật Refund thất bại.'),
      );
    },
  });
};

export const useUpdateOrderNotes = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderNotesRequest }) =>
      adminOrderApi.updateNotes(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật ghi chú đơn hàng.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật ghi chú thất bại.'),
      );
    },
  });
};

export const useUpdateOrderMessage = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderMessageRequest }) =>
      adminOrderApi.updateMessage(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật thông báo đơn hàng.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật thông báo thất bại.'),
      );
    },
  });
};

export const useIssueTagLibrary = (enabled: boolean) => {
  return useQuery({
    queryKey: ['adminIssueTags'],
    queryFn: async () => {
      const res = await adminOrderApi.listIssueTags();
      return res.data ?? [];
    },
    enabled,
    staleTime: 60_000,
  });
};

export const useCreateIssueTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ label }: { label: string }) => adminOrderApi.createIssueTag({ label }),
    onSuccess: (response) => {
      if (response.success) {
        queryClient.invalidateQueries({ queryKey: ['adminIssueTags'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Thêm tag thất bại.'),
      );
    },
  });
};

export const useUpdateIssueTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ tagId, label }: { tagId: string; label: string }) =>
      adminOrderApi.updateIssueTag(tagId, { label }),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật nhãn.');
        queryClient.invalidateQueries({ queryKey: ['adminIssueTags'] });
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật nhãn thất bại.'),
      );
    },
  });
};

export const useDeleteIssueTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ tagId }: { tagId: string }) => adminOrderApi.deleteIssueTag(tagId),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa nhãn.');
        queryClient.invalidateQueries({ queryKey: ['adminIssueTags'] });
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Xóa nhãn thất bại.'),
      );
    },
  });
};

export const useSetOrderIssueTags = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: { tagIds: string[] } }) =>
      adminOrderApi.setOrderIssueTags(id, data),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật tag thất bại.'),
      );
    },
  });
};

export const useSetOrderIssueStatus = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: { status: string } }) =>
      adminOrderApi.setOrderIssueStatus(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái lỗi.');
        invalidateOrdersCaches(queryClient, 'admin');
        queryClient.invalidateQueries({ queryKey: ['adminOrder'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: unknown) => {
      const axiosMsg =
        error &&
        typeof error === 'object' &&
        'response' in error &&
        (error as { response?: { data?: { message?: string } } }).response?.data?.message;
      message.error(
        (typeof axiosMsg === 'string' && axiosMsg) ||
          (error instanceof Error ? error.message : 'Cập nhật trạng thái thất bại.'),
      );
    },
  });
};
