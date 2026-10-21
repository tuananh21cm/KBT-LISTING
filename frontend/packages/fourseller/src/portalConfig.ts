/** Feature flags that differentiate the admin vs seller 4Seller connect UI. */
export type FourSellerPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate connect/sync. */
  managePermission: string;
  /** Seller filter dropdown (admin only). */
  showSellerFilter: boolean;
  /** Owner/seller column in table (admin only). */
  showOwnerColumn: boolean;
  /** Connected shops show "Cập nhật" instead of a static OK badge (seller). */
  allowReconnectWhenConnected: boolean;
  /** Hint about 4Seller cookie in profile (seller). */
  showCookieHint: boolean;
};

export const ADMIN_FOURSELLER_CONFIG: FourSellerPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.shops.manage',
  showSellerFilter: true,
  showOwnerColumn: true,
  allowReconnectWhenConnected: false,
  showCookieHint: false,
};

export const SELLER_FOURSELLER_CONFIG: FourSellerPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.shops.edit',
  showSellerFilter: false,
  showOwnerColumn: false,
  allowReconnectWhenConnected: true,
  showCookieHint: true,
};
