import { ShopTasksDailyPage as SharedDailyPage } from '@drop/shop-tasks';
import { AdminShopTasksShell, adminShopTasksConfig } from '../AdminShopTasksShell';

/** Admin `/shop-tasks/daily` — Tương tác. */
export const ShopTasksDailyPage = () => (
  <AdminShopTasksShell>
    <SharedDailyPage config={adminShopTasksConfig} />
  </AdminShopTasksShell>
);
