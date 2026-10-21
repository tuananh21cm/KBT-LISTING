/** Feature flags that differentiate admin vs seller Shop Finance UI. */
export type ShopFinancePortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` (view gate). */
  viewPermission: string;
  /** Seller filter dropdown (admin only). */
  showSellerFilter: boolean;
  /** Inline seller name on the shop profile line (admin only). */
  showOwnerColumn: boolean;
  /** KPI + column for Pending ledger (admin). */
  showPendingConfirmation: boolean;
  /** KPI for shops missing a receiving-bank card (admin). */
  showShopsWithoutBank: boolean;
};

export const ADMIN_SHOP_FINANCE_CONFIG: ShopFinancePortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.shops.view',
  showSellerFilter: true,
  showOwnerColumn: true,
  showPendingConfirmation: true,
  showShopsWithoutBank: true,
};

export const SELLER_SHOP_FINANCE_CONFIG: ShopFinancePortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.shops.view',
  showSellerFilter: false,
  showOwnerColumn: false,
  showPendingConfirmation: false,
  showShopsWithoutBank: false,
};
