import {
  SELLER_ORDERS_CONFIG,
  OrdersDepsProvider,
  OrdersPage as SharedOrdersPage,
  type OrdersDeps,
} from '@drop/orders';
import { useAuthStore } from '../../stores/authStore';
import {
  ORDERS_DEFAULT_PAGE_SIZE,
  useOrderDetail,
  useOrders,
  useOrderStatusCounts,
  useMarkShipOrder,
  useSyncTrackOrder,
  useSyncPlatformStatus,
  useShippingServices as useSellerShippingServices,
  useSyncOrders,
  useOrderSyncJob,
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
} from '../../hooks/useOrders';
import { usePlatformOptions } from '../../hooks/usePlatforms';
import { useShops } from '../../hooks/useShops';
import { UpdateCostModal } from './UpdateCostModal';

/** Normalize admin-style (ownerId, platform, enabled) → seller shipping-services hook. */
function useShippingServices(
  _ownerId: string | null | undefined,
  platform: string | null | undefined,
  enabled: boolean,
) {
  return useSellerShippingServices(platform, enabled);
}

function useSellerOrdersDeps(): OrdersDeps {
  const hasPermission = useAuthStore((s) => s.hasPermission);

  return {
    useOrdersList: useOrders,
    useOrderStatusCounts,
    useOrderDetail,
    useSyncOrders,
    useOrderSyncJob,
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
    useShopOptions: useShops,
    hasPermission,
    defaultPageSize: ORDERS_DEFAULT_PAGE_SIZE,
    CostModal: UpdateCostModal,
  };
}

/** Seller Orders shell — wires portal deps into shared `@drop/orders` page. */
export const OrdersPage = () => {
  const deps = useSellerOrdersDeps();
  return (
    <OrdersDepsProvider value={deps}>
      <SharedOrdersPage config={SELLER_ORDERS_CONFIG} />
    </OrdersDepsProvider>
  );
};
