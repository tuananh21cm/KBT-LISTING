import { ShopTasksOptimizePage as SharedOptimizePage } from '@drop/shop-tasks';
import { SellerShopTasksShell, sellerShopTasksConfig } from '../SellerShopTasksShell';

/** Seller `/shop-tasks/optimize` — Tối ưu listing. */
export const ShopTasksOptimizePage = () => (
  <SellerShopTasksShell>
    <SharedOptimizePage config={sellerShopTasksConfig} />
  </SellerShopTasksShell>
);
