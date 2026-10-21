import { ShopTasksSettingsPage as SharedSettingsPage } from '@drop/shop-tasks';
import { SellerShopTasksShell, sellerShopTasksConfig } from '../SellerShopTasksShell';

/**
 * Seller `/shop-tasks/settings` — shared page tự redirect về daily
 * (seller không bật enableDefinitionCrud).
 */
export const ShopTasksSettingsPage = () => (
  <SellerShopTasksShell>
    <SharedSettingsPage config={sellerShopTasksConfig} />
  </SellerShopTasksShell>
);
