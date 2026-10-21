/** Feature flags that differentiate the admin vs seller Listings UI on top of the shared page. */
export type ListingsPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate all manage actions. */
  managePermission: string;
  /** Permission required to open read-only pages (Listing analytics). */
  viewPermission: string;
  /** Seller filter dropdown + owner-scoped shop filtering (admin only). */
  showSellerFilter: boolean;
  /** Show seller/owner name in listing cards (admin only — seller already scoped to self). */
  showOwnerName: boolean;
  /** Read listing-rating success/warning thresholds from admin settings instead of defaults. */
  useAdminSettingsForRating: boolean;
};

export const ADMIN_LISTINGS_CONFIG: ListingsPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.listings.manage',
  viewPermission: 'admin.listings.view',
  showSellerFilter: true,
  showOwnerName: true,
  useAdminSettingsForRating: true,
};

export const SELLER_LISTINGS_CONFIG: ListingsPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.products.manage',
  viewPermission: 'seller.products.view',
  showSellerFilter: false,
  showOwnerName: false,
  useAdminSettingsForRating: false,
};
