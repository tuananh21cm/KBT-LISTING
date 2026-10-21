import { ShopTasksSettingsPage as SharedSettingsPage } from '@drop/shop-tasks';
import { AdminShopTasksShell, adminShopTasksConfig } from '../AdminShopTasksShell';

/** Admin `/shop-tasks/settings` — Thiết lập. */
export const ShopTasksSettingsPage = () => (
  <AdminShopTasksShell>
    <SharedSettingsPage config={adminShopTasksConfig} />
  </AdminShopTasksShell>
);
