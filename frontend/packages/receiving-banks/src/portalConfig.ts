/** Feature flags that differentiate admin vs seller Receiving Bank Transactions UI. */
export type ReceivingBanksPortalConfig = {
  portal: 'admin' | 'seller';
  /** Permission checked via `deps.hasPermission` to gate create/edit/delete. */
  managePermission: string;
  /** Manual ledger create/edit/delete (seller: Pending Credit only). */
  enableManage: boolean;
  /** Admin can approve/reject Pending rows into stats. */
  enableConfirm: boolean;
  /** Show seller/owner column (admin). */
  showSellerColumn: boolean;
};

export const ADMIN_RECEIVING_BANKS_TX_CONFIG: ReceivingBanksPortalConfig = {
  portal: 'admin',
  managePermission: 'admin.shops.manage',
  enableManage: true,
  enableConfirm: true,
  showSellerColumn: true,
};

export const SELLER_RECEIVING_BANKS_TX_CONFIG: ReceivingBanksPortalConfig = {
  portal: 'seller',
  managePermission: 'seller.shops.view',
  enableManage: true,
  enableConfirm: false,
  showSellerColumn: false,
};
