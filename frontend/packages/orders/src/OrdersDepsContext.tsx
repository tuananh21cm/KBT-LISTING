import { createContext, useContext, type ComponentType, type ReactNode } from 'react';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import type { ApiResponse } from '@drop/types/api';
import type { PlatformOption } from '@drop/types/platform';
import type {
  MarkShipRequest,
  MarkShipResult,
  OrderDetail,
  OrderListItem,
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
  SetOrderIssueTagsRequest,
  SetOrderIssueStatusRequest,
} from '@drop/types/order';
/** Slim shop shape enough for the filter dropdown — admin adds `ownerId` for seller-scoped filtering. */
export type OrdersShopOption = {
  id: string;
  name: string;
  profileName?: string | null;
  platformCode?: string | null;
  status?: string;
  ownerId?: string;
};

/** Admin-only seller filter option. */
export type OrdersSellerOption = {
  id: string;
  fullName?: string | null;
  userName?: string | null;
};

/** Admin-only setting row (listing rating thresholds). */
export type OrdersAdminSettingOption = {
  key: string;
  value: string;
};

export type OrderDeleteResult = { deletedCount: number };

/**
 * Hooks/data the shared Orders page needs, without importing anything app-specific
 * (routing, stores, admin-only services…). Each portal (admin/seller) provides its own
 * implementation backed by its own React Query hooks.
 */
export type OrdersDeps = {
  useOrdersList: (
    params: OrderListParams,
  ) => Pick<UseQueryResult<OrderListPage>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch: () => Promise<unknown>;
  };
  /**
   * Workflow tab counts. Separate from the list because the counts scope ignores paging and
   * the selected tab — folding them into the list response recomputed them on every page turn.
   */
  useOrderStatusCounts: (
    params: OrderListParams,
  ) => Pick<UseQueryResult<OrderStatusCounts | null | undefined>, 'data' | 'isLoading'>;
  useOrderDetail: (
    id: string | null,
  ) => Pick<UseQueryResult<OrderDetail | null | undefined>, 'data' | 'isLoading'>;
  useSyncOrders: () => Pick<
    UseMutationResult<ApiResponse<OrderSyncJobStatus>, unknown, OrderSyncRequest>,
    'mutateAsync' | 'isPending'
  >;
  /** Polls a started order sync job until it reaches a terminal state. Pass null to stop. */
  useOrderSyncJob: (jobId: string | null) => { data?: OrderSyncJobStatus };
  /**
   * Admin only — runs the after-sales sync (returns / refunds). Kept separate from
   * `useSyncOrders` because only this flow sets or releases the REFUND status.
   */
  useSyncAfterSales?: () => Pick<
    UseMutationResult<
      ApiResponse<OrderAfterSalesSyncResult>,
      unknown,
      OrderAfterSalesSyncRequest
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Admin only — delete a single local order. */
  useDeleteOrder?: () => Pick<
    UseMutationResult<ApiResponse<null>, unknown, string>,
    'mutateAsync' | 'isPending' | 'variables'
  >;
  /** Admin only — bulk delete matching the current filter. */
  useDeleteOrdersByFilter?: () => Pick<
    UseMutationResult<ApiResponse<OrderDeleteResult>, unknown, OrderListParams>,
    'mutateAsync' | 'isPending'
  >;
  useMarkShipOrder: () => Pick<
    UseMutationResult<ApiResponse<MarkShipResult>, unknown, { id: string; data: MarkShipRequest }>,
    'mutateAsync' | 'isPending'
  >;
  /** Sync track: SHEIN detail + cost + Mark Ship for one order (nhiều đơn song song). */
  useSyncTrackOrder: () => Pick<
    UseMutationResult<ApiResponse<SheinOrderSyncTrackResult>, unknown, string>,
    'mutateAsync'
  >;
  /** Sync trạng thái sàn: 4Seller exact orderNo → PlatformOrderStatus (optional — thiếu thì ẩn menu). */
  useSyncPlatformStatus?: () => Pick<
    UseMutationResult<ApiResponse<OrderPlatformStatusSyncResult>, unknown, string>,
    'mutateAsync'
  >;
  useUpdateOrderMilestones: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderMilestonesRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderSettlement: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderSettlementRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderUspsProcessed: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderUspsProcessedRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Bulk USPS track-scan push to PrintPackShip (charges remote account). */
  usePushUspsScan: () => Pick<
    UseMutationResult<ApiResponse<UspsScanPushResult>, unknown, PushUspsScanRequest>,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderThankCardProcessed: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderThankCardProcessedRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderRefundProcessed: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderRefundProcessedRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderNotes: () => Pick<
    UseMutationResult<ApiResponse<OrderDetail>, unknown, { id: string; data: UpdateOrderNotesRequest }>,
    'mutateAsync' | 'isPending'
  >;
  useUpdateOrderMessage: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: UpdateOrderMessageRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useIssueTagLibrary?: (
    enabled: boolean,
  ) => Pick<UseQueryResult<IssueTag[]>, 'data' | 'isLoading' | 'isFetching'> & {
    refetch?: () => Promise<unknown>;
  };
  useCreateIssueTag?: () => Pick<
    UseMutationResult<ApiResponse<IssueTag>, unknown, { label: string }>,
    'mutateAsync' | 'isPending'
  >;
  useSetOrderIssueTags?: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: SetOrderIssueTagsRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  useSetOrderIssueStatus?: () => Pick<
    UseMutationResult<
      ApiResponse<OrderDetail>,
      unknown,
      { id: string; data: SetOrderIssueStatusRequest }
    >,
    'mutateAsync' | 'isPending'
  >;
  /** Not called directly by the shared page — kept for parity with `@drop/api-client` OrderApi & app-owned CostModal. */
  useUpdateOrderCost?: () => Pick<
    UseMutationResult<ApiResponse<OrderDetail>, unknown, { id: string; data: UpdateOrderCostRequest }>,
    'mutateAsync' | 'isPending'
  >;
  /** Seller ignores `ownerId` (scoped to self server-side). */
  useShippingServices: (
    ownerId: string | null | undefined,
    platform: string | null | undefined,
    enabled: boolean,
  ) => Pick<
    UseQueryResult<ShippingServiceOption[]>,
    'data' | 'isLoading' | 'isFetching' | 'isFetched' | 'isError' | 'error'
  >;
  usePlatformOptions: () => Pick<UseQueryResult<PlatformOption[]>, 'data'>;
  useShopOptions: () => Pick<UseQueryResult<OrdersShopOption[]>, 'data'>;
  /** Admin only — seller filter dropdown. */
  useSellers?: () => Pick<UseQueryResult<OrdersSellerOption[]>, 'data'>;
  /** Admin only — powers listing-rating success/warning thresholds. */
  useAdminSettings?: (options?: {
    enabled?: boolean;
  }) => Pick<UseQueryResult<OrdersAdminSettingOption[]>, 'data'>;
  hasPermission: (permission: string) => boolean;
  defaultPageSize: number;
  /**
   * App-owned "Cập nhật phí" modal slot — admin has extra fulfil account/card/paidBy
   * fields, seller is simpler. Rendered by the shared page when the seller opens it.
   */
  CostModal?: ComponentType<{ order: OrderListItem | null; open: boolean; onClose: () => void }>;
};

const OrdersDepsContext = createContext<OrdersDeps | null>(null);

export const OrdersDepsProvider = ({
  value,
  children,
}: {
  value: OrdersDeps;
  children: ReactNode;
}) => <OrdersDepsContext.Provider value={value}>{children}</OrdersDepsContext.Provider>;

export const useOrdersDeps = (): OrdersDeps => {
  const deps = useContext(OrdersDepsContext);
  if (!deps) {
    throw new Error('useOrdersDeps must be used within an <OrdersDepsProvider>');
  }
  return deps;
};
