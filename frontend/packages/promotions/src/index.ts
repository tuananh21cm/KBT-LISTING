export type { PromotionsPortalConfig } from './portalConfig';
export { ADMIN_PROMOTIONS_CONFIG, SELLER_PROMOTIONS_CONFIG } from './portalConfig';
export type {
  PromotionsDeps,
  PromotionsShopOption,
  PromotionsSellerOption,
} from './PromotionsDepsContext';
export { PromotionsDepsProvider, usePromotionsDeps } from './PromotionsDepsContext';
export {
  ProductDiscountPage,
  FlashDealPage,
  type SharedProductDiscountPageProps,
  type SharedFlashDealPageProps,
  type PromotionActivityVariant,
} from './ProductDiscountPage';
