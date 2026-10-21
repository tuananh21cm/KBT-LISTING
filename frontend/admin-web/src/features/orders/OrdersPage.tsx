import {
  ADMIN_ORDERS_CONFIG,
  OrdersDepsProvider,
  OrdersPage as SharedOrdersPage,
  type OrdersDeps,
} from '@drop/orders';
import { useAuthStore } from '../../stores/authStore';
import {
  ADMIN_ORDERS_DEFAULT_PAGE_SIZE,
  useAdminOrderDetail,
  useAdminOrders,
  useAdminOrderStatusCounts,
  useDeleteAdminOrder,
  useDeleteAdminOrdersByFilter,
  useMarkShipOrder,
  useSyncTrackOrder,
  useSyncPlatformStatus,
  useShippingServices,
  useSyncAdminAfterSales,
  useSyncAdminOrders,
  useAdminOrderSyncJob,
  useUpdateOrderCost,
  useUpdateOrderMilestones,
  useUpdateOrderNotes,
  useUpdateOrderMessage,
  useIssueTagLibrary,
  useCreateIssueTag,
  useSetOrderIssueTags,
  useSetOrderIssueStatus,
  useUpdateOrderSettlement,
  useUpdateOrderUspsProcessed,
  usePushUspsScan,
  useUpdateOrderThankCardProcessed,
  useUpdateOrderRefundProcessed,
} from '../../hooks/useAdminOrders';
import { usePlatformOptions } from '../../hooks/useAdminPlatforms';
import { useAdminShopOptions } from '../../hooks/useAdminShops';
import { useSellers } from '../../hooks/useSellers';
import { useAdminSettings } from '../../hooks/useAdminSettings';
import { UpdateCostModal } from './UpdateCostModal';

function useAdminOrdersDeps(): OrdersDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useOrdersList: useAdminOrders,
    useOrderStatusCounts: useAdminOrderStatusCounts,
    useOrderDetail: useAdminOrderDetail,
    useSyncOrders: useSyncAdminOrders,
    useOrderSyncJob: useAdminOrderSyncJob,
    useSyncAfterSales: useSyncAdminAfterSales,
    useDeleteOrder: useDeleteAdminOrder,
    useDeleteOrdersByFilter: useDeleteAdminOrdersByFilter,
    useMarkShipOrder,
    useSyncTrackOrder,
    useSyncPlatformStatus,
    useUpdateOrderMilestones,
    useUpdateOrderSettlement,
    useUpdateOrderUspsProcessed,
    usePushUspsScan,
    useUpdateOrderThankCardProcessed,
    useUpdateOrderRefundProcessed,
    useUpdateOrderNotes,
    useUpdateOrderMessage,
    useIssueTagLibrary,
    useCreateIssueTag,
    useSetOrderIssueTags,
    useSetOrderIssueStatus,
    useUpdateOrderCost,
    useShippingServices,
    usePlatformOptions,
    useShopOptions: useAdminShopOptions,
    useSellers,
    useAdminSettings,
    hasPermission,
    defaultPageSize: ADMIN_ORDERS_DEFAULT_PAGE_SIZE,
    CostModal: UpdateCostModal,
  };
}

/** Admin Orders shell — wires portal deps into shared `@drop/orders` page. */
export const OrdersPage = () => {
  const deps = useAdminOrdersDeps();
  return (
    <OrdersDepsProvider value={deps}>
      <SharedOrdersPage config={ADMIN_ORDERS_CONFIG} />
    </OrdersDepsProvider>
  );
};
