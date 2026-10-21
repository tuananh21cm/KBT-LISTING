import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { ORDER_QUERY_KEYS, invalidateOrdersCaches } from '@drop/api-client';
import { orderApi } from '../services/orderApi';
import { platformApi } from '../services/platformApi';
import { shopApi } from '../services/shopApi';
import { rangeForPresetVn, toUtcIsoRange } from '../utils/datetime';
import type {
  MarkShipRequest,
  OrderListParams,
  OrderListPage,
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

export const ORDERS_DEFAULT_PAGE_SIZE = 50;

export const getDefaultOrdersListParams = (): OrderListParams => {
  const [from, to] = rangeForPresetVn('30d');
  const { dateFrom, dateTo } = toUtcIsoRange(from, to);
  return {
    pageCurrent: 1,
    pageSize: ORDERS_DEFAULT_PAGE_SIZE,
    platformOrderStatus: undefined,
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
  pageSize: params.pageSize ?? ORDERS_DEFAULT_PAGE_SIZE,
});

const fetchOrdersPage = async (params: OrderListParams): Promise<OrderListPage> => {
  const response = await orderApi.list(params);
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

const fetchOrderStatusCounts = async (params: OrderListParams) => {
  const response = await orderApi.statusCounts(params);
  if (!response.success) {
    throw new Error(response.message);
  }
  return response.data ?? null;
};

/** Warm Orders page caches on menu hover. */
export const prefetchOrdersPageData = (queryClient: QueryClient): void => {
  const params = getDefaultOrdersListParams();
  void queryClient.prefetchQuery({
    queryKey: [ORDER_QUERY_KEYS.sellerList, params],
    queryFn: () => fetchOrdersPage(params),
    staleTime: 30_000,
  });
  void queryClient.prefetchQuery({
    queryKey: [ORDER_QUERY_KEYS.sellerStatusCounts, toStatusCountsScope(params)],
    queryFn: () => fetchOrderStatusCounts(params),
    staleTime: 30_000,
  });
  void queryClient.prefetchQuery({
    queryKey: ['shops'],
    queryFn: async () => {
      const response = await shopApi.list();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
  });
  void queryClient.prefetchQuery({
    queryKey: ['platformOptions'],
    queryFn: async () => {
      const response = await platformApi.getOptions();
      if (!response.success) throw new Error(response.message);
      return response.data ?? [];
    },
    staleTime: 5 * 60_000,
  });
};

export const useOrders = (params: OrderListParams) =>
  useQuery({
    queryKey: [ORDER_QUERY_KEYS.sellerList, params],
    queryFn: () => fetchOrdersPage(params),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    refetchOnMount: 'always',
  });

/**
 * Tab counts, fetched separately from the list so paging never recomputes them.
 * Always invalidated alongside the list via `invalidateOrdersCaches`.
 */
export const useOrderStatusCounts = (params: OrderListParams) =>
  useQuery({
    queryKey: [ORDER_QUERY_KEYS.sellerStatusCounts, toStatusCountsScope(params)],
    queryFn: () => fetchOrderStatusCounts(params),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

export const useOrderDetail = (id: string | null) =>
  useQuery({
    queryKey: ['sellerOrder', id],
    queryFn: async () => {
      if (!id) {
        return null;
      }
      const response = await orderApi.getById(id);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data;
    },
    enabled: !!id,
  });

export const useShippingServices = (
  platform: string | null | undefined,
  enabled: boolean,
) =>
  useQuery({
    queryKey: ['sellerOrderShippingServices', platform],
    queryFn: async () => {
      const response = await orderApi.listShippingServices(platform);
      if (!response.success) {
        throw new Error(response.message);
      }
      return response.data ?? [];
    },
    enabled,
    staleTime: 5 * 60 * 1000,
  });

/**
 * Starts an order sync job. The pull itself runs server-side — result toasts come from the
 * status poll on OrdersPage.
 */
export const useSyncOrders = () =>
  useMutation({
    mutationFn: async (data: OrderSyncRequest = {}) => {
      const response = await orderApi.sync(data);
      if (!response.success) {
        throw new Error(response.message || 'Đồng bộ đơn hàng thất bại.');
      }
      return response;
    },
    onError: (error: Error) => message.error(error.message),
  });

/** Polls an order sync job every 2s until it finishes. */
export const useOrderSyncJob = (jobId: string | null) =>
  useQuery({
    queryKey: ['sellerOrderSyncJob', jobId],
    queryFn: async () => {
      const response = await orderApi.syncStatus(jobId!);
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

export const useMarkShipOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: MarkShipRequest }) => orderApi.markShip(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Mark Shipped thành công.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      invalidateOrdersCaches(queryClient, 'seller');
      queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
    },
  });
};

/** Sync track — toast chi tiết do OrdersPage xử lý; hook chỉ invalidate cache. */
export const useSyncTrackOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => orderApi.syncTrack(id),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      }
    },
    onError: () => {
      invalidateOrdersCaches(queryClient, 'seller');
      queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
    },
  });
};

/** Sync trạng thái sàn — toast do OrdersPage; invalidate list/detail. */
export const useSyncPlatformStatus = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => orderApi.syncPlatformStatus(id),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
      }
    },
    onError: () => {
      invalidateOrdersCaches(queryClient, 'seller');
      queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
    },
  });
};

export const useUpdateOrderCost = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderCostRequest }) =>
      orderApi.updateCost(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật phí đơn hàng.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.updateMilestones(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật mốc fulfil.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.updateSettlement(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái thanh toán.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.updateUspsProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái USPS.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
          (error instanceof Error ? error.message : 'Cập nhật USPS thất bại.'),
      );
    },
  });
};

export const usePushUspsScan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: PushUspsScanRequest) => orderApi.pushUspsScan(data),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
        queryClient.invalidateQueries({ queryKey: ['fulfilTransactions'] });
      }
    },
  });
};

export const useUpdateOrderThankCardProcessed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderThankCardProcessedRequest }) =>
      orderApi.updateThankCardProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật ThankCard.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
          (error instanceof Error ? error.message : 'Cập nhật ThankCard thất bại.'),
      );
    },
  });
};

export const useUpdateOrderRefundProcessed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateOrderRefundProcessedRequest }) =>
      orderApi.updateRefundProcessed(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái Refund.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.updateNotes(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật ghi chú đơn hàng.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.updateMessage(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật thông báo đơn hàng.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
    queryKey: ['sellerIssueTags'],
    queryFn: async () => {
      const res = await orderApi.listIssueTags();
      return res.data ?? [];
    },
    enabled,
    staleTime: 60_000,
  });
};

export const useCreateIssueTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ label }: { label: string }) => orderApi.createIssueTag({ label }),
    onSuccess: (response) => {
      if (response.success) {
        queryClient.invalidateQueries({ queryKey: ['sellerIssueTags'] });
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
      orderApi.updateIssueTag(tagId, { label }),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật nhãn.');
        queryClient.invalidateQueries({ queryKey: ['sellerIssueTags'] });
        queryClient.invalidateQueries({ queryKey: ['orders'] });
        queryClient.invalidateQueries({ queryKey: ['order'] });
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
    mutationFn: ({ tagId }: { tagId: string }) => orderApi.deleteIssueTag(tagId),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa nhãn.');
        queryClient.invalidateQueries({ queryKey: ['sellerIssueTags'] });
        queryClient.invalidateQueries({ queryKey: ['orders'] });
        queryClient.invalidateQueries({ queryKey: ['order'] });
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
      orderApi.setOrderIssueTags(id, data),
    onSuccess: (response) => {
      if (response.success) {
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
      orderApi.setOrderIssueStatus(id, data),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã cập nhật trạng thái lỗi.');
        invalidateOrdersCaches(queryClient, 'seller');
        queryClient.invalidateQueries({ queryKey: ['sellerOrder'] });
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
