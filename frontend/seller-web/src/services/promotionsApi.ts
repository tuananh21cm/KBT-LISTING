import { createPromotionsApi } from '@drop/api-client';
import api from './api';

export const promotionsApi = createPromotionsApi(api, {
  basePath: '/promotions/product-discounts',
});
