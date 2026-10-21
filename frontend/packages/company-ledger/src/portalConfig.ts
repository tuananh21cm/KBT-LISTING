/** Feature flags that differentiate admin vs seller company ledger UI. */
export type CompanyLedgerPortalConfig = {
  portal: 'admin' | 'seller';
  viewPermission: string;
  managePermission: string;
  enableManage: boolean;
  showSellerFilter: boolean;
  showSellerField: boolean;
};

export const ADMIN_COMPANY_LEDGER_CONFIG: CompanyLedgerPortalConfig = {
  portal: 'admin',
  viewPermission: 'admin.company-ledger.view',
  managePermission: 'admin.company-ledger.manage',
  enableManage: true,
  showSellerFilter: true,
  showSellerField: true,
};

export const SELLER_COMPANY_LEDGER_CONFIG: CompanyLedgerPortalConfig = {
  portal: 'seller',
  viewPermission: 'seller.company-ledger.view',
  managePermission: 'seller.company-ledger.view',
  enableManage: false,
  showSellerFilter: false,
  showSellerField: false,
};
