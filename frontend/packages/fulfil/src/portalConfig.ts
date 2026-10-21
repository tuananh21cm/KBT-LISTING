/** Feature flags that differentiate admin vs seller Fulfil Transactions UI. */
export type FulfilPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate create/edit/delete. */
  managePermission: string;
  /** Manual ledger create/edit/delete. */
  enableManage: boolean;
  /** Admin can bulk-delete selected rows (incl. CostUpdate). */
  enableBulkDelete: boolean;
  /** Allow edit/delete of CostUpdate rows (admin). Seller: Manual only. */
  enableCostUpdateMutate: boolean;
  /** Show seller filter + column (admin). */
  showSellerColumn: boolean;
  /** Paid-by user dropdown on create/edit form + bộ lọc người fulfil (admin). */
  showPaidByFilter: boolean;
  /**
   * Quyền cần để tải danh sách user cho dropdown người fulfil. Tách khỏi <see cref="managePermission"/>
   * vì admin chỉ có quyền xem sổ cái vẫn phải lọc được theo người fulfil.
   */
  userListPermission: string;
  /**
   * Chuyển dòng sổ cái sang đơn khác (admin). Với dòng Cập nhật phí đây là gắn lại **lần mua NCC**,
   * nên cần thêm quyền đơn hàng ở <see cref="reassignPermission"/>, không chỉ quyền sổ cái.
   */
  enableReassignOrder: boolean;
  /** Quyền bổ sung cho thao tác chuyển đơn (gắn lại lần mua NCC). */
  reassignPermission: string;
};

export const ADMIN_FULFIL_TX_CONFIG: FulfilPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.fulfil.manage',
  enableManage: true,
  enableBulkDelete: true,
  enableCostUpdateMutate: true,
  showSellerColumn: true,
  showPaidByFilter: true,
  userListPermission: 'admin.users.view',
  enableReassignOrder: true,
  reassignPermission: 'admin.orders.manage',
};

export const SELLER_FULFIL_TX_CONFIG: FulfilPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.orders.manage',
  enableManage: true,
  enableBulkDelete: false,
  enableCostUpdateMutate: false,
  showSellerColumn: false,
  showPaidByFilter: false,
  userListPermission: 'admin.users.view',
  enableReassignOrder: false,
  reassignPermission: 'admin.orders.manage',
};
