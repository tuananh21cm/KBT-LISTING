/** Feature flags that differentiate admin vs seller Product Discount UI. */
export type PromotionsPortalConfig = {
  portal: 'admin' | 'seller';
  viewPermission: string;
  managePermission: string;
  /** Seller filter dropdown (admin only). */
  showSellerFilter: boolean;
  /** Owner name column (admin only). */
  showOwnerColumn: boolean;
};

export const ADMIN_PROMOTIONS_CONFIG: PromotionsPortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.promotions.view',
  managePermission: 'admin.promotions.manage',
  showSellerFilter: true,
  showOwnerColumn: true,
};

export const SELLER_PROMOTIONS_CONFIG: PromotionsPortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.promotions.view',
  managePermission: 'seller.promotions.manage',
  showSellerFilter: false,
  showOwnerColumn: false,
};
