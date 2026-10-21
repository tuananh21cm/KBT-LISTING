export type SupplierListsPortalConfig = {
  portal: 'admin' | 'seller';
  viewPermission: string;
  showSellerFilter: boolean;
  showOwnerName: boolean;
};

export const ADMIN_SUPPLIER_LISTS_CONFIG: SupplierListsPortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.supplier-sellers.view',
  showSellerFilter: true,
  showOwnerName: true,
};

export const SELLER_SUPPLIER_LISTS_CONFIG: SupplierListsPortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.supplier-sellers.view',
  showSellerFilter: false,
  showOwnerName: false,
};
