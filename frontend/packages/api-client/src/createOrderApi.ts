import type { AxiosInstance } from 'axios';
import type { ApiResponse } from '@drop/types/api';
import type {
  MarkShipRequest,
  MarkShipResult,
  OrderDetail,
  OrderHoverPreview,
  OrderListPage,
  OrderListParams,
  OrderStatusCounts,
  OrderAfterSalesSyncRequest,
  OrderAfterSalesSyncResult,
  OrderSyncRequest,
  OrderSyncJobStatus,
  OrderPlatformStatusSyncResult,
  SheinOrderSyncTrackResult,
  ShippingServiceOption,
  UpdateOrderCostRequest,
  UpdateOrderMilestonesRequest,
  UpdateOrderNotesRequest,
  UpdateOrderMessageRequest,
  UpdateOrderSettlementRequest,
  UpdateOrderUspsProcessedRequest,
  PushUspsScanRequest,
  UspsScanPushResult,
  UpdateOrderThankCardProcessedRequest,
  UpdateOrderRefundProcessedRequest,
  IssueTag,
  CreateIssueTagRequest,
  UpdateIssueTagRequest,
  SetOrderIssueTagsRequest,
  SetOrderIssueStatusRequest,
} from '@drop/types/order';
export interface OrderDeleteResult {
  deletedCount: number;
}

export type OrderApiBasePath = '/orders' | '/admin/orders';

export type OrderApi = {
  list: (params?: OrderListParams) => Promise<ApiResponse<OrderListPage>>;
  /**
   * Workflow tab counts. Scope ignores paging and the selected tab, so the result stays
   * valid while the user pages or switches tabs.
   */
  statusCounts: (params?: OrderListParams) => Promise<ApiResponse<OrderStatusCounts>>;
  getById: (id: string) => Promise<ApiResponse<OrderDetail>>;
  /** Admin: lightweight hover card. Seller path may 404 if not wired. */
  getPreview: (id: string) => Promise<ApiResponse<OrderHoverPreview>>;
  /** Starts a detached sync job (HTTP 202). Poll syncStatus until terminal. */
  sync: (data?: OrderSyncRequest) => Promise<ApiResponse<OrderSyncJobStatus>>;
  /** Progress of a queued order sync job. */
  syncStatus: (jobId: string) => Promise<ApiResponse<OrderSyncJobStatus>>;
  /**
   * Admin only: runs the after-sales (return / refund) sync now. Sets / releases REFUND, which
   * the plain order sync never touches.
   */
  syncAfterSales?: (
    data?: OrderAfterSalesSyncRequest,
  ) => Promise<ApiResponse<OrderAfterSalesSyncResult>>;
  markShip: (id: string, data: MarkShipRequest) => Promise<ApiResponse<MarkShipResult>>;
  /** SHEIN get_order_info → refresh tracking + cost + Auto Mark Ship for one order. */
  syncTrack: (id: string) => Promise<ApiResponse<SheinOrderSyncTrackResult>>;
  /** 4Seller exact orderNo → refresh commerce PlatformOrderStatus (+ tracking / clocks). */
  syncPlatformStatus: (id: string) => Promise<ApiResponse<OrderPlatformStatusSyncResult>>;
  updateCost: (id: string, data: UpdateOrderCostRequest) => Promise<ApiResponse<OrderDetail>>;
  updateMilestones: (
    id: string,
    data: UpdateOrderMilestonesRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  updateSettlement: (
    id: string,
    data: UpdateOrderSettlementRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  updateUspsProcessed: (
    id: string,
    data: UpdateOrderUspsProcessedRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  /** Bulk USPS track-scan push (PrintPackShip create → send). Not idempotent. */
  pushUspsScan: (data: PushUspsScanRequest) => Promise<ApiResponse<UspsScanPushResult>>;
  updateThankCardProcessed: (
    id: string,
    data: UpdateOrderThankCardProcessedRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  updateRefundProcessed: (
    id: string,
    data: UpdateOrderRefundProcessedRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  updateNotes: (id: string, data: UpdateOrderNotesRequest) => Promise<ApiResponse<OrderDetail>>;
  updateMessage: (id: string, data: UpdateOrderMessageRequest) => Promise<ApiResponse<OrderDetail>>;
  listIssueTags: () => Promise<ApiResponse<IssueTag[]>>;
  createIssueTag: (data: CreateIssueTagRequest) => Promise<ApiResponse<IssueTag>>;
  updateIssueTag: (tagId: string, data: UpdateIssueTagRequest) => Promise<ApiResponse<IssueTag>>;
  deleteIssueTag: (tagId: string) => Promise<ApiResponse<null>>;
  setOrderIssueTags: (id: string, data: SetOrderIssueTagsRequest) => Promise<ApiResponse<OrderDetail>>;
  setOrderIssueStatus: (
    id: string,
    data: SetOrderIssueStatusRequest,
  ) => Promise<ApiResponse<OrderDetail>>;
  /** Admin: requires ownerUserId. Seller: platform only. */
  listShippingServices: (
    ownerUserIdOrPlatform?: string | null,
    platform?: string | null,
  ) => Promise<ApiResponse<ShippingServiceOption[]>>;
  deleteById?: (id: string) => Promise<ApiResponse<null>>;
  deleteByFilter?: (params?: OrderListParams) => Promise<ApiResponse<OrderDeleteResult>>;
};

/**
 * Shared order API client. Admin uses `/admin/orders` (+ delete).
 * Seller uses `/orders` (no delete; shipping-services takes platform only).
 */
export function createOrderApi(
  http: AxiosInstance,
  opts: { basePath: OrderApiBasePath },
): OrderApi {
  const { basePath } = opts;
  const isAdmin = basePath === '/admin/orders';
  const issueTagsBase = isAdmin ? '/admin/issue-tags' : '/issue-tags';

  const api: OrderApi = {
    list: async (params: OrderListParams = {}) => {
      const response = await http.get<ApiResponse<OrderListPage>>(basePath, { params });
      return response.data;
    },

    statusCounts: async (params: OrderListParams = {}) => {
      // Strip everything the counts scope ignores so paging / tab switches reuse one cache entry.
      const {
        pageCurrent: _pageCurrent,
        pageSize: _pageSize,
        cursor: _cursor,
        platformOrderStatus: _platformOrderStatus,
        pendingFilter: _pendingFilter,
        ...filters
      } = params;
      const response = await http.get<ApiResponse<OrderStatusCounts>>(
        `${basePath}/status-counts`,
        { params: filters },
      );
      return response.data;
    },

    getById: async (id: string) => {
      const response = await http.get<ApiResponse<OrderDetail>>(`${basePath}/${id}`);
      return response.data;
    },

    getPreview: async (id: string) => {
      const response = await http.get<ApiResponse<OrderHoverPreview>>(`${basePath}/${id}/preview`);
      return response.data;
    },

    // Returns 202 with a job id: the pull runs server-side, the client polls syncStatus.
    sync: async (data: OrderSyncRequest = {}) => {
      const response = await http.post<ApiResponse<OrderSyncJobStatus>>(`${basePath}/sync`, data);
      return response.data;
    },

    syncStatus: async (jobId: string) => {
      const response = await http.get<ApiResponse<OrderSyncJobStatus>>(
        `${basePath}/sync/status/${jobId}`,
      );
      return response.data;
    },

    markShip: async (id: string, data: MarkShipRequest) => {
      const response = await http.post<ApiResponse<MarkShipResult>>(
        `${basePath}/${id}/mark-ship`,
        data,
        { timeout: 60_000 },
      );
      return response.data;
    },

    syncTrack: async (id: string) => {
      const response = await http.post<ApiResponse<SheinOrderSyncTrackResult>>(
        `${basePath}/${id}/sync-track`,
        undefined,
        { timeout: 120_000 },
      );
      return response.data;
    },

    syncPlatformStatus: async (id: string) => {
      const response = await http.post<ApiResponse<OrderPlatformStatusSyncResult>>(
        `${basePath}/${id}/sync-platform-status`,
        undefined,
        { timeout: 60_000 },
      );
      return response.data;
    },

    updateCost: async (id: string, data: UpdateOrderCostRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(`${basePath}/${id}/cost`, data);
      return response.data;
    },

    updateMilestones: async (id: string, data: UpdateOrderMilestonesRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/milestones`,
        data,
      );
      return response.data;
    },

    updateSettlement: async (id: string, data: UpdateOrderSettlementRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/settlement`,
        data,
      );
      return response.data;
    },

    updateUspsProcessed: async (id: string, data: UpdateOrderUspsProcessedRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/usps-processed`,
        data,
      );
      return response.data;
    },

    pushUspsScan: async (data: PushUspsScanRequest) => {
      const response = await http.post<ApiResponse<UspsScanPushResult>>(
        `${basePath}/usps-scan/push`,
        data,
      );
      return response.data;
    },

    updateThankCardProcessed: async (id: string, data: UpdateOrderThankCardProcessedRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/thank-card-processed`,
        data,
      );
      return response.data;
    },

    updateRefundProcessed: async (id: string, data: UpdateOrderRefundProcessedRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/refund-processed`,
        data,
      );
      return response.data;
    },

    updateNotes: async (id: string, data: UpdateOrderNotesRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(`${basePath}/${id}/notes`, data);
      return response.data;
    },

    updateMessage: async (id: string, data: UpdateOrderMessageRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(`${basePath}/${id}/message`, data);
      return response.data;
    },

    listIssueTags: async () => {
      const response = await http.get<ApiResponse<IssueTag[]>>(issueTagsBase);
      return response.data;
    },

    createIssueTag: async (data: CreateIssueTagRequest) => {
      const response = await http.post<ApiResponse<IssueTag>>(issueTagsBase, data);
      return response.data;
    },

    updateIssueTag: async (tagId: string, data: UpdateIssueTagRequest) => {
      const response = await http.put<ApiResponse<IssueTag>>(`${issueTagsBase}/${tagId}`, data);
      return response.data;
    },

    deleteIssueTag: async (tagId: string) => {
      const response = await http.delete<ApiResponse<null>>(`${issueTagsBase}/${tagId}`);
      return response.data;
    },

    setOrderIssueTags: async (id: string, data: SetOrderIssueTagsRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/issue-tags`,
        data,
      );
      return response.data;
    },

    setOrderIssueStatus: async (id: string, data: SetOrderIssueStatusRequest) => {
      const response = await http.put<ApiResponse<OrderDetail>>(
        `${basePath}/${id}/issue-status`,
        data,
      );
      return response.data;
    },

    listShippingServices: async (
      ownerUserIdOrPlatform?: string | null,
      platform?: string | null,
    ) => {
      if (isAdmin) {
        const response = await http.get<ApiResponse<ShippingServiceOption[]>>(
          `${basePath}/shipping-services`,
          {
            params: {
              ownerUserId: ownerUserIdOrPlatform || undefined,
              platform: platform || undefined,
            },
          },
        );
        return response.data;
      }
      const response = await http.get<ApiResponse<ShippingServiceOption[]>>(
        `${basePath}/shipping-services`,
        {
          params: { platform: ownerUserIdOrPlatform || undefined },
        },
      );
      return response.data;
    },
  };

  if (isAdmin) {
    api.deleteById = async (id: string) => {
      const response = await http.delete<ApiResponse<null>>(`${basePath}/${id}`);
      return response.data;
    };

    api.deleteByFilter = async (params: OrderListParams = {}) => {
      const { pageCurrent: _pageCurrent, pageSize: _pageSize, ...filters } = params;
      const response = await http.delete<ApiResponse<OrderDeleteResult>>(basePath, {
        params: filters,
      });
      return response.data;
    };

    api.syncAfterSales = async (data: OrderAfterSalesSyncRequest = {}) => {
      // Triggers 4Seller's TikTok pull then reads every seller's return pages back, so it is
      // slower than a plain list call but far cheaper than a full order sync.
      const response = await http.post<ApiResponse<OrderAfterSalesSyncResult>>(
        `${basePath}/after-sales/sync`,
        data,
        { timeout: 300_000 },
      );
      return response.data;
    };
  }

  return api;
}
