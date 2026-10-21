export type DashboardPortalConfig = {
  portal: 'admin' | 'seller';
  /** Admin can filter/compare across sellers; seller only sees their own data. */
  showSellerFilter: boolean;
  /** Admin-only sections: seller ranking, seller × shop ownership, revenue share pie, etc. */
  showSellerPerformance: boolean;
};

export const ADMIN_DASHBOARD_CONFIG: DashboardPortalConfig = {
  portal: 'admin',
  showSellerFilter: true,
  showSellerPerformance: true,
};

export const SELLER_DASHBOARD_CONFIG: DashboardPortalConfig = {
  portal: 'seller',
  showSellerFilter: false,
  showSellerPerformance: false,
};
