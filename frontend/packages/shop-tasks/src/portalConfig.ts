/** Khác biệt giữa portal admin và seller trên cùng một trang Nhiệm vụ hệ thống. */
export type ShopTasksPortalConfig = {
  portal: 'admin' | 'seller';
  /** Quyền để mở trang. */
  viewPermission: string;
  /** Quyền để sửa cấu hình nhiệm vụ URL (chỉ admin có). */
  managePermission: string;
  /** Quyền Deactivate / Delete listing từ modal Listing cần tối ưu. */
  listingsManagePermission: string;
  /** Lọc theo seller + hiện cột chủ shop (admin xem mọi shop). */
  showOwnerFilter: boolean;
  /** Tab "Cấu hình" CRUD nhiệm vụ truy cập URL. */
  enableDefinitionCrud: boolean;
  /** Prefix route để deep-link sang Listings — hai portal trùng nhau nhưng giữ tường minh. */
  listingsAnalyticsPath: string;
  listingsDraftPath: string;
};

export const ADMIN_SHOP_TASKS_CONFIG: ShopTasksPortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.shop_tasks.view',
  managePermission: 'admin.shop_tasks.manage',
  listingsManagePermission: 'admin.listings.manage',
  showOwnerFilter: true,
  enableDefinitionCrud: true,
  listingsAnalyticsPath: '/listings/analytics',
  listingsDraftPath: '/listings/draft',
};

export const SELLER_SHOP_TASKS_CONFIG: ShopTasksPortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.shop_tasks.view',
  managePermission: 'admin.shop_tasks.manage',
  listingsManagePermission: 'seller.products.manage',
  showOwnerFilter: false,
  enableDefinitionCrud: false,
  listingsAnalyticsPath: '/listings/analytics',
  listingsDraftPath: '/listings/draft',
};
