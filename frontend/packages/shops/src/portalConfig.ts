/** Feature flags that differentiate the admin vs seller Shops UI on top of the shared page. */
export type ShopsPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate manage/edit actions. */
  managePermission: string;
  /** Seller filter dropdown + owner column (admin only). */
  showSellerFilter: boolean;
  showOwnerColumn: boolean;
  /** Create / delete / drag-reorder (admin only). */
  enableCreate: boolean;
  enableDelete: boolean;
  enableReorder: boolean;
  /** Lazy detail view modal (both portals). */
  enableViewModal: boolean;
  /** Receiving-bank select + card detail modal (admin only). */
  enableReceivingBank: boolean;
  /** Manual `newBankNumber` text field when receiving-bank linking is off (seller). */
  enableManualBankNumber: boolean;
  /** Listing column (published/limit từ TikTok publish_limit; admin + seller list). */
  showActiveListingCount: boolean;
  /** Catalog summary column (ẩn để bảng gọn, không scroll ngang). */
  showCatalogColumn: boolean;
  /** Receiving-bank linked tag column (admin). */
  showReceivingBankColumn: boolean;
  /** Owner/platform editable on form; seller locks platform+region. */
  lockPlatformAndRegion: boolean;
  /** Require ownerId on create/edit form (admin). */
  requireOwnerOnForm: boolean;
  /** Default status tab filter. */
  defaultStatusFilter: string;
};

export const ADMIN_SHOPS_CONFIG: ShopsPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.shops.manage',
  showSellerFilter: true,
  showOwnerColumn: true,
  enableCreate: true,
  enableDelete: true,
  // List cố định theo createdAt ASC — kéo SortOrder không còn ảnh hưởng thứ tự hiển thị.
  enableReorder: false,
  enableViewModal: true,
  enableReceivingBank: true,
  enableManualBankNumber: false,
  showActiveListingCount: true,
  showCatalogColumn: false,
  showReceivingBankColumn: false,
  lockPlatformAndRegion: false,
  requireOwnerOnForm: true,
  defaultStatusFilter: 'Active',
};

export const SELLER_SHOPS_CONFIG: ShopsPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.shops.edit',
  showSellerFilter: false,
  showOwnerColumn: false,
  enableCreate: false,
  enableDelete: false,
  enableReorder: false,
  enableViewModal: true,
  enableReceivingBank: false,
  enableManualBankNumber: true,
  showActiveListingCount: true,
  showCatalogColumn: false,
  showReceivingBankColumn: false,
  lockPlatformAndRegion: true,
  requireOwnerOnForm: false,
  defaultStatusFilter: 'Active',
};
