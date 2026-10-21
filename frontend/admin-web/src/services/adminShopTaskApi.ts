import api from './api';
import { createShopTaskApi } from '@drop/api-client';

export const adminShopTaskApi = createShopTaskApi(api, { basePath: '/admin/shop-tasks' });
