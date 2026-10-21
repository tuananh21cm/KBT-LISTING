import { ShopTasksOptimizePage as SharedOptimizePage } from '@drop/shop-tasks';
import { AdminShopTasksShell, adminShopTasksConfig } from '../AdminShopTasksShell';

/** Admin `/shop-tasks/optimize` — Tối ưu listing. */
export const ShopTasksOptimizePage = () => (
  <AdminShopTasksShell>
    <SharedOptimizePage config={adminShopTasksConfig} />
  </AdminShopTasksShell>
);
