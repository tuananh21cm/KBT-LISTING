import { createFlashDealsApi } from '@drop/api-client';
import api from './api';

export const flashDealsApi = createFlashDealsApi(api, {
  basePath: '/promotions/flash-deals',
});
