/** Feature flags that differentiate the admin vs seller Orders UI on top of the shared page. */
export type OrdersPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate all manage actions. */
  managePermission: string;
  /** Seller filter dropdown + owner-scoped shop filtering (admin only). */
  showSellerFilter: boolean;
  /** Show seller/owner name in order cards & detail (admin only — seller already scoped to self). */
  showOwnerName: boolean;
  /** "Xóa đơn" (single + by-filter) actions (admin only). */
  enableDelete: boolean;
  /** Read listing-rating success/warning thresholds from admin settings instead of defaults. */
  useAdminSettingsForRating: boolean;
};

export const ADMIN_ORDERS_CONFIG: OrdersPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.orders.manage',
  showSellerFilter: true,
  showOwnerName: true,
  enableDelete: true,
  useAdminSettingsForRating: true,
};

export const SELLER_ORDERS_CONFIG: OrdersPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.orders.manage',
  showSellerFilter: false,
  showOwnerName: false,
  enableDelete: false,
  useAdminSettingsForRating: false,
};
