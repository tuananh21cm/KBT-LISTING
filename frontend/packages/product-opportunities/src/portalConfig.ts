/** Feature flags that differentiate admin vs seller Product opportunities UI. */
export type ProductOpportunitiesPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` (view gate / menu). */
  viewPermission: string;
  /** Seller filter dropdown (admin only). */
  showSellerFilter: boolean;
  /** Owner name next to shop (admin only). */
  showOwnerColumn: boolean;
  /** Checkbox + bulk delete selected rows. */
  canBulkDelete: boolean;
};

export const ADMIN_PRODUCT_OPPORTUNITIES_CONFIG: ProductOpportunitiesPortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.product_opportunities.view',
  showSellerFilter: true,
  showOwnerColumn: true,
  canBulkDelete: true,
};

export const SELLER_PRODUCT_OPPORTUNITIES_CONFIG: ProductOpportunitiesPortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.product_opportunities.view',
  showSellerFilter: false,
  showOwnerColumn: false,
  canBulkDelete: true,
};
