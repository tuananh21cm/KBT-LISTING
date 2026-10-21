import { createFlashDealsApi } from '@drop/api-client';
import api from './api';

export const adminFlashDealsApi = createFlashDealsApi(api, {
  basePath: '/admin/promotions/flash-deals',
});
