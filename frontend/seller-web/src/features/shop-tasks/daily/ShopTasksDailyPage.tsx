import { ShopTasksDailyPage as SharedDailyPage } from '@drop/shop-tasks';
import { SellerShopTasksShell, sellerShopTasksConfig } from '../SellerShopTasksShell';

/** Seller `/shop-tasks/daily` — Tương tác. */
export const ShopTasksDailyPage = () => (
  <SellerShopTasksShell>
    <SharedDailyPage config={sellerShopTasksConfig} />
  </SellerShopTasksShell>
);
