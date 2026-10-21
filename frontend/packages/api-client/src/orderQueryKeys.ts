/**
 * Shared React Query keys for orders, plus the one helper every mutation should call.
 *
 * Tab counts moved off the list response onto `GET …/orders/status-counts`, so anything that
 * changes an order now has to invalidate two caches instead of one. Going through
 * {@link invalidateOrdersCaches} keeps that pairing in a single place — a mutation that only
 * invalidated the list would leave stale badge numbers on screen.
 */

/** Root query keys for order data. */
export const ORDER_QUERY_KEYS = {
  adminList: 'adminOrders',
  adminStatusCounts: 'adminOrderStatusCounts',
  sellerList: 'sellerOrders',
  sellerStatusCounts: 'sellerOrderStatusCounts',
} as const;

/** Which app's order caches to drop. */
export type OrderCacheScope = 'admin' | 'seller' | 'both';

/**
 * Minimal shape of a React Query client — declared structurally so this package does not
 * need a `@tanstack/react-query` dependency.
 */
export interface OrderCacheInvalidator {
  invalidateQueries: (filters: { queryKey: readonly unknown[] }) => unknown;
}

/** Invalidates the order list and its tab counts together. */
export function invalidateOrdersCaches(
  queryClient: OrderCacheInvalidator,
  scope: OrderCacheScope = 'both',
): void {
  if (scope === 'admin' || scope === 'both') {
    void queryClient.invalidateQueries({ queryKey: [ORDER_QUERY_KEYS.adminList] });
    void queryClient.invalidateQueries({ queryKey: [ORDER_QUERY_KEYS.adminStatusCounts] });
  }
  if (scope === 'seller' || scope === 'both') {
    void queryClient.invalidateQueries({ queryKey: [ORDER_QUERY_KEYS.sellerList] });
    void queryClient.invalidateQueries({ queryKey: [ORDER_QUERY_KEYS.sellerStatusCounts] });
  }
}
