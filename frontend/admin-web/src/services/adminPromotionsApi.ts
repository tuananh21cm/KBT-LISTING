import { createPromotionsApi } from '@drop/api-client';
import api from './api';

export const adminPromotionsApi = createPromotionsApi(api, {
  basePath: '/admin/promotions/product-discounts',
});
